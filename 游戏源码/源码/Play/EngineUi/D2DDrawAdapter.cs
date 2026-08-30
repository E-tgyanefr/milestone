using System;
using System.Drawing;

namespace ChartPlayer
{
    /// <summary>
    /// 引擎 <see cref="IUiDraw"/> → 宿主 <see cref="IRenderer"/>（D2DRenderer）适配器。
    /// 重做 UI 的渲染抽象：把引擎侧 RgbaColor / UiImage 统一映射到宿主渲染后端，
    /// 供引擎驱动的场景 UI（UiElement 控件树）在宿主 Begin()…End() 帧内绘制。
    ///  - RgbaColor → System.Drawing.Color
    ///  - Rect/RoundedRect/Text/Ellipse/Line/Image/PushClip/PopClip/MeasureText 逐一对齐 IRenderer 的 D2D 方法
    /// </summary>
    public sealed class D2DDrawAdapter : IUiDraw
    {
        readonly IRenderer _r;

        public D2DDrawAdapter(IRenderer renderer)
        {
            _r = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        /// <summary>已绑定的后端渲染器（IRenderer）。</summary>
        public IRenderer Renderer => _r;

        static Color ToColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        public void Rect(double x, double y, double w, double h, RgbaColor c)
            => _r.FillRect((float)x, (float)y, (float)w, (float)h, ToColor(c));

        public void RoundedRect(double x, double y, double w, double h, double radius, RgbaColor c)
            => _r.FillRoundedRect((float)x, (float)y, (float)w, (float)h, (float)radius, ToColor(c));

        /// <summary>
        /// 文本（(x,y) = 左上锚点）：以 IUiDraw 无宽高/居中参数，故先 MeasureText 求宽，
        /// 再按 D2DRenderer.Text 的“cx=左缘、cy=垂直中心”语义换算，保持左上脚对齐。
        /// 控件需要居中/右对齐时自行按 MeasureText 换算 x。
        /// </summary>
        /// <summary>按 Unicode 范围 run-split 混合字体绘制：常规→Microsoft YaHei UI；emoji/符号→Segoe UI Emoji（t56）。
        /// 逐 run 同字号/同基线（左上锚点）；monochrome 字形，前景色不变；只改字体回退，不改文本/坐标。</summary>
        public void Text(string s, double x, double y, double size, RgbaColor c)
        {
            if (string.IsNullOrEmpty(s) || _r == null) return;
            float sz = (float)size;
            float h = Math.Max(sz + 6f, sz * 1.5f);
            float cy = (float)(y + h * 0.5f);
            double penX = x;
            // t9：纯文本整段单字体快速路径（零 run-split 分配）
            if (!UiGlyphRuns.ContainsEmoji(s))
            {
                string fam = UiGlyphRuns.DefaultFont;
                float w = _r.MeasureText(s, sz, fam);
                _r.Text(s, (float)penX, cy, w, h, ToColor(c), sz, false, fam);
                return;
            }
            foreach (var run in UiGlyphRuns.Split(s))
            {
                string fam = UiGlyphRuns.FontFor(run.Emoji);
                // t31：推进=TextLayout 在该字体（emoji=Segoe UI Emoji）下的真实 advance（旧 1.15em 估宽低估致图标压字）；
                // emoji run 末尾 +6px 保底间距（任意缩放下图标-文字 gap≥6）；t34：scale<1 窄档再加 EmojiGapBoost(+2)
                float w = _r.MeasureText(run.Text, sz, fam) + (run.Emoji ? 6f + (float)UiGlyphRuns.EmojiGapBoost : 0f);
                _r.Text(run.Text, (float)penX, cy, w, h, ToColor(c), sz, false, fam);
                penX += w;
            }
        }

        public void Ellipse(double cx, double cy, double rx, double ry, RgbaColor c)
            => _r.FillEllipse((float)cx, (float)cy, (float)rx, (float)ry, ToColor(c));

        public void Line(double x1, double y1, double x2, double y2, RgbaColor c, double thickness = 1)
            => _r.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, ToColor(c), (float)thickness, 0);

        /// <summary>绘制图像：UiImage.NativeHandle 必须是宿主 <see cref="D2DBitmap"/>（由 CreateBitmap 生成）。</summary>
        public void Image(UiImage image, double x, double y, double w, double h, double opacity = 1)
        {
            if (image?.NativeHandle is D2DBitmap bmp)
                _r.DrawImage(bmp, (float)x, (float)y, (float)w, (float)h, (float)Math.Max(0, Math.Min(1, opacity)));
        }

        /// <summary>矩形裁剪：用宿主 PushQuadLayer（把矩形表示为 4 点几何遮罩）。可嵌套，与 PopClip 配对。</summary>
        public void PushClip(double x, double y, double w, double h)
            => _r.PushQuadLayer((float)x, (float)y, (float)(x + w), (float)y, (float)(x + w), (float)(y + h), (float)x, (float)(y + h));

        public void PopClip() => _r.PopLayer();

        /// <summary>与 Text 同 run-split 口径测量宽度（避免换行/截断/居中偏差）：按 run 字体逐段测量求和。</summary>
        public double MeasureText(string s, double size)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            float sz = (float)size;
            double w = 0;
            // t9：纯文本整段单字体快速路径（零 run-split 分配）
            if (!UiGlyphRuns.ContainsEmoji(s))
                return _r.MeasureText(s, sz, UiGlyphRuns.DefaultFont);
            foreach (var run in UiGlyphRuns.Split(s))
            {
                // t31：测量=推进=绘制同口径（emoji 用 Segoe UI Emoji 真 advance + 6px 保底间距）；t34：+EmojiGapBoost（scale<1 窄档）
                w += _r.MeasureText(run.Text, sz, UiGlyphRuns.FontFor(run.Emoji)) + (run.Emoji ? 6 + UiGlyphRuns.EmojiGapBoost : 0);
            }
            return w;
        }
    }
}
