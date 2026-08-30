# Milestone 引擎定位纠正与语言权衡设计（engine-language-tradeoff）

> 起草：eng-design-vis（引擎策划）· 任务 t78（v1.2：批A 状态标注——**批 A=已实施（t81 完成并评审通过）**；批 B=暂缓（D4：待批A 验收+用户确认）；批 C=P2（D5-D8））
> 用户重大指令：① 引擎团队制作的是【游戏引擎】不是游戏程序——引擎=可通过代码对游戏进行编写的工具（可编程游戏引擎 SDK），不是谱面播放器（播放器=示例应用/演示层，不主导引擎定义）；② 经权衡后选择最适合的编程语言进行重构。
> 产出：语言权衡表（8 维）+ 引擎定位纠正设计（SDK 主线/示例分层/差距清单/分批改造）+ 实施分批建议。只做设计不写代码。
> 盘点基准：t78 时刻（四目录迁移后：引擎源码/、游戏源码/、编辑指南/、输出产物/）。

---

## 0. 结论先行

1. **语言结论：继续 C#（net8.0，零 NuGet/零 WinForms/零 System.Drawing 红线不变）**——硬理由=保留 47.8K 已验证 C# 资产（总资产 120K+ 含备份/快照）与 t57-t73 全链验收 + 热路径性能已达标（判定 0B 分配/帧 P99 1.1ms/240Hz 1500FPS/压测 12000FPS 级）+ 单文件发布已成；短板应对见 §2.3（AOT 削启动/原生绑定 FFI/游戏循环调谐）。
2. **定位结论：SDK 主线**——公开 API（GameEngine 门面/Scene/GameObject/Component/Ruleset/渲染窗/音频/输入/生命周期/Assets 管线）+ engine-cookbook（5-10 个完整示例：用代码写 4K 下落/Phigros 线场/Arcaea 场景，每一行都是引擎 API）+ 模板工程 + 引擎文档大纲 = 引擎本体；**EnginePresets 10 预设/工具窗 ToolApp/CLI/PublishPlayer 播放器/Milestone.exe 游戏（31.8K）= examples/应用层（显式标注，不主导引擎定义）**；分批改造（批 A/B/C）**不破坏 t57-t73 与游戏层**（public API 化渐进）。

---

## 1. 现状盘点（t78 实测）

| 项 | 实测 | 说明 |
|---|---|---|
| 引擎库（MilestoneEngine.csproj，net8.0 纯托管零依赖） | 核心 60 文件 **11,562 行**；Samples 3,386；Tests 199；Hosts（EnginePlay/PublishPlayer/HelloEngine）862；合计 **16,009 行** | 零 NuGet/零 WinForms/零 System.Drawing；Windows 专有=#if WINDOWS P/Invoke（HardwareProbe 先例） |
| 游戏层（游戏源码/Milestone.csproj，WinForms+D2D） | 67 文件 **31,781 行** | 历史先行者：先有游戏后有引擎；namespace ChartPlayer ×64 |
| 全仓可维护 C# | ≈**47.8K 行**（游戏 31.8K + 引擎 16K）；**用户口径 120K+ 行**=含 输出产物/迁移快照/obj 备份（不可维护资产） | 本文以 47.8K 可维护为权衡基准；120K+ 为总资产 |
| 命名空间 | 引擎全部核心文件 =**单一 namespace ChartPlayer**（游戏层同） | SDK 命名空间治理起点 |
| 公开面 | 核心内部符号仅 **1 个**（几乎全公开）——但**无治理**：无 PublicAPI 清单/无 XML doc 全量/无 API 版本策略 | API 面已具雏形，缺门禁 |
| 平台/渲染 | EngineApp.Run(IEngineAppHost)/IEngineWindow/IEngineRenderer（9 原语）/SoftwareRenderer/ViewportPolicy/IUiDraw | App/渲染后端已抽象（D2D/GDI 适配器在游戏层=需归位引擎插件层） |
| 音游域 | Ruleset 四族/RulesetFactory/ChartData/JudgementProfile/EngineTime/AudioClock/Storyboard/PracticeSession/JudgementLog | 领域库已完整（可作引擎自带样板域库） |
| 工具/示例 | EnginePresets 10 预设/ToolApp 4 页/CLI 12 命令/EnginePlay/PublishPlayer pack | **t57-t73 已交付——定位=examples 层** |
| 文档 | README（能力矩阵/快速上手/测试矩阵）+ 协作文档群 | SDK 文档缺失（cookbook/API 参考/模板说明） |
| 验证链 | EngineChecks（DemoRunner 全量断言）+ 压测矩阵 + 试玩/引擎使用者视觉与输入验证 | 迁移安全网 |

---

## 2. 语言权衡表（8 维）

> 评分：★1=差 ★5=优。目标语境：音乐游戏引擎+通用游戏能力；零依赖桌面优先；与 47.8K 可维护 C# 资产及 10 模式游戏层共存。

| 维度 | **C# .NET 8（现状）** | C++20 | Rust | TypeScript | Kotlin (JVM) | Go | **Lua + C（脚本+C 核）** |
|---|---|---|---|---|---|---|---|
| 1 性能 | ★4（JIT 近原生；热路径已验证：判定 0B/次、软渲帧 P99 1.1ms、240Hz 1500FPS、压测 12000FPS 级；NativeAOT 可再压 10-20%） | ★5（无 JIT/零 GC） | ★5（无 GC 运行时） | ★2（V8 解释/JIT；软渲/判定 10ms 级不可行） | ★3（JVM JIT+GC，冷启动/内存基线差） | ★3（GC 停顿+反射弱） | ★4（C 核性能顶格+LuaJIT 脚本层快；但仍需 C 核重写） |
| 2 生态 | ★5（NuGet 巨量；零依赖红线=自主性；Unity/Godot C# 参照系=游戏/音游生态天然契合） | ★3（库多但构建/ABI/依赖碎片化：CMake/vcpkg/conan） | ★4（cargo 一流；游戏/音游域库稀少） | ★2（前端强、桌面/原生弱） | ★4（JVM 成熟） | ★3（云/网络强、游戏域弱） | ★3（Love2D/Defold 参照系=嵌入脚本好，但独立游戏引擎生态小） |
| 3 跨平台 | ★4（net8.0 纯托管：win/linux/macOS 预研已通；WASM（future）；NativeAOT） | ★4（各平台原生；构建矩阵成本高） | ★5（交叉编译一流） | ★4（Node/Web 即跑；桌面壳重） | ★3（JVM 好；桌面打包差） | ★4（交叉编译好；GUI/音频弱） | ★4（C 全平台+Lua 可移植） |
| 4 工具链 | ★5（VS/Rider/dotnet CLI/单文件发布/调试器一等；零包引用） | ★3（构建/包管理三套并存） | ★4（cargo/rust-analyzer 好） | ★4（npm/TS 成熟；桌面受限） | ★4（IntelliJ 一流；Gradle 重） | ★4（go tool 极简） | ★3（C 构建碎片+Lua 生态两套） |
| 5 学习成本 | ★5（团队已在；新手快） | ★2（内存模型/模板/ABI 难） | ★2-3（所有权模型陡；unsafe 窗口） | ★4（易；渐进） | ★4（易） | ★4（易） | ★3（脚本层易、C 层难；双语言=双学习面） |
| 6 长期维护 | ★5（零迁移——唯一无重写路径；GC 免手动；工具链 10 年+） | ★2（重写 47.8K+双轨翻倍；手工内存隐患） | ★2-3（重写 47.8K；语义化迁移风险高；双轨最长） | ★3（重写中；热路径不成立=非主语言位） | ★2-3（重写+JVM 基线；JNI 桥） | ★2（重写+领域库缺席） | ★2-3（C 核重写+Lua 层双轨；脚本边界=长期维护面大） |
| 7 嵌入性 | ★4（NativeAOT C ABI；Runtime hosting；COM） | ★5（原生一等） | ★5（cdylib 一等） | ★2（需 host） | ★3（JNI） | ★4（c-shared） | ★5（脚本嵌入=此类组合的天职；但那是「供他人嵌引擎」而非引擎本体） |
| 8 与现有资产（47.8K C#/t57-t73 验收链） | ★5（0 迁移；EngineChecks 全部为回归闸） | ★1（全量重写） | ★1（全量重写） | ★1-2（重写且能力不符） | ★1-2（重写+桥） | ★1（重写） | ★1-2（C 核全量重写+脚本化双轨） |
| **合计/结论** | **★4.7（唯一全维 ≥4；唯一 0 迁移）** | ★3.4（性能顶格但维护/资产代价巨大） | ★3.4（性能/安全好但资产代价最大） | ★2.8（能力不符主语言位） | ★3.0（无优势项） | ★3.0（领域缺席） | ★3.1（脚本嵌入天职≠引擎本体；C 核重写代价=同 C++） |

### 2.1 结论与理由（选 C# 给硬理由）

**选择 = C#（net8.0）**，理由按权重排序：
1. **资产与验收链**：47.8K 已验证 C#（10 模式游戏层 31.8K+引擎 16K、t57-t73 全链验收与压测基线）——唯一零迁移路径；任何替换=6-12 月重写+双轨+验收链重做，而「重写求性能」的前提（性能不足）**不成立**（热路径余量：判定 0B 分配/帧 P99 1.1ms/240Hz 1500FPS/压测 12000FPS 级/60 分钟长稳无泄漏——**已被验证的游戏循环调谐**）。
2. **零依赖红线=差异化引擎定位**：无 NuGet/无 WinForms/无 System.Drawing 的纯托管引擎可嵌入任意宿主 + NativeAOT 后单文件 20-30MB 级——「可编程游戏引擎」的工程学基础。
3. **工具链与长期维护**：VS/Rider/dotnet CLI/单文件发布/调试器=开发者成本最低；C# 是游戏引擎主流语言（Unity/Godot C#/MonoGame 参照系）。
4. **跨平台可进可退**：net8.0 纯托管（win/linux/macOS 预研）+ WASM（future）+ NativeAOT。
5. **嵌入性现成**：NativeAOT C ABI + Runtime hosting——未来宿主嵌入场景（P2 落地）。

**不选 C++/Rust/Lua+C 的硬理由**：三者性能优势在当前热路径**无收益**（已被 t9/t11 压测验证为「有余量」），但代价=全量重写（C++/Rust/Lua+C 的 C 核同额）+双轨+验收链重做；Lua+C 的「脚本嵌入」收益可在 C# 侧以（Roslyn 脚本/内嵌 DSL）+（未来）宿主嵌入 C ABI 取得，**无需牺牲引擎本体语言**。**不选 TS/Kotlin/Go**：性能/桌面/领域能力不符主语言位（TS 可作编辑器 UI 示例层工具语言，非引擎主语言）。

### 2.2 打分差异说明（诚实）
- C# 性能 ★4：JIT 冷启动/尾延迟略高于原生；应对=NativeAOT profile/ReadyToRun/热路径 struct/池化纪律（0B 红线已有）。
- Rust/Lua+C 的跨平台/嵌入 ★5 面向「从零新引擎」；对「现有 47.8K」语境被资产列拖垮——本文=资产继承优先。

### 2.3 短板应对（选 C# 后的明确对策）
| 短板 | 应对 | 批 |
|---|---|---|
| 冷启动/单文件 67MB | **AOT 削启动**：NativeAOT 或 ReadyToRun+裁剪发布 profile（目标 20-30MB；JIT 版默认，AOT 可选） | 批 C（P2） |
| 原生宿主嵌入 | **原生绑定 FFI**：MilestoneEngine.Native 小层（NativeAOT 导出 C ABI：Start/Tick/Render/Input）；EnginePlay 作验证宿主 | 批 C（P2） |
| GC 暂停/热路径 | **游戏循环调谐**（已证明 12000FPS/零分配热路径）+ 未来局部 span/unsafe + 音游域纯数据结构 | 持续（已有 t9 验收） |
| 脚本化/热更需求 | 内嵌 DSL/Roslyn 脚本面（示例层工具语言可选 TS）——不换引擎主语言 | 批 C（P3） |
| 移动/Web | 桌面优先；跨平台基线已通；WASM 预研记录（P3） | 批 C（P3） |
| 与旧命名空间 | vNext 主版本一次性切换 + 全局 using 兼容（见 §3.5 批 B） | 批 B |
### 2.4 语言结论一句话
**继续 C# net8.0（零依赖）——不重写；以 SDK/文档/示例分层完成「游戏引擎」定位纠偏；以 AOT/FFI/循环调谐补短板。**

---

## 3. 引擎定位纠正设计（SDK 主线）

### 3.1 分层模型（定位金字塔）

| 层 | 内容 | 位置 | 定位表述 |
|---|---|---|---|
| L0 引擎核心（SDK） | 通用游戏能力：GameEngine 门面/App/Scene/GameObject/Component/Transform/Render 窗/Audio/Input/Time/Tween/Particles/Camera3D/Assets 管线（engine-cookbook 全用此层 API） | 引擎源码/engine（核心 60 文件） | **引擎本体** |
| L0.5 领域域库（内置样板） | 音游域：Ruleset 四族/RulesetFactory/ChartData/JudgementProfile/JudgementTracker/Storyboard/PracticeSession/JudgementLog/AudioClock | 引擎源码/engine（Rhythm* 类） | 引擎自带领域库（引擎本体不依赖；文档明示「游戏领域样板」） |
| L1 渲染后端插件 | SoftwareRenderer（引擎内）/D2D/GDI 适配器（现游戏层）→ 收编为 MilestoneEngine.Platform.*（#if WINDOWS 或宿主注入） | 引擎+插件 | Renderer 可插拔 |
| L2 示例应用层 | EnginePresets 10 预设/工具窗 ToolApp/CLI/EnginePlay 演示/PublishPlayer 播放器/pack 链路 | 引擎源码/engine/{Tool,EnginePlay,PublishPlayer,Samples}+文档标注 | **examples——不定义引擎** |
| L3 游戏（产品） | Milestone.exe（31.8K，10 模式+编辑器+联机） | 游戏源码/ | 「用引擎写的大示例」（历史先行者；文档定位 examples/milestone-game） |

显式化动作：README 顶部定调「**Milestone 引擎=可编程游戏引擎；内置音游域库与全部演示（预设/工具窗/CLI/打包器/示例游戏）=示例应用（examples/）**」；Tool/EnginePlay/PublishPlayer/Samples 归示例注释块（不物理迁移=不破坏构建链，文档+csproj 注释双标注）。

### 3.2 SDK 主线 API 面（GameEngine 门面+示例草案）

~~~csharp
// ── engine-cookbook·示例 A：用代码写一个 4K 下落游戏（每一行都是引擎 API）──
var engine = new GameEngine { Title = "My 4K", Width = 1280, Height = 720 };
engine.EnableAudio(new MciAudioBackend());                 // t65 已实现
engine.LoadScene(new LaneGameScene());                     // 玩家场景
engine.Run();                                              // 生命周期：Init/Tick/Render/Close

public sealed class LaneGameScene : Scene
{
    GameObject _board; RulesetAnchor _anchor;
    public override void Build()
    {
        _board = Root.Add(new GameObject());               // 场景对象
        _anchor = _board.Add(new RulesetAnchor());         // 域库锚点（音游样板）
        _anchor.Setup(LaneRuleset(4), ChartData.From(MyNotes), JudgementProfile.OsuMania(8));
        _board.Add(new UiCanvas(Screen.Width, Screen.Height));  // UI 层
    }
    public override void Update(double dt) { _anchor.Tick(dt); }       // 判定/音游时钟
    public override bool Render(IEngineRenderer r)
    {
        _anchor.DrawLanes(r);                              // 轨道/音符/判定线（域库提供）
        return true;
    }
}
~~~
**示例规范**：每个 cookbook 示例=单一文件可复制运行；**只引用 MilestoneEngine**（零游戏层引用）；每行标注所用 API 域（Scene/GameObject/Component/Ruleset/Render/Audio/Input）。

**engine-cookbook 完整示例规划（8 篇，其中 3 篇=用户点名）**：
| # | 示例 | 展示 API |
|---|---|---|
| A | **程序化 4K 下落游戏**（用户点名；~120 行：轨道/音符/判定线/键输入/打分） | GameEngine 门面/Scene/GameObject/Component/RulesetAnchor/Input/音频 |
| B | **Phigros 线场**（用户点名；动态判定线 moveY/rotate+线点击+圆点音符） | LineRuleset/PlayfieldLine/TimeLine/触摸输入/UI 判定层 |
| C | **Arcaea 场景**（用户点名；天地双轨近似+ScrollDir 视觉+ARC 时序） | LaneRuleset(6)+ScrollDir/Storyboard(ResetTo)/UI 组合 |
| D | 最小游戏（白盒；模板工程同源） | 全部 |
| E | 自定义判定档与计分 | JudgementProfile/ScoreBoard |
| F | UI 面板/主题/转场 | UiCanvas/UiTheme/SceneTransition |
| G | 故事板+练习模式 | Storyboard/PracticeSession |
| H | 嵌入宿主（AOT+Native C ABI） | 批 C 后补 |

**公开 API 补位清单（现有已公开→SDK 目标）**：GameEngine 门面（包装 EngineApp）+IScene/Scene 基类+RulesetAnchor 场景约定+Assets 管线（纹理/音频加载统一入口——现缺失，缺口 G7 外新增 G8 Assets 管线）；渲染/音频/UI/音游域同前表。

### 3.3 模板工程 + 文档大纲
1. **模板工程**：dotnet new milestone-engine（白盒最小游戏=D 同源；csproj 引用 MilestoneEngine+构建/运行脚本；EngineChecks 断言=模板可编译可运行）。
2. **引擎文档大纲**：①README 定位段（引擎=SDK；examples 列表）②API 参考（XML doc+PublicAPI.Shipped.txt 门禁）③cookbook 8 篇（上述）④开发者指南（新玩法三步/嵌入宿主/发布 AOT）⑤examples 目录说明。

### 3.4 差距清单（未公开 API 面/命名空间/Assembly 边界/示例/文档）
| # | 差距 | 现状 | 目标 | 批 |
|---|---|---|---|---|
| G1 | 入口门面缺失 | 无 GameEngine/IScene（仅 IEngineAppHost 底层） | 补 GameEngine 门面+Scene 基类+Builder | A |
| G2 | Assembly 无公开面边界 | 全公开（内部符号 1 个）无门禁 | PublicAPI.Shipped.txt+XML doc 门禁（CI 断言） | A |
| G3 | 命名空间单一 | 引擎+游戏=ChartPlayer | MilestoneEngine.*（一次性切换+using 兼容） | B |
| G4 | 示例未分层 | Samples+Tool+Hosts 混入引擎树 | examples 显式层（注释+README 表） | B |
| G5 | SDK 文档缺失 | 无 cookbook/模板/API 参考 | cookbook 8 篇+模板+文档大纲 | A+B |
| G6 | 渲染后端在游戏层 | D2D/GDI 在游戏层 | 收编 MilestoneEngine.Platform.（插件化；核心零依赖不变） | C（P2） |
| G7 | 玩法模板化入口弱 | RulesetFactory 强 | 「新玩法三步」文档化（cookbook ⑨→开发者指南） | A |
| G8 | Assets 管线缺失 | 纹理/音频加载散落游戏层 | 引擎统一 Assets API（LoadTexture/LoadAudio/Stream） | A（骨架） |
### 3.5 分批改造方案（核心原则：不破坏 t57-t73 与游戏层，public API 化渐进）

**批 A（零行为变更，新增面，~1-2 周）**
1. 新增 MilestoneEngine.App.GameEngine（门面，包装现有 EngineApp.Run）+ Scene 基类/IScene（桥接 IEngineAppHost——宿主不变）；
2. PublicAPI.Shipped.txt 生成+XML doc 门禁（EngineChecks 断言「API 清单不漂移」可禁）；
3. cookbook 示例 A/B/C/D/E/F 六篇+模板工程 templates/milestone-engine+dotnet new 验证断言；
4. Assets 管线骨架（LoadTexture/LoadAudio 统一入口，底层复用现有）P0 面（G8 骨架）。
验收：示例可编译/模板可跑/EngineChecks exit=0/PublicAPI 清单存在；**现有代码零改动**。

**批 B（命名空间与分层，~3-4 周，一次 release 窗口）**
1. codemod：ChartPlayer→MilestoneEngine.*（分域；using 全局替换；迁移脚本+回归闸）；
2. examples 层显式化（README 表+csproj 注释块+XML doc [Example]）；
3. 回归闸：EngineChecks 全量+游戏层 0 警告+试玩视看抽查（主菜单/编辑器/游玩）。
风险：外部引用 ChartPlayer=README 记「vNext 主版本一次性 break」；内置由脚本覆盖。

**批 C（赋能，P2，不阻塞）**：AOT profile（20-30MB）+Native C ABI FFI 层（EnginePlay 验证）+渲染后端插件化（G6）+DSL/脚本面（P3）+WASM 预研（P3）。

### 3.6 「播放器只是示例应用」显式化清单
- [ ] README 顶部定位段+examples 应用列表
- [ ] Tool/EnginePlay/PublishPlayer/Samples 归 examples 注释块
- [ ] docs/engine-sdk.md（SDK 主线+examples 边界）
- [ ] EnginePresets 文档注明「示例应用之一（预设演示）」
- [ ] 游戏层 README 定位「示例产品」
- [ ] cookbook A/B/C 展示「新玩法=写代码，不是建播放器」

---

## 4. 给 eng-coder-vis 的实施分批建议（C 部分）

| 批 | 建议任务标题（captain 可照抄建单） | 交付 | 验收 | 依赖 |
|---|---|---|---|---|
| 批 A（建议即派） | 「引擎 SDK 批 A：GameEngine 门面+Scene 桥接+PublicAPI 门禁+cookbook A-F+模板工程」 | 新增 EngineApplication/GameEngine+Scene 基类；PublicAPI.Shipped.txt；cookbook 6 篇；templates/milestone-engine；EngineChecks 新增 3 断言（API 清单不漂移/模板编译/示例编译） | EngineChecks exit=0+示例可编译+dotnet new 可跑；现有代码零改动 | t78 设计 |
| 批 B（captain 排期） | 「引擎 SDK 批 B：命名空间 MilestoneEngine.*+examples 层显式化+回归闸」 | codemod 脚本+全部 csproj/using 更新；README 定位段；[Example] 标注 | 全链 EngineChecks+游戏层 0 警告+试玩视看 3 抽查 | 批 A |
| 批 C（后置） | 「引擎 SDK 批 C：AOT profile+Native C ABI+后端插件化」 | AOT 发布 20-30MB；Native 层 C ABI 样本；D2D/GDI 收编 | EnginePlay 嵌入验证+AOT 单文件冒烟 | 批 B |
| 每批交接 | 实现报告：改动清单+验证命令+与 t57-t73 回归对照表 |||||

---

## 5. 风险与验收
**风险**：①命名空间迁移波及游戏层 67 文件（codemod+回归闸；1 release 窗口）②示例降级误伤（注明=dogfooding 证明非废物）③零依赖红线在插件化下稀释（插件 Plan B 层，#if WINDOWS/宿主注入）④cookbook 漂移（EngineChecks 断言防）。
**验收**：§0 两结论被 captain 采纳；批 A 后=示例/模板/门禁全绿；批 B 后=命名空间切换全链绿+README 定位段；批 C 后=AOT 20-30MB+C ABI 样本。

---

## 6. 关键决策点（供 captain 拍板）
| # | 决策 | 选择 |
|---|---|---|
| D1 | 语言 | **继续 C# net8.0 零依赖（不重写）**（硬理由 §2.1；AOT/FFI/循环调谐补短板） |
| D2 | 定位 | SDK 主线（GameEngine 门面/Scene+cookbook 8 篇+模板+文档大纲） |
| D3 | 现有工具/预设/播放器/游戏 | **examples 层**（显式标注，不物理迁移不删） |
| D4 | 命名空间 | 批 B 一次性切换 MilestoneEngine.*（+using 兼容+README 记 break） |
| D5 | 渲染后端插件化 | 批 C（P2；核心零依赖不变） |
| D6 | NativeAOT/原生绑定 FFI | 批 C（P2；不阻塞主线） |
| D7 | 脚本面 | 内嵌 DSL/Roslyn（P3）；TS 仅示例层工具语言（不换引擎主语言） |
| D8 | 与 t57-t73 | 全部新增/文档层；零行为破坏（EngineChecks 回归闸） |

---

*终稿 v1.1*（t78 设计交付；语言=继续 C#；SDK 主线；批 A 建议即派 eng-coder-vis，批 B/C 随 captain）
