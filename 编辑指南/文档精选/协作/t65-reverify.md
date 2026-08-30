# t65（引擎批3）复验记录：--open/--export/--help —— 发现 P1 预览黑屏

> 复验：引擎使用者 · 2026-08-28 · 对象=**scratch 构建**（构建产物\obj\t65-play\bin\MilestoneEngine.exe，t65 报告 §3 同源；官方 bin 构建归 captain）
> 方法：命令行直跑（捕获 stderr）+ PrintWindow 定时截帧 + 像素采样（1280×720 窗口）
> 证据：构建产物\obj\t65-reverify\（run.log / run2.log / open-s1..s3.png / demo-s1..s2.png）

## 结果表

| 项 | 结果 | 证据 |
|---|---|---|
| --help（命令表） | ✅ **11 命令 + 预设表**（含每预设 键位/判定档/示例谱/说明）→ T5 达成 | run.log L2-27 |
| --open 不存在文件 | ✅ 友好错误：`打开失败：文件不存在：…`，exit=1 | run.log L32 |
| --open Mania 4K 示例.mil | ⚠ 窗口正常创建+标题正确（"Milestone Engine · 打开 Mania 4K 示例.mil（ESC 退出）"）+ 自动关闭 exit=0 —— **但画面全黑** | 见下 P1 |
| --open Phigros / Arcaea / IIDX / 多模式示例 | ⚠ 同（exit=0 窗口运行；画面未验证——与 Mania 同路径，预计同状） | run2.log（exit=0） |
| --export（无模板） | ✅ 友好错误：`打包失败：缺少播放器模板 pack-template.exe（引擎 exe 目录或当前目录）；请先构建 PublishPlayer 并放置。`，exit=1 → T2/T3 文案达标 | run.log L39 |
| --export（有模板后） | ⏳ 待批4（pack-template.exe 属构建产物；载荷断言 EngineChecks 已覆盖） | t65 报告 §2 |

## ⚠ P1 发现：--open 预览窗黑屏（渲染路径缺陷）

**对照实验（同 exe 同截法）：**
| 窗口 | 1.8s 像素(500,400) | 3.3s 像素(500,400) | 4.8s |
|---|---|---|---|
| `--demo`（EngineGameHost） | **80,140,255**（亮蓝=音符） | 18,24,40（轨道底色） | — |
| `--open Mania 4K 示例.mil`（EnginePreviewHost.LoadExternal） | **0,0,0** | **0,0,0** | **0,0,0** |

- 图形：open-s1/s2/s3.png 三帧全黑（仅标题栏+左缘窄条），demo-s1.png 有亮蓝音符。
- 0,0,0 连宿主 `r.Clear(9,13,23)` 的背景色都没呈现 → 推测 **Render 未执行或 Present 被跳过**（而非"画了但太暗"）。
- 无头数据路径正常（EngineChecks 外部谱面 20/20 全命中；--open exit=0/自动关闭）——**问题纯在预览渲染呈现**，初判范围：EnginePreviewHost.Render/EngineApp 帧循环对该宿主的分支（如 Render 返回 false / FitMode.size 0 / 控制器未挂渲染）。

## 建议
1. **captain 官方构建前先修**：黑屏会让"人类可用引擎工具"的 T1 结论打折（能跑但看不见）。
2. 修后复验项：`--open Mania/Phigros/Arcaea 示例.mil` 各截 1 帧（要求：背景+轨道+音符可见；Mania 亮蓝音符/Phigros XY 音符/Arcaea arc 近似可见）。
3. 试玩可在官方 bin 上做视觉确认（他们原定复验项不变，外加黑屏对照）。

## 追溯
- t65 实现报告（eng-coder-vis）§2 验收 "--open 预览播放 ✅ 窗口实跑 exit=0" —— 未覆盖渲染可见性；本复验补上该缺口。
- 我此前对 T1 的复验清单（打开/预览播放/退出/往返/报错）中"预览播放"项 = **不通过**。
---

## 二轮复验（当前源码 1:19 重建 scratch：构建产物\obj\t65-rv2\bin，与官方同源）
| 项 | 结果 |
|---|---|
| --open Mania 4K 示例.mil（当前源码） | ❌ **黑屏仍然存在**：2.0s/3.5s 全采样点 0,0,0（含顶部 640,60——提示层区）；窗口标题/exit=0 正常 |
| 对照 --demo（同 exe 同截法） | ✅ 80,140,255（亮蓝音符）—— 截图/呈现机制正常，排除取证伪影 |
| --preset maimai --headless | ✅ 规则集 Ring·8 键·Maimai 档：命中 10/10 · ACC 1.000（批1 方位断言闭环） |
| eng-coder-vis 声明「预览页顶部提示层 ESC 返回 · AUTO…」 | ⚠ 截帧未见（顶部采样 0,0,0；未见任何 UI 层）——需 eng-coder-vis 本地复核或说明 |

> 二轮结论：**P1（--open 预览黑屏）在最新源码仍复现**，非旧 scratch 伪影。建议：①eng-coder-vis 检查 EnginePreviewHost 的 Render/Present 是否被引擎帧循环调用（对照 EngineGameHost——demo 正常）；②captain 官方构建前优先修复；③修复后我按「背景+轨道+音符可见」标准复验。
---

## 三角色核实 + 根因分析（引擎使用者 1:20:25 二进制实测含 t67 修复）
- 试玩：--preview mania4 1920×1080 同黑（开窗渲染链问题确认）；--open 当前 bin 无画面。
- 我：t65-rv2（DLL 1:20:25，**含 t67『vb.Present()』修复**）--open 仍全黑 0,0,0（对照 --demo 正常）。
- **根因分析**（读码）：`EngineApp.Run` 窗口环 = `host.Render(gfx)` 返回 false 则**不呈现**；`EnginePreviewHost.Render`（EnginePreviewHost.cs:84-115）与 demo 宿主（EngineGameHost 直渲）差异=**嵌套 SoftwareRenderer(1280×720) + 逐像素 FillRect 全屏 blit（≈92 万次/帧）**。t67 只修了内层 Present 顺序，未解决外层呈现问题；嵌套软渲染+逐像素 blit 为最强嫌疑（太慢/像素格式/执行路径未出图）。
- 建议修复：**引擎预览宿主改为直渲**（同 EngineGameHost——按 letterbox 平移到 renderer 直接画，或给 IEngineRenderer 加批量 CopyFrom(SoftwareRenderer) API），废弃嵌套+逐像素路径。
---

## 三轮：t67 直渲版复测 = ✅ 通过（P1 关闭）
- 对象：构建产物\obj\t67-play\bin（LetterboxRenderer 直渲版，1:25:43）。双路径截帧（PrintWindow + CopyFromScreen live）：
  - @2.5s：PW (500,400)=18,24,40（轨道底色）· LIVE (320,360)=**255,210,63（亮黄音符）**；@4.0s 双路径均见轨道底色；live 图（t67-open-live.png）肉眼可见 4 轨+黄色音符+蓝色 hold 长条+判定线 ✅
  - **PrintWindow 也已可见**（不再是 0,0,0）→ 此前三轮 0,0,0 为**真实黑屏**（demo 对照法有效），非抓帧伪影；eng-coder-vis 顾虑的 PrintWindow 兼容问题在直渲版不复现。
- 判定：**T1（--open 预览播放）✅ 修复确认**（scratch 直渲版）；官方 bin 构建后按同标准做终验（我：双路径截帧+像素；试玩：目视）。
---

## 四轮（终验·当前源码 t68-rv）：✅ 三方标准全通过
- 对象：构建产物\obj\t68-rv\bin（当前源码直渲版）。同款 PrintWindow 5 点采样：
  - --open Mania 4K 示例.mil @2.0/3.5s：(500,400)=**80,140,255**（亮蓝音符，与 demo 一致）· (640,60)=15,21,38（顶部提示层可见）· (300,300)/(100,100)=18,24,40（轨道底色）· (640,360)=15,21,38 → **5 点全非 0,0,0**
  - --preview mania4 @2.0/3.5s：同构全可见（3.5s (500,400)=18,24,40 轨道底，帧时刻差异）→ exit=0
- 判定：**T1/T2 链（--open/--preview 预览播放）✅ 通过**；『背景+轨道+音符可见+提示层可见』三方标准达成（eng-coder-vis 实证实显 + 我 5 点采样 + 试玩待官方 bin 目视）。
---

## 五轮（试玩交叉验收）：✅ t65/t66 P1 正式关闭
- 试玩 t67 直渲版四帧交叉：--preview mania4（t67-01）；--open Mania 4K（t67-02）；--open Phigros（t67-03：自由判定线双交叉青线+黄色 XY 音符——XY 近似可见）；--open Arcaea（t67-04：6 轨天2+地4 近似+黄蓝音符/蓝 hold——arc 轨道近似可见；arc 曲线曲率本帧未显，标注近似）。
- **结论：t66 P1（--preview 黑屏）+ t65 P1（--open 黑屏）正式关闭**——直渲修复验收通过（双路径+三模式交叉+我 5 点采样+eng-coder-vis 实测四证合一）。
- **P2 记录：--open 中文路径需 UTF-8 支持（英文路径 OK）**——命令行参数编码（控制台代码页）建议后续统一 UTF-8（批4 文档/README 标注；与我 --open 中文路径实测成功并存，疑为不同调用环境控制台代码页差异）。
---

## 六轮（试玩视觉终验 t68-rv）：✅ T1 正式关闭（全公司口径）
- 试玩四帧（构建产物\obj\engine-tool-verify\t68-01~04，t68-rv/bin/EnginePlay.exe 01:33:47）：--preview mania4（4轨+黄蓝音符+青判定线+完成色带）/ --open Mania 4K（+蓝 hold）/ --open Phigros（自由判定线双交叉+XY 音符=XY 近似确认）/ --open Arcaea（6轨天2+地4+arc 轨道近似确认；曲线曲率本帧未显标注近似）——与我 5 点采样全绿交叉一致。
- **结论：T1（--open/--preview 黑屏）正式关闭**。
- P2 记录追加：②--tool 窗自绘按钮需键盘导航（keybd 导航缺失）——建议批4/后续工具窗体验批；①中文路径 UTF-8 已入批4 文档清单。
---

## 批4（t69）CLI 面验收（t69-play 1:47:27，官方 bin 前）
- ✅ --version=1.0.0-t64（**版本串未随批4 递增，P2 观察**：建议批5 或 t69 收尾 bump）；✅ --help 含 --synthmetronome 帮助行；✅ --check 全 PASS exit=0；✅ --synthmetronome 120 → samples\bpm_120.wav **361,664 B**（16bit mono 44.1kHz，8 拍，首拍重音；.mil 示例谱 audio 指向 samples/bpm_120.wav 的口径已打印）。
- 待批：build-tool.ps1（captain 首跑）→ 两形态发布+模板链路 --export 终验（T2/T3/T6/T7）；--tool 键盘导航（P2）。
---

## 批4 模板链路终验（t69-play + 官方 pack-template.exe）：✅ T2/T3 链通过
- --export Mania 4K 示例.mil → ✅ 打包成功（20 音）→ 产出单文件 exe（67MB）→ 直接运行：**PAINTED=True**（(500,400)=15,20,34 轨道底）exit=0 —— 内嵌谱自动游玩闭环 ✓（T2/T3）。
- --export-mil maimai → ✅ exports\maimai.mil（10 音 · mode=maimai）→ --open 该 mil：**PAINTED=True** exit=0 —— 顺带解决『maimai 示例谱缺失』缺口（T7 途经）✓
- ⚠ **P2 CLI 不一致**：--export help 标注 [out 目录] 但 RunExport 未解析（产物始终落谱面目录）——建议实现 arg2 或改 help；--version 仍 1.0.0-t64（未随批4 bump）。
- 证据：构建产物\obj\t65-reverify\export\（Mania4K-packed.exe / exported-player.png / exports\maimai.mil / open-maimai-mil.png）。
---

## P2 修复复验（当前源码重建 p2-verify）：✅ 全绿
- --version = **1.0.0-t69** ✓；--export 谱面 **--out** 目录 → 产物落指定目录 ✓（p2out\Milestone 试玩 · Mania 4K.exe，20 音）；产出 exe 直接运行 PAINTED=True ((500,400)=15,20,34) exit=0 ✓；guide 导出步已含 [out 目录 | --out <dir>] + 模板放置说明 + FAQ（engine-tool-guide.md:46-53,64,80）✓。
- 至此 P2 清单状态：①中文路径 UTF-8=批4 文档/指南 ✓ ②--tool 键盘导航=批5/工具窗迭代（待派）③--export [out 目录]=已修 ✓ ④版本串=已修 ✓。









