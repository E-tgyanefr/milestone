# t119：v3 M3 设计细目（六区编辑器：Hierarchy/Scene/Inspector/Console/Project/Toolbar 自绘 + Prefab 挂接 + C# 扩展脚本）

> eng-design-vis · 依据：t90 EngineEditor 设计（v2 经验：六区 ASCII/MA7 Unity 术语/Play-Stop 快照隔离 ED4）+ M2.5（ComponentBridge/Register 基座；t118 偏离⑥=C# 反射桥待增补；M25-d 事件桥 P1）+ M2（AssetDatabase/PrefabAsset/SceneSerializer/ReflectionRegistry）+ M1（IRenderer/IWindow/Input/Audio）+ M0（Core/LifecycleDriver/Time/Event）。
> 产出：编辑器壳+六区布局（Unity 对齐）/场景层级树/Inspector（原生反射+C# 脚本字段）/Console/Project 资产浏览/Toolbar（Play-Stop-Pause 快照隔离）/Prefab 完整挂接/C# 扩展脚本生命周期接线；验收清单随文。只做设计。
> **前置增补（M3 需要）**：①EditorUiKit（自绘 UI——v3 核心无 UI 工具集：Panel/Label/Button/List/TextEdit/TreeView/TabBar+布局+主题——经 IRenderer 绘制；**文本=内嵌位图字体**（VGA 8×8/5×7 ASCII+CP437 子集——零第三方；ttf=后续白名单（stb_truetype 与 PNG 同批评估）；UI 标签=英文（Unity 味））②EditorLog（日志 sink：Info/Warn/Error 缓冲+过滤——Console 数据源）③CsScriptBridge（**C# 反射桥——t118 偏离⑥ M3 增补**：C# 侧注册 GetFields/SetField 委托表→C++ Editor 经绑定调 C# 反射（脚本组件字段编辑/保存）。

---

## 0. 定位与形态

- Editor=**引擎内模块**（Milestone::Editor namespace，milestone_editor STATIC——依赖 core+platform+assets+bind(仅 CsScriptBridge 需要)；**不会进入引擎核心**（0 依赖核心）——主流引擎同理（Unity Editor ≠ 运行时）。
- 形态：独立 exe（**MilestoneEditor.exe**：EditorApp 入口）——M3 后随引擎发布三形态（Editor 独立 exe+--editor 入口（M3.4 顺手）+引擎内复用）。
- **MA7 术语落地**：类名英文 Unity 术语（HierarchyPanel/SceneView/InspectorPanel/ConsolePanel/ProjectPanel/Toolbar）；UI 标签英文（Hierarchy/Scene/Inspector/Console/Project/Play/Stop/Pause）——「Unity 味」。

---

## 1. 编辑器壳与六区布局（Unity 对齐）

### 1.1 EditorApp（壳）

~~~cpp
namespace Milestone::Editor {
struct EditorOptions { std::string projectRoot; WindowDesc window; std::string startupScene; };
class EditorApp {                                     // 编辑器应用（独立 exe 入口）
public:
  EditorApp(const EditorOptions&);
  int Run();                                          // 创建窗/白初始化/布局/主循环（Pump→Tick→Render→Present）
  void Play();  void Pause();  void Stop();           // 状态机（§3.5）
  void SaveScene(); void SavePrefab();                // 工具栏
  bool IsPlaying() const;
private: /* Engine(宿主)/EditorWindow/IEditorLayout 面板集 */
};
}
~~~

### 1.2 六区布局（默认 Unity 风；F 键切换备选（双布局））

~~~
┌───────────────────────────────────────────────────────────────┐
│ Toolbar:  ▶Play ■Stop ⏸Pause │ 💾SaveScene │ +New GO │ +Prefab │
├───────────────┬────────────────────────────┬─────────────────┤
│ Hierarchy     │ Scene View                 │ Inspector       │
│ (场景层级树)   │  (场景视图: UiKit 渲染当前   │  (选中对象组件)  │
│  ▶Scene       │   场景+网格参考(25%);       │    Transform    │
│    ▶Player    │   选中高亮/线框)            │    [组件字段]    │
│      ▸Lane0   │  Play=运行时同视图          │  (原生反射+     │
│    ▸Enemy1    │  (画面实时——PlayMode)      │   C# 脚本字段)  │
├───────────────┴────────────────────────┴─────────────────────┤
│ Console (日志/警告/错误+过滤[Info|Warn|Error]+清空)              │
├───────────────────────────────────────────────────────────────┤
│ Project (Assets/ 树: .mscene .msprefab .bmp .wav | 类型过滤)     │
└───────────────────────────────────────────────────────────────┘
~~~

- **默认尺寸**：1280×800（Editor 窗=窗口客户区——M1 window 复用）；面板宽=左 260/右 320/中弹性；底 Console 160/Project 底左 360（可 F 切换=Project 单独 tab——P2）。
- **UI 复用**：EditorUiKit（Label/Button/CheckBox/List/TextEdit/TreeView/TabBar/ScrollView——自绘经 IRenderer；位图字体）。

---

## 2. 签名级（各面板/UI）

### 2.1 EditorUiKit（自绘控件——经 IRenderer）

~~~cpp
namespace Milestone::Editor::UiKit {
class Font {                                            // 内嵌 VGA 8×8 位图（ASCII+CP437 子集）+Measure/Text
public: static const Font& Default(); double Measure(const std::string&) const;
        void Draw(IRenderer&, const char* text, double x, double y, double size, uint32_t color);
};
struct Style { uint32_t bg, panel, text, accent, border; double padding; };  // 主题（暗色=Unity 风）
class Widget {                                           // 控件基类（rect+hit+draw+hover）
public: Rect Bounds() const; void SetBounds(Rect); virtual void Draw(IRenderer&, const Style&) = 0;
        virtual bool OnClick(double x, double y) { return false; }
};
class Label : Widget; class Button : Widget { std::string text; std::function<void()> OnClick; };
class CheckBox : Widget { bool value; }; class List : Widget { /* items+sel index+scroll */ };
class TextEdit : Widget { std::string value; /* 单行文本编辑（输入=Input 键路由）*/ };
class TreeView : Widget { struct Node { std::string name; std::vector<Node> children; }; };
class TabBar : Widget; class ScrollView : Widget; class Row : Widget;     // 布局容器
class Layout : public Widget { void Add(Widget*, ...); }                 // 简单堆叠（M3 最小）
}
~~~

**文本范围**：编辑器标签+数值+日志（ASCII/CP437）；**中文字符=未来 ttf 白名单**（标注——editor 文案英文=Unity 味一致）。

### 2.2 HierarchyPanel（场景层级树）

~~~cpp
class HierarchyPanel {
public:
  void SetScene(Scene*);  void Refresh();                                // 场景→树（增删改后调）
  void OnUi(UiKit::TreeView&, const Selection&);
  void Select(unsigned idx);                                            // 单选（高亮）
private: /* TreeView node=游戏对象（ID+名称） */ 
};
~~~

### 2.3 SceneView（场景视图）

~~~cpp
class SceneView {
public:
  void SetRenderer(IRenderer*);  void SetViewport(ViewportPolicy*);
  void Render(Scene&, bool playing);                                    // UiKit 渲染当前场景+网格参考(25%)
  void Highlight(ms_id sel);                                            // 选中对象线框（中心 marker——P1：完整拖拽手柄）
  void ToScreen(Vec3 world, double& x, double& y);                      // 世界→视图（简单正交——P1 相机）
};
~~~

### 2.4 InspectorPanel（原生反射 + C# 脚本字段）

~~~cpp
class InspectorPanel {
public:
  void SetSelection(const Selection& sel);                               // GameObject/Asset 二态
  void Refresh();                                                       // 组件列表+字段
  // —— 原生（反射注册表 M2）——
  void RenderFields(IRenderer&, GameObject* go);                        // 每组件：字段名+TextEdit/CheckBox/枚举下拉
  bool CommitField(const char* type, void* obj, const char* field, const char* jsonValue);  // ms_property_set 语义
  // —— C# 脚本组件（CsScriptBridge）——
  void RenderScriptFields(IRenderer&, const std::string& instanceKey);  // 经桥 GetFields→JSON→渲染
  bool CommitScriptField(const std::string& instanceKey, const char* field, const char* jsonValue);
private: /* 选中对象/缓存字段描述 */
};
~~~

### 2.5 ConsolePanel（日志）

~~~cpp
class EditorLog {                                                       // sink（引擎+编辑器共用）
public: enum Level { Info, Warn, Error };
  static void Write(Level, const std::string&);  // 全局缓冲（环形 512 条）
  static const std::vector<Entry>& Buffer();     // {level, text, time}
};
class ConsolePanel { public: void Draw(IRenderer&); void SetFilter(Level); void Clear(); };
~~~

### 2.6 ProjectPanel（资产浏览——M2 AssetDatabase）

~~~cpp
class ProjectPanel {
public:
  void SetDatabase(AssetDatabase*);
  void Refresh();                        // List(dir) 递归+类型过滤（.mscene/.msprefab/.bmp/.wav）
  void OnSelectAsset(const std::string& path);                          // 选中
  AssetRef OpenAsset(const std::string& path);                          // 双击加载（Scene→编辑场景/Prefab→预制视图/纹理/音频=预览占位）
};
~~~

### 2.7 Toolbar（Play/Stop/Pause——快照隔离）

~~~cpp
class PlayController {
public:
  // Play: 快照=SceneSerializer 序列化当前场景→buffer；复制到「运行场景」（runtime Scene 副本）
  // Pause: TimeSingleton.timeScale=0（冻结帧；画面停——Unity Pause；再点=继续）
  // Stop: 丢弃运行场景；**编辑器场景=快照反序列化还原**（运行期修改=弃用——ED4 v1/快照隔离）
  void Play(std::string& sceneJson, Scene& editorScene);                 // 编辑场景=序列化快照（导出 buffer）
  void Pause(); void Resume();
  void Stop(Scene& editorScene, const std::string& sceneJson);          // 还原
  bool IsPlaying() const; bool IsPaused() const;
};
~~~

---

## 3. Prefab 完整挂接

### 3.1 模板编辑（Project 双击 .msprefab → Prefab 视图）

- Prefab 视图=Hierarchy 显示模板根+Inspector 编辑模板对象/组件——**编辑对象=模板内存副本**（未保存不落盘）。
- **保存**：模板 root→PrefabAsset.templateRoot（M2 序列化子集——字段经反射/C# 桥）→ AssetDatabase.Save（.msprefab 容器+校验）。

### 3.2 实例化（Editor UI：Toolbar/+Prefab 或拖放——P1 拖放；M3=菜单中选择）

~~~cpp
// AssetDatabase（M2 已有）+ Editor 扩展：
GameObject* EditorInstantiatePrefab(Scene& target, const std::string& assetPath, const Vec3& at);
// 语义=M2 InstantiatePrefab（模板 JSON 重解析→target 场景）；编辑器场景对象记录 AssetRef{path,guid}（可回源更新——P1）
~~~

---

## 4. C# 扩展脚本生命周期接线（CsScriptBridge——t118 偏离⑥ 增补）

~~~cpp
// —— C# 侧注册（CsEditorBridge，C# 程序集启动可选）——
namespace Milestone::Bind;
public static class CsScriptBridge
{
    // 委托（.NET 8 函数指针 thunk→native 注册）：
    public delegate int  FieldsGet(string instanceKey, out string json);       // C# 反射：ComponentBase 字段清单+值
    public delegate int  FieldSet(string instanceKey, string field, string jsonValue);
    public delegate int  TypeList(out string json);                            // 可挂脚本类型清单（AddComponent 下拉）
    public static void Register(IntPtr engineHandle);  // 注册三项回调→ ms_script_bridge_register
}
// —— C++ 侧（Editor 经绑定）——
int ms_script_bridge_register(ms_engine* e, ms_cb_script_fields fieldsFn, ms_cb_script_field_set setFn, ms_cb_script_types typesFn);
MS_API int ms_script_fields(ms_engine* e, const char* instanceKey, char* outJson, int cap);
MS_API int ms_script_field_set(ms_engine* e, const char* instanceKey, const char* field, const char* jsonValue);
MS_API int ms_script_types(ms_engine* e, char* outJson, int cap);
~~~

- **生命周期接线**（M3 完整）：C# ComponentBase 八回调=已接（M2.5 ComponentBridge）；Editor 增补：①AddComponent 下拉=C# 脚本类型（types 桥）②脚本组件字段编辑=fields/set 桥③**场景保存含脚本字段**：SaveScene 时 Editor 对每个脚本组件经桥取字段 JSON→.mscene components 条目增 "scriptFields" 段；LoadScene 后=组件创建→桥 SetField 恢复——**C# 脚本字段序列化闭环**。

---

## 5. 里程碑拆分（M3.1-3.4 给 eng-coder-vis）

| 批 | 内容 | 验收 |
|---|---|---|
| M3.1 | Editor UiKit（位图字体+控件+主题）+EditorLog+EditorApp 壳+六区空布局（PS 渲染非空） | ms_test_editor_ui（控件绘制非空/字体 Measure 单调/命中检测）+六区渲染非空帧 |
| M3.2 | Hierarchy+Inspector（原生反射字段渲染+CommitField round-trip）+Project（List/双击加载/类型过滤）+SceneView（渲染+网格+选中高亮） | 选择→组件/字段显示；字段改→写回断言；资产加载/保存场景（M2 复用） |
| M3.3 | PlayController（快照隔离：Play 序列化→运行副本；Stop 还原断言=场景树/字段前后一致；Pause=timeScale 0）+Toolbar 全按钮接线 | 快照 round-trip 断言（Play→改→Stop→树/字段=原快照）；Pause 冻结断言 |
| M3.4 | Prefab 挂接（模板编辑+保存 round-trip+实例化到场景）+CsScriptBridge（C# 反射桥：types/fields/set+场景保存含 scriptFields+加载恢复）+--editor 入口+独立 exe | C# 脚本组件字段编辑+保存/加载 round-trip（C# 侧断言）；Prefab 编辑-保存-实例化 round-trip；--editor 冒烟（试玩视觉 P1） |
| P1 后置 | SceneView 拖拽手柄/相机/拖放 Prefab；中文字体（ttf 白名单评估） | — |

---

## 6. 验收清单（M3 全绿）

| # | 项 | 判据 |
|---|---|---|
| 1 | UiKit | 控件绘制非空帧+命中检测断言+字体 Measure 单调/边界 |
| 2 | 六区布局 | EditorApp 渲染=六区非空（离屏断言）；F 切换（P2 标注） |
| 3 | Hierarchy | 场景→树+选择联动（Inspector 刷新断言） |
| 4 | Inspector（原生） | 反射字段渲染+TextEdit 修改→CommitField→反射值回读== |（含 m2 ReflectionRegistry round-trip 经 UI） |
| 5 | Console | EditorLog.Write→Buffer 条数/过滤/清空断言 |
| 6 | Project | AssetDatabase.List 过滤+双击加载（场景/Prefab/纹理）断言 |
| 7 | Toolbar Play/Stop | 快照隔离 round-trip（Play→改→Stop→场景还原== 快照——树/Transform/字段双等）+Pause 冻结 |
| 8 | Prefab | 模板编辑-保存 round-trip+EditorInstantiatePrefab 实例化（层级/字段断言） |
| 9 | C# 脚本 | types 下拉/字段渲染/CommitScriptField→桥回写；场景保存含 scriptFields→加载恢复（C# 侧断言——CsTestRunner 扩展） |
| 10 | 集成冒烟 | csdemo-editor（C# Build 编辑器场景+脚本组件→Run 1 帧非空+退出 0）+--editor 冒烟（试玩视觉 P1）；双链 0 警告 0 错误 |

---

## 7. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| M3-a | 字体 | 内嵌 VGA 8×8 位图（ASCII/CP437——零第三方；英文标签=Unity 味）；ttf=后续白名单（stb_truetype 与 PNG 同批） |
| M3-b | Editor UI 位置 | 引擎内模块 milestone_editor（不依赖核心——运行时零 UI）；独立 exe+--editor 入口（M3.4） |
| M3-c | Play 快照语义 | 快照隔离（ED4 v1：运行副本弃用；Stop=快照还原）——采纳（复刻 v2 经验；P1=Play 现场修改保留） |
| M3-d | C# 桥形式 | MsScriptBridge 三回调（types/fields/set）经绑定注册——C++ Editor 调 C# 反射；脚本字段序列化闭环（采纳） |
| M3-e | SceneView 视图 | 简单正交+网格参考+选中 marker（P1=拖拽手柄/相机） |
| M3-f | Console 数据源 | 引擎+编辑器全局 Log（环形 512）——不做独立日志系统（P1 文件落盘） |

---

*t119 完成*（六区编辑器签名级：EditorApp/面板群/UiKit 位图字体/PlayController 快照隔离/Prefab 挂接/CsScriptBridge/里程碑 M3.1-3.4+验收 10 项+M3-a..f；交付 eng-coder-vis 实现）

> **实现后评审（t120，RhygeMaker 更名后）：通过（10/10）**——C++（core 17/platform 20/bind 5/**editor 7**/ctest 4/4，0 警告 0 错误）+C#（csdemo --editor OK（types/fields/set+scriptFields 并入）/--renderhash PARITY OK/BF1/BF2/CsTestRunner fails=0）+双源 98 文件 MD5 diff=0；**7 偏离全采纳**（①更名承接 RhygeMaker（namespace/路径/RHYGEMAKER_REFLECT/目标）②宏更名一致性 ③CsScriptBridge 回调出参=IntPtr+cap（byte[] 反向 P/Invoke 长度未知——实测修正；语义不变）④场景加载脚本组件=P1（BindComponent 无工厂（RegisterRaw）——C# 宿主重建+scriptFields 重放；csdemo --editor 语义——登记 P1）⑤Pause=跳过 Tick（完全冻结——语义同设计）⑥Prefab 子树=scene 布局同构（平铺+父名引用——设计扩展为子树，环检测共用——优于设计）⑦中文字体=未来 ttf 白名单（M3-a 标注））。P1 后置：SceneView 相机/手柄、拖放、脚本组件加载重放、试玩视觉（RhygeMakerEditor --editor 六区窗口）。
