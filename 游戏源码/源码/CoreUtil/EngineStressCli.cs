using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ChartPlayer
{
    /* ================= 引擎压力测试执行器（t9 全量）：Milestone.exe --stress-engine "<outDir>" [minutes] =================
       0 基线 DemoRunner.RunAll（含 EngineChecks 同源断言）
       1 EngineJobs 10^7 一致性 + 基准
       2 判定引擎 10 万音符 × 10 分钟 × 5 判定档（吞吐/累计误差/单音符分配）
       3 引擎热路径分配（NoteStyleBook.Get / Evaluate / Judge / ScoreBoard.Apply）
       4 引擎壳 UI：GDI 离屏 1000 帧（菜单/选歌/设置三页轮换）帧耗时 P50/P99/Max + 每帧分配；引擎窗软件渲染 1000 帧
       5 长稳：EngineApp 切片连续运行 minutes 分钟
       6 引擎壳热路径分配（UiGlyphRuns.Split / MeasureText / Text）
       产物：res.json + stress-engine.log + stress-engine.md（写入 outDir）。
       写入规范：字符串字面量一律单行（换行用 "\n" 转义）。 */

    public sealed class ShellUiStressDto
    {
        public int Frames;
        public int Width;
        public int Height;
        public string Pages = "";
        public double AvgMs;
        public double P50Ms;
        public double P99Ms;
        public double MaxMs;
        public double Fps;
        public long AllocBytesTotal;
        public double AllocBytesPerFrame;
        public bool Pass;
        public string Error = "";
    }

    public sealed class ShellAllocRowDto
    {
        public string Path;
        public int Calls;
        public long AllocBytes;
        public double BytesPerCall;
        public bool Ok;
        public string Note = "";
    }

    public sealed class ShellAllocStressDto
    {
        public List<ShellAllocRowDto> Rows = new List<ShellAllocRowDto>();
        public bool Pass;
    }

    public sealed class StressFullModel
    {
        public string GeneratedUtc = "";
        public string Runner = "";
        public MachineDto Machine = new MachineDto();
        public BaselineDto Baseline = new BaselineDto();
        public JobsStressDto Jobs = new JobsStressDto();
        public JudgeStressDto Judgement = new JudgeStressDto();
        public AllocStressDto AllocEngine = new AllocStressDto();
        public EngineWindowStressDto EngineWindow = new EngineWindowStressDto();
        public EngineWindowStressDto EngineWindowSmall = new EngineWindowStressDto();
        public LongStabDto LongStab = new LongStabDto();
        public ShellUiStressDto ShellUi = new ShellUiStressDto();
        public ShellAllocStressDto AllocShell = new ShellAllocStressDto();
        public List<string> Fails = new List<string>();
        public bool Pass;

        public sealed class MachineDto
        {
            public string Os = "";
            public string Clr = "";
            public int Cores;
            public int EngineJobsDegree;
        }

        public sealed class BaselineDto
        {
            public bool Ok;
            public string Summary = "";
        }

        public void ComputeFails()
        {
            if (!Baseline.Ok) Fails.Add("baseline: DemoRunner.RunAll 未全绿");
            if (!Jobs.Pass) Fails.Add("jobs: " + (Jobs.Error.Length > 0 ? Jobs.Error : "一致性/性能断言失败"));
            if (!Judgement.Pass) Fails.Add("judgement: " + (Judgement.Error.Length > 0 ? Judgement.Error : "判定断言失败"));
            if (Judgement.Realtime != null && !Judgement.Realtime.Pass) Fails.Add("judgementRealtime: " + (Judgement.Realtime.Error.Length > 0 ? Judgement.Realtime.Error : "实时判定长跑断言失败（窗口漂移>10% 或吞吐硬线）"));
            if (!AllocEngine.Pass) Fails.Add("allocEngine: 引擎热路径分配水位超标");
            if (!EngineWindow.Pass) Fails.Add("engineWindow: " + (EngineWindow.Error.Length > 0 ? EngineWindow.Error : "帧耗时超标"));
            if (!EngineWindowSmall.Pass) Fails.Add("engineWindowSmall: " + (EngineWindowSmall.Error.Length > 0 ? EngineWindowSmall.Error : "帧耗时超标"));
            if (!LongStab.Pass) Fails.Add("longstab: " + (LongStab.Error.Length > 0 ? LongStab.Error : "长稳失败（切片判定异常或内存增长）"));
            if (!ShellUi.Pass) Fails.Add("shellUi: " + (ShellUi.Error.Length > 0 ? ShellUi.Error : "引擎壳 UI 帧耗时超标"));
            if (!AllocShell.Pass) Fails.Add("allocShell: 引擎壳热路径分配水位超标");
        }
    }

    public static class EngineStressCli
    {
        public static int Run(string outDir, double minutes = 10)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = outDir;
            try
            {
                if (string.IsNullOrEmpty(outFull)) outFull = "obj/stress-engine";
                Directory.CreateDirectory(outFull);
                log.AppendLine("===== Milestone --stress-engine（t9 全量：引擎库 + 引擎壳）=====");
                Console.WriteLine("[stress-engine] 开始：" + Path.GetFullPath(outFull));

                // 0 基线
                Console.WriteLine("[stress-engine] 0 基线 DemoRunner.RunAll ...");
                bool baselineOk;
                string baselineSummary;
                try
                {
                    string all = DemoRunner.RunAll();
                    baselineOk = true;
                    baselineSummary = all.Length <= 500 ? all : all.Substring(0, 500);
                    log.AppendLine("基线 DemoRunner.RunAll：全绿");
                }
                catch (Exception ex)
                {
                    baselineOk = false;
                    baselineSummary = "基线失败: " + ex.GetType().Name + " | " + ex.Message;
                    log.AppendLine("基线 FAIL：" + baselineSummary);
                }

                // 1 EngineJobs
                Console.WriteLine("[stress-engine] 1 EngineJobs 10^7 ...");
                var jobs = StressEngine.RunEngineJobs();
                log.AppendLine("1 EngineJobs：10^7 Map 串行 " + jobs.SerialMapMs + "ms / 并行 " + jobs.ParallelMapMs + "ms（一致=" + jobs.MapSame
                    + "）· Reduce 一致=" + jobs.ReduceSame + " · 超卖 " + jobs.OversubDegree + " 一致=" + jobs.OversubSame
                    + " · 单核一致=" + jobs.SingleCoreSame + " · Paused 一致=" + jobs.PausedSame
                    + " · 1M Map " + jobs.Map1MSerialMs + "/" + jobs.Map1MParallelMs + "ms（" + jobs.Map1MSpeedup + "x，一致=" + jobs.Map1MSame
                    + "/" + jobs.Reduce1MSame + "）· Benchmark " + jobs.BenchMapMs + "ms " + jobs.BenchSpeedup + "x");

                // 2 判定
                Console.WriteLine("[stress-engine] 2 判定 10 万音符 × 5 档位 ...");
                var judge = StressEngine.RunJudgement();
                log.AppendLine("2 判定：10 万音符 × 5 档位；");
                foreach (var pd in judge.Profiles)
                    log.AppendLine("   " + pd.Name + "：判定 " + pd.Judged + "/" + pd.Notes + " · 命中 " + pd.Hit + " · MISS " + pd.Miss
                        + " · 吞吐 " + pd.ThroughputNotesPerSec + " 音符/s · 窗口吞吐 " + pd.MinWindowTput + "~" + pd.MaxWindowTput
                        + "（漂移 " + pd.ThroughputDriftPct + "%）· 累计误差 " + pd.CumOffsetMs + "ms（期望 " + pd.ExpectedCumOffsetMs + "）"
                        + " · 判定分配 " + pd.AllocBytesPerJudge + " B/次");
                log.AppendLine("   Tracker 构造 " + judge.TrackerCtorBytesPerNote + " B/音符 · 漏判路径 " + judge.UpdateMissAllocBytesPerNote + " B/音符");

                // 2b 判定实时长跑（t8 SE-03 T1：10s 墙钟窗口漂移 ≤10% 硬断言）
                Console.WriteLine("[stress-engine] 2b 判定实时长跑 " + minutes + " 分钟 ...");
                var judgeRt = StressEngine.RunJudgementRealtime(minutes);
                judge.Realtime = judgeRt;
                foreach (var pd in judgeRt.Profiles)
                    log.AppendLine("   实时 " + pd.Name + "：判定 " + pd.Judged + "/" + pd.Notes + " · 命中 " + pd.Hit + " · MISS " + pd.Miss
                        + " · 吞吐 " + pd.ThroughputNotesPerSec + " 音符/s · 窗口 " + pd.MinWindowTput + "~" + pd.MaxWindowTput
                        + "（漂移 " + pd.ThroughputDriftPct + "%）· 采样窗 " + pd.SampleWindows);

                // 3 引擎热路径分配
                Console.WriteLine("[stress-engine] 3 引擎热路径分配 ...");
                var alloc = StressEngine.RunAllocEngine();
                log.AppendLine("3 引擎热路径分配（1000 次调用）：");
                foreach (var row in alloc.Rows)
                    log.AppendLine("   " + row.Path + "：" + row.BytesPerCall + " B/次 " + (row.Ok ? "OK" : "超标"));

                // 4a 引擎窗 1000 帧（t8 SE-05：两尺寸——1280×800 ×1000 + 640×360 ×500）
                Console.WriteLine("[stress-engine] 4a 引擎窗 1000 帧（两尺寸）...");
                var win = StressEngine.RunEngineWindowFrames();
                var winSmall = StressEngine.RunEngineWindowFrames(500, 640, 360);
                log.AppendLine("4a 引擎窗（软件渲染 " + win.Width + "x" + win.Height + " × " + win.Frames + " 帧）："
                    + "平均 " + win.AvgMs + "ms · P50 " + win.P50Ms + " · P99 " + win.P99Ms + " · Max " + win.MaxMs
                    + " · " + win.Fps + " FPS · 分配 " + win.AllocBytesPerFrame + " B/帧");
                log.AppendLine("   第二尺寸（" + winSmall.Width + "x" + winSmall.Height + " × " + winSmall.Frames + " 帧）："
                    + "平均 " + winSmall.AvgMs + "ms · P99 " + winSmall.P99Ms + " · Max " + winSmall.MaxMs
                    + " · " + winSmall.Fps + " FPS · 分配 " + winSmall.AllocBytesPerFrame + " B/帧");

                // 4b 引擎壳 UI：GDI 离屏 1000 帧
                Console.WriteLine("[stress-engine] 4b 引擎壳 UI 1000 帧 ...");
                var shellUi = RunShellUi(1000, log);

                // 5 长稳
                Console.WriteLine("[stress-engine] 5 长稳 " + minutes + " 分钟 ...");
                var stab = StressEngine.RunLongStab(minutes);
                log.AppendLine("5 长稳 " + stab.Minutes + " 分钟：" + stab.Slices + " 切片 · 失败 " + stab.Fails
                    + " · 沉降内存增长 " + stab.SettledMemGrowthMb + "MB · 峰值工作集 " + stab.PeakWorkingSetMb + "MB"
                    + (stab.FailLog.Length > 0 ? " · " + stab.FailLog : ""));

                // 6 引擎壳热路径分配
                Console.WriteLine("[stress-engine] 6 引擎壳热路径分配 ...");
                var allocShell = RunAllocShell();
                log.AppendLine("6 引擎壳热路径分配（1000 次调用）：");
                foreach (var row in allocShell.Rows)
                    log.AppendLine("   " + row.Path + "：" + row.BytesPerCall + " B/次 " + (row.Ok ? "OK" : "超标"));

                // ---- 汇总 + 产物 ----
                var model = new StressFullModel
                {
                    GeneratedUtc = DateTime.UtcNow.ToString("O"),
                    Runner = "Milestone --stress-engine",
                    Machine = new StressFullModel.MachineDto
                    {
                        Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        Clr = Environment.Version.ToString(),
                        Cores = Environment.ProcessorCount,
                        EngineJobsDegree = EngineJobs.Degree,
                    },
                    Baseline = new StressFullModel.BaselineDto { Ok = baselineOk, Summary = baselineSummary },
                    Jobs = jobs,
                    Judgement = judge,
                    AllocEngine = alloc,
                    EngineWindow = win,
                    EngineWindowSmall = winSmall,
                    LongStab = stab,
                    ShellUi = shellUi,
                    AllocShell = allocShell,
                };
                model.ComputeFails();
                model.Pass = model.Fails.Count == 0;

                var jsonOpts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                string json = JsonSerializer.Serialize(model, jsonOpts);
                File.WriteAllText(Path.Combine(outFull, "res.json"), json, new UTF8Encoding(false));
                log.AppendLine("结论：" + (model.Pass ? "PASS（全部断言通过）" : "FAIL（" + model.Fails.Count + " 项）"));
                foreach (var f in model.Fails) log.AppendLine("  [FAIL] " + f);
                log.AppendLine("产物：res.json / stress-engine.log -> " + Path.GetFullPath(outFull));
                code = model.Pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL stress-engine 异常：" + ex.GetType().Name + " | " + ex.Message);
                code = 1;
            }
            try
            {
                File.WriteAllText(Path.Combine(outFull, "stress-engine.log"), log.ToString(), new UTF8Encoding(true));
                Console.WriteLine(log.ToString());
            }
            catch { }
            return code;
        }

        /* ---------------- 4b 引擎壳 UI（GDI 离屏 1000 帧） ---------------- */

        static ShellUiStressDto RunShellUi(int frames, StringBuilder log)
        {
            var d = new ShellUiStressDto { Frames = frames, Width = 1280, Height = 800 };
            var pages = new[] { "menu", "songs", "settings" };
            d.Pages = string.Join("+", pages);
            try
            {
                using var shell = new EngineMainShell(new Dictionary<string, Action>(), () => "", (c, dd) => true);
                using var bmp = DpiBitmap.Create(1280, 800);   // t78：96 DPI（与实窗渲染同口径）
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var draw = new GdiDrawAdapter(g);

                // 暖机 10 帧（JIT/字体缓存就位）
                for (int i = 0; i < 10; i++)
                {
                    g.Clear(Color.FromArgb(UiTheme.Default.Bg.R, UiTheme.Default.Bg.G, UiTheme.Default.Bg.B));
                    shell.RenderPageTo(pages[i % pages.Length], draw, 1280, 800);
                }

                long b0 = GC.GetAllocatedBytesForCurrentThread();
                var times = new double[frames];
                for (int i = 0; i < frames; i++)
                {
                    var page = pages[i % pages.Length];
                    g.Clear(Color.FromArgb(UiTheme.Default.Bg.R, UiTheme.Default.Bg.G, UiTheme.Default.Bg.B));
                    long t0 = Stopwatch.GetTimestamp();
                    if (!shell.RenderPageTo(page, draw, 1280, 800))
                        throw new InvalidOperationException("页面不存在：" + page);
                    times[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                }
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                d.AllocBytesTotal = b1 - b0;
                d.AllocBytesPerFrame = Math.Round((b1 - b0) / (double)frames, 3);

                Array.Sort(times);
                double avg = 0;
                foreach (var v in times) avg += v;
                avg /= frames;
                d.AvgMs = Math.Round(avg, 3);
                d.P50Ms = Math.Round(times[(int)(times.Length * 0.5)], 3);
                d.P99Ms = Math.Round(times[Math.Min(times.Length - 1, (int)(times.Length * 0.99))], 3);
                d.MaxMs = Math.Round(times[times.Length - 1], 3);
                d.Fps = Math.Round(1000.0 / Math.Max(0.001, avg), 1);
                log.AppendLine("4b 引擎壳 UI（GDI 离屏 " + d.Width + "x" + d.Height + " × " + d.Frames + " 帧，" + d.Pages + " 轮换）："
                    + "平均 " + d.AvgMs + "ms · P50 " + d.P50Ms + " · P99 " + d.P99Ms + " · Max " + d.MaxMs
                    + " · " + d.Fps + " FPS · 分配 " + d.AllocBytesPerFrame + " B/帧");
            }
            catch (Exception ex)
            {
                d.Error = ex.GetType().Name + ": " + ex.Message;
            }
            d.Pass = d.Error.Length == 0 && d.AvgMs < 33 && d.P99Ms < 100 && d.AllocBytesPerFrame < 512;
            return d;
        }

        /* ---------------- 6 引擎壳热路径分配 ---------------- */

        static ShellAllocStressDto RunAllocShell()
        {
            var d = new ShellAllocStressDto();
            const int calls = 1000;

            void Probe(string path, Action act, double maxBytesPerCall, string note = "")
            {
                for (int i = 0; i < 50; i++) act();          // 暖机
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < calls; i++) act();
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                double per = (b1 - b0) / (double)calls;
                d.Rows.Add(new ShellAllocRowDto
                {
                    Path = path, Calls = calls, AllocBytes = b1 - b0,
                    BytesPerCall = Math.Round(per, 3),
                    Ok = per <= maxBytesPerCall, Note = note
                });
            }

            const string plain = "设置界面文字（纯文本快速路径）";
            const string emoji = "⚙ 设置";
            const string mixed = "🎮 游玩 1234 次 · 累计命中 5678 音符 · 🏆 最高ACC 99% · 最佳连击 12x";

            Probe("UiGlyphRuns.ContainsEmoji(纯文本)", () => UiGlyphRuns.ContainsEmoji(plain), 0.9);
            Probe("UiGlyphRuns.Split(纯文本)", () => UiGlyphRuns.Split(plain), 0.9, "修复后共享空列表，零分配");
            Probe("UiGlyphRuns.Split(emoji)", () => UiGlyphRuns.Split(emoji), 1.0, "修复后重复文本走缓存，稳态零分配");
            Probe("UiGlyphRuns.Split(混合长文本)", () => UiGlyphRuns.Split(mixed), 1.0, "修复后重复文本走缓存，稳态零分配");

            using (var bmp = DpiBitmap.Create(640, 360))   // t78：96 DPI（分配压测与实窗渲染同口径）
            using (var g = Graphics.FromImage(bmp))
            {
                var draw = new GdiDrawAdapter(g);
                Probe("GdiDrawAdapter.MeasureText(纯文本)", () => draw.MeasureText(plain, 16), 256, "GDI 内部实现分配不计入修复口径");
                Probe("GdiDrawAdapter.MeasureText(emoji)", () => draw.MeasureText(emoji, 16), 64, "字体/split 均走缓存，GDI 内部微分配上限");
                Probe("GdiDrawAdapter.Text(纯文本)", () => draw.Text(plain, 10, 10, 16, new RgbaColor(255, 255, 255, 255)), 1024, "GDI DrawString 内部分配");
                Probe("GdiDrawAdapter.Text(emoji)", () => draw.Text(emoji, 10, 40, 16, new RgbaColor(255, 255, 255, 255)), 128, "字体/split 均走缓存，GDI 内部微分配上限");
                Probe("GdiDrawAdapter.Rect(实体填充)", () => draw.Rect(10, 80, 100, 30, new RgbaColor(45, 108, 255, 255)), 0.9, "修复后画刷走静态缓存，零分配");
                Probe("GdiDrawAdapter.Line(描线)", () => draw.Line(10, 120, 200, 120, new RgbaColor(200, 200, 200, 255), 1), 0.9, "修复后画笔走静态缓存，零分配");
            }

            Probe("NoteStyleBook.Get(\"mania\")（壳侧复验）", () => NoteStyleBook.Get("mania"), 0.9, "引擎修复在壳侧同样零分配");
            // t10 SG-02 复核：GamePanel 实际调用链形状（ModeSystem.ModeId → Get → 颜色转换）零分配回归探针
            Probe("GamePanel 链 ModeId→Get→ToColor", () =>
            {
                var s2 = NoteStyleBook.Get(ModeSystem.ModeId(GameMode.Mania));
                var c2 = System.Drawing.Color.FromArgb(s2.NoteColor.A, s2.NoteColor.R, s2.NoteColor.G, s2.NoteColor.B);
                _ = c2;
            }, 0.9, "与 GamePanel.NoteColCurrent 同形状（t10 WARN 复核）");

            d.Pass = true;
            foreach (var row in d.Rows) if (!row.Ok) d.Pass = false;
            return d;
        }
    }
}
