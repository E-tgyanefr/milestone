using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 引擎 UI 演示（垂直切片）：用引擎 Component(UiComponents)+UiTheme 构建主菜单/选歌/设置三页场景，
    /// 用引擎 SceneManager + SceneTransition 做页面转场（Fade / Slide / CircleReveal 至少三种），
    /// 渲染经 IUiDraw（D2DDrawAdapter→D2DRenderer）。选歌页可启动既有 GamePanel 游玩（由 MainForm 接线 PlayChart）。
    /// </summary>
    public static class EngineUiDemo
    {
        public const string EntryButton = "✨ 引擎 UI 演示";

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        /// <summary>打开演示窗口（owner 上方）。返回演示窗体。</summary>
        public static EngineUiDemoForm Open(Form owner, Func<Chart, string, bool> playChart)
        {
            var f = new EngineUiDemoForm(playChart);
            if (owner != null) { f.StartPosition = FormStartPosition.Manual; f.Location = owner.Location; f.Size = owner.Size; }
            f.Show();
            return f;
        }

        /// <summary>截取控件当前画面到 Bitmap（离屏/覆盖转场用）。</summary>
        public static Bitmap Capture(Control c)
        {
            try
            {
                int w = Math.Max(1, c.ClientSize.Width), h = Math.Max(1, c.ClientSize.Height);
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(c.Handle, hdc, 2); } catch { }
                    g.ReleaseHdc(hdc);
                }
                return bmp;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// 引擎 UI 演示窗体：自持 D2DRenderer + D2DDrawAdapter，定时驱动引擎 SceneManager 并逐帧渲染当前页面。
    /// 页面 = 引擎 Scene（根 GameObject + UiCanvas）。页面切换走 SceneManager.LoadScene(next, transition)，
    /// 转场视觉按 SceneTransition 采样值（Progress/OutAlpha/InAlpha/OffsetX/流派参数）实时渲染旧/新两页。
    /// </summary>
    public sealed class EngineUiDemoForm : Form
    {
        readonly SceneManager _sm = new SceneManager();
        readonly Dictionary<string, Scene> _pages = new Dictionary<string, Scene>(StringComparer.Ordinal);
        readonly Dictionary<Scene, UiCanvas> _canvases = new Dictionary<Scene, UiCanvas>();
        readonly Func<Chart, string, bool> _playChart;
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
        double _lastMs;
        /// <summary>非空时在每帧 D2D 提交后立即 PrintWindow 截图（与 GamePanel.AutoShotTick 同法，避免截图时序丢帧）。</summary>
        public static string AutoShotDir;
        int _autoShotCounter;
        /// <summary>诊断：D2D 渲染器是否初始化成功。</summary>
        public bool D2dReady => _d2d != null;
        /// <summary>诊断：OnPaint 实际提交的帧数。</summary>
        public int PaintFrames;
        /// <summary>选歌页曲目枚举目录覆盖（默认 AppDomain.BaseDirectory/Chart）；--uitrial 指到测试格式目录保证曲目存在。</summary>
        public static string ChartSearchDir;
        /// <summary>最近一次 GoTo 的转场风格（诊断/试玩认证断言）。</summary>
        public string LastTransitionStyle;
        /// <summary>SceneTransition.Completed 已触发的次数（每次 GoTo 的转场完成 +1）。</summary>
        public int TransitionCompletedCount;
        /// <summary>页面切换次数（转场完成后 PaintContent 切到新页 +1）。</summary>
        public int PageSwitchCount;

        public EngineUiDemoForm(Func<Chart, string, bool> playChart)
        {
            _playChart = playChart;
            Text = "✨ 引擎 UI 演示 · Milestone";
            ClientSize = new Size(1280, 720);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(_theme.Bg.R, _theme.Bg.G, _theme.Bg.B);
            ShowInTaskbar = true;
            // ResizeRedraw：D2D 渲染目标跟随窗口尺寸/DPI 变化而重建，尺寸变化时立即重绘（与 GamePanel 同策略）
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            BuildPages();
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
                Invalidate();
            };
            _lastMs = _clock.ElapsedMilliseconds;
            _timer.Start();
        }

        void BuildPages()
        {
            _pages["menu"] = BuildMenu();
            _pages["songs"] = BuildSongs();
            _pages["settings"] = BuildSettings();
        }

        UiCanvas NewCanvas(Scene scene, string name)
        {
            var go = scene.AddRootGameObject(name);
            var canvas = go.AddComponent<UiCanvas>();
            canvas.Width = 1280; canvas.Height = 720; canvas.ViewWidth = 1280; canvas.ViewHeight = 720;
            _canvases[scene] = canvas;
            return canvas;
        }

        Scene BuildMenu()
        {
            var scene = new Scene();
            var canvas = NewCanvas(scene, "MainMenu");
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 720; bg.Padding = 48; bg.Background = _theme.Bg; bg.CornerRadius = 0;

            var logo = bg.AddChild<UiLabel>();
            logo.Text = "MILESTONE"; logo.Color = _theme.TextPrimary; logo.FontSize = _theme.FnH1;
            logo.Width = 1184; logo.Height = 90; logo.X = 48; logo.Y = 32; logo.Align = UiAlign.Left;

            var sub = bg.AddChild<UiLabel>();
            sub.Text = "Unity 式场景运行时 · 引擎驱动 UI · 现代深色玻璃";
            sub.Color = _theme.TextMuted; sub.FontSize = _theme.FnSub;
            sub.Width = 1184; sub.Height = 28; sub.X = 48; sub.Y = 128; sub.Align = UiAlign.Left;

            var stack = bg.AddChild<UiStackLayout>();
            stack.X = 48; stack.Y = 200; stack.Width = 1184; stack.Height = 420;
            stack.Orientation = UiOrientation.Vertical; stack.Spacing = _theme.S3; stack.FillCrossAxis = true;

            stack.AddChild(MakeButton("▶  开始游玩", () => GoTo("songs", TransitionStyle.Fade)));
            stack.AddChild(MakeButton("🎵  选歌", () => GoTo("songs", TransitionStyle.Slide)));
            stack.AddChild(MakeButton("⚙  设置（主题/交互）", () => GoTo("settings", TransitionStyle.CircleReveal)));
            stack.AddChild(MakeButton("🌀  引擎演示（再演一遍转场）", () => GoTo("menu", TransitionStyle.Fade)));
            return scene;
        }

        Scene BuildSongs()
        {
            var scene = new Scene();
            var canvas = NewCanvas(scene, "SongSelect");
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 720; bg.Padding = 48; bg.Background = _theme.Surface; bg.CornerRadius = 0;

            var title = bg.AddChild<UiLabel>();
            title.Text = "选歌"; title.Color = _theme.TextPrimary; title.FontSize = _theme.FnH1;
            title.Width = 900; title.Height = 52; title.X = 48; title.Y = 28; title.Align = UiAlign.Left;

            var back = bg.AddChild<UiButton>();
            back.Text = "←  返回"; back.Width = 150; back.Height = 46; back.X = 48; back.Y = 96;
            back.CornerRadius = _theme.R2; back.Accent = false; back.FontSize = _theme.FnBody;
            back.Clicked += () => GoTo("menu", TransitionStyle.Fade);

            var list = bg.AddChild<UiStackLayout>();
            list.X = 48; list.Y = 170; list.Width = 1184; list.Height = 500;
            list.Orientation = UiOrientation.Vertical; list.Spacing = _theme.S2; list.FillCrossAxis = false;

            int n = 0;
            foreach (var (chart, dir) in EnumerateCharts())
            {
                var c = chart; var d = dir;
                var btn = new UiButton
                {
                    Text = chart.Title + " · " + chart.Artist + " · " + ModeSystem.DisplayName(chart.Mode),
                    Accent = false, CornerRadius = _theme.R2, FontSize = _theme.FnBody,
                    Width = 1184, Height = 54
                };
                btn.Clicked += () =>
                {
                    try { if (_playChart != null && _playChart(c, d)) Close(); }
                    catch (Exception ex) { Logger.Error("引擎 UI 演示启动游玩失败", ex); }
                };
                list.AddChild(btn);
                if (++n >= 18) break;
            }
            if (n == 0)
            {
                var empty = bg.AddChild<UiLabel>();
                empty.Text = "未找到谱面，请将谱面放入 Chart 目录（或用 MainForm 选歌）。";
                empty.Color = _theme.TextMuted; empty.FontSize = _theme.FnBody;
                empty.Width = 1184; empty.Height = 60; empty.X = 48; empty.Y = 200; empty.Align = UiAlign.Left;
            }
            return scene;
        }

        Scene BuildSettings()
        {
            var scene = new Scene();
            var canvas = NewCanvas(scene, "Settings");
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 720; bg.Padding = 48; bg.Background = _theme.Surface2; bg.CornerRadius = 0;

            var title = bg.AddChild<UiLabel>();
            title.Text = "设置"; title.Color = _theme.TextPrimary; title.FontSize = _theme.FnH1;
            title.Width = 900; title.Height = 52; title.X = 48; title.Y = 28; title.Align = UiAlign.Left;

            var back = bg.AddChild<UiButton>();
            back.Text = "←  返回"; back.Width = 150; back.Height = 46; back.X = 48; back.Y = 96;
            back.CornerRadius = _theme.R2; back.Accent = false; back.FontSize = _theme.FnBody;
            back.Clicked += () => GoTo("menu", TransitionStyle.Fade);

            var grid = bg.AddChild<UiGridLayout>();
            grid.X = 48; grid.Y = 180; grid.Width = 1184; grid.Height = 400;
            grid.Columns = 3; grid.Spacing = _theme.S3; grid.CellHeight = 100;

            grid.AddChild(ThemeSwatch("深空蓝", new RgbaColor(61, 123, 255)));
            grid.AddChild(ThemeSwatch("极夜紫", new RgbaColor(139, 92, 246)));
            grid.AddChild(ThemeSwatch("晨光青", new RgbaColor(74, 168, 255)));
            return scene;
        }

        UiButton MakeButton(string text, Action onClick)
        {
            var b = new UiButton { Text = text, Accent = true, CornerRadius = _theme.R3, FontSize = _theme.FnH2, Width = 1184, Height = 60 };
            b.Clicked += onClick;
            return b;
        }

        UiPanel ThemeSwatch(string name, RgbaColor accent)
        {
            var p = new UiPanel { Width = 372, Height = 96, CornerRadius = _theme.R3, Background = _theme.CardBg };
            var l = new UiLabel { Text = name, Color = _theme.TextPrimary, FontSize = _theme.FnTitle, Width = 340, Height = 30, X = 16, Y = 14, Align = UiAlign.Left };
            p.AddChild(l);
            var line = new UiLabel { Text = "■ 玻璃质感", Color = accent, FontSize = _theme.FnBody, Width = 340, Height = 24, X = 16, Y = 52, Align = UiAlign.Left };
            p.AddChild(line);
            return p;
        }

        void GoTo(string key, TransitionStyle style)
        {
            if (!_pages.TryGetValue(key, out var next)) return;
            if (ReferenceEquals(_active, _canvases[next])) return;
            _prev = _active;
            _next = _canvases[next];
            _next.X = 0; _next.Y = 0;
            _trans = SceneCompositor.Create(style, 380);
            LastTransitionStyle = style.ToString();
            _trans.Completed += _ => TransitionCompletedCount++;   // 试玩认证：转场完成的直接证据
            _sm.LoadScene(next, _trans);
        }

        IEnumerable<(Chart, string)> EnumerateCharts()
        {
            var dirs = new List<string>();
            var baseChart = string.IsNullOrEmpty(ChartSearchDir)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart")
                : ChartSearchDir;
            if (Directory.Exists(baseChart)) dirs.Add(baseChart);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            var exts = ChartParser.ChartExts;
            foreach (var dir in dirs)
            {
                string[] files;
                try { files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories); } catch { continue; }
                foreach (var f in files)
                {
                    if (count >= 40) yield break;
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(exts, ext) < 0) continue;
                    Chart c = null;
                    try { c = ChartParser.ParseFile(f); } catch { }
                    if (c == null || !seen.Add(f)) continue;
                    count++;
                    yield return (c, Path.GetDirectoryName(f));
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { DarkMode.Enable(Handle); } catch { }
            _w = Math.Max(1, ClientSize.Width); _h = Math.Max(1, ClientSize.Height);
            try { _d2d = new D2DRenderer(Handle, _w, _h); _adapter = new D2DDrawAdapter(_d2d); if (_active != null) { _active.Width = _w; _active.Height = _h; } }
            catch (Exception ex) { Logger.Error("引擎 UI 演示渲染器初始化失败", ex); }
        }

        /// <summary>把指定页面渲染到任意 IUiDraw（无窗口 / 软件渲染 / 截图证据用）。</summary>
        public bool RenderPageTo(string key, IUiDraw draw, int w, int h)
        {
            if (!_pages.TryGetValue(key, out var scene) || !_canvases.TryGetValue(scene, out var c)) return false;
            c.Width = w; c.Height = h;
            c.Render(draw);
            return true;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        // 渲染修复（与引擎壳同根因）：D2D 实显在部分会话/DPI 下把 UI 填色渲染为白色；
        // 引擎 UI 演示窗改为 GDI 软件渲染（固定 1280x720 画布 + 等比 letterbox 适配窗口），颜色逐像素正确。
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
                if (_gdiBmp == null) _gdiBmp = DpiBitmap.Create(1280, 720);   // t78：96 DPI（演示窗与引擎壳同口径——高 DPI 屏文本 1.5× 放大被裁根治）
                using (var g = Graphics.FromImage(_gdiBmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.FillRectangle(new SolidBrush(Color.FromArgb(_theme.Bg.R, _theme.Bg.G, _theme.Bg.B)), 0, 0, 1280, 720);
                    _gdiAdapter = new GdiDrawAdapter(g);
                    _adapterG = g;
                    PaintContent();
                }
                using (var bgBrush = new SolidBrush(Color.FromArgb(_theme.Bg.R, _theme.Bg.G, _theme.Bg.B)))
                    e.Graphics.FillRectangle(bgBrush, 0, 0, w, h);
                double scale = Math.Min((double)w / 1280.0, (double)h / 720.0);
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform((float)((w - 1280 * scale) / 2.0), (float)((h - 720 * scale) / 2.0));
                e.Graphics.ScaleTransform((float)scale, (float)scale);
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(_gdiBmp, 0, 0, 1280, 720);
                e.Graphics.Restore(state);
                PaintFrames++;
                _w = w; _h = h;
                AutoShotTick();
            }
            catch { }
        }

        /// <summary>等待 D2D 就绪并已提交至少 minFrames 帧（供转场 To 快照等待真实首帧，避免截到黑/空白帧）。</summary>
        public bool WaitReady(int minFrames, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!IsDisposed && IsHandleCreated && sw.ElapsedMilliseconds < timeoutMs)
            {
                if (D2dReady && PaintFrames >= minFrames) return true;
                Application.DoEvents();
                System.Threading.Thread.Sleep(8);
            }
            return D2dReady && PaintFrames >= minFrames;
        }

        // ===== 试玩认证支持（--uitrial 用）：只读/驱动接口，不改既有渲染与交互路径 =====

        /// <summary>当前活动页 key（menu/songs/settings），无则 null。</summary>
        public string ActivePageKey
        {
            get
            {
                foreach (var kv in _pages)
                    if (_canvases.TryGetValue(kv.Value, out var c) && ReferenceEquals(c, _active)) return kv.Key;
                return null;
            }
        }

        /// <summary>在指定页按文本命中并模拟用户点击（UiCanvas PointerMove/Down/Up 命中接口），返回是否命中并派发。</summary>
        /// <param name="index">多个命中时取第 index 个（默认 0 = 第一个）。</param>
        public bool ClickByText(string pageKey, string textSubstring, int index = 0)
        {
            if (string.IsNullOrEmpty(textSubstring)) return false;
            if (!_pages.TryGetValue(pageKey, out var scene) || !_canvases.TryGetValue(scene, out var canvas)) return false;
            var matches = new List<UiElement>();
            WalkText(canvas, textSubstring, matches);
            if (index < 0 || index >= matches.Count) return false;
            var (x, y, w, h) = matches[index].WorldRect;
            double cx = x + w * 0.5, cy = y + h * 0.5;
            canvas.PointerMove(cx, cy);
            canvas.PointerDown(cx, cy);
            canvas.PointerUp(cx, cy);
            return true;
        }

        /// <summary>按页面层级序取指定页上文本命中个数（断言前置：目标控件确实在树上）。</summary>
        public int CountByText(string pageKey, string textSubstring)
        {
            if (!_pages.TryGetValue(pageKey, out var scene) || !_canvases.TryGetValue(scene, out var canvas)) return 0;
            var matches = new List<UiElement>();
            WalkText(canvas, textSubstring, matches);
            return matches.Count;
        }

        /// <summary>按文本/控件类型遍历（命中模拟与断言共用）。</summary>
        void WalkText(UiElement n, string sub, List<UiElement> found)
        {
            if (n is UiButton b && b.Text != null && b.Text.Contains(sub)) found.Add(n);
            else if (n is UiLabel l && l.Text != null && l.Text.Contains(sub)) found.Add(n);
            foreach (var c in n.Children) WalkText(c, sub, found);
        }

        /// <summary>泵消息等待当前转场完成（_trans 清空 + 页面已切）。超时返回 false。</summary>
        public bool WaitTransitionEnd(int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!IsDisposed && sw.ElapsedMilliseconds < timeoutMs)
            {
                if (_trans == null) return true;
                Application.DoEvents();
                System.Threading.Thread.Sleep(8);
            }
            return _trans == null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        /// <summary>渲染帧提交后立即捕获（PrintWindow PW_RENDERFULLCONTENT）——与 GamePanel.AutoShotTick 同一已验证路径。</summary>
        void AutoShotTick()
        {
            if (string.IsNullOrEmpty(AutoShotDir) || _d2d == null) return;
            if (++_autoShotCounter < 10) return;
            _autoShotCounter = 0;
            try
            {
                string path = Path.Combine(AutoShotDir, "engineui-main.png");
                int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
                using var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    bool ok = false;
                    try { ok = PrintWindow(Handle, hdc, 2); } catch { }
                    g.ReleaseHdc(hdc);
                    if (ok) bmp.Save(path, ImageFormat.Png);
                }
            }
            catch { }
        }

        void PaintContent()
        {
            if (_trans != null)
            {
                double p = _trans.Progress;
                if (_trans is SlideTransition st)
                {
                    double dist = st.Distance * _w;
                    double oldX = -p * dist * st.Direction;
                    double newX = (1 - p) * dist * st.Direction;
                    if (_prev != null) { _prev.X = oldX; _prev.Render(_gdiAdapter); _prev.X = 0; }
                    Dim(0.5 * p);
                    if (_next != null) { _next.X = newX; _next.Render(_gdiAdapter); }
                    _next.X = 0;
                }
                else if (_trans is CircleRevealTransition ct)
                {
                    if (_prev != null) _prev.Render(_gdiAdapter);
                    Dim(0.5 * p);
                    double maxR = ct.MaxRadius * (Math.Sqrt((double)_w * _w + (double)_h * _h) * 0.5 + 1);
                    RenderClippedCircle(_next, _w * 0.5, _h * 0.5, p * maxR);
                }
                else
                {
                    if (_prev != null) { _prev.Render(_gdiAdapter); Dim(p); }
                    if (_next != null) { _next.Render(_gdiAdapter); Dim(1 - p); }
                }
                if (_trans.IsComplete)
                {
                    _active = _next;
                    _prev = null; _next = null; _trans = null;
                    PageSwitchCount++;                              // 试玩认证：转场完成即切新页的证据
                    if (_active != null) { _active.Width = _w; _active.Height = _h; }
                }
            }
            else if (_active != null)
            {
                _active.Render(_gdiAdapter);
            }
        }

        void Dim(double a)
        {
            if (a <= 0.001 || _adapterG == null) return;
            using var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, a)), 0, 0, 0));
            _adapterG.FillRectangle(b, 0, 0, 1280, 720);
        }

        void RenderClippedCircle(UiCanvas c, double cx, double cy, double r)
        {
            if (c == null || r <= 0) return;
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
                _gdiAdapter.PushClip(l, tt, rr - l, bb - tt);
                c.Render(_gdiAdapter);
                _gdiAdapter.PopClip();
            }
        }

        /// <summary>客户区像素 → 虚拟画布坐标（letterbox 逆变换；全屏/任意尺寸命中不漂移）。</summary>
        (double x, double y) ToVirtual(double fx, double fy)
        {
            double w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
            double scale = Math.Min(w / 1280.0, h / 720.0);
            if (scale <= 0) return (fx, fy);
            double tx = (w - 1280.0 * scale) / 2.0, ty = (h - 720.0 * scale) / 2.0;
            return ((fx - tx) / scale, (fy - ty) / scale);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var (vx, vy) = ToVirtual(e.X, e.Y); _active?.PointerMove(vx, vy); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); var (vx, vy) = ToVirtual(e.X, e.Y); _active?.PointerDown(vx, vy); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); var (vx, vy) = ToVirtual(e.X, e.Y); _active?.PointerUp(vx, vy); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _active?.PointerLeave(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer?.Stop();
            _d2d?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
