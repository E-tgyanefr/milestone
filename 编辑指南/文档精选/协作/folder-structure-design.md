# 四大分类目录设计（t60 综合定稿）

> 本文件 = captain 盘点定稿（原 58 行：四类目标树 + 归属矩阵 + 引用修复清单 + 执行顺序 + 编辑指南 2 册 + 游戏内容根=BaseDirectory 用户指令）与 designer-vis 深化（逐项判定/实测源码锚点/csproj 改写全文/S0-S10 步骤/编辑指南大纲/PDF 管线/风险/任务建议）的合并版。
> 用户指令：文件按「游戏源码／引擎源码／编辑指南（含 PDF，零基础用户可明白如何更改文件）／输出产物」四类组织，均为同一父文件夹 D:\Users\etgya\Desktop\milestone 下的子文件夹；**游戏内容根=程序当前目录（BaseDirectory）**。

---

## 0. 目标树

```
milestone\
├─ 游戏源码\    Milestone.csproj + app.manifest + Program.cs + 源码\（游戏层全树）+ obj\（游戏工程中间物）
├─ 引擎源码\    引擎\engine\ 全树（MilestoneEngine.csproj / App / Ui / Platform / RhythmCore / JudgementTracker / Samples / Tests(EngineChecks) / EnginePlay / PublishPlayer / HelloEngine）
├─ 编辑指南\    README.md（门户）+ 工程地图.md + 模式与UI更改.md + 引擎改动.md + 打包与命令.md + 零基础指南.md + 文档精选\（其他\docs\协作 全量随迁+精选索引）+ 工具脚本\ + PDF\（工程说明.pdf / 零基础指南.pdf，一册两章方案）
└─ 输出产物\    bin\（开发构建，csproj OutputPath 指向）+ 发布\（单文件 exe + Chart\ 首启内容 + 运行时内容根）+ 单文件\（MilestonePlayer.exe + engine-exe）+ 示例内容\（素材库副本源）+ dev\（构建产物\obj 全量：取证/压测/caprepro 工具）+ logs\（根日志）
    （.agent-teams\ 保留根，不计入四类）
```

**游戏内容根 = 程序当前目录（BaseDirectory，用户指令）**——程序目录结构（发布件自带首启内容）：
```
输出产物\发布\（=程序目录）
├─ Milestone.exe
├─ Chart\            谱面（AppConfig.DefaultChartsFolder=BaseDirectory\Chart；示例谱/段位文件/测试格式并入）
├─ PlayerData\       playerdata.json（性能/存档）+ mp_leaderboard.json（迁移后）
├─ Player\           player.json / skin.json / ai_train.json
├─ Replay\           回放
├─ Log\              会话日志（Logger.LogDir=BaseDirectory\Log）
├─ 屏幕采集\          屏幕采集图片（F12）
└─ 人类试玩\          人类模拟存档
```
现状审计：Chart/PlayerData/Player/Replay/Log/人类试玩 均已随程序（BaseDirectory 派生）✓；需改造 3 处散外路径：见 §2-C。

---

## 1. 归属矩阵（逐项判定）

| 原路径 | 新路径 | 处理 | 说明 |
|---|---|---|---|
| Milestone.csproj / app.manifest / Program.cs | 游戏源码\ | 移动 | 随源码；ProjectReference 改 ..\引擎源码\engine\MilestoneEngine.csproj |
| 源码\ | 游戏源码\源码\ | 移动 | SDK 自动包含（相对 csproj 不变；ItemGroup 重写见 §2-A） |
| obj\（根） | 游戏源码\obj\ | 移动 | 工程中间物随工程 |
| bin\ | 输出产物\bin\ | **csproj OutputPath 指向** | 不搬二进制：Milestone.csproj 增 OutputPath=..\输出产物\bin\（并删除原 bin）；旧 bin 迁移后清理 |
| 引擎\engine\ | 引擎源码\ | 移动 | 内部相对引用（MilestoneEngine.csproj→EnginePlay 等）不变；各工程 obj/bin 随迁 |
| 发布\ | 输出产物\发布\ | 移动 | 单文件 exe + skin.json + 运行数据；桌面快捷方式重建指向 |
| 其他\Chart / 段位文件 | 输出产物\示例内容\ | 移动 | 素材库（源）；**发布时副本进 发布\Chart\（首启即内容，§2-D）** |
| 其他\docs\协作 | 编辑指南\文档精选\ | 移动 | 全量随迁 90+ 篇+精选索引.md（标 Top20 权威篇，不删历史） |
| 其他\README.md / DESIGN.md / REVIEW.md | 编辑指南\ | 移动 | README=工程总览（门户源）；DESIGN/REVIEW=设计/评审 |
| 其他\打包命令.txt / _min*.ps1 | 编辑指南\工具脚本\ | 移动 | 内容并入 打包与命令.md；_min* 注明用途 |
| 其他\library_rating.json | 输出产物\示例内容\ | 移动 | 样例评级数据 |
| 构建产物\ | 输出产物\dev\ | 移动 | 取证/压测/工具（caprepro/geo-test/freeze-check 小工程自持 bin/obj 随迁，子目录原名保留防映射失效） |
| 根 selfcheck.log / engine-check.log / aidetect.log / test.pack | 输出产物\logs\ | 移动 | 根日志归档；test.pack=单文件打包测试件 |
| .agent-teams\ | 保留根 | 不动 | 团队运行时状态 |
| 旧壳（bin/obj/其他/引擎/构建产物/发布 清空后） | 删除 | | §3 步骤末确认空后删 |

已实测需额外处理的代码引用（精确锚点）：
- caprepro\Program.cs:155 硬编码 "D:/Users/etgya/Desktop/milestone/发布/Milestone.exe" → 改相对路径 ..\..\..\输出产物\发布\Milestone.exe（随 dev 迁移后相对化）。
- Milestone.csproj 现有「分类目录」ItemGroup（L31-50）与 ProjectReference：改写见 §2-A。

---

## 2. 引用修复清单（执行时逐一；每项附验证）

### 2-A Milestone.csproj 改写（唯一必改工程文件）
1. ProjectReference：\引擎\engine\MilestoneEngine.csproj → \..\引擎源码\engine\MilestoneEngine.csproj。
2. ItemGroup「分类目录」重写：目录外移后，删除 Remove 其他\**、引擎\engine\**\*.cs、引擎\engine\Tests\**、引擎\engine\HelloEngine\**、引擎\engine\**\obj\**、引擎\engine\**\bin\**、构建产物\**、docs\**、logs\**、Chart\**、段位文件\**、Player\**、.agent-teams\**；保留一条 Include → \..\引擎源码\engine\Samples\**\*.cs（引擎样本仍编入游戏；t6 DemoRunner 契约）。
3. OutputPath=..\输出产物\bin\（IntermediateOutputPath 保持默认 obj\）。
4. PropertyGroup 其余（PublishSingleFile/win-x64/app.manifest/Version）不动；Publish 输出目录命令内改为 -o ../输出产物/发布。
5. 验证：dotnet build 0/0；产物 输出产物\bin\Release\net8.0-windows\win-x64\Milestone.exe 存在且 Milestone.dll 含 DemoRunner 类型（反射/ildasm 抽查）。

### 2-B engine 侧工程
引擎源码\ 整树内相对关系不变；若个别 csproj 有上级/绝对引用（grep ProjectReference 与 Include 的 ..\ 引擎源码）则同步修正；EngineChecks/PublishPlayer 命令工作目录改 引擎源码\engine\Tests\EngineChecks 与对应工程目录。验证：EngineChecks 构建+全绿 exit 0。

### 2-C 代码路径改造（3 处，随 t62 与迁移同批；captain 定稿项）
1. 源码\Mp\MpLeaderboard.cs:23：LocalApplicationData → AppDomain.BaseDirectory\PlayerData\mp_leaderboard.json（旧数据迁移：copy 旧文件到新位置，读时兼容）。
2. 源码\CoreUtil\ScreenCapture.cs：CaptureDir 定义（文件顶部）→ BaseDirectory\屏幕采集\。
3. AppConfig.cs:53 config.json 保留 LocalApplicationData\ChartPlayer 惯例（程序配置非游戏内容）；但**迁移脚本重置当前用户 config 的 ChartsFolder=BaseDirectory\Chart**（用户指令：谱面文件夹默认在程序当前目录）。
4. 验证：启动后 PlayerData\mp_leaderboard.json 生成、F12 截图落 屏幕采集\、曲库默认指向 exe 旁 Chart。

### 2-D 运行时与发布
- Logger.LogDir / AppConfig 各默认目录：BaseDirectory 派生，零改动（随 exe）。
- 发布件首启内容：示例谱（其他\Chart\实例谱面）、段位文件（.mc）、皮肤（Chart\皮肤\osu_default）拷贝进 发布\Chart\（发行脚本固化：copy-item 示例内容\Chart → 发布\Chart）。
- 桌面快捷方式重建：Target=输出产物\发布\Milestone.exe（或 输出产物\单文件\MilestonePlayer.exe）；记录到迁移报告。
- 文档内链接政策：历史报告正文路径保持原样（历史事实），编辑指南\README 附「路径映射表」（附录）；活跃维护文档（本设计/editor-trilab/layout-custom-design/t53-修正/engine-* 说明/指南 7 篇）就地替换。

### 2-E 执行步骤与验证（单批机械迁移）
| 步 | 动作 | 验证 |
|---|---|---|
| S0 | 快照：全树清单+关键 exe/dll 哈希+Find *.exe（定位 engine-exe）；备份原 csproj/config.json | 快照落 输出产物\dev\迁移快照\ |
| S1 | 停进程（Milestone/Engine/工作台占用 bin） | 无占用 |
| S2 | 代码先行：csproj 改写 + caprepro:155 + 3 处代码路径 + config 重置脚本 | git 无仓库→备份；diff 记录 |
| S3 | 机械移动（robocopy /MOVE /V 或 Copy+Delete 校验）：引擎源码→游戏源码→编辑指南→输出产物（dev\ 数 GB 预计 ≤10min，失败目录单列重试） | 每步文件数/大小=快照 |
| S4 | dotnet build -c Release（游戏）+ engine 各工程 | 0 警告 0 错误 |
| S5 | 游戏 --selfcheck exit 0；EngineChecks 全绿 | exit 0 |
| S6 | parity -1（-o 输出产物\dev\parity-new） | 全绿 |
| S7 | 重新发布：publish -o 输出产物\发布\+engine publish 至 输出产物\单文件\；pack-template 重打包 | 单文件可运行 |
| S8 | 冒烟：启动 输出产物\发布\Milestone.exe → 自动建 Chart/Player/PlayerData/Log/屏幕采集/人类试玩；曲库初始=发布\Chart（首启内容可见）；F12 截图落新目录；段位页 .mc 可见 | 全通 + shellshot 截图 |
| S9 | 快捷方式重建 + Logger.OpenLog | 新 exe 旁 Log |
| S10 | 删旧空壳 + 更新编辑指南\README 路径映射 + 迁移记录.md（含全部验证结果） | 根=4 类 + .agent-teams |

---

## 3. 执行顺序（与在跑任务互锁）

1. 等 t56（legacy 删除批1+批2）与 t57（引擎批1）完成并各构建 0/0 + t59（视觉验证）收尾；
2. 快照/备份（S0）；
3. PowerShell 迁移脚本（移动 + csproj/代码引用批量替换 + config 重置）；
4. 构建冒烟（0/0 + selfcheck 0 + EngineChecks 绿）；
5. 重新发布单文件 + 快捷方式；
6. parity -1 + 试玩对 输出产物\发布\ 终验一轮；
7. 编辑指南 + PDF（t62）。
理由：迁移会动 bin\ 与 引擎\engine\，与在飞任务并行会互相打断；legacy 删除/引擎批1 完成后路径一次性适配（避免两次搬家）。**迁移→终验→PDF 串行，期间无并行 task。**

---

## 4. 编辑指南大纲（md 源 7 篇 → 2 册 PDF）

目录：编辑指南\README.md、工程地图.md、模式与UI更改.md、引擎改动.md、打包与命令.md、零基础指南.md、文档精选\（索引+随迁）。

a) **工程地图.md**：四目录树+关键文件表（游戏层：Program.cs CLI 入口表/源码\CoreUtil\（UiText 文案唯一集中地、AppConfig、Logger、ModeSystem 模式注册表、ChartParser 族）/源码\Forms\（MainForm 宿主、SettingsPanel、CalibrationForm、联机）/源码\Play\（GamePanel 游玩渲染、EngineUi\ 引擎壳+次级页+D2D/GDI 适配、UiGlyphRuns 图标口径）/源码\Charting\（ChartEditorPanel）；引擎层：engine\README.md 文件表（Engine3D/RhythmCore/Playfield/InputCore/Ruleset/Scene/GameLoop…）+ App\（EngineApp/ViewportPolicy）/Ui\（UiComponents/UiTheme/UiCanvas）/Platform\（SoftwareRenderer/Win32Window）/Tests\；数据：谱面格式 .mil/.osu/.aff/.mc、皮肤 skin.json）。
b) **模式与UI更改.md**（=工程说明.pdf 中章）：加/改模式=ModeSystem 添加 ModeInfo → ChartParser 格式 → GamePanel.Draw 新函数（参考 DrawMania）→ 判定 Ruleset/JudgementProfile 预设（RhythmCore 16 预设）→ 示例谱（.mil 放 示例内容\）→ 还原度对比流程；改模式预设=JudgementProfile 档位/窗口乘数/EnginePresets（t57 后）；改 UI 文字=UiText 表（严禁硬编码字符串）；改布局=场景/控件代码（UiPanel/UiButton/UiStackLayout + UiMeasure 自适应）；改主题=UiTheme 令牌（Bg/Surface/Accent/Scaled(k)）；图标=UiGlyphRuns 口径（测量=推进=绘制三端一致）；分辨率=ViewportPolicy/letterbox（t3/t6）。
c) **引擎改动.md**：Ruleset 派生（Lane/Line/Ring/Path 四族）；判定（JudgementTracker 二分下界/窗口乘数/档位热换 SetProfile）；渲染（IUiDraw 适配器约定、渲染虚拟化、缓存）；Storyboard/判定可视化/PracticeSession；回填 EngineChecks 断言流程与还原度对比。
d) **打包与命令.md**：dotnet build/publish 表（§2-A 后新路径）+ 单文件说明（PublishSingleFile+IncludeNativeLibrariesForSelfExtract）+ 快捷方式；dev CLI 全表（--selfcheck/--parity -1/--shellshot/--menushot/--stresstest/--gamestress/--humantest/--autoshot/--engine-* 与输出目录约定）；日志（LogDir 随 exe）；本地 AI 已禁用（规则检查可用）说明。
e) **零基础指南.md**（=零基础指南.pdf）：四目录是什么；4 个 walkthrough（每步含 改哪个文件哪一行/如何验证/错了怎么办）：任务1 改主菜单按钮文字（UiText.cs 常量→build→运行）；任务2 加一个音游模式预设（ModeSystem+预设档，改/加示例）；任务3 改主题色（UiTheme 令牌）；任务4 打包并生成桌面快捷方式（publish 单文件+快捷方式）。附加任务：做一张 4K Mania 示例谱并玩到（复制 示例内容\Chart\*.osu → 改 bpm/音符数组 → 曲库 PickFolder → 游玩）。
f) **README.md（门户）**：四目录一图流程+快速开始（构建/运行/打包）+路径映射表+精选索引（Top20：editor-trilab/layout-custom-design/t53-修正/engine-* 说明/还原度总结/主题评审）。
g) **PDF 分册**：工程说明.pdf = a+b+c+d 合并；零基础指南.pdf = e + b 快速上手章。

---

## 5. PDF 工具链（已探测可用）

- 主选 **Edge headless print-to-pdf**：已确认 C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe 存在；链路 HTML→msedge --headless --disable-gpu --no-pdf-header-footer --print-to-pdf=out.pdf file:///…html（captain 已验证中文渲染链路）。
- **md→HTML**：本地无 pandoc → 设计 工具脚本\md2html.ps1（子集转换：标题/表格/代码块/粗体/链接 + @page 页边距样式 + 两册封面/目录页）；若可联网可换 pandoc（可选）。
- 备用 **Word COM**：已确认 WINWORD.EXE 存在；Word → Documents.Add → 导入 HTML（保格式）→ ExportAsFixedFormat(17, pdf)；中文渲染最稳、慢（30-60s/篇）。
- 验证：页数>0、试玩视觉抽查 3 页无方块/无粘连（沿用 md 渲染质量基线）、文本可抽取。

---

## 6. 任务建议

- **t61（迁移实现，给 coder-vis）**：§2-E S0-S10 单批执行；交付 迁移记录.md（快照+每步验证+csproj diff+四目录树证明）；验收=S4-S9 全绿+试玩终验一轮（对 输出产物\发布\）。
- **t62（编辑指南+PDF，给 coder-vis/captain）**：§4 七篇撰写/汇编（大纲由策划供稿、编写执行）+文档精选索引+md2html.ps1+Edge 两册 PDF+样张验证；验收=零基础任务 1/2/4 按文档一次成功。
- 与 t54 关系：引擎能力清单/工具设计定稿后，指南补一节引擎工具 CLI 用法；不阻塞迁移。

## 7. 风险与护栏

R1 证据路径断链（90+ 历史报告引 构建产物\obj\…）：正文保留原文+README 映射表，不批量改历史文本。
R2 迁移与在飞任务互扰：串行编排（§3）+S1 停进程。
R3 csproj 改写丢 Samples/重复编译：S2 备份+S4 构建后 DemoRunner 类型抽查+EngineChecks 绿。
R4 大型证据目录转移失败：robocopy /MOVE /V 逐目录+三步校验（数量/大小/哈希抽样），失败目录单列重试。
R5 快捷方式丢失：迁移报告写精确新路径+命令表。
R6 engine-exe 定位不确定：S0 Find *.exe 快照先行（候选 引擎\engine\PublishPlayer\bin\Release\net8.0-windows\win-x64\）。
R7 caprepro:155 与 3 处散外路径漏改：S2 grep 白名单清单（已完成定位）逐项确认，S8 冒烟兜底。

---

## 附录：路径映射表（旧 → 新，供编辑指南/迁移记录引用）

| 旧 | 新 |
|---|---|
| 源码\ | 游戏源码\源码\ |
| Milestone.csproj / Program.cs / app.manifest | 游戏源码\ |
| obj\ | 游戏源码\obj\ |
| bin\ | 输出产物\bin\（csproj OutputPath） |
| 引擎\engine\ | 引擎源码\ |
| 发布\ | 输出产物\发布\ |
| 其他\docs\协作 | 编辑指南\文档精选\ |
| 其他\Chart\ | 输出产物\示例内容\Chart\（副本进 发布\Chart\） |
| 其他\段位文件\ | 输出产物\示例内容\段位文件\（副本进 发布\Chart\段位\） |
| 其他\README.md / DESIGN.md | 编辑指南\ |
| 其他\打包命令.txt / _min*.ps1 | 编辑指南\工具脚本\ |
| 构建产物\ | 输出产物\dev\ |
| 根日志/test.pack | 输出产物\logs\ |
| （运行时）Chart/PlayerData/Player/Replay/Log/屏幕采集/人类试玩 | 随 exe（BaseDirectory），发布件自带 |
| LocalApplicationData\ChartPlayer\config.json | 保留（config 重设 ChartsFolder=BaseDirectory\Chart） |
