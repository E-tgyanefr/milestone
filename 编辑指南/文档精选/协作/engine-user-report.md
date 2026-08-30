# 引擎使用者报告：游戏层为引擎找问题（性能释放 / 体验）

> 视角：**用引擎写游戏的人**（宿主 `源码\` 层使用方）。
> 方法：沿「启动 → 选歌(谱面加载) → 游玩 → 编辑器 → 引擎 UI 壳」使用链做代码走查（未实机运行 GUI，
> 结论基于宿主热路径源码 + 引擎 README 性能基线；每条都带文件:行号证据）。
> 分类：**引擎层**（需引擎团队改 API/组件）/ **游戏层**（宿主可自行优化）/ 建议按 P0~P2 排序。

---

## 一、引擎层 API 缺口（引擎团队决策项）

| # | 缺口 | 证据 | 游戏层影响 | 建议（引擎侧） | 优先级 |
|---|---|---|---|---|---|
| E1 | **引擎 UI 无脏渲染/测量缓存语义**：`UiElement.Render()` 每帧必 `Layout()` + 全树重绘，静态 UI（无动画）也要每帧重新布局+测量 | `引擎\engine\Ui\UiComponents.cs:200-203`（Render 每帧调 Layout）；`UiLabel.DrawSelf:267-290`、`UiButton.DrawSelf:314-336`、`UiCard.DrawSelf:366-392` 每帧 MeasureText+d.Text | 宿主只能 60fps 全量重绘引擎壳（见 G1/G2），CPU 白烧 | 引擎 UI 增加"测量缓存 + 脏标记"：`UiElement` 内 `(text,size)→width` 缓存（Text 未变不重测）；容器 `Layout()` 只在 `InvalidateLayout()` 后执行；提供 `UiCanvas.CanDrawWithoutChange` 或按帧脏标志 | P0 |
| E2 | **`IUiDraw.MeasureText` 无缓存约定**，宿主实现每次新建昂贵对象：D2D 侧每次 `new TextLayout`（DirectWrite 排版，重）；GDI 侧每次 `new Font`+`MeasureString`（GDI+ Font 构造最贵） | `引擎\engine\IUiDraw.cs:54`（接口只有 MeasureText(s,size)）；`源码\Play\D2DRenderer.cs:680`；`源码\Play\EngineUi\GdiDrawAdapter.cs:60-64,116-127` | 每标签每帧 ≥2 次测量 × 2 次字体创建（GDI）；游玩 HUD `DrawHud` 每帧多次 `MeasureText` → 每帧 New TextLayout | IUiDraw 增加"测量句柄/缓存 id"或明确约定宿主按 (text,size) 缓存；或将测量缓存并入 E1 的 UiElement 层（组件层缓存即可，接口不必改） | P0 |
| E3 | **文本 run-split 每次分配**：`UiGlyphRuns.Split` 每调用 new List+GlyphRun+字符串逐字符拼接（O(n²)），Text/MeasureText 各调一次（同一文本每帧拆 2 次） | `源码\Play\EngineUi\UiGlyphRuns.cs:45-72`；`D2DDrawAdapter.cs:47,81`；`GdiDrawAdapter.cs:51,121` | 引擎壳每帧数百次拆分 → 每帧数 KB~数十 KB 垃圾 | 引擎侧提供 run-split 缓存工具（`string→List<GlyphRun>` LRU，或缓存到 UiLabel 的 Text 版本号）；或宿主层缓存（见 L5） | P0 |
| E4 | **`NoteStyleBook.Get` 每次 2 个字符串分配**（`Trim()`+`ToLowerInvariant()`），字典本身已是 OrdinalIgnoreCase；游玩热路径每音符每帧调用 | `引擎\engine\NoteStyle.cs:131-135`；宿主调用点 `源码\Play\GamePanel.cs:1286-1290`（NoteColCurrent/AccentColCurrent，在 DrawMania/DrawPhigros/DrawArcaea 等逐音符循环内） | 视窗内数百音符 × 60fps → 每秒数万字符串垃圾；L1/L2 GC 波动 | 提供无分配重载（键已规范化时直接字典查）；或在样式表上暴露 `NoteStyle ForModeKey(string)` 并文档标注"热点请帧级缓存" | P1 |
| E5 | **引擎 Jobs/并行解析入口已就绪但游戏层接入缺失的"标准路径"**：`ChartParserExtra.ParseMany（EngineJobs.ParallelMap）` 存在且 SongCardView(legacy) 已用，但**引擎壳** `EnumerateCharts()` 仍是 UI 线程同步逐文件 `ParseFile` | `源码\Charting\ChartParserExtra.cs:52-66`；`源码\Play\EngineUi\EngineMainShell.cs:1104-1127`（同步循环，≤40 文件） | 启动/进选歌页卡顿（见 G5）；丢掉了引擎已提供的并行加速 | 引擎侧提供"目录扫描+并行解析+结果产消"的单一入口/示例（ScanChartsAsync），或文档标注宿主应接 ParseMany；引擎壳直接换用（游戏层动） | P1 |
| E6 | **音频时钟引擎侧有抽象、宿主没接**：引擎 `AudioClock`（音频采样位置+offset 校准）未被宿主使用；宿主用 WPF `MediaPlayer.Position`+手搓 `GameSettings.Offset`（`RawMs()`），变速/暂停/跳转全手写 | `源码\Play\AudioPlayer.cs`（MediaPlayer）；`GamePanel.cs` 的 `RawMs()`；引擎 `AudioClock.cs` | 播放条跳动（Position 采样抖动）；编辑预览变速（0.25x~4x）**只有画面变速、音频不变速**；无音频-画面跳转对齐回调 | 引擎补"音频后端接口 + 变速播放/采样位置时钟"（IEngineAudio：Open/Play/Pause/Seek/PositionMs + TimeScale）；宿主替换 MediaPlayer | P1 |
| E7 | **双记账闭环**：引擎 `JudgementTracker`+`ScoreBoard` 闭环与宿主 `JudgementEngine` 并存（tick 连击口径还不一致，README 已列差异）；游戏层无法直接复用引擎闭环跑宿主谱面 | `引擎\engine\JudgementTracker.cs / ScoreBoard.cs`；`源码\Play\JudgementEngine.cs` | 迁移成本：宿主不敢换；引擎闭环的 100k 音符 11.5ms 基线用不上 | 提供"宿主谱面(Note/Chart)→引擎 ChartData"适配器或明确迁移 docs（README 已有对照表，可升级为迁移指南） | P2 |
| E8 | **UI 渲染后端接口缺"静态层/离屏层"概念**：宿主想只重绘变化区域（如 hover 高亮）没有引擎侧支持，只能全量重绘 | `IUiDraw` 无 PushLayer/离屏缓存；`UiCanvas` 无局部失效 | 引擎壳 60fps 全量软件重绘（G1/G2） | 后续 IUiDraw 可加"离屏画布绑定"（UiImage 已可宿主绑定位图——宿主可先行用 UiImage 做静态层，见 L4） | P2 |

---

## 二、游戏层（宿主）性能瓶颈点

| # | 问题 | 证据 | 估算量级 | 建议 |
|---|---|---|---|---|
| G1 | **引擎壳 16ms 定时器无条件 `Invalidate()`**：菜单/选歌/设置页即使完全静止也 ~60fps 全量重绘 | `源码\Play\EngineUi\EngineMainShell.cs:87-97`（Timer 16ms → Invalidate） | 空闲即满速重绘；待机 CPU 占用高 | 改"脏触发"：仅 hover 变化/转场进行中/数据变化时 Invalidate；静态页改用 30fps 或事件驱动 |
| G2 | **引擎壳 OnPaint 为纯 GDI+ 软件渲染 + 每帧全屏双三次放大**：固定 1280×800 位图 → `HighQualityBicubic` 全屏 DrawImage（4K 屏每帧 ~2.6MP 软件缩放）；背景层每帧重画（渐变+6 光晕+18 星点 ≈26 个 GDI+ 对象） | `EngineMainShell.cs:1194-1211,1220-1241`；D2DRenderer 已创建（:1154）但 OnPaint 不用 | 每帧数 ms 级 CPU（软件缩放为主），满载一核 | ① 背景层一次画到缓存位图（静态）；② D2D 适配器修复后切回 GPU 渲染（D2DDrawAdapter 已就绪）；③ 至少把放大插值降为 Bilinear/Nearest 或直接按物理客户区画（当前 1:1 已适配，见注释） |
| G3 | **`GdiDrawAdapter` 每图元分配 GDI+ 对象**：Rect/Ellipse/Line→每调用 new SolidBrush/Pen；Text→new Font + new SolidBrush + MeasureString；RoundedRect→new GraphicsPath(4 AddArc)；MeasureText→new Font×run | `源码\Play\EngineUi\GdiDrawAdapter.cs:23-76,116-127` | 引擎壳一帧数百图元 → 每秒上万 GDI+ 对象创建/销毁（GC 不明显，但 GDI+ 原生对象创建本身贵） | 适配器内按 (color,size,family) 缓存 Font/Brush/Pen（宿主层可做，不依赖引擎）；见 L6 |
| G4 | **引擎壳文本双倍测量**：UiLabel/UiButton 画前 MeasureText（Font+MeasureString）→ d.Text 内部又 MeasureString+DrawString | `UiComponents.cs:281/326/389` + `GdiDrawAdapter.cs:45-56` | 每标签每帧 2 次字体创建+3 次字形测量 | 并入 E1/E2 测量缓存 |
| G5 | **启动即同步扫谱**：`EngineMainShell` 构造（:82 BuildPages → :161 BuildSongs → :488 EnumerateCharts）在 UI 线程顺序解析 ≤40 个谱面文件；无后台线程 | `EngineMainShell.cs:486-490,1104-1127` | 40 文件 × ~10-40ms ≈ 0.4~1.6s 启动黑屏（首窗口 Show 前） | ① 用 `ChartParserExtra.ParseMany`（EngineJobs 并行）② 放 Task.Run 后台完成，先把窗口显示出来 ③ 缓存扫描结果（见 R2） |
| G6 | **进歌 UI 线程同步解码/上传**：`LoadAndPlay`→`LoadBackgroundArt`（FindBackgroundImage 最多 40+ 文件名逐一 `new Bitmap` 探测 + 2 次复制 + D2D `CreateBitmap` LockBits/Marshal.Copy/CPU 预乘循环） | `GamePanel.cs`（FindBackgroundImage/LoadBackgroundArt/CreateBitmap） | 大图 8-30ms+ 全部卡在点击"开始"到首帧之间 | 后台线程解码 → 完成后回填 D2D 位图；封面首次经选歌页预加载（见 R3） |
| G7 | **编辑器密度柱状图每帧 O(视窗小节数 × 全部音符)** 重算 | `源码\Charting\ChartEditorPanel.cs:4392-4413`（每帧 foreach 全部音符 per 小节） | 大谱（5k+ 音符 × 20+ 小节）≈ 10 万次/帧，播放中 30fps 持续 | 每小节计数按 (measure, col) 预计算缓存，MarkDirty 失效（宿主层可做） |
| G8 | **编辑预览超采样乒乓链**：编辑器 `FitEnabled=false` 恒保 SS 1.4 双离屏乒乓放大（每帧 2+ 次全屏 GPU 填充）+ `PresentOptions.Immediately` 无 vsync | `D2DRenderer.cs:186,354,403-404,423-446`；编辑器播放头 30ms 定时器 `ChartEditorPanel.cs:5274` | 编辑器播放预览时 GPU 满载（非游戏节奏却满帧填充） | 编辑器播放时 SS=1（1:1 直绘）或 1.2；编辑时 30fps 已够 |
| G9 | **`DrawMania3D` 每帧 `_mania3DQuads.Sort`**（List.Sort on view n） | `GamePanel.cs:1575`；缓冲区复用已做（Clear 保容量） | n≈100-500 排序 ~µs 级，可接受 | 优先级低；如要极致可改用按深度桶（引擎 TimeDepthMapper 已给深度，宿主可直接分桶） |
| G10 | **每帧 `ComputeLayout` 分配 1 个 `PlayLayout` 类对象** | `GamePanel.cs:477-497`（new PlayLayout 每次） | 60/s 次小对象 | 改 struct 或复用实例（轻微） |

---

## 三、体验问题清单（玩家/制作人视角）

1. **启动慢/黑屏**：窗口显示前先同步扫谱（G5）——"以为死了"。
2. **待机 CPU 高**：引擎壳静止也 60fps 全量重绘（G1/G2）——笔记本风扇/耗电。
3. **选歌搜索卡顿**：`RebuildSongList` 每次击键删除全部卡片重建（每卡片 3+ 文本控件）+ 每帧重排（`EngineMainShell.cs:559-570,579-608`）；建议仅增删差量 + 防抖。
4. **进歌顿挫**：背景解码/上传在主线程（G6）。
5. **播放条跳动**：进度=RawMs/EndTime，`MediaPlayer.Position` 在音轨解码/负载下抖动（E6）——建议音频时钟 + 播放条平滑（可在宿主先行：位置预测插值；根治靠引擎音频抽象）。
6. **编辑器大谱滚动发滞**：密度柱全量重算（G7）+ 事件求值虽已桶化但每帧全量 `_evMemoDirty` 重建（`ChartEditorPanel.cs:4257`，注释声明为 design trade-off，可在谱面改动时增量）。
7. **GPU 自适应只降不升**：SS 一次性降档后不回升（`D2DRenderer.cs:396-413`），低配机切好机后画质永久 1.0。
8. **HUD 字号桶化**：`D2DRenderer.Text` 按 4 档桶取 TextFormat（10/13/26/44），9f/16f 等实际字号不精确（`D2DRenderer.cs:641-657`）——视觉小瑕疵，非性能。
9. **布局微调拖拽即全屏重绘**：EditLayoutMode 鼠标拖动→Invalidate→全量重绘（`GamePanel.cs` OnMouseMove）；量小，可容忍。
10. **引擎窗 D2D→GDI 回退**（"按键全白"修复的代价）：引擎壳放弃 GPU（G2）——D2DDrawAdapter 已存在且逐方法对齐，建议引擎团队联合复核 D2D 白块根因并切回，释放 CPU/降耗电。

---

## 四、"性能释放"建议汇总（可并行 / 可缓存 / 可预加载）

### 可并行（引擎 EngineJobs 已在，接入即可）
- R1 启动/选歌扫谱：`ChartParserExtra.ParseMany`（并行）跑在后台线程 → 完成回填（G5）。
- R2 谱面解析缓存：按 (路径, mtime, 长度) 缓存解析结果（内存 LRU 或磁盘 cache），第二次启动秒开（宿主层）。
- R3 封面/背景缩略图预加载：选歌页后台生成 256px 缩略（或复用已解码图），进歌直接贴（G6）。

### 可缓存（多数宿主可做，1 项引擎侧）
- R4 引擎壳背景层：`DrawBackdrop` 一次渲染到位图缓存（G2）。
- R5 `UiGlyphRuns.Split` 结果缓存（string→runs，含版本）——若 E3 引擎不做，宿主先做（D2DDrawAdapter/GdiDrawAdapter 层）。
- R6 GdiDrawAdapter 内 Font/Brush/Pen 按 (color|size|family) 缓存（G3）。
- R7 UiElement 测量缓存（E1/E2，若引擎不做，宿主在 UiLabel/UiButton 子类先做）。
- R8 编辑器密度柱按小节预计算 + 脏失效（G7）。
- R9 `NoteStyle` 帧级缓存：每个 DrawXXX 函数开头解析一次 `NoteColCurrent`/`AccentColCurrent`（宿主立即可做，E4 引擎侧再根治）。

### 可预加载 / 降浪费
- R10 引擎壳改"脏触发重绘"（G1）；静态页 30fps。
- R11 编辑器播放时 SS=1（G8）。
- R12 音频 Open 提前（选歌页预 Open 音源）；播放条采样平滑（E6 根治）。
- R13 `NoteStyleBook.Get` 无分配重载（E4，引擎侧）。

---

## 五、已传递给引擎团队的诉求（agent_teams_send_message）

- **引擎编写**：E1/E2（UI 测量缓存+脏渲染）、E4（NoteStyleBook 无分配）、E3（run-split 缓存工具）、E5（扫描标准入口）——优先级与证据见本报告第一/四节。
- **引擎策划**：E6（音频抽象/变速时钟 — 播放条与编辑变速体验）、E8（静态层/离屏语义）、以及"引擎窗 D2D 渲染回退 GDI 的修复优先级"评审。

---

## 附：证据文件索引

- 引擎 UI：`引擎\engine\Ui\UiComponents.cs` / `引擎\engine\IUiDraw.cs` / `引擎\engine\Ui\UiTheme.cs`
- 宿主 UI 适配：`源码\Play\EngineUi\D2DDrawAdapter.cs` / `GdiDrawAdapter.cs` / `UiGlyphRuns.cs` / `EngineMainShell.cs`
- 渲染器：`源码\Play\D2DRenderer.cs` / `源码\Play\IRenderer.cs`
- 游玩：`源码\Play\GamePanel.cs` / `源码\Play\AudioPlayer.cs` / `源码\Play\JudgementEngine.cs`
- 编辑器：`源码\Charting\ChartEditorPanel.cs`
- 谱面加载：`源码\Charting\ChartParser.cs` / `ChartParserExtra.cs` / `源码\CoreUtil\ModeSystem.cs`
- 引擎样式：`引擎\engine\NoteStyle.cs`
