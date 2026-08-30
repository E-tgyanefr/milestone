# 非引擎化代码删除设计（legacy-removal-design，供 t54 编写执行）

> 起草：designer-pro（策划）· 任务 t53 · 用户指令：「删除非引擎化的代码，除了必要的」· 只做设计不写代码
> 依据（本轮实测源码，行号=当前源码）：
> - 引擎化已覆盖页面：主菜单、选歌页(SongGrid)、曲库管理(GoToPage folder)、段位(dan)、回放(replay)、校准(calibration)、玩家信息(player)、我的数据(mydata)、关于(about)、主题(CycleTheme 引擎内)、打开日志(Act)、设置行(t48 引擎设置分区行) —— EngineMainShell.GoToPage + SecondaryPages。
> - 非引擎化残留（待删）：MainForm 旧导航面 + SongCardView + FolderPanel + CalibEmbedPanel + CliLegacy/--legacyui + --menushot 旧菜单快照 + ParityCli legacy 块。
> - 必要保留：编辑器（引擎窗承载）、联机（引擎无）、完整设置 9 分区、校准对话框逻辑、布局编辑器、迷你 mania、引擎 UI 演示、游玩/数据/存档/评级/皮肤/判定/音频/AI 规则逻辑。

---

## 0. 目标与总原则

1. **删掉"非引擎化的代码"**：旧 WinForms 主菜单、菜单栏/状态栏、旧视图导航系统、legacy 选歌/曲库/校准全页面板、转场窗体、--legacyui 回退、--menushot/parity 的旧菜单快照路径。
2. **保留"必要的"**：引擎未覆盖的业务（联机/完整设置/校准对话框/编辑器/迷你 mania/布局编辑器/引擎 UI 演示）与其承载通路；全部游玩/数据逻辑。
3. **MainForm 最小化 = 纯宿主**：引擎壳生命周期 + 必要面板承载（HostContent/Act）+ 必要 CLI + 退出逻辑。
4. **安全优先分批**（与 captain 顺序一致）：批1 旧主菜单/菜单栏/状态栏 → 批2 SongCardView/FolderPanel/Calib（组件+引用） → 批3 MainForm 宿主收口（导航面/Act 白名单/actions 工厂化） → 批4 parity/menushot 迁移 + --legacyui 移除。
5. 每批验收：构建 0 警告 0 错误 + --selfcheck exit 0 + parity -1 全绿 + --shellshot/--menushot 产物正常 + 人工启动走查。

---

## 1. 逐组件「删除/保留」判定表

### 1.1 删除组

| 组件/代码 | 现状位置 | 判定 | 理由 |
|---|---|---|---|
| 旧主菜单 BuildMainMenu + _mainMenu | MainForm.cs:495-740、:1282 防御构建 | **删** | t42 后默认路径永不显示；引擎壳主菜单已覆盖全部 18 入口（_actions 桥接表 :872-889） |
| 菜单栏 _menu（MenuStrip，F1 显示） | MainForm.cs:12/:59-63/:96-101/:1291-1309 | **删** | 引擎壳有 F1/帮助与顶栏；菜单栏仅为 --legacyui/CliLegacy 服务 |
| 状态栏 _status（StatusStrip） | MainForm.cs:13/:65-70/:89 | **删** | 纯 legacy 装饰；引擎页有各自状态行；_st 文本由游玩/编辑器写入——删除后这些写入点改为 Logger.Info 或壳 toast |
| 转场窗体 _fade + TransitionTo + CreateTransition | MainForm.cs:30/:359-419 | **删** | 引擎壳自有 UiTransition/转场（t37 已优化） |
| ShowView/HideOthers/HideAllMenus | MainForm.cs:422-437/:1236/:1245-1256 等 | **删** | legacy 视图切换系统；引擎壳页面切换替代 |
| 7 个 legacy 视图面板+Build*View：_mainMenu/_libraryView/_settingsView/_folderView/_mpView/_calibView/_editorView | MainForm.cs:16-27、Build*View :1000-1240 区域 | **删**（保留 2 个承载容器，见 §1.2） | 页面已引擎化；面板壳仅为 legacy 导航服务 |
| ShowLibrary（legacy 选歌） | MainForm.cs:1073-1077 | **删** | 引擎选歌页 SongGrid 覆盖（t39 同源扫描） |
| ShowFolder（legacy 曲库） | MainForm.cs:1115-1118 | **删** | 引擎曲库页覆盖（GoToPage folder） |
| ShowCalib（legacy 校准全页） | MainForm.cs:1142-1145 | **删** | 引擎校准页覆盖（GoToPage calibration） |
| ShowMainMenu（重定向壳） | MainForm.cs:1269-1290 | **删**（22 处调用点改直连壳） | t42 已是"重定向到引擎壳"；删除后调用点直接 ShowEngineShell()/壳.Show |
| _legacyUi + --legacyui + CliLegacy | MainForm.cs:267/:279-283/:96/:1275 | **删** | 用户决策默认引擎 UI；回退通道仅剩防御性——删除后引擎壳初始化失败走「纯游玩窗口」兜底（见 §3.3） |
| SongCardView.cs（legacy 选歌组件） | Forms\SongCardView.cs 全文 | **删** | 引擎 SongGrid 已替代；引用仅 MainForm（见 §2.1） |
| FolderPanel.cs（legacy 曲库组件） | Forms\FolderPanel.cs 全文 | **删** | 引擎曲库页已替代（同实现）；引用仅 MainForm（见 §2.2） |
| CalibEmbedPanel.cs（legacy 校准全页嵌入） | Forms\CalibEmbedPanel.cs 全文 | **删** | 引擎校准页已替代；**校准对话框 CalibrationForm 保留**（见 §1.2） |
| --menushot 旧菜单快照 | Program.cs:1003-1030 | **删/迁移** | 迁移到引擎壳离屏渲染（§4.1） |
| ParityCli legacy 主菜单快照+桥接反射块 | ParityCli.cs:517-580 | **删/迁移** | 迁移到 EngineUiDemoForm/EngineMainShell 直查（§4.2） |

### 1.2 保留组（必要，引擎未覆盖）

| 组件 | 理由 | 承载方式（MainForm 最小化后） |
|---|---|---|
| ChartEditorPanel（编辑器） | 引擎无编辑器 | 引擎窗 HostContent（现状 t50，不动） |
| SettingsPanel（9 分区完整设置） | 引擎设置页为行式精简（t48 同字段写入），完整 9 分区 UI 仅在 SettingsPanel | Act 白名单承载（MainForm 面板或引擎壳 HostContent——实现二选一，建议 HostContent 进引擎窗） |
| CalibrationForm（校准对话框） | 打拍采集/应用逻辑所在；引擎校准页（SecondaryPages:196-229）与 SettingsPanel:348 均在用 | 保留文件；由引擎校准页/设置页弹出（现状） |
| MpEmbedPanel/MpManager/MpLeaderboard（联机） | 引擎无联机 | Act 白名单承载（现状 Act(ShowMp)） |
| MiniManiaForm（迷你 mania） | 引擎 Demo 窗口 | Act 白名单承载（现状） |
| GamePanel 布局编辑器 _editPanel | 引擎无 | GamePanel 内部，不动 |
| EngineUiDemoForm（引擎 UI 演示） | parity/shellshot 取证 + 主菜单演示入口 | 保留；parity 继续用 |
| Logger.OpenLog | 打开日志入口 | Act 白名单 |
| 游玩/数据/存档/评级/皮肤/判定/设置/音频/AI 规则逻辑 | 核心业务 | 全部不动 |
| EngineMainShell 全部（含 GoToPage/SecondaryPages/引擎设置行） | 引擎化成果 | 不动 |

### 1.3 待核对项（t54 实施时逐一确认，不阻塞设计）

| 项 | 说明 |
|---|---|
| --legacysettings/--uidebug/--editor 等 dev CLI 参数 | devCliRun 路径抑制 ShowEngineShell；t54 需确认这些参数是否仍被工具链使用（grep tools\ 脚本），按使用情况删/留，遗留者改走引擎壳或保留最小直通 |
| parity-text-baseline.json 中 legacy 主菜单文案条目 | 与 BridgeFeatureNames/LegacyMenuKeys 联动，批4 迁移时同步更新基线（或把基线键改为引擎壳文案键） |

---

## 2. 删除项引用点清单（grep 级，当前源码）

### 2.1 SongCardView（15 处）

| 位置 | 性质 |
|---|---|
| MainForm.cs:17（_library 字段）、:911（new SongCardView 初始目录）、:1023（WireLibrary 事件接线）、:1108（FolderChanged 重建） | **真实引用**（批2 删除） |
| EngineMainShell.cs:59/:481/:541/:580/:631/:657 | 注释/文案对照（"与 SongCardView 原文一致"），非运行时引用，删除组件后清注释 |
| ModeSystem.cs:10、UiText.cs:8/:65 | 注释 |
| SongCardView.cs:49/:99（类自身） | 随文件删除 |

### 2.2 FolderPanel（16 处）

| 位置 | 性质 |
|---|---|
| MainForm.cs:21（_folder 字段）、:877（actions 注释）、:1103（BuildFolderView 内 new FolderPanel） | **真实引用**（批2 删除） |
| ParityCli.cs:641（BridgeFeatureNames 字符串「曲库管理 FolderPanel 全部按钮」） | 文案字符串（批4 改「引擎曲库页」） |
| SecondaryPages.cs:10/:11/:85/:87/:93/:97 | 注释/文案对照 |
| UiText.cs:10/:263/:310/:312 | 注释/文案 |
| FolderPanel.cs:12/:41（类自身） | 随文件删除 |

### 2.3 CalibEmbedPanel（6 处）

| 位置 | 性质 |
|---|---|
| MainForm.cs:25（_calib 字段）、:1138（BuildCalibView 内 new CalibEmbedPanel） | **真实引用**（批2 删除） |
| SecondaryPages.cs:10、UiText.cs:332 | 注释 |
| CalibEmbedPanel.cs:8/:14（类自身） | 随文件删除 |

### 2.4 CalibrationForm（14 处，**全部保留**）

| 位置 | 性质 |
|---|---|
| SettingsPanel.cs:348（「🎯 自动调整延迟」→ ShowDialog）、SecondaryPages.cs:196-229（引擎校准页「开始校准」→ ShowDialog） | **真实引用，必须保留** |
| CalibEmbedPanel.cs:7/:10/:39（随文件删） | 引用随删除组件消失 |
| ParityCli.cs:642（文案「🎯 校准 CalibrationForm」） | 文案保留 |
| UiText.cs:310/:332 | 文案 |

### 2.5 MainForm 旧导航面引用点（批1/批3 处理）

- ShowMainMenu/ShowEngineShell 调用点 31 处：:73/:117/:148/:152/:154/:177/:182/:208/:230/:242/:274/:348/:746/:776/:817/:850/:889/:931/:1033/:1063/:1087/:1104/:1125/:1139/:1160/:1271/:1278/:1388/:1400/:1410/:1462 —— 其中 ShowMainMenu 22 处即「返回路径」集合，删除后统一直连 ShowEngineShell()。
- HideAllMenus/ShowView/TransitionTo 调用点 30 处：:324/:337/:395/:422/:424/:477/:668/:695/:784/:792/:807/:904/:921/:957/:1030/:1049/:1068/:1076/:1085/:1096/:1117/:1131/:1145/:1176/:1200/:1236/:1254/:1291/:1293/:1408 —— 批3 收口时逐一改为壳显隐/直接调用。
- CliLegacy 引用：MainForm.cs:73/:96/:242/:283/:348/:1275 + ParityCli.cs:518 + Program.cs:1014 —— 批1/批4 删除。
- _legacyUi/--legacyui：MainForm.cs:96/:267/:274/:279/:348/:1275 —— 批1 删除。

---

## 3. MainForm 最小化方案（纯宿主）

### 3.1 保留职责（= 新 MainForm 的完整职责）

1. **引擎壳生命周期**：创建/显示/隐藏/重建（ShowEngineShell/Reopen）、HostContent/UnhostContent。
2. **必要面板承载（Act 白名单）**：
   - 联机：ShowMp（MpEmbedPanel，MainForm 面板或 HostContent）
   - 设置：ShowSettings（SettingsPanel 9 分区）
   - 布局编辑器：EnterLayoutEditor（GamePanel._editPanel）
   - 回环作曲：StartLoopComposer（GamePanel）
   - 迷你 mania：MiniManiaForm 弹窗
   - 引擎 UI 演示：ShowEngineUiDemo（EngineUiDemoForm）
   - 打开日志：Logger.OpenLog
   - AI 演示选谱：AiDemoPickChart
3. **游玩与编辑器通路**（现状全部保留）：GamePanel 承载、TestPlay/TestAutoplay/TestAiPlay 接线、_testPlayReturn 三返回路径、编辑器 HostContent。
4. **必要 CLI**：保留 --editor/--autoplay/--play/--humantest/--autoshot/--adofai2 等被工具链使用的 dev 参数（§1.3 核对后定）；删除 --legacyui/--menushot 旧路径。
5. **退出逻辑**：Application.Exit、窗体关闭处理。

### 3.2 删除清单（MainForm 内）

- 字段：_menu/_status/_fade/_mainMenu/_libraryView/_folderView/_calibView/_library/_folder/_calib/_legacyUi/CliLegacy/_redirectToEngine。
- 方法：BuildMainMenu、ShowView、TransitionTo、CreateTransition、HideOthers、HideAllMenus、ShowMainMenu、BuildLibraryView/ShowLibrary、BuildFolderView/ShowFolder、BuildCalibView/ShowCalib、ShowSettings 的 legacy 面板路径（改为 Act 白名单承载）、BuildMainMenu 相关 18 格构造。
- 保留视图容器：_settingsView（SettingsPanel 承载）、_mpView（MpEmbedPanel 承载）、_editorView（编辑器归还容器，HostContent 失败回退用）。
- _st 状态文本写入点（游玩/编辑器/预览状态）→ 改为 Logger.Info + 壳 toast（或保留仅 MainForm 可见时的临时文本，建议统一 Logger）。

### 3.3 兜底路径（删除 --legacyui 后）

- 引擎壳初始化失败（异常/资源不足）→ 不再回退旧主菜单；兜底 = 直接以 MainForm 承载 GamePanel（无菜单，ESC/结算走标准退出），日志明确记录「引擎壳不可用，进入纯游玩模式」。防御性代码量 ≈20 行，替代原 ShowMainMenu 回退。

### 3.4 actions 字典工厂化（供 parity 直查）

- 现状：actions 字典在 MainForm.ShowEngineShell 内构造（ParityCli 靠反射 MainForm._engineShell._actions 检查桥接）。
- 设计：把 actions 字典构造移入 EngineMainShell 静态工厂 EngineMainShell.CreateDefault(playChart, menuStats)（或保留 MainForm 构造但新增 public 只读属性 Actions）——parity 不再需要 MainForm 实例即可核对桥接覆盖。
- t54 与 eng-coder-max 对齐此小改（引擎壳文件归属引擎侧，或 t54 游戏层做 public 属性）。

---

## 4. parity/--menushot 快照迁移（具体改法）

### 4.1 --menushot（Program.cs:1003-1030）

现状：CliLegacy=true → new MainForm() 显示旧菜单 → CaptureControl(form)。
改法（输出文件不变，log 格式不变）：

1. 删除 MainForm.CliLegacy = true 与 MainForm 实例。
2. 改为（ParityCli 同款，见 ParityCli.cs:479-497）：new EngineUiDemoForm + RenderPageTo("menu", new GdiDrawAdapter(g), 1280, 800) → 存 mainmenu.png。
3. 或直接复用 RunShellShot（Program.cs:1149 起，已用 EngineUiDemoForm 离屏渲染）——两命令合流：--menushot 保留命令名与产物名 mainmenu.png，内部走 shellshot 渲染管线。推荐此方案（零新代码）。
4. 删除注释「截图走旧主菜单」与相关 CliLegacy 分支。

### 4.2 ParityCli legacy 块（ParityCli.cs:517-580）

现状四段：①MainForm 实例文案快照（CollectTexts）②legacy-mainmenu.png 截图 ③反射 MainForm._engineShell._actions 桥接覆盖 ④EngineUiDemoForm ClickByText 操作流。
改法：

1. **①文案快照**：CollectTexts(form) 改为 CollectTexts(EngineUiDemoForm 实例)（EngineUiDemo.BuildMenu 文案与壳主菜单同源）——LegacyMenuKeys() 基线键不变。
2. **②legacy-mainmenu.png**：删除（壳快照 shell-mainmenu.png 已有，§4.1 的 mainmenu.png 亦可复用）；LegacyMenuRender 条目改为引用 shell 渲染结果或标记 deprecated 移除。
3. **③桥接覆盖**：不再反射 MainForm——改用 §3.4 的 EngineMainShell public 动作表（或 CreateDefault 工厂），直查 actions.ContainsKey(key)。覆盖判定语义不变（identical(桥接同一实现)/identical(壳内页面)/divergent(缺桥接)）。
4. **④ClickByText 操作流**：现状已用 EngineUiDemoForm，不动。
5. BridgeFeatureNames() 字符串更新：「曲库管理 FolderPanel 全部按钮」→「曲库管理 引擎曲库页」、「🎯 校准 CalibrationForm」保留（CalibrationForm 仍存在）。
6. parity-text-baseline.json：若含 legacy 主菜单专用键且引擎壳已覆盖，键值不变即可（文案一致）；若基线含"旧菜单独有"条目，批4 时按壳实况更新（记录 diff 数变化，不静默改基线）。

---

## 5. 回归矩阵（t54 每批后执行 + 最终全量）

| # | 场景 | 验证方式 | 预期 |
|---|---|---|---|
| 1 | 启动默认 → 引擎壳主菜单 | 人工/--shellshot | 无旧菜单闪现，单窗口 |
| 2 | 引擎化各页：选歌/曲库/段位/回放/校准/玩家信息/我的数据/关于/主题/打开日志 | 逐页点击 + parity 壳三页渲染 | 全部正常，无异常引用 |
| 3 | 设置（9 分区完整版，Act） | 打开设置逐分区点 | SettingsPanel 可用 |
| 4 | 校准：引擎校准页「开始校准」弹 CalibrationForm | 弹窗/打拍/应用偏移 | 同现状 |
| 5 | 联机（MpEmbedPanel，Act） | 大厅/排行 | 同现状 |
| 6 | 编辑器：打开/编辑/试玩/自动游玩/AI 游玩/ESC 返回 | 全链 | 同现状（t5/t14 口径） |
| 7 | 迷你 mania / 引擎 UI 演示 | 入口点击 | 弹窗正常 |
| 8 | 布局编辑器（设置→布局编辑） | 进入/拖动/ESC 保存退出 | 同现状 |
| 9 | 游玩返回路径 22 处（原 ShowMainMenu） | 逐条：游玩 ESC/结算 Back/编辑器回首页/AI 演示 ESC/联机返回/迷你 mania 关闭… | 全部回引擎壳菜单 |
| 10 | 回环作曲入口 | 主菜单进入/ESC | 同现状 |
| 11 | 单窗口约束 | EnumWindows 顶层窗口数 | 恒=1（承载路径） |
| 12 | CLI：--selfcheck/--modetest/--shellshot/--menushot/--parity -1 | 全跑 | exit 0，产物齐全（menushot=壳渲染 mainmenu.png） |
| 13 | dev CLI：--editor/--autoplay/--play/--humantest/--autoshot/--uidebug（若保留） | 按 §1.3 核对结果跑 | 行为与删前等价 |
| 14 | 存档/主题/玩家数据/排行榜 | 启动+读档 | 零回归（逻辑未动） |
| 15 | 兜底：引擎壳初始化失败模拟（临时改故障注入） | 纯游玩模式 | 不崩、日志明确 |

---

## 6. 分批执行建议（供 t54，与 captain 顺序一致）

| 批 | 内容 | 预计行数 | 验收 |
|---|---|---|---|
| **批1 旧主菜单/菜单栏/状态栏** | 删 BuildMainMenu/_mainMenu、_menu(F1)、_status、CliLegacy、--legacyui 参数（解析改 log 提示弃用）；22 处 ShowMainMenu 调用点直连 ShowEngineShell()；_st 写入点改 Logger | 删 ~500 行 | 构建 0/0 + 启动走查 + 22 返回路径全通 |
| **批2 展示层组件** | 删 SongCardView.cs / FolderPanel.cs / CalibEmbedPanel.cs 三文件；MainForm 删 _library/_folder/_calib 字段与 BuildLibraryView/BuildFolderView/BuildCalibView/ShowLibrary/ShowFolder/ShowCalib；清 EngineMainShell/SecondaryPages/UiText/ModeSystem 注释引用 | 删 ~1400 行（含组件文件） | 构建 0/0 + 选歌/曲库/校准引擎页正常 |
| **批3 MainForm 宿主收口** | 删 ShowView/TransitionTo/_fade/HideAllMenus/HideOthers/7 视图面板（留 _settingsView/_mpView/_editorView 三容器）；Act 白名单化（联机/设置/布局/回环/迷你mania/引擎UI演示/日志/AI演示选谱）；actions 字典 public 化或 CreateDefault 工厂（与 eng-coder-max 对齐）；兜底纯游玩模式 | 净删 ~400 行 | 构建 0/0 + §5 矩阵 1-11 全绿 |
| **批4 CLI/parity 迁移** | --menushot 合流 --shellshot 管线（产物 mainmenu.png 不变）；ParityCli legacy 块四段改造（§4.2）；BridgeFeatureNames/基线键复核；parity -1 全绿 | 改 ~120 行 删 ~80 行 | parity -1 exit 0 + menushot/shellshot 产物对比 |

**总删量预估**：净删 ~2400 行（MainForm 由 ~1500 行收敛至 ~900 行宿主），删 3 个文件（SongCardView/FolderPanel/CalibEmbedPanel），零引擎改动（除 actions public 化小改）。

## 附：实施风险与护栏

1. 每批独立提交口径（构建 0/0 + parity -1 + shellshot 证据）后才进下一批。
2. 批3 前不删任何「返回路径」的壳重定向逻辑（t42 的 _redirectToEngine 逻辑在批1 即随 ShowMainMenu 删除，删除前确认 22 处调用点已全部直连）。
3. UiText.cs 中 legacy 菜单文案键（MenuPlayGame 等 18 键）保留——引擎壳动作表/parity 基线仍用；仅删 BuildMainMenu 消费方。
4. parity-text-baseline.json 不静默改动：任何 diff 必须记录在 parity 输出并由 captain 确认。
5. t54 只动 源码\（Program.cs 属游戏层根，可动）；EngineMainShell actions public 化若归引擎侧，经 eng-coder-max 对齐。
