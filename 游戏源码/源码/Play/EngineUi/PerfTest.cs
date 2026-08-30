using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace ChartPlayer
{
    /* ================= 性能测试套件：Milestone.exe --perftest "<outDir>" =================
       测量：1 谱面解析吞吐（串行 vs ParseFilesParallel，MB/s 与加速比，测试格式全部谱面 xN 轮）
       2 EngineJobs ParallelMap/Reduce 1k/64k/1M Speedup + Benchmark
       3 判定/校验/对音吞吐（JudgementTracker 100k 音符 / ChartValidator 20k 音符 / BeatAlignEngine 60s@44.1k）
       4 D2D 离屏渲染引擎 UI 主菜单 60 帧（平均/尾帧/P99；无窗口 GDI 保底）
       5 内存：GC.GetTotalMemory 各环节增量 + 峰值工作集
       产物：perftest.json + perftest.md；阈值（宽松回归）：解析 >=1MB/s、UI 平均帧时 <16ms、无 OOM。 */

    public static class PerfTest
    {
        public static int RunPerfTest(string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = outDir;
            try
            {
                if (string.IsNullOrEmpty(outFull)) outFull = "obj/perftest";
                Directory.CreateDirectory(outFull);

                string chartsDir = LocateChartsDir();
                var exts = new HashSet<string>(ChartParser.ChartExts) { ".adofai" };   // 测试格式全部谱面（含 adofai）
                var files = Directory.GetFiles(chartsDir, "*.*")
                    .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray();
                if (files.Length == 0) throw new InvalidOperationException("测试格式目录无谱面：" + chartsDir);

                long totalBytes = 0;
                foreach (var f in files) totalBytes += new FileInfo(f).Length;

                EngineJobs.Degree = Math.Max(2, Environment.ProcessorCount);
                log.AppendLine("===== Milestone --perftest =====");
                log.AppendLine("谱面目录：" + chartsDir + " · 文件 " + files.Length + " · 总 " + totalBytes + " B · 并行度 " + EngineJobs.Degree);

                var m = new PerfModel();

                // ---- 1 解析吞吐 ----
                int rounds = Math.Max(16, (int)Math.Ceiling((4.0 * 1024 * 1024) / Math.Max(1, totalBytes)));
                GcSettle();
                double mem0 = GC.GetTotalMemory(false);
                long t0 = Stopwatch.GetTimestamp();
                for (int r = 0; r < rounds; r++)
                    foreach (var f in files) ChartParser.ParseFile(f);
                double serialSec = Elapsed(t0);
                double memParseSerial = (GC.GetTotalMemory(false) - mem0) / 1048576.0;
                double serialMBs = (totalBytes * (double)rounds / 1048576.0) / serialSec;

                GcSettle();
                mem0 = GC.GetTotalMemory(false);
                t0 = Stopwatch.GetTimestamp();
                int parsed = 0;
                for (int r = 0; r < rounds; r++)
                {
                    var charts = ChartParser.ParseFilesParallel(files);
                    foreach (var c in charts) if (c != null) parsed++;
                }
                double parSec = Elapsed(t0);
                double memParsePar = (GC.GetTotalMemory(false) - mem0) / 1048576.0;
                double parMBs = (totalBytes * (double)rounds / 1048576.0) / parSec;

                m.Parse = new PerfModel.ParseStageDto
                {
                    Files = files.Length, Bytes = totalBytes, Rounds = rounds,
                    SerialSeconds = R2(serialSec), ParallelSeconds = R2(parSec),
                    SerialMbPerSec = R2(serialMBs), ParallelMbPerSec = R2(parMBs),
                    Speedup = R2(serialSec / Math.Max(1e-9, parSec)),
                    ParsedCharts = parsed, MemDeltaSerialMb = R2(memParseSerial), MemDeltaParallelMb = R2(memParsePar)
                };
                log.AppendLine("1 解析：串行 " + serNum(serialMBs) + " MB/s（" + R2(serialSec) + "s）· 并行 " + serNum(parMBs) + " MB/s（" + R2(parSec) + "s）· 加速比 " + R2(m.Parse.Speedup) + "· 解析成功 " + parsed + " / " + (files.Length * rounds));

                // ---- 2 EngineJobs ----
                m.EngineJobs = new PerfModel.JobsStageDto();
                int[] sizes = { 1024, 65536, 1048576 };
                foreach (var n in sizes)
                {
                    var src = new int[n];
                    for (int i = 0; i < n; i++) src[i] = i;
                    t0 = Stopwatch.GetTimestamp();
                    var ser = new long[n];
                    for (int i = 0; i < n; i++) ser[i] = (long)src[i] * src[i];
                    double serMs = Ms(t0);
                    t0 = Stopwatch.GetTimestamp();
                    var par = EngineJobs.ParallelMap(src, x => (long)x * x);
                    double parMs = Ms(t0);
                    m.EngineJobs.MapRows.Add(new PerfModel.MapRowDto
                    {
                        Elements = n, SerialMs = R2(serMs), ParallelMs = R2(parMs),
                        Speedup = R2(serMs / Math.Max(0.001, parMs)), Same = SameSeq(ser, par)
                    });
                    t0 = Stopwatch.GetTimestamp();
                    long s = 0;
                    foreach (var x in src) s += (long)x * x;
                    double rSerMs = Ms(t0);
                    t0 = Stopwatch.GetTimestamp();
                    long p = EngineJobs.ParallelReduce(src, x => (long)x * x, 0L, (a, b) => a + b);
                    double rParMs = Ms(t0);
                    var row = m.EngineJobs.MapRows[m.EngineJobs.MapRows.Count - 1];
                    row.ReduceSerialMs = R2(rSerMs);
                    row.ReduceParallelMs = R2(rParMs);
                    row.ReduceSpeedup = R2(rSerMs / Math.Max(0.001, rParMs));
                    row.ReduceSame = s == p;
                }
                var bench = EngineJobs.Benchmark();
                m.EngineJobs.BenchmarkDegree = bench.Degree;
                m.EngineJobs.BenchmarkMapMs = R2(bench.MapMs);
                m.EngineJobs.BenchmarkSpeedup = R2(bench.Speedup);
                log.AppendLine("2 EngineJobs：1k/64k/1M Map 并行 " + string.Join("/", m.EngineJobs.MapRows.Select(x => x.ParallelMs + "ms")) + " · Speedup " + string.Join("/", m.EngineJobs.MapRows.Select(x => x.Speedup)) + " · Benchmark " + R2(bench.MapMs) + "ms @" + bench.Degree + " 线程 " + R2(bench.Speedup) + "x");

                // ---- 3 判定/校验/对音 ----
                m.Judge = RunJudgeStage();
                m.Validate = RunValidateStage();
                m.Align = RunAlignStage();
                log.AppendLine("3 判定 100k 推进 " + R2(m.Judge.Milliseconds) + "ms · 校验 20k 音符 " + R2(m.Validate.Milliseconds) + "ms（" + m.Validate.Issues + " issues）· 对音 60s@44.1k " + R2(m.Align.Milliseconds) + "ms");

                // ---- 4 D2D / GDI UI 帧时 ----
                m.Ui = RunUiStage(log);
                log.AppendLine("4 UI 主菜单 60 帧：D2D " + (m.Ui.D2dAverageMs.HasValue ? R2(m.Ui.D2dAverageMs.Value) + "ms" : "（无 D2D）") + " · GDI 平均 " + R2(m.Ui.GdiAverageMs) + "ms · GDI P99 " + R2(m.Ui.GdiP99Ms) + "ms · GDI 尾帧 " + R2(m.Ui.GdiTailMs) + "ms");

                // ---- 5 内存 ----
                try { m.Memory.PeakWorkingSetMb = R2(Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0); } catch { }
                log.AppendLine("5 内存增量：解析串行 " + R2(m.Parse.MemDeltaSerialMb) + "MB / 并行 " + R2(m.Parse.MemDeltaParallelMb) + "MB · 峰值工作集 " + R2(m.Memory.PeakWorkingSetMb) + "MB");

                // ---- 阈值（宽松回归基线） ----
                m.Thresholds = new PerfModel.ThresholdsDto
                {
                    ParseMinMbPerSec = 1.0,
                    UiMaxAverageFrameMs = 16.0,
                    ParseMbPerSecOk = m.Parse.SerialMbPerSec >= 1.0,
                    UiAverageFrameOk = (m.Ui.D2dAverageMs.HasValue ? m.Ui.D2dAverageMs.Value : m.Ui.GdiAverageMs) < 16.0,
                    NoOom = m.Oom == null
                };
                bool ok = m.Thresholds.ParseMbPerSecOk && m.Thresholds.UiAverageFrameOk && m.Thresholds.NoOom;
                m.Thresholds.Pass = ok;
                log.AppendLine("阈值：" + (ok ? "PASS" : "FAIL") + "（解析≥1MB/s=" + m.Thresholds.ParseMbPerSecOk + " · UI<16ms=" + m.Thresholds.UiAverageFrameOk + " · NoOOM=" + m.Thresholds.NoOom + "）");

                // ---- 产物 ----
                var jsonOpts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
                string json = JsonSerializer.Serialize(new PerfModel.Snapshot(m), jsonOpts);
                File.WriteAllText(Path.Combine(outFull, "perftest.json"), json, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outFull, "perftest.md"), BuildMarkdown(m, files.Length, totalBytes, chartsDir), new UTF8Encoding(false));
                log.AppendLine("产物：perftest.json / perftest.md --> " + Path.GetFullPath(outFull));
                code = ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL perftest 异常：" + ex.Message);
                code = 1;
            }
            try { File.WriteAllText(Path.Combine(outFull, "perftest.log"), log.ToString(), new UTF8Encoding(true)); Console.WriteLine(log.ToString()); } catch { }
            return code;
        }

        /* ---------------- 3 各吞吐阶段 ---------------- */

        static PerfModel.JudgeStageDto RunJudgeStage()
        {
            GcSettle();
            const int n = 100000;
            var chart = new ChartData();
            var notes = new RhythmNote[n];
            for (int i = 0; i < n; i++)
            {
                notes[i] = new RhythmNote(i * 2.0) { Lane = i % 4 };
                chart.Notes.Add(notes[i]);
            }
            long mem0 = GC.GetTotalMemory(false);
            var track = new JudgementTracker(chart, JudgementProfile.OsuMania(8));
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) track.Judge(notes[i], 0);
            track.Update(200000.0);
            double ms = Ms(t0);
            return new PerfModel.JudgeStageDto
            {
                Notes = n, Milliseconds = R2(ms), Judged = track.JudgedCount, Remaining = track.Remaining,
                MemDeltaMb = R2((GC.GetTotalMemory(false) - mem0) / 1048576.0)
            };
        }

        static PerfModel.ValidateStageDto RunValidateStage()
        {
            GcSettle();
            const int n = 20000;
            var chart = new ChartData();
            for (int i = 0; i < n; i++)
                chart.Notes.Add(new RhythmNote(i * 5.0) { Lane = i % 4 });
            long mem0 = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            var issues = ChartValidator.Validate(chart, new ValidatorOptions { Mode = ChartValidatorMode.Mania, KeyCount = 4 });
            sw.Stop();
            return new PerfModel.ValidateStageDto
            {
                Notes = n, Milliseconds = R2(sw.Elapsed.TotalMilliseconds), Issues = issues.Count,
                MemDeltaMb = R2((GC.GetTotalMemory(false) - mem0) / 1048576.0)
            };
        }

        static PerfModel.AlignStageDto RunAlignStage()
        {
            GcSettle();
            const int sr = 44100;
            int secs = 60;
            var pcm = new float[sr * secs];
            for (int beat = 1000; beat < secs * 1000 - 200; beat += 500)
            {
                int start = (int)(beat * sr / 1000.0);
                for (int mm = 0; mm < 88; mm++)
                {
                    int idx = start + mm;
                    if (idx >= 0 && idx < pcm.Length)
                        pcm[idx] += (float)(0.9 * Math.Exp(-mm * 0.014) * Math.Sin(2.0 * Math.PI * 880.0 * mm / sr));
                }
            }
            var noteMs = new List<double>();
            for (int beat = 1000; beat < secs * 1000 - 200; beat += 500) noteMs.Add(beat);
            long mem0 = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            var rep = BeatAlignEngine.Analyze(pcm, sr, noteMs.ToArray());
            sw.Stop();
            return new PerfModel.AlignStageDto
            {
                Seconds = secs, SampleRate = sr, Milliseconds = R2(sw.Elapsed.TotalMilliseconds),
                BpmEstimate = rep.BpmEstimate, MemDeltaMb = R2((GC.GetTotalMemory(false) - mem0) / 1048576.0)
            };
        }

        /* ---------------- 4 UI 帧时 ---------------- */

        static PerfModel.UiStageDto RunUiStage(StringBuilder log)
        {
            var dto = new PerfModel.UiStageDto();
            GcSettle();
            using (var form = new EngineUiDemoForm((c, d) => true))
            using (var bmp = DpiBitmap.Create(1280, 720))   // t78：96 DPI（压测与实窗同口径）
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var draw = new GdiDrawAdapter(g);
                for (int i = 0; i < 3; i++) form.RenderPageTo("menu", draw, 1280, 720);
                var frames = new List<double>();
                for (int i = 0; i < 60; i++)
                {
                    long t = Stopwatch.GetTimestamp();
                    form.RenderPageTo("menu", draw, 1280, 720);
                    frames.Add((Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency);
                }
                dto.GdiAverageMs = R2(frames.Average());
                dto.GdiTailMs = R2(frames[frames.Count - 1]);
                dto.GdiP99Ms = R2(Percentile(frames, 0.99));
                dto.GdiMaxMs = R2(frames.Max());
            }
            try
            {
                using (var form = new EngineUiDemoForm((c, d) => true))
                {
                    form.Location = new Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    if (form.D2dReady)
                    {
                        var frames = new List<double>();
                        for (int i = 0; i < 60; i++)
                        {
                            int target = form.PaintFrames + 1;
                            long t = Stopwatch.GetTimestamp();
                            var deadline = Environment.TickCount + 2000;
                            form.Invalidate();
                            while (form.PaintFrames < target && Environment.TickCount < deadline) Application.DoEvents();
                            frames.Add((Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency);
                        }
                        dto.D2dAvailable = true;
                        dto.D2dAverageMs = R2(frames.Average());
                        dto.D2dTailMs = R2(frames[frames.Count - 1]);
                        dto.D2dP99Ms = R2(Percentile(frames, 0.99));
                    }
                    form.Close();
                }
            }
            catch (Exception ex)
            {
                dto.D2dAvailable = false;
                dto.D2dError = ex.Message;
            }
            return dto;
        }

        static double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0) return 0;
            var sorted = new List<double>(values);
            sorted.Sort();
            int idx = Math.Min(sorted.Count - 1, (int)Math.Floor(p * (sorted.Count - 1) + 0.5));
            return sorted[idx];
        }

        /* ---------------- 工具 ---------------- */

        static string LocateChartsDir()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.CurrentDirectory, "其他", "Chart", "测试格式"),
                Path.Combine(AppContext.BaseDirectory, "其他", "Chart", "测试格式")
            };
            foreach (var c in candidates)
                if (Directory.Exists(c)) return Path.GetFullPath(c);
            throw new InvalidOperationException("找不到 其他/Chart/测试格式 谱面目录（当前目录：" + Environment.CurrentDirectory + "）");
        }

        static void GcSettle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        static double Elapsed(long t0) => (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
        static double Ms(long t0) => Elapsed(t0) * 1000.0;
        static double R2(double v) => Math.Round(v, 2);
        static string serNum(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        static bool SameSeq(long[] a, long[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string BuildMarkdown(PerfModel m, int files, long bytes, string chartsDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Milestone --perftest 报告");
            sb.AppendLine();
            sb.AppendLine("- 谱面集：`" + chartsDir + "`（" + files + " 文件 · " + bytes + " B）");
            sb.AppendLine("- EngineJobs 并行度：" + m.EngineJobs.BenchmarkDegree);
            sb.AppendLine();
            sb.AppendLine("## 1. 谱面解析吞吐");
            sb.AppendLine();
            sb.AppendLine("| 轮数 | 串行 | 并行 | 加速比 | 串行内存Δ | 并行内存Δ |");
            sb.AppendLine("|---|---|---|---|---|---|");
            sb.AppendLine("| " + m.Parse.Rounds + " | " + m.Parse.SerialMbPerSec + " MB/s (" + m.Parse.SerialSeconds + "s) | " + m.Parse.ParallelMbPerSec + " MB/s (" + m.Parse.ParallelSeconds + "s) | " + m.Parse.Speedup + "x | " + m.Parse.MemDeltaSerialMb + " MB | " + m.Parse.MemDeltaParallelMb + " MB |");
            sb.AppendLine();
            sb.AppendLine("## 2. EngineJobs Map/Reduce");
            sb.AppendLine();
            sb.AppendLine("| 元素 | Map 串行 | Map 并行 | 加速比 | 一致 | Reduce 串行 | Reduce 并行 | Reduce 加速比 | 一致 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var row in m.EngineJobs.MapRows)
                sb.AppendLine("| " + row.Elements + " | " + row.SerialMs + "ms | " + row.ParallelMs + "ms | " + row.Speedup + "x | " + row.Same + " | " + row.ReduceSerialMs + "ms | " + row.ReduceParallelMs + "ms | " + row.ReduceSpeedup + "x | " + row.ReduceSame + " |");
            sb.AppendLine("- Benchmark：" + m.EngineJobs.BenchmarkMapMs + "ms（" + m.EngineJobs.BenchmarkDegree + " 线程）加速 " + m.EngineJobs.BenchmarkSpeedup + "x");
            sb.AppendLine();
            sb.AppendLine("## 3. 判定/校验/对音吞吐");
            sb.AppendLine();
            sb.AppendLine("| 环节 | 规模 | 耗时 | 备注 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine("| JudgementTracker | " + m.Judge.Notes + " 音符 | " + m.Judge.Milliseconds + "ms | 已判定 " + m.Judge.Judged + " · 余 " + m.Judge.Remaining + " |");
            sb.AppendLine("| ChartValidator | " + m.Validate.Notes + " 音符 | " + m.Validate.Milliseconds + "ms | " + m.Validate.Issues + " issues |");
            sb.AppendLine("| BeatAlignEngine | " + m.Align.Seconds + "s@" + m.Align.SampleRate + " | " + m.Align.Milliseconds + "ms | BPM 估计 " + m.Align.BpmEstimate + " |");
            sb.AppendLine();
            sb.AppendLine("## 4. UI 主菜单 60 帧");
            sb.AppendLine();
            sb.AppendLine("| 后端 | 平均 | 尾帧 | P99 | 最大 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine(m.Ui.D2dAvailable ? "| D2D | " + m.Ui.D2dAverageMs + "ms | " + m.Ui.D2dTailMs + "ms | " + m.Ui.D2dP99Ms + "ms | — |" : "| D2D | 未启用 | — | — | — |");
            sb.AppendLine("| GDI（保底） | " + m.Ui.GdiAverageMs + "ms | " + m.Ui.GdiTailMs + "ms | " + m.Ui.GdiP99Ms + "ms | " + m.Ui.GdiMaxMs + "ms |");
            sb.AppendLine();
            sb.AppendLine("## 5. 内存");
            sb.AppendLine();
            sb.AppendLine("- 峰值工作集：**" + m.Memory.PeakWorkingSetMb + " MB**");
            sb.AppendLine("- 解析串行增量：" + m.Parse.MemDeltaSerialMb + " MB · 并行增量：" + m.Parse.MemDeltaParallelMb + " MB");
            sb.AppendLine();
            sb.AppendLine("## 6. 阈值（宽松回归）");
            sb.AppendLine();
            sb.AppendLine("- 解析 >=1.0 MB/s：" + (m.Thresholds.ParseMbPerSecOk ? "PASS" : "FAIL") + "（实测 " + m.Parse.SerialMbPerSec + " MB/s）");
            sb.AppendLine("- UI 平均帧时 <16ms：" + (m.Thresholds.UiAverageFrameOk ? "PASS" : "FAIL") + "（实测 " + (m.Ui.D2dAverageMs.HasValue ? m.Ui.D2dAverageMs.Value : m.Ui.GdiAverageMs) + " ms）");
            sb.AppendLine("- 无 OOM：" + (m.Thresholds.NoOom ? "PASS" : "FAIL"));
            sb.AppendLine();
            sb.AppendLine("**结论：" + (m.Thresholds.Pass ? "通过" : "未通过（见上）") + "**");
            return sb.ToString();
        }
    }

    /// <summary>perftest 数据模型（JSON 序列化载体）。</summary>
    public sealed class PerfModel
    {
        public ParseStageDto Parse = new ParseStageDto();
        public JobsStageDto EngineJobs = new JobsStageDto();
        public JudgeStageDto Judge = new JudgeStageDto();
        public ValidateStageDto Validate = new ValidateStageDto();
        public AlignStageDto Align = new AlignStageDto();
        public UiStageDto Ui = new UiStageDto();
        public ThresholdsDto Thresholds = new ThresholdsDto();
        public MemoryDto Memory = new MemoryDto();
        public string Oom;   // null=无 OOM

        /// <summary>JSON 快照（匿名结构序列化）。</summary>
        public sealed class Snapshot
        {
            public string GeneratedUtc;
            public string SampleSet;
            public ParseStageDto Parse;
            public JobsStageDto EngineJobs;
            public JudgeStageDto Judge;
            public ValidateStageDto Validate;
            public AlignStageDto Align;
            public UiStageDto Ui;
            public ThresholdsDto Thresholds;
            public MemoryDto Memory;

            public Snapshot(PerfModel m)
            {
                GeneratedUtc = DateTime.UtcNow.ToString("O");
                SampleSet = "测试格式";
                Parse = m.Parse; EngineJobs = m.EngineJobs; Judge = m.Judge;
                Validate = m.Validate; Align = m.Align; Ui = m.Ui;
                Thresholds = m.Thresholds; Memory = m.Memory;
            }
        }

        public sealed class ParseStageDto
        {
            public int Files; public long Bytes; public int Rounds;
            public double SerialSeconds; public double ParallelSeconds;
            public double SerialMbPerSec; public double ParallelMbPerSec; public double Speedup;
            public int ParsedCharts; public double MemDeltaSerialMb; public double MemDeltaParallelMb;
        }
        public sealed class JobsStageDto
        {
            public List<MapRowDto> MapRows = new List<MapRowDto>();
            public int BenchmarkDegree; public double BenchmarkMapMs; public double BenchmarkSpeedup;
        }
        public sealed class MapRowDto
        {
            public int Elements;
            public double SerialMs; public double ParallelMs; public double Speedup; public bool Same;
            public double ReduceSerialMs; public double ReduceParallelMs; public double ReduceSpeedup; public bool ReduceSame;
        }
        public sealed class JudgeStageDto { public int Notes; public double Milliseconds; public int Judged; public int Remaining; public double MemDeltaMb; }
        public sealed class ValidateStageDto { public int Notes; public double Milliseconds; public int Issues; public double MemDeltaMb; }
        public sealed class AlignStageDto { public int Seconds; public int SampleRate; public double Milliseconds; public double BpmEstimate; public double MemDeltaMb; }
        public sealed class UiStageDto
        {
            public bool D2dAvailable; public double? D2dAverageMs; public double? D2dTailMs; public double? D2dP99Ms; public string D2dError;
            public double GdiAverageMs; public double GdiTailMs; public double GdiP99Ms; public double GdiMaxMs;
        }
        public sealed class ThresholdsDto
        {
            public double ParseMinMbPerSec; public double UiMaxAverageFrameMs;
            public bool ParseMbPerSecOk; public bool UiAverageFrameOk; public bool NoOom; public bool Pass;
        }
        public sealed class MemoryDto { public double PeakWorkingSetMb; }
    }
}
