using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace ChartPlayer
{
    /* ================= 故事版演示：Milestone.exe --storyshot "<outDir>" =================
       离屏渲染一段示例故事版「星辰序章」（标题淡入 → 粒子星雨 → 镜头推进 → 对白浮现 →
       判定联动（Perfect 绽放/风暴/连击烟花）→ 淡出），N 帧 PNG 证据（复用 GdiDrawAdapter/IUiDraw 路径）。 */

    public static class StoryshotDemo
    {
        /// <summary>小数部分（0..1；StoryshotState 与 Render 共用）。</summary>
        static double Frac(double x) => x - Math.Floor(x);

        /// <summary>故事版演示主入口：--storyshot 输出截图与 storyshot.log；0=通过 / 1=失败。</summary>
        public static int RunStoryshot(string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir ?? "obj/storyshot");
                var log = new StringBuilder();
                log.AppendLine("===== Milestone --storyshot · 星辰序章 =====");

                var sb = BuildStarPrelude();
                sb.Sort();

                const int W = 960, H = 540, N = 9;
                double[] times = { 0, 1200, 2400, 3600, 4400, 4650, 4900, 6200, 7300 };

                var st = new StoryshotState();
                var ok = true;
                var problems = new StringBuilder();

                for (int i = 0; i < N; i++)
                {
                    double t = times[i];
                    // 判定联动演示：在 4650ms 帧触发一次 Perfect（判定联动叙事 -> 绽放/风暴/连击烟花）
                    if (i == 5)
                        sb.OnJudgement(new Judgement(new JudgeTier("Perfect", 40, 300, 1.0), 8, t));

                    var frame = sb.Advance(t);
                    st.Step(frame, t);

                    using var bmp = DpiBitmap.Create(W, H);   // t78：96 DPI——故事板文本按逻辑尺寸光栅化（高 DPI 屏 1.5× 放大与 960×540 布局不符）
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        var d = new GdiDrawAdapter(g);
                        Render(st, frame, d, W, H);
                    }
                    string png = Path.Combine(outDir, "story_" + i.ToString("00") + ".png");
                    bmp.Save(png, ImageFormat.Png);

                    log.AppendLine("frame_" + i.ToString("00") + " t=" + t.ToString("0") + "ms overlay=" +
                        frame.OverlayOpacity.ToString("0.00") + " off=(" + frame.OffsetX.ToString("0.#") + "," + frame.OffsetY.ToString("0.#") +
                        ") rot=" + frame.RotationDeg.ToString("0.#") + " scale=" + frame.Scale.ToString("0.##") +
                        " text='" + frame.Text + "' textOp=" + frame.TextOpacity.ToString("0.00") +
                        " camZ=" + frame.CameraPose.Position.Z.ToString("0.##") +
                        " spawns=" + frame.Spawns.Count + " flashA=" + frame.Color.A + " -> " + png);
                }

                // 证据断言（写在 log 并决定退出码）
                var ev = sb.Events;
                AssertLog(ev.Count == 8, "示例故事版 8 事件", log, ref ok, problems);
                AssertLog(st.SeenTitle, "标题淡入出现", log, ref ok, problems);
                AssertLog(st.SeenDialogue, "对白浮现出现", log, ref ok, problems);
                AssertLog(st.SeenCameraMove, "镜头推进（camZ<-1）", log, ref ok, problems);
                AssertLog(st.SeenJudgementBurst, "判定联动粒子（绽放/烟花）", log, ref ok, problems);
                AssertLog(st.SeenFlash, "判定联动闪色", log, ref ok, problems);
                AssertLog(st.FinalOverlay > 0.9, "结尾淡出遮罩", log, ref ok, problems);

                log.AppendLine(ok
                    ? "===== 通过：星辰序章 8 帧 PNG + 判定联动/镜头/粒子/对白/淡出全部到位 ====="
                    : "===== 失败：" + problems.ToString().Trim() + " =====");

                try { File.WriteAllText(Path.Combine(outDir, "storyshot.log"), log.ToString(), new UTF8Encoding(true)); }
                catch { }
                Console.WriteLine(log.ToString());
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(outDir ?? "obj/storyshot", "storyshot.log"), "失败：" + ex, new UTF8Encoding(true)); } catch { }
                Console.Error.WriteLine("storyshot 失败：" + ex);
                return 1;
            }
        }

        static void AssertLog(bool cond, string what, StringBuilder log, ref bool ok, StringBuilder problems)
        {
            log.AppendLine((cond ? "✅ " : "❌ ") + what);
            if (!cond) { ok = false; if (problems.Length > 0) problems.Append("；"); problems.Append(what); }
        }

        /// <summary>示例故事版「星辰序章」：标题淡入 → 粒子星雨 → 镜头推进 → 对白浮现 → 判定联动 → 淡出。</summary>
        internal static Storyboard BuildStarPrelude()
        {
            var s = new Storyboard();
            s.Add(new StoryEvent(StoryEventType.FadeIn, 0) { DurationMs = 1600, Ease = "EaseOutQuad" });
            s.Add(new StoryEvent(StoryEventType.TextOverlay, 200) { DurationMs = 1800, Text = "星辰序章", TextX = 0.5, TextY = 0.35, TextSize = 46, Color = new RgbaColor(255, 210, 63) });
            s.Add(new StoryEvent(StoryEventType.ParticleBurst, 1500)
            {
                DurationMs = 3200, EmissionRate = 18, BurstCount = 120,
                ParticleX = 0.5, ParticleY = 0.30,
                SpeedMin = 30, SpeedMax = 90, SpreadDeg = 360,
                LifeMin = 0.6, LifeMax = 1.4, SizeMin = 1, SizeMax = 3,
                Color = new RgbaColor(150, 200, 255), Gravity = 14
            });
            s.Add(new StoryEvent(StoryEventType.CameraPath, 1800)
            {
                DurationMs = 2600, Ease = "EaseInOutQuad",
                CameraPoints = new[] { new Vec3(0, 0, 0), new Vec3(0, 0.5f, -3), new Vec3(0, 0, -7) }
            });
            s.Add(new StoryEvent(StoryEventType.BackgroundShift, 2200) { DurationMs = 2000, Color = new RgbaColor(16, 30, 66) });
            s.Add(new StoryEvent(StoryEventType.TextOverlay, 3600) { DurationMs = 2400, Text = "「星际的旅人，欢迎抵达星海之港。」", TextX = 0.28, TextY = 0.74, TextSize = 22, Color = new RgbaColor(180, 210, 255) });
            s.Add(new StoryEvent(StoryEventType.JudgementHook, 0)
            {
                HookFilter = "perfect",
                ChildDelayMs = 100,
                Children = new List<StoryEvent>
                {
                    new StoryEvent(StoryEventType.ParticleBurst, 0) { BurstCount = 150, ParticleX = 0.5, ParticleY = 0.62, SpeedMin = 60, SpeedMax = 160, SpreadDeg = 360, SizeMin = 2, SizeMax = 5, LifeMin = 0.4, LifeMax = 1.0, Color = new RgbaColor(255, 210, 63), Gravity = 60 },
                    new StoryEvent(StoryEventType.ColorFlash, 20) { DurationMs = 420, Color = new RgbaColor(255, 214, 90) }
                }
            });
            s.Add(new StoryEvent(StoryEventType.FadeOut, 6200) { DurationMs = 1100, Ease = "EaseInQuad" });
            return s;
        }

        /// <summary>演示渲染状态（确定性星野 + 粒子池）。</summary>
        sealed class StoryshotState
        {
            public readonly double[] Stars = new double[160];
            public readonly List<EngineParticle> Particles = new List<EngineParticle>();
            public double LastT = -1;
            public bool SeenTitle, SeenDialogue, SeenCameraMove, SeenJudgementBurst, SeenFlash;
            public double FinalOverlay;

            public StoryshotState()
            {
                for (int i = 0; i < Stars.Length; i++) Stars[i] = Frac(i * 0.6180339887498948 + 0.13);
            }

            public void Step(StoryFrame frame, double t)
            {
                // 粒子推进（上一帧 dt）
                if (LastT >= 0)
                {
                    double dt = Math.Max(0, (t - LastT) / 1000.0);
                    foreach (var p in Particles)
                    {
                        if (p.Life <= 0) continue;
                        p.Life -= dt;
                        if (p.Life <= 0) continue;
                        p.Velocity += p.Acceleration * dt;
                        p.Position += p.Velocity * dt;
                    }
                }
                // 本帧新增
                foreach (var s in frame.Spawns) Particles.Add(s);
                LastT = t;

                if (!string.IsNullOrEmpty(frame.Text) && frame.TextOpacity > 0.5)
                {
                    if (frame.Text == "星辰序章") SeenTitle = true;
                    else SeenDialogue = true;
                }
                if (frame.CameraPose.Position.Z < -1) SeenCameraMove = true;
                if (frame.Spawns.Count >= 150) SeenJudgementBurst = true;
                if (frame.Color.A > 60) SeenFlash = true;
                FinalOverlay = frame.OverlayOpacity;
            }
        }

        /// <summary>把 StoryFrame 画成一张 960x540 帧（GDI 软件渲染）。</summary>
        static void Render(StoryshotState st, StoryFrame frame, GdiDrawAdapter d, int W, int H)
        {
            // 背景
            d.Rect(0, 0, W, H, frame.BackgroundColor);

            // 星野（镜头推进 parallax：相机 Z 越深，星星外扩放大）
            double cz = frame.CameraPose.Position.Z;
            double zoom = 1.0 + Math.Max(0, -cz) * 0.10;
            for (int i = 0; i < st.Stars.Length; i++)
            {
                double sx = st.Stars[i];
                double sy = Frac(st.Stars[i] * 3.17 + 0.29);
                double px = W * 0.5 + (sx - 0.5) * W * 1.15 * zoom + frame.OffsetX;
                double py = H * 0.5 + (sy - 0.5) * H * 1.15 * zoom + frame.OffsetY;
                double sz = 1.1 + Math.Max(0, -cz) * 0.14;
                byte br = (byte)Math.Min(255, 120 + 100 * (i % 3));
                d.Ellipse(px, py, sz, sz, new RgbaColor(br, br, (byte)Math.Min(255, br + 24), 255));
            }

            // 星球（镜头推进可见）
            double planet = 230 - Math.Max(0, -cz) * 16;
            if (planet > 30)
                d.Ellipse(W * 0.76 + frame.OffsetX, H * 0.36 + frame.OffsetY, planet, planet, new RgbaColor(64, 116, 190, 210));

            // 标题/对白（含 offset/缩放）
            if (!string.IsNullOrEmpty(frame.Text) && frame.TextOpacity > 0.001)
            {
                double tw = d.MeasureText(frame.Text, frame.TextSize);
                double tx = W * frame.TextX + frame.OffsetX - tw * 0.5;
                double ty = H * frame.TextY + frame.OffsetY;
                var tc = frame.TextColor.WithAlpha((byte)(255 * Math.Min(1, frame.TextOpacity)));
                d.Text(frame.Text, tx, ty, frame.TextSize, tc);
            }

            // 粒子（本帧存活）
            foreach (var p in st.Particles)
            {
                if (p.Life <= 0) continue;
                double k = Math.Max(0, Math.Min(1, p.Life / p.MaxLife));
                byte a = (byte)(255 * k);
                d.Ellipse(p.Position.X * W + frame.OffsetX, p.Position.Y * H + frame.OffsetY,
                          Math.Max(0.6, p.Size), Math.Max(0.6, p.Size), p.Color.WithAlpha(a));
            }

            // 判定联动闪色
            if (frame.Color.A > 0)
                d.Rect(0, 0, W, H, frame.Color);

            // 淡入/淡出遮罩
            if (frame.OverlayOpacity > 0.001)
                d.Rect(0, 0, W, H, frame.OverlayColor.WithAlpha((byte)(255 * Math.Min(1, frame.OverlayOpacity))));
        }
    }
}
