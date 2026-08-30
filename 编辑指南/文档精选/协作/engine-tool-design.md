# 引擎工具化 + 模式预设 + 引擎内游戏预览 综合设计（engine-tool-design，供引擎编写分批执行）

> 起草：eng-design-vis（引擎策划）· 任务 t54 · 用户三条新指令：
> ① 开发问题/阻碍交引擎团队（功能正常 + 引擎打包成人类可使用的工具）
> ② 引擎对各音游模式做好预设帮助开发
> ③ 引擎内支持游戏预览、后续开发在引擎中进行
> 依据：本轮源码盘点（引擎/engine/*、EnginePlay、PublishPlayer、PackExporter、EngineUiDemoForm、EngineMainShell 承载 GamePanel/Editor）+ 各协作文档（t3/t53/t55 衔接）。
> 产出：设计文档 + 实施分批（给 eng-coder-vis）。**本文件只做设计，不写代码。**
> 与 t55（引擎使用者盘点，engine-user-audit.md 产出中）互补：本设计为「引擎侧怎么建」，t55 为「人类视角缺什么」，实施时以本设计为主干，合并 t55 缺口。

---

## 0. 目标与总原则

### 0.1 三条指令 → 三个交付物

| 指令 | 交付物 | 完成判据 |
|---|---|---|
| ① 引擎打包成人类可使用的工具 | **MilestoneEngine.exe = 引擎工具窗**（启动 UI/模式预设/打开谱面/预览游玩/导出打包/内置示例谱/帮助） | 无参数双击 → 有 UI 的工具界面；可完成「选预设→生成示例谱→预览→导出」最小闭环 |
| ② 各音游模式预设 | **EnginePresets 注册表**（引擎层，10 预设：Mania4/6/8K/Phigros/Arcaea/Cytus/osu/IIDX/maimai/ADOFAI）+ 一键加载 API + 引擎内 UI + 文档 | 一个 API 拿到任意模式的 Ruleset+谱面+判定档+键位+布局+背景令牌；引擎内一键生成并预览 |
| ③ 引擎内游戏预览、后续开发在引擎中 | **EnginePreviewHost + RulesetRenderer**（引擎场景内预览组件）+ 开发工作流 | 引擎窗内：选模式→预览自动游玩（可视、可手动按键）；后续玩法开发在引擎库+引擎工具内闭环（写 Ruleset → 工具预览 → 导出），不再依赖 Milestone.exe |

### 0.2 总原则

1. **引擎库零依赖约束不变**：MilestoneEngine.csproj（net8.0，零 NuGet/零 WinForms/零 System.Drawing）。所有新增引擎能力必须纯 C# + P/Invoke（Windows 宿主运行时才调用，非 Windows 构建零 Windows API——参照 HardwareProbe 的 #if WINDOWS 先例）。
2. **不重复造 UI**：工具窗 UI 直接复用引擎既有 U 组件（UiPanel/UiButton/UiLabel/UiStackLayout/UiTabBar/UiCanvas/SceneManager/SceneTransition，已过 UiComponentsChecks）。缺的只是「引擎窗口上的一条 IUiDraw 实现 + 文本」。
3. **不重复造玩法渲染**：预览渲染基于既有 Ruleset/LaneField/LineField/RingField/PathField + TimeDepthMapper 做「引擎原生四族渲染器」（Lane/Line/Ring/Path），游戏层 GamePanel 的 10 模式精细渲染**不回迁**（保留在游戏层，属表现层）。
4. **分批可验收、每批不破坏既有**：EngineChecks 全绿基线不回退；Milestone.exe 侧零改动（除可选衔接项）；每批给 captain 构建 + 试玩验证点。
5. **先盘点、后设计、再实施**：§1 现状盘点必须与实施时源码一致（本轮为 t54 时刻快照，行号=盘点时点，实施时以文件/接口名为准）。

---

## 1. 现状盘点（引擎侧 + 相关游戏层，t54 时刻）

### 1.1 引擎库（引擎/engine/ → MilestoneEngine.csproj，net8.0 纯托管零依赖）

| 模块 | 文件 | 能力（与本次相关的部分） |
|---|---|---|
| 时间 | EngineTime.cs | EngineTime（TimeScale/Pause）、EngineStopwatch |
| 音游核心 | RhythmCore.cs | JudgeTier/JudgementProfile（16+ 预设：Arcaea/Phigros/IIDX/Cytus/ADOFAI(osuMania OD)/osuStandard/SDVX/maimai/CHUNITHM/太鼓/Cytus2/GC/Lanota/Dynamix + WindowMultiplier 乘数）、TimeDepthMapper、BeatMath、LaneField、RhythmNote/SliderNote/RingNote/SpinNote/KnobNote |
| 判定 | JudgementTracker.cs、JudgementLog.cs、PracticeSession.cs、ScoreBoard.cs | 档位热换 SetProfile、判定日志（滚动 200/直方图/热图）、练习模式（段循环/幽灵）、计分板 |
| 谱面模型 | ChartModel.cs | ChartData、BpmTimeline（变速）、RhythmNote 列 |
| 玩法族 | Ruleset.cs、RulesetFactory.cs | 四大玩法族 Ruleset（Lane/Line/Ring/Path）+ RulesetDescriptor（6 字段）+ RulesetFactory.Build/BuildChart/BuildProfile/BuildContext/RunHeadless —— 预设与预览的直接基础 |
| 起步谱 | StarterChartGenerator.cs | onset 序列 → ChartData（Lane/Phigros/Cytus/Ring/Path 五模适配、hold 检测、ChartValidator 0 Error 自断言） |
| 输入 | InputCore.cs | KeyInput/TouchInput/HitZone/InputMapper（键码→列/方位/线） |
| 音频时钟 | AudioClock.cs | IEngineAudioBackend 接口 + Bind（t6 EN-3 已落）——尚无 Windows 实现 |
| 场景/动画 | Component/GameObject/Scene/SceneManager/SceneTransition/GameLoop/Transform/Tween/Particles/Engine3D/Cam3D | Unity 式运行时（转场 Fade/Slide/Wipe/CircleReveal/Beam） |
| 场景 UI | Ui/UiComponents.cs、Ui/LegacyControls.cs（UiTabBar/UiScroll 等）、Ui/UiTheme.cs | UiCanvas/UiPanel/UiLabel/UiButton/UiCard/UiStackLayout/UiGridLayout（Clip/Ellipsis/Wrap/AutoShrinkFont/测量缓存/InvokeClickAt）+ Theme Scale 令牌 —— 渲染只依赖 IUiDraw（10 方法） |
| 应用层 | App/ViewportPolicy.cs、App/EngineWindow.cs、App/EngineApp.cs、Platform/SoftwareRenderer.cs、Platform/EngineRenderApi.cs、Platform/EnginePlatform.cs | IEngineAppHost hook + EngineApp.Run(host, opts) + 引擎窗（Win32 P/Invoke）+ SoftwareRenderer（Frame uint[] 缓冲 + 图形原语）+ letterbox（ViewportPolicy）；IEngineRenderer 原语：Clear/FillRect/FillRoundedRect/DrawLine/FillCircle/DrawCircle/FillTriangle/FillQuad/Present（无文字原语——文本属宿主层） |
| 并行/压力 | EngineJobs.cs、StressCore.cs、StressEngine.cs | 并行 Map/Reduce、压测框架 |
| 其他 | Storyboard.cs（ResetTo/HookFired/StoryboardPlayer）、Dsp.cs（校准 DSP）、BeatAlignEngine.cs（onset 检测）、HardwareProbe.cs（#if WINDOWS 先例）、ChartValidator.cs、ParityCore.cs、PlatformSockets.cs | 周边能力齐备 |
| 示例 | Samples/EngineGame.cs（EngineGameHost：4K 自动演示 + EngineGameCheck 无头）、Samples/DemoRulesets.cs（Mania/Maimai/Phigros/ADOFAI 无头 Demo）、Samples/DemoRunner.cs（含 FakeAudioBackend + IUiDraw 桩 + App 断言）、Samples/TaikoDemo/CatchDemo/BlankRuleset/StoryChecks/UiComponentsChecks/LegacyControlsChecks | 引擎自证「能玩」的样板；DemoRulesets 明确标注「不含渲染，宿主把打印替换为 IRenderer 绘制即可成可玩游戏」——正是本设计的预览点 |

### 1.2 MilestoneEngine.exe（引擎/engine/EnginePlay/ → EnginePlay.csproj，AssemblyName=EnginePlay 构建后改名 MilestoneEngine.exe）

- 命令：--demo（默认，1280×720 引擎窗 4K 自动演示，F11/ESC）、--check（无头自检 4 组）、--bench、--play <pack> [--secs]（pack 单文件播放）。
- 现状结论：**是「演示/自检 exe」，不是「人类可用的工具」**——
  ❌ 无 UI（引擎窗只画图形，无文字——SoftwareRenderer 无文本）
  ❌ 不能打开用户谱面（只认 pack 内嵌载荷，不认 .mil/.osu）
  ❌ 无模式概念（EngineGameHost 写死 4K；PackPlayHost 写死 4K OsuMania(8)）
  ❌ 无音频（AudioClock 未 Bind，静音）
  ❌ 无导出（PackExporter 在游戏层 Milestone.exe 里）
  ❌ 无帮助/指南（帮助=csproj 注释）
- 发布：dotnet publish EnginePlay -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o 构建产物/engine-exe（csproj 注释），**当前 构建产物/engine-exe 为空**（从未发布过——captain 需跑一次）。

### 1.3 PublishPlayer（pack 单文件模板）+ PackExporter（游戏层）

- PublishPlayer.csproj：MilestonePlayer.exe 模板，引用 MilestoneEngine + App/Platform，读 exe 尾部 [payload][int64 len][8B magic MILSTPK1]，4K 下落自动+可按键，ESC/完播关闭。模板实产物 = **bin/Release/net8.0-windows/win-x64/pack-template.exe（已存在）**。
- PackExporter.cs（源码/CoreUtil/）：Milestone.exe --pack <谱面> [out <dir>] → ChartParser.ParseFile（全 10 模式）→ 归一化 4K 时间列 → 复制 pack-template.exe 尾部内嵌 → <曲名>.exe。
- 结论：**导出链路已存在且工作，但它住在游戏层**；引擎工具要「包导出」，需要引擎侧重复 ~90 行（或把 PackExporter 移入引擎——它只依赖 BCL + ChartParser，见 §3 D5 决策）。

### 1.4 引擎 UI 承载层（游戏层，可复用结论）

- **EngineMainShell**（源码/Play/EngineUi/EngineMainShell.cs，1888 行）：WinForms Control，引擎 UiCanvas 页面（menu/songs/settings/SecondaryPages 8 页：曲库/校准/段位/玩家信息/我的数据/皮肤/回放/关于）+ 顶栏 + 转场缓存；HostContent(Control) **承载 GamePanel（游玩）与 ChartEditorPanel（编辑器）**（t50：同窗承载，可见窗口数=1）；扫描曲目 ScanChartsDir（并行 200 上限）；启动对齐屏幕 StartupClientSize。
- **MainForm**（源码/Forms/MainForm.cs）：纯宿主：ShowEngineShell 创建壳（actions/stats/playChart/editChart/game 四接线）；ExitToLibrary/ExitToMenu 按壳可见性分派（t48）。
- **EngineUiDemoForm**（源码/Play/EngineUi/EngineUiDemo.cs）：独立 WinForms 演示窗（引擎 UI 三页样例 + 转场），用于 parity/shellshot 取证与主菜单「✨ 引擎 UI 演示」入口（t53 判定：保留）。
- 结论：**「游戏层在引擎内开发」已经成立**（引擎窗承载游玩+编辑器）；缺的是「引擎团队自己的开发/调试工具」与「引擎工具独立可发」。本设计补齐后者。

### 1.5 模式现状（预设缺口）

- 游戏层 ModeSystem（源码/CoreUtil/ModeSystem.cs）：20 玩法元数据单一来源（Id/Display/Keys/Tracked/KeyHint/Removed）——**显示名/键位提示有，但无判定档/速度/布局/背景/示例谱**。
- 引擎侧判定档：JudgementProfile 各模式均有（Arcaea/Phigros/IIDX/Cytus/Adofai(bpm)/Maimai/OsuMania(od)/OsuStandard(od)/SDVX/CHUNITHM/太鼓/Catch/Cytus2/GC/Lanota/Dynamix）——**无统一「模式→判定档」注册表**（分散在 SettingsPanel/JudgeSettings.ApplyForChart/GamePanel）。
- 引擎侧玩法族：RulesetFactory 描述符仅覆盖 Lane/Line/Ring/Path 四族；**无「Mania6K/8K、IIDX、Arcaea、Cytus、ADOFAI 翻成描述符」的映射**。
- 谱面示例：其他/Chart/Milestone示例/ 有 Mania 4K/Phigros/Arcaea/Cytus/IIDX 示例 .mil（游戏层可加载）；引擎库内只有 RulesetFactory.BuildChart（3~6 音最小谱）与 StarterChartGenerator（onset 起步谱）——**无「每模式一个像样示例谱」的引擎内置集合**。
- 键位：GameSettings.KeyMaps（游戏层，4..10 键）；引擎 KeyInput 用 90+i 基址（LaneRuleset keyBase=90）。

### 1.6 缺口结论（一句话）

**引擎缺三件：① 引擎窗上的文字/UI 渲染（SoftwareDrawAdapter + 文本服务）；② 模式预设注册表 EnginePresets（把 10 模式 → RulesetDescriptor/JudgementProfile/键位/布局/背景/示例谱 统一起来）；③ 引擎内预览宿主 EnginePreviewHost + RulesetRenderer（四族可视渲染 + 自动/手动）。再加：谱面导入（.mil→引擎 ChartData）、音频后端（IEngineAudioBackend Windows 实现）、导出（引擎侧 PackExport）、帮助文档。**

---

## 2. 设计（A/B/C/D 四节）

### A. 引擎工具化：MilestoneEngine.exe →「引擎工具窗」

#### A1. 工具形态（关键决策 D1：推荐 R1）

**R1（推荐）引擎栈自持**：MilestoneEngine.exe 升级为完整工具窗——引擎 UI（UiComponents+SceneManager）在引擎窗（EngineApp/IEngineWindow）上运行，新增 SoftwareDrawAdapter（IUiDraw 实现，文本走 GDI P/Invoke）。
- 理由：符合「后续开发在引擎中进行」；引擎工具可脱离 Milestone.exe 独立发布（单文件）；不引入 WinForms/D2D 依赖；EnginePlay 已是 Windows-only（Win32 壳 P/Invoke），GDI 文本不破零依赖。
- 代价：文本服务 + 适配器 ~400 行（见 A2）。

**R2（备选）游戏层壳当工具**：把 EngineMainShell 打包成工具 exe（WinForms）。
- 理由：UI 全现成；代价：工具=WinForms 宿主，违背引擎零依赖理念，且与 Milestone.exe 同质（重复交付物）。**不推荐**，仅作 t55 若要求「与游戏同界面」时的退路。

#### A2. 引擎文本服务 + SoftwareDrawAdapter（批 1 核心）

- 新增 引擎/engine/Platform/EngineText.cs（#if WINDOWS 内 GDI P/Invoke）：
  - CreateFontW/SelectObject/TextOutW/GetTextExtentPoint32W/GetDeviceCaps；字体族 = Microsoft YaHei UI（游戏层同款），字号经 -MulDiv(pt, dpi, 72)。
  - 服务 API（静态）：Measure(string s, double sizePt) → (w,h)、DrawString(IntPtr hdc, string s, int x, int y, double sizePt, RgbaColor)；字形缓存：(text, sizeBucket, bold) → 位图（LRU ≤128），测量走 GDI 不缓存（快）。
  - 非 Windows：#if !WINDOWS 桩（Measure 估宽 = 码点 × size × 0.55，Draw 无操作）——保库跨平台可编译（HardwareProbe 先例）。
- 新增 引擎/engine/Platform/SoftwareDrawAdapter.cs : IUiDraw：
  - 持有 SoftwareRenderer 的帧缓冲（uint[] Frame）+ 每帧 Begin(IntPtr hdc)/End()；Rect/RoundedRect/Ellipse/Line/Circle 直渲到软件渲染器（复用其原语或直写 Frame）；Text 走 EngineText 光栅到 Frame（逐像素 alpha 混合）；PushClip/PopClip 用矩形裁剪栈（写 Frame 时按栈裁剪）；MeasureText → EngineText.Measure；Image：占位（Key→空画 1px，工具窗暂不用位图）。
  - 与引擎窗集成：EngineApp/IEngineWindow 的渲染回调里，在 IEngineRenderer.Present() 前插入 SoftwareDrawAdapter 的 UI 画面合成（文本/UI 画到帧缓冲同一层），或由工具宿主自行在 Render 中依次 UiCanvas.Render(adapter) + renderer.Present() —— **推荐后者**：工具宿主 Tick 驱动 SceneManager，Render 里先画游戏画面（IEngineRenderer 原语）再 canvas.Render(adapter) 叠 UI，最后 Present。零改动 EngineApp。

#### A3. 工具窗内容页面（新增 引擎/engine/Tool/* 或 EnginePlay 内；用引擎 UI 构建）

| 页面 | 内容 | 复用 |
|---|---|---|
| 主页（菜单） | Logo「Milestone Engine 工具」+ 6 入口卡片：引擎自检/模式预设/打开谱面/导出打包/示例谱/帮助 | UiTheme/UiButton/UiCard |
| 模式预设页 | 左列表 10 预设；右侧详情（判定档、键位、速度、布局、背景令牌）+ 按钮「生成示例谱」「预览游玩」 | EnginePresets（§B） |
| 预览页 | EnginePreviewHost 全窗承载（§C）；ESC 返回；顶部标 AUTO/手动 | §C |
| 打开谱面页 | 文件选择（引擎窗无 WinForms → 命令行 --open <path> + 引擎窗内简易路径输入框；P2 再考虑目录浏览列表页） | 见 D4 |
| 导出页 | 当前谱面 → pack 单文件（批 3）；展示输出路径 | PackExport（§D） |
| 帮助页 | 大文本页：快捷键表/命令表/模式表/引擎指南摘要（内容从 引擎/engine/README.md 提炼，双链） | UiLabel 滚动 + UiScroll |

### B. 引擎模式预设（EnginePresets 注册表）——用户指令②

#### B1. 数据模型（新增 引擎/engine/EnginePresets.cs，纯数据零依赖）

    public sealed class EngineModePreset
    {
        public string Id;              // mania4/mania6/mania8/phigros/arcaea/cytus/osustd/iidx/maimai/adofai
        public string Display;         // Mania 4K…
        public RulesetFieldType Field; // Lane/Line/Ring/Path
        public int Keys;               // 轨道/扇区/线数
        public double Speed;           // 滚动速度 1.0
        public int ScrollDir;          // 1 下落；-1 反向/环形顺时针
        public string ProfileKey;      // OsuMania8/OsuMania6/…/Phigros/Arcaea/Cytus/OsuStandard8/Iidx/Maimai/Adofai120
        public int KeyBase;            // 90（Lane 家族键码基址；触摸族 0）
        public string KeyHint;         // 人类可读键位（复用 ModeSystem 文案口径）
        public PresetLayout Layout;    // hitY/线数/环半径/路径视窗（四族各一子结构）
        public PresetBackground Background; // 背景令牌：主色/次色/渐变方向/轨道色
        public int SampleBpm;          // 示例谱 BPM（120）
        public int SampleNotes;        // 示例谱音符数（32~64，BuildChart 增强）
        public string DocLine;         // 一句话模式说明
    }

#### B2. 10 预设表（引擎内推荐默认值，与现有口径一致）

| Id | Field | Keys | Speed | 判定档（ProfileKey） | 键位 KeyHint | 布局/背景 | 示例谱 |
|---|---|---|---|---|---|---|---|
| mania4 | Lane | 4 | 1.0 | OsuMania(8)：MAX 20/300 40/200 60/100 80/50 | D F J K（90..93） | hitY 0.82、4 道、深空蓝 | BuildChart 增强：32 音 500ms 间隔 + 4 hold |
| mania6 | Lane | 6 | 1.0 | OsuMania(8) | S D F J K L | hitY 0.82、6 道 | 36 音 |
| mania8 | Lane | 8 | 1.0 | OsuMania(8) | S D F ␣ J K L ; | hitY 0.82、8 道 | 40 音 |
| phigros | Line | 2 线 | 1.0 | Phigros：Perfect 80/Good 160/Bad 180 | D F J K（判定线自由位） | 两动态线、命中带 | 6 音（线 X 分布+1 滑条） |
| arcaea | Lane | 6（天2+地4） | 1.0 | Arcaea：PURE+25/PURE 50/FAR 100 | 6 轨（S D F J K L） | hitY 0.78 + 天轨镜像 | 24 音（上行+下行） |
| cytus | Line | 1 扫描线 | 1.0 | Cytus：PERFECT 75/GOOD 150/BAD 220 | 4 分位（D F J K） | 扫描线 Y 扫、4 分位目标圈 | 16 音（Y 分位网格） |
| osustd | Line | 1 | 1.0 | OsuStandard(8)：300/100/50 | 鼠标 + Z X 空格 | 自由场、approach 圈 | 12 音（圆点+滑条+转盘） |
| iidx | Lane | 8（7 键+转盘） | 1.0 | IIDX：PGREAT 16.7/GREAT 33.3/GOOD 116.7/BAD 250 | S=转盘 D F ␣ J K L ; | 8 道 + 转盘条 | 32 音 + 转盘旋钮 |
| maimai | Ring | 8 扇区 | 1.0 | Maimai：PERFECT 31.25/GREAT 62.5/GOOD 125 | 8 分区（鼠标） | 环 r=400、8 扇区 | 8+2 滑星 RingNote |
| adofai | Path | 2 球（Lane0/1） | 1.0 | Adofai(120)：角度 30/45/60 | ␣/D（每拍输入） | 双球路径、砖块、角度判定 | 24 拍路径（含拐角） |

注：游戏层 ADOFAI 有 Adofai(Routlock) 与 AdofaiReal 两种；引擎 PathRuleset 为「双球砖块」对齐 AdofaiReal（真实 ADOFAI）；arcaea 在引擎侧先用 Lane 族近似（6 轨 + ScrollDir=-1 天轨反向），真「Arc 曲线」渲染属游戏层表现——预设设计如实标注「近似族」。

#### B3. 引擎 API（一键加载，签名级草案）

    public static class EnginePresets
    {
        public static IReadOnlyList<EngineModePreset> All { get; }
        public static EngineModePreset Get(string id);                    // 未知 id → null + 日志
        public static Ruleset BuildRuleset(string id);                    // RulesetFactory.Build(descriptor)
        public static JudgementProfile BuildProfile(string id);           // RulesetFactory.BuildProfile
        public static ChartData BuildSampleChart(string id, int notes = 0);// BuildChart 增强（hold/滑星/拐角）
        public static ChartContext BuildContext(string id);               // 一键上下文（含 Tracker/Board/Clock）
        public static RulesetResult RunHeadless(string id);               // 无头闭环（自检/断言）
        public static string ToDocTable();                                // 预设表 → markdown（供 CD）
    }

#### B4. 引擎内一键加载 UI

- 工具窗「模式预设页」：列表 → 详情（字段+判定档表格+键位+布局+背景色块预览）→ 「生成示例谱」「预览游玩」（预览=§C）；「导出 .mil」（批 3，若实现）。
- 无 UI 一键：MilestoneEngine.exe --preset <id>（无窗无头跑 RunHeadless 打 acc）+ --preview <id>（有窗预览）。

#### B5. 文档（CD）

- docs/engine-presets.md（或并入 引擎/engine/README.md 新章节）：EnginePresets.ToDocTable() 生成 10 预设表 + 每模式一段（判定窗口数值/键位/布局/背景）+ 引擎 API 用法示例（C# 5 行：var ctx = EnginePresets.BuildContext("mania4"); RulesetRunner.Run(…, ctx)）。
- 工具窗帮助页嵌入预设表。

### C. 引擎内游戏预览（EnginePreviewHost + RulesetRenderer）——用户指令③

#### C1. 组件设计（引擎内，新增 引擎/engine/Samples/Preview/ 或 engine/PreviewHost.cs）

    /// 引擎内预览宿主：EnginePreviewHost : IEngineAppHost
    /// 输入：EngineModePreset（或自定义 Ruleset+ChartData+Profile）
    /// 行为：Tick 驱动 EngineTime → ChartContext.Advance；自动游玩（RunAuto 同款规则：按谱面序 press）
    ///       或手动（OnKey：KeyBase+i → Tracker.PressLane；触摸族 OnMouse → TouchInput 方位）
    /// 渲染：Render(IEngineRenderer r)：RulesetRenderer.Draw(r, ruleset, ctx, nowMs)
    ///       次序：背景令牌→场地（Lane 轨道/Line 判定线/Ring 环/Path 路径）→音符（Depth 映射）→
    ///      判定线/命中反馈（判定文字=工具窗由 SoftwareDrawAdapter 叠加；纯引擎窗画色条/圆环）
    /// 完成：全部判定后停 2.5s 显示结果（命中/acc 色带）→ 自动退出或等待按键

    /// 四族可视渲染（纯 IEngineRenderer 原语，无文字依赖）
    public static class RulesetRenderer
    {
        public static void Draw(IEngineRenderer r, Ruleset ruleset, ChartContext ctx, double nowMs);
        // Lane：lane 条 + 中心线 + hitY 判定线 + 音符 FillRoundedRect（深度=Mapper.DepthOfRemain）
        // Line：线（PlayfieldLine 位置/旋转）+ 命中带 + 音符圆点（X/Y 映射）
        // Ring：外环 + 8 扇区线 + RingNote 方位圆点 + 滑星弧（DrawLine 折线）
        // Path：双球 + 砖块（贝塞尔取样点 FillRect）+ 目标角指示（FillTriangle）
    }

- 数据来源：ChartData 直接用引擎谱面模型（ChartImport §D4 的 .mil 解析产物，或 EnginePresets.BuildSampleChart）。
- 状态可视化（无文字方案）：进度条（命中/总数）、判定质量色条（命中色绿/漏判红），文字版由工具窗 UI 叠加（SoftwareDrawAdapter 已就绪）。

#### C2. 调用点（三处）

| 调用点 | 位置 | 说明 |
|---|---|---|
| 工具窗「预览游玩」按钮 | EnginePlay 工具 UI | → EngineApp.Run(EnginePreviewHost, opts)（同窗替换场景） |
| --preview <presetId|pack> | EnginePlay CLI | 有窗预览；--preview <id> --headless 无头跑分 |
| 游戏层编辑器 F5 | 现状不动 | 游戏层预览（GamePanel.StartAutoplay）保持；如需「引擎原生预览」可后续加按钮（P2，与 t53 衔接） |

#### C3. 后续开发工作流（在引擎中开发）

    新玩法开发（引擎内闭环）：
    1. 写 RulesetDescriptor（或新 Ruleset 子类 + 绘法补进 RulesetRenderer.Draw 分支）→
    2. 在工具窗「模式预设」注册新预设（EnginePresets.All 加一行）→
    3. 「生成示例谱」→「预览游玩」即时可视验证（自动/手动）→
    4. --check（含 EnginePresets.RunHeadless 该预设断言 100% 命中）→
    5. 需要老游戏层协作时：导出 .mil（批 3）或直接发消息给 coder 团队。

### D. 与 t53（非引擎化代码删除）衔接

- **本设计不新增删除项**，只确认以下判定不变/更新：
  - EngineUiDemoForm：保留（parity/shellshot 取证 + 入口）——t53 §1.2 已定；引擎工具窗上线后其「演示」价值被替代，**标记 P2 待定**（用户未要求删）。
  - PackExporter（源码/CoreUtil/PackExporter.cs）：t53 判「必要保留」→ 本设计批 3 若引擎侧实现 PackExport，则游戏层 --pack 可改为调用引擎侧实现（或保留双份），**删除游戏层版 = P1 待定**（需 captain 拍板：保留 --pack CLI 兼容则留）。
  - ChartParser/ChartParserExtra：t53 判必要保留（游戏层解析核心）→ 本设计 D4 采用「引擎侧 .mil 子集导入器」而非迁移解析器，二者并存，**不删**。
  - --uitrial/--uidebug 等 dev CLI：t53 §1.3 待核对项——本设计确认：引擎工具窗（--preset/--preview/--open）是这些 dev 流程的未来形态，**t53 批 4 时按使用情况迁移到工具窗或保留**，不在本批改。
  - 旧 SongCardView/FolderPanel/菜单栏/状态栏等：与本设计无交互，按 t53 批 1-3 执行（不因本设计变化）。

---

## 3. 实施分批（给 eng-coder-vis，每批可独立验收）

### 批 1（核心基础，约 600 行）：引擎文本 + 适配器 + 预设注册表 + 示例谱增强
> **状态：已完成（t57，2026-08）**——EngineText/SoftwareDrawAdapter/EnginePresets/BuildChart 增强/EngineToolChecks 7 组断言全部落地（含 RingField/RingNote/maimai 增补：SectorAngle 均匀/SectorPoint↔AngleToSector 互逆/环上周长/StartAngle 构造/滑星/10 音全命中）；RunHeadless 10 预设 acc=1.000（32/32…24/24 全命中无降级）；实现报告 其他/docs/协作/t57-实现报告.md（含 §6 增补）；数据基准=其他/docs/协作/engine-presets-data.md。
1. 引擎/engine/Platform/EngineText.cs（GDI P/Invoke 文本度量/绘制 + LRU 字形缓存 + 非 Windows 桩；#if WINDOWS）
2. 引擎/engine/Platform/SoftwareDrawAdapter.cs : IUiDraw（SoftwareRenderer 帧缓冲直绘 + 裁剪栈 + MeasureText）
3. 引擎/engine/EnginePresets.cs（EngineModePreset + 10 预设表 + Build* 一键 API + RunHeadless + ToDocTable）
4. RulesetFactory.BuildChart 增强（可选参数 notes/hold/滑星/拐角，默认输出不变——保 DemoRunner 基线绿）
5. EngineChecks 新增断言：预设全表 RunHeadless(id) 全部命中（10 预设 100% 命中闭环）、SoftwareDrawAdapter 桩渲染非空、EngineText.Measure 单调性
验收：captain 构建 + MilestoneEngine.exe --check exit 0；docs 大纲产出（engine-presets 章节）。

### 批 2（工具窗 UI + 预览，约 900 行）：MilestoneEngine.exe 变工具
> **状态：已完成（t64，2026-08）**——ToolApp 4 页/PreviewController+EnginePreviewHost 拆分/RulesetRenderer 四族渲染/SetViewTransform A 方案/CLI 5 命令（--tool 无参默认/--preset/--preview/--help/--version，t55 T5 关闭）/EngineToolAppChecks 4 组断言；转场=简化立即切换（真 Slide/Fade 帧合成=批5 P2 可选）；实现报告 其他/docs/协作/t64-实现报告.md。
### 批 2b（跟随项，P2 可选）：转场帧合成 + 工具窗像素级布局（试玩走查后按 §5.5/§5.6 定）
> **状态：已完成收口（t67，2026-08）**——实际收口项=批3 缺陷：--export-mil 编译错修复/--preview·--open 黑屏 P1 根治（废弃嵌套软渲+逐像素 Blit → LetterboxRenderer 直渲，同 --demo 已验证路径；live 截图 4 轨道+黄蓝音符+判定线可见）/--size 全链路生效/errLine 警告清除（双工程 0 警告）；真转场帧合成仍=未做（P2，批5 候选）；实现报告 t67-实现报告.md。
1. 引擎/engine/Tool/ToolApp.cs：页面切换改 SceneTransition（Slide/Fade 360ms 帧合成，转场 API 已齐备）
2. 工具窗像素级布局：按试玩走查结论调整（间距 8/6px、图标色族 #2C6CFF、预设页/帮助页排版）

> 批2 原实施清单（t64 已按此实现，保留为轨迹）：
1. 引擎/engine/Tool/ToolApp.cs：工具窗宿主（EngineApp.Run + IEngineAppHost 组合：主页/预设页/预览页/帮助页 4 页场景 + SceneTransition 转场 + UiCanvas+SoftwareDrawAdapter 每帧合成）
2. 引擎/engine/Samples/Preview/EnginePreviewHost.cs：引擎内预览宿主（Tick/Tracker/自动-手动输入/完成退出）
3. 引擎/engine/Samples/Preview/RulesetRenderer.cs：四族可视渲染（Lane/Line/Ring/Path）
4. EnginePlay/Program.cs：新命令 --preset <id>（无头 acc）、--preview <id>（有窗）、--tool（默认开启工具窗，原 --demo 保留）
5. EngineChecks：预览宿主无头 10 预设全命中 + 工具窗一帧渲染非空（FauxDraw 计数）
验收：MilestoneEngine.exe（无参）→ 工具窗 4 页可达；--preview mania4 自动游玩 32 音全命中；--check 仍绿；截图/走查由试玩验证（引擎窗无文字截图可先验色块与布局）。

### 批 3（输入/输出，约 400 行）：谱面导入 + 导出打包 + 音频后端（P0 项——否则工具「开不了用户谱」）
> **状态：已完成（t65，2026-08）**——8 细化点全落地：ChartImport 9 样本全过/MilExport round-trip（maimai 10/10 互逆闭环）/PackExport 同格式/WinAudioBackend mci（TimeScale=1.0 文档化+100ms 定时器）/T3 MessageBoxW/4 CLI（--open/--export/--export-mil；--synthmetronome=P1 未做→批4 候选）/5 组断言全绿；实现报告 t65-实现报告.md + 批3细化增补.md。**收尾跟进=t67（批2b/收尾：--export-mil 编译错 + --preview 黑屏 P1 + --size 生效 + errLine 警告，dep t65）**。细化设计（eng-design-vis 拍板，2026-08）：
> - .mil 实格式=JSON milestone-1：format/mode/title/artist/version/bpm/offset/audio/keys/events[]/notes[{t,c,e,type,x?,y?}]。
> - ChartImport=System.Text.Json（BCL 零包）；t→TimeMs、c→Lane、e→EndMs+type=hold、type=ring/slide→RingNote（StartAngle/EndAngle，无方位→最近扇区）、x/y→X/Y；mode→预设映射：mania→mania{keys}（4/6/8 或最近）、phigros/arcaea/cytus/osustd/iidx/maimai/adofai/adofai2→对应 id；events 忽略（P2）；错误含行/列+字段名；回归样本=示例 6 文件+测试格式 3 文件（9 全过）。
> - MilExport（新增）：ChartData→.mil JSON（round-trip 断言；--export-mil <id> 落盘示例谱，补 maimai/osustd/adofai 缺口）。
> - PackExport=游戏层同格式（[payload][int64 len][8B MILSTPK1]，4K 归一化 Col%4/X×4）；模板发现=exe 目录→--template 覆盖；--out <dir>。
> - WinAudioBackend=mciSendString（open/play/pause/seek/status/close，alias=msx）；TimeScale 不支持→固定 1.0 如实文档化；PositionSample=内部 100ms 定时器；非 Win 桩；Open 失败→调用方降级引擎时钟不崩溃。
> - T3 附项（PublishPlayer）：无内嵌谱 → MessageBoxW P/Invoke 提示+用法，exit 1。
> - CLI：--open <path> [--headless]、--export <path> [--out]、--export-mil <id> [--out]、--synthmetronome <bpm> [--out]（P1：8 小节节拍音轨 WAV 纯 BCL 合成，供示例谱 audio 零版权零体积）。
1. 引擎/engine/Tool/ChartImport.cs：.mil 文本 → 引擎 ChartData（子集：metadata/BPM/音符 tap/hold/lane/X/RingNote；解析失败给明确错误行号；与游戏层 .mil 同格式——**以 其他/Chart/Milestone示例/ 四个文件 + 测试格式/ 为回归样本**）
2. 引擎/engine/Tool/PackExport.cs：模板内嵌导出（复制 pack-template.exe + [payload][len][magic]，与游戏层 PackExporter 同格式——引擎工具目录自带 pack-template.exe 或从 exe 目录发现）
3. 引擎/engine/Platform/WinAudioBackend.cs : IEngineAudioBackend（**mciSendString P/Invoke**——Windows 自带可播 mp3/wav；失败回退 waveOut wav；再退静音回退）
4. EnginePlay 命令：--open <path>（打开谱面→选模式/自动匹配→预览）、--export <path>（打包单文件）
验收：--open 其他/Chart/Milestone示例/Mania 4K 示例.mil 预览播放；--export 产出 <曲名>.exe 双击可播；--check 绿。

### 批 4（文档/发布/收尾，约 200 行 + 文档）
1. 引擎/engine/README.md 增章「引擎工具」：命令表/预设表（ToDocTable 内联）/预览 API 用法/打包说明（3 步）/示例谱清单
2. docs/engine-tool-guide.md（人工向：双击→预设→预览→导出 流程 + 常见问题）
3. 构建产物/engine-exe/ 发布脚本（build-tool.ps1：publish EnginePlay + 拷贝 pack-template.exe + 示例谱目录）——**同时解开「构建产物/engine-exe 为空」问题**
4. 工具窗帮助页接文档内容
验收：发布目录 = 双击可用（试玩验证）；README 与工具行为一致。

### 批 5（P1，可选/待 captain 拍板）：双向衔接
- 游戏层 EngineMainShell 菜单增「引擎工具」入口——评估是否重复，建议**不加**（工具独立 exe 已满足）。
- PackExporter 去重（引擎侧实现后游戏层 --pack 改调用）。
- t53 §1.3 核对项最终清单输出（--uitrial 等）。
- 批3 遗留：--synthmetronome（P1 节拍音轨：WAV 44 字节头/16bit mono 44.1kHz/首拍重音 0dB 啪+后续 -6dB 嗒/结尾 100ms 静音；纯 BCL）→ **已并入批4**（engine-tool-design 批3 完成注记同步）。
- 批2b 遗留：真转场帧合成（Slide/Fade）→ 批5 P2 候选；--demo T4（计分/连击 HUD 提示）登记 P2 候选。

---

## 4. 风险与约束

1. **零依赖红线**：EngineText/SoftwareDrawAdapter/WinAudioBackend 全部 P/Invoke + #if WINDOWS；禁止 NuGet/System.Drawing/WinForms。Windows 构建与 linux 构建必须都通过（EngineChecks csproj 已跨平台）。
2. **EngineChecks 基线不回退**：每批新增断言必须独立、可禁（环境相关如音频用桩），DemoRunner/UiComponentsChecks/LegacyControlsChecks 全绿。
3. **不构建原则**：引擎编写只写代码/脚本不构建（captain 构建）。批 1/2/3 交付时给出「构建后验证命令清单」。
4. **格式风险（.mil 导入器）**：与游戏层 ChartParser.ParseMil 保持同语义易漂移——回归样本 = 其他/Chart/Milestone示例/ 4 文件 + 测试格式/（adofai_h/switch_test/phigros_dual），实施时以样本为准逐字段对照；**若 .mil 语义与游戏层模型强耦合（parts/events/timeline），改走「游戏层导出 .mil 标准子集 + 引擎只读子集」，避免双实现**。
5. **音频 MP3**：mciSendString 方案解决（Windows 自带）；若失败退 wav 自生成（静音拍点音）——预览核心是判定闭环，音频为增强（AudioClock.Bind 已有接口）。
6. **与 t55 合并**：t55 盘点产出后，将缺口表并入批 2/3 验收点（如「打开按钮/帮助/错误提示/单文件可移植性」）。

## 5. 关键决策汇总（供 captain 拍板）

| # | 决策 | 选项 | 推荐 |
|---|---|---|---|
| D1 | 工具形态 | R1 引擎栈自持工具窗 / R2 游戏层壳打包 | R1 |
| D2 | 引擎文本 | GDI P/Invoke / 位图字体精灵 / 无文本 | GDI P/Invoke（游戏层同字体，工作量最小） |
| D3 | 预设放哪 | 引擎层 EnginePresets / 游戏层 ModeSystem 扩展 | 引擎层（用户指令②=引擎做预设；游戏层 ModeSystem 保留显示名口径） |
| D4 | .mil 导入 | 引擎侧镜像子集 / 游戏层导出标准 .mil / 全量迁移 ChartParser | 引擎侧 .mil 子集导入器（样本驱动），全量迁移 P2 |
| D5 | PackExporter | 引擎侧复刻（双方并存）/ 移到引擎（游戏层引用） | 引擎侧 PackExport 复刻，游戏层 --pack 保留；去重 P1 待 captain |
| D6 | 音频 | mciSendString（mp3+wav）/ waveOut（wav only）/ 静音 | mciSendString（首推），waveOut 备选；音频为增强不阻塞批 1/2 |
| D7 | 预览渲染 | RulesetRenderer 四族原语 / 引游戏层 D2D | 引擎四族原语（零依赖、开发闭环）；游戏层表现不回迁 |
| D8 | 工具窗入口 | 默认 --tool（无参即工具窗）/ 保留 --demo 为默认 | 无参=工具窗（用户指令①）；--demo 保留为旧演示命令 |

---

## 6. 与引擎使用者（t55）的衔接

- t55 聚焦「MilestoneEngine.exe 启动形态/每模式新用户体验/引擎内预览现状」——其报告 engine-user-audit.md 产出后，把其「缺口清单」逐条对照本设计 §1.6/§3 并编号（如 TA-1 打开按钮→批 2/3）。**实施以本设计分批为主干，t55 缺口作为每批验收补充**；若 t55 发现「引擎窗无文字」为第一障碍——与批 1 一致。

### 附：t55 合并表（2026-08 盘点后更新）

| t55 缺口 | 批次/归属 | 备注 |
|---|---|---|
| T1 不能打开 .mil/.osu/.adofai | 批3 ChartImport + --open | 引擎侧 .mil 子集导入器（样本驱动） |
| T2 pack 只 4K tap | 文档明示 + P2 包 v2 | 保持现有格式，不做语义失真承诺 |
| T3 模板无内嵌谱报错无提示 | 批3（附提示窗口/用法） | |
| T4 --demo 无屏上提示/计分 | 批2（预览页提示层 + SoftwareDrawAdapter 文字） | |
| T5 无 --help/--version | 批2/4 CLI | |
| T6 不可单文件复制 | 批4（目录版 + 单文件版两形态说明） | 发布脚本 |
| T7 无示例谱/首启空库 | 批1（EnginePresets.BuildSampleChart）+ 游戏层首启引导任务 | 游戏层任务建议 captain 建单给 coder-vis |
| 示例谱无声 | 批3 节拍器音轨（BPM 合成 wav）+ 游戏层打包 | 零版权零体积 |
| maimai/osustd/adofai/回环示例谱缺失 | 批1（引擎内生成）+ 游戏层 .mil 转存 | |
| IIDX/ADOFAI 无玩法说明 | 批1 DocLine/KeyHint + 游戏层屏上浮层（前三秒 + H 开关） | 浮层=GamePanel HUD 任务 |
| Arcaea/osustd/maimai 视觉（③） | 游戏层表现：试玩复验 → coder-vis 默认布局修复 | 非引擎库 |
| 引擎窗文字重叠 Mila␣stone 等（④） | 标准已定：最小间距 8/6px、emoji 槽宽 1.15×+4、Logo 段间距 6px（见 engine-capability-list.md §5.5） | coder-vis 修 + 试玩复核 |

**决策修正**：③④ 两批视觉问题 = 游戏层表现（GamePanel Draw* + Skin + EngineMainShell Logo），**不进引擎库批次**；引擎侧仅批2 工具窗 UI 必须满足同一文字标准。

---

_终稿 v1.1_（t54 设计交付；实施拆分见 §3 + 附 t55 合并表；批 1 即日起可派给 eng-coder-vis）
