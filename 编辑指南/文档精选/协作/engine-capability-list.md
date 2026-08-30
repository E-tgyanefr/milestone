# 引擎能力清单（Living · eng-design-vis 维护）

> 目的：单一来源的「引擎已有什么/缺什么」速查表，供引擎团队（eng-coder-vis）、引擎使用者（t55）、编写（coder-vis）快速定位能力与缺口。
> 维护规则：每次引擎相关任务（设计/实现/盘点）完成时更新本文件「最近更新」与库/缺口条目；与 t3 设计（engine-design-t3.md）、t54 设计（engine-tool-design.md）互链，勿在本文件堆实现细节。
> ⚠️ 2026-08 文件事故记录：一次行尾追加因误用「读尾部+整文件覆写」导致 §1-§7.4 被截断；本文件已按团队对话历史与各任务报告重建（§7.5 后续行/§8 按当时成员增补保留）。如你在此前对该文件 §1-§7.4 有过增量修改且未保留在其他文档，请回告 eng-design-vis 补回。
> 最近更新：2026-08（t57/t64/t65/t67/t68/t66 闭环后重建；补充 t66 残余 backlog）。

---

## 1. 引擎库快速索引（引擎/engine/，MilestoneEngine.csproj，net8.0 纯托管零依赖）

| 能力域 | 核心类型/文件 | 一句话 |
|---|---|---|
| 时间 | EngineTime / EngineStopwatch / BeatMath | 游戏/真实时间分离、变速、暂停、BPM 网格吸附 |
| 判定 | JudgementProfile（16+ 预设 + WindowMultiplier）/ JudgementTracker（SetProfile 热换 + 事件）/ JudgementLog（滚动 200 可视化）/ ScoreBoard | 全主流判定档（Arcaea/Phigros/IIDX/Cytus/ADOFAI/osuMania(OD)/osuStandard/SDVX/maimai/CHUNITHM/太鼓/Cytus2/GC/Lanota/Dynamix） |
| 谱面模型 | ChartData / BpmTimeline / RhythmNote（+Slider/Ring/Spin/Knob） | 变速 BPM、多音符族 |
| 玩法族 | Ruleset（Lane/Line/Ring/Path）+ RulesetFactory + RulesetDescriptor（6 字段 + BuildChart/BuildProfile/BuildContext/RunHeadless） | 最小代价开发新玩法：描述符 → 完整可玩闭环 |
| 起步谱 | StarterChartGenerator（onset→谱面，五模适配，Validator 0 Error 自断言） | 有音频→起步谱的自动化 |
| 输入 | KeyInput / TouchInput（多点）/ HitZone / InputMapper | 键码→列/方位/线 |
| 音频时钟 | AudioClock + IEngineAudioBackend（Bind 已落；**Windows 实现=WinAudioBackend mci，t65 完成**，TimeScale=1.0 文档化） | 谱面时间=音频采样位置+offset |
| 场景/动画 | GameObject/Component/Scene/SceneManager/SceneTransition（Fade/Slide/Wipe/CircleReveal/Beam）/ Transform / Tween / Particles / Engine3D+Cam3D | Unity 式运行时 |
| 场景 UI | UiCanvas/UiPanel/UiLabel/UiButton/UiCard/UiStackLayout/UiGridLayout/UiTabBar/UiScroll + UiTheme（Scale 令牌）+ IUiDraw（9 方法） | 渲染只依赖 IUiDraw；游戏层有 GDI/D2D 两实现；**引擎独立窗=SoftwareDrawAdapter（t57）** |
| 应用层 | EngineApp.Run(IEngineAppHost, opts) / IEngineWindow / IEngineRenderer（9 原语无文字）/ SoftwareRenderer / ViewportPolicy（letterbox+DPI） | 引擎窗 = 跨 OS 抽象 + Win32 实现；文本属宿主层 |
| 练习/诊断 | PracticeSession（段循环/幽灵/变速）/ JudgementLog / Dsp（校准）/ BeatAlignEngine | 练习与校准赋能 |
| Storyboard | Storyboard（ResetTo/HookFired/HookRate）+ StoryboardPlayer | 演出/教学/谱面叙事（P1） |
| 并行/压力 | EngineJobs（Map/Reduce）/ StressCore / StressEngine | 10^7 级并行与压测框架（stress-engine CLI） |
| 交叉 | ChartValidator / ParityCore / HardwareProbe（#if WINDOWS 先例）/ PlatformSockets | 校验/对拍/硬件探测/联机 |
| 引擎文本/UI 适配（t57 批1 已落） | EngineText.cs（GDI P/Invoke：MeasureExtent/Rasterize/DrawString/CodePointCount + 字体 LRU≤64 + 字形光栅 LRU≤128 + #if !WINDOWS 桩）/ SoftwareDrawAdapter.cs : IUiDraw（SoftwareRenderer 帧缓冲直绘 + 裁剪栈委托 + SetViewTransform） | 批1 完成：引擎窗文字/UI 渲染就绪（GDI 灰度 AA；emoji run-split 属宿主层，批2 需要时按 UiGlyphRuns 扩展） |
| 模式预设（t57 批1 已落） | EnginePresets.cs（10 预设：All/Get/BuildRuleset/BuildProfile/BuildSampleChart/BuildContext/RunHeadless/ToDocTable + PresetLayout/PresetBackground） | 批1 完成：RunHeadless 10 预设 acc=1.000（32/32…24/24 全命中无降级）；差异标注 arcaea=Lane 近似/iidx=8 键转盘列/adofai=拐角数据标记 |
| 引擎工具（t64/t65/t67 已落） | Tool/ToolApp.cs（4 页）+ PreviewController + EnginePreviewHost + RulesetRenderer（四族）+ LetterboxRenderer（直渲）+ Tool/ChartImport + MilExport + PackExport + EnginePlay CLI | 引擎工具窗与 CLI 就绪（详见 §2/§4） |

## 2. 可执行形态

| 产物 | 现状 | 缺口（→t54 分批） |
|---|---|---|
| MilestoneEngine.exe（EnginePlay） | **批1+批2+批3+批2b 已完成（t57/t64/t65/t67/t68）**：--tool 无参默认工具窗 4 页/--preset 10 预设 acc=1.000/--preview/--open <path> [--headless]（.mil 导入 9 样本）/--export（单文件打包）/--export-mil <id>（round-trip，maimai 10/10）/--demo/--check/--bench/--play pack/--help/--version（t55 T5 关闭）；WinAudioBackend（mci）已接；转场=立即切换；**批2b 收口已完（t67/t68/t66 复验闭环）**：--export-mil 编译错/--preview·--open 黑屏 P1（LetterboxRenderer 直渲，t66 双路径复验=PrintWindow+live 均画面 OK，P1 关闭）/--size 全链路/errLine 清除 | **批4 待做**：README/guide/build-tool.ps1 + --synthmetronome（P1 节拍音轨）+ T6 两形态发布说明；backlog B1-B10（§8） |
| MilestonePlayer.exe（PublishPlayer 模板 → pack-template.exe） | 单文件播放器，尾部内嵌 [payload][len][magic]；无内嵌谱→MessageBoxW 提示（t65 T3 完成） | 多模式包（P2，B7）；当前恒 4K |
| Milestone.exe（游戏，WinForms+D2D） | 引擎壳 EngineMainShell 承载 GamePanel/ChartEditorPanel；全页面引擎 UI | 无（引擎表现不回迁） |
| PackExporter（游戏层 --pack） | 全 10 模式谱面 → 4K 归一化单文件 exe | 引擎侧复刻已就绪（t65 PackExport）；去重 P1 待 captain（B6） |

## 3. 模式预设现状（Mania4/6/8K/Phigros/Arcaea/Cytus/osu/IIDX/maimai/ADOFAI）

| 维度 | 现状 | 建议 |
|---|---|---|
| 显示名/键数/键位提示 | 游戏层 ModeSystem（20 玩法单一来源） | 保留（显示口径） |
| 判定档 | 引擎 JudgementProfile 全有（分散） | ✅ EnginePresets 注册表统一（t57 批1） |
| Ruleset 映射 | 只覆盖 Lane/Line/Ring/Path 四族 | ✅ EnginePresets 10 预设（t57 批1） |
| 滚动/布局/背景 | 游戏层 GamePanel.Draw* 各自实现 | ✅ 引擎层 PresetLayout/Background 令牌（t57 批1；渲染消费=t64 RulesetRenderer） |
| 示例谱 | 引擎 BuildChart 增强（t57）+ 游戏层 其他/Chart/Milestone示例/（6 文件 .mil） | ✅ 引擎内置生成（BuildSampleChart/--export-mil，t57/t65）；maimai/osustd/adofai 示例可用 --export-mil 落盘（B9 音轨补充中） |

## 4. 已知缺口清单（按优先级，= t54 §1.6/§3）

| 缺口 | 优先级 | 归属批次 | 状态 |
|---|---|---|---|
| 引擎窗文字/UI（EngineText + SoftwareDrawAdapter） | P0 | t54 批1 | ✅ t57 完成 |
| EnginePresets 10 预设注册表 | P0（用户指令②） | t54 批1 | ✅ t57 完成（RunHeadless acc=1.000） |
| EnginePreviewHost + RulesetRenderer（引擎内预览） | P0（用户指令③） | t54 批2 | ✅ t64 完成（PreviewController 拆分） |
| 工具窗 4 页 UI（ToolApp） | P0（用户指令①） | t54 批2 | ✅ t64 完成（转场=立即切换，批2b P2） |
| .mil 导入（ChartImport）+ MilExport + PackExport + 音频后端（WinAudioBackend mci） | P0 | t54 批3 | ✅ t65 完成（9 样本/round-trip/同格式/契约断言） |
| 黑屏 P1 根治（--preview/--open/--preset） | P0 | t54 批2b | ✅ t67/t68 完成（LetterboxRenderer 直渲；t66 双路径复验闭合） |
| README/指南/发布脚本（构建产物/engine-exe 现为空） | P1 | t54 批4 | ⏳（已提案 captain，附 ready-to-paste 草案） |
| 模板无内嵌谱提示（t55 T3） | P0 | 批3附项 | ✅ t65 完成（MessageBoxW+用法+exit 1；--cli 仅控制台；captain 构建后试玩验弹窗） |
| PackExporter 去重 / EngineUiDemoForm 去留 | P1 待 captain | t54 批5 | ⏳（B6） |

## 5. 约束红线（引擎团队必须遵守）

1. 引擎库零 NuGet / 零 WinForms / 零 System.Drawing；Windows 专有代码走 P/Invoke + #if WINDOWS（HardwareProbe 先例）。
2. EngineChecks（EngineChecks.csproj）基线全绿不回退；新增断言可独立、可禁。
3. 引擎编写只写代码不构建（captain 构建）；交付附「构建后验证命令清单」。
4. 文本口径：UI 文本系（码点×字号×1.15 emoji 估宽）与游戏层 UiGlyphRuns 同口径（t34/t6）——引擎 EngineText 需对齐该口径避免测量/绘制偏差。
5. 文字重叠标准（t55 定标准，2026-08）：相邻文本/图标块最小间距 8px（scale≥1）/ 6px（scale<1）；emoji 槽宽=fontSize×1.15+4px，文字 X=槽右+6px；超宽走 AutoShrinkFont/Ellipsis（禁用硬截断）；双色分段 Logo 段间距=6px（品牌分色非缺陷；**实现注意：分段必须以分串测量位移或硬编码段位，禁用整串测量+分串绘制**——t55 复验：实测 Logo 间距 32px=测量宽≠绘制宽根因；eng-coder-vis 原型验证（2026-08）确认 GenericTypographic 无效（单行无尾随空格时 measure/ink 前后不变），修复=分串测量位移+6px，见 engine-user-verify-coords.md）。
6. 图标检出标准（t55 复验补充，2026-08）：引擎壳交互图标统一主蓝色族 #2C6CFF（alpha≥220）绘制于深底，文字=白；离屏取证按色域检出（B>200 且 R<120）；按钮底色与背景色差恒 ≥18/255；未检出/低饱和即视为不符合待修。（注：游玩按钮图标-文字间距后经 4x 复核 ≈5~8px 合规，该项已关闭；取色标准保留供自动化。）

## 6. 互链文档

- 架构：其他/docs/协作/engine-unity-arch.md
- t3 设计（多分辨率/多刷新率/UI/音游内容）：其他/docs/协作/engine-design-t3.md
- t54 设计（工具化/预设/预览/分批）：其他/docs/协作/engine-tool-design.md
- 引擎使用者缺口（人类视角）：其他/docs/协作/engine-user-audit.md（**t55 已产出，合并表见 §7**）
- 压测基线：其他/docs/协作/stress-matrix.md / stress-engine-report.md / stress-final-report.md
- 引擎工具实现报告：t57-实现报告.md（批1+增补）/ t64-实现报告.md（批2）/ t65-实现报告.md+批3细化增补.md（批3）/ t67-实现报告.md（批2b）/ engine-presets-data.md（预设数据基准）

## 7. t55 人类视角缺口合并表（引擎策划决策，2026-08）

### 7.1 引擎工具 T1-T7（→ t54 批次映射）

| t55 缺口 | 决策 | 批次/归属 |
|---|---|---|
| T1 不能打开 .mil/.osu/.adofai | 引擎侧 .mil 子集导入器（样本驱动） | ✅ t65 ChartImport + --open（9 样本） |
| T2 pack 只 4K tap 简化 | 保持 4K 演示口径，文档明示「pack=4K 演示」；多模式包 = P2（B7 包格式 v2） | 批3 文档 + P2 候选 |
| T3 模板 exe 无内嵌谱报错无提示 | 无内嵌谱 → MessageBoxW 提示 + 用法，exit 1；--cli 仅控制台 | ✅ t65（PublishPlayer 附项） |
| T4 --demo 无屏上提示/无计分 HUD | 预览页已带提示层（t64）；--demo 计分/连击 HUD=B5（P2） | 部分完成 + B5 |
| T5 无 --help/--version | --help（12 命令+预设表）/--version | ✅ t64 完成 |
| T6 不可单文件复制（框架依赖） | 发布 2 形态：目录版（复制文件夹+说明）+ --pack 单文件版 | 批4（build-tool.ps1，含两形态说明） |
| T7 无示例谱/首启空库 | 双通道：引擎侧=EnginePresets.BuildSampleChart/--export-mil（✅ t57/t65）；游戏层=发布/首启引导拷贝示例谱→ChartsFolder（**coder-vis 任务，建议 captain 建单**） | 引擎侧 ✅ + 游戏层任务 |

### 7.2 模式预设缺口（→ 引擎/游戏层分工）

| 缺口 | 决策 | 归属 |
|---|---|---|
| 示例谱全部无声（audio:""） | 「节拍器音轨」方案：--synthmetronome 按 BPM 合成 wav（B9，并入批4；零版权零体积），示例谱 audio 指向 samples/bpm_XX.wav | 批4 + 游戏层打包 |
| maimai/osustd/adofai/回环无示例谱 | EnginePresets.BuildSampleChart/--export-mil 覆盖（✅ t57/t65）；游戏层 .mil 转存=建议 coder-vis 按引擎生成内容落盘 | 引擎侧 ✅ + 游戏层任务 |
| IIDX 转盘/ADOFAI 无玩法说明 | DocLine+KeyHint（✅ t57 批1）；屏上浮层=游戏层 GamePanel HUD（前三秒+H 开关） | 引擎侧 ✅ + 游戏层任务 |
| 屏上缺键位提示 | 同上浮层（引擎工具预览页同款=t64 已带） | 游戏层（待排期） |
| 示例谱加长/P2 优化 | 后置登记 | 批5 候选 |

### 7.3 视觉问题（→ 游戏层表现，非引擎库）

| t55 观察 | 决策 | 归属 |
|---|---|---|
| Arcaea 首屏杂乱 / osustd HUD 重叠（徽章压 Score）/ Cytus·IIDX 判定字压左 HUD | GamePanel Draw* + Skin 默认布局；建议排期：试玩 定点复验 → coder-vis 修复默认布局 | 试玩 + coder-vis（t61 视觉修复批） |
| maimai 中心 5棕1黄圆盘簇（疑似特殊音符渲染） | 引擎侧已闭环（§7.5 行）：方位映射缺失=游戏层 DrawMaimai 未消费 StartAngle→SectorPoint | coder-vis（t61 第⑦项）+ 试玩复验 |
| 引擎窗文字重叠 Mila␣stone/⚙图标压标题/⭐统计行 | 标准定案 §5.5/§5.6；修复=coder-vis（t61），复核=引擎使用者/试玩 | coder-vis（t61） |

### 7.4 结论（引擎侧）

- t55 全部 P0/P1 与 t54 批1-4 交叉覆盖：**P0 三项（T7+无声+T1）**=批1（示例谱生成/API ✅）+批3（导入/音频 ✅）+游戏层首启引导（待 captain 排 coder-vis）；**P1 四项**=批1（文案/示例 ✅）+游戏层浮层/布局/文字修复（t61）。
- 引擎批 1/2/3+批2b 已全部闭环；游戏层 4 个视觉/引导任务（首启引导、示例谱+节拍音轨、屏上浮层、默认布局修复）待 captain 排期（t61 进行中，其余依赖其完成）。

### 7.5 §5.5 复验结论（engine-user-verify-coords.md，2026-08）

| 项 | 实测 | 判定 | 归属与决策 |
|---|---|---|---|
| Logo 段间距 | 白 Mile x=96..210 / 蓝 stone x=242..356 → **32px**（非重叠=过大） | 不符合 | coder-vis：Logo 分段偏移常量 6px（分串测量/硬编码段位）——§5.5 实现注意已追加 |
| 右上按钮图标槽宽 | ~~2x 放大复核：游玩图标(936..966)-文字(992..)间距 ≈26px≠6px~~ —— **已作废（旧 bin 23:37:30 数据）**：引擎使用者 4x 独立复核现 bin 图→字间距 ≈5~8px 合规；设置/退出底色差 **9/255 <18**（该子项保留） | ⚠ 拆分 | **图标-文字间距=✅ 关闭**；仅保留取色要求（§5.6 底色差恒 ≥18/255，便于自动化检出），归 coder-vis 色族统一 |
| osustd 徽章压 Score | 重叠区 x=42..78 y=34..50 | 不符合 | coder-vis：Score 锚点下移（0.020W,0.075H）或徽章行上移——坐标在 verify-coords 文档 |
| Cytus/IIDX 判定字压左 HUD | Cytus 贴邻 / IIDX 重叠 x=50..150 y=510..522 | 不符合 | coder-vis：无轨/下落模式判定字默认位按 HitLineY 联动并让出左侧 HUD 带（x≥160 或 y+40） |
| Arcaea 判定字被四边形遮挡 | PURE+ (430..530,170..200) 与四边形上缘交叠 | 不符合 | coder-vis：判定字绘制象限=上层（叠顺序修复）+ 默认 3D 场景 scale/note 尺寸复核；建议试玩真机复验 |
| **maimai 中心圆盘簇** | 棕簇 bbox(542..764,258..494)+黄(614..778,334..494)，环心≈(640,360) | 异常=音符未映射方位落环心 | **引擎侧已闭环（t57 批1 增补 7 断言全绿）**：SectorAngle(0..7)=i×45°/SectorPoint↔AngleToSector 互逆/世界点距圆心=400/maimai 10 音全 RingNote.StartAngle/2 滑星 EndAngle=+2 方位/RunHeadless 10/10 acc=1.000——根因钉死=**游戏层 DrawMaimai 未消费 StartAngle→SectorPoint（渲染坐标映射缺失）**；修=coder-vis（t61 已含），引擎侧辅助已备（RingNote.WorldPoint 存在、RingField.SectorPoint 公开，需扩展可找 eng-coder-vis） |

结论（复验闭环，2026-08 二轮修订）：原 7 项标准项——**『游玩按钮图标-文字 26px』已作废**（现 bin ≈5~8px 合规，引擎使用者 4x 复核定案）；剩余待修=Logo 拼接空隙 32px→按 §5.5「分串测量位移/硬编码段位」修复、设置/退出底色差 9/255（§5.6 取色保留）、osustd/IIDX/Cytus/Arcaea 4 项（坐标见 verify-coords）；maimai 方位映射为唯一引擎侧关联项（批1 增补断言 + 游戏层消费修复双侧闭环）；修复归 coder-vis（t61 默认布局批/色族统一），引擎使用者修复后按模板 1:1 重测收口。

---

## 8. 引擎侧 backlog（待 captain 排期，2026-08）

> 维护：eng-design-vis。P0/P1 已闭环见 §4；本表=低优先/候选/已登记观察项。

| # | 项 | 优先级 | 状态/建议 |
|---|---|---|---|
| B1 | --open 中文路径 UTF-8 | P2 | ✅ **已闭环（试玩复现）**：.NET 命令行 UTF-16 传参正常（ProcessStartInfo 直调成功）；PowerShell 5.1 Start-Process -ArgumentList 失败=cmd/GBK 代码页重编码截断=Console 层限制非引擎缺陷——guide 提示「中文路径用 ProcessStartInfo/UTF-8 控制台直接调用」 |
| B2 | --tool 窗按钮交互：mouse_event/keybd_event/SendMessageW 均未导航 | P0（双修复+内部验证通过，待完整构建+干净桌面窗口级复验） | **根因双修复（t70，2026-08）**：①EngineApp.Run 输入桥（window.Key/Mouse += host.OnKey/OnMouse，EngineApp.cs L89-92）②Win32Window.Show 补 SetForegroundWindow/SetFocus（EngineWindow.cs:247，合成输入需前台）——连带复活 --demo ESC/F11、--play pack ESC、全部引擎窗输入。**路由层已证**：EngineChecks 无窗口导航断言（EngineToolAppChecks.cs:62-66：OnMouse(326,329)down/up→presets/ESC→home）全绿。**终判（2026-08-28 晚）**：engine 侧=路由 OK（EngineChecks 无窗口断言全绿 + Key 路径实测通（demo ESC））；试玩环境=鼠标自动化统一不可达（键盘偶达=环境隔离坐实；a/b 其环境无法物理确认→证据=限制性分类，不判引擎；verify-b2.md+b2-01~09 归档）。**终态：已关闭（2026-08-29 定案）✅**——判定=引擎 OK：①EngineChecks 无窗口导航断言全绿（virtual 326,329→presets/ESC→home=UiCanvas 命中链路已证）②Key 路径实测通（demo ESC）③eng-coder-vis 真机日志实证（bridge.Mouse(164,204)→OnMouse virtual(145,216)=WndProc→桥→host→坐标映射全链正常）④试玩环境 (335,311) 两法未达=run_code/pwsh 会话与桌面 winsta 隔离（WFP 命中但输入消息到其会话前台非引擎窗）——**限制性证据不入引擎判定**；全证据 verify-b2/（b2-01~11）。**双环境收敛（2026-08-29）**：eng-coder-vis 真机实证=真实 DOWN 未达其窗（仅 MOUSEMOVE；same 窗 Key 通）——两个自动化环境均「鼠标注入受限」（系统层，非引擎；引擎链=bridge 生产+映射+断言全绿已证）。**终极破案（2026-08-29 晚·试玩 b2-18/19）**——a）✅ c）✅：SendMessageW 客户区 (335,311) 被**系统 DPI 缩放 ×0.667**（PerMonitorV2：WndProc 收到 (223,208)）→virtual(208,220)=落『引擎自检』空卡（无导航=此前全部失败根因！）；**DPI 反向补偿投递 (503,467) → WndProc (335,312) → virtual(326,330)=模式预设 → 页切换成功**；c) ESC→home ✅（b2-19 主页 B13 禁用卡正常）。**定案=B2 已关闭（引擎链完整三处全活+跨进程窗口级导航实证成功；此前失败=测试方法 DPI 缩放未补偿——非引擎 bug）**。B15=用户侧可选（锦上添花）。原始日志铁证（engine-input.log）：未补偿段 07:54:12.626 WM_LBUTTONDOWN x=223 y=208→virtual(208,220)=落自检空卡（对比根因）；补偿段 07:54:44.595 WM_LBUTTONDOWN x=335 y=312→bridge.Mouse→ToolApp virtual=(326,330) down=True→页切换（b2-18）→ESC→home（b2-19）。「客户区 1896×1021 vs 1920×1080」= Win32 非客户区边框，非缺陷，已登记入 guide FAQ/批5 备注 |
| B3 | --preview --size 800x500 → 显示 1200×750（系统 DPI 1.5 缩放） | 记录 | 非引擎缺陷（Win32 默认 DPI 缩放），记录；如需物理像素=加 PerMonitorV2 或 --size 后按 DPI 换算（P3） |
| B4 | 真转场帧合成（Slide/Fade，批2b 遗留；SceneManager/Transition API 齐备） | P2 | 批5 候选 |
| B5 | --demo T4：计分/连击 HUD 提示层（预览页已带提示层，--demo 仍无） | P2 | 批5 候选 |
| B6 | PackExporter 去重 | P1 | ✅ 批5 架构成立（t73：引擎 PackExport 与游戏层 PackExporter 同格式+归一化不变性断言 5 绿；游戏层 --pack 保留兜底=引擎失败/无模板→游戏层实现明示）；EngineUiDemoForm 去留=仍 P2 待 captain |
| B7 | 多模式包（pack v2 带模式字段；当前恒 4K） | P2 | 批5 候选 |
| B8 | --uitrial/--uidebug 等 dev CLI 核对（t53 §1.3） | P2 | t53 批4 联动 |
| B9 | synthmetronome 剩余（已并入批4 草案；规格已备案：44 字节头/16bit mono 44.1kHz/首拍 0dB 重音/结尾 100ms 静音，默认输出 samples/） | P1 | 批4 |
| B10 | 引擎工具窗内嵌「引擎自检」页（主页自检卡现为提示语） | P3 | 批5 候选 |
| B11 | 工具窗 Tab 焦点管理（键盘导航：Tab+Enter 触发） | P2 | ✅ t73 完成（ToolApp.OnKey VK_TAB 遍历 _focusCards（presets/help，禁用跳过）+Enter/Space 触发+琥珀高亮+ESC 不回归；EngineToolAppChecks 3 断言绿；presets/help 页内 Tab 扩展=P2 批5b 标注） |
| B12 | 批4 文档差异：--help 命令数 | P2 | ✅ 已闭环：实测=13 命令条目+5 子选项（--size/--secs/--headless/--out/--beats）；README+ t69 报告已改『13 条目（子选项另注）』；guide 无命中 |
| B13 | 主页『引擎自检』卡=空实现占位但可点击样式（B2 误点根源） | P2 | ✅ 已实现+截图实证（ToolApp.cs:97,110-128 disabled 样式+提示；B2 误点根源消除；截图 Temp/b2-point.png：模式预设/帮助=高亮可点，引擎自检/打开谱面/导出=灰+提示） |
| B14 | 诊断日志开关化 + --input-selftest（调试资产） | P3 | ✅ t73 完成（EngineDebugLog 静态门控：默认关=MILESTONE_DEBUG=1 或 --debug 开；6 处 Write 替换；实测默认零 IO+开启全链日志；门控断言绿；--input-selftest 保留） |
| B15 | 人工真机终验 4 项 | P3 | ✅ **已闭环（引擎使用者 2026-08-29 终验回执：①=达 ②=达 ③=达 ④=达**——WM 消息级（PostMessage WM_LBUTTONDOWN/UP @335,311 → 像素签名切换：预设页蓝调 (300,120)=38,66,120 出现/主页纯 (10,12,20) → 页面确实切换；ESC→home 三探针回 (10,12,20)；--preview ESC→exit=0；--demo ESC→exit=0）。叠加试玩 b2-18/19（DPI 补偿跨进程导航成功）+ 同进程自测 + 断言全绿——**B2 验证链四方证据完备**；真人物理点击=用户演示时顺手（可选，非必需） |
| B16 | --foldershot P1 回归（t56 删除相关：Graphics.ScaleTransform 崩于 CaptureCanvasFrame/GoTo） | P1 | ✅ 已修复（试玩 t71 实测 EXIT=0（r71 系列，08:37:01 bin 复测）+captain 08:20:09 验证一致——game 层修复归档待 t74 后复验+终验轮全回归确认） |

补充记录：t66 已闭环项（--preset 开窗黑屏亦被 t68-05 直渲修复覆盖；--preset 32/32 全命中/--check 全绿/--version/--help）——引擎工具验证链完整（t65 单元→t67/t68 直渲→t66 人类视看）。

## 9. 迁移后复检清单（2026-08 坑录案：四目录迁移丢 UTF-8 无 BOM 文件 BOM——build-tool.ps1 实证 PS5.1 解析失败根因；与 t73 ④时间戳校验同列为发布脚本验证点）

1. **BOM/编码三件套**：迁移后全部 中文 .ps1/.cs/配置文件 复检——UTF-8 无 BOM 文件写入后确认 BOM/编码（PS5.1 对无 BOM UTF-8 中文解析失败=高频坑；`Get-Content -Raw`+字节头 校验）；建议迁移脚本统一带 BOM 写（或 ASCII-only）。
2. **路径一致性**：脚本/文档/引用 全换新四目录路径（引擎源码/输出产物/编辑指南/其他）——防旧路径残留（B16 类回归先查路径）。
3. **时间戳vs内容**：构建产物 dll/exe 时间戳 vs 源文件（含迁移后）——批5 ④已 fail-fast；迁移后以时间戳校验作哨兵。
4. **样本回归**：样本谱/测试格式在迁移后路径可用性复检（ChartImport 9 样本）。

> 上列随发布脚本 build-tool.ps1 首跑验证 + 试玩/引擎使用者 复验时顺带执行（已在 verify-t71-engine.md 判案段存档）。
