using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 引擎驱动主界面外壳（t11：直接用引擎重构，UI 重新设计）。
    /// 主菜单/选歌/设置三页均为引擎 Scene（UiCanvas+UiComponents+UiTheme），
    /// 页面切换走 SceneManager.LoadScene + SceneTransition；用户可见文字一律引用 UiText（逐字不改）。
    /// </summary>
    public sealed partial class EngineMainShell : Form
    {
        readonly Dictionary<string, Action> _actions;
        readonly Func<string> _statsProvider;
        readonly Func<Chart, string, bool> _playChart;
        readonly Func<Chart, string, bool> _editChart;   // ✏ 编辑此谱面（MainForm 编辑路径；null=不可编辑）
        readonly Func<GamePanel> _gameProvider;          // t48：设置引擎页访问游戏/皮肤实例（与 SettingsPanel 同字段写入）
        readonly SceneManager _sm = new SceneManager();
        readonly Dictionary<string, Scene> _pages = new Dictionary<string, Scene>(StringComparer.Ordinal);
        readonly Dictionary<Scene, UiCanvas> _canvases = new Dictionary<Scene, UiCanvas>();
        readonly UiTheme _theme = UiTheme.Default;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer _timer;
        D2DRenderer _d2d;
        D2DDrawAdapter _adapter;
        Bitmap _gdiBmp;
        GdiDrawAdapter _gdiAdapter;
        Graphics _adapterG;
        UiCanvas _active, _prev, _next;
        SceneTransition _trans;
        int _w, _h;
        int _lastPaintW = -1, _lastPaintH = -1;
        double _lastMs;
        double _ltx, _lty, _lscale;   // t27：本帧 letterbox 矢量变换（Dim/转场裁剪按物理坐标换算用）
        // ===== t37：按需重绘 + 静态层缓存 =====
        bool _contentDirty = true;             // 内容脏标记（悬停/转场/数据变更置位；静止帧整帧跳过，CPU 近零）
        Bitmap _backdropBmp;                   // 背景层（渐变/光晕/星点）离屏缓存位图——尺寸变化才重建，每帧 Blit
        Bitmap _prevBmp, _nextBmp;             // 转场缓存帧（GoTo 预渲染 prev/next 各一次，转场帧只做位图合成）
        readonly Dictionary<Scene, Bitmap> _pageFrames = new Dictionary<Scene, Bitmap>();   // t37 增强：页面帧预热缓存（空闲逐页捕获，GoTo 零等待）
        bool _pageFramesWarmed;
        /// <summary>诊断：D2D 是否就绪 / 已提交帧数（宿主截图与 smoke 用）。</summary>
        public bool D2dReady => _d2d != null;
        public int PaintFrames;

        // ===== t31：选歌分类页状态（曲目扫描缓存/分类过滤/选中态） =====
        readonly List<(Chart chart, string dir)> _songItems = new List<(Chart, string)>();
        bool _songsScanDone;
        int _songFilter = -1;                 // -1=全部模式；>=0=ModeSystem.Available 索引
        UiGridLayout _songsGrid;
        UiLabel _songsEmpty, _songsEmpty2;   // t58/t21：空态两行（bg 层右下，避免网格自动布局覆盖坐标）
        ClickableCard _selCard;
        (Chart chart, string dir) _selItem;

        // ===== t55：选歌工具栏状态（搜索/过滤循环） =====
        int _songFilterCycle;                     // 0=全部模式；1..=legacy 过滤项（引擎选歌原文）
        string _songsSearchText = "";
        bool _songsSearchFocus;
        UiLabel _songsSearchLabel;
        UiButton _songsFilterBtn;
        static readonly string[] SongFilterCycleOptions =
        {
            UiText.SongFilterAll, UiText.SongFilterMania, UiText.SongFilterPhigros, UiText.SongFilterArcaea,
            UiText.SongFilterCytus, UiText.SongFilterOsuStandard, UiText.SongFilterAdofai, UiText.SongFilterIidx,
        };

        // ===== t48：设置引擎页状态 =====
        int _settingsTab;
        UiGridLayout _settingsRows;
        UiTabBar _settingsTabBar;
        string _judgePresetKey;
        double _lastPointerX = -1, _lastPointerY = -1;   // t5 P0-1：最近指针虚拟坐标（HostTabBar 兜底）

        public EngineMainShell(Dictionary<string, Action> actions, Func<string> statsProvider, Func<Chart, string, bool> playChart, Func<Chart, string, bool> editChart = null, Func<GamePanel> game = null)
        {
            _actions = actions ?? new Dictionary<string, Action>();
            _statsProvider = statsProvider;
            _playChart = playChart;
            _editChart = editChart;
            _gameProvider = game;
            Text = UiText.WindowTitle;   // 与 MainForm 窗口标题一字不差（UiText 逐字表）
            // ===== P0-4（t5）：默认启动自动对齐屏幕分辨率 —— 初始客户区取主屏工作区（letterbox 已支持任意尺寸；
            // 与 legacy MainForm Size=Screen.Bounds 同语义；渲染仍为 1280×800 逻辑画布等比居中，不拉伸变形）=====
            ClientSize = StartupClientSize();
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(_theme.Bg.R, _theme.Bg.G, _theme.Bg.B);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            BuildPages();
            BuildTopBar();   // t13：F1 顶部菜单条
            _active = _canvases[_pages["menu"]];
            _sm.LoadScene(_pages["menu"]);
            _sm.Tick(0);

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (s, e) =>
            {
                double now = _clock.ElapsedMilliseconds;
                double dt = Math.Max(0, now - _lastMs) / 1000.0;
                _lastMs = now;
                _sm.Tick(dt);
                // t37：转场/脏帧保持逐帧；静止页跳过 Invalidate（OnPaint 直接 1:1 Blit 缓存帧，CPU 近零）
                if (_trans != null || _contentDirty) Invalidate();
                else WarmPageFramesOneTick();   // t37 增强①：空闲期逐页预热转场帧（每 tick 一页，摊薄一次性预渲染）
            };
            _lastMs = _clock.ElapsedMilliseconds;
            _timer.Start();
        }

        /// <summary>从隐藏/后台恢复：重建三页（最新玩家统计/曲目）并显示（MainForm.ReturnToMenu 调用，t53 批1）。</summary>
        public void Reopen()
        {
            if (IsDisposed) return;
            BuildPages();
            _prev = null; _next = null; _trans = null;
            _active = _canvases[_pages["menu"]];
            _sm.LoadScene(_pages["menu"]);
            _sm.Tick(0);
            if (_d2d != null && _active != null) { _active.Width = _w; _active.Height = _h; }
            _contentDirty = true;                 // t37：页面重建后强制重绘
            try { _prevBmp?.Dispose(); } catch { }
            try { _nextBmp?.Dispose(); } catch { }
            _prevBmp = null; _nextBmp = null;     // t37：缓存帧随页面重建失效
            foreach (var b in _pageFrames.Values) { try { b?.Dispose(); } catch { } }
            _pageFrames.Clear();
            _pageFramesWarmed = false;            // t37 增强：预热帧随页面重建失效，空闲期重捕
            Show();
            BringToFront();
        }

        // ===== t50：引擎窗内容承载（GamePanel / 编辑器同窗，无独立窗体） =====
        // 被承载控件以 WS_CHILD 子控件落入引擎窗（非顶层窗口）——EnumWindows 顶层窗口枚举恒 =1。
        Control _hostedContent;

        /// <summary>当前引擎窗是否承载外部内容（游玩/编辑器）。</summary>
        public bool ContentHosted => _hostedContent != null;

        /// <summary>把内容控件嵌入引擎窗（同窗承载；重复嵌入同控件为无操作；先解除旧承载）。</summary>
        public void HostContent(Control content)
        {
            if (content == null) return;
            if (ReferenceEquals(_hostedContent, content) && content.Parent == this) return;
            UnhostContent();
            _hostedContent = content;
            content.Dock = DockStyle.Fill;
            Controls.Add(content);               // 子控件承载：非顶层窗口，可见窗口数仍 =1
            content.BringToFront();
            content.Focus();
            _timer.Stop();                       // 内容承载期间引擎 UI 场景不驱动（游玩/编辑器自带循环）
            EngineHosting.LogWindowState("HostContent(" + (content is GamePanel ? "GamePanel" : content.GetType().Name) + ")");
        }

        /// <summary>解除内容承载：回到引擎 UI 当前页（原场景仍在，无需重建）。</summary>
        public void UnhostContent()
        {
            if (_hostedContent == null) return;
            Controls.Remove(_hostedContent);
            _hostedContent = null;
            _timer.Start();
            if (_active != null) { _active.Width = _w; _active.Height = _h; }
            _contentDirty = true;                 // t37：解除承载回到引擎 UI 页——强制重绘
            Invalidate();
            EngineHosting.LogWindowState("UnhostContent");
        }

        UiCanvas NewCanvas(Scene scene, string name)
        {
            var go = scene.AddRootGameObject(name);
            var canvas = go.AddComponent<UiCanvas>();
            canvas.Width = 1280; canvas.Height = 800; canvas.ViewWidth = 1280; canvas.ViewHeight = 800;
            canvas.RenderCaching = true;                       // t37：引擎级脏渲染（悬停/按压自标脏）
            canvas.Invalidated += () => _contentDirty = true;  // t37：脏标记联动壳层整帧重绘
            _canvases[scene] = canvas;
            return canvas;
        }

        void BuildPages()
        {
            _pages["menu"] = BuildMenu();
            _pages["songs"] = BuildSongs();
            _pages["settings"] = BuildSettings();
            RegisterSecondaryPages();   // t49：曲库/校准/段位/玩家信息/我的数据/皮肤/回放/关于（SecondaryPages.cs 分部）
        }

        Scene BuildMenu()
        {
            var scene = new Scene();
            var canvas = NewCanvas(scene, "MainMenu");
            // 背景透明：深空渐变/光晕/星点由 OnPaint 背景层绘制（UI 美化 v2）
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 800; bg.Padding = 48; bg.Background = new RgbaColor(0, 0, 0, 0); bg.CornerRadius = 0;

            // ===== t58 现代 UI：暗色渐变背景(OnPaint DrawBackdrop) + 玻璃卡片 + CTA =====
            // —— 顶栏 y=36..84：左 Logo ×2；右=玩家胶囊（头像+名）+ ⚙ 设置 + ✖ 退出 ——
            var logoA = bg.AddChild<UiLabel>();
            logoA.Text = "Mile"; logoA.Color = new RgbaColor(255, 255, 255); logoA.FontSize = 30;
            logoA.Width = 132; logoA.Height = 46; logoA.X = 96; logoA.Y = 37; logoA.Align = UiAlign.Right;
            var logoB = bg.AddChild<UiLabel>();
            logoB.Text = "stone"; logoB.Color = new RgbaColor(45, 108, 255); logoB.FontSize = 30;
            // t61（§5.5）：段间距 32px→6px —— 图元盒按 advance 对齐而 GDI 墨迹含 side-bearing 死区（Mile 尾 18px + stone 头 14px），
            // 渲染"分串测量≠分串绘制"就错位；此处按 §5.5 实现注意硬编码段位：logoB.X = 228 - (18+14-6) = 202（实测 2026-08-28）。
            // logoA 保持右对齐 96..228（墨迹 96..210）；logoB 左对齐 202（墨迹 216..330）→ 墨迹间隙 6px。
            logoB.Width = 230; logoB.Height = 46; logoB.X = 202; logoB.Y = 37; logoB.Align = UiAlign.Left;   // t59：宽 132→220——"stone" 单行完整（此前折行成 ston+n）

            var prof = PlayerData.LoadProfile();
            string pName = prof != null ? prof.Name : "";
            var ava = bg.AddChild<UiPanel>();
            // §5.6 色族统一：主蓝 #2C6CFF α≥220（此前 α=120 与深底 (10,14,22) 色差≈9/255 不可检出）；α=224 图层 (45,108,255) 与背景色差≈45/255 可检出
            ava.Width = 36; ava.Height = 36; ava.X = 930; ava.Y = 42; ava.CornerRadius = 18; ava.Background = new RgbaColor(45, 108, 255, 224);
            var avaText = ava.AddChild<UiLabel>();
            avaText.Text = pName.Length > 0 ? pName.Substring(0, 1) : "玩";
            avaText.Color = new RgbaColor(255, 255, 255); avaText.FontSize = 15;
            avaText.Width = 36; avaText.Height = 36; avaText.Align = UiAlign.Center;
            var pLabel = bg.AddChild<UiLabel>();
            pLabel.Text = pName.Length > 0 ? pName : "玩家";
            pLabel.Color = new RgbaColor(226, 233, 245); pLabel.FontSize = 13;
            // t61b（§5.5 图标槽宽）：头像圆 930..966 → 名字文字 X = 槽右(966) + 6 = 972（旧 974=8px；实测 bin 23:37:30 墨迹间隙 26px 已随新 bin 消失）
            pLabel.Width = 120; pLabel.Height = 20; pLabel.X = 972; pLabel.Y = 49; pLabel.Align = UiAlign.Left;
            var btnSettingsTop = GlassButton(UiText.MenuSettings, 84, 36);
            btnSettingsTop.X = 1090; btnSettingsTop.Y = 42;   // P1：与 ✖ 间距 16（1090+84+16=1190）
            btnSettingsTop.Clicked += () => GoTo("settings", TransitionStyle.CircleReveal);
            bg.AddChild(btnSettingsTop);
            var btnExitTop = GlassButton(UiText.MenuExit, 80, 36);
            btnExitTop.X = 1190; btnExitTop.Y = 42;
            btnExitTop.Clicked += () => InvokeAction(UiText.MenuExit);
            bg.AddChild(btnExitTop);

            // —— 左主区 x=96..600 ——
            var play = bg.AddChild<UiButton>();
            play.Text = UiText.MenuPlayGame; play.CornerRadius = 14; play.FontSize = _theme.FnH2 - 2;   // t20 P1-7：字号微降；t29：文案「▶ 开始游戏」≈124px 远小于 460 宽，去省略号（全文）
            play.Bg = new RgbaColor(45, 108, 255); play.HoverBg = new RgbaColor(64, 126, 255); play.PressBg = new RgbaColor(38, 92, 220);
            play.TextColor = new RgbaColor(255, 255, 255);
            play.X = 96; play.Y = 140; play.Width = 460; play.Height = 64;
            play.Clicked += () => GoTo("songs", TransitionStyle.Slide);
            bg.AddChild(play);

            // 三张快捷玻璃卡：编辑谱面 / ⚙ 设置 / 段位挑战（148×64，间距 8）
            AddQuickCard(bg, UiText.MenuEditChart, 96, 224, () => InvokeAction(UiText.MenuEditChart));
            AddQuickCard(bg, UiText.MenuSettings, 260, 224, () => GoTo("settings", TransitionStyle.CircleReveal));
            AddQuickCard(bg, UiText.MenuDanChallenge, 424, 224, () => InvokeAction(UiText.MenuDanChallenge));   // 156 宽统一：96/260/424，末卡右界 580<640

            // 玩家统计（MenuStatsFormat 单行；无宿主注入直读 PlayerData）
            var stats = bg.AddChild<UiLabel>();
            string statsText = _statsProvider?.Invoke() ?? "";
            if (string.IsNullOrEmpty(statsText))
            {
                var pdStats = PlayerData.Load().Stats;
                statsText = string.Format(UiText.MenuStatsFormat,
                    pdStats.Plays, pdStats.NotesHit, pdStats.MaxAcc.ToString("0.00"), pdStats.BestCombo);
            }
            // t61 P0-1：统计改两行紧凑（按原分隔符切分，各段原文保留，数据完整）
            var parts = statsText.Split(new[] { " · " }, StringSplitOptions.None);
            var stats2 = bg.AddChild<UiLabel>();
            stats2.Text = (parts.Length > 0 ? parts[0] : statsText) + (parts.Length > 2 ? " · " + parts[2] : "");
            stats2.Color = new RgbaColor(148, 163, 190); stats2.FontSize = 10;
            stats2.Width = 440; stats2.Height = 18; stats2.X = 96; stats2.Y = 408; stats2.Align = UiAlign.Left; stats2.AutoShrinkFont = true;   // t21 P2-1：限宽 440 收缩；t29：缩字号保全文（下限 9），弃省略号
            stats.Text = parts.Length > 1 ? parts[1] + (parts.Length > 3 ? " · " + parts[3] : "") : statsText;
            stats.Color = new RgbaColor(148, 163, 190); stats.FontSize = 10;
            stats.Width = 440; stats.Height = 18; stats.X = 96; stats.Y = 428; stats.Align = UiAlign.Left; stats.AutoShrinkFont = true;   // t21 P2-1：限宽 440 收缩；t29：缩字号保全文（下限 9），弃省略号

            // t61 P0-2：药丸改两行 5×2（每枚 94×24，行宽 490，标签无交叠）
            var badgeRows = new List<UiStackLayout>();
            for (int bi = 0; bi < 2; bi++)
            {
                var row = bg.AddChild<UiStackLayout>();
                row.Orientation = UiOrientation.Horizontal; row.Spacing = 4; row.FillCrossAxis = false;
                row.X = 96; row.Y = 352 + bi * 26; row.Width = 490; row.Height = 24;
                badgeRows.Add(row);
            }
            int cnt = 0;
            foreach (var mi in ModeSystem.Available)
            {
                cnt++;
                var chip = new UiPanel { Width = 94, Height = 24, CornerRadius = 12, Background = ModeBadgeColor(mi.Mode).WithAlpha(190) };
                var name = new UiLabel { Text = LegacyBadgeLabel(mi.Mode), Color = new RgbaColor(255, 255, 255), FontSize = 10, Width = 94, Height = 24, Align = UiAlign.Center };
                chip.AddChild(name);
                badgeRows[(cnt - 1) / 5].AddChild(chip);
            }

            // —— 右功能区 x=640..1184：'功能' 两列网格 14 张玻璃卡 ——
            var funcTitle = bg.AddChild<UiLabel>();
            funcTitle.Text = "功能";
            funcTitle.Color = new RgbaColor(148, 163, 190); funcTitle.FontSize = 13;
            funcTitle.Width = 120; funcTitle.Height = 20; funcTitle.X = 640; funcTitle.Y = 120; funcTitle.Align = UiAlign.Left;

            var grid = bg.AddChild<UiGridLayout>();
            grid.X = 614; grid.Y = 152; grid.Width = 646; grid.Height = 382;
            grid.Columns = 2; grid.Spacing = 12; grid.CellHeight = 46;   // t61b：卡宽 (646-12)/2=317——「（引擎 Demo）」尾部完整入卡（≥286+24px）
            AddFunctionCard(grid, UiText.MenuMp);
            AddFunctionCard(grid, UiText.MenuReplay);
            AddFunctionCard(grid, UiText.MenuFolder);
            AddFunctionCard(grid, UiText.MenuCalibration);
            AddFunctionCard(grid, UiText.MenuAiDemo);
            AddFunctionCard(grid, UiText.MenuLayoutEditor);
            AddFunctionCard(grid, UiText.MenuPlayerInfo);
            AddFunctionCard(grid, UiText.MenuMyData);
            AddFunctionCard(grid, UiText.MenuOpenLog);
            AddFunctionCard(grid, UiText.MenuTheme);
            AddFunctionCard(grid, UiText.MenuAbout);
            AddFunctionCard(grid, UiText.MenuLoopComposer);
            AddFunctionCard(grid, UiText.MenuMiniMania, 10.5);       // t61 P0-3：长文案卡收窄字号，286 宽内不穿插
            AddFunctionCard(grid, UiText.MenuEngineUiDemo, 10.5);

            // 底部提示（金色 α150 小字）
            var hint = bg.AddChild<UiLabel>();
            hint.Text = UiText.MenuHint; hint.Color = C(UiColors.Gold).WithAlpha(150); hint.FontSize = _theme.FnCaption;
            hint.Width = 1184; hint.Height = 18; hint.X = 96; hint.Y = 766; hint.Align = UiAlign.Center;
            return scene;
        }

        /// <summary>顶栏玻璃小按钮（低饱和半透白 + 1px 描边）。</summary>
        /// <summary>顶栏玻璃小按钮（t61b §5.6 色族统一：深底+主蓝 #2C6CFF α≥220 图标+白字；底色差 ≥18/255 可检出）。</summary>
        static UiButton GlassButton(string text, double w, double h)
        {
            var b = new UiButton { Text = text, Accent = false, CornerRadius = 10, FontSize = 11, Width = w, Height = h };
            // 深底 (36,48,78) vs 背景 (10,14,22)：各通道差 26/34/56 ≥18 → 取证可检出；Hover/Press 递增亮度
            b.Bg = new RgbaColor(36, 48, 78); b.HoverBg = new RgbaColor(52, 68, 108); b.PressBg = new RgbaColor(28, 38, 64);
            b.TextColor = new RgbaColor(226, 233, 245);
            return b;
        }

        /// <summary>快捷玻璃卡（148×64：玻璃底 + 1px 描边 #2A334A + 圆角 12）。</summary>
        void AddQuickCard(UiElement host, string text, double x, double y, Action click)
        {
            var c = new ClickableCard();
            c.Title = text; c.TitleFont = UiMeasure.FitFont(text, 156 - 2 * _theme.S3 - 4, 12); c.TitleColor = new RgbaColor(232, 238, 248);   // t29：字号适配保全文（UiCard 标题不再省略）
            c.Width = 156; c.Height = 64; c.X = x; c.Y = y; c.CornerRadius = 12;   // P1：三卡 156 宽统一（96/260/424 末卡右界 580<640）
            c.Background = new RgbaColor(255, 255, 255, 10);
            c.HoverBg = new RgbaColor(255, 255, 255, 24);
            c.BorderColor = new RgbaColor(42, 51, 74); c.BorderThickness = 1;
            c.TopAccent = false;
            c.Clicked += click;
            host.AddChild(c);
        }

        /// <summary>功能区玻璃卡（286×46，悬停提亮；行为=InvokeAction 等价；fontSize 供长文案卡收窄）。</summary>
        void AddFunctionCard(UiGridLayout grid, string text, double fontSize = 11.5)
        {
            var c = new ClickableCard();
            c.Title = text; c.TitleFont = UiMeasure.FitFont(text, 286 - 2 * _theme.S3 - 4, fontSize); c.TitleColor = new RgbaColor(226, 233, 245);   // t29：字号适配保全文
            c.Width = 286; c.Height = 46; c.CornerRadius = 12;
            c.Background = new RgbaColor(255, 255, 255, 10);
            c.HoverBg = new RgbaColor(255, 255, 255, 24);
            c.SelBg = new RgbaColor(255, 255, 255, 34);
            c.BorderColor = new RgbaColor(58, 82, 128); c.BorderThickness = 1;   // P2：描边提亮
            c.TopAccent = false;
            string t = text;
            c.Clicked += () => InvokeAction(t);
            grid.AddChild(c);
        }

        /// <summary>模式片总数（ModeSystem.Available 全部=10）。</summary>
        static int AvailableBadgeCount()
        {
            int n = 0;
            foreach (var _mi in ModeSystem.Available) n++;
            return Math.Max(1, n);
        }

        /// <summary>模式片标签（legacy BuildModeBadges 同款：Routlock/ADOFAI/osu!std 等）。</summary>
        static string LegacyBadgeLabel(GameMode m) => m switch
        {
            GameMode.Mania => "Mania",
            GameMode.Maimai => "maimai",
            GameMode.Phigros => "Phigros",
            GameMode.Arcaea => "Arcaea",
            GameMode.Cytus => "Cytus",
            GameMode.OsuStandard => "osu!std",
            GameMode.Adofai => "Routlock",
            GameMode.AdofaiReal => "ADOFAI",
            GameMode.Iidx => "IIDX",
            _ => ModeSystem.DisplayName(m),
        };

        /// <summary>主区支柱卡（玻璃卡视觉：低饱和半透明 + 悬停/按压反馈）。</summary>
        void AddPillarCard(UiGridLayout grid, string text, bool gotoPage)
        {
            var b = new UiButton
            {
                Text = text,
                Accent = false,
                CornerRadius = 12,
                FontSize = _theme.FnTitle
            };
            b.Bg = new RgbaColor(255, 255, 255, 16);
            b.HoverBg = new RgbaColor(255, 255, 255, 30);
            b.PressBg = new RgbaColor(255, 255, 255, 44);
            b.TextColor = _theme.TextPrimary;
            string t = text;
            if (gotoPage) b.Clicked += () => GoTo("settings", TransitionStyle.CircleReveal);
            else b.Clicked += () => InvokeAction(t);
            grid.AddChild(b);
        }

        static RgbaColor C(Color c) => new RgbaColor(c.R, c.G, c.B, c.A);

        /// <summary>支柱按钮（主区 2×2：大号玻璃卡）。</summary>
        void AddPillar(UiGridLayout grid, string text, bool gotoPage = false)
        {
            var b = new UiButton
            {
                Text = text,
                Accent = false,
                CornerRadius = _theme.R3,
                FontSize = _theme.FnTitle
            };
            string t = text;
            if (gotoPage) b.Clicked += () => GoTo("settings", TransitionStyle.CircleReveal);
            else b.Clicked += () => InvokeAction(t);
            grid.AddChild(b);
        }

        static RgbaColor ModeBadgeColor(GameMode m) => m switch
        {
            GameMode.Mania => new RgbaColor(61, 123, 255),
            GameMode.Phigros => new RgbaColor(255, 210, 63),
            GameMode.Arcaea => new RgbaColor(155, 140, 255),
            GameMode.Cytus => new RgbaColor(122, 208, 255),
            GameMode.Maimai => new RgbaColor(255, 120, 160),
            GameMode.OsuStandard => new RgbaColor(255, 150, 180),
            GameMode.Adofai => new RgbaColor(255, 170, 90),
            GameMode.AdofaiReal => new RgbaColor(255, 170, 90),
            GameMode.Iidx => new RgbaColor(255, 90, 90),
            GameMode.LoopComposer => new RgbaColor(120, 220, 255),
            _ => new RgbaColor(61, 123, 255)
        };

        void AddMenuButton(UiGridLayout grid, string text)
        {
            // t45：低饱和玻璃卡（半透明白 + 悬停/按压反馈）；文案/行为不变（同 implementation）
            var b = new UiButton
            {
                Text = text,
                Accent = false,
                CornerRadius = 8,
                FontSize = 12
            };
            b.Bg = new RgbaColor(255, 255, 255, 14);
            b.HoverBg = new RgbaColor(255, 255, 255, 26);
            b.PressBg = new RgbaColor(255, 255, 255, 40);
            b.TextColor = _theme.TextPrimary;
            string t = text;
            b.Clicked += () => InvokeAction(t);
            grid.AddChild(b);
        }

        void InvokeAction(string text)
        {
            if (_actions.TryGetValue(text, out var a)) { try { a(); } catch (Exception ex) { Logger.Error("引擎 UI 动作失败：" + text, ex); } }
        }

        Scene BuildSongs()
        {
            var scene = new Scene();
            var canvas = NewCanvas(scene, "SongSelect");
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 800; bg.Padding = 0; bg.Background = new RgbaColor(0, 0, 0, 0); bg.CornerRadius = 0;

            // ===== t55：legacy 工具栏（♪ 选歌 + 搜索框 + 全部模式循环 + ▶ 游玩当前/📂 曲库/🏠 主菜单） =====
            // t29/t33：图标与「选歌」拆双标签正确实现（间距 +8）——t33：按完整码点取首图标（代理对成对提取，
            // 杜绝双字节 emoji 被 Substring(0,1) 拆成孤立代理渲染成 □□；UiText 已改回单一 ♪ 图标）
            string stTitle = UiText.SongTitle ?? "";
            string stIcon = stTitle.Length >= 2 && char.IsHighSurrogate(stTitle[0]) && char.IsLowSurrogate(stTitle[1]) ? stTitle.Substring(0, 2) : (stTitle.Length > 0 ? stTitle.Substring(0, 1) : "");
            string stRest = stIcon.Length > 0 ? stTitle.Substring(stIcon.Length).TrimStart() : stTitle;
            var titleIcon = bg.AddChild<UiLabel>();
            titleIcon.Text = stIcon;
            titleIcon.Color = _theme.TextPrimary; titleIcon.FontSize = _theme.FnTitle;
            titleIcon.Width = 32; titleIcon.Height = 26; titleIcon.X = 64; titleIcon.Y = 44; titleIcon.Align = UiAlign.Left;
            var title = bg.AddChild<UiLabel>();
            title.Text = stRest;
            title.Color = _theme.TextPrimary; title.FontSize = _theme.FnTitle;
            title.Width = 126; title.Height = 26; title.X = 64 + 32 + 8; title.Y = 44; title.Align = UiAlign.Left;

            // 搜索框（点击聚焦，窗键字符输入；过滤曲名/作者）
            var search = bg.AddChild<ClickableCard>();
            search.Title = ""; search.Width = 260; search.Height = 30; search.X = 232; search.Y = 42;
            search.CornerRadius = 4; search.Background = new RgbaColor(16, 24, 40);
            search.BorderColor = new RgbaColor(42, 58, 90); search.BorderThickness = 1; search.TopAccent = false;
            search.Clicked += () => _songsSearchFocus = true;
            var searchText = search.AddChild<UiLabel>();
            searchText.Text = _songsSearchText; searchText.Color = new RgbaColor(223, 230, 240); searchText.FontSize = 13;
            searchText.Width = 250; searchText.Height = 26; searchText.X = 8; searchText.Y = 2; searchText.Align = UiAlign.Left;
            _songsSearchLabel = searchText;

            // 全部模式 ▾：legacy 过滤下拉 → 循环切换（选项=引擎选歌原文）
            var filterBtn = bg.AddChild<UiButton>();
            filterBtn.Text = SongFilterCurrentLabel(); filterBtn.Width = 130; filterBtn.Height = 30;
            filterBtn.X = 486; filterBtn.Y = 42; filterBtn.CornerRadius = 4; filterBtn.Accent = false; filterBtn.FontSize = 12;
            filterBtn.Clicked += () => { CycleSongFilter(); RebuildSongList(); };
            _songsFilterBtn = filterBtn;

            // 右端按钮组（legacy 头栏同款；间距 8，宽度合计 360 < 390 不重叠）
            var btns = bg.AddChild<UiStackLayout>();
            btns.X = 830; btns.Y = 42; btns.Width = 390; btns.Height = 30;
            btns.Orientation = UiOrientation.Horizontal; btns.Spacing = 8; btns.FillCrossAxis = false;
            var bPlay = new UiButton { Text = UiText.SongPlayCurrent, Accent = true, CornerRadius = _theme.R2, FontSize = 11, Width = 130, Height = 30 };
            bPlay.Clicked += () => PlaySelectedSong();
            btns.AddChild(bPlay);
            var bFolder = new UiButton { Text = UiText.SongFolder, Accent = false, CornerRadius = _theme.R2, FontSize = 11, Width = 114, Height = 30 };
            bFolder.Clicked += () => InvokeAction(UiText.MenuFolder);
            btns.AddChild(bFolder);
            var bHome = new UiButton { Text = UiText.SongGoHome, Accent = false, CornerRadius = _theme.R2, FontSize = 11, Width = 114, Height = 30 };
            bHome.Clicked += () => GoTo("menu", TransitionStyle.Slide);
            btns.AddChild(bHome);

            // 曲目卡网格（玻璃卡片：悬停高亮 / 单击选中描边 / 双击游玩）
            // t39：ScrollableSongGrid——30+ 卡片同源后可滚轮纵向滚动（ClipChildren 裁剪可视区）
            var grid = bg.AddChild<ScrollableSongGrid>();
            grid.X = 64; grid.Y = 96; grid.Width = 1160; grid.Height = 660;
            grid.Columns = 3; grid.Spacing = 14; grid.ClipChildren = true;
            _songsGrid = grid;

            // t58：空态挂在 bg 层（右下、右对齐、白字两行——legacy 同款；网格自动布局会覆盖子元素坐标）
            // t21 P1-3：空态拆两个独立 Label（各 36px 行高——引擎 UiLabel 的 \n 行距不可控，拆行子弹级修复）
            var emptyLines = UiText.SongEmptyLibrary.Split('\n');
            _songsEmpty = bg.AddChild<UiLabel>();
            _songsEmpty.Text = emptyLines.Length > 0 ? emptyLines[0] : UiText.SongEmptyLibrary;
            _songsEmpty.Color = new RgbaColor(255, 255, 255);
            _songsEmpty.FontSize = _theme.FnBody;
            _songsEmpty.Width = 460; _songsEmpty.Height = 36;
            _songsEmpty.X = 700; _songsEmpty.Y = 560;
            _songsEmpty.Align = UiAlign.Right;
            _songsEmpty.Visible = false;
            _songsEmpty2 = bg.AddChild<UiLabel>();
            _songsEmpty2.Text = emptyLines.Length > 1 ? emptyLines[1] : "";
            _songsEmpty2.Color = new RgbaColor(255, 255, 255);
            _songsEmpty2.FontSize = _theme.FnBody;
            _songsEmpty2.Width = 460; _songsEmpty2.Height = 36;
            _songsEmpty2.X = 700; _songsEmpty2.Y = 600;
            _songsEmpty2.Align = UiAlign.Right;
            _songsEmpty2.Visible = false;

            // 曲目扫描一次并缓存（分类数量与列表同源）
            if (!_songsScanDone)
            {
                foreach (var (c, d) in EnumerateCharts()) _songItems.Add((c, d));
                _songsScanDone = true;
            }
            RebuildSongList();
            return scene;
        }

        /* ================= 选歌分类页辅助（t31：分类过滤/玻璃卡片/选中态，逻辑与原文案保留） ================= */

        /// <summary>分类标签（与 legacy 选歌文案一致；不足项取 ModeSystem.Display 原文）。</summary>
        static string SongCategoryLabel(ModeSystem.ModeInfo mi)
        {
            switch (mi.Mode)
            {
                case GameMode.Mania: return UiText.SongFilterMania;
                case GameMode.Phigros: return UiText.SongFilterPhigros;
                case GameMode.Arcaea: return UiText.SongFilterArcaea;
                case GameMode.Cytus: return UiText.SongFilterCytus;
                case GameMode.OsuStandard: return UiText.SongFilterOsuStandard;
                case GameMode.Adofai:
                case GameMode.AdofaiReal: return UiText.SongFilterAdofai;
                case GameMode.Iidx: return UiText.SongFilterIidx;
                case GameMode.Maimai: return UiText.SongFilterMaimai;
                case GameMode.LoopComposer: return UiText.SongFilterLoopComposer;
                default: return mi.Display;
            }
        }

        /// <summary>当前过滤下的曲目（与列表/组头同源）。</summary>
        List<(Chart chart, string dir)> FilteredSongs()
        {
            var res = new List<(Chart, string)>();
            string want = SongFilterCycleOptions[Math.Max(0, Math.Min(SongFilterCycleOptions.Length - 1, _songFilterCycle))];
            string q = _songsSearchText != null ? _songsSearchText.Trim().ToLowerInvariant() : "";
            foreach (var (c, d) in _songItems)
            {
                if (c == null) continue;
                if (_songFilterCycle != 0 && ModeBucket(c.Mode) != want) continue;
                if (q.Length > 0)
                {
                    string t = (c.Title ?? "") + " " + (c.Artist ?? "");
                    if (t.ToLowerInvariant().IndexOf(q) < 0) continue;
                }
                res.Add((c, d));
            }
            return res;
        }

        /// <summary>曲目模式桶（legacy 选歌过滤语义：ADOFAI=adofai+adofai2；maimai/回环作曲仅全部模式可见）。</summary>
        static string ModeBucket(GameMode m)
        {
            switch (m)
            {
                case GameMode.Mania: return UiText.SongFilterMania;
                case GameMode.Phigros: return UiText.SongFilterPhigros;
                case GameMode.Arcaea: return UiText.SongFilterArcaea;
                case GameMode.Cytus: return UiText.SongFilterCytus;
                case GameMode.OsuStandard: return UiText.SongFilterOsuStandard;
                case GameMode.Adofai:
                case GameMode.AdofaiReal: return UiText.SongFilterAdofai;
                case GameMode.Iidx: return UiText.SongFilterIidx;
                default: return "";
            }
        }

        string SongFilterCurrentLabel()
            => SongFilterCycleOptions[Math.Max(0, Math.Min(SongFilterCycleOptions.Length - 1, _songFilterCycle))];

        void CycleSongFilter()
            => _songFilterCycle = (_songFilterCycle + 1) % SongFilterCycleOptions.Length;

        void RebuildSongList()
        {
            if (_songsGrid == null) return;
            foreach (var c in new List<UiElement>(_songsGrid.Children)) _songsGrid.Remove(c);
            _selCard = null; _selItem = (null, null);
            var shown = FilteredSongs();
            if (_songsSearchLabel != null) _songsSearchLabel.Text = _songsSearchText;
            if (_songsFilterBtn != null) _songsFilterBtn.Text = SongFilterCurrentLabel();
            if (_songsEmpty != null) { _songsEmpty.Visible = shown.Count == 0; if (_songsEmpty2 != null) _songsEmpty2.Visible = shown.Count == 0; }
            if (shown.Count == 0) return;
            foreach (var (c, d) in shown) AddSongCard(c, d);
        }

        string CurrentCategoryLabel()
        {
            int i = 0;
            foreach (var mi in ModeSystem.Available) { if (i == _songFilter) return SongCategoryLabel(mi); i++; }
            return UiText.SongFilterAll;
        }

        void AddSongCard(Chart c, string dir)
        {
            var card = new ClickableCard();
            card.Width = 284; card.Height = 140; card.CornerRadius = _theme.R3;
            card.Background = _theme.CardBg; card.HoverBg = _theme.CardHover; card.SelBg = _theme.CardSelected;
            card.Title = string.IsNullOrEmpty(c.Title) ? Path.GetFileNameWithoutExtension(c.SourcePath) : c.Title;
            card.TitleFont = UiMeasure.FitFont(card.Title, 349, _theme.FnTitle);   // t39/t40：谱名长名缩字号保全文（卡内可用=网格 377-24-4）
            var cardRef = card;
            // 曲目信息（格式参照 legacy 选歌原文：曲名 / 未知作者 / 难度 / 模式 / 键数）
            string artist = string.IsNullOrEmpty(c.Artist) ? UiText.SongUnknownArtist : c.Artist;
            string diff = string.IsNullOrEmpty(c.Version) ? UiText.SongDefaultDiff : c.Version;
            var line1 = new UiLabel(artist + " · " + diff, _theme.TextMuted, _theme.FnBody) { Width = 307, Height = 24, Align = UiAlign.Left };
            line1.AutoShrinkFont = true;   // t39/t40：作者/难度长名缩字号保全文（卡内可用 307，badge 让位）
            line1.X = 16; line1.Y = 46;
            card.AddChild(line1);
            string modeText = ModeSystem.DisplayName(c.Mode);
            string meta = (c.KeyCount > 0 ? c.KeyCount + UiText.SongK : UiText.SongDash) + " · " + modeText;
            var line2 = new UiLabel(meta, _theme.TextSecondary, _theme.FnBody) { Width = 307, Height = 24, Align = UiAlign.Left };
            line2.AutoShrinkFont = true;   // t39/t40：模式/键数行缩字号保全文
            line2.X = 16; line2.Y = 76;
            card.AddChild(line2);
            var badge = new UiLabel(ModeInitial(modeText), new RgbaColor(255, 255, 255, 255), 12) { Width = 34, Height = 24, Align = UiAlign.Center };
            badge.X = 331; badge.Y = 54;   // t40：badge 移右缘（331..365）——文本区 16..323 与 badge 无交叠，作者行全文不再被遮挡
            card.AddChild(badge);
            card.Clicked += () =>
            {
                _selCard = cardRef; _selItem = (c, dir);
                foreach (var ch in _songsGrid.Children)
                    if (ch is ClickableCard k) k.Selected = ReferenceEquals(k, cardRef);
            };
            card.DoubleClicked += () => { _selCard = cardRef; _selItem = (c, dir); TryPlay(c, dir); };
            _songsGrid.AddChild(card);
        }

        /// <summary>模式首字母徽章（legacy 选歌 ModeInfo 原文映射）。</summary>
        static string ModeInitial(string mode)
        {
            string m = (mode ?? "").ToLowerInvariant();
            if (m.Contains("phigros")) return "P";
            if (m.Contains("arcaea")) return "A";
            if (m.Contains("cytus")) return "C";
            if (m.Contains("maimai")) return "M";
            if (m.Contains("taiko")) return "T";
            if (m.Contains("catch")) return "C";
            if (m.Contains("adofai")) return "A";
            if (m.Contains("loopcompose")) return "L";
            if (m.Contains("iidx")) return "I";
            if (m.Contains("standard") || m.Contains("osu!")) return "O";
            if (m.Contains("mania")) return "M";
            return "M";
        }

        void TryPlay(Chart c, string dir)
        {
            try
            {
                if (_playChart != null) _playChart(c, dir);   // t50 修正：游玩内容同窗承载（HostContent）——窗保持可见（可见窗口数=1）；不再 Hide（旧两窗口切换遗留）
            }
            catch (Exception ex) { Logger.Error("引擎 UI 启动游玩失败", ex); }
        }

        void PlaySelectedSong()
        {
            if (_selItem.chart != null) { TryPlay(_selItem.chart, _selItem.dir); return; }
            var shown = FilteredSongs();
            if (shown.Count > 0) TryPlay(shown[0].chart, shown[0].dir);
        }

        void PlayRandomSong()
        {
            var shown = FilteredSongs();
            if (shown.Count == 0) return;
            var pick = shown[new Random().Next(shown.Count)];
            TryPlay(pick.chart, pick.dir);
        }

        void EditSelectedSong()
        {
            if (_editChart == null || _selItem.chart == null) return;
            try { if (_editChart != null) _editChart(_selItem.chart, _selItem.dir); }   // t50 修正：编辑器同窗承载——窗保持可见；不再 Hide
            catch (Exception ex) { Logger.Error("引擎 UI 编辑谱面失败", ex); }
        }

        /// <summary>取证/自检：各分类数量（label, count）——与列表/组头同源，sum(counts)==扫描总数。</summary>
        public List<(string label, int count)> SongCategoryCounts()
        {
            var res = new List<(string, int)>();
            res.Add((UiText.SongFilterAll, _songItems.Count));
            foreach (var mi in ModeSystem.Available)
            {
                int n = 0;
                foreach (var (c, _) in _songItems)
                    if (c != null && (c.Mode == mi.Mode || (c.Mode == GameMode.AdofaiReal && mi.Mode == GameMode.Adofai))) n++;
                res.Add((SongCategoryLabel(mi), n));
            }
            return res;
        }

        Scene BuildSettings()
        {
            var scene = new Scene();
            var canvas = NewCanvas(scene, "Settings");
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 800; bg.Padding = 0; bg.Background = new RgbaColor(0, 0, 0, 0); bg.CornerRadius = 0;

            var title = bg.AddChild<UiLabel>();
            title.Text = UiText.UiPageSettings; title.Color = _theme.TextPrimary; title.FontSize = _theme.FnH1;
            title.Width = 900; title.Height = 48; title.X = 190; title.Y = 80; title.Align = UiAlign.Left;

            var back = bg.AddChild<UiButton>();
            back.Text = UiText.UiBack; back.Width = 110; back.Height = 34; back.X = 190; back.Y = 138;
            back.CornerRadius = _theme.R2; back.Accent = false; back.FontSize = _theme.FnBody;
            back.Clicked += () => GoTo("menu", TransitionStyle.CircleReveal);

            // ===== t55：legacy 全宽页签（UiTabBar 同款：分区间隔线 + 选中高亮） =====
            // P0-1（t5 宿主侧兜底）：用 HostTabBar（按记录指针坐标算 idx → Select；t6 引擎侧 InvokeClick 根治后语义等价）
            var tabbar = bg.AddChild<HostTabBar>();
            tabbar.X = 190; tabbar.Y = 196; tabbar.Width = 900; tabbar.Height = 30;
            tabbar.Tabs = UiText.SettingsSectionNames;
            tabbar.ActiveIndex = _settingsTab;
            tabbar.FontSize = 13;
            tabbar.LastPointer = () => (_lastPointerX, _lastPointerY);
            tabbar.TabChanged += idx => { _settingsTab = idx; tabbar.ActiveIndex = idx; RebuildSettingsRows(); };
            _settingsTabBar = tabbar;

            // 行列表（每行=legacy 同序同标签；行控件写同一字段；对话框/文件/文本输入行桥接 legacy 页签）
            var rows = bg.AddChild<UiGridLayout>();
            rows.X = 190; rows.Y = 240; rows.Width = 900; rows.Height = 520;
            rows.Spacing = 10; rows.CellHeight = 42; rows.ClipChildren = true;   // t20 P1-2：列内裁剪防互压
            _settingsRows = rows;
            RebuildSettingsRows();
            return scene;
        }

        void RebuildSettingsTabs()
        {
            // t55：页签已由 UiTabBar 承载（BuildSettings 内建）；保留方法体为空以兼容旧调用
        }

        void RebuildSettingsRows()
        {
            if (_settingsRows == null) return;
            foreach (var c in new List<UiElement>(_settingsRows.Children)) _settingsRows.Remove(c);
            _settingsRows.Columns = (_settingsTab == 3 || _settingsTab == 6) ? 2 : 1;
            switch (_settingsTab)
            {
                case 0: BuildGameFormLegacy(); break;
                case 1: BuildJudgeSection(); break;
                case 2: BuildScoreSection(); break;
                case 3: BuildInterfaceSection(); break;
                case 4: BuildGradeSection(); break;
                case 5: BuildSoundSection(); break;
                case 6: BuildSkinSection(); break;
                case 7: BuildSaveSection(); break;
                default: BuildKeysSection(); break;
            }
        }

        /* ================= t48：设置行（标签=UiText 逐字；写入点与 SettingsPanel 同字段） ================= */

        GamePanel Game() => _gameProvider != null ? _gameProvider() : null;
        SkinSettings Skin() => Game()?.Skin;

        ClickableCard Row(string label, string value, Action click)
        {
            var c = new ClickableCard();
            bool twoCol = _settingsRows != null && _settingsRows.Columns == 2;   // t22：列模式感知——单列行卡加宽（1 列 890 / 2 列 440）
            c.Width = Math.Min(twoCol ? 440 : 890, (twoCol ? 445 : 900) - 8);   // t22b：钳制到网格单元格内（防跨列混排越界）
            c.Height = 40; c.CornerRadius = _theme.R2;
            c.Background = new RgbaColor(255, 255, 255, 12);
            c.HoverBg = new RgbaColor(255, 255, 255, 26);
            c.SelBg = new RgbaColor(255, 255, 255, 40);
            c.TopAccent = false;   // t53：去掉行顶 Accent 蓝线（曾切到标签上缘）
            c.ShowTitle = false;   // t20 P1-1/2：标题改子 UiLabel——左 40% 标签 + 右 60% 值两栏分离
            // t22b：标签单行 AutoShrinkFont（缩字号保全文，下限 9）——彻底弃用换行（用户复报重叠；单行路径无行叠加风险）
            double lw = twoCol ? 178 : 350;
            var l = new UiLabel(label != null ? label : "", _theme.TextPrimary, 11) { Width = lw, Height = 24 };
            l.X = 12; l.Y = 8; l.Align = UiAlign.Left; l.AutoShrinkFont = true;
            c.AddChild(l);
            var v = new UiLabel(value != null ? value : "", _theme.TextSecondary, _theme.FnBody) { Width = twoCol ? 244 : 510, Height = 24, Align = UiAlign.Right };
            v.X = twoCol ? 196 : 368; v.Y = 8; v.Align = UiAlign.Left; v.AutoShrinkFont = true;   // t22：值列随列模式定位（2 列 196 / 1 列 368）
            c.AddChild(v);
            c.Clicked += click;
            _settingsRows.AddChild(c);
            return c;
        }

        void RowBool(string label, Func<bool> get, Action<bool> set)
        {
            Row(label, "", () =>
            {
                set(!(get != null && get()));
                RebuildSettingsRows();
                Game()?.Invalidate();
            });
        }

        void RowNumber(string label, Func<double> get, Action<double> set, double min, double max, double step, string fmt)
        {
            Row(label, (get != null ? get() : 0).ToString(fmt), () =>
            {
                double nv = get != null ? get() : 0;
                nv = Math.Max(min, Math.Min(max, nv + step));
                set(nv);
                RebuildSettingsRows();
                Game()?.Invalidate();
            });
        }

        void RowChoice(string label, string[] options, Func<int> get, Action<int> set)
        {
            int cur = get != null ? get() : 0;
            cur = Math.Max(0, Math.Min(options.Length - 1, cur));
            Row(label, options[cur], () =>
            {
                set((cur + 1) % options.Length);
                RebuildSettingsRows();
                Game()?.Invalidate();
            });
        }

        void RowAction(string label, Action act) => Row(label, "", act);

        /// <summary>信息行：按行拆成独立单行标签（t24 P0-1——引擎 UiLabel 多行路径会把各行压进 24px 高行框互相叠绘，
        /// f23-05 评级页实锤；逐行独立渲染彻底规避多行路径）。t29：全文显示——缩字号保全文（下限 9），弃 Ellipsis。
        /// 其余信息行（判定/界面/存档/键位提示）同为单行短文本，走同路径渲染结果不变。</summary>
        void RowInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var c = new ClickableCard();
                bool twoCol = _settingsRows != null && _settingsRows.Columns == 2;
                c.Width = Math.Min(twoCol ? 440 : 890, (twoCol ? 445 : 900) - 8);
                c.Height = 40; c.CornerRadius = _theme.R2;
                c.Background = new RgbaColor(255, 255, 255, 12);
                c.HoverBg = new RgbaColor(255, 255, 255, 26);
                c.SelBg = new RgbaColor(255, 255, 255, 40);
                c.TopAccent = false;
                c.ShowTitle = false;
                c.Clicked += () => { };
                var l = new UiLabel(line, _theme.TextPrimary, _theme.FnBody) { Width = c.Width - 24, Height = 24 };
                l.X = 12; l.Y = 8; l.Align = UiAlign.Left; l.AutoShrinkFont = true;   // t29：全文——弃 Ellipsis，缩字号（下限 9）保全文
                c.AddChild(l);
                _settingsRows.AddChild(c);
            }
        }

        bool HudShow(string key) { var s = Skin(); return s != null && s.Layout.TryGetValue(key, out var p) && p.Show; }
        void SetHudShow(string key, bool show) { var s = Skin(); if (s != null && s.Layout.TryGetValue(key, out var p)) p.Show = show; Game()?.Invalidate(); }
        bool HudVisible()
        {
            var s = Skin();
            if (s == null || !s.Layout.TryGetValue("score", out var p)) return false;
            return p.Show;
        }

        /// <summary>t55：游戏分区 = legacy 同款表单（微调框/按钮/复选框/下拉循环），行高 42 不切标签。</summary>
        void BuildGameFormLegacy()
        {
            var speed = new UiNumberBox(UiText.SettingsGameSpeed, GameSettings.Speed, 0.4, 4, 0.05) { LabelLeft = true, LabelWidth = 112, Format = "0.00", Width = 300, Height = 30 };
            speed.ValueChanged += v => { GameSettings.Speed = v; Game()?.Invalidate(); };
            var offset = new UiNumberBox(UiText.SettingsGameDelayMs, GameSettings.Offset, -300, 300, 1) { LabelLeft = true, LabelWidth = 112, Width = 300, Height = 30 };
            offset.ValueChanged += v => { GameSettings.Offset = v; };
            var vol = new UiNumberBox(UiText.SettingsGameVolume, GameSettings.Volume, 0, 100, 5) { LabelLeft = true, LabelWidth = 112, Width = 300, Height = 30 };
            vol.ValueChanged += v => { GameSettings.Volume = (int)v; Game()?.ApplyVolume(); };

            foreach (var nb in new[] { speed, offset, vol })
            {
                _settingsRows.AddChild(nb);
                AddArrows(nb);
            }

            // ［🎯 自动调整延迟］按钮（legacy 同款按钮）
            var calib = new UiButton { Text = UiText.SettingsGameAutoDelay, Accent = false, CornerRadius = 4, FontSize = 12, Width = 168, Height = 30 };
            calib.Clicked += () => InvokeAction(UiText.MenuCalibration);
            _settingsRows.AddChild(calib);

            // 键位(列数)：行内绿色值（legacy 同款）
            var kc = new UiPanel { Width = 300, Height = 30, CornerRadius = 0 };
            var kcText = new UiLabel(UiText.SettingsGameKeyCount + "：" + (Game() != null && Game().IsLoaded ? Game().KeyCount + UiText.SongK : UiText.SettingsKeyCountLabel), new RgbaColor(120, 220, 140), 13) { Width = 300, Height = 26, X = 0, Y = 2, Align = UiAlign.Left };
            kc.AddChild(kcText);
            _settingsRows.AddChild(kc);

            // 判定基准：legacy 下拉 → 循环切换（默认=GameSettings.JudgeBase）
            var judgeOptions = new[] { UiText.SettingsGameJudgeBaseBottom, UiText.SettingsGameJudgeBaseCenter, UiText.SettingsGameJudgeBaseTop };
            int jb = Math.Max(0, Math.Min(2, GameSettings.JudgeBase));
            var judge = new UiButton { Text = UiText.SettingsGameJudgeBase + "：" + judgeOptions[jb], Accent = false, CornerRadius = 4, FontSize = 12, Width = 300, Height = 30 };
            judge.Clicked += () =>
            {
                GameSettings.JudgeBase = (GameSettings.JudgeBase + 1) % 3;
                judge.Text = UiText.SettingsGameJudgeBase + "：" + judgeOptions[GameSettings.JudgeBase];
            };
            _settingsRows.AddChild(judge);

            // ［□ 自动］复选框（legacy 同名）
            var auto = new UiCheckBox(UiText.SettingsGameAutoplay, GameSettings.Autoplay) { Width = 200, Height = 26 };
            auto.CheckedChanged += v => GameSettings.Autoplay = v;
            _settingsRows.AddChild(auto);
        }

        /// <summary>微调框 ▲▼ 击点叠层（透明可点区，落在 UiNumberBox 箭头位置）。
        /// P0-2（t5）：叠层不再用固定 X=272 —— UiGridLayout 会把 UiNumberBox 拉伸为单元格宽，
        /// 箭头实际绘制在右端（bx=Width-cell*2-2），旧叠层仍停在 272 → 命中区错位 590px。
        /// 现用 ArrowCard：命中时按父框实际宽实时计算箭头区（与 UiNumberBox.DrawSelf 同公式），任意列宽下均贴合。</summary>
        void AddArrows(UiNumberBox nb)
        {
            var up = new ArrowCard { Box = nb, Down = false };
            up.Title = ""; up.Background = new RgbaColor(255, 255, 255, 0); up.TopAccent = false; up.BorderThickness = 0;
            up.Clicked += () => nb.Bump(1);
            up.DoubleClicked += () => nb.Bump(1);
            if (nb != null) nb.AddChild(up);
            var dn = new ArrowCard { Box = nb, Down = true };
            dn.Title = ""; dn.Background = new RgbaColor(255, 255, 255, 0); dn.TopAccent = false; dn.BorderThickness = 0;
            dn.Clicked += () => nb.Bump(-1);
            dn.DoubleClicked += () => nb.Bump(-1);
            if (nb != null) nb.AddChild(dn);
        }

        void BuildGameSection()
        {
            RowNumber(UiText.SettingsGameSpeed, () => GameSettings.Speed, v => GameSettings.Speed = v, 0.4, 4, 0.05, "0.00");
            RowNumber(UiText.SettingsGameDelayMs, () => GameSettings.Offset, v => GameSettings.Offset = v, -300, 300, 1, "0");
            RowNumber(UiText.SettingsGameVolume, () => GameSettings.Volume, v => { GameSettings.Volume = (int)v; Game()?.ApplyVolume(); }, 0, 100, 5, "0");
            RowAction(UiText.SettingsGameAutoDelay, () => InvokeAction(UiText.MenuCalibration));
            RowInfo(UiText.SettingsGameKeyCount + "：" + (Game() != null && Game().IsLoaded ? Game().KeyCount + UiText.SongK : UiText.SettingsKeyCountLabel));
            RowChoice(UiText.SettingsGameJudgeBase,
                new[] { UiText.SettingsGameJudgeBaseBottom, UiText.SettingsGameJudgeBaseCenter, UiText.SettingsGameJudgeBaseTop },
                () => GameSettings.JudgeBase, v => GameSettings.JudgeBase = v);
            RowBool(UiText.SettingsGameAutoplay, () => GameSettings.Autoplay, v => GameSettings.Autoplay = v);
        }

        void BuildJudgeSection()
        {
            var keys = new List<string>();
            var names = new List<string>();
            foreach (var kv in JudgeSettings.Presets)
                if (kv.Key != null && !string.IsNullOrEmpty(kv.Value.display)) { keys.Add(kv.Key); names.Add(kv.Value.display); }
            if (_judgePresetKey == null || !keys.Contains(_judgePresetKey)) _judgePresetKey = keys.Count > 0 ? keys[keys.Count - 1] : null;
            RowChoice(UiText.SettingsJudgePreset, names.ToArray(), () => _judgePresetKey != null ? keys.IndexOf(_judgePresetKey) : 0, v =>
            {
                if (keys.Count > 0) { _judgePresetKey = keys[Math.Max(0, Math.Min(keys.Count - 1, v))]; JudgeSettings.ApplyPreset(_judgePresetKey); }
            });
            Row(UiText.SettingsJudgeApplyPreset, _judgePresetKey ?? UiText.SongPlaceholderDash, () =>
            {
                if (_judgePresetKey != null) JudgeSettings.ApplyPreset(_judgePresetKey);
            });
            RowInfo(UiText.SettingsJudgeHint);
            RowAction(UiText.SettingsJudgeSavePreset, () => InvokeAction(UiText.SettingsSectionNames[1]));
            RowAction(UiText.SettingsJudgeImportPreset, () => InvokeAction(UiText.SettingsSectionNames[1]));
        }

        void BuildScoreSection()
        {
            RowNumber(UiText.SettingsScoreComboCap, () => JudgeSettings.ComboBonusMax, v => JudgeSettings.ComboBonusMax = (int)v, 10, 200, 10, "0");
        }

        void BuildInterfaceSection()
        {
            RowChoice(UiText.SettingsUiQuality,
                new[] { UiText.SettingsUiQualityLow, UiText.SettingsUiQualityMid, UiText.SettingsUiQualityHigh, UiText.SettingsUiQualityAuto },
                () => GraphicsQuality.Preset, v => { GraphicsQuality.ApplyPreset(v); });
            RowChoice(UiText.SettingsUiRefreshRate,
                FpsGovernor.RateNames, () => Math.Max(0, Array.IndexOf(FpsGovernor.Rates, GameSettings.RefreshRate)),
                v => { int rate = FpsGovernor.Rates[Math.Max(0, Math.Min(FpsGovernor.Rates.Length - 1, v))]; FpsGovernor.Apply(rate); });
            RowBool(UiText.SettingsUiRightPanel, () => Game() != null && Game().ShowRightPanel, v => { if (Game() != null) Game().ShowRightPanel = v; });
            RowBool(UiText.SettingsUiOsuField, () => GameSettings.OsuStdPlayfield, v => GameSettings.OsuStdPlayfield = v);
            RowBool(UiText.SettingsUiFps, () => GameSettings.ShowFps, v => GameSettings.ShowFps = v);
            RowBool(UiText.SettingsUiShowArt, () => Skin() != null && Skin().ShowBackground, v => { if (Skin() != null) Skin().ShowBackground = v; });
            RowBool(UiText.SettingsUiMinimal, () => false, v =>
            {
                var g = Game();
                if (g == null || g.Skin == null) return;
                foreach (var k in new[] { "title", "score", "acc", "bpm", "kps", "notes", "combo", "judge", "dev" })
                    if (g.Skin.Layout.TryGetValue(k, out var p)) p.Show = !v && p.Show;
            });
            RowBool(UiText.SettingsUiFullscreen, () => Owner != null && Owner.FormBorderStyle == FormBorderStyle.None, v =>
            {
                var f = Owner;
                if (f == null) return;
                f.FormBorderStyle = v ? FormBorderStyle.None : FormBorderStyle.Sizable;
                f.WindowState = FormWindowState.Normal;
                f.WindowState = FormWindowState.Maximized;
            });
            RowBool(UiText.SettingsUiBorderless, () => Owner != null && Owner.FormBorderStyle == FormBorderStyle.None, v =>
            {
                var f = Owner;
                if (f == null) return;
                if (v) f.FormBorderStyle = FormBorderStyle.None;
                else f.FormBorderStyle = FormBorderStyle.Sizable;
            });
            RowBool(UiText.SettingsUiHud, () => HudVisible(), v =>
            {
                var g = Game();
                if (g == null || g.Skin == null) return;
                foreach (var k in new[] { "title", "score", "acc", "bpm", "kps", "notes", "combo", "judge", "dev" })
                    if (g.Skin.Layout.TryGetValue(k, out var p)) p.Show = v;
            });
            RowBool(UiText.SettingsUiJudgeText, () => HudShow("judge"), v => SetHudShow("judge", v));
            RowBool(UiText.SettingsUiDevText, () => HudShow("dev"), v => SetHudShow("dev", v));
            RowBool(UiText.SettingsUiAcc, () => Skin() != null && Skin().ShowAcc, v => { if (Skin() != null) Skin().ShowAcc = v; });
            RowBool(UiText.SettingsUiScore, () => Skin() != null && Skin().ShowScore, v => { if (Skin() != null) Skin().ShowScore = v; });
            RowBool(UiText.SettingsUiCombo, () => Skin() != null && Skin().ShowCombo, v => { if (Skin() != null) Skin().ShowCombo = v; });
            RowBool(UiText.SettingsUiBurst, () => Skin() != null && Skin().BurstEffects, v => { if (Skin() != null) Skin().BurstEffects = v; });
            RowBool(UiText.SettingsUiShake, () => Skin() != null && Skin().ScreenShake, v => { if (Skin() != null) Skin().ScreenShake = v; });
            RowBool(UiText.SettingsUiSlant, () => GameSettings.SlantEnabled, v => GameSettings.SlantEnabled = v);
            RowBool(UiText.SettingsUiCamera3D, () => GameSettings.Camera3D, v => GameSettings.Camera3D = v);
            RowNumber(UiText.SettingsUiPitch, () => GameSettings.CameraPitch, v => GameSettings.CameraPitch = v, 0, 60, 2, "0");
            RowNumber(UiText.SettingsUiYaw, () => GameSettings.CameraYaw, v => GameSettings.CameraYaw = v, -30, 30, 2, "0");
            RowNumber(UiText.SettingsUiDepth, () => GameSettings.CameraDepth, v => GameSettings.CameraDepth = v, 0.4, 2.5, 0.1, "0.0");
            RowBool(UiText.SettingsUiHitFx, () => GameSettings.ShowHitFx, v => GameSettings.ShowHitFx = v);
            RowBool(UiText.SettingsUiResult, () => GameSettings.ResultScreenEnabled, v => GameSettings.ResultScreenEnabled = v);
            RowChoice(UiText.SettingsUiTheme,
                new[] { UiText.SettingsUiThemeBlue, UiText.SettingsUiThemePurple, UiText.SettingsUiThemeCyan },
                () => GameSettings.UiTheme, v => { GameSettings.UiTheme = v; UiColors.ApplyTheme(v); });
            RowChoice(UiText.SettingsUiTransition,
                new[] { UiText.SettingsUiTransitionRandom, UiText.SettingsUiTransitionBeam, UiText.SettingsUiTransitionCircle, UiText.SettingsUiTransitionSlide },
                () => GameSettings.TransitionStyle + 1, v => GameSettings.TransitionStyle = v - 1);
            // t61：GPU/CPU 调度可直接在引擎中调整（即改即生效：渲染目标重建 / EngineJobs.Degree）
            RowChoice(UiText.SettingsUiRenderBackend,
                new[] { UiText.SettingsUiRenderHardware, UiText.SettingsUiRenderWarp },
                () => GameSettings.ForceWarp ? 1 : 0,
                v =>
                {
                    GameSettings.ForceWarp = v == 1;
                    try { Game()?.RestartRenderer(); } catch { }
                });
            RowChoice(UiText.SettingsUiCpuDegree,
                new[] { UiText.SettingsUiCpuAuto, "1", "2", "4", "8", "16" },
                () =>
                {
                    int[] opts = { 0, 1, 2, 4, 8, 16 };
                    int d = EngineJobs.Degree;
                    for (int i = opts.Length - 1; i >= 0; i--) if (d >= opts[i]) return i;
                    return 0;
                },
                v =>
                {
                    int[] opts = { 0, 1, 2, 4, 8, 16 };
                    int sel = opts[Math.Max(0, Math.Min(opts.Length - 1, v))];
                    EngineJobs.Degree = sel == 0 ? Environment.ProcessorCount : sel;
                    try { Game()?.Invalidate(); } catch { }
                });
            RowInfo(UiText.SettingsUiQualityHint);
            RowInfo(UiText.SettingsUiRefreshRateHint);
            RowInfo(UiText.SettingsUiCameraHint);
        }

        void BuildGradeSection()
        {
            RowInfo(UiText.SettingsGradeInfo);
        }

        void BuildSoundSection()
        {
            RowChoice(UiText.SettingsSoundStyle,
                new[] { UiText.SettingsSoundClassic, UiText.SettingsSoundElectronic, UiText.SettingsSoundWood },
                () => GameSettings.HitsoundStyle, v => { GameSettings.HitsoundStyle = v; SoundFx.Style = v; });
            RowBool(UiText.SettingsSoundPerJudge, () => GameSettings.HitsoundPerJudge, v => GameSettings.HitsoundPerJudge = v);
            RowAction(UiText.SettingsSoundPreviewJudge, () => SoundFx.Preview(0));
            RowAction(UiText.SettingsSoundPreviewMiss, () => SoundFx.Preview(-1));
            RowBool(UiText.SettingsSoundEnable, () => Skin() != null && Skin().SoundEffects, v => { if (Skin() != null) Skin().SoundEffects = v; SoundFx.Enabled = v; });
            RowNumber(UiText.SettingsSoundVolume, () => SoundFx.Volume, v => { SoundFx.Volume = (int)v; GameSettings.HitsoundVolume = (int)v; SoundFx.Enabled = v > 0; }, 0, 100, 5, "0");
        }

        void BuildSkinSection()
        {
            RowAction(UiText.SettingsSkinEditLayout, () => InvokeAction(UiText.SettingsSectionNames[6]));
            RowNumber(UiText.SettingsSkinHitLineY,
                () => Skin() != null && Skin().Layout.TryGetValue("hitline", out var hp0) ? hp0.Y : 0.84,
                v => { if (Skin() != null && Skin().Layout.TryGetValue("hitline", out var hp)) hp.Y = Math.Max(0.05, Math.Min(0.95, v)); }, 0.5, 0.95, 0.01, "0.00");
            RowNumber(UiText.SettingsSkinSlant, () => Skin() != null ? Skin().Slant : 0, v => { if (Skin() != null) Skin().Slant = v; }, 0, 1, 0.05, "0.00");
            RowNumber(UiText.SettingsSkinPlayScale, () => Game() != null ? Game().PlayScale : 1, v => { if (Game() != null) Game().PlayScale = v; }, 0.25, 1.6, 0.05, "0.00");
            RowNumber(UiText.SettingsSkinNoteThick, () => Game() != null ? Game().NoteThickness : 40, v => { if (Game() != null) Game().NoteThickness = v; }, 8, 120, 4, "0");
            RowNumber(UiText.SettingsSkinJudgeFont, () => Skin() != null ? Skin().JudgeFont : 24, v => { if (Skin() != null) Skin().JudgeFont = (int)v; }, 12, 44, 2, "0");
            RowChoice(UiText.SettingsSkinJudgePos,
                new[] { UiText.SettingsSkinJudgePosOnLanes, UiText.SettingsSkinJudgePosLine, UiText.SettingsSkinJudgePosTop, UiText.SettingsSkinJudgePosHidden },
                () => Skin() != null ? Skin().JudgePosMode : 0, v => { if (Skin() != null) Skin().JudgePosMode = v; });
            RowChoice(UiText.SettingsSkinDevPos,
                new[] { UiText.SettingsSkinDevPosPanel, UiText.SettingsSkinJudgePosLine, UiText.SettingsSkinJudgePosTop, UiText.SettingsSkinJudgePosHidden },
                () => Skin() != null ? Skin().DevPosMode : 0, v => { if (Skin() != null) Skin().DevPosMode = v; });
            RowNumber(UiText.SettingsSkinBgDim, () => Skin() != null ? Skin().BgDim : 0.6, v => { if (Skin() != null) Skin().BgDim = v; }, 0, 1, 0.05, "0.00");
            RowChoice(UiText.SettingsSkinPreset,
                new[] { UiText.SettingsSkinPresetDefault, UiText.SettingsSkinPresetNeon, UiText.SettingsSkinPresetCandy, UiText.SettingsSkinPresetMono, UiText.SettingsSkinPresetBlueBlock },
                () => 0, v => ApplySkinPreset(v));
            RowBool(UiText.SettingsSkinUseLane, () => Skin() != null && Skin().UseLaneColorForNote, v => { if (Skin() != null) Skin().UseLaneColorForNote = v; });
            RowNumber(UiText.SettingsSkinHitLineThick, () => Skin() != null ? Skin().HitLineThickness : 2, v => { if (Skin() != null) Skin().HitLineThickness = (int)v; }, 1, 10, 1, "0");
            RowChoice(UiText.SettingsSkinHitLineStyle,
                new[] { UiText.SettingsSkinLineSolid, UiText.SettingsSkinLineDashed, UiText.SettingsSkinLineDotted },
                () => Skin() != null ? Skin().HitLineStyle : 0, v => { if (Skin() != null) Skin().HitLineStyle = v; });
            RowNumber(UiText.SettingsSkinHitLineGlow, () => Skin() != null ? Skin().HitLineGlow : 0, v => { if (Skin() != null) Skin().HitLineGlow = (int)v; }, 0, 30, 2, "0");
            RowNumber(UiText.SettingsSkinHoldAlpha, () => Skin() != null ? Skin().HoldAlpha : 0.4, v => { if (Skin() != null) Skin().HoldAlpha = v; }, 0.1, 1, 0.05, "0.00");
            RowChoice(UiText.SettingsSkinHoldStyle,
                new[] { UiText.SettingsSkinHoldSolid, UiText.SettingsSkinHoldGlow, UiText.SettingsSkinHoldOutline },
                () => Skin() != null ? Skin().HoldStyle : 0, v => { if (Skin() != null) Skin().HoldStyle = v; });
            RowAction(UiText.SettingsSkinRandomColors, () => { if (Skin() != null) { Skin().RandomizeLanes(); Game()?.Invalidate(); } });
            RowAction(UiText.SettingsSkinApplySave, () => InvokeAction(UiText.SettingsSectionNames[6]));
            RowAction(UiText.SettingsSkinExportJson, () => InvokeAction(UiText.SettingsSectionNames[6]));
            RowAction(UiText.SettingsSkinImportJson, () => InvokeAction(UiText.SettingsSectionNames[6]));
            RowAction(UiText.SettingsSkinReset, () => InvokeAction(UiText.SettingsSectionNames[6]));
        }

        void BuildSaveSection()
        {
            var p = PlayerData.LoadProfile();
            RowInfo(UiText.SettingsSavePlayerName + "：" + (p != null ? p.Name : ""));
            RowAction(UiText.SettingsSavePlayer, () => InvokeAction(UiText.SettingsSectionNames[7]));
            RowAction(UiText.SettingsSaveExportData, () => InvokeAction(UiText.SettingsSectionNames[7]));
            RowAction(UiText.SettingsSaveImportData, () => InvokeAction(UiText.SettingsSectionNames[7]));
            RowInfo(UiText.SettingsSaveStaticHint);
        }

        void BuildKeysSection()
        {
            // t33（r9 残留）：键位提示拆两行独立 Label（正文一行、括号内容一行）——防行内穿插压叠
            string keysHint = UiText.SettingsKeysHint;
            int paren = keysHint != null ? keysHint.IndexOf('（') : -1;
            RowInfo(paren > 0 ? keysHint.Substring(0, paren) : keysHint);
            if (paren > 0) RowInfo(keysHint.Substring(paren));
            RowAction(UiText.SettingsKeysApply, () => InvokeAction(UiText.SettingsSectionNames[8]));
        }

        void ApplySkinPreset(int idx)
        {
            var skin = Skin();
            if (skin == null) return;
            switch (idx)
            {
                case 1:
                    skin.LaneColors = MakeLanes(0, 255, 170, 255, 0, 170, 255, 240, 0, 0, 200, 255, 170, 0, 255, 255, 90, 90, 90, 255, 120, 255, 140, 0);
                    skin.BgColor = System.Drawing.Color.FromArgb(8, 6, 22);
                    break;
                case 2:
                    skin.LaneColors = MakeLanes(255, 150, 200, 255, 200, 120, 255, 240, 150, 150, 255, 200, 150, 210, 255, 220, 180, 255, 255, 170, 220, 180, 255, 220);
                    skin.BgColor = System.Drawing.Color.FromArgb(26, 14, 26);
                    break;
                case 3:
                    skin.LaneColors = MakeLanes(255, 255, 255, 200, 200, 200, 160, 160, 160, 120, 120, 120, 255, 255, 255, 200, 200, 200, 160, 160, 160, 120, 120, 120);
                    skin.BgColor = System.Drawing.Color.FromArgb(6, 6, 8);
                    break;
                case 4:
                    skin.LaneColors = MakeLanes(255, 255, 255, 120, 190, 255, 255, 255, 255, 120, 190, 255);
                    skin.BgColor = System.Drawing.Color.FromArgb(10, 14, 26);
                    break;
            }
            skin.NoteColors = null;
            Game()?.Invalidate();
            RebuildSettingsRows();
        }

        static List<System.Drawing.Color> MakeLanes(params int[] rgb)
        {
            var list = new List<System.Drawing.Color>();
            for (int i = 0; i + 2 < rgb.Length; i += 3)
                list.Add(System.Drawing.Color.FromArgb(rgb[i], rgb[i + 1], rgb[i + 2]));
            return list;
        }

        UiPanel Swatch(string name, RgbaColor accent)
        {
            var p = new UiPanel { Width = 284, Height = 64, CornerRadius = _theme.R3, Background = _theme.CardBg };
            var l = new UiLabel { Text = name, Color = _theme.TextPrimary, FontSize = _theme.FnTitle, Width = 250, Height = 26, X = 14, Y = 8, Align = UiAlign.Left };
            p.AddChild(l);
            var line = new UiLabel { Text = UiText.UiSwatchGlass, Color = accent, FontSize = _theme.FnBody, Width = 250, Height = 20, X = 14, Y = 34, Align = UiAlign.Left };
            p.AddChild(line);
            return p;
        }

        IEnumerable<(Chart, string)> EnumerateCharts()
        {
            // t39（t38 P1）：与曲库管理页同源——用 AppConfig.ChartsFolder（默认=BaseDirectory\Chart；
            // 曲库页切换目录后配置已 Save，此处实时读配置即与曲库页一致，消除独立空源）
            var cfg = AppConfig.Load();
            string baseChart = string.IsNullOrEmpty(cfg.ChartsFolder) ? AppConfig.DefaultChartsFolder : cfg.ChartsFolder;
            foreach (var item in ScanChartsDir(baseChart, 200))
                yield return item;
        }

        /// <summary>t39：曲库目录变更（FolderChanged）后使选歌扫描缓存失效并即时重扫——与曲库管理页同源同步。</summary>
        void InvalidateSongsScan()
        {
            _songsScanDone = false;
            _songItems.Clear();
            if (_pages.TryGetValue("songs", out var scene) && scene != null)
            {
                foreach (var (c, d) in EnumerateCharts()) _songItems.Add((c, d));
                _songsScanDone = true;
                RebuildSongList();
            }
        }

        /// <summary>t10（G5 游戏层修复）：曲库目录扫描 + 并行解析（ChartParserExtra.ParseFilesParallel，
        /// EngineJobs.Paused 时自动降级串行）。上限从 40 提升到 200（需求：200 谱 ≤8s 启动扫描）。
        /// 供引擎壳启动扫描与 --gamestress startupscan 共用同一代码路径。</summary>
        public static List<(Chart Chart, string Dir)> ScanChartsDir(string dir, int cap = 200)
        {
            var list = new List<(Chart, string)>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories); } catch { return list; }
            var targets = new List<string>(cap);
            foreach (var f in files)
            {
                if (targets.Count >= cap) break;
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(ChartParser.ChartExts, ext) >= 0) targets.Add(f);
            }
            if (targets.Count == 0) return list;
            var charts = ChartParser.ParseFilesParallel(targets);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count && i < charts.Length; i++)
            {
                if (charts[i] == null || !seen.Add(targets[i])) continue;
                list.Add((charts[i], Path.GetDirectoryName(targets[i])));
            }
            return list;
        }

        /// <summary>t10 压测入口：外部驱动页面跳转（与 GoTo 同路径）。</summary>
        public void GoToPublic(string key) => GoTo(key, TransitionStyle.CircleReveal);

        /// <summary>t10 压测入口：当前活动页面 key。</summary>
        public string CurrentPageKey
        {
            get
            {
                foreach (var kv in _pages)
                    if (_canvases.TryGetValue(kv.Value, out var c) && ReferenceEquals(c, _active)) return kv.Key;
                return "menu";
            }
        }

        void GoTo(string key, TransitionStyle style)
        {
            if (!_pages.TryGetValue(key, out var next)) return;
            if (ReferenceEquals(_active, _canvases[next])) return;
            _prev = _active;
            _next = _canvases[next];
            _next.X = 0; _next.Y = 0;
            _trans = SceneCompositor.Create(style, 380);
            _sm.LoadScene(next, _trans);
            // t37：转场合成用缓存帧——next 优先取空闲预热帧（GoTo 零等待），缺失才同步捕获；
            // prev 同步捕获当前态（含按压/悬停反馈）；转场帧只做位图 Blit+压暗合成（快 ~10×，掉帧不再放大转场抖动）
            _contentDirty = true;
            if (_pageFrames.TryGetValue(next, out var warmed)) { _nextBmp = warmed; _pageFrames.Remove(next); }   // 取用即移出（Reopen 时统一释放）
            else { CaptureCanvasFrame(_next, ref _nextBmp); }
            CaptureCanvasFrame(_prev, ref _prevBmp);
            Invalidate();
        }

        /// <summary>把指定页面渲染到任意 IUiDraw（无窗口/软件渲染/截图证据用）。
        /// t37：画布 RenderCaching 开启后，离屏取证路径每次调用前强制标脏（否则重复渲染会被缓存跳过成空帧）。</summary>
        public bool RenderPageTo(string key, IUiDraw draw, int w, int h)
        {
            if (!_pages.TryGetValue(key, out var scene) || !_canvases.TryGetValue(scene, out var c)) return false;
            c.Width = w; c.Height = h;
            c.Invalidate();
            c.Render(draw);
            return true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { DarkMode.Enable(Handle); } catch { }   // 深色标题栏（Windows 浅色主题下标题栏为白条——用户反馈“白色方块”根因）
            // t79 P1：多显示器/DPI 会话下启动窗口可能落屏外（-32000 最小化标记，试玩 t80 取证 237×39）——
            // 统一在句柄创建后还原：Normal + 工作区内居中复位，杜绝窗口管理回归。
            try { WindowState = FormWindowState.Normal; } catch { }
            try {
                var wa = Screen.FromHandle(Handle).WorkingArea;
                int w = Math.Max(960, Math.Min(wa.Width, ClientSize.Width));
                int h = Math.Max(640, Math.Min(wa.Height, ClientSize.Height));
                this.Size = new Size(w, h);
                this.Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
            } catch { }
            var pc = PhysClient(); _w = pc.W; _h = pc.H;
            try { _d2d = new D2DRenderer(Handle, _w, _h); _adapter = new D2DDrawAdapter(_d2d); if (_active != null) { _active.Width = _w; _active.Height = _h; } }
            catch (Exception ex) { Logger.Error("引擎主界面渲染器初始化失败", ex); }
        }

        // t59：DPI 虚拟化修复——逻辑 ClientSize(1280x800) 与物理客户区(实测 853x533)不一致时，
        // 若按逻辑尺寸算 scale=1.0 → 内容按 1:1 画到物理客户区被右/下裁剪（用户反馈“放大后无法正确定位”根因）。
        // 统一用物理客户区(GetClientRect)计算 letterbox 缩放/居中，任意 DPI/窗口尺寸内容居中不裁切。
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out NativeRect rc);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct NativeRect { public int L, T, R, B; }
        /// <summary>客户区像素 → 虚拟画布坐标（letterbox 逆变换；全屏/任意尺寸命中测试不漂移）。</summary>
        (double x, double y) ToVirtual(double fx, double fy)
        {
            var pc = PhysClient();
            double w = pc.W, h = pc.H;
            double scale = Math.Min(w / 1280.0, h / 800.0);
            if (scale <= 0) return (fx, fy);
            double tx = (w - 1280.0 * scale) / 2.0, ty = (h - 800.0 * scale) / 2.0;
            return ((fx - tx) / scale, (fy - ty) / scale);
        }

        (int W, int H) PhysClient()
        {
            if (Handle == IntPtr.Zero) return (Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            try { GetClientRect(Handle, out var rc); int w = rc.R - rc.L, h = rc.B - rc.T; if (w > 0 && h > 0) return (w, h); } catch { }
            return (Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        }

        /// <summary>P0-4（t6 引擎落地后接线）+ t79 分辨率修复：启动客户区 = 主屏工作区全尺寸（对齐屏幕分辨率；
        /// t5 基线“默认启动自动对齐屏幕”——窗口贴满工作区，letterbox 按物理工作区原生 1:1 渲染（无 0.92 缩小、
        /// 无 upscale 发虚；PerMonitorV2 下工作区=物理 DIP）。异常/过小屏回退 1280×800。</summary>
        static Size StartupClientSize()
        {
            try
            {
                var wa = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea : Rectangle.Empty;
                if (wa.Width >= 1024 && wa.Height >= 640)
                    return new Size(wa.Width, wa.Height);
            }
            catch { }
            return new Size(1280, 800);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        // 渲染修复（用户反馈“按键全部白色”）：D2D 实显路径在部分 DPI/会话下把 UI 填色渲染为白色；
        // 引擎壳改用 GDI（软件）渲染 GdiDrawAdapter→Bitmap（与 --shellshot 取证同路径，已被证明逐色正确），
        // 渲染确定性、无显存/驱动依赖；按钮/卡片/文字颜色与设计一致。
        // t27 高分辨率渲染（用户反馈“分辨率低＋还有重叠”）：取消固定 1280×800 位图 + DrawImage 双三次拉伸，
        // 改为物理客户区原生尺寸位图（尺寸变化才重建；BGRA 常驻复用）+ 位图内 Translate/Scale 矢量变换——
        // 组件/字体/坐标仍为 1280×800 逻辑值，文字按字形轮廓在目标尺寸直接光栅化（清晰锐利，非位图放大发虚）；
        // 位图 1:1 贴窗（无二次缩放/无双三次），letterbox 边带以背景色填充；_w/_h 保持物理值（Dim/转场/裁剪沿用）。
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                var phys = PhysClient();
                int w = phys.W, h = phys.H;   // 物理客户区（DPI 虚拟化下与逻辑 ClientSize 不同！）
                if (_lastPaintW != w || _lastPaintH != h) { _lastPaintW = w; _lastPaintH = h; _contentDirty = true; try { Logger.Info("OnPaint paint: physClient=" + w + "x" + h + " letterboxScale=" + (Math.Min((double)w / 1280.0, (double)h / 800.0)).ToString("0.000")); } catch { } }
                // t27：位图/Graphics 按物理客户区原生尺寸常驻（尺寸变化才重建；Graphics 跨帧复用——
                // 同时根治旧代码每帧 using 释放后 TopBarHit/命中路径 MeasureText 触碰已释放 Graphics 的隐患）
                if (_gdiBmp == null || _gdiBmp.Width != w || _gdiBmp.Height != h)
                {
                    try { _adapterG?.Dispose(); } catch { }
                    try { _gdiBmp?.Dispose(); } catch { }
                    _gdiBmp = DpiBitmap.Create(w, h);   // t78：固定 96 DPI——GDI+ Point 字号按 96 换算，文本以逻辑尺寸进 letterbox（高 DPI 屏被 1.5× 放大裁切根治）
                    _adapterG = Graphics.FromImage(_gdiBmp);
                    _adapterG.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    _adapterG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    _contentDirty = true;   // t37：帧缓冲重建 → 强制重绘
                }
                double scale = Math.Min((double)w / 1280.0, (double)h / 800.0);
                if (scale <= 0) scale = 1;
                double tx = (w - 1280 * scale) / 2.0, ty = (h - 800 * scale) / 2.0;
                _ltx = tx; _lty = ty; _lscale = scale;   // t27：Dim/转场裁剪共用本帧 letterbox 变换
                UiGlyphRuns.EmojiGapBoost = scale < 1 ? 2 : 0;   // t34：800 极窄档（scale<1）emoji run 推进 +2px 保底（gap≥6）；宽档零变化
                var g = _adapterG;
                bool transActive = _trans != null;
                // t37：按需重绘——静止帧跳过全部内容绘制（背景/页面/顶栏），只做 1:1 贴窗 Blit（CPU 近零）；
                // 转场期间（transActive）保持逐帧（缓存帧合成，见 PaintContent）。
                if (_contentDirty || transActive)
                {
                    g.ResetTransform();   // 常驻 Graphics：每帧清零累积变换
                    g.Clear(Color.FromArgb(_theme.Bg.R, _theme.Bg.G, _theme.Bg.B));   // letterbox 边带=背景色（t27）
                    DrawBackdropCached(g);   // t37：背景层离屏缓存（渐变/光晕/星点）每帧 Blit；尺寸变化才重建
                    g.TranslateTransform((float)tx, (float)ty);
                    g.ScaleTransform((float)scale, (float)scale);
                    _gdiAdapter = new GdiDrawAdapter(g);
                    PaintContent();
                    DrawTopBar();   // t13：F1 顶部菜单条叠加（画布坐标，随 letterbox 缩放）
                    if (!transActive) _contentDirty = false;
                }
                // t86：DPI-精确 1:1 贴窗——_gdiBmp 为 t78 固定 96 DPI；DrawImageUnscaled 会把 96-DPI 位图在 144-DPI 设备上放大 1.5×（整帧被推离窗口，顶栏/右列被裁——t85 证据"顶栏缺失"根因）。
                // 显式目标尺寸 + Pixel 单位 = 物理像素 1:1（无 DPI 换算、无插值放大）。
                var blitState = e.Graphics.Save();
                e.Graphics.PageUnit = System.Drawing.GraphicsUnit.Pixel;
                e.Graphics.DrawImage(_gdiBmp, 0, 0, _gdiBmp.Width, _gdiBmp.Height);
                e.Graphics.Restore(blitState);
                PaintFrames++;
                _w = w; _h = h;
            }
            catch { }
        }

        // t9：背景层画刷静态缓存（每帧 OnPaint 不再 new 渐变刷 + 双光晕 6 刷 + 星点 18 刷 ≈ 25 个 GDI 对象）
        static readonly object _bgBrushLock = new object();
        static readonly Dictionary<int, SolidBrush> _bgBrushes = new Dictionary<int, SolidBrush>();
        static System.Drawing.Drawing2D.LinearGradientBrush _backdropGradient;

        static SolidBrush BgBrush(int a, int r, int g, int b)
        {
            int key = (a << 24) | (r << 16) | (g << 8) | b;
            lock (_bgBrushLock)
            {
                if (_bgBrushes.TryGetValue(key, out var s)) return s;
                if (_bgBrushes.Count >= 128) _bgBrushes.Clear();
                var created = new SolidBrush(Color.FromArgb(a, r, g, b));
                _bgBrushes[key] = created;
                return created;
            }
        }

        static System.Drawing.Drawing2D.LinearGradientBrush BackdropGradient
        {
            get
            {
                lock (_bgBrushLock)
                {
                    if (_backdropGradient == null)
                        _backdropGradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                            new Rectangle(0, 0, 1280, 800),
                            Color.FromArgb(11, 16, 32),      // #0B1020 顶部
                            Color.FromArgb(7, 10, 18),       // #070A12 底部
                            System.Drawing.Drawing2D.LinearGradientMode.Vertical);
                    return _backdropGradient;
                }
            }
        }

        /// <summary>背景层（t58 现代 UI）：暗色垂直渐变 #0B1020→#070A12 + 左上蓝光晕 #2D6CFF α24 + 右下紫光晕 #8B5CF6 α16 + 18 颗确定性星点（α40..90）。</summary>
        static void DrawBackdrop(Graphics g)
        {
            g.FillRectangle(BackdropGradient, 0, 0, 1280, 800);

            // 左上蓝光晕（三层柔化：α8/α16/α24 同心椭圆）
            Glow(g, -150, -170, 620, 460, 45, 108, 255);
            // 右下紫光晕
            Glow(g, 1000, 620, 560, 420, 139, 92, 246);

            // 确定性星点 ×18（避开内容密集区，α40..90）
            Star(g, 96, 140, 1.6, 60); Star(g, 210, 92, 1.1, 88); Star(g, 330, 170, 1.4, 47);
            Star(g, 470, 96, 1.2, 74); Star(g, 590, 150, 1.8, 55); Star(g, 700, 88, 1.1, 82);
            Star(g, 830, 170, 1.5, 49); Star(g, 960, 110, 1.2, 70); Star(g, 1100, 160, 1.7, 58);
            Star(g, 140, 720, 1.3, 66); Star(g, 300, 762, 1.6, 45); Star(g, 460, 706, 1.1, 84);
            Star(g, 620, 748, 1.5, 52); Star(g, 780, 690, 1.2, 76); Star(g, 940, 742, 1.8, 60);
            Star(g, 1060, 700, 1.1, 86); Star(g, 1170, 760, 1.5, 44); Star(g, 1240, 150, 1.2, 68);
        }

        static void Glow(Graphics g, double x, double y, double w, double h, int r, int gc, int b)
        {
            g.FillEllipse(BgBrush(8, r, gc, b), (float)(x - w * 0.25), (float)(y - h * 0.25), (float)(w * 1.5), (float)(h * 1.5));
            g.FillEllipse(BgBrush(16, r, gc, b), (float)x, (float)y, (float)w, (float)h);
            g.FillEllipse(BgBrush(24, r, gc, b), (float)(x + w * 0.22), (float)(y + h * 0.22), (float)(w * 0.56), (float)(h * 0.56));
        }

        static void Star(Graphics g, double x, double y, double r, int a)
        {
            a = Math.Min(120, a + 20);   // P2：星点 α+20
            g.FillEllipse(BgBrush(a, 255, 255, 255), (float)(x - r * 1.25), (float)(y - r * 1.25), (float)(r * 2.5), (float)(r * 2.5));
        }

        void PaintContent()
        {
            if (_gdiAdapter == null) return;
            if (_trans != null)
            {
                // t37：转场帧只做缓存帧位图合成（GoTo 已预渲染 prev/next 各一次）。
                // 合成在物理 1:1 空间（缓存帧已含 letterbox 矢量变换），完成后恢复变换给 DrawTopBar。
                double p = _trans.Progress;
                var g = _adapterG;
                var gst = g.Save();
                g.ResetTransform();
                if (_trans is SlideTransition st)
                {
                    double dist = st.Distance * _w * _lscale;      // 逻辑位移 × letterbox 缩放 = 物理位移（与旧逻辑变换渲染同像素）
                    double oldX = -p * dist * st.Direction;
                    double newX = (1 - p) * dist * st.Direction;
                    if (_prevBmp != null) g.DrawImageUnscaled(_prevBmp, (int)oldX, 0);
                    Dim(0.5 * p);
                    if (_nextBmp != null) g.DrawImageUnscaled(_nextBmp, (int)newX, 0);
                }
                else if (_trans is CircleRevealTransition ct)
                {
                    if (_prevBmp != null) g.DrawImageUnscaled(_prevBmp, 0, 0);
                    Dim(0.5 * p);
                    double maxR = ct.MaxRadius * (Math.Sqrt((double)_w * _w + (double)_h * _h) * 0.5 + 1);
                    RenderClippedCircleBmp(_nextBmp, _w * 0.5, _h * 0.5, p * maxR);
                }
                else
                {
                    if (_prevBmp != null) { g.DrawImageUnscaled(_prevBmp, 0, 0); Dim(p); }
                    if (_nextBmp != null) { g.DrawImageUnscaled(_nextBmp, 0, 0); Dim(1 - p); }
                }
                g.Restore(gst);
                if (_trans.IsComplete)
                {
                    foreach (var kv in _canvases)   // t37 增强：旧页内容可能已随交互变化——其预热帧作废，交空闲期重捕
                        if (ReferenceEquals(kv.Value, _prev)) { _pageFrames.Remove(kv.Key); }
                    _active = _next;
                    _prev = null; _next = null; _trans = null;
                    if (_active != null) { _active.Width = _w; _active.Height = _h; }
                }
            }
            else if (_active != null)
            {
                _active.Invalidate();          // t37：壳层脏帧（顶栏悬停等）也要带动页面重绘——RenderCaching 跳过需先标脏
                _active.Render(_gdiAdapter);
            }
        }

        // ===== t37：静态层缓存与转场缓存帧 =====

        /// <summary>背景层（深空渐变+双光晕+星点）离屏缓存：物理尺寸变化才重建；调用方 Graphics 处于物理 1:1（无变换）。</summary>
        void DrawBackdropCached(Graphics g)
        {
            int w = _gdiBmp != null ? _gdiBmp.Width : 1280;
            int h = _gdiBmp != null ? _gdiBmp.Height : 800;
            if (_backdropBmp == null || _backdropBmp.Width != w || _backdropBmp.Height != h)
            {
                try { _backdropBmp?.Dispose(); } catch { }
                _backdropBmp = DpiBitmap.Create(Math.Max(1, w), Math.Max(1, h));   // t86：96 DPI——下方 DrawImageUnscaled 在 96-DPI 适配器上 1:1（旧 144-DPI 位图会被缩到 2/3）
                using (var bg = Graphics.FromImage(_backdropBmp))
                {
                    bg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    bg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    bg.TranslateTransform((float)_ltx, (float)_lty);
                    bg.ScaleTransform((float)_lscale, (float)_lscale);
                    DrawBackdrop(bg);   // 与旧路径同函数同变换——逐像素一致
                }
            }
            g.DrawImageUnscaled(_backdropBmp, 0, 0);
        }

        /// <summary>t37 增强①：空闲期逐页预热转场帧（每空闲 tick 捕获一页，把 GoTo 的一次性双页预渲染
        /// 摊薄到不可感知的空闲时段——转场开始的「首帧 329ms」类阻塞从点击路径上消除）。</summary>
        void WarmPageFramesOneTick()
        {
            if (_pageFramesWarmed || _contentDirty || _trans != null || _gdiBmp == null) return;
            foreach (var kv in _canvases)
            {
                if (_pageFrames.ContainsKey(kv.Key)) continue;
                Bitmap bmp = null;
                CaptureCanvasFrame(kv.Value, ref bmp);
                _pageFrames[kv.Key] = bmp;
                return;   // 每空闲 tick 只捕一页
            }
            _pageFramesWarmed = true;
        }

        /// <summary>把页面渲染进缓存帧位图（物理尺寸，含 letterbox 矢量变换；透明底——背景层在其下 Blit 透出）。</summary>
        void CaptureCanvasFrame(UiCanvas c, ref Bitmap bmp)
        {
            if (c == null) { bmp = null; return; }
            int w = _gdiBmp != null ? _gdiBmp.Width : 1280;
            int h = _gdiBmp != null ? _gdiBmp.Height : 800;
            // t74 B16：离屏取证/转场缓存路径（--foldershot 等）可能从未 OnPaint——_lscale 缺省 0 → ScaleTransform(0,0) ArgumentException。
            //   尺寸保护：按目标位图自身求 letterbox（scale 恒 >0 且有限），不依赖 _ltx/_lty/_lscale 陈旧值。
            if (w < 1) w = 1280; if (h < 1) h = 800;
            double scale = Math.Min((double)w / 1280.0, (double)h / 800.0);
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1.0;
            double tx = (w - 1280 * scale) / 2.0, ty = (h - 800 * scale) / 2.0;
            if (bmp == null || bmp.Width != w || bmp.Height != h)
            {
                try { bmp?.Dispose(); } catch { }
                bmp = DpiBitmap.Create(w, h);   // t78：96 DPI（转场缓存帧与 OnPaint 同口径）
            }
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform((float)tx, (float)ty);
                g.ScaleTransform((float)scale, (float)scale);
                var adapter = new GdiDrawAdapter(g);
                c.Invalidate();               // RenderCaching：强制本帧出图（画布保持当前布局尺寸，与旧实时渲染同口径）
                c.Render(adapter);
            }
        }

        /// <summary>缓存帧圆揭示（物理坐标；与旧 RenderClippedCircle 同裁剪几何）。
        /// t37 增强②：源矩形条带 Blit——每个条带只拷贝该条带像素（旧实现每带整图 DrawImage ≈ 每帧数百 MB 拷贝，
        /// 是转场帧 130~178ms 的根因之一）；无 SetClip、无变换状态切换，帧间 ≤16ms。</summary>
        void RenderClippedCircleBmp(Bitmap bmp, double cx, double cy, double r)
        {
            if (bmp == null || r <= 0 || _adapterG == null) return;
            int bands = Math.Max(12, Math.Min(48, (int)(r / 10)));
            double bandH = (2.0 * r) / bands;
            double top0 = cy - r;
            for (int i = 0; i <= bands; i++)
            {
                double yTop = top0 + i * bandH;
                double yc = yTop + bandH * 0.5;
                double dy = yc - cy;
                if (Math.Abs(dy) > r) continue;
                double half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
                double l = Math.Max(0, cx - half), rr = Math.Min(_w, cx + half);
                double tt = Math.Max(0, yTop), bb = Math.Min(_h, yTop + bandH);
                if (rr - l <= 0 || bb - tt <= 0) continue;
                int sl = (int)l, stt = (int)tt, sw = Math.Max(1, (int)(rr - l)), sh = Math.Max(1, (int)(bb - tt));
                // 源矩形 = 位图同坐标（位图即物理尺寸）；目标矩形 = 同条带——逐像素一致，只拷条带
                _adapterG.DrawImage(bmp,
                    new Rectangle(sl, stt, sw, sh),
                    sl, stt, sw, sh,
                    GraphicsUnit.Pixel);
            }
        }

        void Dim(double a)
        {
            if (a <= 0.001 || _adapterG == null) return;
            using var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, a)), 0, 0, 0));
            var st = _adapterG.Save();
            _adapterG.ResetTransform();   // t27：按物理客户区全位图（含 letterbox 边带）均匀压暗
            _adapterG.FillRectangle(b, 0, 0, _w, _h);
            _adapterG.Restore(st);
        }

        void RenderClippedCircle(UiCanvas c, double cx, double cy, double r)
        {
            if (c == null || r <= 0 || _gdiAdapter == null) return;
            int bands = Math.Max(12, Math.Min(48, (int)(r / 10)));
            double bandH = (2.0 * r) / bands;
            double top0 = cy - r;
            for (int i = 0; i <= bands; i++)
            {
                double yTop = top0 + i * bandH;
                double yc = yTop + bandH * 0.5;
                double dy = yc - cy;
                if (Math.Abs(dy) > r) continue;
                double half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
                double l = Math.Max(0, cx - half), rr = Math.Min(_w, cx + half);
                double tt = Math.Max(0, yTop), bb = Math.Min(_h, yTop + bandH);
                if (rr - l <= 0 || bb - tt <= 0) continue;
                var st = _adapterG.Save();
                _adapterG.ResetTransform();                                // t27：裁剪矩形用物理客户区坐标
                _adapterG.SetClip(new RectangleF((float)l, (float)tt, (float)(rr - l), (float)(bb - tt)));
                _adapterG.TranslateTransform((float)_ltx, (float)_lty);    // t27：内容回 1280×800 逻辑坐标
                _adapterG.ScaleTransform((float)_lscale, (float)_lscale);
                c.Render(_gdiAdapter);
                _adapterG.Restore(st);
            }
        }

        // ===== t13：F1 顶部菜单条（引擎渲染；动作与旧菜单栏 D4/主菜单同源） =====
        bool _showTopBar;
        bool _topBarSuppressed;   // t20 P1-8：F1 菜单条显示期间隐藏引擎顶栏（防 Logo/顶栏叠放）
        /// <summary>t20 P1-8：MainForm F1 菜单条显示时置 true——引擎顶栏让位（设计选择：菜单条优先，顶栏暂时隐藏）。</summary>
        public bool TopBarSuppressed { set { _topBarSuppressed = value; _contentDirty = true; Invalidate(); } }   // t37：顶栏让位状态变化强制重绘
        readonly List<(string text, Action act)> _topItems = new List<(string, Action)>();
        int _topHover = -1;
        const int TopBarH = 30;

        void BuildTopBar()
        {
            _topItems.Add((UiText.MenuPlayGame, () => GoTo("songs", TransitionStyle.Slide)));
            _topItems.Add((UiText.MenuEditChart, () => InvokeAction(UiText.MenuEditChart)));
            _topItems.Add((UiText.MenuSettings, () => GoTo("settings", TransitionStyle.CircleReveal)));
            _topItems.Add((UiText.MenuOpenLog, () => InvokeAction(UiText.MenuOpenLog)));
            _topItems.Add((UiText.MenuAbout, () => GoTo("about", TransitionStyle.Fade)));
            _topItems.Add((UiText.MenuExit, () => Application.Exit()));
        }

        // 顶部菜单条绘制（画布坐标 1280 宽；在 PaintContent 之后叠加到 _gdiBmp）
        void DrawTopBar()
        {
            if (!_showTopBar || _topBarSuppressed || _topItems.Count == 0) return;   // t20 P1-8：F1 菜单条期间顶栏让位
            if (_gdiAdapter == null) return;
            _gdiAdapter.Rect(0, 0, 1280, TopBarH, new RgbaColor(10, 14, 22, 224));
            _gdiAdapter.Line(0, TopBarH, 1280, TopBarH, new RgbaColor(70, 90, 130, 120), 1.2);
            double x = 12;
            for (int i = 0; i < _topItems.Count; i++)
            {
                string t = _topItems[i].text;
                double tw = _gdiAdapter.MeasureText(t, 12.5);
                bool hov = i == _topHover;
                _gdiAdapter.RoundedRect(x, 3, tw + 18, TopBarH - 6, 6, hov ? new RgbaColor(45, 62, 100, 200) : new RgbaColor(255, 255, 255, 0));
                _gdiAdapter.Text(t, x + 9, TopBarH / 2 - 8, 12.5, hov ? new RgbaColor(255, 255, 255) : new RgbaColor(216, 226, 240, 235));
                x += tw + 30;
            }
        }

        // 命中：返回顶部条命中项（vx,vy=虚拟画布坐标；未命中返回 -1）
        int TopBarHit(double vx, double vy)
        {
            if (!_showTopBar || _topBarSuppressed || vy < 0 || vy > TopBarH) return -1;   // t20 P1-8
            double x = 12;
            for (int i = 0; i < _topItems.Count; i++)
            {
                double tw = _gdiAdapter != null ? _gdiAdapter.MeasureText(_topItems[i].text, 12.5) : 60;
                if (vx >= x && vx <= x + tw + 18) return i;
                x += tw + 30;
            }
            return -1;
        }

        bool _fullscreen;

        /// <summary>F11 全屏切换（无边框最大化；GDI letterbox 渲染自动适配全屏尺寸）。</summary>
        public void ToggleFullscreen()
        {
            _contentDirty = true;   // t37：窗口样式/尺寸变化（WM_SIZE 也会触 OnPaint）
            _fullscreen = !_fullscreen;
            if (_fullscreen)
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.FixedSingle;
                WindowState = FormWindowState.Normal;
            }
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
            if (_fullscreen && keyData == Keys.Escape) { ToggleFullscreen(); return true; }
            // t40：选歌曲目网格键盘翻页（PgUp/PgDn 一屏、Home/End 首末）——与滚轮共用 ScrollSongs
            if (_songsGrid != null && _songsGrid.Children.Count > 0)
            {
                if (keyData == Keys.PageDown) { ScrollSongs(_songsGrid.Height * 0.9); return true; }
                if (keyData == Keys.PageUp) { ScrollSongs(-_songsGrid.Height * 0.9); return true; }
                if (keyData == Keys.Home) { ScrollSongs(-100000); return true; }
                if (keyData == Keys.End) { ScrollSongs(100000); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>t40：选歌曲目网格纵向滚动（delta&gt;0 下滚；按行高总长钳制在可视区内）。滚轮/PgUp/PgDn/Home/End 共用。</summary>
        void ScrollSongs(double delta)
        {
            if (_songsGrid == null || !(_songsGrid is ScrollableSongGrid sg) || _songsGrid.Children.Count == 0) return;
            int cols = _songsGrid.Columns > 0 ? _songsGrid.Columns : 3;
            double pitch = 140 + _songsGrid.Spacing;
            double total = Math.Ceiling((double)_songsGrid.Children.Count / cols) * pitch + _songsGrid.Spacing;
            double maxScroll = Math.Max(0, total - _songsGrid.Height);
            sg.ScrollY = Math.Max(0, Math.Min(maxScroll, sg.ScrollY + delta));
            Invalidate();
        }

        const int WM_MOUSEWHEEL = 0x020A;
        /// <summary>t40：滚轮直接经 WM_MOUSEWHEEL 处理（不依赖控件路由/焦点——s39 实测轮询路由未送达时仍可滚）。</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                try
                {
                    int delta = unchecked((short)((long)m.WParam >> 16));
                    ScrollSongs(-delta / 3.0);
                    return;
                }
                catch { }
            }
            base.WndProc(ref m);
        }

        /// <summary>t55：搜索框聚焦时接收窗键字符（可编辑文本过滤）。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1) { _showTopBar = !_showTopBar; Invalidate(); e.Handled = true; return; }   // t13：F1 顶部菜单条开关
            if (_songsSearchFocus)
            {
                if (e.KeyCode == Keys.Back)
                {
                    if (_songsSearchText.Length > 0) _songsSearchText = _songsSearchText.Substring(0, _songsSearchText.Length - 1);
                }
                else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
                {
                    _songsSearchFocus = false;
                }
                else
                {
                    char ch = (char)e.KeyValue;
                    if (ch >= 32 && ch < 0x2500 && !char.IsControl(ch)) _songsSearchText += ch;
                }
                RebuildSongList();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var (vx, vy) = ToVirtual(e.X, e.Y);
            _lastPointerX = vx; _lastPointerY = vy;   // t5 P0-1：HostTabBar 兜底命中
            int hit = TopBarHit(vx, vy);
            if (hit != _topHover) { _topHover = hit; _contentDirty = true; Invalidate(); }   // t37：顶栏悬停=动画帧
            if (hit >= 0) return;   // 顶部菜单条捕获
            _active?.PointerMove(vx, vy);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var (vx, vy) = ToVirtual(e.X, e.Y);
            _lastPointerX = vx; _lastPointerY = vy;   // t5 P0-1：HostTabBar 兜底命中
            int hit = TopBarHit(vx, vy);
            if (hit >= 0) { _contentDirty = true; try { _topItems[hit].act(); } catch (Exception ex) { Logger.Error("顶部菜单动作失败：" + _topItems[hit].text, ex); } return; }   // t13：菜单条点击
            _active?.PointerDown(vx, vy);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); var (vx, vy) = ToVirtual(e.X, e.Y); _lastPointerX = vx; _lastPointerY = vy; _active?.PointerUp(vx, vy); }   // t5 P0-1：记录点击点供 HostTabBar 兜底

        /// <summary>t39/t40：选歌曲目网格滚轮纵向滚动（与 WndProc WM_MOUSEWHEEL 兜底双路径；滚动量按行高钳制）。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            try { ScrollSongs(-e.Delta / 3.0); } catch { }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _active?.PointerLeave(); _contentDirty = true; }   // t37：悬停清除=动画帧

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer?.Stop();
            _d2d?.Dispose();
            try { _adapterG?.Dispose(); } catch { }   // t27：常驻位图 Graphics 随窗体释放
            try { _gdiBmp?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }

        /// <summary>可点击玻璃卡片：整体命中（子 Label 不拦截）、单击选中 / 双击游玩、悬停高亮与选中描边（UiCard 自带）。</summary>
        class ClickableCard : UiCard
        {
            double _lastClickMs = -1000;
            public event Action Clicked;
            public event Action DoubleClicked;
            public override void InvokeClick()
            {
                double now = Environment.TickCount64;
                if (now - _lastClickMs < 450) DoubleClicked?.Invoke();
                else Clicked?.Invoke();
                _lastClickMs = now;
            }
            public override UiElement Hit(double wx, double wy) => Visible && HitTest(wx, wy) ? this : null;
        }

        /// <summary>t39：选歌曲目网格——Layout 后按 ScrollY 整体上移（配合 ClipChildren 裁剪 + 滚轮滚动，30+ 卡片可达）。</summary>
        sealed class ScrollableSongGrid : UiGridLayout
        {
            public double ScrollY;
            public override void Layout()
            {
                base.Layout();
                if (ScrollY > 0)
                    foreach (var c in _children) c.Y -= ScrollY;
            }
        }

        /// <summary>P0-1（t5 宿主侧兜底）：设置页签点击 —— 引擎 UiTabBar.InvokeClick 为空实现（t6 引擎侧根治），
        /// 宿主侧记录最近指针虚拟坐标，InvokeClick 时按 Tabs 均分宽度计算 idx → Select(idx)。
        /// t6 在引擎侧实现 InvokeClick 后语义等价（点击同一位置选中同一页签），无双重触发风险。</summary>
        sealed class HostTabBar : UiTabBar
        {
            /// <summary>最近指针虚拟坐标提供方（EngineMainShell 记录）。</summary>
            public Func<(double x, double y)> LastPointer;
            public override void InvokeClick()
            {
                try
                {
                    if (Tabs == null || Tabs.Length == 0) return;
                    var p = LastPointer != null ? LastPointer() : (x: -1.0, y: -1.0);
                    var (lx, ly) = WorldToLocal(p.x, p.y);
                    int idx;
                    if (lx < 0 || lx > Width || ly < 0 || ly > Height) idx = ActiveIndex;
                    else idx = (int)(lx / Math.Max(1, Width / Tabs.Length));
                    if (idx < 0 || idx >= Tabs.Length) idx = ActiveIndex;
                    Select(idx);
                }
                catch { }
            }
        }

        /// <summary>P0-2（t5）：▲▼ 箭头命中卡 —— 命中区域随父 UiNumberBox 实际宽实时计算
        /// （与 UiNumberBox.DrawSelf 的 bx=Width-cell*2-2 / cell=(Height-4)/2 同公式），
        /// 修复网格拉伸（300→900）后可见箭头与可点区错位 590px 的问题。</summary>
        sealed class ArrowCard : ClickableCard
        {
            public UiNumberBox Box;
            public bool Down;
            (double ax, double ay, double aw, double ah) Zone()
            {
                if (Box == null) return (0, 0, 0, 0);
                double cell = Math.Max(1, (Box.Height - 4) / 2.0);
                double ax = Math.Max(0, Box.Width - cell * 2 - 2);
                double ay = Down ? cell : 0;
                return (ax, ay, cell * 2, cell);
            }
            public override UiElement Hit(double wx, double wy)
            {
                if (Box == null || !Visible) return null;
                var (lx, ly) = Box.WorldToLocal(wx, wy);
                var (ax, ay, aw, ah) = Zone();
                return lx >= ax && lx <= ax + aw && ly >= ay && ly <= ay + ah ? this : null;
            }
        }
    }
}
