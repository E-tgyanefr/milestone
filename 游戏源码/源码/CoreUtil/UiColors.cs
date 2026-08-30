using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ChartPlayer
{
    /// <summary>
    /// Milestone 全局配色（三套主题可切换）：
    ///  0 深空蓝（默认）· 1 极夜紫 · 2 晨光青
    /// 全部字段可写：ApplyTheme(int) 一次性切换整套调色板。
    /// </summary>
    public static class UiColors
    {
        public static Color Bg = Color.FromArgb(10, 14, 22);          // #0a0e16
        public static Color ScreenBg = Color.FromArgb(8, 11, 19);     // #080b13
        public static Color Fg = Color.FromArgb(223, 230, 240);       // #dfe6f0
        public static Color CardBg = Color.FromArgb(20, 29, 49);      // #141d31
        public static Color CardHover = Color.FromArgb(34, 48, 77);   // #22304d
        public static Color CardSel = Color.FromArgb(28, 47, 85);     // #1c2f55
        public static Color Border = Color.FromArgb(34, 48, 77);      // #22304d
        public static Color BorderLight = Color.FromArgb(45, 60, 94); // #2d3c5e
        public static Color InputBg = Color.FromArgb(16, 24, 40);     // #101828
        public static Color InputBorder = Color.FromArgb(42, 58, 90); // #2a3a5a
        public static Color HeadBg = Color.FromArgb(10, 14, 22);      // #0a0e16
        public static Color HeadTitle = Color.FromArgb(157, 180, 232);// #9db4e8
        public static Color SubText = Color.FromArgb(127, 147, 187);  // #7f93bb
        public static Color Artist = Color.FromArgb(143, 163, 200);   // #8fa3c8
        public static Color BodyText = Color.FromArgb(184, 198, 221); // #b8c6dd
        public static Color Muted = Color.FromArgb(159, 178, 208);    // #9fb2d0
        public static Color Dim = Color.FromArgb(107, 126, 160);      // #6b7ea0
        public static Color Green = Color.FromArgb(127, 208, 160);    // #7fd0a0
        public static Color GreenBg = Color.FromArgb(29, 92, 55);     // #1d5c37
        public static Color GreenBorder = Color.FromArgb(47, 138, 86);// #2f8a56
        public static Color Blue = Color.FromArgb(61, 123, 255);      // #3d7bff
        public static Color BlueBtn = Color.FromArgb(45, 108, 255);   // #2d6cff
        public static Color BtnBg = Color.FromArgb(28, 39, 64);       // #1c2740
        public static Color BtnHover = Color.FromArgb(39, 53, 90);    // #27355a
        public static Color TabBg = Color.FromArgb(22, 32, 58);       // #16203a
        public static Color TabBorder = Color.FromArgb(36, 50, 71);   // #243247
        public static Color Gold = Color.FromArgb(255, 210, 63);      // #ffd23f
        public static Color Accent = Color.FromArgb(61, 123, 255);    // #3d7bff
        public static int CardRadius = 10;                            // ⑨ 统一主题令牌：卡片圆角半径（px，各视图共用）

        /// <summary>切换整套调色板（0 深空蓝 / 1 极夜紫 / 2 晨光青），越界自动钳制。</summary>
        public static void ApplyTheme(int theme)
        {
            theme = Math.Max(0, Math.Min(2, theme));
            switch (theme)
            {
                case 1: SetViolet(); break;
                case 2: SetCyan(); break;
                default: SetDeepSpace(); break;
            }
        }

        static void SetDeepSpace()
        {
            Bg = Color.FromArgb(10, 14, 22);
            ScreenBg = Color.FromArgb(8, 11, 19);
            Fg = Color.FromArgb(223, 230, 240);
            CardBg = Color.FromArgb(20, 29, 49);
            CardHover = Color.FromArgb(34, 48, 77);
            CardSel = Color.FromArgb(28, 47, 85);
            Border = Color.FromArgb(34, 48, 77);
            BorderLight = Color.FromArgb(45, 60, 94);
            InputBg = Color.FromArgb(16, 24, 40);
            InputBorder = Color.FromArgb(42, 58, 90);
            HeadBg = Color.FromArgb(10, 14, 22);
            HeadTitle = Color.FromArgb(157, 180, 232);
            SubText = Color.FromArgb(127, 147, 187);
            Artist = Color.FromArgb(143, 163, 200);
            BodyText = Color.FromArgb(184, 198, 221);
            Muted = Color.FromArgb(159, 178, 208);
            Dim = Color.FromArgb(107, 126, 160);
            Green = Color.FromArgb(127, 208, 160);
            GreenBg = Color.FromArgb(29, 92, 55);
            GreenBorder = Color.FromArgb(47, 138, 86);
            Blue = Color.FromArgb(61, 123, 255);
            BlueBtn = Color.FromArgb(45, 108, 255);
            BtnBg = Color.FromArgb(28, 39, 64);
            BtnHover = Color.FromArgb(39, 53, 90);
            TabBg = Color.FromArgb(22, 32, 58);
            TabBorder = Color.FromArgb(36, 50, 71);
            Gold = Color.FromArgb(255, 210, 63);
            Accent = Color.FromArgb(61, 123, 255);
        }

        static void SetViolet()
        {
            Bg = Color.FromArgb(18, 16, 31);          // #12101f
            ScreenBg = Color.FromArgb(15, 13, 26);    // #0f0d1a
            Fg = Color.FromArgb(230, 227, 242);
            CardBg = Color.FromArgb(28, 25, 48);
            CardHover = Color.FromArgb(42, 37, 71);
            CardSel = Color.FromArgb(47, 40, 82);
            Border = Color.FromArgb(42, 37, 71);
            BorderLight = Color.FromArgb(58, 52, 92);
            InputBg = Color.FromArgb(24, 21, 39);
            InputBorder = Color.FromArgb(58, 51, 88);
            HeadBg = Color.FromArgb(18, 16, 31);
            HeadTitle = Color.FromArgb(184, 169, 255);
            SubText = Color.FromArgb(143, 132, 196);
            Artist = Color.FromArgb(162, 150, 214);
            BodyText = Color.FromArgb(201, 195, 232);
            Muted = Color.FromArgb(168, 159, 208);
            Dim = Color.FromArgb(122, 111, 176);
            Green = Color.FromArgb(143, 224, 168);
            GreenBg = Color.FromArgb(29, 58, 44);
            GreenBorder = Color.FromArgb(47, 106, 78);
            Blue = Color.FromArgb(122, 92, 255);       // #7a5cff
            BlueBtn = Color.FromArgb(106, 76, 245);
            BtnBg = Color.FromArgb(35, 31, 60);
            BtnHover = Color.FromArgb(51, 44, 88);
            TabBg = Color.FromArgb(30, 26, 54);
            TabBorder = Color.FromArgb(51, 45, 82);
            Gold = Color.FromArgb(255, 210, 63);
            Accent = Color.FromArgb(122, 92, 255);
        }

        static void SetCyan()
        {
            Bg = Color.FromArgb(12, 23, 22);          // #0c1716
            ScreenBg = Color.FromArgb(10, 19, 18);    // #0a1312
            Fg = Color.FromArgb(226, 243, 241);
            CardBg = Color.FromArgb(18, 38, 33);
            CardHover = Color.FromArgb(28, 56, 50);
            CardSel = Color.FromArgb(28, 68, 60);
            Border = Color.FromArgb(28, 56, 50);
            BorderLight = Color.FromArgb(42, 82, 74);
            InputBg = Color.FromArgb(15, 32, 29);
            InputBorder = Color.FromArgb(42, 80, 72);
            HeadBg = Color.FromArgb(12, 23, 22);
            HeadTitle = Color.FromArgb(143, 224, 208);
            SubText = Color.FromArgb(111, 179, 166);
            Artist = Color.FromArgb(130, 201, 187);
            BodyText = Color.FromArgb(188, 222, 214);
            Muted = Color.FromArgb(156, 201, 192);
            Dim = Color.FromArgb(107, 156, 146);
            Green = Color.FromArgb(143, 224, 192);
            GreenBg = Color.FromArgb(18, 69, 58);
            GreenBorder = Color.FromArgb(47, 138, 114);
            Blue = Color.FromArgb(25, 200, 176);       // #19c8b0
            BlueBtn = Color.FromArgb(20, 184, 162);
            BtnBg = Color.FromArgb(20, 48, 43);
            BtnHover = Color.FromArgb(29, 68, 60);
            TabBg = Color.FromArgb(18, 43, 38);
            TabBorder = Color.FromArgb(31, 64, 57);
            Gold = Color.FromArgb(255, 210, 63);
            Accent = Color.FromArgb(25, 200, 176);
        }

        /// <summary>颜色线性插值（t 自动钳制到 0~1）。</summary>
        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>垂直渐变填充（c1 在上，c2 在下）。</summary>
        public static void Gradient(Graphics g, Rectangle rect, Color c1, Color c2)
        {
            if (g == null || rect.Width <= 0 || rect.Height <= 0) return;
            using (var b = new LinearGradientBrush(rect, c1, c2, LinearGradientMode.Vertical))
                g.FillRectangle(b, rect);
        }
    }
}
