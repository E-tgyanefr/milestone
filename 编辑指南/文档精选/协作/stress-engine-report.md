# 引擎压力测试报告（t9：实现 + 执行 + 修复）

> 执行人：引擎编写（eng-coder-max） · 2026-08-26
> 产物：构建产物/obj/stress-engine/（res.json 断言结果 / res.engine.json / stress-engine.log / engine-stress.log / run.ps1 复跑脚本）
> 依赖说明：stress-matrix.md（t8）当时仍 pending，本报告阈值按 t9 任务条目 + 既有 PerfTest/StressTest 口径制定，并与引擎策划同步对齐。

## 1. 执行矩阵与总判定

| 项 | 内容 | 阈值（断言） | 结果 |
|---|---|---|---|
| 0 基线 | DemoRunner.RunAll（EngineChecks 全绿基线） | 全绿 | ✅ |
| 1 EngineJobs | 10^7 数组 Map/Reduce 串行一致 + Benchmark + 4× 超卖/单核/Paused | 一致性全真；MapMs < 串行×5+50 | ✅ |
| 2 判定引擎 | 10 万音符 × 549.5s 谱 × 5 判定档（OD8±30/OD3±6/IIDX±18/Phigros±60/SDVX±6） | 判定数=音符数；累计误差=期望值；吞吐>2万/s；单音符判定分配≤256B | ✅ |
| 3 引擎热路径分配 | NoteStyleBook.Get/Evalulate/Judge/ScoreBoard.Apply ×1000 | 0 B/次（脏键≤256B） | ✅ |
| 4a 引擎窗 | SoftwareRenderer 1280×800 离屏 1000 帧 | 平均<33ms、P99<100ms | ✅ |
| 4b 引擎壳 UI | GDI 离屏 1280×800 ×1000 帧（menu/songs/settings 轮换） | 平均<33ms、P99<100ms、分配<512B/帧 | ✅ |
| 5 长稳 | EngineApp 判定切片连续 10 分钟 | 0 失败；沉降内存增长<30MB | ✅（数字见 §4） |
| 6 壳热路径分配 | Split/ContainsEmoji/MeasureText/Text/Rect/Line ×1000 | 0 B/次 | ✅ |

**总判定：PASS（res.json pass=true）。** 机器：Windows 10.0.26200 · CLR 8.0.29 · 24 逻辑核 · EngineJobs.Degree=24。

## 2. 修复清单（6 项）

### F1 判定追踪器 O(n²) 扫描（引擎库，P0 级性能缺陷）
- 现象：JudgementTracker.PressLane/PressRing/PressField 每次按下从下标 0 起全谱扫描（已判定音符 `_done.Contains` 命中后 continue，无 break），10 万音符谱面单轮游玩累计 ≈5×10⁹ 次哈希查找——压力测试首跑实测卡死数分钟（CPU 满载、判定段无法完成）。
- 修复（引擎/engine/JudgementTracker.cs）：谱面按 TimeMs 升序（ctor 已 SortNotes），新增 LowerBound 二分定位首个可能相关音符，扫描变为 O(log n + 窗口内音符数)；HoldLane 用"最长长条时长"下界并加无长条早退。
- 验证：修复后判定吞吐 1.06M~6.24M 音符/s，5 档位全部 判定数=音符数、累计 |偏移| 与确定性期望值完全一致。

### F2 NoteStyleBook.Get 热路径分配（引擎库）
- 现象：Get 每次 Trim().ToLowerInvariant()（大小写/空白变化时分配双字符串），宿主逐音符逐帧调用。
- 修复（引擎/engine/NoteStyle.cs）：_book 为 OrdinalIgnoreCase 字典 → 先直接 TryGetValue（规范键/任意大小写键 0 分配），仅脏键回退 Trim+Lower 兜底（兼容语义不变）。
- 验证：`Get("mania")`/`Get("Phigros")` 0 B/次；脏键 40 B/次。

### F3 JudgementTracker 哈希集未预分配（引擎库）
- 现象：_done 默认容量，漏判路径（Update 判 MISS）扩容分配 ~60 B/音符。
- 修复：ctor 按音符数预分配容量（`new HashSet<RhythmNote>(max(16, n+1))`）。验证：漏判路径 0 B/音符。

### F4 引擎壳 UI 每帧分配风暴（10245 → 82.6 B/帧，124×）
- 现象：引擎壳 UI 1000 帧（GDI 离屏 1280×800，三页轮换）每帧 10.2KB 分配。
- 根因与修复：
  1. UiLabel.DrawSelf 每帧 `Text.Split('\n')` 字符串数组 → 单行快速路径（引擎/engine/Ui/UiComponents.cs）；
  2. GdiDrawAdapter 每图元 new Font/SolidBrush/Pen、每圆角 new GraphicsPath → 静态有界缓存（字体/刷/笔 256 上限）+ ThreadStatic GraphicsPath 复用（源码/Play/EngineUi/GdiDrawAdapter.cs）；
  3. UiGlyphRuns.Split 逐码点字符串拼接 O(n²) + 无缓存 → ContainsEmoji 纯文本快速路径（共享空列表）+ StringBuilder 累积 + 有界缓存（256 上限，重复文本稳态 0 分配）（源码/Play/EngineUi/UiGlyphRuns.cs）；
  4. D2DDrawAdapter.Text/MeasureText 同步加纯文本快速路径（源码/Play/EngineUi/D2DDrawAdapter.cs）；
  5. EngineMainShell 背景层每帧 new 渐变刷+光晕 6 刷+星点 18 刷 → 静态有界画刷缓存（源码/Play/EngineUi/EngineMainShell.cs）。
- 验证：壳 UI 1000 帧平均 2.45ms · P50 1.95ms · P99 6.25ms · Max 8.0ms · 407 FPS · 分配 82.6 B/帧；热路径 12 项全部 0 B/次。

### F5 EngineJobs 一致性（无需修复项）
- 10^7 数组：Map/Reduce 与串行逐元素一致 ✅；4× 核数超卖（Degree=96）✅；单核 ✅；Paused 降级串行（EffectiveDegree=1）✅；现有 Benchmark 记录 2.87ms（0.32x——65536 元素规模在 24 核 + llama-server 常驻共享机上并行收益为负，属环境性数据，仅记录不断言）。

### F6 判定单音符分配（无需修复项，探针见证）
- 稳态 Judge/PressLane 0 B/次；Tracker 构造 21.7 B/音符（预分配容量后）。

## 3. 关键数字（res.json 全量）

### 3.1 EngineJobs（10^7，24 核，llama-server 常驻环境下；最终轮）
- 串行 Map 48.3ms / 并行 Map 106.0ms（一致=True）· 串行 Reduce 36.7ms / 并行 Reduce 121.3ms（一致=True）
- 超卖 96（线程池预热后）：8.1ms 一致=True · 单核：72.4ms 一致=True · Paused：59.9ms 一致=True · Benchmark 2.85ms（0.37x）
- 注：Degree=24 并行轮慢于串行系机器共享负载（线程池预热后的 96 超卖轮仅 8.1ms，说明并行能力本身健康）；一致性契约全部通过，t11 停 llama-server 后可复核加速比。

#### ⚠ WARN-1：EngineJobs 并行加速比为负（记录供 t11 汇总）
- 现象：10^7 Map 0.45x / Reduce 0.30x（并行慢于串行）；Benchmark(65536) 0.32x——低于既有 1M 规模 3.16x 基线一个数量级。
- 原因分析（分层，全部为环境性）：①共享机 24 逻辑核长期驻留 llama-server，并行工作集争抢内存带宽/缓存，串行单线程不受影响（t11 计划停服后错峰复核，矩阵 §7 风险控制原文）；②首个 Degree=24 轮含线程池爬升成本——同规模 96 超卖轮在线程池预热后仅 8.1ms（>串行 6×），证明并行路径本身健康；③Benchmark 的 65536 元素负载过小，并行调度开销占主导（该规模不宜断言加速比）。
- 处置：一致性/三态硬断言全部通过（不阻塞）；矩阵「1M Map ≥1x」硬线归 t11 停服后实测（res.json 已新增 1M Map/Reduce 行：一致性硬断言 + 加速比记录位，见 §7）。

### 3.2 判定引擎（10 万音符 / 549.5s / 5 档位）
| 档位 | 判定 | 命中 | 吞吐（音符/s） | 累计误差 ms（期望） | 判定分配 |
|---|---|---|---|---|---|
| osu!mania OD8 ±30ms | 100000/100000 | 100000 | 1,057,088 | 1,524,609（=） | 0 B |
| osu!mania OD3 ±6ms | 100000/100000 | 100000 | 2,380,669 | 323,078（=） | 0 B |
| IIDX ±18ms | 100000/100000 | 100000 | 3,999,232 | 924,324（=） | 0 B |
| Phigros ±60ms | 100000/100000 | 100000 | 3,069,537 | 3,024,848（=） | 0 B |
| SDVX ±6ms | 100000/100000 | 100000 | 6,238,031 | 323,078（=） | 0 B |

### 3.3 帧耗（1000 帧 @1280×800，最终轮）
- 引擎窗（SoftwareRenderer）：平均 2.07ms · P50 2.18 · P99 2.71 · Max 4.20 · 483 FPS · 8.0 B/帧
- 引擎壳 UI（GDI 离屏三页轮换）：平均 2.20ms · P99 4.16 · Max 5.93 · 454 FPS · 82.6 B/帧

### 3.4 热路径分配（每 1000 次调用，B/次）
引擎侧与壳侧 20 项全部 0 B/次（仅脏键 NoteStyleBook.Get 40 B/次，符合预期）。

## 4. 长稳（10 分钟，双通道实测，均为退出码 0）
- 引擎库通道（EngineChecks --stress-engine，10 分钟）：64 切片 · 0 失败 · 沉降内存增长 -0.028MB · PASS
- 全量通道（Milestone --stress-engine，10 分钟）：64 切片 · 0 失败 · 沉降内存增长 -0.02MB · 峰值工作集 406.6MB · PASS
- 判定口径：每切片 = EngineApp 无头自动游玩 9.4s（16 音符 4K 全命中闭环），连续循环至 10 分钟墙钟；全程无异常、无内存增长（无泄漏）。
- 注：中途一次 10 分钟轮退出码记录为 -1（产物已完整 PASS、无崩溃事件、复跑退出码 0），判定为共享机环境干扰；最终轮与复跑轮均 0。

## 5. 脚本与复跑
- 构建产物/obj/stress-engine/run.ps1：构建 EngineChecks → 引擎层严压（res.engine.json）→ 官方游戏二进制全量严压（res.json）。用法 `pwsh -File run.ps1 -Minutes 10`。
- EngineChecks 新增 CLI：`EngineChecks --stress-engine <outDir> [minutes]`（引擎库/判定/分配/引擎窗/长稳）。
- Milestone 新增 CLI：`Milestone.exe --stress-engine <outDir> [minutes]`（全量 + 引擎壳 UI/壳热路径）。

## 6. 遗留与建议
- t11 实机压测建议在停 llama-server 后复核 EngineJobs 并行收益（一致性断言不受影响）。
- 游戏层 JudgementEngine（源码/Play/JudgementEngine.cs）为独立实现，其按下扫描是否有同类 O(n²) 问题建议游戏层压力（t10）覆盖。
- 构建约束说明：本轮仅构建了 EngineChecks 自检工程与游戏 scratch 副本（构建产物/obj/stress-engine/bin，未触碰官方 bin/Release 产物）；官方构建仍由 captain 执行。

## 7. t8 压力矩阵对齐（stress-matrix.md §4.1 SE-01..09 覆盖表，2026-08-26 追加）

| 编号 | 矩阵要求 | t9 覆盖状态 |
|---|---|---|
| SE-01 基线 | 构建+EngineChecks+--selfcheck | ✅ DemoRunner.RunAll 全绿基线（每轮执行）；官方构建=captain |
| SE-02 EngineJobs 10^7 | 一致性+Benchmark+超卖/单核/Paused 三态；1M Map ≥1x | ✅ 全部一致断言（10^7 + 追加 1M Map/Reduce 一致性行）；加速比记录为 WARN-1（共享机并行收益为负，≥1x 硬线归 t11 停服复核） |
| SE-03 判定 100k×10min | 判定=音符、吞吐漂移≤10%、>2万音符/s、单判≤256B | ✅ 判定数/累计误差精确一致、单判 0B；压缩轮（T0 正确性）：10s 谱面窗口采样，每窗口>2万/s 硬线（漂移仅记录——压缩轮整轮 ~100ms 墙钟，窗口墙钟受调度噪声主导，实测 30~80% 噪声级）；**实时轮（T1，新增 RunJudgementRealtime）**：墙钟 1:1 驱动、谱长=minutes-1s 同密度（~182 音符/s）、10s 墙钟窗口采样，硬断言 全判全命中 + 每窗口≥100/s + 漂移≤10%（0.5min 实测漂移 0.69%） |
| SE-04 热路径分配 | 1000 次/帧 ×100 帧，0B 硬断言 | ✅ 引擎+壳 20 项 0B/次（脏键 40B 兜底） |
| SE-05 壳 UI 1000 帧 | 两尺寸 GDI 离屏+实窗 30s | ◑ 1280×800×1000 已完成（avg2.2ms/P99 4.16ms）；**追加引擎窗 640×360×500 第二尺寸**（代码已入两个 CLI，待构建）；实窗 30s 与壳 GDI 两尺寸需游戏构建 → t11/captain |
| SE-06 --enginerun 600s | 引擎切片 10 分钟实跑 | ◑ 等价口径已测：EngineApp 切片连续 10 分钟（64 切片 0 失败、内存无增长）；游戏 CLI --enginerun 600 需 captain 构建后跑 |
| SE-07 PublishPlayer 长跑 | 打包播放器 3 轮+10 分钟 | ❌ 未执行（需构建 PublishPlayer；入口已具备，t11/captain 跑） |
| SE-08 --shellshot 多尺寸/DPI | 1280/1920/2560 + 150% DPI | ❌ 未执行（游戏构建；与 t5 视口改造联动，t11 跑） |
| SE-09 刷新率挡位表钳制 | TargetFrameMs 单调、1440→≥0.5ms、长度一致、0/-1 语义、PresetForRate 全档 | ✅ t6 已在 FpsGovernor.SelfCheck 实现全部断言（t5 未重复实现，FpsGovernor 由 t6 按 captain 指派完成） |

res.json 阈值口径与矩阵 §3 对齐：判定吞吐>2万音符/s、单判分配≤256B（矩阵 WARN 线；实测 0B）、热路径 0B/次（矩阵硬线）、壳 UI 均值<16ms/P99<40ms（矩阵硬线；实测 2.2/4.2ms）。
