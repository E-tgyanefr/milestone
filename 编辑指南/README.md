# Milestone（里程碑）v6.0

全音游玩法引擎 · 3D 渲染 · 九种玩法 · 多格式谱面 · 单一谱面多模式 · 段位挑战与 AI 训练 · Unity 式引擎运行时（场景/Ruleset/UI/转场）
（maimai / WACCA / 偶像音游 SUS / SDVX / Deemo / Muse Dash / Rotaeno / Dynamix / Lanota / Tone Sphere / Pump It Up 已按需求移除，注册表接口 ModeSystem 保留，开发者可重新接入）

## 游戏模式

| 模式 | 说明 | 谱面格式 |
|------|------|----------|
| Mania | 经典垂直下落（osu!mania / Malody / SM / Quaver），支持斜轨/直轨切换 | .osu .mc .sm .ssc .qua |
| Phigros | 动态判定线：判定线平移/旋转/显隐，事件驱动（多判定线同色无上限） | .json / .mil |
| Arcaea | 天线+地线双判定线、4K 透视轨道、Arc 连续弧线（可接触/装饰两态）、skytap 自由放置、按住 arc 轨道倾斜、skytap/tap 连线 | .aff / .mil |
| Cytus | 扫描线玩法：扫描线=时钟，音符圆点 + 往返扫描线，Tap/Drag/Hold | .txt / .mil |
| osu!standard | 圆点/滑条（直线/贝塞尔/Catmull/完美圆）/转盘，鼠标+键盘击打 | .osu |
| ADOFAI | 冰与火之舞：旋转路径单键，Twirl/Hold/变速 | .adofai |
| IIDX | beatmania IIDX：转盘 + 7 白键 8 轨下落 | .mil |
| 回环作曲（创新玩法） | 谱面=玩家演奏：录→奏→扩循环作曲（16 分吸附/D F J K 4 轨） | .mil |
| Milestone 原生 | 统一 JSON 格式，编辑器保存用（支持单一谱面多模式 parts） | .mil |

## 操作

- 游戏内：`空格/P` 暂停 · `R` 重开 · `A` 自动游玩 · `S` 跳过开头空白 · `M` 切换模式部件（多模式谱面） · `ESC` 退出
- 新快捷键：`T` 切换斜轨/直轨 · `V` 切换 3D 渲染
- 结算画面：`R/回车` 重新开始 · `退格` 返回选歌 · `ESC` 主菜单
- 全局：`F1` 显示/隐藏菜单栏

## 单一谱面多模式

- 一个 `.mil` 谱面可含多个「部件」，每个部件是一种玩法的独立谱面层（音符/事件/键数各自独立，BPM/音频共享）
- 打开多模式谱面时弹出「选择玩法模式」；游玩中按 `M` 切换到下一部件（整局重开并应用新部件）
- 编辑器「部件」下拉框切换当前编辑部件，`➕ 部件` 添加新模式层，`🗑 删部件` 删除当前层
- 已移除玩法的部件在载入/游玩/编辑时自动跳过

## 3D 渲染与斜轨

- 「设置 → 显示」可调：3D 开关、相机俯仰（0~60°）、偏航（±30°）、透视深度
- 3D 关闭时为经典 2D；斜轨（Malody 风格透视消失点）可用 `T` 或设置页开关
- 所有模式均支持 3D 视角

## 打击音效 / 打击效果

- 三种风格：经典（正弦）/ 电子（方波）/ 木鱼（敲击），按判定等级区分音高
- 命中扩散环 + 粒子飞散 + 轨道闪光 + 连击脉冲，MISS 屏幕震动
- 音效可叠加播放（winmm），不互相打断

## 段位挑战与 AI 训练

- 主菜单「段位挑战」：自动扫描曲库中的段位谱面（Dan 目录），段位模式启用 HP 生命条，掉光即失败
- AI 演示/陪玩：按预设等级（osu!mania 4K Dan / Malody Dan / REFORM 等 70+ 等级）
- 「AI 训练」：选定等级 + 段位谱面后离线模拟校准，把 AI 调到**刚好压线过段**
  （预测 ACC 落在过段线 ±1% 内、结束 HP 剩 3~14），训练结果持久化到 Player\ai_train.json

## 谱面编辑器

- 打开谱面后切换「模式」即可编辑对应音游项目（全部可用模式均可编辑；已移除玩法不再列出）
- 无轨（Phigros / Cytus / osu!standard）按 phira-render（RPE）范式：白色判定线随 moveX/moveY/rotate 事件移动旋转（多判定线 1~64 条）、音符沿线自由放置、拖线写 moveY 关键帧、Shift+拖=rotate、右侧菜单可写流速(speed)/亮度/透明度(alpha) 事件、事件键帧列表 + 9 种缓动曲线、Arcaea 3D arc 控制点编辑
- 有轨（Mania / Taiko / Catch / ADOFAI / IIDX）按 Malody V / osu-master 范式：列式轨道 + 时间行网格（小节/细分线只画在轨道上）+ 吸附（1/1~1/64）+ 底部时间条 + 播放头 + hold 拖尾 + 右键删除 + 滚轮滚动/空格播放
- 无轨（非下落式）模式一律保持无轨场编辑，绝不转成轨道
- 统一时间轴画布：单击放音符、拖动改时间、右键删除、底部拖拽拉长条、网格吸附、Ctrl+Z 撤销
- 保存为 .mil 原生格式（多模式谱面保存 parts 数组）；Mania 模式可导出 .mc / .osu
- 选歌界面「编辑此谱面」按钮可直接打开当前难度进入编辑器

## 制谱 AI（对音 / 查错 / 自动偏移）

编辑器工具栏「🎯 自动校准偏移」与「🤖 AI 检查」，为制谱流程提供自动化质量闭环（引擎侧纯计算零依赖：
`Dsp` FFT/谱通量起音/BPM 估计 + `BeatAlignEngine` 对音/打拍偏移 + `ChartValidator` 谱面查错，见 引擎 README）：

- **🎯 自动校准偏移（对音）**：工具按钮——WAV(16-bit PCM) 音频解析起音 → 与当前谱面音符互相关对齐，
  给出最优偏移（OffsetMs）、置信度、离拍音符列表（可逐条查看/应用）；非 WAV 或无音符时自动切**打拍模式**：
  边播边按任意打击键 ≥8 次，按相位集中度给鲁棒偏移。校准结果弹窗可一键应用（写入谱面偏移）。
- **🤖 AI 检查（查错）**：工具按钮——对当前编辑态全部部件即时跑 `ChartValidator` 规则校验
  （通用 + 模式规则：Mania / Phigros / Arcaea / Adofai / Cytus / osu!standard），
  问题按 Error / Warning / Hint 三级着色（红 / 橙 / 默认）列出（代码 + 时间 + 说明），双击跳转到该问题时间点；
  同时把问题列表 + 谱面元数据交给本地 AI 摘要（Ollama qwen3.8-27b，后台线程不卡 UI）——
  返回 3~5 条可执行制谱建议（对音/结构/难度，每条一行「建议：」开头）；AI 离线/超时自动回退规则文本（标注「AI 离线」）。
- 加速器标签：本地 AI 摘要按 `AiAccelerator.Preferred()` 显示（Npu > Gpu > Cpu，见 t1 全硬件探测）。

## 游玩界面还原

- 默认经典 2D 直轨视角（与原音游一致）；`T` 可切 Malody 风格斜轨、`V` 可切 3D
- **Arcaea 2D 平面透视场地**（4K 透视轨道向天线收窄、白弧线按左右手蓝/红、arc 可接触/装饰两态、skytap 自由放置、按住 arc 轨道倾斜、skytap 与 tap 同竖直面连线）；Arcaea 左侧回忆收集率竖条
- Phigros 白色判定线（多线同色、无上限）+ 白色 TAP/红色 FLICK + 无轨位置判定（点击音符所在位置）
- Cytus 扫描线=时钟（扫描线到达音符才可判定，提前击打无效）
- osu 系（standard/taiko/catch）还原实机操作；taiko 默认键 KDDK（Z/V=蓝 kat、X/C=红 don）；catch 无轨单判定点（下方接盘）
- 右侧窗口仅 Mania / IIDX 保留；其余玩法实际游玩面积铺满程序窗口
- 引擎化：Application.Idle 无上限游戏循环（实测 ~300 FPS / 3.3ms）· FPS HUD 显示 GPU（Direct2D1 · GPU: 显卡名）· 判定/模拟自检日志

## 引擎自检（CI / 诊断）

- `Milestone.exe --selfcheck`：运行引擎全部断言（判定预设/数学不变量/模块行为/谱面模型/判定追踪器闭环），
  结果写入 `selfcheck.log`（UTF-8），退出码 0=通过 / 1=失败——可用于 CI 与发布前体检。
- 独立自检工程：`engine\Tests\EngineChecks`（`dotnet run --project engine\Tests\EngineChecks`）。
- 引擎源码在 `engine\`（随宿主一起编译，渲染无关）；发布包见 `engine_引擎打包.zip`。

## 测试矩阵 / 性能测试 / 压力测试

**测试矩阵**（全部无头 CLI，退出码 0=通过 / 1=失败；引擎侧断言组见引擎 README）：

| 入口 | 覆盖 | 产物 |
|---|---|---|
| `--selfcheck` | 引擎全部断言组（判定预设/数学不变量/模块行为/谱面模型/追踪器/滑条/运行时/Ruleset/[多场同屏]/[故事版]/[音符样式]/[起步谱]/[制谱 AI]/[并行/硬件] 等） | selfcheck.log |
| `--modetest` | 模式可用性/门控 | modetest.log |
| `--composertest` | 回环作曲状态机 | composertest.log |
| `--aidetect` | 硬件/线程/NPU/加速器探测 | aidetect.log |
| `--perftest "<outDir>"` | 性能基线（本节） | perftest.json / .md / .log |
| `--stresstest "<outDir>" [1-5]` | 压力基线（本节，默认 1） | stresstest.json / .md / .log |
| `--edshot / --edsim / --shotdemo / --menushot / --uisceneshot` | 编辑器/游玩/UI 可视化取证 | obj\edshot\ 等 |

**`--perftest`（性能测试）**：`Milestone.exe --perftest "构建产物\obj\perftest"`——测量 5 项（谱面解析吞吐、EngineJobs Map/Reduce、判定/校验/对音吞吐、UI 离屏渲染 60 帧、内存），
产物 perftest.json/.md。本轮实测基线（2026-08-25，测试格式 35 文件 39KB，并行度 24）：

| 项 | 实测 | 备注 |
|---|---|---|
| 谱面解析 | 串行 8.61 MB/s（0.47s）· 并行 25.44 MB/s（0.16s）→ **2.96x** | 并行内存增量 3.5MB < 串行 12.39MB |
| EngineJobs Map/Reduce | 1M Map **3.16x** · Benchmark 1.06x | 1k/64k 小负载 0.06x/0.10x = 已知调度开销（阈值未约束） |
| 判定 | JudgementTracker 100k 音符 **11.51ms** | 已判定 100000 · 余 0 |
| 校验 | ChartValidator 20k 音符 **6.66ms** | 30 issues |
| 对音 | BeatAlignEngine 60s@44100 **585.21ms** | BPM 估计 120 |
| UI 60 帧 | D2D 平均 **2.13ms**（尾帧 2.22 / P99 4.34）· GDI 保底 1.06ms | 无窗口离屏 |
| 内存 | 峰值工作集 **120.36MB** | — |

阈值（宽松回归，全 PASS）：解析 ≥1MB/s · UI 平均帧时 <16ms · 无 OOM。

**`--stresstest`（压力测试）**：`Milestone.exe --stresstest "构建产物\obj\stresstest" [级别1-5]`（默认 1；1..L 逐级执行）——级别定义与断言：

| 级别 | 内容 |
|---|---|
| L1 | 合成谱 1k 音符 + BPM100（1 轮自动游玩） |
| L2 | 5k + BPM30/400 突变 + 超长 hold（1 轮） |
| L3 | 20k + 64 判定线 + 同窗密集 jack + 变速链（判定线场 20k 轮 + 轨道场 jack 轮） |
| L4 | L3 × 自动游玩 3 轮 + 引擎 UI 转场 fuzz 500 次（五风格轮换）+ 离屏渲染看门狗（单次 >5s 判 fail） |
| L5 | L4 × 4 轮 + 随机输入 fuzz（200 次/秒按键/触点）+ 内存增长监测（每轮 GC 采样，连续增长 >50MB 记 leak） |

断言：无未处理异常/崩溃；每轮判定总数=音符数；转场 fuzz 每次 Completed 且 5s 虚拟超时判 fail；内存不持续增长；渲染看门狗。
本轮实测基线（L5，2026-08-25）：判定线场 20k × 20 轮 + 轨道场 5k × 20 轮全部「判定=音符 ✅ · ACC=1 ✅」（单轮 1.7~1.9s）；转场 fuzz 500 次失败 0（最大虚拟耗时 2046.05ms）；离屏渲染 60 次平均 0.74ms（最大 15.25ms）失败 0；随机输入 fuzz 59,940 事件「判定=音符 ✅」；内存采样 2.93→9.39MB（+6.46MB，连续上升但 <50MB → 非泄漏）→ **L1~L5 全 PASS**。

**升级压力循环（t20）**：基线 + ×2 + ×4 + ×8 升级轮（音符×8 = 160k、转场 fuzz 4000、输入 479,660 事件）全部 PASS（160k 音符 148.07s，内存 +0.00MB）；期间**真实修复 8 个问题**（P1 RunAll 断言组丢失 / P2 分号误入注释 / …，见 `构建产物\obj\agent-stress-findings.md`）。

**回归方式**（发布前建议一条链）：① `dotnet build 引擎\engine\MilestoneEngine.csproj -c Release`（0 警告 0 错误）→ ② `dotnet build Milestone.csproj -c Release` → ③ `Milestone.exe --selfcheck`（exit 0）→ ④ `Milestone.exe --perftest "构建产物\obj\perftest"`（PASS）→ ⑤ `Milestone.exe --stresstest "构建产物\obj\stresstest" 5`（L1~L5 全 PASS）；引擎侧独立全断言：`dotnet run --project 引擎\engine\Tests\EngineChecks\EngineChecks.csproj -c Release`（exit 0）。

## 引擎架构（Unity 式运行时）

引擎在既有数学/判定/场/输入模块之上新增一层"Unity 式运行时"，让任意玩法场景都能用对象/组件/场景组合搭建、引擎驱动 UI 与转场（渲染无关，宿主负责绘制）：

- **场景运行时**：`GameObject` / `Component` / `Scene` / `SceneManager` / `GameLoop`——组件生命周期 `Awake→OnEnable→Start→Update→LateUpdate→OnDisable→OnDestroy`，父先于子确定性遍历，失活/销毁精确触发（过渡驱动 `RefreshEnable` 防重复）；`GameLoop` 无头驱动，宿主每帧 `Tick` 即可跑真实游戏或自检。
- **Ruleset 组合（可搭建所有游玩法）**：`abstract Ruleset` + `ChartContext` + `Lane/Line/Ring/PathRuleset`——用既有 `LaneField`/`LineField`/`RingField`/`PathField`/`TimeDepthMapper`/`InputMapper`/`JudgementProfile`/`JudgementTracker`/`ScoreBoard` **无头组合**出固定下落 / 自由线场 / 环形触摸 / 路径轨道四大玩法族，均带可自检的判定闭环断言。最小代价新玩法：`RulesetFactory`（6 字段描述符 → Build/BuildChart/BuildContext/RunHeadless 一键闭环）+ `Samples\BlankRuleset.cs` 脚手架（"10 行声明 + 渲染钩子 = 一个新玩法"，见引擎 README）。
- **引擎驱动 UI（重做 UI）**：`IUiDraw`（Rect/RoundedRect/Text/Ellipse/Line/Image/PushClip/PopClip/MeasureText，颜色 `RgbaColor`）+ `UiPanel`/`UiButton`/`UiLabel`/`UiCard`/`UiStackLayout`（`Component` 子类）+ `UiTheme` 新主题，宿主用 `D2DDrawAdapter` 映射到 `D2DRenderer`。
- **场景转场动画**：`SceneTransition` 五风格 `Fade / Slide / Wipe / CircleReveal / Beam`——引擎只算时序/缓动与 `Progress/IsComplete/Completed/OutAlpha/InAlpha/OffsetX` 曝光参数，宿主 `SceneCompositor` 读参数对 from/to 快照合成绘制。

设计契约：`docs\协作\engine-unity-arch.md`（含精确签名与自检断言）；引擎自检覆盖上述运行时（`EngineChecks`）。

## 完全引擎化（引擎应用壳 --enginerun）

- **现状**：引擎库已是「完全引擎化」可玩栈——`App\EngineApp`（无头/有窗统一帧调度：Tick/Render 成对、OnClose 一次、FPS 统计、MaxSeconds 自动退出）+ `App\EngineWindow`（Windows 原生窗口后端）+ `Ui\*`（引擎 UI 组件与主题）+ `Platform\*`（平台抽象/软渲染兜底）+ `Samples\EngineGame`（4K 自动游玩垂直切片：软件渲染+Ruleset 判定+键鼠输入+引擎时钟，无 WinForms）。
- **运行**：`Milestone.exe --enginerun`（有窗，默认自动退出）/ `--enginerun 5 --headless`（无头取证：软件渲染帧产出 + 判定数=音符数，退出码 0=正常）——窗口路径中的判定/按压/视觉观感为人工验证项。
- **验证**：--selfcheck 断言组 [应用壳]（窗口后端创建/无头帧调度/OnClose 一次/FPS 统计）、[引擎 UI]（UiTheme 令牌/按钮事件/布局层级/桩渲染确定性）、[引擎切片]（无头自动游玩 16/16 全命中+帧产出）；EngineChecks 同源全绿；引擎 5 RID 构建矩阵 0 警告 0 错误。
- **引擎化矩阵**：引擎核心（域逻辑/场景树/Ruleset/UI/App 壳/软渲染）= 全部 OS 构建通过；宿主（WinForms+D2D 全套）= Windows 现行；Linux/macOS 宿主接入见 `docs\协作\跨平台方案.md` 阶段 1-4。
- **混合现状（诚实说明）**：正式游戏仍走 WinForms 宿主（GamePanel/D2D 高保真渲染 + 音频 + 编辑器）；`--enginerun` 是「引擎栈一把梭」的垂直切片证明，两者共享同一引擎核心（判定/场/谱面/UI 组件树）。

## 谱面放置

- 曲库目录（默认程序目录下 `Chart\`）内任意子目录均可，自动递归扫描
- 压缩包（.osz/.mcz/.zip）可自动解压导入
- 示例谱面见 `Chart\Milestone示例\`（含多模式示例）与 `Chart\段位挑战\`（段位）


---

## 路径映射表（t62 四目录迁移 2026-08-28）

| 旧路径 | 新路径 |
|---|---|
| 源码\ | 游戏源码\源码\ |
| Milestone.csproj / app.manifest / Program.cs / 根 obj\ | 游戏源码\ |
| 引擎\engine\ | 引擎源码\engine\ |
| 其他\Chart / 段位文件 / library_rating.json / exports | 输出产物\示例内容\ |
| 发布\ | 输出产物\发布\ |
| 构建产物\ | 输出产物\dev\ |
| bin\ | 输出产物\bin\（csproj OutputPath 指向） |
| 其他\docs\协作 + docs\engine-tool-guide + ENGINE_旧引擎规划 + 本地AI接入说明 | 编辑指南\文档精选\ |
| 其他\README.md / DESIGN.md / REVIEW.md | 编辑指南\ |
| 其他\打包命令.txt + _min*.ps1 | 编辑指南\工具脚本\ |
| 根 selfcheck.log / engine-check.log / aidetect.log / test.pack + 其他\logs | 输出产物\日志\ |
| 其他\Player\ai_train.json | 输出产物\示例内容\Player\ |
| .agent-teams\ | 保留根（不动） |

> 游戏内容根=程序当前目录（BaseDirectory）：Chart\ / PlayerData\ / Player\ / Replay\ / Log\ / 屏幕采集\ / 人类试玩\ 均随 exe；MpLeaderboard 已迁到 BaseDirectory\PlayerData\mp_leaderboard.json（旧数据自动迁移）。
