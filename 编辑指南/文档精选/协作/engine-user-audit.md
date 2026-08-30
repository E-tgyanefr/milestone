# 引擎使用者体验盘点：MilestoneEngine.exe 工具完整性 + 各模式预设缺口

> 体验对象：**当前 bin（2026-08-27 23:37:30）** `bin\Release\net8.0-windows\win-x64\Milestone.exe` 与独立引擎 exe
> `引擎\engine\EnginePlay\bin\Release\net8.0-windows\MilestoneEngine.exe`。
> 方法：①实战运行（--check / --demo 截图 / --shellshot 三页 / --shotdemo 8 模式×40 帧自动游玩截图，视觉读图）；
> ②默认值源码核对（ModeSystem.KeyHint / GameSettings / JudgeSettings.ApplyForChart / 各示例谱内容）。
> 说明：①③为实测；②为"自动游玩首印象+默认配置核对"（手玩判定手感需 试玩 角色在真机上补，本报告给出每模式
> 可直接核对/改进的默认值清单）。不改代码；未动 config（全部用临时 outDir 截图）。

---

## ① MilestoneEngine.exe：独立引擎工具形态与缺口

### 实测现状（均实测通过）
| 命令 | 形态 | 实测结果 |
|---|---|---|
| （无参数 / `--demo`）| 引擎窗 4K 自动游玩演示，标题栏提示"（F11 全屏 / ESC 退出）" | ✅ 窗口 1280×720 正常出现（截图见 `构建产物\obj\audit-play\engine-demo-window.png`），自动游玩，退出码 0 |
| `--check` | 无头自检 | ✅ exit 0：引擎切片 16/16 全命中 / EngineJobs · OD8/Phigros/IIDX 档位断言 / 1M 并行一致，全部 PASS |
| `--bench [n]` | EngineJobs 基准 | ✅（README 基线覆盖） |
| `--play <pack>` | 播放**私有 pack 格式**（title+t/lane）| ✅ 可跑，但只认 pack，不认 .mil/.osu |

### 缺口清单（人类可用工具视角）
| # | 缺口 | 证据 | 建议 |
|---|---|---|---|
| T1 | **不能打开谱面文件**：`--play` 只认私有包（`[payload][len][MILSTPK1]`）；用户手上的 .mil/.osu/.mc/.adofai 无法直接预览/游玩 | `EnginePlay\Program.cs:1-17,245-265` | 引擎侧加 `--play <谱面路径>`（ChartParser→4K/多模式 Ruleset）或至少 `--preview <谱面>` 生成 4K 简化预览 |
| T2 | **打包链只支持 4K tap 简化**：`Milestone.exe --pack <谱面>` 把任意模式折叠成 4K(t,lane)——hold→tap、无轨模式按 X×4 映射、l>4 取模；打包 Phigros/maimai/Arcaea 谱面语义失真 | `源码\CoreUtil\PackExporter.cs:30-41`；`PublishPlayer\Program.cs:45-69` | pack 格式升级：支持 hold/多列/模式字段；或文档明示"pack=4K 演示用"并给 UI 入口 |
| T3 | **模板 exe 直接运行=报错且无提示**：`pack-template\MilestonePlayer.exe`（及任何未嵌谱 exe）双击 → 控制台"未找到内嵌谱面"，无窗口/无对话框 | `PublishPlayer\Program.cs:58`（Console 输出） | 无内嵌谱时弹出提示窗口（MessageBox）+ 用法说明，或自动退化为 --demo |
| T4 | **演示无屏上提示**：`--demo` 画面只有轨道+音符+判定线+进度条，无键位说明（实际可按键 D F J K / Z X C V）、无计分/连击/ACC | `EnginePlay\Program.cs:81-113`；截图 engine-demo-window.png | 首屏叠加"按键说明+ESC 退出"；加计分/连击 HUD |
| T5 | **帮助缺失**：无 `--help`/`--version`；用法只写在源码注释 | `EnginePlay\Program.cs:9-17` | 加 `--help` 打印全部命令；错误路径（未知命令/文件缺失）已 catch 但输出仅控制台 |
| T6 | **不可单文件复制**：MilestoneEngine.exe 151KB 框架依赖，需同目录 MilestoneEngine.dll/EnginePlay.dll/deps+runtimeconfig 且装 .NET8 运行时（打包单文件 67MB 才是独立体——但那必须经 T2 打包） | `EnginePlay\bin\Release\net8.0-windows\` 目录清单；实测文件 9 个 | 发布 2 形态：`EnginePlay` 目录版（说明"复制整个文件夹"）+ `--pack` 单文件版；README/发布页写清 |
| T7 | **无示例谱/无内置曲库**：独立引擎 exe 无任何可打开谱面（只有固定 demo）；主程序 `bin\...\win-x64\` 下**没有 Chart 目录**（AppConfig 默认=BaseDirectory\Chart 且自动创建空目录）→ 新装用户首启=空曲库 | `AppConfig.cs:55,88`；实测 bin 目录清单 | 随包内置示例谱目录（首次启动建 Chart 并拷入 Milestone 示例 + 短音频）；或"首启引导：复制示例谱到曲库" |

---

## ② 各模式预设盘点（新用户第一手体验）

默认全局：流速 Speed=2.0 · Offset=0 · 判定基准=音符中心（有 config）/下端（无 config）· 超采样 SS=1.4/48 级。
判定窗口按模式自动套用（`JudgeSettings.ApplyForChart`，源码一致）。
截图证据：`构建产物\obj\audit-play\<mode>\auto_*.png`（每模式 40 帧，自动游玩前 ~2 秒为主）。

| 模式 | 默认键（ModeSystem.KeyHint 屏上无） | 判定窗口（源码） | 实测首印象 / 预设缺口 |
|---|---|---|---|
| Mania 4K | D F J K | 标准2：PERFECT±40/GREAT±80/…MISS200（.mil 无 OD） | ✅ 轨/判定线/receptor 齐全、观感顺眼；⚠ 中段帧出现大白色圆角块（观察项，待试玩复验）；⚠ 示例谱无声（audio:""） |
| Phigros 4K | D F J K（自由位置，到点"位置判定"）| Perfect±80/Good±160/Bad±180 | ✅ 提示"位置判定：鼠标点击音符所在位置"很棒；⚠ 判定线只是细白线，新用户一眼看不出"线=判定位置"；画面过暗/极简（P2 视觉引导） |
| Arcaea 6K | 天2+地4（S D F J K L）| PURE+±25/PURE±50/FAR±100 | ⚠ **视觉杂乱**：白色大四边形+X 形地面纹理+PURE+ 文字+圆圈同时出现，新用户第一眼难懂（P1 视觉）;⚠ Score 与标题在右上角重叠（P2） |
| Cytus 4K | D F J K（自由位置）| PERFECT±75/GOOD±150/BAD±220 | ✅ 简约清晰（判定线+下落音符+TP 显示）；⚠ 判定文字"PERFECT"落在左侧与 KPS/音符 HUD 重叠（P2 布局） |
| IIDX 8K | S D F 空格 J K L ; | PGREAT±16.67/GREAT±33.33/GOOD±116.67/BAD±250 | ⚠ 转盘列（左列斜纹+蓝转盘）突出，但**无"转盘怎么玩"说明**（默认键 S？鼠标？），新用户反直觉（P1）；⚠ "PGREAT"与左侧 HUD 轻微重叠（P2） |
| ADOFAI/Routlock 1K | 空格/D（掉轨重开）| 角度判定 BPM120：PURE≈83ms/PERFECT≈125ms/COUNTED≈167ms | ⚠ **圆盘视图 + 斜线 + 红方块**，无"每拍按一次空格/掉轨重开"说明，新用户不知按哪/什么算正确（P1 引导）；示例谱在测试格式而非示例目录 |
| maimai 8K | 8 分区（鼠标位置判定；键盘 8 键=8 方位）| PERFECT±31.25/GREAT±62.5/GOOD±125 | ⚠ **截图出现中心 5 棕 1 黄圆盘簇**（疑似特殊音符或渲染问题，待试玩确认）；⚠ 8 圆环无方位编号/键位标注（P2 屏上标注）；⚠ 无示例谱（仅测试格式 maimai_test.txt） |
| osu!standard 1K | 鼠标/Z X 空格 | 300/100/50 随 OD（std_test OD5≈49.5/99.5/149.5? 按 stable 公式）| ⚠ **视觉拥挤**：巨大白球+多个圆环+"3"数字圆盘+"300" 文字+顶部录制徽章压住 Score（P1 默认布局/HUD 重叠）；⚠ 4:3 框定区未显示边界（P2） |
| 回环作曲 | D F J K / 鼠标点环 | Cytus 口径 | 无谱面示例在主曲库；LoopComposer 需进入创作而非游玩（P2 门路说明） |
| 多模式示例 | — | — | 标题显示"4K"但画面为无轨场（部件 0 为 Phigros 时 KeyCount 仍显示 4K）→ 部件标题与画面一致性（P2 观察项） |

### 横切缺口（所有模式）
- **P0 示例谱全部无声**：`其他\Chart\Milestone示例\*.mil` 的 `"audio": ""`（实测 6/6 示例均为空）→ 新用户第一手体验=纯打击音效无音乐；且这些示例**不在游戏运行时曲库**（见 T7），需手动复制进 ChartsFolder 或选"曲库管理"指向 `其他\Chart`。
- **P0 首次启动 = 空曲库**：bin 无 Chart 目录；`AppConfig.Load()` 自动建空目录 → 主菜单"开始游戏"→选歌页空态，5 张 Oshama 卡是本机（%LocalAppData%\ChartPlayer\config.json 指定了外部曲库）才有。**没有任何内置"每模式示例"入口**。
- **P1 屏上缺键位提示**：KeyHint（"D F J K"等）只在 `ModeSystem` 数据里 + （部分）底部 toast；游玩画面无"当前键位"浮层；第一次进 IIDX/maimai 的用户不知道按什么。
- **P1 模式示例缺失**：Milestone 示例目录只有 Mania/Phigros/Arcaea/Cytus/IIDX/多模式；**maimai / osu!standard / ADOFAI / 回环作曲 无示例谱**（maimai_test.txt、std_test.osu、adofai_h.mil 都是测试格式）。

---

## ③ 引擎内预览现状（实测/代码核对）

| 链路 | 现状 | 证据 |
|---|---|---|
| 选歌页即点即玩 | ✅ 双击卡片 → `_playChart` → `LoadAndPlay` + `HostContent(_game)` 同窗承载（可见窗口=1） | `MainForm.cs:892-908`；`EngineMainShell.cs:142-154` |
| 编辑器试玩 | ✅ 工具栏"🎮 试玩"+快捷键 → `TestPlay` → GamePanel 同窗承载，ESC 回编辑器（`_testPlayReturn`）| `MainForm.cs:1162-1182`；`ChartEditorPanel.cs:754/1625/3240-3246` |
| 编辑器自动游玩 | ✅ F5 = 编辑器内 `PlayAutoplay`（画布内播）；另有"自动游玩"入口走 GamePanel `StartAutoplayAt`（从播放头开始）同窗承载 | `ChartEditorPanel.cs:8322`；`MainForm.cs:1184-1206` |
| 编辑器 AI 游玩 | ✅ 已接（`TestAiPlay`：0 陪玩 DemoAi / 1-3 陪玩同台） | `MainForm.cs:1208+` |
| 返回 | ✅ ESC 退出游玩 → ExitToMenu → UnhostContent（timer 重启+强制重绘）回引擎菜单 | `EngineMainShell.cs:157-167` |

遗留观察：承载期间引擎 UI timer 停（正确）；**引擎壳当前树已含 t37 脏渲染/背景缓存/1:1 Blit**（`OnPaint` 1342-1382：静止帧跳绘制只 Blit，`DrawBackdropCached`）——比旧版（固定1280×800+全屏双三次）已大幅缓解 CPU；但**当前 bin 的 shell-shot 仍见文字重叠**（见下）。

### 当前 bin 视觉复验（截图 = 现装 bin 实测）
`构建产物\obj\audit-play\shell\*.png`：
- 主菜单：标题渲染为 **"Mila␣stone"**（字符间距/缺失，疑似 run-split 或字体宽度问题）；右上角"游玩/设置/退出"按钮图标与文字重叠（字形挤压）；左下角统计行"⭐ 游玩 11 次…"图标与文字重叠。→ **P0/P1：文字重叠在现装 bin 仍存在**（对应里程碑"修复引擎化内容文字重叠"）。
- 设置页：⚙ 设置 图标与标题重叠；行控件本身整齐（流速/延迟/音量/键位/判定基准/自动游玩）。→ 同 P1。
- 选歌页：卡面整洁；搜索框提示文字正常。

---

## ④ 缺口清单汇总（P0 障碍 / P1 体验 / P2 优化）→ 给引擎团队

### P0（新用户无法开始 / 明显障碍）
1. **示例谱不进运行时曲库 + 首次启动空库**：随包内置/首启引导（把 `其他\Chart\Milestone示例` 作为默认仓库拷入 ChartsFolder，或打包资源）。
2. **示例谱全部无声**：为内置示例生成短音频（或下载/内置 6 首 CC0 短音轨；打包尺寸可控），或至少内置"节拍器音轨"。
3. **引擎 exe 打不开真实谱面**（T1）：`--play` 支持 .mil/.osu/.adofai（引擎侧 1 周内可出：ChartParser 是主程序层，需抽到引擎或宿主宿主——给出双方案）。

### P1（体验/反直觉）
4. **屏上键位提示**：游玩页左上角"键位：D F J K（空格暂停 …）"半透明浮层（前三秒显示 + H 键开关）；对 IIDX 加"转盘=S 键/转盘鼠标"、ADOFAI 加"每拍按空格，错键掉轨重开" 的首屏文案。
5. **Arcaea/maimai 首屏视觉**：Arcaea 默认大四边形与音符/判定文字重叠 → 检查默认 3D 场景与 note 尺寸；maimai 截图中心圆盘簇 → 排查特殊音符（A/B/C/F 等）默认渲染，至少试玩确认。
6. **osu!standard HUD 重叠**：顶部徽章（录制/离线）与 Score 位置碰撞；判定"300/50"与圆环重叠 → 调默认布局坐标。
7. **IIDX/ADOFAI 无"怎么玩"说明**（见 4 的浮层）。
8. **文字重叠（现装 bin 复现）**：主菜单标题 Mila␣stone+右上角按钮+设置页齿轮——**这是 t1 清单的"文字重叠"项在现 bin 的实测复现**，请 编写/引擎编写 对账修复进度。

### P2（优化/细节）
9. maimai 8 圆环方位编号；Cytus/IIDX 判定文字与左 HUD 的轻微碰撞；多模式示例部件标题（KeyCount 显示 vs 画面）一致化；HUD 字号桶化（9f→10f 等）；示例谱加长（20-30 音符 15-20s 更显诚意）。

---

## 附：证据文件索引
- 截图：`构建产物\obj\audit-play\`（engine-demo-window.png / shell\*.png / 8 个模式目录×40 帧 / run.log）
- 引擎入口：`引擎\engine\EnginePlay\Program.cs` / `PackPlay.cs` / `引擎\engine\PublishPlayer\Program.cs`
- 打包：`源码\CoreUtil\PackExporter.cs` / `引擎\engine\App\EngineApp.cs` / `EngineWindow.cs`
- 配置：`源码\CoreUtil\AppConfig.cs` / `源码\Play\GameSettings.cs` / `源码\Play\JudgeSettings.cs`
- 预设：`源码\CoreUtil\ModeSystem.cs` / `引擎\engine\RhythmCore.cs` / `引擎\engine\NoteStyle.cs`
- 承载：`源码\Forms\MainForm.cs`（850-979,1150-1250）/ `源码\Play\EngineUi\EngineMainShell.cs`
- 示例谱：`其他\Chart\Milestone示例\*.mil` / `其他\Chart\测试格式\*.txt|.osu|.adofai`
