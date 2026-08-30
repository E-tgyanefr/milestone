# 引擎策划设计（t3）：多分辨率 / 多刷新率 / 引擎 UI 低分辨率修复 / 音游内容扩充

- 日期：2026-08-26
- 作者：eng-design-max（引擎策划）
- 版本：v1.3（v1.2 为 t6 实施基线；v1.3 回写 t6 实现状态 2026-08-26：P0 批 B/EN-1/EN-2 与 P1 批 C/EN-3/A/EN-4 全部按 §5.2 草案实现，EN-5 ViewportPolicy、EN-8 UiTabBar 亦完成，DemoRunner 新增 7 组断言，待 captain 构建后 EngineChecks 验证；宿主接线项归 t5）
- 读者：eng-coder-max（t6 引擎编写执行）、coder-max（t5 游戏层）、designer-pro（t2 策划对齐）、captain
- 证据基线：t1 试玩诊断（构建产物\obj\diagnosis\诊断报告.md，P0-4 已闭环 P0-1..P0-3 待修）、t4 引擎使用者报告（其他\docs\协作\engine-user-report.md，E1-E8/G1-G10/R1-R13）、引擎 README 性能基线（JudgementTracker 100k 音符 11.51ms；UI 离屏 60 帧 GDI 平均 1.06ms）。
- 约束：本文只出设计，不写代码。

---

## 0. 结论速览（TL;DR）

1. **多分辨率**：壳层「固定 1280×800 GDI 位图 + letterbox 双三次放大」是"引擎化内容分辨率过低"的根因——大屏上内容被 1.87× 插值放大，渲染分辨率被锁死在 1280×800。方案：引入引擎层 ViewportPolicy（设计分辨率 1280×800 不变 + 渲染分辨率 = 物理客户区 × renderScale + letterbox 统一逆变换），把"画布"与"窗口"解耦；GDI 位图按物理客户区尺寸重建，大屏 1:1 锐利、弱机降采样省 CPU。
2. **多刷新率**：FpsGovernor 现 7 档（60/120/144/165/240/360/无限制）缺 50/75/90/100/180/200/300/480/500 与"跟随屏幕"档；TickFps 用帧数阈值（120 帧/300 帧）在高刷下墙钟时间失真（360Hz 下 120 帧仅 0.33s）。方案：完善挡位表 + 时间化迟滞 + 与 GraphicsQuality.AutoAdapt 单一仲裁；1440Hz 属未来超高刷（Windows 枚举上限 ~500Hz），按"自定义档 + 最小帧时 0.5ms 钳制"设计，不硬编码。
3. **UI 低分辨率/文字重叠**：UiLabel 无裁剪/无换行/无省略号（超宽直接越界渲染）、UiGlyphRuns emoji 测量宽≠绘制宽（P1-1 根因）、UiTheme Fn* 固定像素无缩放档。方案：组件层加 Clip/Ellipsis/WordWrap（IUiDraw.PushClip 已具备，零接口改动）+ 测量缓存（E1/E2/E3）+ emoji 估宽统一口径 + 主题缩放令牌；宿主层按 t1 清单修坐标（P0-3/P1-2/P1-3）。
4. **音游内容扩充**：优先实现 **判定可视化（JudgementLog）** 与 **练习模式（PracticeSession）**（引擎能力几乎全就绪、收益最高、与 t2 自动游玩/AI 游玩对齐），次选 Storyboard 播放器增强、段位（.mc）多谱联动。

---

## 1. 现状审视（证据链）

### 1.1 多分辨率现状

| 层 | 现状 | 证据 |
|---|---|---|
| 引擎壳 | 逻辑 ClientSize=1280×800 固定（启动不贴合屏幕=P0-4）；NewCanvas 固定 1280×800；OnPaint 画到**固定 1280×800 GDI 位图**，再 letterbox 双三次（HighQualityBicubic）放大到物理客户区；16ms Timer 无条件 Invalidate（静止也 60fps 全量重绘） | EngineMainShell.cs:77, :152, :87-97, :1186-1217 |
| 壳 DPI | PhysClient() 用 GetClientRect 取物理客户区（t59 已修 DPI 虚拟化），ToVirtual 做 letterbox 逆变换喂鼠标 | EngineMainShell.cs:1161-1179, :1379-1382 |
| 游玩层 | GamePanel/D2DRenderer 按物理客户区 1:1 绘制，SuperSample=1.4 离屏超采样 + 乒乓放大链 + 4096 上限钳制；EffSs 一次性降档（**只降不升**） | GameSettings.cs:9,20；D2DRenderer.cs:245-342, :393-413, :418-448 |
| 引擎栈 | EngineApp/EngineWindow 固定 options.Width/Height 软渲，StretchDIBits 直接拉伸，无 letterbox/viewport 概念 | EngineApp.cs:20-31；EngineWindow.cs:29-38 |

**判定**：letterbox 适配本身正确（t1 多分辨率 1.0/1.35/1.868/0.78 scale 验证通过），缺陷是①渲染分辨率被锁死在 1280×800（大屏模糊=需求项"分辨率过低"根因）；②启动窗口不贴合屏幕（P0-4）；③引擎栈（EngineApp）完全无多分辨率语义。

### 1.2 多刷新率现状

- FpsGovernor.Rates = {60,120,144,165,240,360,0}，RateNames 7 档；TargetFrameMs；PresetForRate（≥240→低 / 144~165→中 / 60~120→高 / 0→自动）——FpsGovernor.cs:15-29。
- 自适应：TickFps(fps) 以**帧数**为阈值（低于目标 80% 持续 120 帧降档、高于 120% 持续 300 帧升档）——FpsGovernor.cs:68-78。高刷下 120 帧≈0.33s@360Hz（设计意图 ~2s 失效）；且与 GraphicsQuality.AutoAdapt(frameMs)（GraphicsQuality.cs:38-64）双系统同写 Effective，无仲裁。
- 屏幕刷新探测：GamePanel.ScreenFrameMs() 用 GetDeviceCaps(VREFRESH) 但钳制 Math.Max(4, Math.Min(34, ms))→**只覆盖 29~250Hz**，300Hz 用户屏实测存在但探测被钳到 250；帧时目标取 Math.Max(ScreenFrameMs, FrameTargetMs)。
- D2DRenderer.FpsStatic（EMA 0.7/0.3）是全局帧率信号；消费方三处：EffSs（一次性降档）、AdaptStages（<950 降 / ≥1200 升）、FpsGovernor/GraphicsQuality。

### 1.3 引擎 UI 低分辨率/文字重叠现状

| 根因 | 位置 | 表现 |
|---|---|---|
| UiLabel.DrawSelf 无裁剪、无换行、无省略号：超宽文本直接越出标签矩形绘制 | 引擎\engine\Ui\UiComponents.cs:267-291 | 相邻行/按钮文字互相穿插（P0-3 曲库统计行）；按钮文本左溢出（P1-1） |
| UiButton.DrawSelf 只把 tx/ty 钳进盒内，不裁剪、不缩字号 | UiComponents.cs:314-336 | 长文案钳位后仍越界（P1-1 齿轮游离） |
| UiGlyphRuns emoji 测量宽（Segoe UI Emoji ≈40px+）≠ 单色绘制宽（≈9px），MeasureText 与 Text 推进口径一致但都基于错误的测量宽 | 源码\Play\EngineUi\UiGlyphRuns.cs:45-72；t1 P1-1 | 按钮居中计算错 → tx 负偏移 → 文本左溢出 + 图标间 30px 空隙 |
| UiTheme Fn* 固定像素（11..46），无缩放令牌；letterbox scale<1 时有效字号等比缩小（0.78 scale → 10px 文案≈7.8px） | 引擎\engine\Ui\UiTheme.cs:71-79 | 小窗/低分下文字发糊发小 |
| UiGridLayout.Layout 无条件把子元素拉伸为单元格宽（AddArrows 叠层按 300 宽假设 X=272，绘制却在 900 宽右端） | UiComponents.cs:429-455；EngineMainShell.cs:844-857 | P0-2 上下箭头命中区错位 590px |
| UiTabBar.InvokeClick 空实现、无"点击→Select"接线 | 引擎\engine\Ui\LegacyControls.cs:119-126 | P0-1 设置 8/9 分区不可达 |
| 每帧全树 Layout+测量+run-split 无缓存（E1/E2/E3/G3/G4） | UiComponents.cs:198-204；GdiDrawAdapter.cs:23-64,116-127 | 60fps 全量 GDI 重绘 + 每图元 new Font/Brush。**状态（2026-08-26 t9 已修复）**：E3（UiGlyphRuns.Split 256 项缓存+纯文本零分配）、G3/G4（GdiDrawAdapter Font/Brush/Pen 缓存、UiLabel 单行快速路径）、E4（NoteStyleBook 0 分配）已闭环；剩余 E1/E2 引擎级 UiElement 测量缓存+脏渲染仍待 t6 |

### 1.4 音游内容现状（引擎侧家底）

- **Storyboard**：12 类事件 + JudgementHook 判定联动 + ToJson/FromJson 保真往返，能力完备；但 Advance(nowMs) 单调求值（_lastAdvance、事件起点字典不可回跳），**无播放器**（seek/暂停/回退需 Reset 语义），宿主只有 StoryshotDemo 取证。
- **判定闭环**：JudgementTracker 有 Judged 事件流（含偏移 ms），但**无日志/统计收集**；宿主 _allDev/_lastDev 自建滚动窗（GamePanel DrawDevGraph/DrawJudgeBar）。
- **时间/音频**：EngineTime.TimeScale 变速、AudioClock 音频时钟（E6：宿主未接，仍用 WPF MediaPlayer）。
- **多谱面**：ChartData+BpmTimeline；宿主已有 parts（M 键切换族）与多场设计（其他\docs\协作\multi-stage-设计.md）；仓库存在 .mc 段位文件（其他\段位文件\）尚无引擎侧 Course 容器。
- t2（designer-pro 进行中）：编辑器三栏 + 自动游玩/AI 游玩 + 2-3 创新玩法（谱面模型/判定/多场/Storyboard 范围内）。

---

## 2. 多分辨率统一方案（引擎层）

### 2.1 概念模型：三种分辨率解耦

| 概念 | 含义 | 现值 |
|---|---|---|
| **窗口分辨率** | 物理客户区像素（含 DPI 虚拟化校正） | GetClientRect 实测（如 853×533@150% / 2560×1494 最大化） |
| **渲染分辨率** | 后备缓冲像素 = 窗口 × renderScale | 壳：固定 1280×800（缺陷）；游玩：窗口 × SS1.4 |
| **设计分辨率** | UI 布局逻辑坐标（布局/命中/字号基准） | 1280×800（保持，全部页面坐标不动） |

规则（引擎层统一）：
1. **设计分辨率恒定 1280×800**——所有 UiElement 坐标/字号语义不变，宿主页面零重排。
2. **renderScale = clamp(画质档 SS, min(4096/窗口宽, 4096/窗口高), 下限)**：
   - 高：renderScale = 1.0（大屏物理 1:1 锐利，**修复"分辨率过低"**；不再把 1280×800 放大到 2560）；
   - 中：renderScale = 1.0（与高同，GDI 成本已按像素线性）；**降采样 renderScale=0.75** 作为低画质+4K 窗口的省 CPU 选项；
   - 游玩层沿用 D2D SuperSample=1.4 语义（GPU 富余时超采样抗锯齿），renderScale>1 = 超采样/降采样呈现。
3. **letterbox 统一**：scale = min(physW/1280, physH/800)；输入逆变换 = (p - offset)/scale（沿用 ToVirtual 语义，上移为引擎公共函数）。
4. **DPI 感知统一**：一律取物理客户区（GetClientRect）+ GetDpiForWindow；DpiContext.Scale = dpi/96 仅用于字号换算决策（引擎 UI 用设计坐标，不乘 DPI；legacy WinForms 面板维持 Ui.P() 自体系，两者互不干扰）。启动时声明 PerMonitorV2（app.manifest 或 SetProcessDpiAwarenessContext），确保 GetDpiForWindow 逐显示器准确。

### 2.2 引擎层新增组件（落地文件）

**新增 引擎\engine\App\ViewportPolicy.cs**（纯 BCL，平台分支 #if WINDOWS）：

    public enum FitMode { Letterbox, Fill, Stretch }
    public readonly struct Letterbox { double Scale, Ox, Oy; (double,double) ToVirtual(double,double); }
    public readonly struct DpiContext { uint Dpi; double DpiScale; int PhysW, PhysH; int LogicalW, LogicalH; }
    public readonly struct ResolutionProfile { double DesignW, DesignH; double MinScale, MaxScale; double RenderScaleFor(int quality, double physW, double physH); }
    public static class ViewportPolicy {
        Letterbox Compute(double physW, double physH, double designW, double designH, FitMode mode);
        DpiContext FromWindow(IntPtr hwnd, int fallbackW, int fallbackH);   // WINDOWS: GetClientRect+GetDpiForWindow；其它: 1:1
        (int w, int h) StartupSize(double screenW, double screenH, double designW, double designH); // 0.92×工作区、16:10 等比、下限 1152×720
    }

要点：RenderScaleFor 内置 4096 上限钳制与 0.5 下限；StartupSize 直接落地 P0-4（启动贴合屏幕）。

### 2.3 改动文件/函数清单（t5 游戏层 ↔ t6 引擎层分工）

| # | 文件 | 函数/位置 | 改动 | 归属 |
|---|---|---|---|---|
| 1 | 引擎\engine\App\ViewportPolicy.cs | 新增 | 上述类型与算法 + 单元断言（[视口策略] 组） | t6 |
| 2 | 引擎\engine\Ui\UiComponents.cs | UiCanvas | 增加 DesignW/DesignH/RenderScale/SetViewport(physW,physH,scale)；ViewWidth/Height 保持设计坐标语义；Render 前把 world→design 变换透传 | t6 |
| 3 | 引擎\engine\App\EngineApp.cs | EngineAppOptions/Run | 增加 DesignWidth/DesignHeight/FitMode/OnResize(int,int)；窗口 SizeChanged → renderer.Resize + 宿主回调 | t6 |
| 4 | 引擎\engine\App\EngineWindow.cs | IEngineWindow | 增加 ClientWidth/ClientHeight/Dpi；Win32Window 上报 WM_SIZE 后物理客户区；Present 按 FitMode letterbox（StretchDIBits 前置黑边） | t6 |
| 5 | 引擎\engine\Platform\SoftwareRenderer.cs | Resize(w,h) | 现 ctor 定尺寸，增加运行时重建帧缓冲 | t6 |
| 6 | 引擎\engine\IUiDraw.cs | 文档契约 | 明确"全部坐标=设计空间；宿主在 Render 前施加 renderScale 变换"（PushClip 已有，接口零改动） | t6 |
| 7 | 源码\Play\EngineUi\EngineMainShell.cs | ctor:77 / OnShown | ClientSize = ViewportPolicy.StartupSize(工作区) → P0-4 修复 | t5 |
| 8 | 同上 | OnPaint:1186-1217 | _gdiBmp 尺寸 = phys×renderScale（尺寸变化重建）；letterbox 变换改调 ViewportPolicy.Compute；插值按画质档（低=Bilinear，中/高=Bicubic） | t5 |
| 9 | 同上 | OnHandleCreated:1149-1156 | 用 DpiContext.FromWindow 初始化 _w/_h 与 renderScale；Reopen/UnhostContent 同步 | t5 |
| 10 | 同上 | PhysClient/ToVirtual:1164-1179 | 委托 ViewportPolicy（行为等价，去私有 P/Invoke 重复） | t5 |
| 11 | 同上 | _timer:87-97 | 16ms 无条件 Invalidate → 脏触发（hover/转场/数据变更才 Invalidate；静止页 30fps 兜底）——G1/R10 | t5 |
| 12 | 源码\Play\EngineUi\GdiDrawAdapter.cs | Text/MeasureText/Rect 族 | Font/Brush/Pen 按 (色,字号,族) 缓存 + run-split 缓存（R5/R6） | t5 |
| 13 | 源码\Play\D2DRenderer.cs | EffSs:393-413 | 一次性降档 → 可回升（见 3.3）；SuperSample 语义 = renderScale>1 的超采样呈现 | t5 |
| 14 | 源码\Play\GamePanel.cs | ScreenFrameMs | VREFRESH 钳制放宽到 [2,200]ms（5~500Hz） | t5 |
| 15 | 源码\Play\EngineUi\EngineUiDemo.cs | 同 #8 的 OnPaint 路径 | 与壳同策略（Demo 亦固定位图缩放） | t5 |

### 2.4 验收标准（需求项"多分辨率"）

- 启动即贴合屏幕（工作区 92%，16:10），最大化/全屏 letterbox 居中不裁切（沿用 t1 4 档验证口径）。
- 2560×1494 最大化：渲染分辨率=物理客户区（renderScale=1），主菜单文字**逐像素锐利**（与修复前 1.87× 放大对比截图入档）。
- --selfcheck/EngineChecks 新增 [视口策略] 断言：letterbox 往返（ToVirtual 后 ToScreen=恒等）、4096 钳制、StartupSize 下限。
- 1000×700 小窗（scale 0.78）与 150% DPI（853×533）命中测试不漂移（t1 已有口径）。

---

## 3. 多刷新率完善方案

### 3.1 完善挡位表（FpsGovernor 全量）

| 挡位 | 目标帧时 | 推荐画质 | 状态 |
|---|---|---|---|
| **-1 跟随屏幕** | 1000/显示器Hz | 自动 | **新增**：启动读 VREFRESH/DXGI 动态选档（用户无需手选） |
| 50Hz | 20.00ms | 高 | **新增**（PAL/旧屏） |
| 60Hz | 16.67ms | 高 | 现有 |
| 75Hz | 13.33ms | 高 | **新增**（办公屏/入门高刷） |
| 90Hz | 11.11ms | 高 | **新增**（笔记本） |
| 100Hz | 10.00ms | 高 | **新增**（超宽屏） |
| 120Hz | 8.33ms | 高 | 现有 |
| 144Hz | 6.94ms | 中 | 现有 |
| 165Hz | 6.06ms | 中 | 现有 |
| 180Hz | 5.56ms | 中 | **新增**（OC 屏） |
| 200Hz | 5.00ms | 中 | **新增** |
| 240Hz | 4.17ms | 低 | 现有 |
| 300Hz | 3.33ms | 低 | **新增**（t1 实测屏 300Hz 探测被钳问题一并修） |
| 360Hz | 2.78ms | 低 | 现有 |
| 480Hz | 2.08ms | 低 | **新增** |
| 500Hz | 2.00ms | 低 | **新增** |
| **自定义 1..2000Hz** | 1000/n，最小钳 0.5ms | 低 | **新增**：超高刷（含 1440Hz）不硬编码 |
| 0 无限制 | 0（目标 1000+） | 自动 | 现有 |

**1440Hz 专项说明**：Windows 显示枚举（EnumDisplaySettings/DXGI 模式列表）当前实际上限约 500Hz；1440Hz 属未来/工程样品面板。引擎侧**不设上限常量**——TargetFrameMs 对 rate>1000 钳制最小帧时 0.5ms（防 0/溢出）；画质固定低档；Rates 允许任意 1..2000 条目（UI 用通用名 nHz）；当运行期检测到显示器 ≥600Hz 时自动把对应档插入选择列表。D2D 呈现用 PresentOptions.Immediately（不锁 vsync），帧循环钳制值即可当目标，无需 DWM 配合。

PresetForRate 更新映射：rate>=240→0(低)；144<=rate<240→1(中)；50<=rate<144→2(高)；rate<0→3(自动)；0→3(自动)（原 60/120/144/165/240/360/0 断言不变，扩展新档断言）。

### 3.2 FpsStatic 自适应边界完善

现状问题：① TickFps 帧数阈值随刷新率漂移；② 双系统（TickFps 与 GraphicsQuality.AutoAdapt）同写 Effective 打架；③ EffSs 只降不升（t4 #7）；④ 屏幕探测钳 250Hz。

方案（单一仲裁 + 时间化迟滞）：
1. TickFps(double fps) → TickFps(double fps, double nowMs)：低于目标 80% **持续 ≥1500ms** 降一档；高于目标 120% **持续 ≥4000ms** 升一档（墙钟时间语义，与刷新率无关；nowMs 由调用方传 MonoMs）。
2. **仲裁**：GraphicsQuality.Step(delta) 单一入口（StepDown/StepUp 迁入），FpsGovernor 与 AutoAdapt 都经它改 Effective；AutoAdapt 保持帧时阈值（>19ms 降 / <9ms 升），但升档需 FpsGovernor 侧未处于降档冷却（共享 _lastStepMs，两控制器互斥 1s 内只允许一次换档），杜绝震荡。
3. **FpsStatic（EMA）用途统一**：EffSs 降档阈值 <950（快降），新增**回升**：avg≥1500 持续 10s 且 effSs<base → effSs += 0.2 缓升（上限 base，迟滞差 950/1500 防闪烁）；AdaptStages 维持现有 950/1200 升降但把 _adaptiveStages 初值由 2 改跟随画质档。
4. ScreenFrameMs 钳制 [2,200]ms（5~500Hz）；FpsGovernor.Apply(-1) 时用探测值选档并写 Describe() 文案。
5. SelfCheck 更新：新挡位表不变量（帧时单调、长度一致、0 与 -1 语义）、时间化迟滞状态机（模拟 1.5s/4s 序列）、1440Hz 钳制（TargetFrameMs(1440)≥0.5ms）。

### 3.3 改动清单（需求项"多刷新率"）

| # | 文件 | 函数 | 改动 | 归属 |
|---|---|---|---|---|
| 1 | 源码\Play\FpsGovernor.cs | Rates/RateNames/TargetFrameMs/PresetForRate/Apply/TickFps/SelfCheck | 3.1/3.2 全量 | t5（游戏层文件；表结构与自检口径按本设计） |
| 2 | 源码\Play\GraphicsQuality.cs | AutoAdapt/Step | 增加 Step(delta) 单一入口 + 换档互斥冷却 | t5 |
| 3 | 源码\Play\D2DRenderer.cs | EffSs/AdaptStages | SS 回升路径 + 初始级数跟画质 | t5 |
| 4 | 源码\Play\GamePanel.cs | ScreenFrameMs/LoopTick | 钳制放宽；TickFps 新签名 | t5 |
| 5 | 源码\Play\EngineUi\EngineMainShell.cs:902-904 | 刷新率行 | 新 RateNames（含"跟随屏幕"） | t5 |
| 6 | 源码\Forms\SettingsPanel.cs:553-560 | 下拉 | 同步新挡位 | t5 |

> 注：FpsGovernor 属游戏层文件（源码\Play\），本表改动的执行主体是 t5；t6 只需保证引擎层无阻塞（引擎 EngineTime/EngineApp.TargetFrameMs 已支持任意帧时，零改动）。

---

## 4. 引擎 UI 低分辨率/文字重叠修复方向

### 4.1 引擎组件层（t6，引擎\engine\Ui\UiComponents.cs / UiTheme.cs）

| # | 组件 | 修复设计 | 兼容性 |
|---|---|---|---|
| 1 | UiLabel | 新增 Clip=true（默认开：DrawSelf 包 d.PushClip/PopClip，IUiDraw 已具备，接口零改动）、Ellipsis（测量超宽 → 截尾加省略号，按测量宽二分）、WordWrap（按 MeasureText 分词折行）、AutoShrinkFont（最小字号 9，超宽自动缩）。钳位逻辑保留为 Clip 的兜底 | 新字段默认值保证旧页面行为不破（Ellipsis 默认 false，仅 Clip 生效——越界不再绘制） |
| 2 | UiButton/UiCard | 文字绘制同样过 Clip；UiButton 增加 Ellipsis 复用 UiLabel 排版（消除钳位后仍越界的 P1-1 路径） | 同上 |
| 3 | UiStackLayout/UiGridLayout | 增加 ClipChildren（默认 false，容器 Render 时对子树整体 PushClip）；子元素对齐选项 ChildAlign（解决 FillCrossAxis 拉伸与宿主叠层假设冲突的 P0-2 类问题） | 默认关闭 |
| 4 | UiTabBar（LegacyControls.cs:119-126） | InvokeClick 实现：按 WorldToLocal 命中 x 计算 idx = (int)(lx / cell) → Select(idx)（P0-1 引擎侧根治） | 事件语义不变 |
| 5 | 测量缓存（E1/E2/E3） | UiElement 内 (text,size)→width 字典缓存 + Text 版本号失效；UiCanvas.Render 增加 dirty 标志——InvalidateLayout() 前 Layout 结果复用。注：UiGlyphRuns.Split 缓存与 GdiDrawAdapter 图元缓存已由 t9 在宿主层落地（stress-engine.log 全绿），t6 本项聚焦引擎级 UiElement 测量缓存 + 脏渲染，不再重复宿主层工作 | 纯优化，无行为变化 |
| 6 | UiTheme | 新增 double Scale=1.0 令牌 + UiTheme.Scaled(double k)（Fn*/S*/R* 整体缩放）+ double MinEffectiveFont = 9；Fn* 数值不变（1280×800 基准），低分辨率场景用"渲染分辨率提升 + 最小窗口下限"兜底，主题缩放留给 1024×640 断点（P2 可选） | 默认值不动 |

**emoji 测量口径修复（P1-1 根治）**：UiGlyphRuns 新增 MeasureRuns(List<GlyphRun>, double size)：常规 run 用 IUiDraw.MeasureText，emoji run 按 码点数 × size × 1.15 估宽（与 monochrome 字形实际宽一致）；GdiDrawAdapter.Text/MeasureText 与 D2DDrawAdapter 绘制推进统一改用该口径（一处规则，两端一致）。配套：UiGlyphRuns.Split 缓存（见上表 #5）。

### 4.2 宿主层（t5，源码\Play\EngineUi\）

t1 诊断逐项修复（引擎侧 #4 已覆盖 P0-1，其余宿主改坐标）：

| 问题 | 位置 | 修复 |
|---|---|---|
| P0-2 上下箭头命中错位 | EngineMainShell.cs:844-857 AddArrows | 叠层 X 改随 UiNumberBox 实际绘制宽计算（或箭头命中区并入 UiNumberBox 内部），消除 272→1062 的 590px 错位 |
| P0-3 曲库统计/筛选重叠 | SecondaryPages.cs:110-126 | _folderStats（y=100, 24 高）与 filterRow（y=132）间距不足：filterRow 下移 y=156，统计行限高 20；标题与首行同步 +8px |
| P1-1 玩家信息按钮溢出 | SecondaryPages.cs（玩家信息页） | 引擎侧 emoji 估宽修复后复查；按钮文本改 Ellipsis |
| P1-2 我的数据行距 | SecondaryPages.cs:341 | y += 34 → y += 44 |
| P1-3 段位标签行距 | SecondaryPages.cs:238-242 | DanHead(y=140) 与 DanSetLabel(y=182) 间距 +10px |

### 4.3 验收标准（需求项"分辨率/文字重叠"）

- EngineChecks 新增 [引擎 UI 裁剪/省略/换行] 断言组：Clip 不越界（离屏桩渲染哈希/像素断言）、Ellipsis 宽度 ≤ 盒宽、WordWrap 行数正确、AutoShrink 不破最小字号、GlyphRunCache 命中一致。
- --selfcheck 全绿 + 引擎构建 0 警告 0 错误；t7 试玩逐页截图：主菜单/选歌/设置/曲库/玩家信息/我的数据/段位 无重叠无溢出（复用 t1 证据图口径）。

---

## 5. 引擎可新增音游内容（与 t2 玩法需求对齐）

> t2 已落盘（其他\docs\协作\editor-trilab.md，designer-pro）：编辑器三栏（§1.1-1.4）、自动游玩/AI 游玩（§2/§3，全部基于引擎内置规则 AI，零本地 AI 服务——t12 边界一致）、创新玩法三案（§4：A 回环变奏 / B 模式接力 / C 判定演出）、引擎诉求 EN-1..EN-8（§5 表）。本节已把 EN 诉求并入候选矩阵与 API 草案；多场（multi-stage-设计.md）已覆盖"同谱多场同屏"，模式接力="同谱分时接力"，为本节新增引擎能力的主要消费方。

### 5.1 候选与优先级矩阵

| # | 内容 | 引擎能力现状（缺口） | 玩家/制作收益 | 工作量 | 建议优先级 |
|---|---|---|---|---|---|
| B | **判定可视化 JudgementLog**：环形缓冲记录 (timeMs, lane, tier, offsetMs) + 滚动统计（均值/标准差、early-late 分桶直方图、每轨命中热图数据）（✅ t6 已实现） | JudgementTracker.Judged 事件流已具备，仅需收集器（~120 行） | 高：练习反馈（"我总是打早"）、编辑器对音诊断、t2 AI 游玩对比基准 | 低 | **P0（t6 首选）** |
| C | **练习模式 PracticeSession**：段循环 [t0,t1] 自动重开、速率 0.5~1.5×（EngineTime.TimeScale）、幽灵回放叠层、只计分段分、逐段统计（✅ t6 已实现） | EngineTime 变速 + JudgementTracker.Reset + ScoreBoard 已具备；缺"段界判定回收 + 分段分 + 回放事件流重放"封装（~300 行） | 极高：玩家第一诉求；t2 编辑器"自动游玩"的教学形态 | 中高 | **P0（t6 与 B 搭配实现）** |
| A | **Storyboard 播放器增强**：StoryboardPlayer（Play/Pause/Seek/Loop 段 + 事件时间轴列表 + 与 AudioClock 同步）；Advance 增加 ResetTo(nowMs)（清 _lastAdvance/起点字典/_pending）支持回跳（✅ t6 已实现：ResetTo + StoryboardPlayer） | Storyboard 求值器已完备；缺播放器封装与 seek 语义（~250 行 + 断言） | 中高：制谱叙事预览、t2 创新玩法（叙事层）必备 | 中 | **P1** |
| D | **段位/多谱联动 CourseData**：课程容器（谱面序列 + 每首 HP/分规则 + 连续游玩结算），对接 其他\段位文件\*.mc | ChartData/BpmTimeline/ScoreBoard 已具备；缺容器与 .mc 解析（~200 行） | 中：段位挑战引擎化（现为宿主功能）；t2 多谱面联动方向 | 中 | **P1** |
| E | **制谱 AI 增强 TimingReport**：音符密度/纵连/jack/间距难度报告（BeatAlignEngine/ChartValidator 之上） | Dsp/对音/校验已具备；缺密度报告聚合（~150 行） | 中：编辑器 AI 辅助升级（t2 AI 游玩数据源） | 中 | P2 |
| F | **判定闭环迁移适配器**：宿主 Note/Chart → 引擎 ChartData 适配器（E7 双记账收敛） | 已具备对照表，缺适配器+迁移文档 | 中：长期性能（引擎闭环 100k 音符 11.5ms） | 中 | P2 |
| EN-1 | **判定档位运行时热换**（t2 模式接力 B 案）：JudgeSettings.ApplyForChart(mode) 局中安全重算（清 _levelIndex 缓存、重入/线程安全确认），段切换换档位不产生错判（✅ t6 已实现：JudgementTracker.SetProfile） | 宿主 JudgeSettings 全局静态现设计为开局一次；需确认局中重算安全性 + 引擎侧 JudgementProfile 切换契约（~20 行确认 + 断言） | 高：模式接力（一歌四场）是 10 模式最佳展示窗口（t2 D4 并列 P0） | 低 | **P0** |
| EN-2 | **判定窗口乘数**：JudgementProfile/JudgeSettings 运行时窗口乘数（×1.5 热身窗），不改 12 预设数值、不污染存档统计（✅ t6 已实现：WindowMultiplier 钳 0.25..4） | 引擎 JudgementProfile.Tiers 为固定数值列表；增加运行期 WindowMultiplier（Evaluate 时乘算）约 ~30 行 + 断言 | 高：接力段切热身（防转场手感突变）；未来教学/演出软窗通用 | 低 | **P0** |
| EN-3 | **AudioClock 变速播放/编辑器预览时钟**（t2 §2.4/§3.4）：0.25~4x 预览音频同步变速、播放条不抖动（t4 E6：现 WPF MediaPlayer+手搓 offset 不变速）（✅ t6 已实现：IEngineAudioBackend + AudioClock.Bind；**定稿=完整变速契约（未走 1.0x 限制分支）**——WPF MediaPlayer.SpeedRatio 变速不改音高已登记为宿主实现提示，回退口径保留在接口契约注释，t5 编辑器侧按契约执行） | 引擎 AudioClock.cs 已具备（音频采样位置+offset+校准），缺：宿主音频回调承载接口 + 变速播放 API（或文档化"变速仅画面"限制的取舍结论） | 高：编辑器变速预览体验、播放条稳定（t4 体验 #5/#6 根治） | 中 | **P1** |
| EN-4 | **Storyboard 宿主渲染样板 + hook 触发率统计**（t2 判定演出 C 案）：StoryFrame → 宿主 D2D/粒子池接线说明 + HookFired/TotalHook 统计接口（✅ t6 已实现：HookFired/TotalHooks/DistinctHooksFired/HookRate） | StoryshotDemo 已是样板；补"并入 GamePanel 渲染链"接线说明 + Storyboard 侧 hook 计数（~20 行） | 中高：教学谱/演出谱落地（t2 C 案 P1） | 低 | **P1** |

**t6 落地建议（v1.1）**：**B（JudgementLog）+ EN-1 + EN-2** 为 P0 批（B 是反馈面板数据源、EN-1/EN-2 是 t2 模式接力 B 案的引擎前提，三者合计 ~170 行，一次提交可完成）；次批 **C（PracticeSession）+ EN-3**；再批 A + EN-4。E/F/EN-7 不进本轮。

**实现状态（v1.3，2026-08-26 t6 回执 + 逐项核对）**：P0 批与 P1 批已全部按 §5.2 草案实现并加 DemoRunner 7 组断言（[视口策略][引擎 UI 裁剪/省略/换行][判定日志][档位热换/窗口乘数][练习模式][Storyboard 播放器/hook 统计][AudioClock 绑定]）；EN-5 ViewportPolicy（UiCanvas.SetViewport/RenderScale/脏渲染、EngineWindow letterbox、SoftwareRenderer.Resize、EngineAppOptions.OnResize）与 EN-8 UiTabBar.InvokeClickAt 亦已完成。待 captain 构建后 EngineChecks 验证；宿主接线（GamePanel StoryFrame 渲染、ApplyForChart 清缓存、AddArrows、编辑器预览速率框口径）归 t5。

### 5.2 引擎 API 草案（签名级，供 t6 参考）

    // 判定可视化
    public sealed class JudgementLog {                       // 引擎\engine\JudgementLog.cs（新）
        public void Record(Judgement j);                       // 挂 JudgementTracker.Judged
        public double MeanMs, StdDevMs;                        // 滚动窗口（默认最近 200 判）
        public int EarlyCount, LateCount;                      // 打早/打晚
        public IReadOnlyList<double> Deviations;               // 环形缓冲（宿主画直方图）
        public void Reset();
    }

    // 练习模式
    public sealed class PracticeSession {                      // 引擎\engine\PracticeSession.cs（新）
        public PracticeSession(ChartData chart, JudgementProfile profile, ScoreBoard board);
        public double LoopStartMs, LoopEndMs;                  // 段界（默认 全谱）
        public double Rate = 1.0;                              // 0.5..1.5 → EngineTime.TimeScale
        public void Advance(double nowMs);                     // 段内推进；越 LoopEnd → 段分结算 + 自动回段
        public SegmentScore SegmentScore;                      // 分段分/命中/连击（不回写全局）
        public event Action<SegmentScore> SegmentCompleted;
        public void ResetSegment();
    }

    // Storyboard 播放器
    public sealed class StoryboardPlayer {                     // 引擎\engine\Storyboard.cs 追加
        public StoryboardPlayer(Storyboard sb);
        public bool Playing; public double TimeMs;             // 与 AudioClock 同步
        public void Play(); public void Pause(); public void Seek(double ms); // Seek 内调 sb.ResetTo(ms)
        public StoryFrame Advance(double dtMs);                // 帧增量求值
    }

    // 段位联动
    public sealed class CourseData {                           // 引擎\engine\CourseData.cs（新）
        public List<CourseStage> Stages;                       // {ChartKey, Mode, HpRule, ScoreRule}
        public static CourseData FromMcText(string text);      // .mc 解析
        public CourseResult RunHeadless(EngineJobs 并行校验);
    }

    // EN-1 判定档位运行时热换 + EN-2 窗口乘数（t2 模式接力 B 案）
    public partial class JudgementProfile {                    // 引擎\engine\RhythmCore.cs 追加
        public double WindowMultiplier = 1.0;                  // 运行期窗口乘数（热身 ×1.5；不写回预设）
        public JudgeTier Evaluate(double offsetMs);            // 现有——改在窗口比较时乘 WindowMultiplier（|off| <= t.WindowMs * m）
    }
    // 游戏层契约：JudgeSettings.ApplyForChart(mode) 局中可重算（清 _levelIndex 缓存、重入安全）——
    // t6 引擎侧负责 JudgementProfile 切换契约与断言；t5 段调度器负责调用时机（段切前 ApplyForChart(段模式)）。
    // 防回归硬断言（designer-pro 复核 v1.2）：WindowMultiplier=1.0 时全流程零差异——判定结果/结算/回放
    // （ReplaySystem 回放=实时同函数）/人类模拟（StartHumanTest）与「未启用乘数」逐字段一致，任何差异即 FAIL。

    // EN-3 AudioClock 宿主音频回调 + 变速（t2 编辑器预览）
    public interface IEngineAudioBackend {                     // 引擎\engine\AudioClock.cs 追加（t6 方案定稿处）
        double PositionMs { get; }                             // 音频采样位置（宿主实现：MediaPlayer.Position 或后端时钟）
        double TimeScale { get; set; }                         // 变速（0.25..4.0；不支持的后端文档化"仅画面变速"）
        void Open(string path); void Play(); void Pause(); void Seek(double ms);
        event Action<double> PositionSample;                   // 采样回调 → AudioClock 平滑（播放条不抖）
    }
    // AudioClock 增：void Bind(IEngineAudioBackend b) —— 谱面时间 = 音频采样位置 + offset，采样间线性插值平滑。
    // 变速取舍（t6 定稿，v1.2 回退口径已登记）：若后端支持 TimeScale 变速 → 音频+画面同步（实现路径提示：
    //   WPF MediaPlayer.SpeedRatio 可变速且不改音高——宿主侧可能仅需接线，t6 评估后定稿）。
    // 若定稿为「1.0x 限制」→ 必须同时交付 t5 编辑器侧口径（与 t2 §2.4 一致）：预览期间「预览速率」框禁用置灰 +
    //   悬停提示「当前音频后端不支持变速预览」；禁止「仅画面变速」（声画不同步会造成判定假象，比不变速更糟）。
    //   判定口径不受影响：变速仅作用于时间推进，不触碰窗口数值。

### 5.3 与 t2 创新玩法的衔接点（按 editor-trilab.md 定稿）

| t2 提案/需求 | 引擎侧配套（本设计） | 状态 |
|---|---|---|
| A 回环变奏（LoopComposer 展平 .mil，t2 §4.1） | **零引擎改动**（t2 已确认）；StarterChartGenerator 演绎样例可作对照（可选） | 引擎无需动 |
| B 模式接力（t2 §4.2，D4 P0） | **EN-1 档位运行时热换 + EN-2 窗口乘数（×1.5 热身）**——本 §5.1 P0 批；多场渲染/合并结算/键位路由由 T27 已实现，无需新增 | t6 实现 EN-1/EN-2 即解锁 |
| C 判定演出（t2 §4.3，D4 P1） | **A（StoryboardPlayer seek/ResetTo）+ EN-4（宿主渲染样板 + hook 触发率统计）**；JudgementHook 引擎侧已完备 | P1 批 |
| 编辑器自动游玩/AI 游玩（t2 §2/§3，宿主 t5 落地） | B（JudgementLog 判定日志，供 AI 对比/展示）；预览变速 = EN-3（P1） | 引擎配套随 t5 |
| 编辑器三栏自适应（t2 §1.4 D6） | **EN-5 = 本设计 §2 ViewportPolicy/letterbox 统一**——编辑器三栏只认逻辑客户区（1280×800 基线 + 比例钳制），物理像素换算由引擎壳统一，三栏坐标无感 | 已覆盖（§2.3 #7-10） |
| 性能释放点（t2 EN-6） | 本设计 §4.1 #5（测量/run-split 缓存）+ 2.3 #11-12（脏重绘/GDI 缓存）；编辑器密度柱缓存为宿主改动（t4 G7，t5 做） | 已覆盖 |
| NoteStyleBook 渲染接入约定（t2 EN-7，P2） | 引擎 NoteStyle.cs 数据层已备；消费约定（Shape→绘制分派）文档化，不强制本轮 | P2 |
| 设置页交互职责边界（t2 EN-8） | UiTabBar.InvokeClick 属引擎文件（LegacyControls.cs）→ t6 修；EngineMainShell AddArrows 属宿主 → t5 修；两方按 §4.1 #4 / §4.2 P0-2 分工对齐（captain 已通告） | P0 批 |

---

## 6. 团队衔接与分工确认

| 事项 | 本设计位置 | 执行方 |
|---|---|---|
| 视口策略/DPI/letterbox 引擎化 + UiCanvas 视口 | 2 | t6（引擎）→ t5 壳接入 |
| FpsGovernor 挡位/仲裁/SS 回升（游戏层文件） | 3 | t5（游戏层），t6 无阻塞 |
| UiLabel 裁剪/省略/换行、UiTabBar、测量缓存、emoji 口径 | 4.1 | t6（引擎） |
| t1 P0-2/P0-3/P1-* 坐标修复、启动贴合屏幕、脏重绘、GDI 缓存 | 2.3 #7-12、4.2 | t5（游戏层） |
| 判定可视化 JudgementLog + 档位热换 EN-1 + 窗口乘数 EN-2 | 5.1/5.2 | t6（引擎）→ 模式接力宿主接线随 t5 |
| AudioClock 变速/预览时钟 EN-3 + Storyboard 样板 EN-4 | 5.1/5.2 | t6（引擎方案/样板）→ t5 编辑器接入（P1 批） |
| 性能释放点（E1-E8/G1-G10/R1-R13） | 1.3、2.3 #11-12、4.1 #5 | t6 引擎侧（E1/E2/E3/E4）+ t5 宿主侧（G1-G10） |

**依赖注意**：t2（designer-pro）已落盘（editor-trilab.md，EN-1..EN-8 已并入本文 v1.1 §5）；t5 依赖 t2 已满足。t6 依赖 t1/t3(本文 v1.1)/t4 全部满足，按 §7 顺序开工。

## 7. t6 执行顺序建议（按本文）

1. 4.1 组件层（UiTabBar→UiLabel Clip/Ellipsis→引擎级 UiElement 测量缓存+脏渲染→UiTheme Scale）——EngineChecks 新断言组全绿。注：run-split/GDI 图元缓存与 NoteStyleBook 分配已由 t9 先行修复，勿重复实现。
2. 2 引擎 ViewportPolicy + UiCanvas 视口 + EngineApp/EngineWindow/SoftwareRenderer 适配——[视口策略] 断言。
3. 5.1 P0 批：B（JudgementLog）→ EN-1（档位热换契约+断言）→ EN-2（WindowMultiplier，×1.5 热身不污染预设）——[判定日志][档位热换][窗口乘数] 断言（含 ApplyForChart 重入安全、乘数 0.5/1.0/1.5/2.0 四值 Evaluate 一致；**防回归硬断言：乘数 ×1.0 时全流程（判定/结算/回放/人类模拟同函数路径）与未启用乘数逐字段零差异**）。
4. P1 批：C（PracticeSession）→ EN-3（AudioClock 绑定/变速取舍定稿；若定稿 1.0x 限制，同步交付 t5 编辑器「预览速率」框禁用置灰口径——§5.2 已登记，与 t2 §2.4 一致）→ A（StoryboardPlayer）+ EN-4（渲染样板 + hook 计数）。
5. 全程约束：dotnet build 引擎\engine\MilestoneEngine.csproj -c Release 0 警告 0 错误；EngineChecks exit 0；宿主 --selfcheck 全绿（t5 配合）。
6. 与 coder-max（t5）/designer-pro（t2）保持 send_message 沟通（6 分工表）。
7. 状态（v1.3）：t6 已按 v1.2 完成引擎侧全部实现（P0/P1 批 + ViewportPolicy + UiTabBar + 7 组新断言），待 captain 构建验证；§5.3 标注「归 t5」的宿主接线项随 t5 执行。

---

## 附：证据索引

- t1 诊断：构建产物\obj\diagnosis\诊断报告.md（P0-1..P0-4、P1-1..P1-3，01-51 号证据图）
- t4 报告：其他\docs\协作\engine-user-report.md（E1-E8 / G1-G10 / R1-R13）
- t2 规范：其他\docs\协作\editor-trilab.md（EN-1..EN-8 引擎诉求、创新玩法 A/B/C、D1-D8 决策）
- 引擎 README：引擎\engine\README.md（模块清单、性能基线、自检入口）
- 多场设计：其他\docs\协作\multi-stage-设计.md
- 关键源码：源码\Play\EngineUi\EngineMainShell.cs / 源码\Play\FpsGovernor.cs / 源码\Play\GraphicsQuality.cs / 源码\Play\D2DRenderer.cs / 源码\Play\GameSettings.cs / 源码\Play\EngineUi\UiGlyphRuns.cs / 源码\Play\EngineUi\GdiDrawAdapter.cs / 引擎\engine\Ui\UiComponents.cs / 引擎\engine\Ui\UiTheme.cs / 引擎\engine\Ui\LegacyControls.cs / 引擎\engine\IUiDraw.cs / 引擎\engine\Storyboard.cs / 引擎\engine\JudgementTracker.cs / 引擎\engine\App\EngineApp.cs / 引擎\engine\App\EngineWindow.cs / 引擎\engine\Platform\SoftwareRenderer.cs
