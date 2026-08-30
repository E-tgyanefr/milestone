# t117：v3 M2.5 绑定层设计（C ABI ms_bind + C# 包装 + cookbook）——签名级

> eng-design-vis · 依据 V3-9/V3-10（V3-B=C ABI+P/Invoke 适配；C++ 核+C# 开发层=Unity 双层）+ 当前 v3 基线（M0 Core/M1 Platform/M2 Assets+ReflectionRegistry——**绑定面与现有 API 对齐**）。
> 输出：ms_bind.h 稳定 ABI（引擎生命周期/场景/组件/变换/资产句柄）、错误码、内存所有权、线程模型、C# 包装语义（八回调虚方法→C 回调）、cookbook。只做设计。
> 前置补建（M2.5 需要）：**Milestone::App::Engine**（小门面——M0-M2 无顶层宿主：持有 Scene+LifecycleDriver+Time+Event+AssetDatabase+可选 Platform（Window/Renderer/Input/Audio）+Run 循环；M3 编辑器复用同一门面）。

---

## 0. 总则

1. **单一入口线程**：所有 ms_* 调用=创建引擎的同一线程（主线程）调用；C# 回调在 ms_engine_tick 内同步回调（同一线程）——线程模型=**单线程（主线程）**（文档明示；跨线程=预留给未来（无效））。
2. **内存所有权**：句柄=C++ 对象指针（ms_destroy 释放；C# 不 free）；delegate GCHandle= C# 侧拥有（注册后释放=组件销毁时 C# 布局释放——文档）；字符串 UTF-8 入参=C# Marshal 拷入/调用方拥有；出参缓冲=调用方提供（尺寸参数）。
3. **错误码**：MS_BIND_OK=0；>0=引擎错误码（映射 AssetErrorCode/核心码——见 §1.2）；<0=纯绑定层错误（句柄空/类型错误）。

---

## 1. ms_bind.h（稳定 C ABI）

### 1.1 句柄与常量

~~~c
#define MS_API __declspec(dllexport)          // Windows DLL；Linux= visibility(default)（宏切换）
typedef struct ms_engine ms_engine;           // 不透明句柄（Milestone::App::Engine*）
typedef struct ms_scene ms_scene;             // Milestone::Core::Scene*
typedef struct ms_go ms_go;                   // Milestone::Core::GameObject*
typedef struct ms_asset ms_asset;             // 资产句柄（AssetDatabase 项指针）
typedef long ms_id;                           // InstanceId（C++ uint64 → C# long）
typedef uint64_t ms_hash;                     // FNV-1a 64
~~~

### 1.2 错误码

~~~c
enum {
  MS_BIND_OK = 0,
  // 绑定层（<0）
  MS_ERR_NULL_HANDLE = -1,     MS_ERR_BAD_TYPE = -2,   MS_ERR_BAD_ARG = -3,
  // 引擎（>0，映射 Core/Asset 码）
  MS_ERR_INVALID_OP = 1,       // 操作非法（如未初始化）
  MS_ERR_NOT_FOUND = 2,        // 资产/名称未找到
  MS_ERR_IO = 3,               // 文件 IO
  MS_ERR_HASH_MISMATCH = 4,    // 校验失败（M2 HashMismatch）
  MS_ERR_TRAVERSAL_DENIED = 5, // 路径穿越（M2）
  MS_ERR_DUPLICATE_NAME = 6,   // 场景对象重名（M2）
  MS_ERR_UNKNOWN_FIELD = 7,    // 反射未知字段（M2）
  MS_ERR_NOT_SUPPORTED = 8,    // 二进制资产骨架（M2-a）
  MS_ERR_THREAD = 9            // 非主线程调用（防御——文档性）
};
~~~
（引擎内部 AssetResult→int 映射表在适配层维护。）

### 1.3 ABI 签名（分组）

~~~c
/* ---- 引擎生命周期 ---- */
MS_API ms_engine* ms_engine_create(const char* title, int width, int height, int* err);
MS_API void       ms_engine_destroy(ms_engine* e);
MS_API int        ms_engine_tick(ms_engine* e, double dt);            // 触发生命周期调度+事件 flush；返回 err
MS_API int        ms_engine_render(ms_engine* e, void* rendererCtx); // 渲染委托（C# 侧可挂 OnRender）
MS_API int        ms_engine_pump(ms_engine* e);                      // 泵窗口消息（输入→Input 桥）；无窗口=ok
MS_API int        ms_engine_resize(ms_engine* e, int w, int h);
MS_API double     ms_engine_last_frame_ms(ms_engine* e);
MS_API ms_hash    ms_engine_render_hash(ms_engine* e);               // 当前帧 FNV（parity 复用）

/* ---- 场景 ---- */
MS_API ms_scene* ms_engine_scene(ms_engine* e);
MS_API int       ms_scene_add_root(ms_engine* e, ms_scene* s, const char* name, ms_id* outId);
MS_API int       ms_scene_save(ms_engine* e, ms_scene* s, const char* assetPath);   // .mscene 容器（M2）
MS_API int       ms_scene_load(ms_engine* e, ms_scene* s, const char* assetPath);   // 反序列化入场景
MS_API int       ms_scene_go_get(ms_engine* e, ms_scene* s, ms_id id, ms_go** outGo);
MS_API int       ms_scene_root_count(ms_engine* e, ms_scene* s, int* outCount);
MS_API int       ms_scene_root_get(ms_engine* e, ms_scene* s, int index, ms_go** outGo);

/* ---- 对象/组件 ---- */
MS_API long      ms_go_instance_id(ms_engine* e, ms_go* g);
MS_API int       ms_go_set_active(ms_engine* e, ms_go* g, int active);
MS_API int       ms_go_destroy(ms_engine* e, ms_go* g);                             // 延迟（帧尾，M0 语义）
MS_API int       ms_go_add_child(ms_engine* e, ms_go* g, const char* name, ms_go** outGo);
typedef void (*ms_cb_void)(void* ctx);
typedef void (*ms_cb_dt)(void* ctx, double dt);
typedef struct ms_component_spec {
  const char* scriptType;        // 反射注册键（C# 完全限定名）
  ms_cb_void  onAwake, onEnable, onStart, onDisable, onDestroy;
  ms_cb_dt    onUpdate, onFixedUpdate, onLateUpdate;
  void*       userData;          // C# 实例 GCHandle（Pinned）
} ms_component_spec;
MS_API int ms_component_register(ms_engine* e, ms_component_spec* spec);
MS_API int ms_go_add_component(ms_engine* e, ms_go* g, const char* scriptType);      // 经反射工厂+绑定回调表实例化
MS_API int ms_go_get_component(ms_engine* e, ms_go* g, const char* scriptType, void** outReflected);

/* ---- 变换 ---- */
MS_API int ms_transform_get(ms_engine* e, ms_go* g, const char* field, double* out); // pos(3)/rot(4 quat wxyz)/scale(3)
MS_API int ms_transform_set(ms_engine* e, ms_go* g, const char* field, const double* in);
MS_API long ms_transform_parent(ms_engine* e, ms_go* g);
MS_API int  ms_transform_set_parent(ms_engine* e, ms_go* g, long parentId, int keepWorld);

/* ---- 反射属性（Inspector/脚本字段——M2 ReflectionRegistry）---- */
MS_API int ms_property_get(ms_engine* e, ms_go* g, const char* type, const char* field,
                           char* outJson, int cap);                    // JSON 值（M2 JsonValue 序列化）
MS_API int ms_property_set(ms_engine* e, ms_go* g, const char* type, const char* field,
                           const char* jsonIn);                        // 反序列化写入（错误=UNKNOWN_FIELD）
MS_API int ms_property_fields(ms_engine* e, const char* type, char* outJson, int cap); // 字段描述清单

/* ---- 资产（句柄）---- */
MS_API ms_asset* ms_assets_load(ms_engine* e, const char* assetPath, int* err);       // 泛型 Load
MS_API int    ms_assets_type(ms_engine* e, ms_asset* a, char* outType, int cap);
MS_API int    ms_assets_guid(ms_engine* e, ms_asset* a, char* outGuid, int cap);
MS_API void   ms_assets_unref(ms_engine* e, ms_asset* a);                             // 引用计数-1
MS_API int    ms_assets_save_text(ms_engine* e, const char* assetPath, const char* payload, const char* type);
~~~

**范围注释**：①不做事件桥（P1——M3 编辑器需要时加 ms_event_*）；②纹理/音频句柄=M2 TextureAsset/AudioAsset 经泛型 Load（解码归属资产层——绑定仅句柄）；③renderCtx=平台渲染器（M1）——C# 挂 OnRender 用。

---

## 2. C# 包装（Milestone.Bind——纯 BCL 零依赖；net8.0）

### 2.1 GameEngine.cs（门面+P/Invoke）

~~~csharp
namespace Milestone.Engine;
public sealed class GameEngine : IDisposable
{
    [DllImport("milestone_v3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ms_engine_create(string title, int w, int h, out int err);
    // tick/render/pump/scene_save/load/…（同 ABI 一一映射）
    private IntPtr _handle; private IntPtr _scene;
    public string Title { get; set; } = "Milestone";
    public int Width { get; set; } = 1280; public int Height { get; set; } = 720;
    public event Action<double>? OnFrame;                        // C# 侧每帧（可挂渲染/逻辑）
    public event Action<IRenderer>? OnRender;                    // 渲染委托（renderCtx 适配）
    public GameObject Root { get; }                               // Scene 根（一场景一引擎模型）
    public GameEngine LoadScene(IScene scene) { /* Build(本引擎) → 场景树注册 → 返回 this */ return this; }
    public int Run() { /* create→loop(tick/pump/render)→destroy；块式 */ }
    public void SaveScene(string assetPath) => /* ms_scene_save */;
    public void LoadScene(string assetPath) => /* ms_scene_load */;
    public AssetStore Assets { get; }
    public void Dispose() { /* ms_engine_destroy */ }
}
~~~

### 2.2 Scene.cs（IScene+Scene 便捷）

~~~csharp
namespace Milestone.Engine;
public interface IScene { void Build(GameEngine engine); void Update(double dt); }
public sealed class Scene : IScene
{
    public GameObject Root { get; } = new("Root");
    public void Build(GameEngine engine) { }                     // 默认空；用户覆盖
    public void Update(double dt) { }
}
~~~

### 2.3 ComponentBase.cs（八回调→C 回调——Unity 语义）

~~~csharp
namespace Milestone.Engine;
public abstract class ComponentBase : IDisposable
{
    // 覆写点（C# 侧八回调——Unity 顺序由 C++ LifecycleDriver 保证）
    public virtual void Awake() { } public virtual void OnEnable() { } public virtual void Start() { }
    public virtual void Update(double dt) { } public virtual void FixedUpdate(double dt) { }
    public virtual void LateUpdate(double dt) { } public virtual void OnDisable() { }
    public virtual void OnDestroy() { }
    public GameObject Owner { get; internal set; } = null!;
    public bool Enabled { get; set; } = true;                    // ms_go_set_active 联动（OnEnable/OnDisable）
    public void Dispose() { OnDestroy(); }
}

// 绑定桥（内部）：每个注册组件实例=静态回调表（[UnmanagedCallersOnly] 静态 thunk）
//   onAwake(ctx)→((ComponentBase)GCHandle.FromIntPtr(ctx).Target).Awake()——GCHandle 由注册时 Pin 持有
//   注册=GameObject.AddComponent<T>()：new T + ms_component_spec{fn ptrs=(thunks), userData=(GCHandle)} → ms_component_register + ms_go_add_component
~~~

### 2.4 GameObject.cs / Transform.cs / AssetStore.cs（句柄代理）

~~~csharp
namespace Milestone.Engine;
public sealed class GameObject
{
    public long Id { get; internal set; }                        // ms_id
    public string Name { get; set; } = "GameObject";
    public Transform Transform { get; internal set; }
    public T AddComponent<T>() where T : ComponentBase, new()
    { /* new T → 注册（thunk 表+GCHandle）→ ms_go_add_component → 返回 T */ }
    public T GetComponent<T>() where T : ComponentBase { /* ms_go_get_component */ }
    public void SetActive(bool v) { }
    public void Destroy() { }                                    // ms_go_destroy（延迟帧尾）
    public GameObject AddChild(string name) { }
}
public readonly struct Transform
{
    public Vector3 Position { get => /* ms_transform_get(pos) */; set { } }
    public Quaternion Rotation { get => /* ms_transform_get(rot quat) */; set { } }   // 四元数（M2 忠实往返）
    public Vector3 Scale { get => /* get */; set { } }
    public GameObject? Parent { get; }
}
public sealed class AssetStore
{
    public Asset Load(string assetPath) { /* ms_assets_load→句柄包装 */ }
    public string TypeOf(Asset a) { } public string GuidOf(Asset a) { }
    public void Unref(Asset a) { }
}
~~~

**规则**：①纯 BCL（System.Runtime.InteropServices 仅）——零 WinForms/System.Drawing；②UTF-8 字符串；③Handle 勿 free（引擎拥有）；④[UnmanagedCallersOnly] 静态 thunk+GCHandle（无 MonoPInvokeCallback 依赖——.NET 8 原生函数指针）。

---

## 3. 分层测试与 cookbook

### 3.1 分层测试

| 层 | 工具 | 断言 |
|---|---|---|
| C++（Core/绑定） | ms_test（既有） | ABI 契约：create/destroy 无泄漏（句柄计数）/错误码映射/场景保存-加载 round-trip 经 ABI |
| C# 绑定集成 | **CsTestRunner**（自研 console——TEST_CASE 风格同 ms_test；零依赖） | ①GameEngine 创建/销毁（句柄非空/无泄漏=加载计数）②AddComponent 后生命周期顺序 == Unity 官方（Awake→OnEnable→Start→FixedUpdate→Update→LateUpdate）③Transform get/set round-trip（quat）④场景保存/加载 round-trip（经 C# 侧）⑤属性 get/set（JSON） |
| 联合冒烟 | csdemo（C# 白盒：Build 4 对象+1 组件→Run 120 帧→exit 0；--renderhash 与 C++ 同=跨语言 parity） | 冒烟通过+哈希一致 |

### 3.2 cookbook 案例

1. **CookbookBF1_Minimal.cs**（10 行）：new GameEngine → LoadScene(new MinimalScene) → Run()；MinimalScene=AddChild+Lane 组件（Update 计数）。用例文案=文档式（每行注释 API 域）。
2. **CookbookBF2_ComponentLifecycle.cs**（25 行）：ComponentBase 子类（八回调日志）→ AddComponent → Run 2 帧 → 断言日志序==官方（C# 侧断言）。

---

## 4. 验收清单（M2.5 全绿）

| # | 项 | 判据 |
|---|---|---|
| 1 | ABI 编译/链接 | ms_bind.h 双链 0 警告 0 错误；C# P/Invoke DllImport 无 DllNotFound（csdemo 运行） |
| 2 | 生命周期 | create/destroy 句柄配对（多次 create-destroy 无泄漏——计数断言） |
| 3 | 组件注册+回调序 | C# 组件八回调顺序==Unity 官方（C# 断言）且 C++ LifecycleDriver 同序 |
| 4 | Transform | get/set round-trip（pos/quat/scale/parent/keepWorld）经 ABI |
| 5 | 属性 | ms_property_get/set JSON round-trip（M2 ReflectionRegistry 经 ABI） |
| 6 | 资产 | ms_assets_load 纹理(wav/音频)/unref 引用计数 |
| 7 | 场景 | save/load round-trip 经 ABI（.mscene 容器+校验） |
| 8 | 线程模型 | 非主线程调用→MS_ERR_THREAD（防御断言） |
| 9 | 零依赖 | C#=纯 BCL；C++ 侧零第三方（绑定=手写映射表） |
| 10 | cookbook | 2 案例编译+运行（C#）；csdemo --renderhash 与 C++ 一致 |

---

## 5. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| M25-a | 顶层宿主 | M2.5 增补 Milestone::App::Engine（门面；M0-M2 无顶层）——采纳（M3 编辑器复用） |
| M25-b | 线程模型 | 单线程（主线程）；跨线程防御=MS_ERR_THREAD（文档明示） |
| M25-c | C# 运行时 | net8.0 纯 BCL；无 XUnit/MSTest——CsTestRunner 自研（零依赖红线延续） |
| M25-d | 事件桥 | 不做（M3 编辑器需要时 ms_event_* 增补——P1） |
| M25-e | 属性传输 | JSON 字符串（M2 JsonValue 序列化）——避免 ABI 类型爆炸（采纳） |
| M25-f | 组件工厂 | Register<T> 已有（M2）——绑定层直接复用（无需重复注册机制） |

---

*t117 完成*（ms_bind ABI 签名（生命周期/场景/对象组件/变换/属性/资产）+错误码+内存所有权+单线程模型+C# 包装语义（GameEngine/Scene/ComponentBase 八回调→C 回调）+cookbook 2 案例+验收 10 项+M25-a..f）

> **实现后评审（t118）：通过（10/10）**——C++（core 17/platform 20/bind 5/ctest 3/3，0 警告 0 错误）+C#（dotnet 0 警告；CsTestRunner fails=0：句柄配对/八回调序==Unity 官方精确（Awake→OnEnable→Start→FixedUpdate→Update→LateUpdate…）/Transform/资产/场景往返/跨线程 MS_ERR_THREAD）+**csdemo --renderhash 4634E387E024BE90==C++（跨语言 parity OK）**+BF1/BF2 PASS+双源 83 文件 MD5 diff=0；**7 偏离全采纳**（①ms_id=int64_t（long 截断风险）②ABI 增补 ms_assets_set_root+ms_assets_save（二进制安全长度——C# 侧 Save/Load 与 bmp 需要）③scriptType=注册键（可含 FullName#n 实例后缀——多实例 userData 防串扰）④引擎双渲染面（parity 固定 1280×800+窗口面——跨语言 parity 稳定）⑤render(ctx) 预留（C# OnRender 完全委托=P1——M25-d 同批）⑥属性 ABI 面向原生反射组件（C# 脚本组件字段=托管侧——**M3 Inspector C# 反射桥增补**）⑦C# 运行前提（原生 DLL 目录+mingw PATH——发布期随包））。
