using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 转场基类：覆盖在主窗体之上的顶层窗口，负责计时器与关闭/资源释放。
    /// 子类只需实现 OnPaint 画一帧过渡画面（t 从 0→1）。
    /// 注意：构造后由本窗体接管 from/to 两张位图的所有权，关闭时统一释放。
    /// </summary>
    public abstract class TransitionBase : Form
    {
        protected readonly Bitmap From, To;
        protected readonly int Duration;
        protected readonly System.Windows.Forms.Timer Timer;
        protected readonly Stopwatch Clock = Stopwatch.StartNew();
        protected bool Finished;
        protected readonly int W, H;

        protected TransitionBase(Form owner, Bitmap from, Bitmap to, int durationMs)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            if (owner != null) { Location = owner.Location; Size = owner.Size; }
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            From = from; To = to;
            Duration = Math.Max(260, durationMs);
            W = Math.Max(1, ClientSize.Width);
            H = Math.Max(1, ClientSize.Height);
            Timer = new System.Windows.Forms.Timer { Interval = 15 };
            Timer.Tick += (s, e) =>
            {
                if (Finished) return;
                Invalidate();
                if (Clock.ElapsedMilliseconds >= Duration) { Finished = true; Close(); }
            };
            Timer.Start();
            Invalidate();
        }

        /// <summary>当前进度 0~1。</summary>
        protected double T => Math.Min(1.0, Clock.ElapsedMilliseconds / (double)Duration);

        /// <summary>平滑缓动（ease-in-out）。</summary>
        protected static double Smooth(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x * x * (3 - 2 * x);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Timer.Stop();
            Timer.Dispose();
            DisposeResources();
            base.OnFormClosed(e);
        }

        protected virtual void DisposeResources()
        {
            From?.Dispose();
            To?.Dispose();
        }
    }

    /// <summary>
    /// 场景切换「光束劈裂」转场（GPU/D2D 渲染）：
    ///  1. 一道光束从任意方向斜劈划过屏幕（旧页面开始变暗）
    ///  2. 光束绕屏幕中心旋转 0→360°
    ///  3. 从光束位置把旧画面裂开到两边，新页面自劈开瞬间渐入
    /// </summary>
    public sealed class BeamTransition : TransitionBase
    {
        readonly IRenderer _d2d;
        D2DBitmap _d2dFrom, _d2dTo;
        readonly double _theta0;

        public BeamTransition(Form owner, Bitmap from, Bitmap to, int durationMs) : base(owner, from, to, durationMs)
        {
            var rnd = new Random();
            double baseAng = (35 + rnd.NextDouble() * 30) * Math.PI / 180.0;
            if (rnd.Next(2) == 0) baseAng = Math.PI - baseAng;
            if (rnd.Next(2) == 0) baseAng = -baseAng;
            _theta0 = baseAng;

            _d2d = new D2DRenderer(Handle, W, H);
            _d2dFrom = _d2d.CreateBitmap(from);
            _d2dTo = _d2d.CreateBitmap(to);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_d2d == null || Finished) return;
            double t = Math.Min(1.0, Clock.ElapsedMilliseconds / (double)Duration);

            double cx = W / 2.0, cy = H / 2.0;
            double diag = Math.Sqrt((double)W * W + H * H);
            double half = diag / 2 + 120;

            double slashP = Smooth(Math.Min(1, t / 0.22));
            double rotP = t <= 0.22 ? 0 : t >= 0.50 ? 1 : Smooth((t - 0.22) / 0.28);
            double splitP = t <= 0.50 ? 0 : Smooth((t - 0.50) / 0.50);
            double newAlpha = Smooth(Math.Max(0, Math.Min(1, (t - 0.22) / 0.78)));
            double oldBright = 1.0 - 0.88 * Smooth(t);

            double angle = _theta0 + rotP * Math.PI * 2.0;
            double dirX = Math.Cos(angle), dirY = Math.Sin(angle);
            double nX = -dirY, nY = dirX;
            double gap = splitP * diag * 0.62;
            double beamAlpha = t > 0.5 ? Math.Max(0, 1 - (t - 0.5) / 0.12) : 1;

            if (!_d2d.Begin()) return;

            if (_d2dTo != null)
                _d2d.DrawImage(_d2dTo, 0, 0, W, H, (float)newAlpha);

            if (_d2dFrom != null && splitP > 0.001)
            {
                double off = gap / 2;
                float ax1 = (float)(cx - dirX * half + nX * off + dirX * 1e4);
                float ay1 = (float)(cy - dirY * half + nY * off + dirY * 1e4);
                float ax2 = (float)(cx + dirX * half + nX * off + dirX * 1e4);
                float ay2 = (float)(cy + dirY * half + nY * off + dirY * 1e4);
                float ax3 = (float)(cx + dirX * half + nX * off - dirX * 1e4);
                float ay3 = (float)(cy + dirY * half + nY * off - dirY * 1e4);
                float ax4 = (float)(cx - dirX * half + nX * off - dirX * 1e4);
                float ay4 = (float)(cy - dirY * half + nY * off - dirY * 1e4);
                _d2d.PushQuadLayer(ax1, ay1, ax2, ay2, ax3, ay3, ax4, ay4);
                _d2d.DrawImage(_d2dFrom, (float)(nX * off), (float)(nY * off), W, H, (float)oldBright);
                _d2d.PopLayer();

                float bx1 = (float)(cx - dirX * half - nX * off + dirX * 1e4);
                float by1 = (float)(cy - dirY * half - nY * off + dirY * 1e4);
                float bx2 = (float)(cx + dirX * half - nX * off + dirX * 1e4);
                float by2 = (float)(cy + dirY * half - nY * off + dirY * 1e4);
                float bx3 = (float)(cx + dirX * half - nX * off - dirX * 1e4);
                float by3 = (float)(cy + dirY * half - nY * off - dirY * 1e4);
                float bx4 = (float)(cx - dirX * half - nX * off - dirX * 1e4);
                float by4 = (float)(cy - dirY * half - nY * off - dirY * 1e4);
                _d2d.PushQuadLayer(bx1, by1, bx2, by2, bx3, by3, bx4, by4);
                _d2d.DrawImage(_d2dFrom, (float)(-nX * off), (float)(-nY * off), W, H, (float)oldBright);
                _d2d.PopLayer();
            }
            else if (_d2dFrom != null)
            {
                _d2d.DrawImage(_d2dFrom, 0, 0, W, H, (float)oldBright);
            }

            Color core = Color.FromArgb((int)(255 * beamAlpha), 255, 244, 200);
            Color glow1 = Color.FromArgb((int)(70 * beamAlpha), 255, 210, 110);
            Color glow2 = Color.FromArgb((int)(30 * beamAlpha), 255, 190, 90);
            if (splitP <= 0.001)
            {
                double len = (t <= 0.22 ? slashP : 1) * half;
                float bx1 = (float)(cx - dirX * len), by1 = (float)(cy - dirY * len);
                float bx2 = (float)(cx + dirX * len), by2 = (float)(cy + dirY * len);
                _d2d.DrawLine(bx1, by1, bx2, by2, glow2, 26f);
                _d2d.DrawLine(bx1, by1, bx2, by2, glow1, 12f);
                _d2d.DrawLine(bx1, by1, bx2, by2, core, 3.5f);
            }
            else
            {
                double dirSX = Math.Cos(_theta0), dirSY = Math.Sin(_theta0);
                double nSX = -dirSY, nSY = dirSX;
                double off = gap / 2;
                Color edge = Color.FromArgb(Math.Min(255, (int)(255 * (1 - splitP * 0.5))), 255, 220, 140);
                for (int s = -1; s <= 1; s += 2)
                {
                    float ex1 = (float)(cx - dirSX * half + nSX * off * s);
                    float ey1 = (float)(cy - dirSY * half + nSY * off * s);
                    float ex2 = (float)(cx + dirSX * half + nSX * off * s);
                    float ey2 = (float)(cy + dirSY * half + nSY * off * s);
                    _d2d.DrawLine(ex1, ey1, ex2, ey2, Color.FromArgb(40, 255, 200, 120), 14f);
                    _d2d.DrawLine(ex1, ey1, ex2, ey2, edge, 3f);
                }
            }

            _d2d.End();
        }

        protected override void DisposeResources()
        {
            _d2dFrom?.Dispose();
            _d2dTo?.Dispose();
            _d2d?.Dispose();
            base.DisposeResources();
        }
    }

    /// <summary>
    /// 圆环揭示转场（风格 1）：发光圆环从中心扩大，圈内露出新画面，旧画面在外围渐暗。
    /// </summary>
    public sealed class RingTransition : TransitionBase
    {
        public RingTransition(Form owner, Bitmap from, Bitmap to, int durationMs) : base(owner, from, to, durationMs) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            double t = T;
            double ease = Smooth(t);
            double maxR = Math.Sqrt((double)W * W + H * H) * 0.5 + 12;
            double r = Math.Max(0, ease * maxR);

            // 旧画面：全屏 + 渐暗
            if (From != null) g.DrawImage(From, 0, 0, W, H);
            using (var dim = new SolidBrush(Color.FromArgb((int)(190 * ease), 0, 0, 0)))
                g.FillRectangle(dim, 0, 0, W, H);

            // 新画面：按圆形裁剪渐显
            if (To != null)
            {
                var state = g.Save();
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse((float)(W / 2.0 - r), (float)(H / 2.0 - r), (float)(r * 2), (float)(r * 2));
                    g.SetClip(clip, CombineMode.Replace);
                    g.DrawImage(To, 0, 0, W, H);
                }
                g.Restore(state);
            }

            // 发光圆环
            using (var glow = new Pen(Color.FromArgb(70, UiColors.Gold), 16f))
                g.DrawEllipse(glow, (float)(W / 2.0 - r), (float)(H / 2.0 - r), (float)(r * 2), (float)(r * 2));
            using (var ring = new Pen(Color.FromArgb(230, UiColors.Gold), 3f))
                g.DrawEllipse(ring, (float)(W / 2.0 - r), (float)(H / 2.0 - r), (float)(r * 2), (float)(r * 2));
        }
    }

    /// <summary>
    /// 推拉转场（风格 2）：新页面从右侧滑入覆盖，旧页面左移 15% 并变暗（ease-out-cubic）。
    /// </summary>
    public sealed class PushTransition : TransitionBase
    {
        public PushTransition(Form owner, Bitmap from, Bitmap to, int durationMs) : base(owner, from, to, durationMs) { }

        static double EaseOutCubic(double x)
        {
            double i = 1 - x;
            return 1 - i * i * i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            double t = T;
            double ease = EaseOutCubic(t);
            int shift = (int)(W * 0.15 * ease);

            // 旧画面：左移 + 渐暗
            if (From != null) g.DrawImage(From, -shift, 0, W, H);
            using (var dim = new SolidBrush(Color.FromArgb((int)(150 * ease), 0, 0, 0)))
                g.FillRectangle(dim, 0, 0, W, H);

            // 新画面：从右侧滑入
            if (To != null)
            {
                int x = W - (int)(W * ease);
                g.DrawImage(To, x, 0, W, H);
            }
        }
    }
}
