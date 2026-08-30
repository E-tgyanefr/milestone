using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace ChartPlayer
{
    /* ================= 音符设计取证：Milestone.exe --noteshot "<outDir>" =================
     * 离屏渲染每模式 1 张设计卡（判定线 + 典型音符组合 tap/hold/特殊 + 接近动画帧），
     * 共 9 张 noteshot_<mode>.png + noteshot.log；0=全部通过 / 1=失败。
     * 颜色全部取自 NoteStyleBook.Get(mode)（与 GamePanel t25 接入同源），形状为示意绘制。
     */

    public static class NoteshotDemo
    {
        public const string Entry = "--noteshot";

        /// <summary>取证的 9 个可玩模式（ADOFAI 两体（Routlock/真）合并为 1 张；Style 键 adofai/adofai2 均已收录）。</summary>
        public static readonly GameMode[] ShotModes =
        {
            GameMode.Mania, GameMode.Maimai, GameMode.Phigros, GameMode.Arcaea, GameMode.Cytus,
            GameMode.OsuStandard, GameMode.Adofai, GameMode.Iidx, GameMode.LoopComposer
        };

        public static int Run(string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir ?? "obj/noteshot");
                var log = new StringBuilder();
                log.AppendLine("===== Milestone --noteshot =====");
                int okCount = 0;
                foreach (var mode in ShotModes)
                {
                    string id = ModeSystem.ModeId(mode);
                    try
                    {
                        string path = Path.Combine(outDir, "noteshot_" + id + ".png");
                        bool saved = Render(mode, path, log);
                        if (saved) okCount++;
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine("❌ " + id + "：" + ex.Message);
                    }
                }
                log.AppendLine(okCount == ShotModes.Length
                    ? "===== --noteshot 全部通过（" + okCount + "/" + ShotModes.Length + " 张非空 PNG） ====="
                    : "===== --noteshot 未全部通过（" + okCount + "/" + ShotModes.Length + "） =====");
                File.WriteAllText(Path.Combine(outDir, "noteshot.log"), log.ToString(), new System.Text.UTF8Encoding(true));
                Console.WriteLine(log.ToString());
                return okCount == ShotModes.Length ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("===== --noteshot 失败 =====" + ex);
                try { File.WriteAllText(Path.Combine(outDir ?? ".", "noteshot.log"), "失败：" + ex); } catch { }
                return 1;
            }
        }

        /// <summary>渲染单模式设计卡；返回是否保存成功且非纯黑（像素采样）。</summary>
        static bool Render(GameMode mode, string path, StringBuilder log)
        {
            const int W = 960, H = 540;
            var style = NoteStyleBook.Get(ModeSystem.ModeId(mode));
            string display = ModeSystem.DisplayName(mode);

            using var bmp = DpiBitmap.Create(W, H);   // t78：96 DPI——设计卡文本按逻辑尺寸光栅化（高 DPI 屏 1.5× 放大与 960×540 布局不符）
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var d = new GdiDrawAdapter(g);

                // 背景（暗色双段）
                d.Rect(0, 0, W, H, new RgbaColor(14, 18, 28, 255));
                d.Rect(0, H - 120, W, 120, new RgbaColor(20, 26, 40, 255));

                // 标题行：模式名 + 样式键
                d.Text(display, 24, 12, 30, new RgbaColor(240, 245, 255, 255));
                d.Text("NoteStyle · " + style.ModeKey, 24, 54, 14, new RgbaColor(160, 180, 220, 255));

                // 三色样本条（NoteColor / AccentColor / GlowColor + 十六进制）
                int swY = 86;
                d.Rect(24, swY, 120, 26, style.NoteColor);
                d.Rect(152, swY, 120, 26, style.AccentColor);
                d.Rect(280, swY, 120, 26, style.GlowColor);
                d.Text("主 " + NoteStyle.ToHex(style.NoteColor), 24, swY + 30, 11, new RgbaColor(200, 210, 235, 255));
                d.Text("强调 " + NoteStyle.ToHex(style.AccentColor), 152, swY + 30, 11, new RgbaColor(200, 210, 235, 255));
                d.Text("发光 " + NoteStyle.ToHex(style.GlowColor), 280, swY + 30, 11, new RgbaColor(200, 210, 235, 255));

                // 判定线（线宽示意）
                double lineY = H * 0.72;
                d.Line(60, lineY, W - 60, lineY, style.GlowColor, 3);
                d.Text("判定线", 60, lineY + 8, 11, new RgbaColor(150, 170, 210, 255));

                // 音符组合：tap（主色形状）+ hold（强调色长条）+ 特殊（发光圈标记）
                double nx = 150, ny = 350;
                DrawNoteShape(d, style, nx, ny, 46);
                d.Text("tap · " + style.Shape, 110, ny + 58, 11, new RgbaColor(180, 195, 225, 255));

                double hx = 380, hy = lineY;
                d.RoundedRect(hx - 14, hy - 150, 28, 130, 8, style.AccentColor);
                d.RoundedRect(hx - 22, hy - 172, 44, 28, 7, style.NoteColor);
                d.Text("hold", hx - 46, hy + 18, 11, new RgbaColor(180, 195, 225, 255));

                // 接近动画帧（3 帧同心圈：外→中→内，发光色）
                double ax = 660, ay = 320;
                d.Ellipse(ax, ay, 88, 88, style.GlowColor.WithAlpha(60));
                d.Ellipse(ax, ay, 62, 62, style.GlowColor.WithAlpha(120));
                d.Ellipse(ax, ay, 42, 42, style.GlowColor.WithAlpha(200));
                if (style.Approach == NoteApproachType.Shrink)
                    d.Ellipse(ax, ay, 106, 106, style.GlowColor.WithAlpha(40));
                else if (style.Approach == NoteApproachType.Sweep)
                    d.Line(ax - 110, ay, ax + 110, ay, style.GlowColor.WithAlpha(90), 3);
                else if (style.Approach == NoteApproachType.Spread)
                    d.Line(ax - 120, ay - 6, ax + 120, ay - 6, style.GlowColor.WithAlpha(80), 3);
                d.Text("approach · " + style.Approach, ax - 120, ay + 108, 11, new RgbaColor(180, 195, 225, 255));

                // 打击特效示意
                double fx = 850, fy = 330;
                if (style.HitFx == NoteHitFxType.Ring)
                    d.Ellipse(fx, fy, 36, 36, style.GlowColor.WithAlpha(150));
                else if (style.HitFx == NoteHitFxType.Star)
                {
                    d.Line(fx - 30, fy, fx + 30, fy, style.GlowColor.WithAlpha(180), 2);
                    d.Line(fx, fy - 30, fx, fy + 30, style.GlowColor.WithAlpha(180), 2);
                }
                else
                {
                    d.Ellipse(fx - 18, fy - 18, 12, 12, style.GlowColor.WithAlpha(200));
                    d.Ellipse(fx + 18, fy + 10, 9, 9, style.GlowColor.WithAlpha(160));
                }
                d.Text("hitfx · " + style.HitFx, fx - 90, fy + 44, 11, new RgbaColor(180, 195, 225, 255));

                // 描边示意（OutlineWidth）
                d.Text("outline " + style.OutlineWidth.ToString("0.##"), 24, 494, 11, new RgbaColor(200, 210, 235, 255));
                d.Text("接近动画帧 x3 · 判定线 + tap/hold/特殊组合 · 全模式统一视觉语言", 24, 516, 11, new RgbaColor(120, 140, 180, 255));
            }

            bmp.Save(path, ImageFormat.Png);

            // 像素采样：10x6 网格，亮度 > 40 视为非黑（排除纯黑/损坏图）
            int lit = 0;
            for (int sx = 0; sx < 15; sx++)
                for (int sy = 0; sy < 9; sy++)
                {
                    var px = bmp.GetPixel(12 + sx * 64, 12 + sy * 60);
                    if (px.R + px.G + px.B > 120) lit++;
                }

            bool ok = lit >= 8;
            log.AppendLine((ok ? "✅ " : "❌ ") + Path.GetFileName(path)
                + " · " + display + " · 非黑采样 " + lit + "/135");
            return ok;
        }

        /// <summary>形状示意：Capsule/Bar/Tile=RoundedRect；Circle/Ellipse；Ring=双圆环；Diamond=旋转 45° 方块（近似为圆角小方块）。</summary>
        static void DrawNoteShape(IUiDraw d, NoteStyle s, double cx, double cy, double r)
        {
            switch (s.Shape)
            {
                case NoteShape.Circle:
                    d.Ellipse(cx, cy, r, r, s.NoteColor);
                    break;
                case NoteShape.Ring:
                    d.Ellipse(cx, cy, r, r, s.NoteColor);
                    d.Ellipse(cx, cy, r * 0.55, r * 0.55, new RgbaColor(14, 18, 28, 255));
                    break;
                case NoteShape.Diamond:
                    d.RoundedRect(cx - r * 0.62, cy - r * 0.62, r * 1.24, r * 1.24, r * 0.24, s.NoteColor);
                    break;
                case NoteShape.Bar:
                    d.RoundedRect(cx - r * 0.16, cy - r, r * 0.32, r * 2, r * 0.1, s.NoteColor);
                    break;
                case NoteShape.Tile:
                    d.Rect(cx - r * 0.72, cy - r * 0.72, r * 1.44, r * 1.44, s.NoteColor);
                    break;
                default:
                    d.RoundedRect(cx - r * 0.5, cy - r, r, r * 2, r * 0.5, s.NoteColor);
                    break;
            }
            if (s.OutlineWidth > 0)
                d.Line(cx - r * 0.7, cy - r * 0.7, cx + r * 0.7, cy + r * 0.7, s.AccentColor, Math.Max(1, s.OutlineWidth));
        }
    }
}