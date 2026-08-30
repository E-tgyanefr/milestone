# 引擎官方终验 verify-t71（captain 指令）：MilestoneEngine.exe 官方 bin + build-tool.ps1 首跑

> 执行：引擎使用者 · 2026-08-28 · 官方 EXE=引擎\engine\EnginePlay\bin\Release\net8.0-windows\MilestoneEngine.exe（07:38:29 · 1.0.0-t69 · 含桥/直渲/批4）
> 证据：`构建产物\obj\verify-t71\`（shots\ 8 张 PNG / shipping-export\ / buildlog *.txt）· 发布产物 `构建产物\engine-exe\engine-tool\`（目录版）+ `engine-tool-standalone\`（单文件版）

## ① official-verify.ps1（四模式双帧 PW 采样 + help 计数 + 错误路径）
| 用例 | frame1/(500,400) | frame2 | 判定 |
|---|---|---|---|
| --open Mania 4K 示例.mil | 18,24,40（轨道底）| **80,140,255（音符）** | ✅ |
| --open Phigros 示例.mil | 11,14,26（深底）| 11,14,26 + **(640,360)=255,210,63（黄色 XY 音符）** | ✅ |
| --open Arcaea 示例.mil | 59,101,183（蓝）| 100,133,187（蓝=arc/轨道）| ✅ |
| --preview mania4 | 首次脚本运行 NO-WINDOW/exit-1（当时 build-tool 后台 publish 抢占资源）；**重测 2/2 PAINTED=True**（18,24,40）exit=0 | ✅（瞬态负载 flake，非缺陷）|
| --help | 命令条目=**13**（口径一致）| ✅ |
| 错误路径 `--open D:\no\such.mil` | `打开失败：文件不存在：…` | ✅ |

## ② build-tool.ps1 首跑（含 2 个前置缺陷修复后成功）
- **P1-①（build-tool.ps1 模板源路径缺陷）**：`[3/6] 拷贝 pack-template.exe` 源= `引擎\engine\PublishPlayer\bin\Release\net8.0-windows\win-x64\pack-template.exe`——**该路径不存在**（`dotnet build PublishPlayer` 只产出 MilestonePlayer.exe 9.6MB 框架版；67MB 单文件模板实际在 `构建产物\pack-template\MilestonePlayer.exe`）→ 首次运行 [3/6] WARN，目录版**无模板**（--export 即坏）。复验处置：把模板放到脚本期望路径后重跑 → [OK]。**建议修**：build-tool.ps1 模板源改为 `构建产物\pack-template\MilestonePlayer.exe`（或先 publish 生成）；否则一键发布产物缺模板。
- **P1-②（编码缺陷）**：build-tool.ps1 为 UTF-8 无 BOM → **Windows PowerShell 5.1（powershell -File）解析报错**（Missing closing '}' 假象）；pwsh 7 正常。建议：脚本保存加 BOM 或 README/guide 注明 `pwsh` 运行（本次已加 BOM）。
- 修复后全流程 exit=0：EngineChecks **全量断言绿**（含批1-4 断言组）→ publish 目录版 → 模板拷贝 → samples/ 生成（bpm_100/120/140/150 四档 WAV）→ README/USAGE 拷贝 → publish 单文件版（1 条 IL3002 warning：GetHINSTANCE 单文件下可能返回 -1——实测未破坏窗口，见③）。

## ③ 目录版 / 单文件版 / 模板链路
| 项 | 结果 |
|---|---|
| 目录版结构（engine-tool/）| ✅ 全部运行时 dll + pack-template.exe (67.7MB) + samples\bpm_100/120/140/150.wav + ENGINE-README.md + USAGE.md + MilestoneEngine.exe（EnginePlay.exe+MilestoneEngine.exe 双保险）|
| 目录版 --version / --help | ✅ 1.0.0-t69 / 命令条目 13 |
| 目录版 --export --out | ✅ 打包成功 → 67,675,037B 单文件 exe → 运行 **PAINTED=True** (15,20,34) exit=0 |
| 单文件版（engine-tool-standalone/）| ✅ 6 文件（MilestoneEngine.exe 67MB + pack-template.exe + 少量）· --version=1.0.0-t69 · **--demo PAINTED=True exit=0（IL3002 未破坏窗口）** |
| 单文件版 --preview mania4 | ⚠ 画面 PAINTED=True 但 **exit=-1**（静默）——画面正常、进程异常退出；目录版/官方版同命令 exit=0 → **P2：单文件版预览关闭路径待查**（建议 eng-coder-vis 排查 close/OnClose 在单文件下的异常，疑似 GetHINSTANCE 相关或终结器）|
| 模板链路 | ✅ 官方 exe 需模板与 exe 同目录（发布目录已被 07:38 重建清空——复验时补回了模板；build-tool.ps1 产出即正确）。 |

## ④ --synthmetronome 规格
✅ `--synthmetronome 120` → samples\bpm_120.wav **361,664B**（16bit mono 44.1kHz·8 拍·首拍重音/后续-6dB/100ms 尾静音）；build-tool 自动生成 100/120/140/150 四档（432,224/361,664/311,264/291,104 B）。

## ⑤ 待试玩交叉目视
建议试玩对 `构建产物\obj\verify-t71\shots\preview-1.png`（--preview mania4 官方版）与 arcaea-b/phigros-b 目视确认；或对官方 exe 自拍。我侧四模式像素+画面已全绿。

## 结论与遗留
- ① 四模式打开/预览：✅（脚本级/像素级）
- ② 发布脚本：⚠ 首跑暴露 2 缺陷（模板源路径 P1、编码 P1），修复（放模板+加 BOM）后全绿；**建议修 build-tool.ps1 两处**（模板源=bootstrap 路径；加 BOM）再由 captain 复跑
- ③ T2/T3/T6：目录版 ✅；模板链路 ✅；单文件版 ⚠ **--preview exit=-1 P2 待查**（--demo 正常）
- ④ T7 示例谱音频：✅（4 档 BPM 音轨随包；--export-mil 可补示例谱）
- ⑤ 试玩目视待执行
- P2 存量：--tool 键盘导航（批5）；中文路径 UTF-8（文档已覆盖）

---

## 迁移坑判案（新增，t73/批5 已记）
> **四目录迁移对 UTF-8 无 BOM 文件的 BOM 丢失坑**：t71 修复的 BOM（原 构建产物\engine-exe\build-tool.ps1）在迁移到 输出产物\dev\engine-exe 后丢失（首字节 35,32,61 = '##' 无 BOM）→ PS5.1 解析失败复现；已重新补 EF BB BF（双源：我复验路径 + eng-coder-vis 发布源）。**复检清单建议**（随批5 入 README/协作文档）：迁移后对含中文 .ps1/.cs 等文件复检 BOM/编码/路径三件套。

---

## T1 终验正式放行（试玩三张目视交叉，2026-08-28）
> 试玩目视：preview-1.png（--preview mania4：4 轨+黄蓝音符+青判定线+绿完成带）/ open-phigros-b.png（自由判定线双交叉+黄色 XY 音符）/ open-arcaea-b.png（6 轨近似 4地面+2天空+黄蓝音符/蓝 hold）——与我像素采样交叉一致 → **T1 终验正式放行 ✅**。0748 全套可选项（t61 五坐标关键帧 maimai 环上/Arcaea quad）已由 0820 版复核覆盖，不再抽帧。


