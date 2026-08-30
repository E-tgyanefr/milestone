# 主流引擎对齐核验：Unity/Godot 标准接口 → v2 核心校正表（engine-mainstream-alignment）

> 起草：eng-design-vis（引擎策划）· 任务 t92 · 用户指令：『我只要原汁原味的引擎，参考主流引擎』——引擎核心=Unity/Godot 式通用游戏引擎（语义/命名/组织对标主流；**音游域=可插拔域库层**）。
> 基线：v2 设计（engine-v2-unity-design.md A-E）+ t90 Editor 设计 + t91（E1）实施中；本表=核心对齐校正（不改变已拍板 V/ED 决策，只做语义/命名/组织层校正）。
> 产出：主流对照表/对齐差异清单（现状/主流/改法/工作量）/音游域解耦/不做项声明/校正执行顺序。只做设计。

---

## 1. 主流引擎标准接口对照表（Unity / Godot）

| 接口（语义） | Unity | Godot | 我们 v2 现状 | 对齐等级 |
|---|---|---|---|---|
| 组件挂接 | GameObject.AddComponent<T>() / GetComponent<T>() | Node.add_child + 脚本/节点类型 | **已有** AddComponent<T>()（同 Unity）——GetComponent 缺 | 差 1 缺口 |
| 变换几何 | Transform.position/rotation/localScale/parent/children（世界/局部） | Node2D/Node3D position/rotation/scale + get_parent/get_children | **已有** Local/WorldPosition·Euler·Scale·SetParent(keepWorld)·AddChild·Forward/Right/Up·矩阵/逆变换（双精度） | 基本对齐（命名=longName，见 §2 改名项） |
| 对象活性/销毁 | Object.SetActive(bool)/Destroy(obj)（延迟到帧尾） | Node.queue_free()/visible | 缺 SetActive/活跃标志；Destroy 缺（域库与工具用 Disable/null 变通） | 缺口（见 §2 D1/D2） |
| 场景加载 | SceneManager.LoadScene(name)（切场景） | change_scene_to_file | **已有** SceneManager.LoadScene(scene, transition)（对象级+转场——无资产名加载） | 语义对齐+缺口=资产寻址加载 |
| 时间抽象 | Time.deltaTime/timeScale/time | delta/Engine.time_scale | **已有**EngineTime（真实/分离/TimeScale/Pause//帧增量）+AudioClock 采样时钟 | 对齐（命名差 deltaSeconds，见 D3） |
| 输入抽象 | Input.GetKeyDown/GetKeyUp/GetAxis("Horizontal") | Input.is_action_pressed | **已有** KeyInput（90+i 键位映射）/TouchInput/HitZone/InputMapper——**无轴/动作表** | 差 缺口（GetAxis/动作表 P2） |
| 资产寻址 | Resources.Load<T>(path)/AssetDatabase | load(path)（资源路径） | **缺统一**（ChartImport/EngineText/找图散落——G8/AssetManager 骨架在 t88 §C 已规划） | 缺口（阶段 C/E3 承接） |
| 组件生命周期 | Awake/OnEnable/Start/Update/FixedUpdate/LateUpdate/OnDisable/OnDestroy | _ready/_process/_physics_process/_exit_tree | **v2 已定八回调**（t89 B 实施中——对齐） | 已对齐（验收=八回调断言） |
| Prefab 实例化 | Prefab 资产+Instantiate | PackedScene.instantiate | 规划中（阶段 D） | 规划对齐 |
| 物理 | PhysX 刚体/碰撞体 | PhysicsBody2D/3D/Area | **不做**（§4 范围声明） | 范围外标注 |
| UI | Canvas/UGUI（树式 UI） | Control 节点 | 已有 UiCanvas/UiPanel/UiButton/UiLabel/UiStackLayout/UiGridLayout/UiTabBar（树式+脏渲染） | 语义对齐（UI=Canvas 类比） |
| 序列化资产格式（语义） | YAML 场景/预制体+meta guid | .tscn/.tres 文本 | 规划 JSON SceneAsset/PrefabAsset（语义等价=文本人类可读+guid 引用） | 规划对齐（格式=JSON 自选，语义一致） |

**结论**：v2 现有骨架与主流**语义高度一致**（组件/变换/树/生命周期/时间/UI）——**差异集中在命名细节与四个缺口**（GetComponent/SetActive+Destroy/资产寻址/轴动作输入）。

---

## 2. 对齐差异清单（每项：现状/主流语义/改法/工作量）

| # | 项 | 现状 | 主流语义 | 改法 | 工作量 |
|---|---|---|---|---|---|
| D1 | GetComponent/GetComponents | 无（域库用字段直挂） | GameObject.GetComponent<T>() | 新增（遍历组件表 return; 语义=Unity） | S（~60 行+断言） |
| D2 | SetActive/Destroy 语义 | 无（工具用 null/Disable 变通） | SetActive(bool)（activity+可中断生命周期）；Destroy=帧尾延迟销毁 | 新增：SetActive 门控（OnDisable/OnEnable 回调触发）+Destroy(帧尾删除列表→OnDestroy 回调) | M（~150 行+生命周期联动断言） |
| D3 | Time 命名 | EngineTime.Tick(dt)/GameTime | Time.deltaTime/timeScale | 增常用别名：TimeSingleton { deltaTime; timeScale; }（包装 EngineTime——命名对齐不改芯）；保留 EngineTime | S（~60 行） |
| D4 | Transform 命名 | LocalPosition 等长名 | position/rotation 短名 | 增短名别名属性（position→LocalPosition 等；不改芯） | S（~40 行） |
| D5 | Input 动作轴 | KeyInput 键值映射（90+i） | Input.GetAxis("Horizontal")/GetKeyDown | 增 ActionMap：GetAxis(name)/GetButton(name)（引擎默认映射=WASD/方向键；自定义表=用户配置） | M（~120 行+断言） |
| D6 | 资产寻址 | 散落（规划 AssetManager G8） | Resources.Load<T>(path) | **AssetManager.Load<T>(path)**（阶段 C 骨架；语义=Resources.Load——路径寻址+类型化） | M（阶段 C 已含） |
| D7 | Prefab | 规划（阶段 D） | Instantiate（模板实例化+go 场景） | 按阶段 D（命名=Instantiate 对齐） | 已排期 |
| D8 | Destroy 延迟语义 | — | Destroy(obj) 帧尾销毁（避免迭代中删除） | 随 D2 一并（帧尾摧毁列表） | 并入 D2 |
| D9 | Inspector 标量 vs 属性 | ReflectionInspector=公共字段（FieldInfo） | Unity=序列化字段（SerializeField 私有也可） | 白名单扩展：字段+可读属性（Info 标记）；v2 默认=public 字段（与 Godot export 语义类同） | S（~40 行） |
| D10 | 命名空间组织 | 引擎全域 ChartPlayer（v1）/v2 新代码 MilestoneEngine.* | Unity=UnityEngine.*、Godot=Godot.* | v2 根命名空间=MilestoneEngine（t88 V7 已定）；核心子命名空间=MilestoneEngine.Core/Scene/…（批B 段2/或 v2 新代码直用） | 已定（V7） |
| D11 | GameObject 实例身份 | 无 InstanceId | 引擎两套（Unity=InstanceID/Godot=NodePath） | 增 long InstanceId（单调计数；序列化引用=Path+Id） | S（~30 行+序列化依赖） |
| D12 | 生命周期 表驱动 | 已定（t89 B） | — | —（对齐完成） | 0 |

**工作量合计**：S×5 + M×3（D2/D5）≈ 引擎侧 1-1.5 周（与 E2/E3 并行；全部新增+别名，**零破坏**——现有长名/KeyInput 保留）。

---

## 3. 音游域解耦方案（核心=通用引擎零音游依赖）

**原则**：MilestoneEngine 核心=通用游戏引擎（Unity/Godot 式）——**零音游域类型**；音游域=可插拔域库层。

| 层 | 命名空间 | 内容 | 依赖 |
|---|---|---|---|
| 核心（通用） | MilestoneEngine（子：Core/Scene/Render/Audio/Input/App/UI/Tooling） | GameObject/Component/Transform/Scene/SceneManager/TimeSingleton/Input* 动作表/AssetManager/IEngineRenderer/IUiDraw/EngineApp/GameEngine/… | 零（零依赖红线；不含 Ruleset/Judgement/谱面类型） |
| 域库（可选引用） | **MilestoneEngine.Rhythm**（可插拔） | Ruleset 四族/RulesetFactory/ChartData/JudgementProfile/JudgementTracker/Storyboard/PracticeSession/JudgementLog/StageSpec（多场） | 仅依赖核心（无游戏层） |
| 演示/应用 | MilestoneEngine.Rhythm.Samples / Examples（编辑器-cookbook） | EnginePresets 10 预设/ToolApp 演示/播放器/示例谱 | 域库+核心 |

**迁移路径（零破坏）**：
1. v1 存量引擎文件按域归位——**命名空间阶段**（t78 批B 段2 与 v2 新代码并行；v2 核心=新代码直写域零音游引用；v1 ChartPlayer 存量留在 v1 兼容面，由 1 次 release 切换后重映射）；
2. 域库=将 现有 Ruleset*/Judgement*/Chart/Stage/Storyboard 文件规整为 MilestoneEngine.Rhythm.*（**复制-接线而非移动**（v1 兼容期）；批B 段2 后删旧名）；
3. 核心「零音游」验证=EngineChecks 核心断言组不含 Rhythm 命名空间引用（可用反射断言=核心程序集无 Rhythm 类型）+ csc 级检查（核心 csproj 排除 Rhythm 目录 Compile）。

**差异化保留**：StageSpec/Storyboard/判定档=**域库层特性**（主流引擎没有——差异即域库 API，可插拔=开关式使用）。

---

## 4. 不做项声明（范围外，标注）

| 不做 | 理由/声明 | 未来 |
|---|---|---|
| 物理（刚体/碰撞/关节） | 范围外——主流引擎核心子项之一但非本项目目标（游戏=音游+轻量演示）；文档明确声明 | P4 预研（如需要以专用库/插件接入） |
| 着色器/材质系统 | 渲染=纯原语+软渲/D2D 后端（无 Shader 管道） | P4 |
| 多平台导出链 | 当前=Windows 桌面（win-x64 单文件）；linux/macOS 预研；移动/Web=WASM 预研记录 | P3-P4 |
| 场景资产二进制 | 采用 JSON 文本（人类可读/差异可检）——二进制=可选（后续） | P4 |
| 动画系统（Animator 类） | 现有 Tween/Particles/Storyboard（游戏域）——通用动画树=范围外 | P4 |

---

## 5. 校正执行顺序（与 E2/E3/B-E 阶段联动）

| 序 | 校正 | 联动 | 说明 |
|---|---|---|---|
| 1 | D1/D11（GetComponent/InstanceId）+ D3/D4（Time/Transform 别名） | 与 E1 收尾并行（Editor Inspector 需要） | 语义缺口最小集（Editor 联动） |
| 2 | D2/D8（SetActive/Destroy 延迟语义）+ 生命周期联动 | t89 B 之后（生命周期地基） | 域库例子（原 变通 null/Disable）随 迁移改用 |
| 3 | D5 动作轴输入（ActionMap） | E3 前后（编辑器映射面板=P2） | 默认映射=WASD/方向键；键位编辑 P2 |
| 4 | D6 资产寻址（AssetManager.Load=Resources.Load 语义） | 阶段 C/E3 | 已排期 |
| 5 | D7 Prefab | 阶段 D | 已排期 |
| 6 | D9 Inspector 字段+属性 | E1 内（ReflectionInspector 扩展） | 小 |
| 7 | D10 命名空间（核心/域库分层落位） | 批B 段2（用户确认后）+v2 新代码先行 | 与 §3 解耦步骤同步 |
| 8 | 分段验收 | 每步断言+EngineChecks 全绿+零破坏（游戏层不动） | — |

---

## 5.5 引擎壳（Editor）按主流对齐（产品思路：核心与 Editor 都保持 Unity 味道）

**原则**：Editor 命名/组织跟随 Unity 术语（类名英文术语；UI 中文文案+英文括注@括号）——「Unity 味道」在编辑器可见面得到延续。

| t90 Editor 面板 | Unity 术语 | 说明 |
|---|---|---|
| SceneTreePanel（场景层级树） | **Hierarchy** | 类名改 HierarchyPanel（对齐） |
| SceneViewPanel（场景视图） | **Scene**（编辑）/ **Game**（Play 时） | 双名=Play/Stop 状态机切换视图标题 |
| InspectorPanel | **Inspector** | 已对齐 |
| ConsolePanel | **Console** | 已对齐（过滤/清空） |
| AssetBrowserPanel（资产浏览器） | **Project** | 类名改 ProjectPanel + 双击加载 |
| 工具栏 | **Toolbar**（Play/Stop/Pause） | 增 Pause（P2） |
| Save SceneAsset | **File→Save Scene**（Ctrl+S） | 快捷键 P2 |
| 新建 GameObject/AddComponent | **菜单 GameObject/Create + Component/Add** | 工具栏按钮等价 |

**附加**（Editor 侧）缺省键位/视图快捷键按 Unity 习惯：W/E/R 变换工具（后置 P1）、双击 Hierarchy=聚焦、Ctrl+S 保存场景（P2）；Editor 布局默认=Unity 风（Hierarchy 左/Inspector 右/Console 底/Project 底左）——t90 ASCII 已一致；本表对齐=命名+术语层（t90 实施时按此命名落地）。

---

## 6. 关键决策点（供 captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| MA1 | 对齐深度 | 语义级+命名别名（不改芯不动存量；正名=短名属性别名，长名保留）——「原汁原味」=语义/命名对齐但零破坏 |
| MA2 | 音游域解耦 | 核心程序集零 Rhythm 引用（反射+csproj 双门禁）；域库=MilestoneEngine.Rhythm.*（复制-接线→批B 段2 删旧） |
| MA3 | 物理/着色器/多平台 | **不做项声明**入 README（范围外标注；P4 预研） |
| MA4 | 执行顺序 | D1/D11/D3/D4 →（t89 B 后）D2/D8 → D5 → 阶段 C/D 承接 → D10 随批B 段2 |
| MA5 | 与 t90 Editor | Inspector 反射=字段+属性（D9 随 E1）；ActionMap=编辑器映射面板 P2 |
| MA6 | 与已拍板（V/ED） | 本表不推翻 V/ED——仅语义与命名层校正（D12 生命周期=已对齐） |
| MA7 | Editor 壳按主流 | Editor 命名对齐 Unity 术语（Hierarchy/Scene/Game/Inspector/Console/Project/Toolbar——类名英文术语+中文文案括注）；默认布局/快捷键按 Unity 习惯；t90 实施按此命名落地 |

---

*终稿*（t92 设计交付；对照表/差异 D1-D12/域库解耦/不做项/执行顺序/MA1-MA6；未写代码；等待 captain 拍板后并入实施排期）
