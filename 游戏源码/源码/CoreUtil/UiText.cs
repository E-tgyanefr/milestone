using System;

namespace ChartPlayer
{
    /* ================= UiText：用户可见文字常量表（逐字复制，禁止改写/润色/翻译/增删标点） =================
     * 来源（逐字原样提取）：
     *   源码\Play\EngineUi\EngineMainShell.cs —— 引擎主菜单全部按钮/标题/提示 + 玩家统计行
     *   源码\Play\EngineUi\EngineMainShell.cs —— 选歌页头栏/子面板/卡片/难度行全部用户可见文案
     *   源码\Forms\SettingsPanel.cs  —— 设置面板全部设置项文案/选项/按钮/提示（9 个分区：游戏/判定/分数/界面/评级/音效/皮肤/存档/键位）
     *   源码\Forms\FolderPanel.cs     —— 曲库管理页全部按钮/标题/提示
     * 用途：引擎驱动 UI（EngineMainShell）重建主界面时，一律引用本表，保证新旧界面文字一致。
     */

    public static class UiText
    {
        /// <summary>换行符（用于多行文案拼接，仅此一处定义）。</summary>
        const string NL = "\r\n";

        /* ================= 主菜单（引擎壳主菜单原文） ================= */

        /// <summary>Logo 副标题（格式：全模式音游·N 种玩法引擎）。</summary>
        public const string MenuSubtitleFormat = "全模式音游 · {0} 种玩法引擎";

        /// <summary>主按钮「开始游戏」。</summary>
        public const string MenuPlayGame = "▶ 开始游戏";

        /// <summary>功能按钮网格（18 项，与 BuildFeatureGrid 逐字一致）。</summary>
        public const string MenuEditChart = "✏ 编辑谱面";
        public const string MenuDanChallenge = "🏆 段位挑战";
        public const string MenuMp = "🌐 联机";
        public const string MenuReplay = "🎬 回放";
        public const string MenuFolder = "📂 曲库管理";
        public const string MenuSettings = "⚙ 设置";
        public const string MenuCalibration = "🎯 校准";
        public const string MenuAiDemo = "🤖 AI 演示";
        public const string MenuLayoutEditor = "📐 布局编辑器";
        public const string MenuPlayerInfo = "👤 玩家信息";
        public const string MenuMyData = "📊 我的数据";
        public const string MenuOpenLog = "📋 打开日志";
        public const string MenuTheme = "🎨 主题";
        public const string MenuAbout = "ℹ 关于";
        public const string MenuExit = "✖ 退出";
        public const string MenuLoopComposer = "🎵 回环作曲（创新玩法）";
        public const string MenuMiniMania = "🎮 迷你 mania（引擎 Demo）";

        /// <summary>✨ 引擎 UI 演示（与 EngineUiDemo.EntryButton 同值）。</summary>
        public const string MenuEngineUiDemo = "✨ 引擎 UI 演示";

        /// <summary>菜单底部提示行。</summary>
        public const string MenuHint = "💡 快捷键：F1 显示/隐藏菜单栏 · Milestone v6.0";

        /// <summary>玩家卡统计行（格式：游玩 N 次 · 累计命中 N 音符 · 最高ACC N% · 最佳连击 Nx）。</summary>
        public const string MenuStatsFormat = "🎮 游玩 {0} 次 · 累计命中 {1} 音符 · 🏆 最高ACC {2}% · 最佳连击 {3}x";

        /// <summary>关于弹窗（AboutMilestone 原文）。</summary>
        public const string AboutText = "Milestone（里程碑）v6.0" + NL + NL + "全音游模式：osu!mania / Malody / Phigros / Arcaea / Cytus / osu!standard / taiko / catch / ADOFAI / IIDX" + NL + NL + "空格=Pause · R=重开 · A=自动 · S=跳过空白 · M=切换模式部件 · ESC=退出" + NL + "F1=显示/隐藏菜单栏";
        public const string AboutCaption = "关于";

        /* ================= 创建固定文本（编辑谱面/回环作曲/布局编辑器状态栏提示） ================= */

        public const string StatusLoopComposer = "🎵 回环作曲：录（D F J K / 点环写谱）→ 奏（实时演奏）→ 扩（3 满分解锁）";
        public const string StatusLayoutEditor = "📐 布局编辑器：拖动各元素到想要的位置 · ESC 保存并退出";
        public const string StatusPlayPrefix = "▶ ";

        /* ================= 选歌页（引擎选歌原文） ================= */

        public const string SongTitle = "♪ 选歌";   // t33：改回单一 ♪ 图标（r9「□□♪」多图标并排根因=双字节 emoji 被拆段渲染）
        public const string SongFilterAll = "全部模式";
        public const string SongFilterMania = "Mania";
        public const string SongFilterPhigros = "Phigros";
        public const string SongFilterArcaea = "Arcaea";
        public const string SongFilterCytus = "Cytus";
        public const string SongFilterOsuStandard = "osu!standard";
        public const string SongFilterAdofai = "ADOFAI";
        public const string SongFilterIidx = "IIDX";

        /// <summary>maimai 分类名（ModeSystem 原文 Display 逐字）。</summary>
        public const string SongFilterMaimai = "maimai";

        /// <summary>回环作曲分类名（ModeSystem 原文 Display 逐字）。</summary>
        public const string SongFilterLoopComposer = "回环作曲";
        public const string SongPlayCurrent = "▶ 游玩当前";
        public const string SongFolder = "📂 曲库";
        public const string SongGoHome = "🏠 主菜单";
        public const string SongBackToSongs = "🎵 返回选歌";
        public const string SongPlayStart = "▶ 开始游玩";
        public const string SongRandom = "🎲 随机";
        public const string SongEditThisChart = "✏ 编辑此谱面";
        public const string SongDiffHint = "选择难度（单击选中 · 双击游玩）";
        public const string SongDiffTip = "💡 单击选中难度 · 双击开始游玩";
        public const string SongUnknownArtist = "未知作者";
        public const string SongDefaultDiff = "Normal";
        public const string SongPlaceholderDash = "--";
        public const string SongK = "K";
        public const string SongDash = "—";
        public const string SongDiffCountSuffix = " 难度";
        public const string SongDiffCountSuffix2 = " 个难度";
        public const string SongModeBadgeFormat = "{0} · {1}";
        public const string SongEmptyLibrary = "曲库为空" + NL + "请先「📂 打开谱面文件夹」指定曲库目录";
        public const string SongOptionPreset = "判定预设";
        public const string SongOptionVolume = "音量";
        public const string SongOptionScrollSpeed = "流速";
        public const string SongOptionSlant = "斜轨";
        public const string SongOption3D = "3D 渲染";
        public const string SongOptionAuto = "自动";

        /* ================= 设置页（SettingsPanel 原文） ================= */

        /// <summary>设置分区名（_secNames 原文：游戏/判定/分数/界面/评级/音效/皮肤/存档/键位）。</summary>
        public static readonly string[] SettingsSectionNames = new[] { "游戏", "判定", "分数", "界面", "评级", "音效", "皮肤", "存档", "键位" };

        // ---- 游戏 ----
        public const string SettingsGameSpeed = "流速";
        public const string SettingsGameDelayMs = "延迟(ms)";
        public const string SettingsGameVolume = "音量";
        public const string SettingsGameKeyCount = "键位(列数)";
        public const string SettingsGameJudgeBase = "判定基准";
        public const string SettingsGameAutoplay = "自动游玩";
        public const string SettingsGameAutoDelay = "🎯 自动调整延迟";
        public const string SettingsGameJudgeBaseBottom = "音符下端";
        public const string SettingsGameJudgeBaseCenter = "音符中心";
        public const string SettingsGameJudgeBaseTop = "音符上端";

        // ---- 判定 ----
        public const string SettingsJudgeColName = "名称";
        public const string SettingsJudgeColWindow = "窗口(ms)";
        public const string SettingsJudgeColScore = "得分";
        public const string SettingsJudgeColWeight = "权重";
        public const string SettingsJudgePreset = "预设";
        public const string SettingsJudgePresetName = "预设名称";
        public const string SettingsJudgeApplyPreset = "应用预设";
        public const string SettingsJudgeSavePreset = "💾 保存判定预设";
        public const string SettingsJudgeImportPreset = "📥 导入预设";
        public const string SettingsJudgeMissRow = "MISS";
        public const string SettingsJudgeHint = "表格最后一行 MISS 为判定失败的窗口；选择预设即在表格中预览数值";

        // ---- 分数 ----
        public const string SettingsScoreComboCap = "连击加成上限";

        // ---- 界面 ----
        public const string SettingsUiRightPanel = "显示右侧数据面板";
        public const string SettingsUiMinimal = "极简模式";
        public const string SettingsUiFps = "显示 FPS";
        public const string SettingsUiFullscreen = "全屏（F11）";
        public const string SettingsUiBorderless = "无边框窗口";
        public const string SettingsUiShowArt = "背景显示曲绘";
        public const string SettingsUiAcc = "ACC";
        public const string SettingsUiScore = "得分";
        public const string SettingsUiCombo = "连击";
        public const string SettingsUiHud = "命中统计";
        public const string SettingsUiJudgeText = "判定文字";
        public const string SettingsUiDevText = "偏差数字";
        public const string SettingsUiBurst = "打击特效";
        public const string SettingsUiShake = "屏幕震动";
        public const string SettingsUiOsuField = "osu!std 4:3 框定游玩区";
        public const string SettingsUiQuality = "画质";
        public const string SettingsUiQualityLow = "低（老设备 ≥60帧）";
        public const string SettingsUiQualityMid = "中（均衡）";
        public const string SettingsUiQualityHigh = "高（全特效）";
        public const string SettingsUiQualityAuto = "自动（动态调档）";
        public const string SettingsUiQualityHint = "低画质：关闭曲绘/特效/图表，老设备也流畅；自动：掉帧自动降档，流畅自动回升";
        public const string SettingsUiRefreshRate = "刷新率";
        public const string SettingsUiRefreshRateHint = "刷新率挡位：高刷新率自动降低画质以保证帧率；无限制=目标 1000+ FPS 跑满（自适应兜底）";
    public const string SettingsUiRenderBackend = "渲染后端";
    public const string SettingsUiRenderHardware = "硬件 GPU";
    public const string SettingsUiRenderWarp = "软件渲染（WARP）";
    public const string SettingsUiCpuDegree = "CPU 并行度";
    public const string SettingsUiCpuAuto = "自动";
        public const string SettingsSkinUseLane = "音符=轨道色";
        public const string SettingsSaveStaticHint = "最近通关记录将自动保存";
        public const string SettingsUiShow = "显示";
        public const string SettingsUiSlant = "斜轨（斜向轨道）";
        public const string SettingsUiCamera3D = "3D 渲染";
        public const string SettingsUiPitch = "俯仰";
        public const string SettingsUiYaw = "偏航";
        public const string SettingsUiDepth = "深度";
        public const string SettingsUiHitFx = "打击特效";
        public const string SettingsUiResult = "结算画面";
        public const string SettingsUiTheme = "UI 主题";
        public const string SettingsUiTransition = "转场风格";
        public const string SettingsUiThemeBlue = "深空蓝";
        public const string SettingsUiThemePurple = "极夜紫";
        public const string SettingsUiThemeCyan = "晨光青";
        public const string SettingsUiTransitionRandom = "随机";
        public const string SettingsUiTransitionBeam = "光束";
        public const string SettingsUiTransitionCircle = "圆环";
        public const string SettingsUiTransitionSlide = "推拉";
        public const string SettingsUiCameraHint = "相机俯仰 0~60° · 偏航 -30~30° · 深度 0.4~2.5";

        // ---- 评级 ----
        public const string SettingsGradeInfo = "评级按 ACC 固定档位：" + NL + "SSS ≥100 · SS ≥98 · S ≥95 · A ≥90" + NL + "B ≥80 · C ≥70 · D <70";

        // ---- 音效 ----
        public const string SettingsSoundStyle = "打击音效风格";
        public const string SettingsSoundClassic = "经典";
        public const string SettingsSoundElectronic = "电子";
        public const string SettingsSoundWood = "木鱼";
        public const string SettingsSoundPerJudge = "按判定区分音效";
        public const string SettingsSoundPreviewJudge = "🔊 试听判定音";
        public const string SettingsSoundPreviewMiss = "🔊 试听 MISS";
        public const string SettingsSoundEnable = "启用打击音效";
        public const string SettingsSoundVolume = "音效音量";

        // ---- 皮肤 ----
        public const string SettingsSkinHitLineY = "判定线位置";
        public const string SettingsSkinSlant = "斜轨强度";
        public const string SettingsSkinPlayScale = "界面缩放";
        public const string SettingsSkinNoteThick = "音符厚度";
        public const string SettingsSkinJudgeFont = "判定字大小";
        public const string SettingsSkinJudgePos = "判定位置";
        public const string SettingsSkinDevPos = "延迟位置";
        public const string SettingsSkinBgColor = "背景色";
        public const string SettingsSkinBgDim = "背景亮度";
        public const string SettingsSkinPreset = "配色方案";
        public const string SettingsSkinHitLineColor = "判定线颜色";
        public const string SettingsSkinHitLineThick = "判定线粗细";
        public const string SettingsSkinHitLineStyle = "判定线样式";
        public const string SettingsSkinHitLineGlow = "判定线辉光";
        public const string SettingsSkinHoldAlpha = "长条透明度";
        public const string SettingsSkinHoldStyle = "长条样式";
        public const string SettingsSkinJudgePosOnLanes = "轨道上方";
        public const string SettingsSkinJudgePosLine = "判定线中央";
        public const string SettingsSkinJudgePosTop = "顶部中央";
        public const string SettingsSkinJudgePosHidden = "隐藏";
        public const string SettingsSkinDevPosPanel = "右侧面板";
        public const string SettingsSkinPresetDefault = "默认";
        public const string SettingsSkinPresetNeon = "霓虹";
        public const string SettingsSkinPresetCandy = "糖果";
        public const string SettingsSkinPresetMono = "黑白";
        public const string SettingsSkinPresetBlueBlock = "蓝白块（4K）";
        public const string SettingsSkinLineSolid = "实线";
        public const string SettingsSkinLineDashed = "虚线";
        public const string SettingsSkinLineDotted = "点线";
        public const string SettingsSkinHoldSolid = "实心";
        public const string SettingsSkinHoldGlow = "辉光";
        public const string SettingsSkinHoldOutline = "描边";
        public const string SettingsSkinEditLayout = "📐 在皮肤中编辑布局";
        public const string SettingsSkinRandomColors = "🎲 随机配色";
        public const string SettingsSkinApplySave = "💾 应用并保存皮肤";
        public const string SettingsSkinExportJson = "📤 导出 JSON";
        public const string SettingsSkinImportJson = "📥 导入 JSON";
        public const string SettingsSkinReset = "恢复默认";

        // ---- 存档（玩家信息） ----
        public const string SettingsSavePlayerName = "玩家名称";
        public const string SettingsSavePlayerAvatar = "玩家头像";
        public const string SettingsSaveUploadAvatar = "上传头像";
        public const string SettingsSaveClear = "清除";
        public const string SettingsSavePlayer = "💾 保存玩家信息";
        public const string SettingsSaveExportData = "💾 导出玩家数据";
        public const string SettingsSaveImportData = "📂 导入玩家数据";

        // ---- 键位 ----
        public const string SettingsKeysKeyCount = "轨道数";
        public const string SettingsKeysKeys = "按键(按键录入)";
        public const string SettingsKeysApply = "💾 应用键位";
        public const string SettingsKeysHint = "点击输入框后依次按下要绑定的按键（退格删除最后一个，Del 清空）";

        // ---- 通用 ----
        public const string SettingsGoHome = "🏠 返回主菜单";
        public const string SettingsKeyCountLabel = "4K";

        /* ================= 曲库管理（FolderPanel 原文） ================= */

        public const string FolderTitle = "📂 曲库管理";
        public const string FolderPathPrefix = "📁 ";
        public const string FolderChooseFolder = "📂 选择文件夹";
        public const string FolderImportFiles = "📎 导入谱面文件";
        public const string FolderScanZip = "🗜 扫描并解压容器";
        public const string FolderRefresh = "↻ 刷新";
        public const string FolderOpenDir = "📂 打开所在文件夹";
        public const string FolderDelete = "🗑 删除所选";
        public const string FolderCopyPath = "📋 复制路径";
        public const string FolderGoSongs = "🎵 去选歌";
        public const string FolderGoHome = "🏠 主菜单";
        public const string FolderFileListTitle = "🎵 谱面文件";
        public const string FolderNoCharts = "（该文件夹没有匹配的谱面文件）";
        public const string FolderSelectHint = "选择文件以查看完整路径";
        public const string FolderPathPrefixSel = "📄 ";
        public const string FolderExtOsu = "osu!";
        public const string FolderExtMalody = "Malody";
        public const string FolderExtSm = "SM";
        public const string FolderExtSsc = "SSC";
        public const string FolderExtQuaver = "Quaver";
        public const string FolderExtMil = "Mile";
        public const string FolderExtPhigros = "Phi";
        public const string FolderExtArc = "Arc";
        public const string FolderExtCytus = "Cytus";
        public const string FolderExtContainer = "容器";
        public const string FolderExtFile = "文件";
        public const string FolderDialogDesc = "选择曲库文件夹（作为内嵌选歌的目录）";
        public const string FolderRobotDialogDesc = "选择包含压缩包的文件夹（.mcz/.osz/.zip 将自动解压）";

        /* ================= 引擎 UI 页（沿用既有文案） ================= */

        public const string UiBack = "←  返回";
        public const string UiPageSongs = "🎵 选歌";
        public const string UiPageSettings = "⚙ 设置";
        public const string UiPageHome = "🏠 主菜单";

        /// <summary>选歌空态（EngineUiDemo 原文）。</summary>
        public const string UiNoCharts = "未找到谱面，请将谱面放入 Chart 目录（或用 MainForm 选歌）。";

        /// <summary>主题卡玻璃质感行（EngineUiDemo 原文）。</summary>
        public const string UiSwatchGlass = "■ 玻璃质感";

        /// <summary>主窗体窗口标题（MainForm 原文：Milestone · 里程碑）。</summary>
        public const string WindowTitle = "Milestone · 里程碑";

        /* ================= 次级页（t49，FolderPanel/CalibrationForm/DanSelectDialog/MainForm 原文，逐字） ================= */

        // ---- 曲库管理（FolderPanel 原文） ----
        public const string FolderFilterLabel = "筛选：";
        public const string FolderFilterAll = "全部格式";
        public const string FolderFilterOsu = "osu!";
        public const string FolderFilterMc = "Malody (.mc)";
        public const string FolderFilterSm = "SM/Etterna (.sm/.ssc)";
        public const string FolderFilterQua = "Quaver (.qua)";
        public const string FolderFilterMil = "Milestone (.mil)";
        public const string FolderFilterAff = "Arcaea (.aff)";
        public const string FolderFilterTxt = "Cytus (.txt)";
        public const string FolderFilterJson = "Phigros (.json)";
        public const string FolderFilterZip = "压缩包 (.mcz/.osz/.zip)";
        public const string FolderStatsFormat = "共 {0} 个文件　｜　osu! {1}　｜　Malody {2}　｜　SM/Etterna {3}　｜　Quaver {4}　｜　其他模式 {5}　｜　容器 {6}";
        public const string FolderListTitleFormat = "🎵 谱面文件（{0}）";
        public const string FolderZipDoneFormat = "解压完成，导入 {0} 个谱面";
        public const string FolderZipDoneCaption = "曲库";
        public const string FolderExistsTip = "该谱面已在曲库中";
        public const string FolderTipCaption = "提示";
        public const string FolderCopyFailFormat = "复制失败：{0}";

        // ---- 校准（CalibrationForm 原文） ----
        public const string CalibTitle = "自动调整延迟";
        public const string CalibInfo = "跟随节拍按下任意打击键，共 8 拍";
        public const string CalibOffsetLabel = "偏移(ms)：";
        public const string CalibStart = "▶ 开始校准";
        public const string CalibApply = "✅ 应用";
        public const string CalibAppliedFormat = "✅ 已应用全局延迟：{0} ms";

        // ---- 段位挑战（DanSelectDialog 原文） ----
        public const string DanTitle = "🏆 段位挑战";
        public const string DanHead = "🏆 段位挑战 · 选择段位与曲目（双击曲目直接开始）";
        public const string DanSetLabel = "段位集";
        public const string DanSongLabel = "曲目";
        public const string DanStart = "▶ 开始游玩";
        public const string DanCancel = "取消";
        public const string DanNoCharts = "未检测到段位谱面（将含 Dan 字样的谱面放入曲库目录即可）";
        public const string DanPickFirst = "请先选择曲目";

        // ---- 玩家信息 / 我的数据 / 皮肤 / 回放 / 关于（MainForm 原文） ----
        public const string PlayerTitle = "👤 玩家信息";
        public const string PlayerOpenSettings = "⚙ 打开设置存档分区";
        public const string PlayerNameFormat = "玩家名称：{0}";
        public const string PlayerStatsFormat = "🎮 游玩 {0} 次 · 累计命中 {1} 音符 · 🏆 最高ACC {2}% · 最佳连击 {3}x";
        public const string MyDataTitle = "📊 我的数据";
        public const string MyDataEmpty = "（暂无记录）";
        public const string MyDataScoreFormat = "最高 ACC {0}% · 最高得分 {1} · 最佳连击 {2}";
        public const string MyDataRecentTitle = "📜 最近成绩：";
        public const string SkinTitle = "🎨 皮肤";
        public const string SkinOpenSettings = "⚙ 打开皮肤设置";
        public const string SkinBgColorFormat = "背景色：RGB({0},{1},{2})";
        public const string SkinHitLineFormat = "判定线：{0} · 粗细 {1} · 样式 {2} · 辉光 {3}";
        public const string SkinSlantFormat = "斜轨强度：{0:0.00}";
        public const string SkinJudgeFontFormat = "判定字大小：{0}";
        public const string ReplayTitle = "🎬 回放";
        public const string ReplayPlay = "▶ 播放所选";
        public const string ReplayEmpty = "（暂无回放记录）";
        public const string AboutContent = "（关于内容与主菜单「ℹ 关于」同源：UiText.AboutText）";
    }
}