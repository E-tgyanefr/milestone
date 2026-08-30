# 给另一个 AI(DeepSeek)的协作说明

你好!我是同一个项目(ChartPlayer,位于 `D:\Users\etgya\Desktop\milestone`)的主代理/验收方。我们通过**共享文件系统 + 约定文件**协作,职责分离:

## 分工
| 角色 | 你 | 我(验收方) |
|---|---|---|
| 引擎源码 | **只由你修改** `engine\` 目录 | 不碰(用户明确要求) |
| 自检 | 你实现时自己可跑 `dotnet run --project engine\Tests\EngineChecks\EngineChecks.csproj -c Release` | 每次你改完,我自动重跑官方自检 + 主项目构建 |
| 验收反馈 | 读 | 写 |

## 你的改动范围(现状)
`engine\` 下这些文件是引擎本体,你自由改/加文件:
- 核心:Engine3D.cs / Transform.cs / EngineTime.cs / RhythmCore.cs / Playfield.cs / InputCore.cs / AudioClock.cs / Particles.cs / Tween.cs / ChartModel.cs / ScoreBoard.cs / JudgementTracker.cs / Cam3D.cs
- 示例:Samples\DemoRulesets.cs / Samples\DemoRunner.cs(自检断言)/ Samples\MiniMania*.cs(可玩示例,依赖项目根 IRenderer)
- 自检入口:Tests\EngineChecks\EngineChecks.csproj + Program.cs(调用 DemoRunner.RunAll())
- 注意:`engine\Tests\**` 已从主项目编译排除,不会污染主程序

## 协作协议(每次迭代)
1. 你修改/新增 `engine\` 下文件,并保证 `DemoRunner.RunAll()` 全部断言通过(那是我们的共同验收标准)。
2. 改完告诉我方(通过用户说一声,或直接改文件)。
3. 我自动跑:官方自检(EngineChecks)+ 主项目构建,结果写入 **`docs\协作\验收反馈.md`**(含结论、自检输出、失败定位:断言名/文件/行号)。
4. 你读反馈继续迭代。

## 若你无法访问磁盘(仅聊天窗口)
你只需输出**完整的新版文件内容**(说明覆盖哪个文件),由用户保存到对应路径;我检测到变化后自动验收,反馈照旧写 `docs\协作\验收反馈.md`。

## 协作脚本(统一入口,推荐双方使用)
`tools\协作.ps1` 是协作中枢(PS 5.1/7 均可):
| 命令 | 用途 |
|---|---|
| `协作.ps1 status` | 状态总览(验收结论/指令时间/消息/监控) |
| `协作.ps1 say "内容"` | 留言(默认主代理身份;加 `-Who 引擎AI` 换身份) |
| `协作.ps1 msg` | 查看消息队列 |
| `协作.ps1 commands` | 查看主代理下达的指令 |
| `协作.ps1 check` | 手动验收(有变化才跑) |
| `协作.ps1 done "完成说明"` | **完成一轮:留言 + 立即验收**(推荐每次改完用) |
| `协作.ps1 watch` | 进入监控循环(每 10 秒自动验收) |

例:`powershell -NoProfile -ExecutionPolicy Bypass -File tools\协作.ps1 done "指令2完成:滑条/长条判定闭环已实现"`

## 验收标准(共同基线)
`DemoRunner.RunAll()` 当前覆盖:三个玩法 Demo(mania/maimai/Phigros)、ScoreBoard 结算、新能力(变速吸附/路径最近点/相机拾取/乒乓补间等)、数学不变量、16 判定预设、输入/杂项、谱面模型、判定追踪器——全部通过才算完成一轮。
