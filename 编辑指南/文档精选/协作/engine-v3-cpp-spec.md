# 引擎 v3（C++20 完全重写）规格书：特性提炼 + C++ 架构 + 里程碑（engine-v3-cpp-spec）

> 起草：eng-design-vis（引擎策划）· 任务 t107 · 用户指令（最终拍板，**取代 t78 语言结论**）：『引擎用 C++ 完全重写，只保留引擎的特性，只要原汁原味的引擎体验』；**补充定案：双层架构（Unity 式分工）——引擎核心=C++（性能与本体）+ 开发层=C#（公开 API 面/脚本/扩展/编辑器扩展——开发者只写 C#）**（本版 v1.1 落实：§3.5 双层图/§3.6 C# 开发层/§3.7 绑定方案 V3-B）。
> 范围：特性提炼（保留/舍弃/改造）+ C++20 技术栈 + 架构分层 + 里程碑 M0-M4 + 游戏层对接 + 原汁原味对照 + V3-1..V3-8 决策点。只做设计不写码。
> 前提记录（诚实）：C# 存量 47.8K（游戏 31.8K+引擎 16K）+ v2 设计/编辑器 E1 等=**按用户指令转轨**：C# 引擎库与 v2 建设=封存为「legacy 参考实现」（feature extraction 的素材源；不随 v3 继续演进）；游戏（Milestone.exe）=examples 保留（见 §5）。

---

## 0. 总纲与原汁原味定义

- **目标**：C++20 通用游戏引擎——**原汁原味=主流引擎语义**（Unity/Godot 命名、生命周期、场景/组件/Prefab/资产寻址一致——t92 对齐表即 v3 的语义蓝图），**零托管依赖/零 GC**；音游域=可插拔域库（MilestoneEngine 核心零音游依赖——t92 解耦原则延续）。
- **只保留引擎特性**：重构取 v1/v2 已验证特性子集（13 项核心），**不做**播放器/工具窗/游戏本体（=examples）+ 物理/着色器（范围外声明延续）。
- **技术栈**：C++20 / CMake ≥3.24 / MSVC(2022)+Clang(≥17) 双链 / 测试=自研轻量头文件（Catch2 式断言宏，零第三方——红线）/ Profile=Trace+PerfGate 自研（基线 0B 热路径目标延续）。

---

## 1. 特性清单提炼（保留 / 舍弃 / 改造）

### 1.1 保留（以 C++ 原生表达——13 项核心引擎特性）

| # | 特性 | 来源验证（v1/v2） | v3 C++ 语义 |
|---|---|---|---|
| C1 | 场景树/GameObject/Component | Scene.AddRootGameObject/GameObject.AddComponent | GameObject（Handle 化）+Component+Transform 层级（Scene 树） |
| C2 | Transform | 世界/局部矩阵+SetParent(keepWorld)+轴向量 | 同语义（C++ math：Vec3/Quat/Mat4——手写双精度+SIMD 可选） |
| C3 | 生命周期八回调 | Unity 顺序（v2 t89 已对齐） | Awake/OnEnable/Start/Update/FixedUpdate/LateUpdate/OnDisable/OnDestroy（LifecycleDriver=组件表驱动，顺序断言） |
| C4 | 事件调度 | 组件/输入/判定事件（v1 事件体系） | EventBus（类型擦除+queue；每帧 dispatch——域名事件（Judgement 等）公开） |
| C5 | 时间系统 | EngineTime（变速/暂停/分离） | TimeSingleton{deltaTime/timeScale/真实时间}（Unity 命名） |
| C6 | 输入抽象 | KeyInput/TouchInput/HitZone/InputMapper（v1） | Input{Axis/Button/ActionMap}（t92 D5 动作表语义）+触摸/命中区 |
| C7 | 资产寻址+序列化 | AssetManager 规划（G8）+ChartImport .mil | AssetStore{Load<T>(path)}；SceneAsset/PrefabAsset=JSON（语义等同 Unity YAML/Godot tscn；guid 引用） |
| C8 | 渲染抽象 | IEngineRenderer 9 原语+IEngineWindow+ViewportPolicy | IRenderer（原语层）+后端接口（Platform 层；D3D11/Vulkan/GLFW 后端点）；四族绘制=域库/示例层（识别语义=主线：Lane/Line/Ring/Path 域库 Draw 模块） |
| C9 | 音频采样时钟 | AudioClock+IEngineAudioBackend（mci） | AudioClock{采样位置驱动}；后端（WinMM/WASAPI 或驱动；后端=Platform） |
| C10 | 并行 Job 系统 | EngineJobs（Map/Reduce 10^7） | 原生 C++ 线程池（C++20 jthread+任务流（ForkJoin 式）——无托管依赖；Steal 可选） |
| C11 | 判定域库（**域库层**） | JudgementProfile/Ruleset 四族/JudgementTracker/StageSpec/Storyboard/PracticeSession/JudgementLog | MilestoneEngine.Rhythm.*（C++ namespace；可插拔；核心零依赖） |
| C12 | 组件式 UI 栈 | UiCanvas/UiPanel/UiLabel/UiButton/UiStackLayout/UiGridLayout/UiTabBar/Theme+脏渲染 | UI 组件树（引擎自绘 UI——Editor 用；ImGui 可选=决策点 V3-5） |
| C13 | 编辑器（Unity 式六区） | t90 设计（Hierarchy/Scene/Inspector/Console/Project/Toolbar） | v3 Editor 模块（§3.4）——**原汁原味体验核心** |

### 1.2 舍弃（不迁入 v3）

| 舍弃 | 理由 |
|---|---|
| SharpDX/D2D 托管渲染（游戏层） | 托管依赖；v3 渲染=原生后端（D3D11/WGPU/Vulkan 决策点 V3-6） |
| 托管 GC 依赖（C# 资源管理） | v3=原生 RAII/唯一所有权+句柄池（零 GC） |
| C# 专用设施（LINQ/反射/GC 分配器/程序集） | 原生替代（容器/反射=自研元数据注册表（组件反射用于 Inspector——C++ 反射=注册表宏） |
| WinForms/WPF 相关（游戏层 UI/对话框） | examples 层；v3 引擎 UI=自绘（C12/ImGui） |
| 工具窗/CLI 播放器/打包器（t57-t73） | **examples/工具层**（非引擎特性）；v3 Tools=--editor/--preset/--preview 三条轻 CLI（§3.5） |

### 1.3 改造（红线保持/可选项）

| 改 | 说明 | 决策点 |
|---|---|---|
| 零依赖红线（核心零第三方） | 核心+Platform+Domain:零第三方；**Editor UI**：可选 **ImGui（单头库=主流体验）**或自绘（C12） | V3-5 |
| 渲染后端 | Win32 软渲（v1 端口）+D3D11（原生首推）+Vulkan/GLFW 跨平台后续 | V3-6 |
| 测试框架 | 自研头文件（Catch2 式：TEST_CASE/REQUIRE 宏——零第三方） | 固定 |
| 反射（Inspector 用） | 自研注册表宏（MILESTONE_REFLECT(Type, fields...)）——组件字段枚举/序列化/Inspector 共用 | 固定 |

---

## 2. C++ 技术栈

| 层 | 选型 | 说明 |
|---|---|---|
| 语言 | C++20（concepts/ranges/jthread/span；协程=音频/加载可选） | 主流与现代；避开 C++26 波动 |
| 构建 | CMake ≥3.24（单树双后端 MSVC/Clang；选项=开关后端） | 跨平台可扩展（后续 Linux/GLFW） |
| 编译器 | MSVC 2022（win 主链）+ Clang ≥17（跨平台/lint） | 双链 CI（Debug+Release） |
| 测试 | 自研 ms_test.hpp（宏+断言+用例注册——Catch2 风格零第三方） | 红线（核心零第三方） |
| CI | GitHub Actions 或本地 run.ps1（build+test+perf gate 三阶段） | Debug/Release+MSVC/Clang 矩阵 |
| 性能 | PerfGate（自研——热路径分配/帧耗断言；基线=0B/12000FPS 目标延续） | 优化矩阵 O1 的 v3 版 |
| 第三方白名单 | **唯一候选：ImGui（Editor 单头库）**——决策点 | V3-5 |

---

## 3. 架构分层

```
┌─────────────────────────────────────────────────────────────┐
│  Tools（CLI：--editor / --preset <id> / --preview <id>）      │
├─────────────────────────────────────────────────────────────┤
│  Editor（六区：Hierarchy/Scene/Inspector/Console/Project/      │
│           Toolbar——自绘或 ImGui）                            │
├─────────────────────────────────────────────────────────────┤
│  Domain（可插拔：Milestone Engine Rhythm——判定/Ruleset 四族/   │
│           StageSpec/Storyboard/Practice）                    │
├─────────────────────────────────────────────────────────────┤
│  Core（ECS 式组件+Scene+GameObject+Component+Transform+       │
│        LifecycleDriver+EventBus+Time+JobSystem+AssetStore+    │
│        Serialization(JSON)+Reflection 注册表）               │
├─────────────────────────────────────────────────────────────┤
│  Platform（窗口/输入/渲染/音频——Win32+GLFW: D3D11/软渲/Vulkan；  │
│            WinMM/WASAPI 音频后端；后端=接口+实现）              │
└─────────────────────────────────────────────────────────────┘
```

- **Core**：ECS 式（GameObject=组件池引用+Transform 组建；组件=类型注册+生命周期钩子；场景树=层级）；零第三方/零音游。
- **Platform**：接口（IWindow/Input/IRenderer/IAudioBackend）+默认实现（Win32 窗口+软渲 = 端口 v1；D3D11 原生；WinMM 音频端口 v1）。跨平台=接口第二实现（GLFW/Vulkan/ALSA）——**决策点 V3-7**（一期=win-only）。
- **Domain**：Rhythm.*（C11）——可插拔（核心零引用；域库链=cookbook 演示）。
- **Editor**：t90 六区+MA7 术语；自绘（C12）或 ImGui（V3-5）。
- **Tools**：三条 CLI（--editor 启编辑器/--preset 无头跑域库预设 acc/--preview 开窗预览四族示例）；播放器/打包器=examples（不迁）。

### 3.5 双层架构图（Unity 式分工——用户补充定案 v1.1）

```
┌─────────────────────────────────────────────────────────────────┐
│  C# 开发层（开发者只写 C#——公开编程面）                          │
│    GameEngine/IScene/GameObject 脚本体/C# 组件+C# Component 生命周期│
│    回调（Awake..OnDestroy 挂 C# 侧）/Assets/Input/Editor 扩展类   │
│    （Inspector 自定义/编辑器工具——Unity 式）                      │
├─────────────────────────────────────────────────────────────────┤
│  绑定层（V3-B：C ABI + C# P/Invoke 适配层——推荐）                 │
│    ms_bind.h（extern "C" 面：生命周期回调调度/组件注册/Transform  │
│    获取/渲染委托/事件桥）                                        │
├─────────────────────────────────────────────────────────────────┤
│  C++ 引擎核心（性能与本体——本规格 §3.1-3.4 全部不变）              │
│    场景树/GameObject/Component/Transform/生命周期 C++/Time/Input/ │
│    资产序列化/渲染抽象/音频/Job C++20/域库层(或 C++ 侧)/CLI        │
└─────────────────────────────────────────────────────────────────┘
```

### 3.6 C# 开发层规范（公开编程面=Unity 式）

| 面 | C# 公开 API（开发者视角） | 绑定到 C++ 运行时 |
|---|---|---|
| 入口 | GameEngine（new + LoadScene(IScene)/Run()——同 v2 SDK 草案） | 绑定层创建 C++ EngineHandle |
| 场景 | IScene（Build/Update/Render/OnKey/OnMouse/OnClose） | 生命周期回调经绑定注册到 C++ LifecycleDriver |
| 组件 | C# Component（Awake/OnEnable/Start/Update/FixedUpdate/LateUpdate/OnDisable/OnDestroy 八回调——Unity 序） | 组件注册表（绑定层元注册+回调表） |
| 对象 | GameObject（AddComponent/GetComponent/SetActive/Destroy/Transform 访问） | 句柄式代理（C++ 对象引用→C# 句柄） |
| 资产 | Assets.Load<T>/AssetDatabase（JSON SceneAsset/PrefabAsset） | C++ AssetStore 代理 |
| 输入 | Input.GetAxis/GetKeyDown/GetButton/ActionMap | 绑定事件桥（轮询+回调） |
| 编辑器扩展 | [CustomInspector]/[MenuItem] 特性类——Inspector 自定义/工具栏扩展 | 编辑器 C++ 引擎绑定（扩展宿主注册表） |
| 域库（可选） | MilestoneEngine.Rhythm.*（C# 面=判定/规则/Storyboard 托管绑定） | 域库 C++ 侧实现+C# 代理（或域库本体 C#——决策点 V3-B2） |

**规则**：①零托管依赖红线持续=开发层 C# 程序集独立（依赖注入由绑定运行时提供，不引用任何 UI 框架）②开发者不接触 C++（模板/联调=绑定层唯一入口）③边界=每帧 C++→C# 回调（生命周期/Update）+C#→C++ 调用（命令式）两种模式。

### 3.7 绑定方案（决策点 V3-B）

| 方案 | 描述 | 优点 | 缺点 | 建议 |
|---|---|---|---|---|
| **A C ABI + C# P/Invoke 适配层（推荐）** | C++ 导出 extern "C" 面（ms_bind.h：Dispose/Create/Tick/Render/Input/ComponentRegister/PropertyGetSet/EventSubscribe）；C# 侧 DllImport 适配层（Marshal+delegate 回调） | 跨平台（Linux 后续同链）/零托管依赖/两层测试分离（C++ 单测+C# 集成测）/心智负担=单向 | 需手写绑定面+封送（S/M——封送表+句柄表先建） | **推荐**（红线一致；与 C# 侧零依赖红线同链） |
| B C++/CLI | MSVC 专用编译（/clr）直接桥 | 绑定免封送 | 锁 MSVC（跨平台不可）/CLR 依赖（与纯原生目标冲突）/工具链受限 | 不推荐（性能与跨平台需求不符） |

**绑定 API 面（A 方案草案）**：
```c
// ms_bind.h（C ABI——稳定版序）
void*  ms_engine_create(const char* title, int w, int h);
int   ms_engine_tick(void* h, double dt, double* outFrameMs);   // 回调 C# 生命周期（经注销后的 delegate 表）
int   ms_engine_render(void* h, void* rendererCtx);             // 渲染委托（C# 侧可挂 OnRender）
void  ms_engine_input(void* h, int key, int down);              // 输入桥
void  ms_engine_destroy(void* h);
int   ms_component_register(const char* scriptNS, const char* type, void* onAwake, void* onUpdate, int n);
void* ms_object_get_transform(void* h, long instanceId);        // Transform 访问（只读属性 get）
int   ms_property_get(void* h, long instanceId, const char* prop, double* out);
int   ms_property_set(void* h, long instanceId, const char* prop, double v);
void  ms_scene_load(void* h, const char* assetPath);            // 资产寻址（JSON SceneAsset）
```
（完整面=生命周期回调调度/组件注册/Transform 获取/渲染委托/事件桥六大块——签名在 M2 绑定阶段定稿。）

**分层测试**：C++ 侧 ms_test 单测（Core 级）；C# 侧绑定集成测试（生命周期顺序断言=Unity 序）；双层同为验收标准。

---

## 4. 里程碑 M0-M4

| 里程碑 | 内容 | 验收 |
|---|---|---|
| **M0 骨架** | CMake 双链+命名空间骨架+GameObject/Component/Transform/Scene 树+生命周期八回调+LifecycleDriver+EventBus+Time+ms_test 断言（生命周期顺序/树层级/矩阵正确）+控制台 run（无窗） | ms_test 全绿（生命周期顺序断言=Unity 顺序）；双链（MSVC+Clang）构建 |
| **M1 平台** | Win32 窗口+输入（键鼠/触摸接口）+软渲后端（v1 端口：9 原语+letterbox/DPI）+音频后端（WinMM 端口+AudioClock 采样时钟） | 窗口冒烟（软渲画面非空+输入日志）；PerfGate=帧耗/分配基线（0B 热路径目标）；Parity=与 v1 软渲像素对照（抽帧） |
| **M2 资产与序列化** | JSON 序列化（快速单头 parse=零第三方）+SceneAsset/PrefabAsset+AssetStore（寻址 Load<T>+guid）+反射注册表（MILESTONE_REFLECT） | 序列化 round-trip 断言（Transform 层级+组件字段）；SceneAsset 加载→场景一致；反射枚举字段断言 |
| **M3 编辑器** | 六区（Hierarchy/Scene/Inspector/Console/Project/Toolbar——MA7 术语）+Play/Stop+Pause+保存场景+新建/AddComponent（ImGui 或自绘 V3-5）+资产浏览 | 走查 8 项（快捷键/菜单/布局记忆）；M2 资产在 Editor 可开；退出干净 |
| **M4 域库+示例** | Rhythm.*（JudgementProfile/Ruleset 四族/StageSpec/Storyboard/Practice——端口 v1 域库，零托管化）+cookbook A/B/C（4K/Phigros/Arcaea 原生实现）+CLI --preset/--preview | 域库 10 预设 100% 命中（端口断言）；cookbook 编译+运行；--preset acc=1.000；--preview 四族可视 |

**阶段顺序理由**：M0（语义地基）→M1（可玩窗口=可见反馈）→M2（资产=可编写游戏）→**M2.5 绑定层（C ABI+C# P/Invoke 适配——双层结构核心；开发者自此只写 C#）**→M3（编辑器=原汁原味可见形态+C# 扩展脚本）→M4（域库=差异化落位+C# 绑定）+示例闭环。

---

## 5. 游戏层对接路线（决策点）

| 方案 | 说明 | 成本 | 建议 |
|---|---|---|---|
| A 并行（推荐） | C# 游戏（Milestone.exe）保持 examples 运行（v1 SDK 不动）；v3 独立演进；两者未来经 **C ABI**（M2 后：Core C ABI：CreateScene/Tick/Render/Input——v3 作为原生库供任何宿主）互通 | 最低 | **推荐**（用户「游戏=examples」已定；短期无互操作需求；长期 C ABI 收口） |
| B 立即 C ABI | v3 起就导出 C ABI（游戏侧不迁） | 中（A/B 均可后置） | 若 captain 想早互通 |
| C 迁移游戏 | C# 游戏重写进 v3 | 高（31.8K 托管→原生；数月） | **不做**（examples 不随引擎重写；作死） |

**结论建议**：A（并行+C ABI 后置）；游戏=v3 的「对照 examples」（v1 验证链保留作 v3 语义对照——M1 parity 抽帧即用 v1 参考）。

---

## 6. 原汁原味对照（v3 原生实现主流语义）

| 主流语义（Unity/Godot） | v3 | 对照 |
|---|---|---|
| MonoBehaviour 生命周期 | Component 八回调+LifecycleDriver | ✅ 顺序断言=Unity 官方 |
| Transform.position/rotation/localScale/parent | Transform（双精度 Vec3/Quat——long→precision 保持） | ✅（命名=Unity 短名——v3 全新代码直接正名，无需别名） |
| GameObject.AddComponent/GetComponent/SetActive/Destroy | 同（Destroy=延迟帧尾；SetActive 门控生命周期） | ✅ |
| SceneManager.LoadScene(name) | SceneManager.LoadScene(AssetId)+transition（保留差异化转场） | ✅ |
| Time.deltaTime/timeScale | TimeSingleton | ✅ |
| Input.GetAxis/GetKeyDown | Input.ActionMap（Axis/Button/Key）+触摸/命中区 | ✅ |
| Resources.Load<T> | AssetStore.Load<T>(path)（JSON 资产） | ✅ |
| Prefab.Instantiate | PrefabAsset+Instantiate（模板+覆盖） | ✅ |
| UI Canvas/UGUI | 组件式 UI 树（自绘）——或 ImGui（编辑器） | ✅ |
| 物理/着色器 | **范围外**（声明含文档——t92 不做项延续） | 范围外 |
| 差异化（保留宣示） | SceneTransition 5 式/多场 StageSpec/Storyboard 钩子域/判定档热换/JobSystem 0B | **域库层差异化**（v3 延续——t88/92 差异化表） |

---

## 7. 决策点 V3-1..V3-8（供 captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| V3-1 | 重写确认 | 用户已拍板 C++ 完全重写——确认（本规格=执行基线；**v1/v2 引擎库+Editor 建设=封存参考**（特性源），不再演进） |
| V3-2 | 语言标准 | C++20（固定） |
| V3-3 | 一期平台 | Windows 优先（Win32+软渲+D3D11+WinMM）；GLFW/Vulkan/ALSA=二期接口（声明） |
| V3-4 | 渲染后端 | 一期=软渲（v1 端口，最快可见）+D3D11（原生，P1）；Vulkan=二期 |
| V3-5 | 编辑器 UI | **自绘（C12 端口=零第三方红线全保持）**为首选；ImGui=可选（若 captain 同意"唯一第三方=Editor UI 单头库"→主流体验更快）——**推荐自绘（红线一致）**，ImGui 列为 A/B |
| V3-6 | 域名层 | Rhythm.* 可插拔（核心零引用断言；M4 落地）——**保留**（差异化=域库特性） |
| V3-7 | 测试框架 | 自研 ms_test.hpp（零第三方）——固定 |
| V3-8 | 游戏层 | 并行（A）+C ABI（M2 后可选）——推荐 |
| V3-9（V3-B） | **双层绑定方案** | **C ABI + C# P/Invoke 适配层（推荐）**——跨平台/零托管依赖/两层测试分离（§3.7 方案 A；C++/CLI=不推荐：MSVC 专用锁跨平台）；绑定面=生命周期回调调度/组件注册/Transform 获取/渲染委托/事件桥六大块（M2 阶段定稿签名） |
| V3-10（V3-B2） | 域库语言位 | 域库本体 C++（性能）+C# 代理绑定（开发者用 C# 调 Rhythm.*）——**推荐**（性能与公开面分离）；或域库全 C#（慢路径） |

---

## 8. 风险与诚实声明

1. **重写成本**：v3 全量=6-12 个月（M0-M4）；与 C# 结论（t78）的差异=**用户最新指令覆盖**——本规格忠实执行，但**决策点 V3-1 让 captain/用户是否知情确认**（成本/收益：原汁原味+零 GC+跨平台原生 vs 47.8K 托管资产转 examples 维护）。
2. **特性迁移完整性**：13 项特性=以 v1 源码为 spec（feature extraction 文档=本表+M4 端口断言（10 预设 100% 命中/抽帧 parity）——防"原汁原味"流失）。
3. **零第三方红线**：核心/Platform/Domain 零第三方；Editor 自绘=零第三方全保（推荐）。
4. **Examples 保留**：C# 游戏不迁（V3-8 A）——v1 验证链（EngineChecks/试玩）归 v3 语义对照参考（不复制）。

---

*终稿*（t107 设计交付；特性提炼/14 项保留-舍弃-改造 + C++20 栈 + 五层架构 + M0-M4 + 对接路线 + 原汁原味对照 + V3-1..V3-8；未写代码；等待 captain 拍板（尤其 V3-1/V3-5）后派 eng-coder-vis 项目化）
