using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ChartPlayer
{
    /// <summary>
    /// IUiDraw 的 GDI+（软件渲染）实现：离屏截图 / 无 GPU 环境 / 测试保底用。
    /// 与 D2DDrawAdapter 同一接口约定：Text 的 (x,y) 为左上锚点；Image 支持 NativeHandle 为 System.Drawing.Image。
    /// 引擎 UI 组件只依赖 IUiDraw 绘制——换后端（D2D/GDI）无需改动任何 UI 组件代码（重做 UI 的渲染可移植性证据）。
    /// t9 性能：字体/实心刷/画笔改静态缓存（有界 LRU 式清空），消除"每图元/每帧 new Font/SolidBrush/Pen"
    /// （引擎壳 16ms 定时器全帧重绘时的头号 CPU/GC 来源）；纯文本走单字体快速路径（免 run-split 分配）。
    /// </summary>
    public sealed class GdiDrawAdapter : IUiDraw
    {
        readonly Graphics _g;
        readonly Stack<RectangleF> _clips = new Stack<RectangleF>();

        // ---- t9：静态 GDI 对象缓存（进程级、有界）----
        static readonly object _cacheLock = new object();
        static readonly Dictionary<(string family, float size), Font> _fontCache = new Dictionary<(string, float), Font>();
        static readonly Dictionary<int, SolidBrush> _brushCache = new Dictionary<int, SolidBrush>();
        static readonly Dictionary<(int argb, float width), Pen> _penCache = new Dictionary<(int argb, float), Pen>();
        const int MaxCacheEntries = 256;

        public GdiDrawAdapter(Graphics g) { _g = g; }

        static Color ToColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        /// <summary>字体缓存：按（字体族, 字号 0.25px 桶）复用；超过上限整表清空（有界，进程生命周期托管）。</summary>
        static Font GetFont(string family, float sizePx)
        {
            float bucket = (float)Math.Round(sizePx * 4.0) / 4.0f;
            var key = (family, bucket);
            lock (_cacheLock)
            {
                if (_fontCache.TryGetValue(key, out var f)) return f;
                if (_fontCache.Count >= MaxCacheEntries) _fontCache.Clear();
                Font created;
                try { created = new Font(family, sizePx, GraphicsUnit.Point); }
                catch { created = new Font(UiGlyphRuns.DefaultFont, sizePx, GraphicsUnit.Point); }
                _fontCache[key] = created;
                return created;
            }
        }

        /// <summary>实心刷缓存：按 ARGB 复用；超过上限整表清空（刷对象不与 Graphics 绑定，可跨帧共享）。</summary>
        static SolidBrush GetBrush(RgbaColor c)
        {
            int argb = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            lock (_cacheLock)
            {
                if (_brushCache.TryGetValue(argb, out var b)) return b;
                if (_brushCache.Count >= MaxCacheEntries) _brushCache.Clear();
                var created = new SolidBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                _brushCache[argb] = created;
                return created;
            }
        }

        /// <summary>画笔缓存：按（ARGB, 线宽 0.25px 桶）复用；有界。</summary>
        static Pen GetPen(RgbaColor c, double thickness)
        {
            int argb = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            float width = (float)Math.Max(0.5, thickness);
            float bucket = (float)Math.Round(width * 4.0) / 4.0f;
            var key = (argb, bucket);
            lock (_cacheLock)
            {
                if (_penCache.TryGetValue(key, out var p)) return p;
                if (_penCache.Count >= MaxCacheEntries) _penCache.Clear();
                var created = new Pen(Color.FromArgb(c.A, c.R, c.G, c.B), width);
                _penCache[key] = created;
                return created;
            }
        }

        public void Rect(double x, double y, double w, double h, RgbaColor c)
        {
            _g.FillRectangle(GetBrush(c), (float)x, (float)y, (float)w, (float)h);
        }

        /// <summary>圆角路径复用（t9）：线程静态 GraphicsPath 每调用 Reset 后重建——
        /// 消除"每圆角图元每帧 new GraphicsPath"的托管分配（引擎壳每帧数十卡片/按钮圆角）。</summary>
        [ThreadStatic] static GraphicsPath _rrectPath;
        static GraphicsPath GetRoundedPath()
        {
            var p = _rrectPath;
            if (p == null) { p = new GraphicsPath(); _rrectPath = p; }
            else p.Reset();
            return p;
        }

        public void RoundedRect(double x, double y, double w, double h, double radius, RgbaColor c)
        {
            float r = (float)Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2.0));
            var rect = new RectangleF((float)x, (float)y, (float)w, (float)h);
            var path = GetRoundedPath();
            path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            _g.FillPath(GetBrush(c), path);
        }

        /// <summary>按 Unicode 范围 run-split 混合字体绘制：常规→Microsoft YaHei UI；emoji/符号→Segoe UI Emoji（t56）。
        /// 逐 run 同字号/同基线（左上锚点）；monochrome 字形，前景色不变；只改字体回退，不改文本/坐标。
        /// t9：纯文本（无 emoji 码点）走整段单字体快速路径（零 run-split 分配）。</summary>
        public void Text(string s, double x, double y, double size, RgbaColor c)
        {
            if (string.IsNullOrEmpty(s)) return;
            float sz = (float)Math.Max(1, size);
            var b = GetBrush(c);
            double penX = x;
            if (!UiGlyphRuns.ContainsEmoji(s))
            {
                _g.DrawString(s, GetFont(UiGlyphRuns.DefaultFont, sz), b, (float)penX, (float)y);
                return;
            }
            foreach (var run in UiGlyphRuns.Split(s))
            {
                var f = GetFont(UiGlyphRuns.FontFor(run.Emoji), sz);
                _g.DrawString(run.Text, f, b, (float)penX, (float)y);
                // t31：推进=本 run 在自身字体（emoji=Segoe UI Emoji）下的真实 advance（实测 ≈2.33em @20pt，旧 1.15em 估宽低估致图标压字）；
                // emoji run 末尾 +6px 保底间距（任意缩放下图标-文字 gap≥6）；t34：scale<1 窄档再加 UiGlyphRuns.EmojiGapBoost(+2)
                penX += _g.MeasureString(run.Text, f).Width + (run.Emoji ? 6 + UiGlyphRuns.EmojiGapBoost : 0);
            }
        }

        public void Ellipse(double cx, double cy, double rx, double ry, RgbaColor c)
        {
            _g.FillEllipse(GetBrush(c), (float)(cx - rx), (float)(cy - ry), (float)(rx * 2), (float)(ry * 2));
        }

        public void Line(double x1, double y1, double x2, double y2, RgbaColor c, double thickness = 1)
        {
            _g.DrawLine(GetPen(c, thickness), (float)x1, (float)y1, (float)x2, (float)y2);
        }

        public void Image(UiImage image, double x, double y, double w, double h, double opacity = 1)
        {
            if (image?.NativeHandle is not Image img) return;
            var dst = new RectangleF((float)x, (float)y, (float)w, (float)h);
            double op = Math.Max(0, Math.Min(1, opacity));
            if (op < 0.999)
            {
                var cm = new ColorMatrix
                {
                    Matrix00 = 1, Matrix11 = 1, Matrix22 = 1,
                    Matrix33 = (float)op, Matrix44 = 1
                };
                using var attr = new ImageAttributes();
                attr.SetColorMatrix(cm);
                _g.DrawImage(img, new Rectangle((int)x, (int)y, (int)w, (int)h), 0f, 0f, img.Width, img.Height, GraphicsUnit.Pixel, attr);
            }
            else
            {
                _g.DrawImage(img, dst);
            }
        }

        public void PushClip(double x, double y, double w, double h)
        {
            var rect = new RectangleF((float)x, (float)y, (float)w, (float)h);
            _clips.Push(rect);
            _g.SetClip(rect, CombineMode.Intersect);
        }

        public void PopClip()
        {
            if (_clips.Count == 0) return;
            _clips.Pop();
            if (_clips.Count == 0) _g.ResetClip();
            else _g.SetClip(_clips.Peek(), CombineMode.Replace);
        }

        /// <summary>与 Text 同 run-split 口径测量宽度（避免换行/截断/居中偏差）：按 run 字体逐段测量求和。
        /// t9：纯文本整段单字体快速路径（零 run-split 分配，字体走静态缓存）。</summary>
        public double MeasureText(string s, double size)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            float sz = (float)Math.Max(1, size);
            double w = 0;
            if (!UiGlyphRuns.ContainsEmoji(s))
            {
                w += _g.MeasureString(s, GetFont(UiGlyphRuns.DefaultFont, sz)).Width;
                return w;
            }
            foreach (var run in UiGlyphRuns.Split(s))
            {
                var f = GetFont(UiGlyphRuns.FontFor(run.Emoji), sz);
                // t31：测量=推进=绘制同口径（emoji 用 Segoe UI Emoji 真 advance + 6px 保底间距）；t34：+EmojiGapBoost（scale<1 窄档）
                w += _g.MeasureString(run.Text, f).Width + (run.Emoji ? 6 + UiGlyphRuns.EmojiGapBoost : 0);
            }
            return w;
        }
    }
}
