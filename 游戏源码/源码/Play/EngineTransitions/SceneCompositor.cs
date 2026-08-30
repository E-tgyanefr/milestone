using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 场景转场风格（宿主视角）。引擎库 <see cref="SceneTransition"/> 用独立子类表达各风格，
    /// 宿主用该枚举做“创建/接管”入口，并映射到引擎子类。
    /// </summary>
    public enum TransitionStyle { Fade, Slide, Wipe, CircleReveal, Beam }

    /// <summary>
    /// 宿主场景转场合成器：读引擎 <see cref="SceneTransition"/> 的采样值（Progress/OutAlpha/InAlpha/OffsetX + 风格特有参数），
    /// 用宿主 <see cref="IRenderer"/>（D2DRenderer）对 from/to 两帧快照逐帧合成。引擎只算时序/缓动，本类只读参数画。
    /// 用法（配合引擎 <see cref="SceneManager"/>）：
    ///   var t = new SlideTransition { DurationMs = 600 };
    ///   sceneManager.LoadScene(nextScene, t);            // 引擎：转场期新旧场景都 Tick，完成时切换 ActiveScene + 触发事件
    ///   var comp = new SceneCompositor(t, renderer) { From=fromBmp, To=toBmp, Width=W, Height=H };
    ///   每帧 renderer.Begin() → comp.Draw() → renderer.End();   （Draw 不推进 t，由 SceneManager 推进，避免双加速）
    /// </summary>
    public sealed class SceneCompositor
    {
        public SceneTransition Transition;
        public IRenderer Renderer;
        public D2DBitmap From, To;
        public int Width, Height;

        public SceneCompositor() { }
        public SceneCompositor(SceneTransition transition, IRenderer renderer)
        {
            Transition = transition;
            Renderer = renderer;
        }

        /// <summary>按当前 <see cref="Transition"/> 采样值合成一帧（不推进 transition）。</summary>
        public void Draw()
        {
            if (Transition == null || Renderer == null) return;
            int w = Width > 0 ? Width : (To?.Width ?? From?.Width ?? 1);
            int h = Height > 0 ? Height : (To?.Height ?? From?.Height ?? 1);
            if (w <= 0) w = 1; if (h <= 0) h = 1;

            double p = Transition.Progress;
            double outA = Transition.OutAlpha, inA = Transition.InAlpha;

            if (Transition is FadeTransition)
            {
                Img(From, 0, 0, w, h, outA);
                Img(To, 0, 0, w, h, inA);
            }
            else if (Transition is SlideTransition st)
            {
                double dist = st.Distance * w;
                double dir = st.Direction;
                double oldX = -p * dist * dir;
                double newX = (1 - p) * dist * dir;
                Img(From, oldX, 0, w, h, 1);
                Dim(0.55 * p, w, h);
                Img(To, newX, 0, w, h, 1);
            }
            else if (Transition is WipeTransition wt)
            {
                double width = Math.Max(1, wt.Width * w);
                double e = p;
                double bound = wt.Direction >= 0 ? e * width : w - e * width;
                bound = Math.Max(0, Math.Min(w, bound));
                Img(From, 0, 0, w, h, 1);
                Dim(0.35 * e, w, h);
                if (wt.Direction >= 0) DrawClipped(To, 0, 0, bound, h, 0, 0, 1, w, h);
                else DrawClipped(To, bound, 0, w - bound, h, 0, 0, 1, w, h);
            }
            else if (Transition is CircleRevealTransition ct)
            {
                double maxR = ct.MaxRadius * (Math.Sqrt((double)w * w + h * h) * 0.5 + 1);
                double r = p * maxR;
                // 旧页先画、再压暗，最后圆内揭示新页
                Img(From, 0, 0, w, h, 1);
                Dim(0.45 * p, w, h);
                RevealCircle(To, w * 0.5, h * 0.5, r, w, h);
            }
            else if (Transition is BeamTransition2 bt)
            {
                double pos = Transition.OffsetX;                 // (Progress-0.5)*2 → -1 .. 1
                double bx = (pos + 1) * 0.5 * w;                 // 光束中心 x（左→右扫）
                double thick = Math.Max(2, bt.BeamThickness * w);
                Img(From, 0, 0, w, h, outA);
                RevealBeam(To, bx, thick, w, h, inA);
            }
            else
            {
                Img(From, 0, 0, w, h, outA);
                Img(To, 0, 0, w, h, inA);
            }
        }

        void Img(D2DBitmap b, double x, double y, int w, int h, double a)
        {
            if (b == null) return;
            Renderer.DrawImage(b, (float)x, (float)y, (float)w, (float)h, ClampA(a));
        }
        void Dim(double a, int w, int h)
        {
            if (a <= 0.001) return;
            Renderer.FillRect(0, 0, (float)w, (float)h, Color.FromArgb((int)(255 * Math.Min(1, a)), 0, 0, 0));
        }
        void DrawClipped(D2DBitmap b, double cx, double cy, double cw, double ch, double ox, double oy, double a, int w, int h)
        {
            if (b == null || cw <= 0 || ch <= 0) return;
            Renderer.PushQuadLayer((float)cx, (float)cy, (float)(cx + cw), (float)cy,
                                   (float)(cx + cw), (float)(cy + ch), (float)cx, (float)(cy + ch));
            Renderer.DrawImage(b, (float)ox, (float)oy, (float)w, (float)h, ClampA(a));
            Renderer.PopLayer();
        }
        void RevealCircle(D2DBitmap b, double cx, double cy, double r, int w, int h)
        {
            if (b == null || r <= 0) return;
            int bands = Math.Max(12, Math.Min(56, (int)(r / 8)));
            double bandH = (2.0 * r) / bands;
            double top0 = cy - r;
            for (int i = 0; i <= bands; i++)
            {
                double yTop = top0 + i * bandH;
                double yc = yTop + bandH * 0.5;
                double dy = yc - cy;
                if (Math.Abs(dy) > r) continue;
                double half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
                double l = Math.Max(0, cx - half);
                double rr = Math.Min(w, cx + half);
                double tt = Math.Max(0, yTop);
                double bb = Math.Min(h, yTop + bandH);
                if (rr - l <= 0 || bb - tt <= 0) continue;
                DrawClipped(b, l, tt, rr - l, bb - tt, 0, 0, 1, w, h);
            }
        }
        void RevealBeam(D2DBitmap b, double bx, double thick, int w, int h, double a)
        {
            if (b == null) return;
            double half = thick * 0.5;
            double slant = thick * 1.2;                    // 对角光束偏移
            double x1 = bx - half, x2 = bx + half;
            Renderer.PushQuadLayer((float)x1, 0, (float)x2, 0, (float)(x2 + slant), (float)h, (float)(x1 + slant), (float)h);
            Renderer.DrawImage(b, 0, 0, (float)w, (float)h, ClampA(a));
            Renderer.PopLayer();
            // 光束发光边线
            Renderer.DrawLine((float)x1, 0, (float)(x1 + slant), (float)h, Color.FromArgb(80, 255, 244, 200), (float)Math.Max(2, thick * 0.6));
            Renderer.DrawLine((float)x2, 0, (float)(x2 + slant), (float)h, Color.FromArgb(40, 255, 244, 200), (float)Math.Max(1, thick * 0.3));
        }
        static float ClampA(double a) => (float)Math.Max(0, Math.Min(1, a));

        /// <summary>按风格创建引擎转场实例。</summary>
        public static SceneTransition Create(TransitionStyle style, int durationMs)
        {
            SceneTransition t = style switch
            {
                TransitionStyle.Slide => new SlideTransition(),
                TransitionStyle.Wipe => new WipeTransition(),
                TransitionStyle.CircleReveal => new CircleRevealTransition(),
                TransitionStyle.Beam => new BeamTransition2(),
                _ => new FadeTransition()
            };
            t.DurationMs = Math.Max(1, durationMs);
            return t;
        }

        /// <summary>接管：在 owner 之上弹出一个 TopMost 全屏覆盖窗，用 D2DRenderer 按 SceneTransition 逐帧合成 from→to。</summary>
        public static SceneTransitionOverlay PlayOverlay(Form owner, Bitmap from, Bitmap to, int durationMs, TransitionStyle style)
        {
            var t = Create(style, durationMs);
            var ov = new SceneTransitionOverlay(owner, from, to, durationMs, t);
            ov.Show();
            return ov;
        }
    }

    /// <summary>
    /// 转场覆盖窗：TopMost 无边框 Form（复用既有 UiTransition.cs 的做法），自持 D2DRenderer 与 SceneCompositor，
    /// 内部计时器推进引擎 <see cref="SceneTransition"/>，OnPaint 逐帧合成 from/to；完成后关闭并触发 <see cref="Completed"/>。
    /// </summary>
    public sealed class SceneTransitionOverlay : Form
    {
        readonly SceneTransition _t;
        readonly D2DRenderer _d2d;
        readonly D2DBitmap _from, _to;
        readonly SceneCompositor _compositor;
        readonly System.Windows.Forms.Timer _timer;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly int W, H;
        double _lastMs;
        public event Action Completed;
        bool _finished;

        public SceneTransitionOverlay(Form owner, Bitmap from, Bitmap to, int durationMs, SceneTransition transition)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            if (owner != null) { Location = owner.Location; Size = owner.Size; }
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _t = transition ?? new FadeTransition();
            _t.DurationMs = Math.Max(1, durationMs);
            _t.Start();                                   // 归零计时

            W = Math.Max(1, ClientSize.Width);
            H = Math.Max(1, ClientSize.Height);
            _d2d = new D2DRenderer(Handle, W, H);
            _from = from != null ? _d2d.CreateBitmap(from) : null;
            _to = to != null ? _d2d.CreateBitmap(to) : null;

            _compositor = new SceneCompositor(_t, _d2d) { From = _from, To = _to, Width = W, Height = H };

            _timer = new System.Windows.Forms.Timer { Interval = 15 };
            _timer.Tick += (s, e) =>
            {
                if (_finished) return;
                double now = _clock.ElapsedMilliseconds;
                double dtMs = Math.Max(0, now - _lastMs);
                _lastMs = now;
                _t.Update(dtMs);
                Invalidate();
                if (_t.IsComplete) { _finished = true; Completed?.Invoke(); Close(); }
            };
            WarmUp();      // Show 之前预热首帧：保证覆盖窗首个可见帧是合成画面而不是黑帧
            _timer.Start();
            Invalidate();
        }

        /// <summary>Show 前预热：强制 Begin/合成/提交一帧；Begin 失败（设备丢失/目标重建中）则重试。</summary>
        void WarmUp()
        {
            for (int i = 0; i < 6; i++)
            {
                if (_d2d == null) return;
                if (TryRender()) return;
                System.Threading.Thread.Sleep(2);
            }
        }

        /// <summary>合成提交一帧；Begin 失败返回 false（由 OnPaint 请求下帧重试）。</summary>
        bool TryRender()
        {
            if (_d2d == null || _finished) return false;
            if (!_d2d.Begin()) return false;
            _d2d.Clear(Color.Black);
            _compositor.Draw();
            _d2d.End();
            return true;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }   // 防闪烁
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_finished || _d2d == null) return;
            if (!TryRender())
            {
                Invalidate();   // Begin 失败（D2D 目标重建中）：立即请求重试，避免黑帧残留
                return;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _from?.Dispose();
            _to?.Dispose();
            _d2d?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
