# 引擎 v2 设计：Unity 式构建 + 独立项目（engine-v2-unity-design）

> 起草：eng-design-vis（引擎策划）· 任务 t88（原 t87 无依赖版）· 用户重大指令：① 上网检索『游戏引擎』定义并按其构建 ② 按 Unity 的方式构建引擎 ③ 这是单独的项目。
> 产出：定义定稿（附来源）/ Unity 语义映射表 / 独立项目方案 / 阶段 A-E+验收。只做设计不写代码。
> 前置：eng-design-vis 已完成 web 检索取证（来源链接见 §1）+ 引擎语义清单抽取（§2 映射表素材，实测签名）。

---

## 0. 结论先行

1. **定义定稿**：游戏引擎 = **游戏开发平台**——一套集成 渲染/物理（碰撞与刚体）/输入/音频/场景管理/资产管线/脚本（生命周期与组件系统）/编辑器 的可编程框架；本引擎按其构建（当前已覆盖 渲染/输入/音频/场景/资产草案/脚本·组件/编辑器雏形（工具窗）——v2 补齐 Unity 式 生命周期/序列化资产/Prefab/Editor-Runtime 双态）。
2. **实现方式=按 Unity 方式**：组件化（GameObject+Component+Transform 层级+Scene+Prefab 序列化）+ 官方生命周期 ExecutionOrder（Awake→OnEnable→Start→Update/FixedUpdate/LateUpdate→OnDisable→OnDestroy）。
3. **独立项目**：引擎 v2 = **独立 sln/版本/发布产物**（独立于游戏程序）；游戏（Milestone.exe）= examples 应用引用引擎。
4. **零破坏策略**：v2 语义=**新增+兼容层**（现有 Build-Update-Render-OnKey 宿主路径保留为 legacy 兼容；v2 生命周期叠加为可选驱动），现有 t57-t83 交付（含 SDK GameEngine/IScene）全绿保持。

---

## 1. 定义定稿（附来源）

**定义**：游戏引擎 = 为游戏开发而建的**软件框架/开发平台**——集成渲染、物理与碰撞、输入、音频、脚本（生命周期/组件）、动画、AI、网络、场景管理与资产（资源）管线，并常含**编辑器**（关卡/资产编辑）。其价值=「**开发者以代码+资产组合编写游戏**，而非把播放器写死」。

**来源链接**（web 检索取证）：
- 维基百科 Game engine（定义+子系统清单+编辑器）：https://en.wikipedia.org/wiki/Game_engine （存档 https://webarchiveweb.wayback.bac-lac.canada.ca/web/20220325150448/https://en.wikipedia.org/wiki/Game_engine ）
- Unity 官方 Execution Order（生命周期顺序）：https://docs.unity3d.com/2019.3/Documentation/Manual/ExecutionOrder.html （中文：https://docs.unity.cn/cn/current/Manual/ExecutionOrder.html ）
- ARM 术语表 Gaming Engine（游戏引擎=用于开发游戏的工具集）：https://www.arm.com/zh-cn/glossary/gaming-engines
- Unity 组件化（GameObject/Components/Transform/Scene/Prefab）：https://docs.unity3d.com/Manual/GameObjects.html / https://docs.unity3d.com/Manual/Prefabs.html

**结论映射到本引擎**：已覆盖=渲染（IEngineRenderer/软渲/D2D//GDI）、输入（KeyInput/TouchInput/HitZone/InputMapper）、音频（IEngineAudioBackend/AudioClock/mci）、时间（EngineTime/Tween）、场景（Scene/GameObject/Component/Transform/SceneManager/SceneTransition）、脚本组件（Component/RulesetAnchor）、资产草案（ChartImport/.mil 逐字符序列化/EngineText）、编辑器雏形（工具窗 4 页/CLI）；**v2 补**=Unity 式组件生命周期、Scene/Prefab 序列化资产、Editor-Runtime 双态、资产管理器（统一 Load 入口）。

---

## 2. Unity 语义映射表（现有骨架 → Unity 对齐）

### 2.1 现有骨架实测（t88 时刻）

| 现有 | 签名（实测） | 说明 |
|---|---|---|
| Component | class Component { public virtual void Update(double dt) } | 单一回调；无生命周期/无 enabled/无 Awake |
| GameObject | AddComponent<T>()（泛型+new()）、Transform 属性 | 无 SetActive/GetComponent(接口)/active 标志 |
| Transform | 局部/世界位置·欧拉·缩放；SetParent(parent, keepWorld)、AddChild、Forward/Right/Up、世界矩阵 | 已 Unity 式（README 自称 Unity 式 Transform） |
| Scene | AddRootGameObject(name)、根对象集合 | 无序列化（运行时构建式） |
| SceneManager | LoadScene(scene, transition)；Tick(dt) | 运行时驱动；无资产加载（仅对象引用） |
| GameLoop | 时钟/循环驱动（GameLoop/EngineTime/TweenRunner） | 无生命周期分派 |
| SceneTransition | 抽象转场（Fade/Slide/CircleReveal…） | 已具 |
| IEngineAppHost/IScene(SDK) | Build/Update/Render/OnKey/OnMouse/OnClose | v1 SDK 入口（保留） |

### 2.2 映射表（语义 → Unity → 差距 → 改动面 → 零破坏策略）

| Unity 语义 | 现有 | 差距 | v2 改动（新增/增强） | 零破坏策略 | **差异化（引擎独有/强于 Unity）** |
|---|---|---|---|---|---|
| 生命周期 Awake→OnEnable→Start→Update→FixedUpdate→LateUpdate→OnDisable→OnDestroy | 仅 Update(double) | 无 Awake/OnEnable/Start/FixedUpdate/LateUpdate/OnDisable/OnDestroy；无双态（设计期/运行时） | Component 新增虚回调：Awake()/OnEnable()/Start()/Update(double)/FixedUpdate(double)/LateUpdate(double)/OnDisable()/OnDestroy()；**LifecycleDriver**（组件注册表：按生命周期顺序分派；GameLoop 接线） | 现有 Update(double) 保留（driver 视为 OnEnable 后首帧=Start 后常规 Update 别名）；无组件=driver 零开销分支 |
| Transform 父层级 | 已 Unity 式（SetParent keepWorld） | 缺 activeInHierarchy/层级枚举便捷 | Transform 增 bool IsActive + 树遍历 API（Children/Ancestors）；GameObject.SetActive 联动 | Additive |
| Scene 序列化资产 | 运行时构建 | 无 SceneAsset | SceneAsset（JSON：根对象→组件类型+属性表+Transform 层级+prefab 引用）+ SceneSerializer/Loader（AssetManager 统一） | 新资产管线；旧运行时构建式=保留（两种手段并存） |
| Prefab | 无 | 无模板/实例化 | PrefabAsset（序列化 GameObject 模板+guid）+ PrefabInstantiate/池（实例=live 对象，可改元数据回存） | 新面；现有规则集锚点=运行时组合（不加 prefab 也无回退面） |
| Editor-Runtime 双态 | 工具窗=运行时 UI（页面） | 无设计期数据模型 | 引擎内 Editor API：SceneAsset 编辑（Open/Inspect/Apply）+Prefab 编辑+组件面板（复用 UiCanvas 栈渲染为编辑器 UI）——**编辑器=引擎内模块**（非独立 IDE） | Editor API 以 Runtime 数据模型为准（编辑器只读写数据）；双态=同一场景数据两种驱动（Editor=停更查看；Runtime=播放） |
| 组件化范式 | 已有 Component/RulesetAnchor（域库锚点）+组件化 UI | 组件查找/启停缺 | GameObject.GetComponent<T>()/GetComponents/Component.enabled（停用不入 Update）+组件生命周期门控 | GameObjects 现有 Start() 门控；enabled=false=driver 跳过 Update 类回调 |
| Assets 管线 | {ChartImport/EngineText/找图 FindBackgroundImage 散落} | 无统一 Load | AssetManager.Load<T>(path)：Scene/Prefab/Texture(BMP->UiImage)/Audio(路径→后端)/Chart(ChartImport)；虚拟 FS 抽象 | 统一入口=包装既有实现；旧直访路径保留 |

**差异化列说明（引擎独有/强于 Unity——映射含「差异化」列）**：上表各语义行的差异化=引擎相对 Unity 的独特能力标注（非差距，是优势清单）：

| Unity 语义 | 差异化（引擎独有/强于 Unity） | 说明 |
|---|---|---|
| 生命周期 | 域库锚点组件（RulesetAnchor）+生命周期的场景即玩法 | Unity 需自建音游域；我们有 Ruleset 四族+Start 生命周期合一 |
| Transform 父层级 | 世界矩阵/逆变换/轴向量精确缩放+刻度复用（轨道/相机挂点） | 与 Unity 等价外，附带双精度 Vec3（长时间轴稳定） |
| Scene 序列化资产 | **多场同屏 StageSpec**（parts×stages 时间轴接力+档位热换+Checkpoint——Unity 多场景=切换加载，非同屏接力）；.mil 谱面=标准内容资产 | 差异化核心能力 |
| Prefab | **四族 Ruleset 组合模板**（Lane/Line/Ring/Path 描述符→场景物） | 我们的「预制体」=规则集描述符（6 字段出新玩法）；Unity Prefab 无玩法域 |
| Editor-Runtime 双态 | **Storyboard**（ResetTo/HookFired/HookRate/StoryboardPlayer=时间线钩子域）+ 工具窗即编辑器雏形 | Unity Timeline 相近但音游钩子域专有 |
| 组件化范式 | JudgementProfile 16+ 判定档+WindowMultiplier 热换/PracticeSession（段循环/幽灵回放）/JudgementLog（判定可视化） | 域组件是 v2 一等公民 |
| Assets 管线 | 零依赖红线（纯托管零 NuGet=可嵌入任意宿主）+ EngineText（GDI 文本）/ChartImport（.mil 内容资产） | Unity=巨引擎；我们=可嵌入 SDK |
| 补充（全局差异化） | EngineTime 变速+AudioClock 采样时钟绑定（变速口径统一）；SceneTransition 内建 5 式转场；EngineJobs 纯托管并行（0B 分配热路径）；ViewportPolicy（letterbox/DPI 显式策略）；UiCanvas 脏渲染即时 UI | 各语义行全局加分项 |

**改动面汇总**：大约 8 个新增文件（LifecycleDriver/SceneAsset/SceneSerializer/PrefabAsset/AssetManager/Component 生命周期增强/GameObject 增强/EditorApi）+ 2 个接线（GameLoop 接 LifecycleDriver、GameEngine 暴露 AssetManager）。**全部新增/增强，无破坏**（现有 t57-t83 与 SDK GameEngine/IScene 原样）。

### 2.3 生命周期顺序断言（验收锚点）

Awake（组件挂接时立即）→ OnEnable（挂接后/激活时）→ Start（首个 Update 前，一帧一次）→ Update/FixedUpdate（固定步长独立）→ LateUpdate（每帧 Update 后）→ OnDisable（失活/移除前）→ OnDestroy（移除时）。EngineChecks 断言=顺序日志比对（t88 单测模式）。

---

## 3. 独立项目方案

### 3.1 独立 sln/版本/发布产物

| 项 | 现在（v1） | v2 方案 |
|---|---|---|
| 项目形态 | 引擎源码/engine/MilestoneEngine.csproj（游戏 sln 内子目录引用） | **独立 sln**：MilestoneEngineV2.sln（根=引擎源码/engine；含 MilestoneEngine.csproj + Editor(MilestoneEngine.Editor.csproj 可选) + Tests/EngineChecks 相对引用）；游戏程序=外部项目引用（ProjectReference 不变式） |
| 版本 | 无独立版本 | **AssemblyVersion/FileVersion = 2.0.0**；NuGet 包（nuget pack MilestoneEngine.2.0.0.nupkg，零依赖=发布黑盒）；git 可独立仓库（用户确认后；当前先独立 sln+包） |
| 发布产物 | 游戏目录内 | **输出产物/engine-v2/**：lib 包 + XML doc + PublicAPI.Shipped.txt（门禁随包）+ AOT profile（阶段 E）+ 模板包（dotnet new milestone-engine 随包下沉） |
| 状态元数据 | — | version.json / CHANGELOG（v2.0.0 起）+ 引擎 README 顶部定位（SDK 定义段） |

### 3.2 与游戏程序解耦边界

- **引擎=库/SDK**：零依赖红线（net8.0 纯托管、零 NuGet/零 WinForms/零 System.Drawing）；不引用游戏源码任何 类型。
- **游戏（游戏源码/Milestone.exe）**= **examples 应用**：WinForms+D2D 壳 + 10 模式 + 编辑器 + 联机——经 引擎 API（v1 兼容层 + v2 生命周期/资产）使用引擎；其 31.8K 代码中 GamePanel/编辑器/曲库 = 应用层（不回流引擎；v2 引擎只抽「通用能力」：已有 EngineApp/组件/UI/域库/SDK）。
- **内容资产**：谱面/音频/皮肤/背景 = 运行时内容（AssetManager.Load 消费位于 examples 目录 其他/Chart 等）——不入引擎 dll。
- **依赖方向**：游戏 → 引擎（单向）；引擎 → 无（纯）。

### 3.3 独立项目 vs 当前 引擎源码 子目录

- 保留 引擎源码/engine 目录作为项目根（物理不动，**sln 化**=根放 MilestoneEngineV2.sln；子目录不变=避免大搬迁/破坏构建链）；「独立」体现在：独立 sln/版本/包/发布/（可选）独立 git 仓库——而非物理挪目录。
- 说明（如有用户意需独立目录）：备选=引擎v2/ 新根（复制+双轨），**不推荐**（双轨=维护两套）；推荐 sln+包级别独立。

---

## 4. 分阶段实施 A-E（每步验收）

| 阶段 | 内容 | 破坏面 | 验收 |
|---|---|---|---|
| **A 独立项目化** | MilestoneEngineV2.sln+版本 2.0.0+nuget pack 脚本+输出产物/engine-v2 发布流+模板包/PublicAPI 随包；现有代码原样 | 零（构建收口） | 构建 0 警告 0 错误；EngineChecks 全绿；游戏层 ProjectReference 指向 sln 工程构建通过；nuget pack 产出 nupkg；dotnet new 模板随包生成 |
| **B 生命周期统一** | Component 八回调+LifecycleDriver+GameLoop 接线+GameObject.SetActive/GetComponent/enabled；Sdk GameEngine 暴露 Driver | 零（现有 Update 兼容） | EngineChecks 生命周期顺序断言（Awake→OnEnable→Start→Update→FixedUpdate→LateUpdate→OnDisable→OnDestroy 日志比对）；现有 DemoRunner 全绿；SDK cookbook 不回归 |
| **C 场景序列化资产** | SceneAsset(JSON)/SceneSerializer/Loader/层级枚举；AssetManager 统一 Load（Scene/Prefab/Texture/Audio/Chart） | 零（新管线条；旧运行时组合保留） | 序列化 round-trip 断言（精确保留 Transform 层级/组件字段）；示例谱加载=旧路径回归绿 |
| **D Prefab + Editor-Runtime 双态** | PrefabAsset/PrefabInstantiate/池；Editor API（SceneAsset 打开/查看/应用+组件面板=复用 UiCanvas；运行时数据同一模型） | 零（新面） | 预制体实例化断言（层级/字段/覆盖元数据）；Editor API 编译样例+场景查看器可开（工具窗接入引擎编辑器页=cookbook 样例） |
| **E 游戏化收口** | 游戏层迁用 v2 语义样例（示例游戏=examples 应用之证明：新示例场景用 v2 生命周期编写）；AOT profile+模板随包 | 小（仅新增样例；游戏本体不动） | 新 cookbook 示例（v2 生命周期+AssetManager+Prefab）运行；AOT 单文件冒烟；EngineChecks 全绿+PublicAPI 门禁 |

**阶段顺序理由**：A（独立化先行=边界与发布平台）→B（生命周期=Unity 语义核心）→C（资产=可编写游戏的基础）→D（Prefab/编辑器=Unity 灵魂）→E（应用验证+发布闭环）。

---

## 5. 与既有资产/交付的关系

- t57-t83（引擎工具/预设/CLI/播放器/SDK GameEngine/IScene/cookbook）=**v1 成果**：全部保留（v2=叠加）；SDK IScene（Build/Update/Render/OnKey/…）在 v2 中**继续可用**（生命周期=SceneBase 内部默认接 LifecycleDriver——Sdk/IScene 改一行挂接即成 v2 场景；或保留原样=零破坏）。
- EnginePresets/工具窗/CLI/播放器=Milestone.exe 游戏=examples（t78 定位不变，《播放器只是示例应用》沿用）。
- 与 t78 语言结论一致：**继续 C# net8.0 零依赖**；v2 不重写（叠加式）。

---

## 6. 关键决策点（供 captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| V1 | 独立形态 | **sln+nuget 包+发布产物独立**（物理目录不动，避免破坏构建链）；可选独立 git 仓库（用户确认） |
| V2 | 生命周期 | 完整 Unity 八回调+LifecycleDriver；Update(double) 兼容保留 |
| V3 | Scene/Prefab 序列化 | JSON SceneAsset/PrefabAsset（人类可读；版本化字段）；二进制=阶段 E 可选 |
| V4 | Editor 形态 | **引擎内 Editor API 模块**（复用 UiCanvas 栈；非独立 IDE）——与「可编程引擎」定位一致 |
| V5 | 游戏层 | 保持 examples 应用（仅新增 v2 语义示例；不迁移现有 31.8K） |
| V6 | 阶段拆分 | A-E 每阶段独立可验收；B 为语义核心（建议排期优先）；C/D 依赖 B |
| V7 | 与 t78 批A/B/C | v2=独立推进（不与 v1 批B 命名空间切换耦合——v2 新代码可直接用 MilestoneEngine.* 命名空间，v1 存量按 D4 另行处理） |

---

*终稿*（t88 设计交付；定义定稿+映射表+独立方案+阶段 A-E；未写代码；等待 captain 拍板 V1-V7 后派 eng-coder-vis 实施）
