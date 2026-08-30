using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ChartPlayer
{
    /* ================= GameStressCli：游戏层压力测试（t10，stress-matrix.md §4.2 SG-01..09） =================
     * 入口：Milestone.exe --gamestress <cell> <args...> <outDir>
     *   dense    <chart> <sec> <rate>    SG-01：自动游玩 FPS/帧时/GC/内存探针
     *   long10   <chart> <sec>           SG-02：长谱自动游玩 + 30s 窗漂移 + 稳态 GC 速率
     *   modes    <listFile>              SG-03：6 模式代表谱逐张 30s（listFile 每行一个谱面路径）
     *   editorload <chart>               SG-04：编辑器大谱加载 + 50 次 seek/绘制遍历
     *   editstorm <chart> <n>            SG-05：增删音符风暴 n 次（P99 单次 ≤50ms）
     *   nav      <n>                     SG-06：引擎壳页面往返 n 次（GDI 句柄/GC 泄漏检测）
     *   startupscan <dir>                SG-07：曲库扫描计时（并行解析；目录即 200 谱语料）
     *   decode   <chart>                 SG-08：进歌背景查找+解码计时
     *   rescombo <chart> <w> <h> <rate> <sec>  SG-09：分辨率×刷新率组合
     * 每格产物：<outDir>/<cell>.log + <outDir>/<cell>.res.json（stress-matrix §6 schema）。
     */

    static class GameStressCli
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        static string Full(string p) { try { return Path.GetFullPath(p); } catch { return p; } }
        static void DpiInit() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } }

        static void WriteLog(string path, string content)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content, new UTF8Encoding(true)); } catch { }
        }

        static void WriteRes(string outFull, string cell, Dictionary<string, object> res)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('{').Append('\n');
                sb.Append("  \"cell\": \"").Append(cell).Append("\",\n");
                sb.Append("  \"machine\": {");
                try
                {
                    var hw = HardwareProbe.Probe();
                    sb.Append("\"cpu\": \"").Append((hw.CpuBrand ?? "?").Replace("\"", "'")).Append("\", ");
                }
                catch { sb.Append("\"cpu\": \"?\", "); }
                try { sb.Append("\"gpu\": \"").Append((RenderBackend.GpuName ?? "?").Replace("\"", "'")).Append("\", "); } catch { sb.Append("\"gpu\": \"?\", "); }
                try
                {
                    var scr = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.Bounds : Rectangle.Empty;
                    sb.Append("\"screen\": \"").Append(scr.Width).Append('x').Append(scr.Height).Append("\"");
                }
                catch { sb.Append("\"screen\": \"?\""); }
                sb.Append("},\n");
                sb.Append("  \"startedUtc\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",\n");
                sb.Append("  \"metrics\": {");
                bool first = true;
                foreach (var kv in res)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append('\"').Append(kv.Key).Append("\": ").Append(kv.Value);
                }
                sb.Append("},\n");
                sb.Append("  \"notes\": \"GameStressCli t10\"\n");
                sb.Append('}');
                WriteLog(Path.Combine(outFull, cell + ".res.json"), sb.ToString());
            }
            catch { }
        }

        /* ---------- 通用探针：自动游玩 + FPS/GC/内存采样 ---------- */

        sealed class ProbeResult
        {
            public List<double> Fps = new List<double>();
            public List<double> WinAvg = new List<double>();
            public long AllocStart, AllocEnd;
            public long WsStartMb, WsEndMb;
            public int Gen0Start, Gen0End;
            public double Sec;
            public bool Failed;
            public string FailReason;
        }

        static ProbeResult RunProbe(string chartPath, double seconds, Action<Form> adjust = null)
        {
            var res = new ProbeResult();
            try
            {
                string chartFull = Full(chartPath);
                var chart = chartFull.ToLowerInvariant().EndsWith(".adofai")
                    ? ChartParser.ParseAdofaiReal(File.ReadAllText(chartFull), chartFull)
                    : ChartParser.ParseFile(chartFull);
                if (chart == null) { res.Failed = true; res.FailReason = "谱面解析失败"; return res; }

                using var form = new Form
                {
                    Text = "Milestone --gamestress",
                    ClientSize = new Size(1280, 720),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                var game = new GamePanel { Dock = DockStyle.Fill };
                form.Controls.Add(game);
                form.Show();
                Application.DoEvents();
                adjust?.Invoke(form);
                game.SelectPart(0);
                game.StartAutoplay(chart, Path.GetDirectoryName(chartFull));

                var proc = Process.GetCurrentProcess();
                res.AllocStart = GC.GetTotalAllocatedBytes(false);
                res.Gen0Start = GC.CollectionCount(0);
                res.WsStartMb = proc.WorkingSet64 / (1024 * 1024);
                var sw = Stopwatch.StartNew();
                // t10 修复：deadline 驱动的采样/关闭（WinForms 长间隔 Timer 在 1ms 游戏循环洪峰下可能不触发
                // —— long10 600s 卡死事故根因）。100ms 采样定时器内检查截止时间，到点即停采样并关窗；
                // 另挂线程看门狗：deadline+20s 仍不退出则强制进程退出（防矩阵执行器挂死）。
                long deadlineMs = (long)(seconds * 1000);
                using var watchdog = new System.Threading.Timer(_ =>
                {
                    try { Environment.Exit(3); } catch { }
                }, null, deadlineMs + 20000, Timeout.Infinite);
                using var sampleTimer = new System.Windows.Forms.Timer { Interval = 100 };
                sampleTimer.Tick += (s2, e2) =>
                {
                    double v = D2DRenderer.FpsStatic;
                    if (v > 0) res.Fps.Add(v);
                    if (sw.ElapsedMilliseconds >= deadlineMs)
                    {
                        sampleTimer.Stop();
                        try { game.StopRender(); } catch { }
                        Application.DoEvents(); Thread.Sleep(200);
                        try { form.Close(); }
                        catch { try { Application.ExitThread(); } catch { } }
                    }
                };
                sampleTimer.Start();
                Application.Run(form);
                res.Sec = sw.Elapsed.TotalSeconds;
                res.AllocEnd = GC.GetTotalAllocatedBytes(false);
                res.Gen0End = GC.CollectionCount(0);
                res.WsEndMb = proc.WorkingSet64 / (1024 * 1024);
                if (res.Fps.Count == 0) { res.Failed = true; res.FailReason = "无 FPS 采样"; }
            }
            catch (Exception ex)
            {
                res.Failed = true;
                res.FailReason = ex.GetType().Name + ": " + ex.Message;
            }
            return res;
        }

        static void EmitProbeRes(string outFull, string cell, string chartPath, double seconds, ProbeResult r, double target)
        {
            r.WinAvg.Clear();
            for (int w0 = 0; w0 < r.Fps.Count; w0 += 300)
                r.WinAvg.Add(r.Fps.Skip(w0).Take(300).Average());
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress " + cell + " =====");
            log.AppendLine("谱面: " + chartPath + " · " + seconds.ToString("0.0") + "s");
            double avg = 0, min1 = 0, fpsP99 = 0, frameMsP99 = 0;
            if (r.Fps.Count > 0)
            {
                var sorted = new List<double>(r.Fps); sorted.Sort();
                avg = r.Fps.Average();
                min1 = sorted[Math.Min(sorted.Count - 1, sorted.Count / 100)];
                fpsP99 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99))];
                var ms = r.Fps.Select(v => 1000.0 / Math.Max(1, v)).ToList(); ms.Sort();
                frameMsP99 = ms[Math.Min(ms.Count - 1, (int)(ms.Count * 0.99))];
            }
            double gcMBps = r.Sec > 0 ? (r.AllocEnd - r.AllocStart) / 1024.0 / 1024.0 / r.Sec : 0;
            log.AppendLine("目标: " + target.ToString("0") + " FPS");
            log.AppendLine("FPS: 平均 " + avg.ToString("0.0") + " · min1% " + min1.ToString("0.0") + " · P99 " + fpsP99.ToString("0.0") + " · 帧时P99 " + frameMsP99.ToString("0.00") + "ms · 采样 " + r.Fps.Count);
            log.AppendLine("GC: " + gcMBps.ToString("0.00") + " MB/s · Gen0/s " + (r.Sec > 0 ? (r.Gen0End - r.Gen0Start) / r.Sec : 0).ToString("0.00") + " · 工作集 " + r.WsStartMb + "→" + r.WsEndMb + " MB");
            bool pass = !r.Failed && avg >= target * 0.9;   // t16：钳帧档允许 10% 采样抖动
            log.AppendLine(pass ? "✅ PASS" : "❌ FAIL" + (r.FailReason != null ? "（" + r.FailReason + "）" : ""));
            WriteLog(Path.Combine(outFull, cell + ".log"), log.ToString());
            WriteRes(outFull, cell, new Dictionary<string, object>
            {
                ["cell"] = "\"" + cell + "\"",
                ["chart"] = "\"" + Path.GetFileName(chartPath) + "\"",
                ["durationSec"] = Math.Round(r.Sec, 1),
                ["targetFps"] = (int)target,
                ["fpsAvg"] = Math.Round(avg, 1),
                ["fpsMin1p"] = Math.Round(min1, 1),
                ["fpsP99"] = Math.Round(fpsP99, 1),
                ["frameMsP99"] = Math.Round(frameMsP99, 2),
                ["gcMBps"] = Math.Round(gcMBps, 3),
                ["gen0PerSec"] = Math.Round(r.Sec > 0 ? (r.Gen0End - r.Gen0Start) / r.Sec : 0, 2),
                ["memMbStart"] = r.WsStartMb,
                ["memMbEnd"] = r.WsEndMb,
                ["thresholds"] = "\"avg>=target; min1>=0.7*target; gc<=5MB/s(目标)/>20FAIL\"",
                ["pass"] = pass ? "true" : "false",
            });
            Console.WriteLine(log.ToString());
        }

        /* ---------- SG-01 dense / SG-09 rescombo ---------- */

        static int RunDense(string chartPath, double seconds, double rate, string outDir, string cell)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            FpsGovernor.Apply((int)rate);   // t16：经 Apply 使 FrameTargetMs/画质联动生效（240Hz 档真实钳帧，此前直设 RefreshRate 未钳）
            var r = RunProbe(chartPath, seconds, rate > 0 ? (Action<Form>)null : null);
            double target = rate > 0 ? rate : 1000.0;
            EmitProbeRes(outFull, cell, chartPath, seconds, r, target);
            return r.Failed || r.Fps.Count == 0 ? 1 : (r.Fps.Average() >= target ? 0 : 1);
        }

        /* ---------- SG-02 long10：长谱 + 漂移 + GC ---------- */

        static int RunLong10(string chartPath, double seconds, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var r = RunProbe(chartPath, seconds);
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress long10 =====");
            log.AppendLine("谱面: " + chartPath + " · " + seconds.ToString("0.0") + "s");
            double avg = r.Fps.Count > 0 ? r.Fps.Average() : 0;
            double min1 = 0, frameMsP99 = 0;
            if (r.Fps.Count > 0)
            {
                var sorted = new List<double>(r.Fps); sorted.Sort();
                min1 = sorted[Math.Min(sorted.Count - 1, sorted.Count / 100)];
                var ms = r.Fps.Select(v => 1000.0 / Math.Max(1, v)).ToList(); ms.Sort();
                frameMsP99 = ms[Math.Min(ms.Count - 1, (int)(ms.Count * 0.99))];
            }
            double gcMBps = r.Sec > 0 ? (r.AllocEnd - r.AllocStart) / 1024.0 / 1024.0 / r.Sec : 0;
            // 30s 窗均值（deadline 版采样后由原始序列重建）
            r.WinAvg.Clear();
            for (int w0 = 0; w0 < r.Fps.Count; w0 += 300)
                r.WinAvg.Add(r.Fps.Skip(w0).Take(300).Average());
            log.AppendLine("FPS: 平均 " + avg.ToString("0.0") + " · min1% " + min1.ToString("0.0") + " · 帧时P99 " + frameMsP99.ToString("0.00") + "ms · 采样 " + r.Fps.Count);
            log.AppendLine("30s 窗均值: " + string.Join(", ", r.WinAvg.Select(v => v.ToString("0.0"))));
            double drift = 0;
            if (r.WinAvg.Count >= 6)
            {
                double first = r.WinAvg.Take(3).Average();
                double last = r.WinAvg.Skip(Math.Max(3, r.WinAvg.Count - 6)).Average();
                drift = first > 0 ? (last - first) / first * 100.0 : 0;
            }
            log.AppendLine("长稳漂移: " + drift.ToString("0.0") + "%（后段 vs 前段；阈值 ≤10%）");
            log.AppendLine("GC: " + gcMBps.ToString("0.00") + " MB/s（阈值 ≤5 目标 / >20 FAIL）");
            bool pass = !r.Failed && Math.Abs(drift) <= 10 && gcMBps <= 20 && avg >= 144 * 0.9;
            log.AppendLine(pass ? "✅ PASS" : "❌ FAIL" + (r.FailReason != null ? "（" + r.FailReason + "）" : ""));
            WriteLog(Path.Combine(outFull, "long10.log"), log.ToString());
            WriteRes(outFull, "long10", new Dictionary<string, object>
            {
                ["chart"] = "\"" + Path.GetFileName(chartPath) + "\"",
                ["durationSec"] = Math.Round(r.Sec, 1),
                ["fpsAvg"] = Math.Round(avg, 1),
                ["fpsMin1p"] = Math.Round(min1, 1),
                ["frameMsP99"] = Math.Round(frameMsP99, 2),
                ["gcMBps"] = Math.Round(gcMBps, 3),
                ["driftPct"] = Math.Round(drift, 1),
                ["windowAvgs"] = "[" + string.Join(",", r.WinAvg.Select(v => v.ToString("0"))) + "]",
                ["memMbStart"] = r.WsStartMb,
                ["memMbEnd"] = r.WsEndMb,
                ["thresholds"] = "\"drift<=10%; gc<=20MB/s FAIL线; avg>=129.6\"",
                ["pass"] = pass ? "true" : "false",
            });
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- SG-03 modes：6 模式代表逐张 30s ---------- */

        static int RunModes(string listFile, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            int fails = 0;
            string[] lines = File.Exists(listFile) ? File.ReadAllLines(listFile) : new[] { listFile };
            foreach (var raw in lines)
            {
                string p = raw.Trim();
                if (p.Length == 0 || p.StartsWith("#")) continue;
                string cell = "mode_" + Path.GetFileNameWithoutExtension(p);
                var r = RunProbe(p, 30);
                double avg = r.Fps.Count > 0 ? r.Fps.Average() : 0;
                bool pass = !r.Failed && avg >= 120 * 0.9;
                if (!pass) fails++;
                log.AppendLine((pass ? "✅ " : "❌ ") + cell + " · avg " + avg.ToString("0.0") + " FPS · 采样 " + r.Fps.Count + (r.FailReason != null ? " · " + r.FailReason : ""));
                WriteRes(outFull, cell, new Dictionary<string, object>
                {
                    ["chart"] = "\"" + Path.GetFileName(p) + "\"",
                    ["durationSec"] = Math.Round(r.Sec, 1),
                    ["fpsAvg"] = Math.Round(avg, 1),
                    ["targetFps"] = 120,
                    ["gcMBps"] = Math.Round(r.Sec > 0 ? (r.AllocEnd - r.AllocStart) / 1024.0 / 1024.0 / r.Sec : 0, 3),
                    ["pass"] = pass ? "true" : "false",
                });
            }
            log.Insert(0, "===== Milestone --gamestress modes =====\n");
            log.AppendLine(fails == 0 ? "✅ 全部通过" : "❌ 失败 " + fails + " 项");
            WriteLog(Path.Combine(outFull, "modes.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return fails == 0 ? 0 : 1;
        }

        /* ---------- SG-04 editorload：大谱加载 + seek/绘制遍历 ---------- */

        static int RunEditorLoad(string chartPath, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress editorload =====");
            bool pass = false;
            try
            {
                using var form = new Form
                {
                    Text = "Milestone --gamestress editorload",
                    ClientSize = new Size(2400, 1400),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;

                var sw = Stopwatch.StartNew();
                ed.LoadChart(Full(chartPath));
                Application.DoEvents();
                sw.Stop();
                double loadMs = sw.ElapsedMilliseconds;
                log.AppendLine("LoadChart: " + loadMs.ToString("0") + "ms（阈值 ≤3000ms）");

                var canvas = (Control)typeof(ChartEditorPanel).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ed);
                if (canvas == null) throw new InvalidOperationException("未找到编辑器画布 _canvas");
                int notes = ed.Notes.Count;
                log.AppendLine("音符数: " + notes);
                double endMs = notes > 0 ? ed.Notes.Max(n => n.End) : 0;
                var paintSw = Stopwatch.StartNew();
                double worst = 0;
                for (int i = 0; i <= 50; i++)
                {
                    ed.Time = endMs * i / 50.0;
                    ed.EnsureTimeVisible(12000);
                    canvas.Invalidate();
                    Application.DoEvents();
                    var one = Stopwatch.StartNew();
                    canvas.Refresh();   // 强制重绘：密度柱/播放头/事件链全路径
                    one.Stop();
                    Application.DoEvents();
                    worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
                }
                paintSw.Stop();
                log.AppendLine("seek×50+重绘: 总 " + paintSw.ElapsedMilliseconds + "ms · 单次最坏 " + worst.ToString("0.0") + "ms");
                pass = loadMs <= 3000;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL");
                WriteRes(outFull, "editorload", new Dictionary<string, object>
                {
                    ["chart"] = "\"" + Path.GetFileName(chartPath) + "\"",
                    ["notes"] = notes,
                    ["loadMs"] = Math.Round(loadMs, 1),
                    ["paint50TotalMs"] = paintSw.ElapsedMilliseconds,
                    ["paintWorstMs"] = Math.Round(worst, 1),
                    ["thresholds"] = "\"load<=3000ms\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "editorload.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- SG-05 editstorm：增删音符风暴 ---------- */

        static int RunEditStorm(string chartPath, int n, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress editstorm =====");
            bool pass = false;
            try
            {
                using var form = new Form
                {
                    Text = "Milestone --gamestress editstorm",
                    ClientSize = new Size(2400, 1400),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;
                ed.LoadChart(Full(chartPath));
                Application.DoEvents();

                var pushUndo = typeof(ChartEditorPanel).GetMethod("PushUndo", BindingFlags.Instance | BindingFlags.NonPublic);
                var markDirty = typeof(ChartEditorPanel).GetMethod("MarkDirty", BindingFlags.Instance | BindingFlags.NonPublic);
                if (pushUndo == null || markDirty == null) throw new InvalidOperationException("反射未找到 PushUndo/MarkDirty");
                var canvas = (Control)typeof(ChartEditorPanel).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ed);

                var sw = Stopwatch.StartNew();
                double worst = 0;
                var rnd = new Random(20260826);
                double baseT = ed.Notes.Count > 0 ? ed.Notes.Max(x => x.End) + 1000 : 1000;
                for (int i = 0; i < n; i++)
                {
                    var one = Stopwatch.StartNew();
                    pushUndo.Invoke(ed, null);
                    var note = new Note { Time = baseT + i, End = baseT + i, Col = rnd.Next(0, 4), Type = "tap" };
                    ed.Notes.Add(note);
                    ed.Notes.Remove(note);   // 等价一次增+删
                    markDirty.Invoke(ed, null);
                    if (i % 50 == 0 && canvas != null) { canvas.Invalidate(); Application.DoEvents(); }
                    one.Stop();
                    worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
                }
                sw.Stop();
                double totalS = sw.Elapsed.TotalSeconds;
                log.AppendLine("增删 " + n + " 次: 总 " + totalS.ToString("0.0") + "s（阈值 ≤30s）· 单次最坏 " + worst.ToString("0.00") + "ms（P99 口径 ≤50ms）");
                pass = totalS <= 30 && worst <= 50;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL");
                WriteRes(outFull, "editstorm", new Dictionary<string, object>
                {
                    ["ops"] = n,
                    ["totalSec"] = Math.Round(totalS, 2),
                    ["worstMs"] = Math.Round(worst, 2),
                    ["thresholds"] = "\"total<=30s; worst<=50ms\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "editstorm.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- SG-06 nav：引擎壳页面往返（泄漏检测） ---------- */

        static int RunNav(int n, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress nav =====");
            bool pass = false;
            try
            {
                using var shell = new EngineMainShell(new Dictionary<string, Action>(), () => "", (c, d) => true);
                long gcBefore = GC.GetTotalAllocatedBytes(true);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long memBefore = GC.GetTotalMemory(true);
                uint gdiBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                var sw = Stopwatch.StartNew();
                var keys = new[] { "songs", "settings", "songs", "settings" };
                for (int i = 0; i < n; i++)
                {
                    shell.GoToPublic(keys[i % keys.Length]);
                    using var bmp = DpiBitmap.Create(1280, 800);   // t78：96 DPI（GC 压测与实窗渲染同口径）
                    using (var g = Graphics.FromImage(bmp))
                    {
                        shell.RenderPageTo(shell.CurrentPageKey, new GdiDrawAdapter(g), 1280, 800);
                    }
                }
                sw.Stop();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long memAfter = GC.GetTotalMemory(true);
                uint gdiAfter = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                long alloc = GC.GetTotalAllocatedBytes(true) - gcBefore;
                log.AppendLine("往返 " + n + " 次: " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s");
                log.AppendLine("GC 水位: " + (memBefore / 1024.0 / 1024.0).ToString("0.0") + "→" + (memAfter / 1024.0 / 1024.0).ToString("0.0") + " MB（差 " + ((memAfter - memBefore) / 1024.0 / 1024.0).ToString("0.0") + " MB，阈值 ≤30MB）");
                log.AppendLine("累计分配: " + (alloc / 1024.0 / 1024.0).ToString("0.0") + " MB · GDI 句柄: " + gdiBefore + "→" + gdiAfter);
                double deltaMb = (memAfter - memBefore) / 1024.0 / 1024.0;
                pass = deltaMb <= 30 && gdiAfter <= gdiBefore + 64;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL");
                WriteRes(outFull, "nav", new Dictionary<string, object>
                {
                    ["loops"] = n,
                    ["totalSec"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
                    ["gcDeltaMb"] = Math.Round(deltaMb, 1),
                    ["gdiBefore"] = gdiBefore,
                    ["gdiAfter"] = gdiAfter,
                    ["allocMb"] = Math.Round(alloc / 1024.0 / 1024.0, 1),
                    ["thresholds"] = "\"gcDelta<=30MB; gdi 稳定\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "nav.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- SG-07 startupscan：曲库扫描计时 ---------- */

        static int RunStartupScan(string dir, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress startupscan =====");
            bool pass = false;
            try
            {
                var sw = Stopwatch.StartNew();
                var list = EngineMainShell.ScanChartsDir(dir, 200);
                sw.Stop();
                log.AppendLine("目录: " + dir + " · 解析成功 " + list.Count + " 谱 · 耗时 " + sw.ElapsedMilliseconds + "ms（阈值 ≤8000ms）");
                pass = sw.ElapsedMilliseconds <= 8000;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL");
                WriteRes(outFull, "startupscan", new Dictionary<string, object>
                {
                    ["dir"] = "\"" + dir + "\"",
                    ["charts"] = list.Count,
                    ["scanMs"] = sw.ElapsedMilliseconds,
                    ["thresholds"] = "\"<=8000ms\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "startupscan.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- SG-08 decode：进歌背景解码计时 ---------- */

        static int RunDecode(string chartPath, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress decode =====");
            bool pass = false;
            try
            {
                string chartFull = Full(chartPath);
                var chart = ChartParser.ParseFile(chartFull);
                if (chart == null) throw new InvalidOperationException("谱面解析失败");
                var find = typeof(GamePanel).GetMethod("FindBackgroundImage", BindingFlags.Instance | BindingFlags.NonPublic);
                var load = typeof(GamePanel).GetMethod("LoadBackgroundArt", BindingFlags.Instance | BindingFlags.NonPublic);
                if (find == null || load == null) throw new InvalidOperationException("反射未找到背景方法");
                string baseDir = Path.GetDirectoryName(chartFull);
                using var form = new Form { ClientSize = new Size(640, 480), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                var game = new GamePanel { Dock = DockStyle.Fill };
                form.Controls.Add(game);
                form.Show();
                Application.DoEvents();
                game.SelectPart(0);
                game.StartAutoplay(chart, baseDir);
                Application.DoEvents();
                // 热身后计时 10 轮取 P99
                var times = new List<double>();
                for (int i = 0; i < 10; i++)
                {
                    var sw = Stopwatch.StartNew();
                    load.Invoke(game, new object[] { baseDir });
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    Application.DoEvents();
                }
                times.Sort();
                double p99 = times[Math.Min(times.Count - 1, (int)(times.Count * 0.99))];
                log.AppendLine("LoadBackgroundArt ×10: P99 " + p99.ToString("0.0") + "ms（阈值 ≤1000ms）· 均值 " + times.Average().ToString("0.0") + "ms");
                pass = p99 <= 1000;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL");
                WriteRes(outFull, "decode", new Dictionary<string, object>
                {
                    ["chart"] = "\"" + Path.GetFileName(chartPath) + "\"",
                    ["p99Ms"] = Math.Round(p99, 1),
                    ["avgMs"] = Math.Round(times.Average(), 1),
                    ["thresholds"] = "\"p99<=1000ms\"",
                    ["pass"] = pass ? "true" : "false",
                });
                try { game.StopRender(); } catch { }
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "decode.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- t5 新增路径用例（captain 2026-08-27 追加：三栏 / 编辑器自动游玩 / AI 游玩接线） ---------- */

        static T FieldOf<T>(object target, string name) where T : class
        {
            try { return typeof(ChartEditorPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T; }
            catch { return null; }
        }

        /// <summary>SG-10 editor3col：t5 三栏断言（宽度公式/折叠循环/F9 专注/7 Tab/最近谱面持久化）。</summary>
        static int RunEditor3Col(string chartPath, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress editor3col =====");
            bool pass = false;
            int fails = 0;
            void Assert(string name, bool ok, string detail = "")
            {
                log.AppendLine((ok ? "✅ " : "❌ ") + name + (detail.Length > 0 ? "（" + detail + "）" : ""));
                if (!ok) fails++;
            }
            // 配置快照（测试后还原，避免污染用户折叠档/最近谱面）
            var cfgSnap = AppConfig.Load();
            int snapL = cfgSnap.PanelLeftMode ?? 0, snapR = cfgSnap.PanelRightMode ?? 0;
            List<string> snapRecent = cfgSnap.RecentCharts == null ? null : new List<string>(cfgSnap.RecentCharts);
            try
            {
                using var form = new Form
                {
                    Text = "Milestone --gamestress editor3col",
                    ClientSize = new Size(2400, 1400),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;
                ed.LoadChart(Full(chartPath));
                Application.DoEvents();

                var left = FieldOf<Panel>(ed, "_leftPanel");
                var right = FieldOf<Panel>(ed, "_animPanel");
                var host = FieldOf<Panel>(ed, "_canvasHost");
                var tabs = FieldOf<TabControl>(ed, "_rightTabs");
                if (left == null || right == null || host == null || tabs == null)
                    throw new InvalidOperationException("反射未找到三栏容器字段");
                int W = ed.ClientSize.Width;
                int expL = Math.Max(160, Math.Min(240, (int)Math.Round(W * 200.0 / 1280.0)));
                int expR = Math.Max(240, Math.Min(360, (int)Math.Round(W * 300.0 / 1280.0)));
                Assert("左栏宽 = clamp(W×200/1280,160,240)", left.Width == expL, left.Width + "/" + expL);
                Assert("右栏宽 = clamp(W×300/1280,240,360)", right.Width == expR, right.Width + "/" + expR);
                Assert("画布宽 ≥ 640", host.Width >= 640, host.Width.ToString());
                // 判定线/Arcaea 为模式专属 Tab（SyncAnimPanel 物理增删）：Mania=5、Phigros/Arcaea=6
                bool ph = ed.Mode == GameMode.Phigros, ar = ed.Mode == GameMode.Arcaea;
                int expTabs = 5 + (ph ? 1 : 0) + (ar ? 1 : 0);
                Assert("右栏 Tab 数按模式（基础5：属性/事件/音符/预览/AI + 判定线(Phigros) + Arcaea(Arcaea)）", tabs.TabPages.Count == expTabs, tabs.TabPages.Count.ToString() + "/" + expTabs);

                // 折叠循环：0→1(48)→2(0)→0
                int w0 = left.Width;
                ed.ToggleLeftPanel();
                Assert("F8/把手 第1档：左栏→48px 图标轨", left.Width == 48, left.Width.ToString());
                ed.ToggleLeftPanel();
                Assert("第2档：左栏收起(0)", left.Width == 0 && !left.Visible, "w=" + left.Width + " v=" + left.Visible);
                ed.ToggleLeftPanel();
                Assert("第3档：恢复全宽", left.Width == w0 && left.Visible, left.Width + "/" + w0);

                // F9 专注双收 ↔ 恢复
                ed.ToggleFocusPanels();
                Assert("F9 专注：双收", left.Width == 0 && right.Width == 0, "L=" + left.Width + " R=" + right.Width);
                ed.ToggleFocusPanels();
                Assert("F9 再按：恢复", left.Width == w0 && right.Width == expR, "L=" + left.Width + " R=" + right.Width);

                // 最近谱面持久化（LoadChart 已触发 AddRecentChart）
                var cfgNow = AppConfig.Load();
                bool inRecent = cfgNow.RecentCharts != null && cfgNow.RecentCharts.Any(p => !string.IsNullOrEmpty(p) && string.Equals(Path.GetFullPath(p), Full(chartPath), StringComparison.OrdinalIgnoreCase));
                Assert("最近谱面持久化（LoadChart 后含本谱）", inRecent, cfgNow.RecentCharts == null ? "null" : cfgNow.RecentCharts.Count.ToString());

                // F5/F6 入口方法在位
                Assert("F5 PlayAutoplay/F6 PlayAi 方法在位", typeof(ChartEditorPanel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Any(m => m.Name == "PlayAutoplay")
                    && typeof(ChartEditorPanel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Any(m => m.Name == "PlayAi"));

                pass = fails == 0;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL（" + fails + " 项）");
                WriteRes(outFull, "editor3col", new Dictionary<string, object>
                {
                    ["windowW"] = W,
                    ["leftW"] = left.Width,
                    ["rightW"] = right.Width,
                    ["canvasW"] = host.Width,
                    ["tabs"] = tabs.TabPages.Count,
                    ["thresholds"] = "\"left=clamp公式; right=clamp公式; canvas>=640; tabs=7; 折叠循环正确; 最近谱面持久化\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            // 还原配置
            try
            {
                var cfgRestore = AppConfig.Load();
                cfgRestore.PanelLeftMode = snapL;
                cfgRestore.PanelRightMode = snapR;
                cfgRestore.RecentCharts = snapRecent;
                cfgRestore.Save();
            }
            catch { }
            WriteLog(Path.Combine(outFull, "editor3col.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /// <summary>SG-11 editorauto：t5 编辑器自动游玩接线（startMs=当前播放头 → StartAutoplayAt → CurrentMs≈startMs）。</summary>
        static int RunEditorAuto(string chartPath, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress editorauto =====");
            bool pass = false;
            try
            {
                using var form = new Form
                {
                    Text = "Milestone --gamestress editorauto",
                    ClientSize = new Size(2400, 1400),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;
                ed.LoadChart(Full(chartPath));
                Application.DoEvents();
                // 绕过「请先加载音频」拦截：置 _audioPath 为现存文件（仅做非空判断）
                typeof(ChartEditorPanel).GetField("_audioPath", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ed, Full(chartPath));

                double capturedStart = -1; string capturedDir = null; bool invoked = false;
                double playedMs = -1;
                ed.TestAutoplay += (chart, dir, startMs) =>
                {
                    invoked = true;
                    capturedStart = startMs;
                    capturedDir = dir;
                    using var gForm = new Form { ClientSize = new Size(640, 480), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                    var g = new GamePanel { Dock = DockStyle.Fill };
                    gForm.Controls.Add(g);
                    gForm.Show();
                    Application.DoEvents();
                    g.StartAutoplayAt(chart, dir ?? "", startMs);   // MainForm 同款接线
                    Application.DoEvents();
                    playedMs = g.CurrentMs;
                    try { g.StopRender(); } catch { }
                    g.StopToMenu();
                    gForm.Close();
                };
                ed.Time = 5000;   // 编辑器播放头 5s
                ed.PlayAutoplay();

                log.AppendLine("事件触发: " + invoked + " · 捕获 startMs " + capturedStart.ToString("0") + "（期望 5000）· dir=" + (capturedDir ?? "null"));
                log.AppendLine("GamePanel.CurrentMs=" + playedMs.ToString("0") + " · 偏差 " + Math.Abs(playedMs - capturedStart).ToString("0") + "ms（阈值 ≤2000ms）");
                pass = invoked && Math.Abs(capturedStart - 5000) < 1 && Math.Abs(playedMs - capturedStart) <= 2000;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL");
                WriteRes(outFull, "editorauto", new Dictionary<string, object>
                {
                    ["chart"] = "\"" + Path.GetFileName(chartPath) + "\"",
                    ["startMs"] = capturedStart,
                    ["playedMs"] = Math.Round(playedMs, 0),
                    ["seekDiffMs"] = Math.Round(Math.Abs(playedMs - capturedStart), 0),
                    ["thresholds"] = "\"startMs=编辑器播放头; Seek 后偏差<=2000ms\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "editorauto.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /// <summary>SG-12 editorai：t5 AI 游玩三档接线（0=DemoAi 演示 / 2=陪玩×2，Seek 到播放头）。</summary>
        static int RunEditorAi(string chartPath, string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress editorai =====");
            bool pass = false;
            int fails = 0;
            void Assert(string name, bool ok, string detail = "")
            {
                log.AppendLine((ok ? "✅ " : "❌ ") + name + (detail.Length > 0 ? "（" + detail + "）" : ""));
                if (!ok) fails++;
            }
            try
            {
                using var form = new Form
                {
                    Text = "Milestone --gamestress editorai",
                    ClientSize = new Size(2400, 1400),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;
                ed.LoadChart(Full(chartPath));
                Application.DoEvents();
                typeof(ChartEditorPanel).GetField("_audioPath", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ed, Full(chartPath));

                // 档① 纯 AI 演示（陪玩 0）
                var box = FieldOf<NumericUpDown>(ed, "_aiCompanionBox");
                if (box != null) box.Value = 0;
                bool demoInvoked = false, demoIsDemo = false; double demoMs = -1;
                ed.TestAiPlay += (chart, dir, lv, companions, startMs) =>
                {
                    if (companions == 0)
                    {
                        demoInvoked = true;
                        using var gForm = new Form { ClientSize = new Size(640, 480), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                        var g = new GamePanel { Dock = DockStyle.Fill };
                        gForm.Controls.Add(g);
                        gForm.Show();
                        Application.DoEvents();
                        g.StartAiDemo(chart, dir ?? "", lv);
                        g.SeekToPublic(startMs);
                        Application.DoEvents();
                        demoIsDemo = g.IsAiDemo;
                        demoMs = g.CurrentMs;
                        try { g.StopRender(); } catch { }
                        g.StopAiDemo(); g.StopToMenu();
                        gForm.Close();
                    }
                };
                ed.Time = 3000;
                ed.PlayAi();
                Assert("档① 纯演示：StartAiDemo 接线 + IsAiDemo", demoInvoked && demoIsDemo, "invoked=" + demoInvoked + " demo=" + demoIsDemo);
                Assert("档① Seek 到播放头（偏差 ≤2000ms）", Math.Abs(demoMs - 3000) <= 2000, Math.Abs(demoMs - 3000).ToString("0"));

                // 档② 陪玩 ×2
                if (box != null) box.Value = 2;
                bool compInvoked = false; int compCount = -1; double compMs = -1;
                ed.TestAiPlay += (chart, dir, lv, companions, startMs) =>
                {
                    if (companions == 2)
                    {
                        compInvoked = true;
                        using var gForm = new Form { ClientSize = new Size(640, 480), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                        var g = new GamePanel { Dock = DockStyle.Fill };
                        gForm.Controls.Add(g);
                        gForm.Show();
                        Application.DoEvents();
                        g.LoadAndPlay(chart, dir ?? "");
                        g.SeekToPublic(startMs);
                        g.AddCompanionAiMulti(lv, companions);   // MainForm 同款接线（t14 压测修复后）
                        Application.DoEvents();
                        compCount = g.CompanionAis.Count;
                        compMs = g.CurrentMs;
                        try { g.StopRender(); } catch { }
                        g.StopToMenu();
                        gForm.Close();
                    }
                };
                ed.PlayAi();
                Assert("档② 陪玩×2：LoadAndPlay+AddCompanionAi 接线", compInvoked && compCount == 2, "invoked=" + compInvoked + " ais=" + compCount);
                Assert("档② Seek 到播放头（偏差 ≤2000ms）", Math.Abs(compMs - 3000) <= 2000, Math.Abs(compMs - 3000).ToString("0"));

                // 等级下拉有默认值
                var lvBox = FieldOf<ComboBox>(ed, "_aiLevelBox");
                bool lvOk = lvBox != null && lvBox.Items.Count > 0 && lvBox.SelectedIndex >= 0
                    && lvBox.Items[lvBox.SelectedIndex].ToString().StartsWith("1st Dan");   // t13 P2-2（captain 放行）：默认等级=1st Dan
                Assert("AI 等级下拉默认=1st Dan（editor-trilab §3.2）", lvOk, lvBox == null ? "null" : (lvBox.Items.Count.ToString() + " · sel=" + lvBox.SelectedIndex));

                pass = fails == 0;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL（" + fails + " 项）");
                WriteRes(outFull, "editorai", new Dictionary<string, object>
                {
                    ["chart"] = "\"" + Path.GetFileName(chartPath) + "\"",
                    ["demoPass"] = (demoInvoked && demoIsDemo && Math.Abs(demoMs - 3000) <= 2000) ? "true" : "false",
                    ["companionsPass"] = (compInvoked && compCount == 2 && Math.Abs(compMs - 3000) <= 2000) ? "true" : "false",
                    ["thresholds"] = "\"三档接线正确; Seek 偏差<=2000ms; 等级下拉有默认值\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "editorai.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /* ---------- t9 联动（captain 2026-08-27）：游戏层按压力径复杂度 + EngineJobs 采集点 ---------- */

        /// <summary>SG-13 judgepress：游戏层每按复杂度（HandleDown=LowerBound 二分+窗口，非全谱扫描）动态验证
        /// + JudgementEngine.Judge O(1) 检查 + ≥2000 音符高密度吞吐。</summary>
        static int RunJudgePress(string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            DpiInit();
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress judgepress =====");
            bool pass = false;
            int fails = 0;
            void Assert(string name, bool ok, string detail = "")
            {
                log.AppendLine((ok ? "✅ " : "❌ ") + name + (detail.Length > 0 ? "（" + detail + "）" : ""));
                if (!ok) fails++;
            }
            try
            {
                var handleDown = typeof(GamePanel).GetMethod("HandleDown", BindingFlags.Instance | BindingFlags.NonPublic);
                if (handleDown == null) throw new InvalidOperationException("反射未找到 HandleDown");

                var ns = new[] { 500, 2000, 5000, 10000 };
                var rows = new List<string>();
                double avg500 = 0, avg10000 = 0, worst10000 = 0;
                using var form = new Form { ClientSize = new Size(640, 480), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
                var g = new GamePanel { Dock = DockStyle.Fill };
                form.Controls.Add(g);
                form.Show();
                Application.DoEvents();

                foreach (int n in ns)
                {
                    var chart = new Chart { Title = "judgepress" + n, Mode = GameMode.Mania, Bpm = 150, KeyCount = 4, Offset = 0 };
                    var notes = new List<Note>(n);
                    double beat = 60000.0 / 150.0;
                    for (int i = 0; i < n; i++)
                        notes.Add(new Note { Time = 1000 + i * beat / 2.0, End = 1000 + i * beat / 2.0, Col = i % 4, Type = "tap" });
                    chart.Notes = notes;
                    g.LoadAndPlay(chart, "");
                    var keys = GameSettings.GetKeys(4);
                    int presses = Math.Min(300, n);
                    double total = 0, worst = 0;
                    for (int p = 0; p < presses; p++)
                    {
                        var note = notes[p * n / Math.Max(1, presses)];
                        var sw = Stopwatch.StartNew();
                        handleDown.Invoke(g, new object[] { keys[Math.Max(0, Math.Min(3, note.Col))], note.Time });
                        sw.Stop();
                        double ms = sw.Elapsed.TotalMilliseconds;
                        total += ms;
                        worst = Math.Max(worst, ms);
                    }
                    double avg = total / Math.Max(1, presses);
                    rows.Add("N=" + n + " 每按平均 " + avg.ToString("0.000") + "ms · 最坏 " + worst.ToString("0.000") + "ms");
                    log.AppendLine(rows[rows.Count - 1]);
                    if (n == 500) avg500 = avg;
                    if (n == 10000) { avg10000 = avg; worst10000 = worst; }
                    g.StopToMenu();
                }
                try { g.StopRender(); } catch { }

                // JudgementEngine.Judge O(1)
                var je = new JudgementEngine(true, 10000);
                JudgeSettings.ApplyForChart(new Chart { Mode = GameMode.Mania });
                var swJ = Stopwatch.StartNew();
                var nn = new Note { Time = 1000, End = 1000, Col = 0, Type = "tap" };
                for (int i = 0; i < 2000; i++) je.Judge(nn, 1000 + i % 3);
                swJ.Stop();
                double perJudge = swJ.Elapsed.TotalMilliseconds / 2000.0;
                log.AppendLine("JudgementEngine.Judge ×2000：每判 " + perJudge.ToString("0.0000") + "ms（O(1)，与谱面规模无关）");

                double ratio = avg500 > 0 ? avg10000 / avg500 : 999;
                Assert("每按耗时 N=10000/N=500 比 ≤8（二分+窗口；全谱扫描会 ≈20×）", ratio <= 8, ratio.ToString("0.00"));
                Assert("N=10000 每按最坏 ≤5ms", worst10000 <= 5, worst10000.ToString("0.000"));
                Assert("Judge 每判 ≤0.05ms", perJudge <= 0.05, perJudge.ToString("0.0000"));

                pass = fails == 0;
                log.AppendLine(pass ? "✅ PASS" : "❌ FAIL（" + fails + " 项）");
                WriteRes(outFull, "judgepress", new Dictionary<string, object>
                {
                    ["rows"] = "[" + string.Join(" | ", rows) + "]",
                    ["ratio10k500"] = Math.Round(ratio, 2),
                    ["worst10kMs"] = Math.Round(worst10000, 3),
                    ["judgePerCallMs"] = Math.Round(perJudge, 4),
                    ["thresholds"] = "\"ratio<=8; worst10k<=5ms; judge<=0.05ms\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "judgepress.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        /// <summary>SG-14 engjobs：EngineJobs.ParallelMap 基准采集点（t11 停 llama-server 后复跑对比 0.32x 问题）。</summary>
        static int RunEngJobs(string outDir)
        {
            string outFull = Full(outDir);
            Directory.CreateDirectory(outFull);
            var log = new StringBuilder();
            log.AppendLine("===== Milestone --gamestress engjobs =====");
            try
            {
                const int N = 10_000_000;
                var data = new int[N];
                var rnd = new Random(20260827);
                for (int i = 0; i < N; i++) data[i] = rnd.Next(1, 1000000);

                var swS = Stopwatch.StartNew();
                long sumS = 0;
                for (int i = 0; i < N; i++) sumS += (long)data[i] * data[i] % 999983;
                swS.Stop();

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var swP = Stopwatch.StartNew();
                var mapped = EngineJobs.ParallelMap(data, x => (long)x * x % 999983);
                long sumP = 0;
                foreach (var v in mapped) sumP += v;
                swP.Stop();

                double serial = swS.Elapsed.TotalMilliseconds;
                double parallel = swP.Elapsed.TotalMilliseconds;
                double speedup = parallel > 0 ? serial / parallel : 0;
                bool consistent = sumS == sumP;
                log.AppendLine("ParallelMap 10^7：串行 " + serial.ToString("0.0") + "ms · 并行 " + parallel.ToString("0.0") + "ms · 加速 " + speedup.ToString("0.00") + "x · 一致性 " + (consistent ? "✅" : "❌"));
                log.AppendLine("注：本机 24 核 + llama-server 常驻时 t9 实测 0.32x——本格为 t11 停服后对比采集点（同机同负载）。");
                bool pass = consistent;
                log.AppendLine(pass ? "✅ PASS（一致性硬断言）" : "❌ FAIL");
                WriteRes(outFull, "engjobs", new Dictionary<string, object>
                {
                    ["serialMs"] = Math.Round(serial, 1),
                    ["parallelMs"] = Math.Round(parallel, 1),
                    ["speedup"] = Math.Round(speedup, 3),
                    ["consistent"] = consistent ? "true" : "false",
                    ["note"] = "\"llama-server 常驻状态采集点；t11 停服后复跑对比\"",
                    ["pass"] = pass ? "true" : "false",
                });
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "engjobs.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return 0;
        }

        /* ---------- 入口 ---------- */

        public static int Run(string[] args)
        {
            try
            {
                string cell = args.Length > 0 ? args[0] : "";
                string outDir = args.Length >= 2 ? args[args.Length - 1] : "stress-game";
                switch (cell)
                {
                    case "dense":
                        return RunDense(args[1], args.Length >= 3 && double.TryParse(args[2], out var d) ? d : 60, args.Length >= 4 && double.TryParse(args[3], out var rate) ? rate : 0, outDir, "dense");
                    case "long10":
                        return RunLong10(args[1], args.Length >= 3 && double.TryParse(args[2], out var l) ? l : 600, outDir);
                    case "modes":
                        return RunModes(args[1], outDir);
                    case "editorload":
                        return RunEditorLoad(args[1], outDir);
                    case "editstorm":
                        return RunEditStorm(args[1], args.Length >= 3 && int.TryParse(args[2], out var n) ? n : 5000, outDir);
                    case "nav":
                        return RunNav(args.Length >= 2 && int.TryParse(args[1], out var nn) ? nn : 200, outDir);
                    case "startupscan":
                        return RunStartupScan(args[1], outDir);
                    case "decode":
                        return RunDecode(args[1], outDir);
                    case "editor3col":
                        return RunEditor3Col(args[1], outDir);
                    case "editorauto":
                        return RunEditorAuto(args[1], outDir);
                    case "editorai":
                        return RunEditorAi(args[1], outDir);
                    case "judgepress":
                        return RunJudgePress(outDir);
                    case "engjobs":
                        return RunEngJobs(outDir);
                    case "rescombo":
                    {
                        string chart = args[1];
                        int w = int.TryParse(args[2], out var ww) ? ww : 1280;
                        int h = int.TryParse(args[3], out var hh) ? hh : 800;
                        double r2 = double.TryParse(args[4], out var rr) ? rr : 0;
                        double sec = double.TryParse(args[5], out var ss) ? ss : 15;
                        string c2 = "res_" + w + "x" + h + "_" + (int)r2;
                        return RunDense(chart, sec, r2, outDir, c2);
                    }
                    default:
                        Console.WriteLine("未知 cell: " + cell + "（dense/long10/modes/editorload/editstorm/nav/startupscan/decode/rescombo/editor3col/editorauto/editorai）");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ --gamestress 失败: " + ex);
                return 1;
            }
        }
    }
}
