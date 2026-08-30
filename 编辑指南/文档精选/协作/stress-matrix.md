# 压力测试矩阵设计（t8）：全项目 × 多分辨率 × 多刷新率 × 内容密度 × 时长

- 日期：2026-08-26
- 作者：eng-design-max（引擎策划）
- 读者/执行：eng-coder-max（t9 引擎层）、coder-max（t10 游戏层）、试玩（t11 严压终验）、captain
- 输入证据：引擎 README（L1-L5 压力基线、性能基线 2026-08-25、自检入口）、StressCore/StressTestCli（--stresstest 1-5）、PerfTest（--perftest 阈值）、t3 设计（engine-design-t3.md 多刷新率挡位表）、t4 报告（E1-E8/G1-G10 热点清单）、t1 诊断（诊断报告.md）
- 约束：本文只出矩阵与判定阈值，不写代码；执行入口一律引用现有 CLI/脚本，新增脚本由 t9/t10 自行落盘到各自产物目录。
- 版本：v1.3（v1.2 回写 t9 实现状态；v1.3 落盘 t9 口径定稿：SE-02 1M 一致性硬断言 + 加速比永久记录位（2026-08-27 停服复核 0.26x，小粒度负收益，见 enginejobs-finding.md）；SE-03 压缩轮=T0 正确性轮（每窗口>2万/s 硬线）、漂移≤10% 硬线归实时轮 RunJudgementRealtime=T1（res.json Judge.Realtime），10 分钟全量归 t11；captain 干净全量构建绿后解冻落盘）。

---

## 1. 目的与范围

把 t11「严压终验」拆解为可执行、可判定、可复现的测试矩阵；t9（引擎层）、t10（游戏层）按本矩阵实现并执行，t11 只做实机代表格 + 长稳 + 视觉确认 + 汇总判定。

覆盖四层：
1. **引擎库**（引擎/engine/MilestoneEngine：数学/场景/音游域/UI/Storyboard/EngineJobs/HardwareProbe）
2. **引擎自检与切片**（Tests/EngineChecks、Samples/DemoRunner、引擎壳 EngineApp/EngineWindow、PublishPlayer 打包播放器）
3. **引擎壳（宿主 UI）**（源码/Play/EngineUi：EngineMainShell/EngineUiDemo + GdiDrawAdapter/D2DDrawAdapter）
4. **游戏层**（源码/：游玩 GamePanel、编辑器 ChartEditorPanel、设置/选歌/次级页、启动/扫描路径）

---

## 2. 维度定义

### 2.1 分辨率（5 档，含 DPI 虚拟化）

| 编号 | 档位 | 含义 | 验证点 |
|---|---|---|---|
| R1 | 1280×800 | 设计基准（scale 1.0） | 基线正确性 |
| R2 | 1600×900 | 中窗口 | letterbox 1.125 |
| R3 | 1920×1080 | 主流全屏 | letterbox 1.35（t1 已验） |
| R4 | 2560×1440 | 高分辨率 | letterbox 1.8；渲染分辨率=物理（t3 §2 修复后锐利） |
| R5 | 4K+物理缩放 | 2560×1600@150% DPI（逻辑 1707×1067 / 物理 3840×2400 或最大化） | DPI 虚拟化命中一致性（t1 环境同款，PhysClient/ToVirtual） |

### 2.2 刷新率（11 档；t5 实现 t3 §3.1 新表后全量可用，实现前只测现有 7 档）

| 档位 | 目标帧时 | 说明 |
|---|---|---|
| 50Hz | 20.00ms | 新增（PAL/旧屏） |
| 60Hz | 16.67ms | 现有 |
| 75Hz | 13.33ms | 新增 |
| 90Hz | 11.11ms | 新增 |
| 100Hz | 10.00ms | 新增 |
| 120Hz | 8.33ms | 现有 |
| 144Hz | 6.94ms | 现有 |
| 165Hz | 6.06ms | 现有 |
| 240Hz | 4.17ms | 现有 |
| 360Hz | 2.78ms | 现有 |
| 0 无限制 | 0（目标 1000+ FPS） | 现有；项目性能目标核心档 |
| 自定义 1..2000Hz | 1000/n，最小钳 0.5ms | 新增：超高刷入口（1440Hz → 目标 0.694ms 钳至 0.5ms，画质固定低档）；无实机（>500Hz 面板不可得）→ 以挡位表断言 + 帧时钳制仿真代替实机格（见 SE-09），t5 落地后启用 |

> 180/200/300/480/500 档在 t5 落地后按同规则抽测（t11 至少抽 300Hz 一档，t1 实测屏即 300Hz）。1440Hz 钳制口径以 t3 §3.1 为准：不设上限常量、最小帧时 0.5ms、画质低档、UI 通用名 nHz、检测到 ≥600Hz 显示器时动态插入选择列表。

### 2.3 谱面密度（6 类）

| 编号 | 类 | 素材 | 来源 |
|---|---|---|---|
| D0 | 低密度 | 100 音符合成谱 | StressCharts 缩小或 StarterChartGenerator 小谱 |
| D1 | 标准 | 2K 音符 | 合成谱（4K，BPM 120-180）或测试格式常规谱 |
| D2 | 高密度 | 10K 音符 | 合成谱（StressCharts.BuildL2 5K 加倍或 4K 高 BPM） |
| D3 | Dense Stress 4K | 其他/Chart/测试格式/mania_dense_test.osu（现行密度基准；t10 可加大重制一份 dense_plus） | 仓库已有 |
| D4 | ADOFAI 密度 | adofai_density_test.adofai / adofai_long_visual.adofai / adofai_curvy_visual.adofai | 仓库已有 |
| D5 | 长谱 10 分钟 | 合成：4K @BPM150 连续 10 分钟（约 3000 音符起，密度可调） | t10 生成器写入 构建产物/obj/stress-game/long10.mil |

### 2.4 模式（10 模式各选样，游玩层抽 6 代表 + 引擎层全模式无头）

游玩层 6 代表：Mania（下落式）、Phigros（判定线场）、Arcaea（天空/地面）、ADOFAI（路径场双球）、maimai（环形触摸）、osu!standard（自由场滑条/转盘）。IIDX/Cytus/LoopComposer/AdofaiReal 由引擎层无头（RulesetFactory.RunHeadless 全 FieldType）与 --edsim/--composertest 覆盖。

### 2.5 时长

| 编号 | 档 | 用途 |
|---|---|---|
| T0 | 瞬测 | 3-10s/单轮，矩阵逐格扫 |
| T1 | 10 分钟 | 长谱自动游玩、引擎切片稳定 |
| T2 | 60 分钟 | t11 长稳（自动游玩高密度谱 + 后台编辑器循环） |

### 2.6 环境

| 编号 | 环境 | 用途 |
|---|---|---|
| E1 | 硬件 GPU（D2D Hardware + SS1.4） | 常态压测；**要求 llama-server 停止**（错峰，见 §7） |
| E2 | WARP 软件（ForceWarp / D2D 回退） | 弱机模拟；**AI 服务共存格**（llama-server 运行中） |
| E3 | 引擎无头（Headless） | 纯逻辑压测（判定/解析/Jobs），不占 GPU |

---

## 3. 指标与判定阈值（总表）

### 3.1 帧率/帧耗时（游玩与引擎壳）

| 指标 | 阈值 | 判定 |
|---|---|---|
| FPS 均值（挡位档） | ≥ 挡位Hz × 0.9 | 低于 → FAIL |
| FPS min1%（1% 低帧） | ≥ 挡位Hz × 0.7 | 低于 → FAIL |
| FPS 均值（无限制档） | ≥ 1000（目标）；< 600 → FAIL | 600-999 → WARN（记录画质/SS 档） |
| 帧耗时 P99 | ≤ 2 × 目标帧时（无限制档 ≤ 2ms） | 超过 → FAIL |
| 长稳漂移（60 分钟） | 后 30min FPS 均值 vs 前 10min 差 ≤ 10% | 超过 → FAIL（t11） |

### 3.2 CPU/GPU/内存/GC

| 指标 | 阈值 | 判定 |
|---|---|---|
| 引擎壳 UI 离屏渲染（1000 帧） | **FAIL 硬线（t9 res.json 口径）：平均 ≥100ms 或 P99 ≥100ms 或单次 >5s（看门狗）**；WARN 回归线：平均 >16ms 或 P99 >40ms | 超硬线 → FAIL；超回归线 → WARN 记录（t9 修复后实测：平均 2.45ms / P99 6.25ms / 82.6 B/帧） |
| GC 分配热路径（NoteStyleBook.Get / JudgementProfile.Evaluate / RhythmNote.Judge / ScoreBoard.Apply / UiGlyphRuns.Split / GdiDrawAdapter.MeasureText+Text，各 1000 次/帧） | **FAIL 硬线（t9 res.json 口径）：主路径 0 B/次（脏键回退 40 B 允许）；每帧累计 ≥512B → FAIL**；WARN：>128B/帧 | t9 用 GC.GetAllocatedBytesForCurrentThread 采样；已修复项见 §8 |
| 游玩稳态 GC 速率 | ≤ 5 MB/s（目标）；> 20 MB/s → FAIL | t10 采样 30s 窗口 |
| 内存泄漏（导航 200 往返） | GC 后水位差 ≤ 30MB 且 GDI 句柄数稳定 | 超过 → FAIL |
| 60 分钟长稳内存 | 总增长 ≤ 100MB 且最后 10 分钟无单调上升（+2MB 噪声容差） | 违反 → FAIL（沿用 L5 口径放宽至 100MB） |

### 3.3 启动/响应

| 指标 | 阈值 | 判定 |
|---|---|---|
| 启动曲库扫描（40 谱，现状 G5） | ≤ 2s（修复目标；现状 0.4-1.6s 已达标→WARN 记录） | > 5s → FAIL |
| 启动曲库扫描（200 谱，t10 构造） | ≤ 8s | 超过 → FAIL |
| 进歌背景解码+上传 | P99 ≤ 1s | 超过 → FAIL（G6） |
| 编辑器大谱（>5000 音符）加载 | ≤ 3s | 超过 → FAIL |
| 编辑器操作风暴（增删音符 5000 次） | 总时长 ≤ 30s 且单次 P99 ≤ 50ms | 超过 → FAIL（G7 密度柱 O(窗×音符) 修复验证点） |
| 主菜单→选歌→设置→编辑器→游玩往返 | 200 次无崩溃、无句柄泄漏（§3.2） | 崩溃/泄漏 → FAIL |

### 3.4 引擎库基线（t9，沿用现有口径）

| 项 | 基线（2026-08-25） | 阈值 |
|---|---|---|
| 谱面解析 | 串行 8.61 MB/s · 并行 25.44 MB/s（2.96x） | 串行 ≥ 1MB/s；并行 ≥ 串行（宽松回归） |
| EngineJobs Map 1M | 3.16x（2026-08-25 重任务基线） | 一致性=硬断言；**加速比不设硬线（记录位）**——2026-08-27 停服错峰复核 0.26x（小粒度负收益，与 AI 负载无关，见 enginejobs-finding.md） |
| JudgementTracker 100k（判定吞吐） | 11.51ms（修复前） | ≤ 30ms 且 **吞吐硬线 >2万音符/s（SE-03）**；t9 修复 O(n²) 后实测 0.86M~6.0M 音符/s、0 B/判 |
| ChartValidator 20k | 6.66ms | ≤ 30ms |
| BeatAlignEngine 60s@44.1k | 585.21ms | ≤ 2s |
| UI 离屏 60 帧（D2D/GDI） | 2.13 / 1.06ms | 平均 < 16ms |
| --stresstest L1-L5 | PASS（判定=音符、满分、fuzz 0 失败、leak 否） | 全 PASS |

### 3.5 全局硬门槛

- 引擎构建（dotnet build 引擎/engine/MilestoneEngine.csproj -c Release）：0 警告 0 错误。
- EngineChecks（dotnet run --project 引擎/engine/Tests/EngineChecks/EngineChecks.csproj -c Release）：exit 0。
- Milestone.exe --selfcheck：exit 0（selfcheck.log 全绿）。
- 任何一档压测出现崩溃/未处理异常/设备丢失未恢复 → 该格 FAIL。

---

## 4. 测试矩阵主表

约定：命令中的路径按仓库根运行；产物目录见 §6。

### 4.1 引擎层（t9 执行，产物 构建产物/obj/stress-engine/）

| 编号 | 测试 | 入口/命令 | 维度 | 判定阈值 | 备注 |
|---|---|---|---|---|---|
| SE-01 | 基线全绿 | 引擎构建 + EngineChecks + Milestone.exe --selfcheck | E3/T0 | 3.5 硬门槛 | 先跑，绿才继续 |
| SE-02 | EngineJobs 大数组 | 引擎侧新增 StressJobs：ParallelMap/Reduce 10^7 int（与串行结果逐元素一致断言）+ Benchmark | E3/T0 | **v1.3 口径定稿（含停服复核结论）**：一致性=硬断言（1M/10^7 均含：Map1MSame/Reduce1MSame 入 Pass）；加速比**不设硬线（永久记录位 Map1MSpeedup）**——2026-08-27 停服错峰复核 0.26x，与 AI 负载无关，根因=极小每项工作下分区/调度开销>收益（本机 24 逻辑核），细节见 其他/docs/协作/enginejobs-finding.md | 覆盖 4×核数超卖、Degree=1、EngineJobs.Paused=true 三态。**t9 实测注记（v1.2）**：10^7 Map 在 llama-server 常驻共享机 串行 48ms vs 并行 106ms（0.32x）、超卖预热后 8ms（>串行 6×）——并行路径健康；一致性硬断言已过。**停服复核（2026-08-27）**：llama-server 已停、Benchmark 1M 极小工作 Speedup=0.26x——此前共享机归因证不成立，加速比永久记录位；处置=下一轮引擎优化项（自动串行阈值/chunk 增大/重任务退化线 ≥0.8x），本轮不阻塞 |
| SE-03 | 判定长跑 | **v1.3 口径定稿**：压缩轮（T0）= JudgementTracker 100k 音符 × 5 判定档（正确性轮，RunProfilePass 10s 谱面窗口采样）；实时轮（T1）= StressEngine.RunJudgementRealtime(minutes)：墙钟 1:1 驱动、谱长=minutes-1s 同密度（~182 音符/s）、10s 墙钟窗口采样 | E3/T0+T1 | 压缩轮（T0）：判定=音符、全判全命中、每窗口>2万音符/s 硬线（漂移只记录不设硬线——墙钟窗口 ~1.7ms 受 GC/调度噪声主导，不可测）；实时轮（T1）：全判全命中 + 每窗口≥100/s + 漂移≤10% 硬线；§3.4 阈值 | t9 已修复 O(n²)（二分下界+HashSet 预分配）：实测 0.86M~6.0M 音符/s、单判 0 B；单判 ≤256B（WARN）/≥1KB（FAIL）口径保留。res.json：SampleWindows/MinWindowTput/MaxWindowTput/ThroughputDriftPct（压缩轮）+ Judge.Realtime（实时轮）；实测 0.5min：5258/5258 全命中、窗口 181.3~182.6/s、漂移 0.69% PASS；**10 分钟全量归 t11（-Minutes 10）** |
| SE-04 | 热路径分配 | 引擎侧新增 AllocProbe：NoteStyleBook.Get / UiGlyphRuns.Split / IUiDraw.MeasureText+Text（GDI 适配器）各 1000 次/帧 × 100 帧 | E3/T0 | §3.2 GC 分配阈值（0 B/次硬断言） | t9 已全绿（stress-engine.log：全项 0 B/次，脏键 40 B 回退）；E4/E3/E1 修复验证通过，保留回归项 |
| SE-05 | 引擎壳 UI 1000 帧 | 引擎侧新增 UiStress：EngineUiDemo 离屏（GdiDrawAdapter，640×360 与 1280×800 两尺寸各 500 帧）+ EngineWindow 实窗 30s | E1+E2/T0 | §3.2 UI 阈值（硬线对齐 res.json） | t9 已实测：壳 UI 平均 2.45ms/P99 6.25ms/82.6 B/帧、引擎窗 2.22ms/3.24ms/8 B/帧。**v1.2 已实现**：两 CLI 均增引擎窗 640×360×500 帧第二遍（res.json 新字段 EngineWindowSmall）；实窗 30s 与壳 GDI 两尺寸离屏需实机 → t11 交接 |
| SE-06 | 引擎切片 10 分钟 | Milestone.exe --enginerun 600 | E1+E3/T1 | 判定=音符、FPS 漂移 ≤10%、内存无单调增长 | EngineGame 4K 自动游玩切片。**v1.2：待实机（游戏构建后）→ t11 交接** |
| SE-07 | 打包播放器 | 引擎/engine/PublishPlayer（打包样本谱 30s ×3 轮 + 长谱 10 分钟一轮） | E1/T0+T1 | 无崩溃；判定=音符；ESC/结束自动关闭 | PackPlayerHost 已内置判定闭环。**v1.2：待实机 → t11 交接** |
| SE-08 | 引擎壳多尺寸/DPI | --shellshot 1280×800 / 1920×1080 / 2560×1440 + 150% DPI 会话 | E1/T0 | 渲染哈希/像素一致性（t3 §2.4 口径） | 与 t5 视口改造联动。**v1.2：待实机 → t11 交接** |
| SE-09 | 刷新率挡位表钳制（1440Hz 等超高刷） | FpsGovernor 自检断言复核：TargetFrameMs 单调且 rate>1000 时 ≥0.5ms（1440→0.5ms 钳制）、Rates/RateNames 长度一致、0 与 -1 语义、PresetForRate 全档映射；帧时钳制仿真（自定义档 1000/1440/2000 三值 × 无限制档对比帧循环钳制） | E3/T0 | 全断言通过；钳制不产生 0/负帧时 | **v1.2 已覆盖**：FpsGovernor.SelfCheck 全表断言已实现（t6 按 captain 指派实现全表：单调/1440Hz≥0.5ms/2000Hz=0.5ms/长度一致/0 与 -1 语义/PresetForRate 全档映射）；1440Hz 无实机，以此格代替 |

### 4.2 游戏层（t10 执行，产物 构建产物/obj/stress-game/）

| 编号 | 测试 | 入口/命令 | 维度 | 判定阈值 | 备注 |
|---|---|---|---|---|---|
| SG-01 | Dense Stress 游玩 | Milestone.exe --fpsprobe 其他/Chart/测试格式/mania_dense_test.osu 60 构建产物/obj/stress-game/dense | R3/240Hz 与 0 无限制/T0 | §3.1 | 自动游玩路径；重制 dense_plus（>4000 音符）再跑一轮 |
| SG-02 | 长谱 10 分钟 | 生成 long10.mil → --fpsprobe 600s | R3/144Hz/T1 | §3.1+§3.2 GC 速率 | 稳态 GC 采样 30s×20 |
| SG-03 | 6 模式代表游玩 | --fpsprobe 各模式样本谱 30s（Mania/Phigros/Arcaea/ADOFAI/maimai/osu!std） | R3/120Hz/T0 | §3.1 | 测试格式目录选样 |
| SG-04 | 编辑器大谱 | --edsim 5000+ 音符谱（加载/事件求值/密度条/播放头） | E1/T0 | §3.3 | G7/G8 修复验证 |
| SG-05 | 编辑操作风暴 | 编辑器脚本：增删音符 5000 次（日志计时） | E1/T0 | §3.3 | 需要 t10 写 UI 自动化或 CLI 驱动 |
| SG-06 | 导航 200 往返 | 主菜单→选歌→设置→编辑器→游玩→返回 ×200（自动化点击 + 内存/GDI 句柄采样） | E1/T0 | §3.2 无泄漏 | 进出页 GC 对比口径（t8 需求项） |
| SG-07 | 启动扫描 | 曲库目录 200 谱（t10 复制测试格式×5 生成）冷启动计时 | E1/T0 | §3.3 | G5 修复验证（ParseMany/后台化） |
| SG-08 | 进歌解码 | 大背景图谱面进歌（FindBackgroundImage/LoadBackgroundArt 计时 P99） | E1/T0 | §3.3 | G6 修复验证 |
| SG-09 | 分辨率×刷新率组合 | 4 格抽查：R1@60 / R3@144 / R4@240 / R5@0（窗口尺寸由 t10 脚本 SetWindowPos 控制） | T0 | §3.1 | t5 落地后可全档；落地前只测现有档 |

### 4.3 实机代表格（t11 终验，12 格，产物 构建产物/obj/stress-final/）

| # | 格 | 分辨率×刷新率×密度×模式 | 时长 | 环境 |
|---|---|---|---|---|
| 1 | 基线格 | R1(1280×800) × 60Hz × D1 标准2K × Mania | T0 | E1 |
| 2 | 主流格 | R3(1920×1080) × 144Hz × D2 高10K × Mania | T0 | E1 |
| 3 | 高刷格 | R4(2560×1440) × 240Hz × D3 Dense4K × Mania | T0 | E1 |
| 4 | 性能目标格 | R4 × 0 无限制 × D3 Dense4K × Mania（验证 1000+ FPS 目标） | T0 | E1 |
| 5 | 新增 360 档 | R3 × 360Hz × D1 × Mania | T0 | E1 |
| 6 | 新增 75 档 | R1 × 75Hz × D0 低100 × Phigros | T0 | E1 |
| 7 | 判定线场格 | R3 × 120Hz × D2 高10K × Phigros | T0 | E1 |
| 8 | ADOFAI 格 | R2(1600×900) × 60Hz × D4 密度 × ADOFAI | T0 | E1 |
| 9 | 新增 90 档 | R3 × 90Hz × D3 Dense4K × Mania | T0 | E1 |
| 10 | 长稳前置格 | R4 × 165Hz × D5 长谱10分钟 × Mania | T1 | E1 |
| 11 | DPI 格 | R5(2560×1600@150% 最大化) × 0 无限制 × D1 × Mania | T0 | E1 |
| 12 | 共存格 | R1 × 60Hz × D2 高10K × Mania（llama-server 运行中，WARP） | T0 | E2 |

每格：截图（游玩中/结算）+ 数据（FPS 均值/min1%/P99、帧耗时 P99、CPU/GPU、GC、内存）+ res.json（见 §6）。任一格 FAIL → 该格 P0 问题登记；P0=0 即严压通过。

环境限制登记：1440Hz（及 >500Hz）刷新率面板当前不可得（Windows 显示枚举实际上限 ~500Hz）——该档以 SE-09 挡位表钳制断言 + 帧时钳制仿真代替实机格，t11 不设 1440Hz 实机格；若未来拿到超高刷面板，按格 3/4 口径补跑。

### 4.4 60 分钟长稳（t11）

- 方案：D5 长谱自动游玩循环（R4×240Hz，E1）+ 每 10 分钟穿插 3 分钟编辑器后台循环（打开大谱→滚动→播放头→关闭）。
- 监视：FPS 漂移（§3.1）、内存（§3.2）、GDI/句柄数、GC 累计、温度（若可读）。
- 判定：漂移 >10% 或内存违反阈值 → FAIL。

---

## 5. 结果判定与回归规则

1. **硬断言**（不一致即 FAIL）：EngineChecks/selfcheck exit、判定=音符、串并行结果一致、崩溃/泄漏阈值。
2. **性能阈值**（§3 各表）：低于阈值 → FAIL；落入 WARN 带（表内标注）→ 记录不阻塞，但 t11 汇总须列出。
3. **回归对比**：每项记录与 §3.4 基线（2026-08-25）对比；相对基线劣化 >30% → FAIL（防性能回退）。
4. **修复闭环**：FAIL 项由 t9/t10 修复后重跑该格；修复涉及引擎 → EngineChecks 追加断言；t11 汇总时所有格重跑一次。

---

## 6. 产物与报告约定

- 目录：构建产物/obj/stress-engine/（t9）、构建产物/obj/stress-game/（t10）、构建产物/obj/stress-final/（t11）。
- 每格产物：res.json（machine: cpu/gpu/dpi/screen；cell；startedUtc；durationSec；metrics：fpsAvg, fpsMin1p, fpsP99, frameMsP99, cpuPct, gpuPct, gcMBps, memMb[], gen0PerSec；thresholds；pass；notes）+ 截图（PNG）+ 日志。
- 报告文档：其他/docs/协作/stress-engine-report.md（t9）、stress-game-report.md（t10）、stress-final-report.md（t11，含 12 格表 + 长稳 + 判定）。
- 阈值表以本矩阵 §3 为准；t9/t10 若需调整阈值须在报告中注明理由并抄送 captain。

---

## 7. 风险控制（需求项：llama-server 错峰 / WARP 共存）

1. **GPU 压测错峰**：所有 E1 格执行前确认 llama-server 已停（captain 协调）；每格 GPU 满载 ≤15 分钟，格间间隔 ≥2 分钟（防 TDR/过热）；D2D 设备丢失需自动重建（D2DRenderer 已有 _recreatePending 机制，压测验证其恢复次数=0 目标）。
2. **AI 服务共存格**：E2 用 WARP/软件渲染 + llama-server 运行中（模拟弱机+显存被占）；按 t12 边界——应用不调用本地 AI 服务，llama-server 仅作为外部 GPU 竞争负载；GpuGuard 进程检测可触发（记录触发与恢复）。
3. **无头优先**：逻辑类压测（SE-02/03/04、解析、判定）一律 E3 无头，不占 GPU、不受显示环境影响。
4. **Ollama 端口**：压测期间不发起任何 11434 请求（t12）；共存格如检测到应用侧网络活动 → 该格直接 FAIL 并抄送 coder-max（t12）。
5. **自动化边界**：UI 自动化（SG-06/09、t11 截图）用 PrintWindow/SetCursorPos（t1 已验证口径），不做注入式钩子。

---

## 8. 与既有热点清单的对照（预期发现 → 验证点）

| 热点（t4/t3） | 本矩阵验证格 | 状态（2026-08-26） |
|---|---|---|
| E1/E2/E3 UI 测量/run-split 缓存缺失 | SE-04、SE-05 | 部分修复（t9）：UiGlyphRuns.Split 缓存+GdiDrawAdapter Font/Brush/Pen 缓存+UiLabel 单行快速路径已落地；引擎级 UiElement 测量缓存+脏渲染仍待 t6 |
| E4 NoteStyleBook.Get 分配 | SE-04 | ✅ 已修复（t9：OrdinalIgnoreCase 快速路径 0 B/次） |
| G1/G2 壳 16ms 无条件重绘 + 全屏双三次放大 | SE-05、SG-09、格 11 |
| G5 启动同步扫谱 | SG-07 |
| G6 进歌主线程解码 | SG-08 |
| G7 编辑器密度柱 O(窗×音符) | SG-04、SG-05 |
| G8 编辑器 SS 乒乓链 | SG-04 |
| GPU 自适应只降不升（t4 #7） | 格 3/4（观察 EffSs/AdaptStages 回升） |
| FpsGovernor 新挡位（t3 §3） | 格 5/6/9（75/90/360） |
| 分辨率过低/letterbox（t3 §2） | 格 11、SE-08、SG-09 |

---

## 9. 执行顺序与依赖

1. **立即**：t9 先跑 SE-01（基线）→ SE-02/03/04/05/06/07/08 按序；本矩阵 §4.1 即 t9 的完整清单（t9 已开工，无需等本文其余部分）。
2. t10 依赖本矩阵（t8 完成即解锁）：SG-01..09。
3. t11 依赖 t9+t10 完成与构建：12 格 + 60 分钟长稳 + 视觉确认（高密度谱 10 分钟人类模拟：无卡顿/字幕错乱/文字重叠）。
4. t5/t6 的修复（视口/刷新率档位/UI 裁剪）落地后，SG-09、格 5/6/9/11 重跑验证。
5. 全部 P0=0 → stress-final-report.md 判定「严压通过」。