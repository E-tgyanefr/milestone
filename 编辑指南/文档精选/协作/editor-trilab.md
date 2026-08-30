# 编辑器三栏 + 自动游玩/AI 游玩 + 创新玩法深想（editor-trilab 策划规范）

> 起草：designer-pro（策划·玩法深想/编辑器设计）· milestone-play 团队 · 任务 t2
> 读者：coder-max（t5 游戏层实现）、eng-design-max（t3 引擎策划）、eng-coder-max（t6 引擎编写）、captain、试玩（t7 终验）
> 性质：可执行规范文档 + 关键决策。不写代码。
> 依据（本文件结论全部对照现状源码/文档）：
> - 编辑器现状：「源码\Charting\ChartEditorPanel.cs」（8069 行单文件）：顶部工具栏 6 组（①谱面 ②音符 ③事件 ④AI与校准 ⑤音频·运输 ⑥视图）、右侧 _animPanel（Dock=Right, Width=Ui.P(320)，Tab：事件/判定线/音符/Arcaea，:188-253）、中间 EditorCanvas（Dock=Fill，:2800 起内嵌类）、BuildChart()/TestPlay 事件（:2815）→ 宿主 GamePanel.LoadAndPlay；编辑器被 EngineMainShell.HostContent 同窗承载（引擎窗 ClientSize=1280×800 逻辑画布 + letterbox 物理缩放，ToVirtual/PhysClient 见 EngineMainShell.cs:1153-1214）。
> - 游玩能力：GamePanel.StartAutoplay(chart,dir)（AUTO 徽章/_autoIdx 自动命中，多场分场 _stageAutoIdx）、StartAiDemo(chart,dir,AiLevel)（DemoAi=AiEngine.MakePlayer(lv,lv.Name)，徽章「🤖 AI 演示中」+ 紧张度/UR/超神/对拍）、AddCompanionAi(AiLevel)（「🤖 陪玩」面板：Score/Acc/Stamina/Tension）、AiEngine.Levels（osu!mania 70+ 等级预设）、PlayReplay/ReplaySystem。
> - 引擎能力：「引擎\engine\Storyboard.cs」（12 类 StoryEventType + JudgementHook 判定联动 + StoryFrame 每帧求值 + ToJson/FromJson）、Playfield（Lane/Line/Ring/Path 四大场）、Ruleset/RulesetFactory（6 字段描述符 + BlankRuleset 脚手架）、JudgementTracker/ScoreBoard、BeatAlignEngine/Dsp（FFT/onset 对音）、Particles/Tween/Cam3D、EngineJobs；多场（stages≤4）契约已实现（multi-stage-设计.md T27）。
> - 10 玩法：Mania/maimai/Phigros/Arcaea/Cytus/osu!std/Routlock/ADOFAI/IIDX/回环作曲（ModeSystem.cs；回环作曲=环上 4 分区 16 分格 录-奏-扩，源码\Play\LoopComposer.cs）。
>
> **⛔ 约束登记（用户新增决策）**：AI 游玩**不调用本地 AI**（Ollama/llama-server 一律禁用）——自动游玩/AI 游玩全部基于**引擎内置规则 AI**（AiEngine/AiPlayer：击打计划/紧张度/体力/对拍/超神，纯本地规则计算，零本地模型服务依赖、零网络、零显存占用）。编辑器既有「AI 摘要/检查」（AiChartReview）若受该决策影响，按既有离线回退路径（BuildFallbackText 规则文本）降级，绝不阻塞编辑器；本文全部 AI 文案均不含「本地大模型/服务」依赖。

---

## 0. 目标一句话

把编辑器从「单画布 + 右属性面板」升级为「**左资源栏 · 中编辑画布 · 右属性/事件/预览栏**」三栏工作台；在编辑器内直接提供「**自动游玩**（完整谱面演示）」与「**AI 游玩**（AI 演示/陪玩/对练）」；并基于 10 模式 + 回环作曲给出 3 个可落地创新玩法，把引擎诉求清单交给引擎团队。

---

## 1. 编辑器三栏布局规范（t5 实现依据）

### 1.1 三栏职责（最终决策 D1）

| 栏 | 宽度（1280×800 基线） | 职责（承载内容） | 来源（现有控件迁移） |
|---|---|---|---|
| **左栏（资源/结构）** | **200px** | ①曲目/谱面：📂打开谱面、🎨新建、💾保存、导出(.mc/.osu/.aff)、✏️属性、**最近谱面列表**（新增：近 8 条）；②分区：模式下拉、部件下拉、➕部件/🗑部件、🎛多场 + X/Y/W/H 舞台定位、键数、BPM、偏移；③音符模板：类型、滑条曲线、角度、细分/吸附/网格 | 工具栏组①谱面 + 组②音符整体迁入；「最近谱面」为新增小列表（打开/保存时记录，存 AppConfig） |
| **中栏（画布）** | **780px** | 编辑画布（EditorCanvas 全部现有绘制/交互不动）：模式轨道视图、播放头、时间标尺（DrawTimeBar）、音符/事件/判定线编辑、右键菜单、Ctrl+滚轮缩放、QWER/拖拽等全部快捷键 | 现状不动 |
| **右栏（属性/事件/预览）** | **300px** | Tab 化：**属性**（选中音符/事件/判定线属性——现散列控件归整）、**事件**（事件下拉/➕事件/⚡事件键帧列表 _evtList）、**判定线**、**音符**、**预览**（预览速率/缓动曲线/▶播放/⏮开头/**🅰自动游玩**/**🤖AI 游玩**/A-B 循环）、**AI**（🎯自动校准偏移/🤖AI 检查/🧙制谱助手） | 现有 _animPanel（320px，事件/判定线/音符/Arcaea 4 Tab）收窄为 300px 并**新增「属性」「预览」「AI」3 个 Tab**；工具栏组③事件、组④AI与校准迁入；组⑤音频·运输的播放类按钮迁入「预览」Tab |

**顶部工具栏保留**：组⑥视图（缩放）与状态标签、以及高频快捷键类按钮（保存、撤销/重做提示、🎮试玩、🏠主菜单）。工具栏保留两行自动换行（现有 FlowLayoutPanel），高度 Ht≈88~132（按内容行数），坐标表取 96。

### 1.2 1280×800 布局坐标表（设计基线，DpiScale=1 逻辑像素）

    +-------------------------------- 1280 --------------------------------+
    | 顶部工具栏 (0, 0, 1280, 96)   组⑥视图+保存+试玩+主菜单+状态            |
    +--------------+----------------------------------+--------------------+
    | 左栏          | 中栏·编辑画布                     | 右栏                |
    | (0,96,200,680)| (200,96,780,680)                 | (980,96,300,680)    |
    | 曲目/谱面     |  EditorCanvas（全部现有绘制）      | Tab: 属性/事件/     |
    | 分区/部件     |  播放头+时间标尺+场视图            | 判定线/音符/预览/AI |
    | 音符模板      |                                  |                    |
    +--------------+----------------------------------+--------------------+
    | 底部状态栏 (0, 776, 1280, 24)：音符/事件/判定线计数 · 播放状态 · 预览状态 |
    +-------------------------------------------------------------------------+

| 区域 | 矩形 (x, y, w, h) | 备注 |
|---|---|---|
| 顶部工具栏 | (0, 0, 1280, 96) | 现有 FlowLayoutPanel；Ht 随行数 88~132，坐标表按 96 计（y 偏移一律用 Ht 变量） |
| 左栏 | (0, 96, **200**, 680) | 高 = 800−Ht−24 |
| 中栏画布 | (200, 96, **780**, 680) | EditorCanvas Dock=Fill 于中间容器 |
| 右栏 | (980, 96, **300**, 680) | 现 320 → 300（与「预览」Tab 内容密度匹配） |
| 底部状态栏 | (0, 776, 1280, 24) | 由画布内底部绘制改为独立 strip（或画布内绘制，实现二选一；坐标预留） |

### 1.3 折叠规则与折叠态坐标

| 折叠档 | 触发 | 左栏 | 右栏 | 画布 |
|---|---|---|---|---|
| 全开（默认） | — | 200 | 300 | W−500 |
| 左收为图标轨 | 画布宽 < 640 或用户点「◀」 | **48**（仅图标+悬浮提示） | 300 | W−348 |
| 左全收 | 用户点「⏴」或再点「◀」 | 0 | 300 | W−300 |
| 右收 | 画布宽 < 560 或用户点「▶」 | 保持 | **0** | W−200（或 W−48/全宽） |
| 双收（专注模式） | 快捷键 F9 或画布宽 < 480 | 0 | 0 | 全宽 |

- 折叠入口：左栏右缘/右栏左缘各一条 **8px 宽折叠把手**（竖线+三角），点击切换；把手常显（hover 高亮），不占栏宽。
- 折叠状态**持久化**：AppConfig 新增 editor.PanelLeftMode / editor.PanelRightMode（0=开 1=轨 2=收），重启保持（决策 D5）。
- 快捷键：F8 切左栏、F9 专注模式（双收）、F10 切右栏。与现有编辑器 OnKeyDown（:5967-6085）不冲突（F5/F6 见 §2/§3）。
- 双收态：工具栏最左出现「≡ 面板」按钮，点开以**浮动面板**（Drawer，宽 300，Dock=Right 临时覆盖画布）唤出任一栏内容，点画布空白自动收回。

### 1.4 自适应（非 1280×800 窗口）规则（决策 D6）

- 栏宽按**比例+钳制**：L = clamp(round(W×200/1280), 160, 240)；R = clamp(round(W×300/1280), 240, 360)；栏高 = H − Ht − 24。
- 画布最小宽 640px：W−L−R < 640 时按 §1.3 顺序折叠（先左收轨 → 再右收）。
- 所有固定内距/字号继续走 Ui.P()/pt（DpiScale），逻辑坐标以 96dpi 计——与引擎壳 1280×800 虚拟画布 + letterbox 一致（t3 多分辨率方案落地后，物理像素由引擎统一换算，编辑器三栏只认逻辑客户区）。
- **实现落点**：三栏为 ChartEditorPanel 内部布局重构（新增 _leftPanel/_rightPanel/_canvasHost），不改引擎壳 HostContent 契约（编辑器 Dock=Fill 全客户区）；引擎侧唯一联动 = t3 的 letterbox/多分辨率方案（§6 EN-5）。
- **迁移成本**：BuildToolbar（组①-⑤按 §1.1 表拆入左右栏）+ BuildAnimPanel（Tab 扩为 6 个）各约 150~250 行改动；画布/Undo/事件编辑零改动；编辑器现有 6 组工具栏按钮**全部保留**（迁栏不删功能）。

---

## 2. 自动游玩（编辑谱面 · 完整演示）

### 2.1 功能定义（决策 D2）

**自动游玩 = 以现有游玩管线（GamePanel autoplay）对当前编辑谱面做完整自动播放演示**，所见即所玩。复用 GamePanel.StartAutoplay(chart, dir)（GameSettings.Autoplay=true → _autoIdx 逐场自动命中；多场 stages 分场 _stageAutoIdx 已支持），**不在编辑器内新写渲染/判定**——保证演示画面与正式游玩 100% 一致（编辑器编辑视图与游玩管线同源是既成契约，D-编辑器对齐差距表 §2.2）。

### 2.2 入口位置

| 入口 | 位置 | 行为 |
|---|---|---|
| 主按钮 | 右栏「预览」Tab 顶部大按钮 **「🅰 自动游玩」** | 一键进入预览 |
| 工具栏 | 组⑤音频·运输保留位：**「🅰 自动游玩」**（紧邻「🎮 试玩」） | 同上 |
| 快捷键 | **F5**（编辑器 OnKeyDown 新增；F5 当前未占用） | 同上 |
| 无音频拦截 | 谱面无音符 → toast「谱面还没有音符」（与 PlayTest 同文案）；音频未加载 → 弹窗「请先加载音频」（与「自动校准偏移」现有文案一致，t1 已验证该交互） | 不进入预览 |

### 2.3 预览承载与状态显示

- 承载：新增 TestAutoplay 事件（Action&lt;Chart,string&gt;，与 TestPlay 并列）→ 宿主 EngineMainShell.HostContent(gamePanel) 复用现有同窗承载（t50 机制），gamePanel.StartAutoplay(chart, dir)。
- 状态显示（全部复用 GamePanel 既有绘制，零新增）：左上「**AUTO 自动游玩**」徽章（现有）、底部全宽进度条（现有 ratio 条）、暂停覆盖层（空格/P 暂停、R 重开、ESC 返回）、多场模式各场边框+场名（现有）。
- 编辑器侧新增唯一状态：底部状态栏「🅰 自动游玩预览中… ESC 返回编辑器」一行（进入预览期间显示）。
- 退出路径：ESC（GamePanel.ExitToLibrary → 宿主 UnhostContent() 回编辑器）；谱面自然结束（SongEnded）同路径。

### 2.4 与播放条关系（决策 D7）

| 时点 | 编辑器播放条（画布播放头 + DrawTimeBar） | 预览端 |
|---|---|---|
| 进入预览 | **冻结**（_playing=false 编辑器音频暂停；画布停止 Invalidate） | 从**当前播放头位置** Seek 开始（GamePanel 新增可选 StartAutoplayAt(chart,dir,startMs)，默认从头） |
| 预览中 | 不显示（GamePanel 覆盖承载） | 自带进度条/暂停/重开 |
| 退出（ESC/自然结束） | **同步**：_time = 预览当前 now（自然结束=谱面末）并重绘画布；编辑器音频保持暂停（避免双声源） | 卸载 |
| A-B 循环预览（P1） | 进入前在画布上框选时间区（现有选区机制复用） | 到 B 点自动回 A 点循环，右上角「🔁 A-B」徽章，ESC 退出循环后再 ESC 返回 |

- 变速预览（PlayRate 0.25~4x）在自动游玩内**首版不做**（t4 已记录：编辑变速音频不变速，依赖引擎 AudioClock，见 §6 EN-3）；预览内以 1.0x 运行，编辑器「预览速率」框在预览期间禁用置灰。

### 2.5 性能/资源建议（t5 实施约束）

1. 进入预览前 BuildChart() 一次并缓存（TestAutoplay 传同一 Chart 实例），不重复解析。
2. 预览期间 EditorCanvas.Enabled=false（防双渲染：GamePanel 自带渲染线程 + 编辑器 OnPaint 无效重绘）；_animPanel/_leftPanel 保持不可交互（Disabled 即可）。
3. 音频：预览用 GamePanel 自带 AudioPlayer（与正式游玩同源）；编辑器 _audio（WPF MediaPlayer）先 Pause 再进入，退出不自动恢复（防双声源）。
4. 大谱提示：音符数 > 2000 时 toast「大谱面演示可能降帧，建议关闭 3D（V）」（沿用 GraphicsQuality.AutoAdapt 自动降档兜底）。
5. 多场（stages≤4）：自动游玩直接可用（T27 §2.4 已分场路由），无需额外工作。

---

## 3. AI 游玩（编辑谱面 · AI 演示/陪玩/对练）

### 3.1 功能定义（决策 D3）

**AI 游玩 = 复用现有 AI 演示 + 陪玩能力的三档组合**，全部基于**引擎内置规则 AI**（AiEngine/AiPlayer：击打计划/紧张度/体力/对拍/超神，纯本地规则计算；按用户决策**不调用本地 AI 服务**——Ollama/llama-server 一律不参与，无网络/模型/服务依赖）：

| 档位 | 名称 | 复用 | 交互 |
|---|---|---|---|
| ① | **AI 演示**（纯看） | GamePanel.StartAiDemo(chart,dir,lv)（DemoAi） | 玩家输入被忽略（现有行为：DemoAi≠null 时 HandleDown 早退）；徽章「🤖 AI 演示中 · ESC 退出」+ 紧张度/UR/超神/对拍 |
| ② | **AI 陪玩**（人机同台） | LoadAndPlay + AddCompanionAi(lv)×1~3 | 玩家正常游玩，右侧「🤖 陪玩」面板实时显示 AI Score/Acc/Stamina/Tension |
| ③ | **人机对练**（PK） | ② + 结算对比（现有陪玩面板 + MpLeaderboard 同款 AI 得分口径） | 陪玩面板顶部加「vs 我」差值行（我 Score − AI Score），结算页复用 GameResult |

### 3.2 入口位置

| 入口 | 位置 | 行为 |
|---|---|---|
| 主按钮 | 右栏「预览」Tab：**「🤖 AI 游玩」** 大按钮 + **AI 等级下拉**（AiEngine.Levels 名称列表，默认按曲库星级/难度评估映射，无数据则「1st Dan」）+ **陪玩数 0~3**（0=纯演示） | 进入对应档位 |
| 工具栏 | 组④ AI 与校准保留位：**「🤖 AI 游玩」**（打开右栏预览 Tab 并聚焦） | 跳转 |
| 快捷键 | **F6** | 以当前右栏设置直接进入（档位=陪玩数>0 ? ②/③ : ①） |
| 拦截 | 同 §2.2（无音符/无音频拦截，文案一致） | — |

### 3.3 状态显示

- 全复用 GamePanel 徽章体系：🤖 AI 演示中 · ESC 退出、😨 紧张度% · UR · ⚡超神 · 🎵对拍、🤖 陪玩 面板（Score/Acc/♥Stamina/😨Tension）。
- 编辑器底部状态栏：「🤖 AI 游玩（等级名 · 陪玩 N）预览中… ESC 返回」。
- 档位③「vs 我」差值行：陪玩面板首行，正=领先绿、负=落后红（Color 沿用 UiColors 语义）。

### 3.4 与播放条关系

与 §2.4 完全一致（进入冻结/退出同步/A-B 循环 P1）；差异点：**退出时同步编辑器 _time 并可选弹「本局 AI 数据」小卡**（等级/ACC/UR/紧张度峰值——数据来自 AiPlayer 字段，直接展示，无任何本地 AI 服务依赖）。

### 3.5 性能/资源建议

1. AI 击打计划生成 O(n) 一次性（AiEngine.MakePlayer 构造时）；>1500 音符或陪玩数 ≥2 时**后台线程生成计划**（Task.Run），UI 显示「生成 AI 计划…」（计划完成后 BeginInvoke 入局）。
2. 陪玩数上限 3；低端机（WARP/GraphicsQuality.Effective 低）默认建议 1 个陪玩（toast 提示一次）。
3. 无 GPU 影响（AI 为 CPU 逻辑，每帧 O(1) 推进）；FxParticles 随 GraphicsQuality 自动降档（现状）。
4. **不**触发任何本地 AI 服务（Ollama/llama-server 禁用）、不产生网络/磁盘 IO；GpuGuard 与 AI 服务显存防护不受影响（规则 AI 零显存占用）。
5. 与自动游玩互斥：进入 AI 游玩即 GameSettings.Autoplay=false（AI 自己打，不叠 AUTO）。

---

## 4. 创新玩法深想（基于 10 模式 + 回环作曲，3 个可落地提案）

> 原则：**全部落在引擎现有能力范围内**（谱面模型 parts/stages、判定 12 档预设、多场 ≤4、Storyboard 12 类事件 + JudgementHook、RulesetFactory、回放、BeatAlignEngine）；每个提案 = 规则 + 引擎需求（精确到模块）+ 预期体验 + 成本。与「天马行空创新提案.md」12 案不同：本 3 案**以复用已有实现为第一标准**，是"下一步就能做"的候选。

### 4.1 提案 A「回环变奏 Loop Variations」—— 作曲即谱面（推荐 P0）

- **一句话**：回环作曲录出的环，一键展平成标准 .mil，由 9 个游玩模式任选一个"演绎"；录 4 轨 = 4 种乐器声部，扩环即变奏。
- **玩法规则**：
  1. LoopComposer 现有录-奏-扩（LoopComposer.cs：4 小节循环、环上 4 分区=4 轨、16 分格吸附 ≤180ms、连续 3 满分循环扩 4 小节且 BPM+2%）之后新增第 4 阶段「**终·演绎**」：停止扩环后（ESC 或主动按 F），把已录音符**展平为绝对时间序列**（Time += Cycle×LoopMs，Col=轨）写入 Chart.Notes，保存为标准 milestone-1 .mil（mode=loopcompose 或演绎目标模式）。
  2. 演绎：选一个游玩模式打开该 .mil（Mania=4 轨 4K 直映；Phigros=4 分区自由场；maimai=环上 4 扇区；IIDX=取 4 键+余轨忽略或全 8 键映射；其余模式按 ModeSystem.KeyCount 就近映射，规则写入 ModeSystem 注释级映射表）。
  3. 计分：演绎用该模式标准判定档位（JudgeSettings.ApplyForChart 现有）；作曲侧分数（录-奏-扩 0..100）作为谱面元数据「作曲评分」一并保存展示。
- **引擎需求**：**零引擎改动**。游戏层：LoopComposer 加 FlattenToChart()（~60 行）+ ChartEditorPanel 或主菜单「回环作曲」出口加「演绎为谱面」按钮（~30 行）+ 映射表（~40 行）。引擎侧可选联动：StarterChartGenerator 已有演绎谱生成样例可作对照（eng-design-max 可选任务）。
- **预期体验**："我弹了 8 小节的即兴 → 它变成一张别人能玩的谱"。创作→游玩闭环首次打通；与 R2 MVP（回环作曲可玩）零冲突，直接放大 R2 价值。风险极低（M2 保存 .mil 本在 LoopComposer 规划内）。

### 4.2 提案 B「一歌四场 · 模式接力 Mode Relay」—— parts × stages 跨模式接力（推荐 P0）

- **一句话**：一首歌分 2~4 段，每段一种玩法（Mania→Phigros→IIDX→回环作曲…），段落间转场接力、判定档位跟随段落、HP 延续，失败从段首重试。
- **玩法规则**：
  1. 谱面用现有 **parts**（每段一个部件，模式可不同）+ **stages**（每段一个舞台，rect 全屏或分屏由谱师定）表达；新谱面元数据 relay: true（或 stages+parts 模式互异即隐式接力）。
  2. 运行：按**时间轴接力**（与 T27 多场"同屏同判"不同——接力=当前段活跃、其余段隐藏），段 i 末音符后 2s 触发 UiTransition 转场（1s 淡切），切段后 JudgeSettings.ApplyForChart(段模式) 热换判定档位。
  3. 热身窗：每段切换后**首 3 秒内判定窗口 ×1.5**（防转场手感突变，文案「模式切换：xxx」toast 复用现有 SwitchNextPart 文案样式）。
  4. 段首 Checkpoint：HP≤0（非段位局）→ 自动回段首重试（段内成绩回滚），整曲重试次数 ≤3。
  5. 结算：总分=Σ段 Score、ACC=按音符加权（现有多场合并口径）、结果页列**每段评级**（段名+模式+ACC+评级）。
- **引擎需求**（精确到模块）：
  - **游戏层（t5）**：GamePanel 段调度器（活跃段推进 + 转场 + 热身窗 + Checkpoint，~250 行）；ChartEditorPanel「🎛多场」面板加「接力/同屏」切换（~40 行）。
  - **引擎侧（t6，最小集）**：①判定档位**运行时热换**（当前 JudgeSettings.ApplyForChart 为全局静态，需确认段切换时安全重算——预期改 _levelIndex 缓存即可）；②**判定窗口乘数**（热身 ×1.5；挂 JudgementProfile 或 JudgeSettings.Levels[i].Window 的运行时乘数，~30 行）——「天马行空」§14 已有"窗口乘数"共识。其余（多场渲染/合并结算/键位路由）T27 已实现。
- **预期体验**：一首歌四种玩法轮番登场，是 10 模式最集中的展示窗口；制谱成本低（parts 现成，官方示范谱 1 天可出）；新手看热闹、老手练全能。
- **成本**：游戏层 ~300 行 + 引擎 ~40 行；无新依赖；edshot/shotdemo 可自动验证（转场帧截图）。

### 4.3 提案 C「判定演出 Judgement Storyboard」—— 教学谱与演出层（推荐 P1）

- **一句话**：把引擎 Storyboard 接进游玩渲染，判定质量驱动演出（Perfect=绽放、Miss=暗涌），一张谱=一场演出/一堂课。
- **玩法规则**：
  1. .mil 新增可选 storyboard 段（事件序列化复用引擎 Storyboard.ToJson/FromJson）：TextOverlay（指法/歌词/提示）、CameraPath（镜头）、ImageSwap/BackgroundShift（场景）、JudgementHook（perfect→粒子绽放+连击烟花；miss→风暴/暗涌；段级 hook=段 ACC≥95% 触发奖励演出）。
  2. 两种消费形态：**教学谱**（段前 TextOverlay 指法提示 + 段内 JudgementHook 强化反馈 + 段末 ImageSwap 复盘图）与**演出谱**（判定驱动镜头/灯光，结算=演出完成度=已触发 hook 数/总 hook 数）。
  3. 判定零改动（标准档位）；hook 判定数据来自 JudgementEngine 现有 _lastJudge/Hits。
- **引擎需求**：
  - **引擎侧（t6）**：Storyboard 引擎已完整（Advance/StoryFrame/OnJudgement/序列化）；需补：宿主渲染接入样板（Samples\StoryshotDemo.cs 已是样板，照搬到游戏层即可）＋ 若希望引擎级「hook 触发率统计」再加 ~20 行。**核心引擎工作≈0**。
  - **游戏层（t5）**：GamePanel.PaintCore 消费 StoryFrame（遮罩/文本/镜头姿态写入 Cam3D/粒子并入现有池，~200 行）；ChartEditorPanel 右栏「事件」Tab 加「演出」分区（事件时间轴叠加 Storyboard 事件，~150 行）；Models/ChartParserExtra 加 storyboard 段读写（~80 行）。
- **预期体验**：教学不再读字；高手局有"演出感"；谱面投稿直接视频化（呼应未来规划 P3-2 演出层）。风险：内容（文案/镜头）是资产成本——教学谱 3 张模板 + 演出谱 2 张模板入库即可起步（R4 模板典库联动）。
- **成本**：游戏层 ~430 行 + 引擎 ~20 行（样板化）；模板谱 5 张。

### 4.4 推荐与取舍（决策 D4）

| 优先级 | 提案 | 理由 |
|---|---|---|
| **P0 先做** | **4.1 回环变奏** | 引擎零改动、游戏层 ~130 行、直接放大 R2 回环作曲价值、风险最低 |
| **P0 同步** | **4.2 模式接力** | 复用 T27 多场 90%，只需引擎 2 个最小能力（档位热换 + 窗口乘数），是 10 模式最佳展示 |
| **P1 后接** | **4.3 判定演出** | Storyboard 引擎现成，纯宿主接入 + 内容模板；价值高但内容线长 |

三案互不冲突，可同谱组合（接力段内可挂演出事件）。

---

## 5. 与引擎团队沟通点（t5 游戏层 / t6 引擎编写 分工输入）

> t5（coder-max）只动「源码\」；引擎层能力 → t6（eng-coder-max）/t3（eng-design-max）。下表即"游戏层要引擎支持什么"，供 t5 直接 agent_teams_send_message 给引擎团队。
>
> **衔接更新（eng-design-max 已将本表并入 engine-design-t3.md v1.2，t6 以 v1.2 为准）**：EN-1/EN-2 已升级为 t6 **P0 批**（档位热换契约+重入断言、JudgementProfile.WindowMultiplier 运行期乘算签名，×1.5 热身不写回 12 预设；**防回归硬断言：乘数 ×1.0 时判定/结算/回放/人类模拟全流程逐字段零差异**）；EN-3 已有 IEngineAudioBackend 接口草案（PositionMs/TimeScale/Bind 采样回调→AudioClock 平滑；实现路径提示=WPF MediaPlayer.SpeedRatio 变速不改音高，宿主或仅需接线；若 t6 定稿「1.0x 限制」→ 同步交付编辑器口径：预览期间「预览速率」框禁用置灰+悬停提示，**禁止仅画面变速**，与本文 §2.4 一致）；EN-5=ViewportPolicy 方案本身（t3 §2 已覆盖）；EN-4 列 P1、EN-7 列 P2、EN-8 职责边界已标注（UiTabBar=t6、AddArrows=t5）。t6 执行顺序见 t3 v1.2 §7。
>
> **t6 回执（eng-coder-max，引擎侧构建/运行验证已完成：删 obj 重编引擎库+EngineChecks+游戏 scratch 三处 0 警告 0 错误、EngineChecks EXIT=0（DemoRunner 基线+7 组新断言，途中修正 5 处断言口径），仅剩 captain 官方打包）**：EN-1 已就绪（JudgementTracker.SetProfile/Profile 热换契约+[档位热换]断言；游戏层 JudgeSettings.ApplyForChart 的 _levelIndex 清缓存归 t5）；EN-2 已就绪（JudgementProfile.WindowMultiplier 运行期乘算钳 0.25..4，不改 12 预设；×1.0 零差异硬断言已入 DemoRunner）；EN-3 接口就绪（IEngineAudioBackend+AudioClock.Bind，宿主接线归 t5，不支持变速须禁用「预览速率」框）；EN-4 就绪（Storyboard.ResetTo/HookFired/HookRate/StoryboardPlayer，并入 GamePanel 渲染链归 t5）；EN-5 就绪（ViewportPolicy/UiCanvas.SetViewport/EngineWindow 适配，编辑器三栏只认逻辑客户区无感）；EN-6 引擎侧就绪（UiCanvas.RenderCaching 脏渲染+UiElement 测量缓存；编辑器密度柱缓存归 t5）；EN-7 P2 未强制（NoteStyle 数据层已备）；EN-8 引擎侧根治（UiTabBar.InvokeClickAt，t1 P0-1）。**模式接力 B 案引擎前提（EN-1+EN-2）已解锁**，t5 段调度器接线即可（**captain 拍板：B 案排期下一轮实施——本轮用户指令清单已全部实现仅剩终验，交付前不扩范围；届时建任务给 coder-max**）。**EN-2 接线顺序契约（t5 必读）**：WindowMultiplier 是 JudgementProfile 运行期字段，12 预设工厂每次返回全新实例且乘数恒 1.0，引擎无任何路径覆盖；正确口径=先 ApplyForChart(mode) 换档、再对当前实例设 WindowMultiplier（热身段 ×1.5），或复用同一实例仅换 Tiers——顺序颠倒会丢乘数。

| # | 引擎能力诉求 | 用途（对应本文档） | 责任方 | 优先级 | 说明/接口提示 |
|---|---|---|---|---|---|
| EN-1 | **判定档位运行时热换**：JudgeSettings.ApplyForChart(mode) 在局中可安全重算（清 _levelIndex 缓存） | 4.2 模式接力：段切换换档位 | t6 引擎编写 + t5 联调 | **P0** | 引擎侧确认全局静态切换的线程安全/重入；游戏层调用点在 GamePanel 段调度器 |
| EN-2 | **判定窗口乘数**：JudgementProfile/JudgeSettings 加运行时窗口乘数（如 ×1.5 热身），不改 12 预设数值 | 4.2 热身窗；未来教学/演出软窗 | t6 | **P0** | 「天马行空」§14 已有共识（窗口乘数挂 JudgementProfile，不与现有档位耦合） |
| EN-3 | **AudioClock 变速播放/编辑器预览时钟**：编辑预览（0.25~4x）变速时音频同步变速、播放条不抖动（t4 已报：现 WPF MediaPlayer+手搓 offset 不变速） | §2.4/§3.4 变速预览（P1） | t6 提供引擎方案、t5 接入 | P1 | 引擎 AudioClock.cs 已存在；确认能否承载宿主音频回调（变速播放 API 或建议保持 MediaPlayer+文档化限制） |
| EN-4 | **Storyboard 宿主渲染样板**：StoryFrame → 宿主渲染链的最小消费示例（遮罩/文本/镜头/粒子并入）+ hook 触发率统计接口 | 4.3 判定演出 | t6（样板）+ t5（游戏层接入） | P1 | Samples\StoryshotDemo.cs 已是样板，补齐「并入 GamePanel 现有 D2D/粒子池」的接线说明即可 |
| EN-5 | **多分辨率/letterbox 统一方案落地**（t3 文档内容）：引擎壳 1280×800 逻辑画布 → 物理客户区映射与 DPI 一致 | §1.4 三栏自适应依赖 | t6 按 t3 执行 | **P0** | 编辑器三栏只认逻辑客户区；ToVirtual/PhysClient 统一后三栏坐标无感 |
| EN-6 | **性能释放点确认**：UiElement 脏渲染/字体画笔缓存（t4 R1/R2）、编辑器密度柱缓存（t4 ⑤） | §2.5/§3.5 预览流畅 | t6（引擎 UI）+ t5（编辑器缓存） | **P0** | 预览期间编辑器停绘已规避一半；密度柱 O(视窗×全音符) 每帧重算需游戏层缓存 |
| EN-7 | **NoteStyleBook 渲染接入**（音符三色/形状/接近动画消费约定，音符设计-全模式.md §12） | 编辑器画布与预览画面视觉统一（P2） | t6 提供消费约定、t5 编辑器侧接入 | P2 | 引擎 NoteStyle.cs 已备数据层；仅约定 Shape→绘制分派，不强制本轮 |
| EN-8 | **设置页交互修复职责边界**：UiTabBar.InvokeClick 空实现（引擎 Ui\LegacyControls.cs）与 EngineMainShell 设置行微调命中错位（t1 P0-1/P0-2） | t5 里程碑必需（不在本 t2 范围但同批） | t5 改游戏层、引擎文件经 eng-coder-max 对齐 | **P0** | LegacyControls.cs 属引擎库——t5 与引擎团队提前对齐职责（captain 已通告） |

---

## 6. 关键决策（供 captain/用户拍板，t5 按此执行）

| # | 决策 | 结论 | 理由 |
|---|---|---|---|
| D1 | 三栏职责 | 左=曲目/谱面+分区/部件+音符模板（资源侧）；右=属性/事件/判定线/音符/预览/AI 六 Tab（操作侧）；中=画布不动 | 与任务描述一致；"资源在左、操作在右、画布居中"符合 RPE/ArcCreate 习惯；全部现有控件迁栏不删功能 |
| D2 | 自动游玩实现 | **复用 GamePanel.StartAutoplay 同窗承载预览**，编辑器不写第二套渲染/判定 | 保证演示=正式游玩（同源契约）；零新渲染器；多场/回放/徽章全免费 |
| D3 | AI 游玩定义 | 三档：AI 演示（DemoAi）／AI 陪玩（CompanionAi 1~3）／人机对练（陪玩+vs 差值行）；**只用引擎内置规则 AI** | **用户决策：不调用本地 AI（Ollama/llama-server）**。AiEngine 为引擎内置规则 AI，零本地服务依赖且已具备全部状态显示；编辑器既有「AI 摘要/检查」若受该决策影响，按离线回退（规则文本）降级，不阻塞 |
| D4 | 创新玩法顺序 | A 回环变奏 → B 模式接力（P0 并列）→ C 判定演出（P1） | A 零引擎改动最小闭环；B 复用 T27 多场且只需 EN-1/EN-2 两个小能力；C 内容线长后置 |
| D5 | 折叠持久化 | AppConfig 记 editor.PanelLeftMode/PanelRightMode，重启保持；F8/F9/F10 快捷键 | 三栏价值取决于用户可自定义工作区 |
| D6 | 自适应基准 | 1280×800 为设计基线；栏宽按比例+钳制（L=W×15.6% 160~240，R=W×23.4% 240~360）；画布最小 640 | 与引擎壳 1280×800 虚拟画布一致；t1 P0-4 分辨率对齐修复后任意窗口可用 |
| D7 | 预览与编辑数据关系 | 自动游玩/AI 游玩为**只读预览**：不写回编辑内容（退出只同步播放头位置）；AI 游玩结果不存档 | 预览≠试玩；试玩（🎮试玩/人类游玩）仍走现有 TestPlay 路径保留存档语义 |
| D8 | 编辑器音频策略 | 进入预览暂停编辑器 MediaPlayer，退出不自动恢复 | 防双声源；t4 音频抖动问题另由 EN-3 解决 |

---

## 7. 验收要点（供 t7 试玩终验 / t5 自测）

1. 三栏：1280×800 下左 200/中 780/右 300；折叠四档坐标正确（含把手点击/快捷键/重启保持）；拉窗口至 1100/900/700 宽按 §1.4 自适应不裁切不重叠；右栏 6 Tab 全部可达且 AutoScroll 兜底。
2. 自动游玩：F5/按钮进入 → 「AUTO 自动游玩」徽章+进度条 → ESC 返回编辑器且播放头=预览位置；无音频弹「请先加载音频」；多场谱自动游玩逐场命中无串场；预览期间编辑器无重复渲染（任务管理器 CPU 无异常尖峰）。
3. AI 游玩：F6/按钮进入三档可切换；AI 等级下拉可选且默认有值；陪玩 1~3 面板实时刷新；档位③有「vs 我」差值行；退出后编辑器正常；**全程无任何本地 AI 服务调用（Ollama 服务停止/断网下功能完全不变）**。
4. 创新玩法（若本轮落地 A/B）：回环作曲「演绎为谱面」产出 .mil 可被 Mania/Phigros 正常打开游玩；接力谱两段模式切换时档位/热身窗/HP 延续正确，转场 toast 出现，结算列每段评级。
5. 回归：所有既有编辑器功能（画布编辑/Undo/事件/判定线/AI 检查/自动校准/试玩/导出）不因迁栏丢失；--selfcheck/--modetest exit 0。

---

## 附：改动文件预估（t5 实现落点，供排期）

| 文件 | 改动 | 量级 |
|---|---|---|
| 源码\Charting\ChartEditorPanel.cs | 三栏重构（_leftPanel/_rightPanel 6 Tab/_canvasHost + 折叠 + 最近谱面列表）；TestAutoplay/TestAiPlay 事件；F5/F6/F8/F9/F10 快捷键；预览同步 | ~600 行（迁栏为主，不删功能） |
| 源码\Play\EngineUi\EngineMainShell.cs | 宿主接线（TestAutoplay/TestAiPlay → HostContent(GamePanel)） | ~60 行 |
| 源码\Play\GamePanel.cs | StartAutoplayAt(chart,dir,startMs)（可选）、AI 计划后台生成入口、「vs 我」差值行 | ~80 行 |
| 源码\CoreUtil\AppConfig.cs | editor.PanelLeftMode/PanelRightMode + 最近谱面列表持久化 | ~40 行 |
| 源码\Play\LoopComposer.cs（A 案） | FlattenToChart 演绎展平 | ~60 行 |
| 引擎\engine\（t6，经 EN 列表） | EN-1 档位热换确认 / EN-2 窗口乘数 / EN-4 样板 | ~50 行 |
