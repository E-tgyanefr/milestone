# t115：v3 M2 设计细目（资产/序列化——签名级）

> eng-design-vis · 依据 t107 规格（资产/序列化：场景/资产图、.mscene/.msasset 文本+二进制布局+校验、项目目录路径管理、懒加载边界）。
> 前置：M0（Core+生命周期 R1 修复闭环）/M1（Platform+软渲+输入+音频+parity——全部通过）；M2=Core 资产域（新 include/milestone/core/assets/* + src/core/assets/*——零第三方红线继续；JSON/文本=手写 msjson 单头）。
> 交付后：eng-coder-vis 按本细目实现（captain 建 t116 依赖本设计）。

---

## 0. 范围与决策锚点

| 决策 | 选项 | 本细目采用 |
|---|---|---|
| 序列化格式 | 文本 JSON（人类可读/diff 友好——默认）+ 二进制（紧凑/热路径可选） | **文本=.mscene/.msasset（默认）**；二进制=同 schema 紧凑变异（§2.2；M2=骨架+NOT_SUPPORTED——决策 M2-a） |
| 校验 | FNV-1a 64（与 parity 同实现） | 文本=尾校验行；二进制=尾 8B |
| 纹理 | BMP 自写解码（零第三方）；PNG/WebP=后置（stb_image 单头白名单=后续与 ImGui 同批评估） | **M2 仅 BMP**（M2-c） |
| 谱面 .mil | 属域库 Rhythm.*（M4）；M2=类型表注册钩子 | 标注 |
| 反射 | ReflectionRegistry（宏注册表——序列化+Inspector 共用） | M2 包含（场景组件字段序列化需要） |
| 懒加载 | 按需 Load+引用计数显式 Unload；异步=P2 标注 | 同步默认（§4） |

---

## 1. 资产图与路径管理

### 1.1 路径模型

- **项目根**：ProjectRoot（绝对路径；默认=exe 目录/Assets；可 SetProjectRoot）。
- **资产虚拟路径**：Assets/<相对路径>（相对 ProjectRoot；统一 / 分隔符）。
- **解析**：Resolve(assetPath)→绝对路径；../ 穿越→拒绝（false+日志）；扩展名→类型映射。

### 1.2 资产类型表

| 类型 | 扩展 | 内容 | M2 |
|---|---|---|---|
| SceneAsset | .mscene | JSON 场景（§2） | ✅ |
| PrefabAsset | .msprefab | JSON 预制体模板（§2.3） | ✅（骨架） |
| TextureAsset | .bmp | BMP 解码→像素 | ✅ |
| AudioAsset | .wav/.mp3 | 路径（WinMM 打开不做解码） | ✅（路径注册） |
| Meta | .msmeta | 侧车元数据（guid+导入设置 JSON） | ✅ |
| DomainType(Chart) | .mil | 域库资产（M4 注册骨架） | ⏳（钩子） |

### 1.3 签名级（asset_database 等）

~~~cpp
namespace Milestone::Core {
struct AssetId { std::string guid; std::string path; };
struct AssetMeta { std::string guid; std::string type; std::string importJson; };

class AssetDatabase {
public:
  static AssetDatabase& Instance();                                   // 便捷单例（宿主也可自建）
  bool SetProjectRoot(const std::string& abs); std::string ProjectRoot() const;
  bool Resolve(const std::string& assetPath, std::string& abs) const; // ../ 拒绝
  bool Exists(const std::string& assetPath) const;
  AssetId GetMeta(const std::string& assetPath) const;                // 无 meta=按内容生成（懒）
  std::vector<std::string> List(const std::string& dir = "Assets/") const;   // 递归
  template<class TAsset> TAsset* Load(const std::string& assetPath);  // 按需+缓存（懒句柄）
  bool Save(const std::string& assetPath, const void* data, size_t bytes, const char* type);
  bool Import(const std::string& srcAbs, const std::string& dest, const char* type);  // 导入+meta
  void Unload(const std::string& assetPath);                          // 引用计数
private: /* path/guid map, refcount, cache */
};

// .msasset 载荷（文本/二进制共用 schema）
// 文本: magic line "MSASSET1" + type line + guid line + payload(JSON) + FNV-1a 64 尾行
// 二进制: magic(8B:MSASSET1)+ver(4)+type(16)+guidLen(4)+guid+payloadLen(8)+payload+FNV(8)
// guid= FNV(path + 内容首 64B)（确定性——重复导入同 guid）
}
~~~

---

## 2. 场景序列化（.mscene JSON 布局）

### 2.1 布局（语义=Unity YAML 场景等价）

~~~json
{
  "mscene": "1", "guid": "a1b2c3d4", "name": "MyScene",
  "roots": [
    { "name": "Player", "active": true,
      "transform": { "pos": [0,0,0], "rot": [0,0,0], "scale": [1,1,1] },   // 内建 Transform 承载（规范④；不重复为组件）
      "parent": null,
      "components": [
        { "type": "MyGame.RulesetAnchor", "fields": { "keys": 4, "profileName": "OsuMania8", "speed": 1.0 } }
      ] },
    { "name": "Lane0", "parent": "Player", "components": [ { "type": "X", "fields": { } } ] }
  ]
}
// 注：本 payload 为 .msasset 文本容器内 JSON（容器头 MSASSET1+type+guid → 本 JSON → 容器尾 FNV-1a 64——
//     校验以容器 FNV 为准（不重复 in-JSON hash 字段——与 eng-coder-vis 确认②一致）
~~~

**规范**：①平铺对象表+parent 引用（父 name 路径——重建循环检测：成环→错误码）②组件类型=完全限定名（ReflectionRegistry 键）③字段=JSON 值（number/string/bool/array[3]/嵌套对象）④内建 Transform 由 transform 块承载（不重复为组件）。

### 2.2 二进制同 schema（.msbin——骨架）

- §1.3 AssetHeader 二进制布局；payload=定序对象/组件/字段表（同 JSON 树遍历序）；尾 FNV。
- **M2 决策（M2-a）**：实现=文本全功能+二进制入口返回 NOT_SUPPORTED（诚实）；同一序列化器开关=P1 后补全。

### 2.3 PrefabAsset（.msprefab——骨架）

- 单根模板（场景格式子集）+实例化：

~~~cpp
class PrefabAsset { public: std::string guid; GameObject templateRoot; };
// AssetDatabase:
GameObject* InstantiatePrefab(const std::string& assetPath, Scene* targetScene, const Vec3& at = {});
~~~
M3（编辑器）完整挂接；M2=模板序列化 round-trip（断言）。

---

## 3. ReflectionRegistry（反射注册表）

~~~cpp
namespace Milestone::Core {
struct FieldDesc { const char* name; enum class Kind { Int, Float, Bool, String, Enum, Vec3, Struct } kind; };
// 组件类声明尾: MILESTONE_REFLECT(MyRuleset, keys, profileName, speed)
class ReflectionRegistry {
public:
  template<class T> static void Register(const char* typeName);
  static const std::vector<FieldDesc>& Fields(const char* typeName);
  static bool GetField(const char* typeName, void* obj, const char* field, JsonValue& out);
  static bool SetField(const char* typeName, void* obj, const char* field, const JsonValue& in);
  static const char* TypeNameOf(const std::type_info&);
};
}
~~~
**实现**：模板特化描述符（宏生成 fields+get/set）；支持 int/double/bool/std::string/enum(int 保底)/Vec3(double[3])/嵌套 struct（递归）；**用途**=SceneAsset 字段读写+（M3）Inspector——同一注册表。
**扩展（M2 确认采纳）**：Register<T> 内 if constexpr (std::is_base_of_v<Component,T>) → 同时注册**组件工厂**（Create→new T）——场景反序列化按类型名实例化组件用（eng-coder-vis 提议采纳；M3 Inspector 亦受益）。

---

## 4. 懒加载边界

| 边界 | 规则 |
|---|---|
| 按需加载 | Load<T> 首次调用解析/解码+缓存；**场景保存不触发子资产加载**（引用=路径+guid 元数据） |
| 引用计数 | Load +1；Unload -1；0=释放（位图/解析数据） |
| 场景依赖图 | SceneAsset 引用（prefab/audio/texture）=AssetRef{path,guid} 懒解析（播放/渲染时按需） |
| 异步 | 默认同步；异步=P2（标注不阻塞） |
| 卸载 | 显式 Unload+场景切换=宿主约定 Unload 旧场景资产 |

---

## 5. 验收清单（M2 全绿）

| # | 项 | 判据 |
|---|---|---|
| 1 | 路径管理 | Resolve/List/防穿越（../ 拒绝断言）/SetProjectRoot 幂等 |
| 2 | 序列化 round-trip | 多根/父子/3 组件（含字段）→Save→Load→树/Transform/字段**双等**（Reflection 值等价） |
| 3 | 校验 | 篡改 payload→加载 FAIL（错误码+日志）；FNV 读写一致 |
| 4 | Reflection | int/float/string/enum/Vec3 字段 Get/Set round-trip；未知类型=错误码 |
| 5 | Prefab 骨架 | 模板序列化 round-trip |
| 6 | 懒加载 | 计数断言（场景保存不加载依赖；Load 一次；Unload→重载再+1） |
| 7 | 纹理 BMP | 已知 BMP 像素抽样断言+宽高/步进 |
| 8 | 类型表 | Scene/Prefab/Texture/Audio/Meta 注册+List 过滤 |
| 9 | 零第三方/命名 | assets 域零第三方（msjson 手写+BMP 手写）；namespace Milestone::Core::Assets |
| 10 | 双链 | GCC+（MSVC/Clang 待 SDK——README 已注）0 警告 0 错误 |

**完整度声明**：M2=文本序列化全功能+二进制骨架（M2-a）+BMP 纹理（M2-c）；异步=P2；.mil=域库钩子（M4）。

---

## 6. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| M2-a | 二进制布局 | 文本全功能+二进制 NOT_SUPPORTED 骨架；P1 补全（同 schema） |
| M2-b | 缺失 meta | 按内容 FNV 自动 guid（确定性） |
| M2-c | 纹理格式 | 仅 BMP（零第三方）；PNG=后置（stb_image 单头——后续与 ImGui 同批评估或独立） |
| M2-d | 场景对象引用 | 平铺+父引用（非嵌套）+环检测 |
| M2-e | 反射范围 | 标量+Vec3+嵌套 struct（enum=int 保底）——序列化+Inspector 共用 |

---

*t115 完成*（资产图/路径/.mscene JSON+二进制 schema/ReflectionRegistry/懒加载边界/验收 10 项/M2-a..e；交付 eng-coder-vis 实现（t116））

> **实现后评审（t116）：通过**——10/10 验收（core 17/17+platform 20/20 无回归+ctest 2/2+renderhash STABLE+双源 59 文件 MD5 一致）；**7 偏离全采纳**（①Save→AssetResult 14 错误码（验收 #3 需要）②rot 四元数 [w,x,y,z] 忠实往返（欧拉歧义——文档化）③组件工厂入 Register<T>（协约）④MILESTONE_REFLECT ≥1 字段（0 字段编译错误——文档化，P1 改善）⑤场景名唯一约定（DuplicateName 码）⑥Prefab=JSON 重解析（协约）⑦二进制骨架 NotSupported（M2-a））。
