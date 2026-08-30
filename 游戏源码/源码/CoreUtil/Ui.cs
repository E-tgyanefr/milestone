using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 高 DPI 适配工具：所有固定像素尺寸经 P()/S()/Pt() 换算；
    /// 字号使用 pt（由系统按显示器 DPI 自动缩放）。
    /// </summary>
    public static class Ui
    {
        public static readonly float DpiScale;

        static Ui()
        {
            try
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                DpiScale = Math.Max(0.8f, Math.Min(2.5f, g.DpiX / 96f));
            }
            catch { DpiScale = 1f; }
        }

        public static int P(int v) => (int)Math.Round(v * DpiScale);
        public static Size S(int w, int h) => new Size(P(w), P(h));
        public static Point Pt(int x, int y) => new Point(P(x), P(y));
        public static Padding Pad(int l, int t, int r, int b) => new Padding(P(l), P(t), P(r), P(b));

        /// <summary>颜色增亮（f&gt;1）或变暗（f&lt;1）。</summary>
        public static Color Tint(Color c, double f)
        {
            int R = Math.Min(255, (int)(c.R * f));
            int G = Math.Min(255, (int)(c.G * f));
            int B = Math.Min(255, (int)(c.B * f));
            return Color.FromArgb(c.A, R, G, B);
        }

        /// <summary>给按钮加悬停高亮：悬停增亮、按下变暗、手型光标（保留原配色风格）。</summary>
        public static void Hover(Button b)
        {
            if (b == null) return;
            var baseBg = b.BackColor;
            b.FlatAppearance.MouseOverBackColor = Tint(baseBg, 1.28);
            b.FlatAppearance.MouseDownBackColor = Tint(baseBg, 0.9);
            b.Cursor = Cursors.Hand;
        }

        /// <summary>圆角矩形路径（自绘控件通用）。</summary>
        public static System.Drawing.Drawing2D.GraphicsPath Rounded(System.Drawing.Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
