# EngineEditor 设计：Unity 式引擎编辑器（engine-editor-design）

> 起草：eng-design-vis（引擎策划）· 任务 t90 · 用户指令：『这不是引擎』——引擎本体=可编程 SDK（2.0.0 就位），**可见形态=引擎编辑器（Unity 方式）**尚未有。
> 基线：t88 阶段 D（Editor-Runtime 双态）+ V4（引擎内 Editor API 模块，复用 UiCanvas，非独立 IDE）+ t89（A 独立项目化/B 生命周期——依赖）均已列入。
> 产出：布局 ASCII/模块与 API 签名/分批实施（给 eng-coder-vis）/阶段 D 衔接清单。只做设计。

---

## 0. 定位与形态

- **Editor = 引擎本体可见形态**：同一个引擎数据模型（Scene/GameObject/Component/Transform）在**编辑器态**与**运行态**双驱动（Editor=停更查看/编辑；Runtime=GameEngine.Run 播放）。
- **形态**：①引擎内模块（MilestoneEngine.Editor 程序集/命名空间 MilestoneEngine.Editor.*；复用 UiCanvas/UiComponents/Scene/GameObject 反射；**零依赖红线**：纯托管+reflection，无 WinForms）②入口=CLI --editor（EnginePlay 内）与独立 exe（MilestoneEngineEditor.exe=同一 EntryPoint 不同名称；单文件发布随引擎 v2 包）。
- **差异化**：Inspector 中「玩法组件」（Ruleset 描述符/JudgementProfile/StageSpec/Storyboard）以一等公民呈现——Unity 没有的域组件在编辑器中直接可视可编（v2 差异化宣示表的可见化）。

---

## 1. 窗口布局（ASCII）

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ [工具栏]  ▶ Play  ■ Stop  │  💾 保存场景  │  ＋新建 GameObject  ｜  +AddComponent │ Net │
├──────────────────┬────────────────────────────────────────┬─────────────────┤
│ 场景层级树        │  场景视图 (Scene View)                  │  Inspector       │
│ (GameObject 树)   │  - UiCanvas 渲染当前场景               │  (选中对象)      │
│  ▼ Scene (root)   │  - 网格/参考叠加：坐标轴/网格线         │  ▼ GameObject   │
│    ▼ Player       │  - 线框选中高亮/选区框 (P1)            │    Transform     │
│      Transform    │  - 运行态同视图（Play 时实时画面）      │      Position/…  │
│      Sprite       │                                        │  ▼ RulesetAnchor│
│    ▼ Camera       │                                        │    Keys 4 ▾      │
│  ▸ Enemy1         │                                        │    Profile ▾    │
│  ▸ Enemy2         │                                        │  Scale: [0.9 ]   │
│  [折叠箭头][选中高亮][拖拽层级 = P2]                        │  [标量字段可改]   │
├──────────────────┴────────────────────────────────────────┴─────────────────┤
│ [控制台]  过滤: ●错误(0) ●警告(0) ●日志(N) ▸ [行]  [最新日志滚动]            │
├─────────────────────────────────────────────────────────────────────────────┤
│ [资产浏览器]  Assets/  SceneAsset │ PrefabAsset │ 图像 │ 音频  [双击加载/单击选中] │
└─────────────────────────────────────────────────────────────────────────────┘
```

- **默认尺寸**：1280×800 设计逻辑（Editor 窗=引擎窗 FitMode.Letterbox 复用）；左 300/中 620/右 300/底部 160/资产条 96（宽度可按 F 键切换布局——P2）。
- **运行/停止**：Play=GameEngine 嵌入当前场景（Runtime 态；暂停后编辑器数据=运行副本）；Stop=回编辑器态（状态差量回滚=V2 数据副本隔离初始设计；P2 支持现场修改）。

---

## 2. 模块结构（Editor/ 目录，MilestoneEngine.Editor.*）

| 模块 | 职责 | 复用 |
|---|---|---|
| EditorApp | 编辑器应用（Editor 窗宿主=引擎窗+SceneManager 驱动；工具栏/面板布局/加载初始场景） | EngineApp/GameEngine/SDK |
| EditorWindow | 编辑器窗口状态（活动面板/尺寸/布局缓存/主题） | UiTheme/设置 |
| SceneTreePanel | 场景层级树（GameObject 树绘制+折叠+选中高亮+新建/删除） | UiCanvas/UiCollapse 自绘 |
| SceneViewPanel | 场景视图（当前场景经 UiCanvas 渲染+网格/参考叠加+选中线框 P1） | UiCanvas/IUiDraw |
| InspectorPanel | 选中对象组件列表+反射标量字段编辑（int/float/bool/string/enum/颜色/枚举→下拉/数值→NumericUp 样式） | System.Reflection/UiControls |
| ConsolePanel | 日志/警告/错误+过滤+滚动（EngineLog 桥接） | Logger 桥（OnLog 事件） |
| AssetBrowserPanel | Assets 目录树（SceneAsset/PrefabAsset/图/音频——点击选中/双击加载） | AssetManager（G8/阶段 C） |
| PlayController | Play/Stop 状态机（Embed GameEngine 于 Editor；数据副本隔离） | GameEngine/LifecycleDriver（t89 B） |
| Selection | 当前选中（GameObject/Asset 二态；事件 SelectionChanged） | — |
| Assets | 资产数据库（Scan/Get/Import；.mil=ChartAsset；JSON SceneAsset） | 阶段 C SceneAsset/AssetManager |
| SceneDatabase | 场景注册/保存（Dirty 标记；保存=JSON SceneAsset 序列化） | 阶段 C SceneSerializer |
| EditorLog | 控制台缓冲+过滤 | — |

**依赖**：Editor → EngineApp/UiCanvas/Scene/GameObject/Component/reflection +（t89 B）LifecycleDriver +（阶段 C）SceneAsset/AssetManager。零依赖红线不变（Editor 亦纯托管）。

---

## 3. 骨架 API 签名（顶层草案）

```csharp
namespace MilestoneEngine.Editor;

public sealed class EditorApp                                // 编辑器应用（宿主入口）
{
    public EditorAppOptions Options { get; }                  // 布局/主题/启动场景
    public EditorWindow Window { get; }
    public GameEngine Host { get; }                           // 运行态承载（Play 用）
    public Scene CurrentScene { get; }                        // 编辑器态场景
    public void OpenAssets(string root);                      // 资产目录
    public int Run();                                         // 块式（--editor 入口）
    public void Play(); public void Stop();                   // 状态机
    public void SaveScene(string path);                       // JSON SceneAsset
    public void LoadScene(string path);
}

public static class Selection
{
    public static GameObject Current { get; set; }            // 选中场景对象（null=资产）
    public static Asset CurrentAsset { get; set; }
    public static event Action Changed;                       // Inspector/场景视图刷新
}

public static class Assets                                       // 资产数据库（骨架）
{
    public static IEnumerable<Asset> List(string path);
    public static Asset Load(string path);                    // SceneAsset/PrefabAsset/Chart/Image/Audio
    public static void Import(string sourcePath);             // 导入（.mil→ChartAsset）
}

public static class SceneDatabase                              // 场景注册/保存
{
    public static bool Dirty { get; set; }
    public static void Save(Scene scene, string path);        // SceneSerializer（阶段 C）
    public static Scene Load(string path);
    public static void MarkDirty(Scene scene);
}

public static class ReflectionInspector                        // 组件字段反射（标量集）
{
    public static IReadOnlyList<FieldInfo> Scalars(Type componentType);   // int/float/bool/string/enum/RgbaColor
    public static object Read(object comp, FieldInfo f);
    public static void Write(object comp, FieldInfo f, object value);
    public static string Format(object comp, FieldInfo f);
}
```

**Editor 面板基类**：EditorPanel { Title; Build(UiCanvas); OnSelectionChanged(); Refresh(); }——面板=引擎 UiCanvas 场景（复用 UiPanel/UiLabel/UiButton/UiStackLayout/UiGridLayout/UiScroll）。

**Scenario（差异化）**：Inspector 的「玩法组件」= RulesetAnchor（含 RulesetDescriptor 字段：Keys/ProfileName/Speed/ScrollDir 下拉+数值）——编辑 6 字段=创建新玩法（cookbook ⑨ 的编辑器化）；JudgementProfile 档位热换（t6 E）在 Inspector 直接可切——差异化在编辑器可见。

---

## 4. 交互矩阵

| 交互 | 行为 | 批 |
|---|---|---|
| 场景树 单击 | 选中（Selection.Current+高亮；Inspector 联动） | E1 |
| 场景树 ▶/▸ | 折叠/展开 | E1 |
| 场景树 双击 | 重命名（P2） | P2 |
| 工具栏 Play | PlayController（GameEngine 嵌入当前场景+运行副本） | E2 |
| 工具栏 Stop | 回编辑器态（差量回滚 v1：场景数据=运行副本弃用） | E2 |
| 💾 保存场景 | SceneDatabase.Save（JSON；Dirty 清除） | E2 |
| ＋新建 GameObject/AddComponent | 空对象/组件（反射默认值；Inspector 可编） | E1 |
| Inspector 标量字段 | 反射读/写（数值 Input+Enter；枚举下拉） | E1 |
| 资产浏览器 单击/双击 | 选中/加载（DoubleClick→编辑态加载或文件内容查看） | E3 |
| 控制台过滤 | 错误/警告/日志开关+清空 | E3 |
| 场景视图网格/参考 | 100% 透明度网格+坐标轴（P1：选中线框/选区框/拖拽变换） | E1（网格）/P1 |

---

## 5. 分批实施（给 eng-coder-vis）

| 批 | 内容 | 验收 | 依赖 |
|---|---|---|---|
| E1 | 编辑器壳（EditorApp+EditorWindow 布局 6 区）+场景树+Inspector（ReflectionInspector 只读+标量编辑）+工具栏基础（新建/AddComponent） | 打开-->场景树/Inspector 联动；标量改值回写断言（t90 单测）；EngineChecks 绿 | t89（A+B）；阶段 C SceneAsset 可选（E1 用运行时场景） |
| E2 | Play/Stop 状态机+SaveScene/LoadScene（JSON SceneAsset 序列化）+控制台接日志 | Play→Run 嵌入断言；Save/Load round-trip=GameObject 树+字段一致；控制台日志进入 | t89 B + 阶段 C SceneSerializer |
| E3 | 资产浏览器（Assets 目录树+ChartAsset 导入 .mil+双击加载）+差异化玩法组件 Inspector（Ruleset 描述符/JudgementProfile 下拉编辑） | 资产列表/导入断言；玩法组件字段编辑回写 Ruleset 描述符（复用 EnginePresets 运行时） | E1/E2 + AssetManager |
| E4 | CLI --editor（EnginePlay 入口）+ 独立 exe（MilestoneEngineEditor.exe+单文件发布+模板随包）+README 编辑器章节 | --editor 启动=布局在位；独立 exe 单文件冒烟；README 截图位 | E1-E3 + 阶段 A 发布流 |
| P1（后置） | 场景视图选中线框/拖拽变换（Transform 手柄）；层级拖拽；Play 现场修改 | 手动/断言 | E2 |
| P2 | 窗口布局自定义（F 键切换/持久化）；资产预览缩略图；撤销/重做（命令栈） | — | E3 |

**每批交付**：改动清单+验证命令+与 t89/游戏层回归对照表（零破坏原则；游戏层不动）。

---

## 6. 阶段 D 衔接清单（t88 D Editor-Runtime 双态 + V4）

| 衔接点 | 状态 | 说明 |
|---|---|---|
| Editor-Runtime 双态 | ✅ 本设计=其落地骨架（Play/Stop+数据副本隔离） | 双态=同一数据模型两驱动 |
| PrefabAsset | ⏳（阶段 C 或 Editor E3 后） | 预设体=资产浏览器「PrefabAsset」类型+实例化（Inspector 的 Prefab 引用字段） |
| 引擎内 Editor 模块 | ✅ 本设计（MilestoneEngine.Editor + 零依赖红线） | V4 落地 |
| Object 规范化 | ⏳ 建议 E1 前：GameObject 增加 InstanceId+Name 序列化字段（SceneSerializer 需要） | 阶段 C |
| Assets 管线 | ⏳（阶段 C）E3 依赖其骨架 | — |

---

## 7. 关键决策点（供 captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| ED1 | Editor 形态 | 引擎内模块+CLI --editor+独立 exe 三形态同一实现（EnginePlay 入口参数化） |
| ED2 | 布局默认 | 1280×800 六区（左 300/中 620/右 300/底 160/资产 96）F 键切换（P2） |
| ED3 | Inspector 反射 | 标量字段白名单（int/float/bool/string/enum/RgbaColor+枚举下拉）——纯反射+无序列化注入 |
| ED4 | Play 数据语义 | v1=运行副本弃用（Stop 回滚）；P2=现场修改并回写 |
| ED5 | 差异化可见化 | 玩法组件（Ruleset 描述符/JudgementProfile/StageSpec/Storyboard）Inspector 一等公民——E3 落地 |
| ED6 | 独立 exe | MilestoneEngineEditor.exe（同 EntryPoint 改名；单文件发布随引擎包） |
| ED7 | 与 t89/t88 | 依赖=独立项目化（A）+生命周期（B）；本设计不含 Prefab/Asset 全量（按阶段 C/D 排期） |

---

*终稿*（t90 设计交付；布局 ASCII/模块+API/分批 E1-E4+P1/P2/阶段 D 衔接/ED1-ED7；未写代码；等待 captain 拍板后派 eng-coder-vis（建议 E1 先排））
