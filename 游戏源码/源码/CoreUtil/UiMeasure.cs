using System;
using System.Drawing;

namespace ChartPlayer
{
    /// <summary>
    /// t29 全文显示：GDI 文本宽度测量 / 字号适配 / 按钮宽（构建期一次性调用，非帧内路径）。
    /// t31：与 GdiDrawAdapter 同口径——emoji run 用 Segoe UI Emoji 真实 advance（实测 ≈2.33em）+ 6px 保底间距；
    /// 字号线性比例适配（GDI 测宽 ≈ 字号线性）。
    /// </summary>
    public static class UiMeasure
    {
        static double GdiWidth(string text, double sizePt, string family = "Microsoft YaHei UI")
        {
            try
            {
                using var f = new Font(family, (float)Math.Max(1, sizePt));
                using var bmp = DpiBitmap.Create(1, 1);   // t78：96 DPI——测量与 GdiDrawAdapter 绘制同口径（否则 MeasureString 按显示器 DPI 放大 1.5×，FitFont/AutoSize 误缩字号）
                using var g = Graphics.FromImage(bmp);
                return g.MeasureString(text, f).Width;
            }
            catch { return text != null ? text.Length * sizePt : 0; }
        }

        /// <summary>文本绘制宽——t31 与 GdiDrawAdapter 同口径：emoji run 用 Segoe UI Emoji 真实 advance（实测 ≈2.33em）
        /// + 6px 保底间距；常规 run 用 Microsoft YaHei UI 测宽。测量=推进=绘制。</summary>
        public static double Width(string text, double sizePt)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (!UiGlyphRuns.ContainsEmoji(text)) return GdiWidth(text, sizePt);
            double w = 0;
            foreach (var run in UiGlyphRuns.Split(text))
                w += GdiWidth(run.Text, sizePt, run.Emoji ? UiGlyphRuns.EmojiFont : UiGlyphRuns.DefaultFont) + (run.Emoji ? 6 : 0);
            return w;
        }

        /// <summary>适配字号：文本宽 ≤ 可用宽则原字号；超宽按比例缩到下限 minFont（t29 下限 8）。</summary>
        public static double FitFont(string text, double availWidth, double baseSize, double minFont = 8)
        {
            if (string.IsNullOrEmpty(text) || availWidth <= 0 || baseSize <= 0) return baseSize;
            double w = Width(text, baseSize);
            if (w <= availWidth) return baseSize;
            double f = baseSize * availWidth / Math.Max(1, w);
            return Math.Max(minFont, f);
        }

        /// <summary>按钮/自适应宽：文本宽 + 左右内边距（t29 按文本 AutoSize 口径；minW 兜底最小可点宽）。</summary>
        public static double ButtonWidth(string text, double sizePt, double padX = 12, double minW = 56, double maxW = 1200)
        {
            double w = Width(text, sizePt) + padX * 2;
            return Math.Max(minW, Math.Min(maxW, w));
        }
    }
}
