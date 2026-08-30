# ChartPlayer(milestone)项目对接总结

> 生成时间:2026-08-22。供主代理/新 AI 快速接手本项目的完整上下文。

## 一、项目现状(最新,2026-08-22)

| 项目 | 值 |
|---|---|
| 项目根目录 | `D:\Users\etgya\Desktop\milestone`(已从 ChartPlayer 2.0 改名) |
| 项目文件 | `Milestone.csproj`(已从 ChartPlayer.csproj 改名) |
| 构建命令 | `dotnet build Milestone.csproj -c Release`(必须 0 警告 0 错误) |
| 可执行文件 | `bin\Release\net8.0-windows\win-x64\Milestone.exe` |
| 引擎自检 | `Milestone.exe --selfcheck`(exit 0 为通过;含判定/数学/模块行为全套断言) |
| 技术栈 | C# WinForms + SharpDX Direct2D1 硬件加速音游,支持 9 种玩法模式 |
| 用户数据 | `%LOCALAPPDATA%\ChartPlayer\config.json`(刻意保留原名,勿改) |

## 二、近期完成的三大工作线(均已验证)

### 1. 内置玩法复刻 + 引擎化 + 对比闭环(目标已完成)
- 从实机视频/参考源码复刻 9 大玩法:mania、maimai、Phigros、osu!(std/taiko/catch)、Arcaea、Cytus、ADOFAI、taiko 等,要求 100% 还原,经 FrameCompare 逐帧对比达标
- FrameCompare(ONNX ResNet18 + DirectML,位于 tools\FrameCompare):实机帧间自相似基线 0.30-0.37,程序 vs 实机 ≥0.6 视为还原达标
- engine\ 子项目(RhythmCore/JudgementTracker/Playfield 等)由引擎 AI 维护,主代理只验收

### 2. 性能优化(用户要求 GPU 满载、帧率拉满)
- 超采样 SS=1.4(env `CHART_SS`,范围 1~1.6)+ 48 级乒乓放大链(env `CHART_SS_STAGES`,1~96)→ Mania GPU 98-99%、Phigros 32-41%
- 引擎热点优化:事件索引 `_evIndex` 二分查找、speed 场前缀积分缓存、批量填充零分配、`JudgeSettings._levelIndex` 字典缓存、判定引擎断连击版本化缓存
- 已知边界:BitmapRenderTarget 离屏超 4096 会静默回退 WARP(帧率暴跌),超采样须控在上限内

### 3. 代码精简(用户要求"精简全部代码,优化引擎",目标已完成)
- 代码量:初始 22,494 行/45 文件 → 当前 21,618 行/41 文件(-876 行,-4 文件)(**注:此为文件夹重组前口径;重组与拆分见第七节**)
- 删除 4 个零实例化整文件:KeysDialog.cs、JudgeSettingsForm.cs、SkinSettingsForm.cs、SongSelectForm.cs
- 合并重复实现(仅语义逐字等价才合并):Ui.Rounded、ChartParser.ChartExts/ChartZipExts、AdofaiCumulative(×4)、ParentLineTransform(×3)、OsuSelBox(×2)、DrawRibbon(×2)、ApplyEaseStatic 委托、DrawJudgeTicks、taiko don/kat 分支、MaimaiButtonPos、ParseOsu 复用 partial 解析助手、SerializeNotes 的 PtArray<T> 等
- 已论证拒绝的合并(行为不同,勿再尝试):Arcaea 投影中心(_arcCx 游玩区 vs gCx 屏幕中心)、Cytus 音符 Y 边距、各格式拍→毫秒积分、RPE vs 旧版事件字段、各模式 hold/tap 绘制循环

## 三、本次改名操作(最近一次任务,已完成)

- 文件夹 `ChartPlayer 2.0` → `milestone`(65 个子项全部移入,旧文件夹已删除)
- `ChartPlayer.csproj` → `Milestone.csproj`;exe 本来就是 Milestone.exe(AssemblyName=Milestone)
- 105 个文本文件批量替换旧路径/旧项目名:tools\ 下 10 个脚本(引擎自动验收/团队舵手/引擎监控/协作/帧对比闭环/human-run/human-play2/截图游玩/analyze_shot×2)、ScreenCapture.cs、人类试玩\PlaytestSim\Program.cs、docs\协作\给另一个AI的说明.md、research_tmp 参考文件、策划\ 与试玩报告等
- 验证:全量重建 0 警告 0 错误、--selfcheck exit 0、6,137 个文本文件扫描零残留旧名
- 守护脚本已按新路径重启(后台):tools\引擎监控.ps1(每 10s 检测 engine 变化→自动验收)、tools\团队舵手.ps1 watch(每 15s 汇总待办)

**刻意保留未改(兼容性,勿动):**
- C# 命名空间 `namespace ChartPlayer`(约 90 个文件含 engine\,research_tmp 反射工具依赖)
- 运行时数据目录 `%LOCALAPPDATA%\ChartPlayer\config.json` 与排行榜 leaderboard.json(保留用户设置)
- bin\Log、publish\Log 历史日志(存档)

## 四、验证工作流(每次改动后必须执行)

1. `dotnet build Milestone.csproj -c Release` → 0 警告 0 错误
2. `Milestone.exe --selfcheck` → exit 0(判定引擎/数学不变量/模块行为全部通过)
3. 性能回归:nvidia-smi 查 GPU%(Mania ≈99%,Phigros 32-41%;明显低于此值即回归)
4. 实机对比:FrameCompare 跑 --video 实机录像 vs 屏幕采集 截图,达标阈值 ≥0.6

## 五、目录职责与编译边界

- **编译范围**:主项目源码(根 `Program.cs` + `Forms\` `Play\` `Charting\` `Ai\` `Mp\` `CoreUtil\`) + `engine\` 源码参与编译(SDK 风格自动递归,子目录新增 .cs 无需改 csproj)
- **另:engine\Tests\EngineChecks** 为独立自检工程(`dotnet run --project engine\Tests\EngineChecks\EngineChecks.csproj -c Release`)
- **排除编译**(csproj Compile Remove,勿改此边界):示范\osu-master(参考源码)、人类试玩\(试玩AI 管辖)、research_tmp\(调研/抓取)、tools\(工具脚本)、Chart\、段位文件\、插件\、策划\、engine\Tests\
- **职责划分**:engine\ 归引擎 AI;人类试玩\ 归试玩 AI;主代理动主项目源码目录、tools\、docs\协作\
- **协作机制**:docs\协作\消息.md 留言板、待办.md 汇总、给另一个AI的说明.md 协议说明;团队舵手 watch 自动跑构建验收
- **当前源码规模**:主项目 48 文件/23,821 行 + engine 17 文件/4,566 行 = **65 文件/28,387 行**(2026-08-22 晚,含本轮拆分)

## 六、给对接 AI 的注意事项

1. 新会话工作区目录指向 `D:\Users\etgya\Desktop\milestone`(旧路径已不存在)
2. 构建一律用 `Milestone.csproj`,不要再找 ChartPlayer.csproj(历史上曾因此报 MSB1009)
3. 接手前先读 `docs\协作\给另一个AI的说明.md`(职责协议)与本文档
4. 环境测试开关:env `CHART_SS`(1~1.6)、`CHART_SS_STAGES`(1~96)
5. 团队子代理(策划、引擎AI、试玩AI)存在但休眠,唤醒方式见 docs\协作\团队协作组织方案.md
6. 未完成事项见 docs\协作\待办.md 与 任务板.md(以当时内容为准)
7. **最近的目录重构/拆分与等价验证见 `obj\精简执行记录.md`**;回滚点:`obj\src-backup-20260822.zip`(重构前全部源码)+ `obj\*.cs.bak`
8. 已接入本地 AI(Ollama `deepseek-r1-14b` 9.0GB,端口 11434,调用脚本 `tools\AskLocalAi.ps1`)——注意:该模型只胜任单文件开放式代码分析,不胜任清单式规划/校验(6 次实测),勿指望它做结构性任务

## 七、2026-08-22 晚 · 目录重组与拆分(主代理,已完成,等价验证通过)

- **A 目录整理**:41 个根 .cs 按职责入 6 目录:`Forms\`(窗口/面板)、`Play\`(游玩/渲染/判定)、`Charting\`(谱面解析/数据)、`Ai\`(AI陪玩/训练)、`Mp\`(多人)、`CoreUtil\`(通用/配置);`Program.cs` 留根;csproj 无需改(SDK 自动编译,新目录名不撞 Compile Remove 排除项)
- **C 巨型文件拆分(纯文本拆分,零逻辑改动)**:
  - `Play\GamePanel.cs` 6,145 行 → 5 个 partial:`GamePanel.cs`(核心 1,692)/`.Render.cs`(3,543)/`.Judge.cs`(549)/`.Input.cs`(260)/`.Ai.cs`(168)
  - `Charting\ChartEditorPanel.cs` 6,081 行 → 2 文件:`ChartEditorPanel.cs`(主编辑类)+ `ChartEditorPanel.Canvas.cs`(`EditorCanvas` 画布类,独立顶层类)
  - `Forms\SettingsPanel.cs` → +`SettingsPanel.Tabs.cs`(10 个 Build*Tab,partial);`Forms\SongCardView.cs` → +`SongCardView.Support.cs`(SongGroup/ArtBox)
  - `ChartParserExtra.cs` 已 partial,未动
- **B 清理**:`obj\OllamaSetup.exe`(1.5GB)已删;`research_tmp\`(4.6GB)/`示范\`(3.6GB)/`屏幕采集\`(59MB)经用户 2026-08-22 决定**保留**(调研/实机参考素材),勿删
- **等价验证(三重)**:① 逐行多重集对比 4 组拆分 `countDiff=0`(零丢失/零重复,extra 仅包装行);② 原始树(从 src-backup 重建于 `obj\orig-test\`)与重构树均 0 警告 0 错误;③ 两版 `--selfcheck` 输出 **SHA256 完全一致**(`AAAA0A98…`),exit 均为 0 → 结论:**游戏行为与原版一致**
- **教训**:个别文件(如 ChartEditorPanel)PowerShell `Get-Content` 行号不可靠(少算 189 行),大文件拆分建议用**标记字符串/括号匹配**切分并先建 `*.cs.bak` 备份;发现异常立即从备份恢复
