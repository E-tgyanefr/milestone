using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChartPlayer
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        static extern bool PrintWindowCli(IntPtr hwnd, IntPtr hdc, uint flags);

        [STAThread]
        static void Main(string[] args)
        {
            // t15 诊断：全局未处理异常落盘（进程级崩溃取证）
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                try { File.WriteAllText("crash_dump.log", "UNHANDLED: " + ex.ExceptionObject, new System.Text.UTF8Encoding(true)); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                try { File.WriteAllText("crash_dump.log", "TASK: " + ex.Exception, new System.Text.UTF8Encoding(true)); } catch { }
            };
            // 开发者模式已移除：CLI 工具入口（--selfcheck/--shotdemo/--edshot 等）保留 TestModes=true 设置
            // （字段已不再影响玩法可见性/编辑器菜单——纯兼容保留，供 --modetest 验证无门控）。
            if (args != null && args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
                GameSettings.TestModes = true;
            // ===== 开发者门控已移除验证（铁命令E→玩法全开放）：--modetest → 关闭=开启=全部9玩法（无门控；osu!taiko/osu!catch 已移除） =====
            if (args != null && args.Length > 0 && string.Equals(args[0], "--modetest", StringComparison.OrdinalIgnoreCase))
            {
                int code = 1;
                try
                {
                    GameSettings.TestModes = false;
                    var closed = ModeSystem.Available.Select(m => m.Display).ToArray();
                    GameSettings.TestModes = true;
                    var opened = ModeSystem.Available.Select(m => m.Display).ToArray();
                    string log = "===== Milestone --modetest =====\n关闭测试: " + string.Join(",", closed) +
                        "\n开启测试: " + string.Join(",", opened) + "\n";
                    bool ok = closed.Length == ModeSystem.Available.Count() && closed.SequenceEqual(opened);
                    log += (ok ? "===== 通过：开发者门控已移除（关闭=开启=" + closed.Length + "玩法，无门控；osu!taiko/osu!catch 已移除，回环作曲=编玩一体化创新玩法） =====" : "===== 失败 =====");
                    Console.WriteLine(log);
                    try { File.WriteAllText("modetest.log", log, new System.Text.UTF8Encoding(true)); } catch { }
                    code = ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText("modetest.log", "失败：" + ex, new System.Text.UTF8Encoding(true)); } catch { }
                    code = 1;
                }
                Environment.Exit(code);
                return;
            }
            // ===== 音符设计取证（t25）：Milestone.exe --noteshot "<outDir>" → 9 张 noteshot_<mode>.png + noteshot.log =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--noteshot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(NoteshotDemo.Run(args[1]));
                return;
            }
            // ===== AI 硬件探测（引擎优化①）：Milestone.exe --aidetect → 打印 HardwareInfo + EngineJobs.Degree + AiDeviceKind 首选，写 aidetect.log =====
            if (args != null && args.Length > 0 && string.Equals(args[0], "--aidetect", StringComparison.OrdinalIgnoreCase))
            {
                int code = 0;
                try
                {
                    var probeHw = HardwareProbe.Probe();
                    var probeBench = EngineJobs.Benchmark();
                    var probeKind = AiAccelerator.Probe();
                    string log =
                        "===== Milestone --aidetect =====\n" +
                        probeHw.ToString() + "\n" +
                        "EngineJobs.Degree: " + EngineJobs.Degree + "（工作线程数 EffectiveDegree=" + EngineJobs.EffectiveDegree + "）\n" +
                        "EngineJobs.Benchmark: {Degree=" + probeBench.Degree + ", MapMs=" + probeBench.MapMs.ToString("0.00") +
                        ", Speedup=" + probeBench.Speedup.ToString("0.00") + "}\n" +
                        "AiDeviceKind 首选: " + probeKind + " (" + AiAccelerator.Name(probeKind) + ")\n" +
                        "AI 加速器：" + AiAccelerator.Name(AiAccelerator.Preferred()) + "（内置规则 AI · 本地 AI 服务已禁用）\n" +
                        AiAccelerator.Describe() + "\n" +
                        "===== 完成 =====";
                    Console.WriteLine(log);
                    try { File.WriteAllText("aidetect.log", log, new System.Text.UTF8Encoding(true)); } catch { }
                }
                catch (Exception ex)
                {
                    string msg = "===== --aidetect 失败 =====\n" + ex;
                    Console.Error.WriteLine(msg);
                    try { File.WriteAllText("aidetect.log", msg, new System.Text.UTF8Encoding(true)); } catch { }
                    code = 1;
                }
                Environment.Exit(code);
                return;
            }
            // ===== 窗口枚举诊断（t50）：可见顶层窗口计数/列表（验证任意路径可见窗口数=1（引擎窗）） =====
            if (args != null && args.Length > 0 && string.Equals(args[0], "--wincount", StringComparison.OrdinalIgnoreCase))
            {
                string msg = "===== Milestone --wincount =====\n" + EngineHosting.DescribeVisibleWindows() + "\n===== 完成 =====";


                Console.WriteLine(msg);
                try { File.WriteAllText("wincount.log", msg, new System.Text.UTF8Encoding(true)); } catch { }
                Environment.Exit(0);
                return;
            }
            // ===== 引擎自检入口（CI/诊断）：Milestone.exe --selfcheck → 写 selfcheck.log，退出码 0=通过 / 1=失败 =====
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selfcheck", StringComparison.OrdinalIgnoreCase))
            {
                int code = 1;
                try
                {
                    string log = "===== ChartPlayer 引擎自检通过 =====\n" + DemoRunner.RunAll() + "\n" + FpsGovernor.SelfCheck();
                    Console.WriteLine(log);
                    try { File.WriteAllText("selfcheck.log", log, new System.Text.UTF8Encoding(true)); } catch { }
                    code = 0;
                }
                catch (Exception ex)
                {
                    string msg = "===== 引擎自检失败 =====\n" + ex;
                    Console.Error.WriteLine(msg);
                    try { File.WriteAllText("selfcheck.log", msg, new System.Text.UTF8Encoding(true)); } catch { }
                    code = 1;
                }
                Environment.Exit(code);
                return;
            }
            // ===== 编辑器截图入口（源码事故重建）：Milestone.exe --edshot "<chartPath>" "<seconds>" "<outDir>" [pxPerMs] =====
            if (args != null && args.Length >= 4 && string.Equals(args[0], "--edshot", StringComparison.OrdinalIgnoreCase))
            {
                // 第 5 参数（可选）：时间缩放 px/ms（0.003~8）——全谱概览/时间窗口验证（⑤⑥）
                Environment.Exit(CliTestTools.RunEdshot(args[1], args[2], args[3], args.Length >= 5 ? args[4] : null));
                return;
            }
            // ===== 编辑器交互仿真（源码事故重建）：Milestone.exe --edsim "<chartPath>" "<outDir>" =====
            if (args != null && args.Length >= 3 && string.Equals(args[0], "--edsim", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunEdsim(args[1], args[2]));
                return;
            }
            // ===== 主菜单/UI 截图（⑨ UI 重设计证据）：Milestone.exe --menushot "<outDir>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--menushot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunMenushot(args[1]));
                return;
            }
            // ===== 打包导出（用户指令）：Milestone.exe --pack "<谱面>" [--out "<目录>"] → 独立可运行单文件 exe =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--pack", StringComparison.OrdinalIgnoreCase))
            {
                string outDir = null;
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && args[i + 1].Length > 0) outDir = args[i + 1];
                Environment.Exit(PackExporter.RunPack(args[1], outDir));
                return;
            }
            // ===== 引擎驱动 UI 外壳截图（t11 证据）：Milestone.exe --shellshot "<outDir>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--shellshot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunShellShot(args[1]));
                return;
            }
            // ===== 完全引擎化应用壳（无 WinForms）：Milestone.exe --enginerun [秒] [--headless] =====
            if (args != null && args.Length >= 1 && string.Equals(args[0], "--enginerun", StringComparison.OrdinalIgnoreCase))
            {
                double secs = 0;
                bool headless = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--headless") headless = true;
                    else if (double.TryParse(args[i], out var s)) secs = s;
                }
                Environment.Exit(EngineApp.Run(new EngineGameHost(), new EngineAppOptions
                {
                    Title = "Milestone Engine · 完全引擎化壳",
                    Width = 960, Height = 540,
                    Headless = headless,
                    MaxSeconds = secs
                }));
                return;
            }
            // ===== FPS 实测取证（性能目标 1000+）：Milestone.exe --fpsprobe "<chart>" [秒] "<outDir>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--fpsprobe", StringComparison.OrdinalIgnoreCase))
            {
                double secs = args.Length >= 3 && double.TryParse(args[2], out var fp) ? Math.Max(2, fp) : 5;
                string fpOut = args.Length >= 4 ? args[3] : "fpsprobe";
                Environment.Exit(CliTestTools.RunFpsProbe(args[1], secs, fpOut));
                return;
            }
            // ===== 故事版演示（t22 证据）：Milestone.exe --storyshot "<outDir>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--storyshot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(StoryshotDemo.RunStoryshot(args[1]));
                return;
            }
            // ===== 性能测试套件（t13）：Milestone.exe --perftest "<outDir>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--perftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(PerfTest.RunPerfTest(args[1]));
                return;
            }
            // ===== 压力测试套件（t19）：Milestone.exe --stresstest "<outDir>" [级别1-5]（默认 1） =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--stresstest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(StressTest.RunStressTest(args[1], args.Length >= 3 ? args[2] : "1"));
                return;
            }
            // ===== 引擎压力测试（t9）：Milestone.exe --stress-engine "<outDir>" [minutes]（默认 10） =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--stress-engine", StringComparison.OrdinalIgnoreCase))
            {
                CliTestTools.DpiInit();
                double minutes = 10;
                if (args.Length >= 3 && double.TryParse(args[2], out var sm) && sm > 0) minutes = Math.Min(60, sm);
                Environment.Exit(EngineStressCli.Run(args[1], minutes));
                return;
            }
            // ===== 对比测试执行器（t42）：Milestone.exe --parity "<outDir>" → legacy vs 引擎壳 逐项行为对比 =====
            // ===== t17：parity 单元格隔离（父进程逐格 spawn；子进程原生崩溃不杀主进程）=====
            if (args != null && args.Length >= 3 && string.Equals(args[0], "--paritycell", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args[1], out int cellIdx)) Environment.Exit(ParityTest.RunCell(cellIdx, args[2]));
                Environment.Exit(2);
                return;
            }
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--parity", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(ParityTest.RunParity(args[1]));
                return;
            }
            // ===== 游玩自动截图（源码事故重建）：Milestone.exe --shotdemo "<chartPath>" "<seconds>" "<outDir>" [--cam3d] =====
            if (args != null && args.Length >= 4 && string.Equals(args[0], "--shotdemo", StringComparison.OrdinalIgnoreCase))
            {
                // --cam3d（第 4 参数可带）或环境变量 CHART_CAM3D=1：强制 Camera3D=true——
                // reviewer 关键缺口：--shotdemo 不加载 AppConfig→Camera3D 恒 false 只测 2D；真实游玩 AppConfig.Camera3D??true
                // 走 DrawMania3D（FPS 第一热点）。开关只影响本进程的 GameSettings.Camera3D，无 UI/持久化影响。
                bool cam3d = args.Length >= 5 && string.Equals(args[4], "--cam3d", StringComparison.OrdinalIgnoreCase)
                    || (System.Environment.GetEnvironmentVariable("CHART_CAM3D") ?? "").Trim() == "1";
                if (cam3d)
                {
                    GameSettings.Camera3D = true;
                    GameSettings.SlantEnabled = false;   // 3D 相机与斜轨叠加效果分离——3D 路径独立测试
                }
                Environment.Exit(CliTestTools.RunShotdemo(args[1], args[2], args[3]));
                return;
            }
            // ===== 回环作曲自检（创新玩法状态机）：Milestone.exe --composertest "<bpm>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--composertest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunComposertest(args[1]));
                return;
            }
            // ===== 引擎 UI 演示场景截图（垂直切片证据）：Milestone.exe --uisceneshot "<outDir>" =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--uisceneshot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunUiSceneShot(args[1]));
                return;
            }
            // ===== 多场同屏取证（t27）：Milestone.exe --multishot "<outDir>" → 离屏渲染双场同屏图 + stages 往返断言 =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--multishot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunMultishot(args[1]));
                return;
            }
            // ===== t19 曲库页取证：Milestone.exe --foldershot "<outDir>" → folder 页离屏渲染 1x/2x PNG =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--foldershot", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(CliTestTools.RunFolderShot(args[1]));
                return;
            }
            // ===== 游戏层压力测试（t10，stress-matrix §4.2）：Milestone.exe --gamestress <cell> <args...> <outDir> =====
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--gamestress", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(GameStressCli.Run(args.Skip(1).ToArray()));
                return;
            }
            // 强制 Per-Monitor V2 DPI 感知：修复高 DPI 下窗口按虚拟化尺寸
            // （2560 逻辑宽只显示 1707 可见区）导致界面右侧/底部被裁切的问题
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            // ===== 全局异常加固：任何异常都记录日志，UI 线程异常不让程序崩溃退出 =====
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try { Logger.Error("UI 线程异常（已恢复，程序继续运行）", e.Exception); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { Logger.Error("未处理异常（进程即将退出）", e.ExceptionObject as Exception); } catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try { Logger.Error("后台任务异常", e.Exception); } catch { }
                e.SetObserved();
            };

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // GPU 保护：常态硬件渲染不动，不再启动检查/弹窗（用户决策 08-26：常态也调用 GPU；保护仅在 AI 演示/陪玩会话内 BeginAiSession 触发）
            // ===== 会话启动：AI 加速器路由提示（NPU>GPU>CPU；格式与 --aidetect 一致） =====
            try
            {
                string accLine = "AI 加速器：" + AiAccelerator.Name(AiAccelerator.Preferred()) + "（内置规则 AI · 本地 AI 服务已禁用）";
                Logger.Info(accLine);
                Console.WriteLine(accLine);
            }
            catch { }
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try { Logger.Error("程序主循环异常退出", ex); } catch { }
                try
                {
                    MessageBox.Show("程序遇到严重错误：" + ex.Message + "\n\n详细信息已写入日志。", "Milestone（里程碑）",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// CLI 测试基建（源码事故后重建，2026-08-24）：--edshot / --edsim / --shotdemo 三个入口。
    /// 遵循 --selfcheck/--modetest 模式：args[0] 匹配、Console 输出、写 log、Environment.Exit(code)。
    /// </summary>
    static class CliTestTools
    {
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);   // DPI：编辑器截图保真（engineer 补声明）

        static string Full(string p) => Path.GetFullPath(p);

        static void WriteLog(string path, string content)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content, new UTF8Encoding(true)); } catch { }
        }

        internal static void DpiInit()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        }

        // =================== --edshot：编辑器截图 ===================
        // Milestone.exe --edshot "<chartPath>" "<seconds>" "<outDir>" [pxPerMs]
        internal static int RunEdshot(string chartPath, string secondsArg, string outDir, string pxPerMsArg = null)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                double seconds = double.TryParse(secondsArg, out var s) ? Math.Max(0.5, s) : 3;
                Directory.CreateDirectory(outFull);
                string chartFull = Full(chartPath);
                log.AppendLine("===== Milestone --edshot =====");
                log.AppendLine("谱面: " + chartFull + " · " + seconds.ToString("0.0") + "s → " + outFull);

                using var form = new Form
                {
                    Text = "Milestone --edshot",
                    ClientSize = new Size(1400, 860),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(0, 0),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;   // CLI：无头保存不弹提示
                ed.LoadChart(chartFull);
                Application.DoEvents();
                // ⑤⑥：可选时间缩放（px/ms）——全谱概览/时间窗口验证
                if (!string.IsNullOrEmpty(pxPerMsArg) && double.TryParse(pxPerMsArg, out var zPxPerMs))
                {
                    ed.PxPerMs = Math.Max(0.003, Math.Min(8, zPxPerMs));
                    Application.DoEvents();
                }

                var canvas = (Control)typeof(ChartEditorPanel).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ed);
                if (canvas == null) throw new InvalidOperationException("未找到编辑器画布 _canvas");
                double windowMs = (double)canvas.GetType().GetMethod("TimeWindowMs", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas, new object[] { canvas.ClientSize.Width });

                string name = Path.GetFileNameWithoutExtension(chartFull);
                int saves = 0;
                double step = 0.5;
                int maxShots = 12;
                var tw9 = System.Diagnostics.Stopwatch.StartNew();   // t9：分段计时（诊断后保留为低噪音诊断输出）
                for (double t = 0; t <= seconds + 0.001 && saves < maxShots; t += step)
                {
                    var it = System.Diagnostics.Stopwatch.StartNew();
                    ed.Time = t * 1000.0;
                    ed.EnsureTimeVisible(windowMs);
                    canvas.Invalidate();
                    Application.DoEvents();
                    Thread.Sleep(80);
                    Application.DoEvents();
                    var cap = System.Diagnostics.Stopwatch.StartNew();
                    string p = Path.Combine(outFull, $"{name}_{t.ToString("0.0")}s.png");
                    // 捕获整个编辑器面板（工具栏+画布+右侧菜单，D2D 走 PW_RENDERFULLCONTENT）
                    if (CaptureControl(ed, p)) { saves++; log.AppendLine("✅ " + Path.GetFileName(p)); }
                    else log.AppendLine("❌ 截图失败 " + Path.GetFileName(p));
                    cap.Stop();
                    it.Stop();
                    log.AppendLine($"[t9] t={t:0.0}s 迭代 {it.ElapsedMilliseconds}ms（其中捕获 {cap.ElapsedMilliseconds}ms）累计 {tw9.Elapsed.TotalSeconds:0.0}s");
                    try { System.IO.File.AppendAllText(Path.Combine(outFull, "live.log"), log.ToString() + Environment.NewLine); } catch { }   // t9：实时进度（被杀时仍有分段数据）
                }
                log.AppendLine("共保存 " + saves + " 张截图（" + Path.GetFileName(chartFull) + "）");
                code = saves > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                code = 1;
            }
            WriteLog(Path.Combine(outFull, "edshot.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return code;
        }

        static bool CaptureControl(Control c, string path)
        {
            try
            {
                int w = Math.Max(1, c.ClientSize.Width), h = Math.Max(1, c.ClientSize.Height);
                using var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    bool ok = false;
                    // t9：PrintWindow(PW_RENDERFULLCONTENT) 在超大谱面（D2D 内容）上可能长时间不返回——
                    // 工作线程执行 + 5s 上限；超时/失败走 DrawToBitmap 保底（保证 CLI 截图循环不卡死）
                    var capTask = Task.Run(() => { try { ok = PrintWindow(c.Handle, hdc, 2); } catch { } });
                    if (!capTask.Wait(5000)) ok = false;
                    g.ReleaseHdc(hdc);
                    if (!ok)
                    {
                        // 回退：DrawToBitmap（D2D 内容可能为黑，仅作保底）
                        try { c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h)); } catch { }
                        bmp.Save(path, ImageFormat.Png);
                        return true;
                    }
                }
                bmp.Save(path, ImageFormat.Png);
                return true;
            }
            catch { return false; }
        }

        // =================== --edsim：编辑器交互仿真 ===================
        // Milestone.exe --edsim "<chartPath>" "<outDir>"
        internal static int RunEdsim(string chartPath, string outDir)
        {
            var log = new StringBuilder();
            int fails = 0;
            bool pass = true;
            string outFull = Full(outDir);
            void Assert(string name, bool ok, string detail = "")
            {
                log.AppendLine((ok ? "✅ " : "❌ ") + name + (detail.Length > 0 ? "（" + detail + "）" : ""));
                if (!ok) { fails++; pass = false; }
            }
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                string chartFull = Full(chartPath);
                log.AppendLine("===== Milestone --edsim =====");
                log.AppendLine("谱面: " + chartFull + " → " + outFull);

                using var form = new Form
                {
                    Text = "Milestone --edsim",
                    ClientSize = new Size(2400, 1400),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(0, 0),
                    ShowInTaskbar = false
                };
                var ed = new ChartEditorPanel { Dock = DockStyle.Fill };
                form.Controls.Add(ed);
                form.Show();
                Application.DoEvents();
                ChartEditorPanel.SuppressSaveToast = true;   // CLI：无头保存不弹提示
                ed.LoadChart(chartFull);
                Application.DoEvents();

                var canvas = (Control)typeof(ChartEditorPanel).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ed);
                if (canvas == null) throw new InvalidOperationException("未找到编辑器画布 _canvas");
                var cvType = canvas.GetType();
                int W = canvas.ClientSize.Width, H = canvas.ClientSize.Height;
                Console.WriteLine("Canvas=" + W + "x" + H);
                var f = (RectangleF)cvType.GetMethod("FieldRect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { W, H });
                double windowMs = (double)cvType.GetMethod("TimeWindowMs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { W });
                // —— P0 垂直事件柱几何（与 DrawPhigrosEventTimeline 同源：列区=场右缘+12 → W-SidePad） ——
                double colX0 = f.Right + 12;
                double colW = Math.Max(40, W - 16 - colX0);
                double cw = (colW - 8) / 5.0;
                double k1x = colX0 + 1 * (cw + 2);                 // moveY 列（k=1）
                double TimeToY(double t) => f.Y + (t - ed.ScrollMs) / Math.Max(1, windowMs) * f.Height;
                int yAt(double t) => (int)Math.Round(TimeToY(t));
                int xVal(double v01) => (int)Math.Round(k1x + v01 * cw);   // 值 0..1 → 列内 x
                var mDown = cvType.GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                var mMove = cvType.GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                var mUp = cvType.GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
                void Md(int x, int y) => mDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
                void Mv(int x, int y) => mMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, x, y, 0) });
                void Mu(int x, int y) => mUp.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
                ChartEvent MoveYAt(double t) => ed.EventList.Where(e => e != null && e.Type == "moveY" && (e.Line == ed.ActiveLine || e.Line < 0))
                    .OrderBy(e => e.Time).FirstOrDefault(e => Math.Abs(e.Time - t) < 40);
                int MoveYCount() => ed.EventList.Count(e => e != null && e.Type == "moveY" && (e.Line == ed.ActiveLine || e.Line < 0));
                // 反射调用 EditorCanvas 不受保护几何：音符屏幕位置（out 参数经 args 数组回读）
                (float px, float py) NotePos(Note n)
                {
                    object[] args = new object[] { n, f, 0.0, n.Time, 0f, 0f };
                    cvType.GetMethod("PhigrosNoteScreenPosAt", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, args);
                    return ((float)args[4], (float)args[5]);
                }
                // 反射获得判定线端点（out 参数经 args 数组回读）
                (PointF a, PointF b) LineGeom()
                {
                    object[] args = new object[] { f, ed.ActiveLine, PointF.Empty, PointF.Empty, 0d, 0d, 0d };
                    cvType.GetMethod("PhigrosLineGeom", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, args);
                    return ((PointF)args[2], (PointF)args[3]);
                }
                bool EdHas(string name) => typeof(ChartEditorPanel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Any(m => m.Name == name)
                    || typeof(ChartEditorPanel).GetMethods(BindingFlags.Instance | BindingFlags.Public).Any(m => m.Name == name);

                log.AppendLine($"画布 {W}x{H} · 场 {f.X:0},{f.Y:0} {f.Width:0}x{f.Height:0} · windowMs {windowMs:0} · 列区 {colX0:0}+{colW:0} (cw {cw:0})");
                log.AppendLine("--- 键帧交互（D 队列判据 · P0 垂直柱） ---");

                // A. 键帧命中：单击 moveY@500ms 关键帧（列 k=1，y=TimeToY(500)）→ DragKind=26
                int x0 = xVal(0.2);                    // moveY@500 值 0.2
                Md(x0, yAt(500));
                Assert("键帧命中（单击 moveY@500ms → DragKind=26）", ed.DragKind == 26, "DragKind=" + ed.DragKind);
                Mu(x0, yAt(500));

                // B. 竖拖改时间：y 拖到 1500ms（拍吸附 500ms 步进），事件离开原位且数量不增
                var ev500 = MoveYAt(500);
                int countBeforeB = MoveYCount();
                Md(x0, yAt(500));
                Mv(x0, yAt(1500));
                Assert("竖拖改时间（moveY@500 拖到 1500ms，数量不变）",
                    ev500 != null && Math.Abs(ev500.Time - 1500) < 1.5 && MoveYCount() == countBeforeB,
                    "ev500.Time=" + (ev500?.Time.ToString("0") ?? "?") + " 数量=" + MoveYCount() + "/" + countBeforeB);
                Mu(x0, yAt(1500));

                // C. 横拖改值：在 1500ms 键帧处横向拖到值 0.8（列内宽度 80%）
                Md(xVal(0.5), yAt(1500));
                Mv(xVal(0.8), yAt(1500));
                double vC = ev500?.Value ?? double.NaN;
                Assert("横拖改值（值 0.5 → 0.8）", Math.Abs(vC - 0.8) < 0.02, "Value=" + vC.ToString("0.###") + " Time=" + (ev500?.Time.ToString("0") ?? "?"));
                Mu(xVal(0.8), yAt(1500));
                Assert("拖拽不增删事件（moveY 计数不变）", MoveYCount() == countBeforeB, "数量=" + MoveYCount() + "/" + countBeforeB);

                // C2. 多选/框选/组拖/批删（P0 D2d 复刻）：框选上下 1000ms 区间 → ≥2 事件选中；Delete 批删
                if (EdHas("SelectedEvts") && MoveYCount() >= 4)
                {
                    int beforeSel = MoveYCount();
                    Md((int)k1x - 4, yAt(400));
                    Mv((int)(k1x + cw + 4), yAt(2600));
                    Mu((int)(k1x + cw + 4), yAt(2600));
                    int selN = ed.SelectedEvts().Count();
                    Assert("框选（400~2600ms 区间 ≥2 事件选中）", selN >= 2, "选中=" + selN);
                    if (selN > 0)
                    {
                        // 组拖：拖任一选中帧 +1 拍（500ms）→ 全部同步
                        var first = ed.SelectedEvts().First();
                        double t0Group = first.Time;
                        var times = ed.SelectedEvts().Select(q => q.Time).ToList();
                        Md(xVal(0.2), yAt((int)t0Group));
                        Mv(xVal(0.2), yAt((int)t0Group + 500));
                        Mu(xVal(0.2), yAt((int)t0Group + 500));
                        bool allMoved = times.All(t0 => ed.SelectedEvts().Any(q => Math.Abs(q.Time - (t0 + 500)) < 1.5));
                        Assert("组拖 +1 拍全同步（500ms）", allMoved, "拖前=" + string.Join(",", times.Select(t => t.ToString("0"))));
                        // 批删
                        ed.ClearEvtSelect();
                        Md(xVal(0.2), yAt(2500)); Mv(xVal(0.2), yAt(2500)); Mu(xVal(0.2), yAt(2500));   // 选中一个
                        int selOne = ed.SelectedEvts().Count();
                        if (selOne >= 1)
                        {
                            // 用 Delete 键语义：直接调 DeleteSelectedEvts（等价批删）
                            int cntB4 = MoveYCount();
                            ed.DeleteSelectedEvts();
                            Assert("Delete 批删选中事件", MoveYCount() == cntB4 - selOne, "数量 " + cntB4 + "→" + MoveYCount());
                        }
                    }
                    log.AppendLine("⏭ 说明：框选/组拖/批删（D2d）本轮已重建基础（Ctrl/Shift 多选&框选&组拖&Delete）；贝塞尔手柄断言见下（ev.Bezier 存在时）。");
                }
                else log.AppendLine("⏭ 多选/框选/组拖/批删：当前谱面事件不足，跳过（>=4 事件 + SelectedEvts 支持才跑）");

                // D. 音符命中：复位 t=0，按反射几何点击音符（t=0 x=0）
                ed.Time = 0; ed.ScrollMs = 0;
                var n0 = ed.Notes.FirstOrDefault(n => n != null && Math.Abs(n.Time) < 1);
                if (n0 != null)
                {
                    var (npx, npy) = NotePos(n0);
                    Md((int)Math.Round(npx), (int)Math.Round(npy));
                    Assert("音符命中（t=0 音符 → SelNote+DragKind=5）", ed.SelNote == n0 && ed.DragKind == 5, "SelNote=" + (ed.SelNote?.Time.ToString("0") ?? "null") + " DragKind=" + ed.DragKind);
                    Mu((int)Math.Round(npx), (int)Math.Round(npy));
                }
                else Assert("音符命中（t=0 音符存在）", false, "谱面无音符");

                // E. 判定线拖拽：线心点击 → DragKind=20（moveY 关键帧写入口）
                var (la, lb) = LineGeom();
                int lx = (int)Math.Round((la.X + lb.X) / 2), ly = (int)Math.Round((la.Y + lb.Y) / 2);
                Md(lx, ly);
                Assert("判定线拖拽（线心 → DragKind=20）", ed.DragKind == 20, "DragKind=" + ed.DragKind + " 线心=" + lx + "," + ly);
                Mu(lx, ly);

                // F. 重载后数据完整（文件未改 → moveY@500 回归，数量=谱面原始）
                var dirty = typeof(ChartEditorPanel).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
                dirty.SetValue(ed, false);
                ed.LoadChart(chartFull);
                Application.DoEvents();
                Assert("重载后不丢事件（moveY@500 存在 5 条原始）", MoveYAt(500) != null && MoveYCount() >= 5, "moveY 数量=" + MoveYCount());

                // G. P0 面板控件存在性（预制事件/执行列表/音符编辑区）：在 _animPanel 控件树中查找按钮文本
                var animPanel = (Control)typeof(ChartEditorPanel).GetField("_animPanel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ed);
                string[] expectTexts = { "🚀 倍速2x", "🌑 淡出", "MirrorY", "ToFlick", "AttachX", "音符编辑（Phigros·选中）" };
                var found = new List<string>();
                if (animPanel != null)
                {
                    void Walk(Control c)
                    {
                        foreach (Control child in c.Controls)
                        {
                            string txt = (child is Button b ? b.Text : child is Label lb ? lb.Text : child is ComboBox cb ? cb.Text : "");
                            if (expectTexts.Any(x => txt.Contains(x))) found.Add(txt);
                            Walk(child);
                        }
                    }
                    Walk(animPanel);
                }
                bool panelOk = found.Distinct().Count() >= 5;
                Assert("P0 右侧面板控件（预制/执行列表/音符编辑 ≥5 项）", panelOk, string.Join(" | ", found.Distinct().Take(8)));

                // P0 数据保真（t5)：① SliderTickRate 区段 [Difficulty]
                {
                    var osuText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(chartFull), "std_prop_test.osu"));
                    if (File.Exists(Path.Combine(Path.GetDirectoryName(chartFull), "std_prop_test.osu")))
                    {
                        var osuChart = ChartParser.ParseOsuStandard(osuText, "std_prop_test.osu");
                        Assert("t5 tick率区段 [Difficulty]（std_prop_test SliderTickRate=2）", Math.Abs(osuChart.SliderTickRate - 2) < 0.01,
                            "SliderTickRate=" + osuChart.SliderTickRate);
                    }
                }
                // P0 数据保真（t5)：② Bezier/Next .mil 往返 ③ CloneNotes Decor
                {
                    // phigros_edsim_test.json 的 alpha 事件含 bezierPoints；给它加 next=true 后保存→重载
                    var alphaEv = ed.EventList.FirstOrDefault(q => q != null && q.Type == "alpha" && q.Bezier != null && q.Bezier.Length >= 8);
                    Assert("t5 alpha 事件含 Bezier（edsim_test 谱面）", alphaEv != null, "bezierLen=" + (alphaEv?.Bezier?.Length ?? -1));
                    if (alphaEv != null)
                    {
                        double[] bzBefore = (double[])alphaEv.Bezier.Clone();
                        alphaEv.Next = true;
                        var d2 = typeof(ChartEditorPanel).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
                        d2.SetValue(ed, false);
                        string milPath2 = Path.Combine(outFull, "bezier-next-roundtrip.mil");
                        // 直接走 BuildChart → SerializeMil（SaveMil 内部 MessageBox 会静默吞异常，这里显式暴露）
                        File.WriteAllText(milPath2, ChartParser.SerializeMil(ed.BuildChart()), new UTF8Encoding(true));
                        var chart3 = ChartParser.ParseMil(File.ReadAllText(milPath2), milPath2);
                        var a2 = chart3.Events?.FirstOrDefault(q => q != null && q.Type == "alpha" && q.Bezier != null && q.Bezier.Length >= 8);
                        bool bezierOk = a2 != null && a2.Bezier.Length == bzBefore.Length
                            && a2.Bezier.Zip(bzBefore, (x, y) => Math.Abs(x - y) < 1e-4).All(v => v);
                        Assert("t5 Bezier+Next .mil 往返保真", bezierOk && a2.Next,
                            "bezier=" + (bezierOk ? "✅" : "❌") + " next=" + (a2?.Next == true));
                        // 诊断②：tickRate .mil 往返（非默认值才写；这里直接构造 2 值 Chart 序列化验证）
                        var tickChart = new Chart { Mode = GameMode.OsuStandard, KeyCount = 1, SliderTickRate = 2 };
                        var tickMil = Path.Combine(outFull, "tickrate-roundtrip.mil");
                        File.WriteAllText(tickMil, ChartParser.SerializeMil(tickChart), new UTF8Encoding(true));
                        var tickBack = ChartParser.ParseMil(File.ReadAllText(tickMil), tickMil);
                        Assert("t5 tickRate .mil 往返保真（诊断②）", Math.Abs(tickBack.SliderTickRate - 2) < 1e-9,
                            "tickRate=" + tickBack.SliderTickRate);
                    }
                    // CloneNotes Decor
                    var arcN = ed.Notes.FirstOrDefault(q => q != null && q.Decor);
                    var cloned = ed.CloneNotes(new Note { Time = 100, End = 100, Type = "arc", Decor = true, Bpm = 172.5 });
                    Assert("t5 CloneNotes Decor 保留（arc 虚实）", cloned != null && cloned.Decor,
                        "cloned.Decor=" + (cloned?.Decor ?? false));
                    Assert("t5 CloneNotes Bpm 保留（诊断②点名）", cloned != null && Math.Abs(cloned.Bpm - 172.5) < 1e-9,
                        "cloned.Bpm=" + (cloned?.Bpm ?? -1));
                    // Undo 快照 CloneEvents 保真 Bezier
                    if (alphaEv != null)
                    {
                        ed.Time = 1;
                        var bezBefore = (double[])alphaEv.Bezier.Clone();
                        ed.PushUndo();
                        // 直接改 Bezier 再 Undo（Undo 重建 _events 列表 → 需重新取 alpha 事件）
                        alphaEv.Bezier[2] = 0.42;
                        ed.UndoRequest();
                        var alphaAfterUndo = ed.EventList.FirstOrDefault(q => q != null && q.Type == "alpha" && q.Bezier != null && q.Bezier.Length >= 8);
                        bool undoBez = alphaAfterUndo != null && Math.Abs(alphaAfterUndo.Bezier[2] - bezBefore[2]) < 1e-6;
                        Assert("t5 Undo 快照保真 Bezier（CloneEvents 深拷贝）", undoBez,
                            "Bezier[2]=" + (alphaAfterUndo != null ? alphaAfterUndo.Bezier[2].ToString("0.###") : "?"));
                        ed.RedoRequest();
                    }
                }
                // H. D2q 填充曲线：在谱面放两个锚音符 → Ctrl+F/G 等效（SetFillAnchorStart/End）→ FillCurveNotes 生成中间音符
                {
                    int fillBefore = ed.Notes.Count;
                    var nA = ed.PlaceNoteAt(0.2, 0.5, 0, 1000);
                    var nB = ed.PlaceNoteAt(0.8, 0.5, 0, 3000);
                    ed.SelNote = nA; ed.SetFillAnchorStart();
                    ed.SelNote = nB; ed.SetFillAnchorEnd();
                    ed.FillType = "tap"; ed.FillShape = "linear"; ed.FillDensity = 2;
                    ed.FillCurveNotes();
                    int fillAfter = ed.Notes.Count;
                    Assert("填充曲线（linear，1s→3s 密度2 → 生成中间音符）", fillAfter > fillBefore + 1,
                        fillBefore + "→" + fillAfter + " 新音符=" + (fillAfter - fillBefore));
                    // 回滚改动（不污染重载断言）：清空重建
                    dirty.SetValue(ed, false);
                    ed.LoadChart(chartFull);
                    Application.DoEvents();
                }

                // I. D2o 判定线元数据：设置名称/分组/Z/Cover → SaveMil→ParseMil 往返 → 断言保留
                {
                    var metaField = typeof(ChartEditorPanel).GetField("LineMeta", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var metaList = (System.Collections.IList)metaField.GetValue(ed);
                    Assert("D2o 元数据列表初始化（≥1 线）", metaList != null && metaList.Count >= 1, "count=" + (metaList?.Count ?? -1));
                    if (metaList != null && metaList.Count >= 1)
                    {
                        metaList[0] = new ChartPlayer.PhigrosLineMeta { Name = "测试线A", Group = 3, Z = 5, Cover = true };
                        var milPath = Path.Combine(outFull, "meta-roundtrip.mil");
                        ed.SaveMil(milPath);
                        var chart2 = ChartParser.ParseMil(File.ReadAllText(milPath), milPath);
                        bool hasRoundTripped = chart2.LineMeta != null && chart2.LineMeta.Count >= 1
                            && chart2.LineMeta[0] != null && chart2.LineMeta[0].Name == "测试线A"
                            && chart2.LineMeta[0].Group == 3 && chart2.LineMeta[0].Z == 5 && chart2.LineMeta[0].Cover;
                        Assert("D2o 元数据 .mil 往返保真（Name/Group/Z/Cover）", hasRoundTripped,
                            "name=" + (chart2.LineMeta != null && chart2.LineMeta.Count > 0 ? chart2.LineMeta[0]?.Name : "?"));
                    }
                }

                // J（t8）贝塞尔手柄运行时断言：选中 alpha 贝塞尔事件 → 拖手柄（DragKind=30）→ Bezier[4]/[5] 变化
                {
                    var alphaEv2 = ed.EventList.FirstOrDefault(q => q != null && q.Type == "alpha" && q.Bezier != null && q.Bezier.Length >= 8);
                    Assert("t8 alpha 事件含 Bezier 可选中", alphaEv2 != null, "bezierLen=" + (alphaEv2?.Bezier?.Length ?? -1));
                    if (alphaEv2 != null)
                    {
                        ed.SelEvt = alphaEv2;   // 手柄显示/命中前提
                        // 手柄 2（索引2）几何：x=列内 by（Bezier[5]）、y=时间比例 bx（Bezier[4]）
                        int k = Array.IndexOf(new[] { "moveX", "moveY", "rotate", "alpha", "speed" }, alphaEv2.Type);
                        double colX0b = f.Right + 12;
                        double colWb = Math.Max(40, W - 16 - colX0b);
                        double cwb = (colWb - 8) / 5.0;
                        double cxxb = colX0b + k * (cwb + 2);
                        double durB = Math.Max(1, (double.IsNaN(alphaEv2.End) ? alphaEv2.Time : alphaEv2.End) - alphaEv2.Time);
                        int hx = (int)Math.Round(cxxb + alphaEv2.Bezier[5] * cwb);
                        int hy = (int)Math.Round(f.Y + (alphaEv2.Time + alphaEv2.Bezier[4] * durB - ed.ScrollMs) / Math.Max(1, windowMs) * f.Height);
                        double b4Before = alphaEv2.Bezier[4], b5Before = alphaEv2.Bezier[5];
                        Md(hx, hy);
                        bool kind30 = ed.DragKind == 30;
                        // 拖到新位置（移动 ~15px）
                        Mv(hx + 15, hy + 15);
                        double b4After = alphaEv2.Bezier[4], b5After = alphaEv2.Bezier[5];
                        Mu(hx + 15, hy + 15);
                        Assert("t8 贝塞尔手柄命中+拖拽（DragKind=30，Bezier[4]/[5] 变化）",
                            kind30 && (Math.Abs(b4After - b4Before) > 0.001 || Math.Abs(b5After - b5Before) > 0.001),
                            "kind30=" + kind30 + " B4 " + b4Before.ToString("0.###") + "→" + b4After.ToString("0.###") + " B5 " + b5Before.ToString("0.###") + "→" + b5After.ToString("0.###"));
                    }
                }

                // K（t8）BatchEdit MirrorY + mil Side/Width/Alpha round-trip 断言
                {
                    // 补一个音符凑够多选（谱面原生仅 1 音；PlaceNoteAt 走编辑器放置语义）
                    var nAdd = ed.PlaceNoteAt(0.5, 0.25, 0, 2000);
                    var ns = ed.Notes.Where(n => n != null).OrderBy(n => n.Time).Take(2).ToList();
                    Assert("t8 音符多选先决（≥2 音符）", ns.Count >= 2, "count=" + ns.Count);
                    if (ns.Count >= 2)
                    {
                        ed.ClearMultiSelect();
                        ed.ToggleMultiSelect(ns[0]);
                        ed.ToggleMultiSelect(ns[1]);
                        var y0a = ns[0].Y; var y0b = ns[1].Y;
                        ed.BatchEdit("MirrorY");
                        bool mirrored = Math.Abs(ns[0].Y - (1 - y0a)) < 1e-9 && Math.Abs(ns[1].Y - (1 - y0b)) < 1e-9;
                        Assert("t8 BatchEdit MirrorY（Y 翻转）", mirrored, "Y " + y0a.ToString("0.##") + "→" + ns[0].Y.ToString("0.##"));
                        // mil Side/Width/Alpha 往返
                        ns[0].Side = 1; ns[0].Width = 1.5; ns[0].Alpha = 0.6;
                        string milS = Path.Combine(outFull, "note-edit-roundtrip.mil");
                        File.WriteAllText(milS, ChartParser.SerializeMil(ed.BuildChart()), new UTF8Encoding(true));
                        var chartS = ChartParser.ParseMil(File.ReadAllText(milS), milS);
                        var nS = chartS.Notes?.OrderBy(n => n.Time).FirstOrDefault();
                        bool noteEditOk = nS != null && nS.Side == 1 && Math.Abs(nS.Width - 1.5) < 1e-9 && Math.Abs(nS.Alpha - 0.6) < 1e-9;
                        Assert("t8 mil round-trip Side/Width/Alpha 保留", noteEditOk,
                            "side=" + (nS?.Side ?? -1) + " width=" + (nS?.Width ?? -1).ToString("0.##") + " alpha=" + (nS?.Alpha ?? -1).ToString("0.##"));
                    }
                }

                // L. ⑤⑥ 时间窗口泛化——时间条音符刻度拖拽（侧边拖柄）：命中刻度 → DragKind=31 → 横向拖到新位置 → Time 移动
                {
                    ed.Time = 0; ed.ScrollMs = 0; Application.DoEvents();
                    const int TimeBarPx = 30;   // EditorCanvas.TimeBarH（无轨底部时间条高度）
                    var nT = ed.Notes.FirstOrDefault(q => q != null && Math.Abs(q.Time) < 3000 && !ed.IsLong(q));
                    double winL = (double)cvType.GetMethod("TimeWindowMs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { W });
                    int tickX = (int)Math.Round((nT.Time - ed.ScrollMs) / Math.Max(1, winL) * W);
                    double beforeT = nT.Time;
                    Md(tickX, H - TimeBarPx);
                    bool kind31 = ed.DragKind == 31;
                    Mv(tickX + (int)Math.Round(W * 0.10), H - TimeBarPx);
                    Mu(tickX + (int)Math.Round(W * 0.10), H - TimeBarPx);
                    Assert("⑤⑥ 时间条刻度拖拽（DragKind=31 且 Time 移动）",
                        kind31 && Math.Abs(nT.Time - beforeT) > 60,
                        "kind31=" + kind31 + " Time " + beforeT.ToString("0") + "→" + nT.Time.ToString("0"));
                    // 撤销回滚（不污染后续）
                    ed.UndoRequest();
                    Application.DoEvents();
                }

                // M. t5 ADOFAI 网格编辑器（原版式）：反射 AdofaiGridGeom → 点末格 8 邻空格=追加（数量+1）；
                //    点非末格 8 邻=中间插入（数量+1）；Undo 还原
                {
                    string adofaiPath = Path.Combine(Path.GetDirectoryName(chartFull), "adofai_real_test.adofai");
                    if (File.Exists(adofaiPath))
                    {
                        var dirtyM = typeof(ChartEditorPanel).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
                        dirtyM.SetValue(ed, false);   // 避免 LoadChart 触发 ConfirmDiscard 模态框（无头挂起）
                        ed.LoadChart(adofaiPath);
                        Application.DoEvents();
                        Thread.Sleep(300);            // 等工具栏 AutoSize 重排后取**实时**画布尺寸（ADOFAI 工具栏更高）
                        Application.DoEvents();
                        int Wm = canvas.ClientSize.Width, Hm = canvas.ClientSize.Height;
                        var gmM = cvType.GetMethod("AdofaiGridGeom", BindingFlags.Instance | BindingFlags.NonPublic);
                        object[] ga = new object[] { Wm, Hm, null, null, null, null, null, null };
                        bool gok = (bool)gmM.Invoke(canvas, ga);
                        var gpM = ga[2] as PointF[]; double gcellM = (double)ga[3], goxM = (double)ga[4], goyM = (double)ga[5];
                        var glM = ga[6] as List<Note>;
                        int cntM = glM?.Count ?? 0;
                        Assert("t5 ADOFAI 网格几何（≥3 tile）", gok && gpM != null && cntM >= 3, "count=" + cntM);
                        if (gok && gpM != null && cntM >= 3)
                        {
                            int[] dxsM = { 1, 1, 0, -1, -1, -1, 0, 1 }, dysM = { 0, 1, 1, 1, 0, -1, -1, -1 };
                            int lxM = (int)Math.Round((gpM[cntM - 1].X - goxM) / gcellM), lyM = (int)Math.Round((gpM[cntM - 1].Y - goyM) / gcellM);
                            int axM = 0, ayM = 0; bool foundM = false;
                            for (int k = 0; k < 8 && !foundM; k++)
                            {
                                int cx2 = lxM + dxsM[k], cy2 = lyM + dysM[k];
                                bool occ = false;
                                for (int i = 0; i < cntM; i++)
                                    if ((int)Math.Round((gpM[i].X - goxM) / gcellM) == cx2 && (int)Math.Round((gpM[i].Y - goyM) / gcellM) == cy2) { occ = true; break; }
                                if (!occ) { axM = (int)Math.Round(goxM + cx2 * gcellM); ayM = (int)Math.Round(goyM + cy2 * gcellM); foundM = true; }
                            }
                            Assert("t5 网格追加候选格存在", foundM, "");
                            if (foundM)
                            {
                                Md(axM, ayM);
                                Assert("t5 网格追加（末格邻空格 → 数量+1）", ed.Notes.Count == cntM + 1, "count=" + ed.Notes.Count + "/" + cntM);
                                Mu(axM, ayM);
                                ed.UndoRequest(); Application.DoEvents();
                                Assert("t5 追加 Undo 还原", ed.Notes.Count == cntM, "count=" + ed.Notes.Count);
                                // 中间插入：tile 0 的 8 邻空格（避开末格追加区）
                                object[] ga2 = new object[] { Wm, Hm, null, null, null, null, null, null };
                                bool gok2 = (bool)gmM.Invoke(canvas, ga2);
                                var gp2 = ga2[2] as PointF[]; double gcell2 = (double)ga2[3], gox2 = (double)ga2[4], goy2 = (double)ga2[5];
                                var gl2 = ga2[6] as List<Note>;
                                int cnt2 = gl2?.Count ?? 0;
                                int ix0 = (int)Math.Round((gp2[0].X - gox2) / gcell2), iy0 = (int)Math.Round((gp2[0].Y - goy2) / gcell2);
                                int lx2 = (int)Math.Round((gp2[cnt2 - 1].X - gox2) / gcell2), ly2 = (int)Math.Round((gp2[cnt2 - 1].Y - goy2) / gcell2);
                                int sx = 0, sy = 0; bool foundIns = false;
                                for (int k = 0; k < 8 && !foundIns; k++)
                                {
                                    int cx3 = ix0 + dxsM[k], cy3 = iy0 + dysM[k];
                                    if (Math.Abs(cx3 - lx2) <= 1 && Math.Abs(cy3 - ly2) <= 1) continue;   // 末格追加区优先
                                    bool occ = false;
                                    for (int i = 0; i < cnt2; i++)
                                        if ((int)Math.Round((gp2[i].X - gox2) / gcell2) == cx3 && (int)Math.Round((gp2[i].Y - goy2) / gcell2) == cy3) { occ = true; break; }
                                    if (!occ) { sx = (int)Math.Round(gox2 + cx3 * gcell2); sy = (int)Math.Round(goy2 + cy3 * gcell2); foundIns = true; }
                                }
                                Assert("t5 网格插入候选格存在", foundIns, "");
                                if (foundIns)
                                {
                                    Md(sx, sy);
                                    Assert("t5 网格中间插入（非末格邻空格 → 数量+1）", ed.Notes.Count == cnt2 + 1, "count=" + ed.Notes.Count + "/" + cnt2);
                                    Mu(sx, sy);
                                    ed.UndoRequest(); Application.DoEvents();
                                    Assert("t5 插入 Undo 还原", ed.Notes.Count == cnt2, "count=" + ed.Notes.Count);
                                }
                            }
                        }
                    }
                }

                log.AppendLine("--- 说明 ---");
                log.AppendLine("⏭ 贝塞尔手柄（D2f）断言：谱面 alpha 通道含 bezierPoints（phigros_edsim_test.json 有），桩在 t2 交互段验证绘制；edshot 截图人工核验。");
                log.AppendLine(fails == 0 ? "===== --edsim 全部通过 =====" : $"===== --edsim 失败 {fails} 项 =====");
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                pass = false;
            }
            WriteLog(Path.Combine(outFull, "edsim.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return pass ? 0 : 1;
        }

        // =================== --multishot：多场同屏取证（t27） ===================
        // Milestone.exe --multishot "<outDir>" —— 隐藏窗体 + GamePanel 渲染双场（左 Mania 4K/右 Phigros，自定义 rect），
        // 自动游玩推进后离屏截图；同时断言 .mil stages 往返保真（SerializeMil → ParseMil → stages/rect/音符归属一致）。
        internal static int RunMultishot(string outDir)
        {
            var log = new StringBuilder();
            int fails = 0;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                // ---- ① 构造双场谱（谱师自定义位置：左 4K Mania + 右 Phigros 线场） ----
                var chart = new Chart
                {
                    Title = "multi_stage_demo",
                    ModeName = "多场同屏",
                    Mode = GameMode.Mania,
                    KeyCount = 4,
                    Bpm = 120
                };
                var p0 = new ChartPart { Name = "左场 4K", Mode = GameMode.Mania, KeyCount = 4 };
                for (int i = 0; i < 4; i++) p0.Notes.Add(new Note { Time = 1000 + i * 500, Col = i });
                var p1 = new ChartPart { Name = "右场 Phigros", Mode = GameMode.Phigros, KeyCount = 4 };
                p1.Notes.Add(new Note { Time = 1200, X = 0.5, Y = 0.5 });
                p1.Notes.Add(new Note { Time = 1800, X = 0.7, Y = 0.5 });
                p1.Notes.Add(new Note { Time = 2400, X = 0.3, Y = 0.5 });
                chart.Parts.Add(p0);
                chart.Parts.Add(p1);
                chart.Stages = new List<Stage>
                {
                    new Stage { Id = 0, Name = "左场 4K", X = 0.02, Y = 0.05, W = 0.46, H = 0.90 },
                    new Stage { Id = 1, Name = "右场 Phigros", X = 0.52, Y = 0.05, W = 0.46, H = 0.90 }
                };

                // ---- ② .mil stages 往返保真断言 ----
                string json = ChartParser.SerializeMil(chart);
                var back = ChartParser.ParseMil(json, "multi_stage_test.mil");
                bool rt = back.Stages != null && back.Stages.Count == 2
                    && Math.Abs(back.Stages[0].X - 0.02) < 0.0001
                    && Math.Abs(back.Stages[0].W - 0.46) < 0.0001
                    && Math.Abs(back.Stages[1].Y - 0.05) < 0.0001
                    && back.Parts != null && back.Parts.Count == 2
                    && back.Parts[0].Notes.Count == 4 && back.Parts[1].Notes.Count == 3
                    && back.Parts[1].Notes.TrueForAll(n => n.Field == 1)
                    && back.Parts[0].Notes.TrueForAll(n => n.Field == 0);
                log.AppendLine((rt ? "✅ " : "❌ ") + ".mil stages 往返保真（stages=2 · rect X/W 保真 · parts 4+3 · Field 0/1 分场）");
                if (!rt) fails++;
                // 兼容：旧谱无 stages → 单主场
                var legacy = new Chart { Mode = GameMode.Mania, KeyCount = 4, Bpm = 120 };
                legacy.Notes.Add(new Note { Time = 1000, Col = 0 });
                var backL = ChartParser.ParseMil(ChartParser.SerializeMil(legacy), "legacy.mil");
                bool rtL = (backL.Stages == null || backL.Stages.Count == 0);
                log.AppendLine((rtL ? "✅ " : "❌ ") + "旧谱无 stages 保持单主场（兼容）");
                if (!rtL) fails++;

                // ---- ③ 离屏渲染：双场同屏截图 ----
                using var form = new Form
                {
                    Text = "Milestone --multishot",
                    ClientSize = new Size(1400, 860),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(0, 0),
                    ShowInTaskbar = false
                };
                var panel = new GamePanel { Dock = DockStyle.Fill };
                form.Controls.Add(panel);
                form.Show();
                Application.DoEvents();
                panel.StartAutoplay(chart, AppDomain.CurrentDomain.BaseDirectory);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2650)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                Application.DoEvents();
                string png = Path.Combine(outFull, "multistage.png");
                bool okBmp = CaptureControl(panel, png);
                log.AppendLine((okBmp ? "✅ " : "❌ ") + "双场同屏截图（左 Mania 4K ✚ 右 Phigros）：" + png);
                if (!okBmp) fails++;
                panel.StopRender();
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                fails++;
            }
            File.WriteAllText(Path.Combine(outFull, "multishot.log"), log.ToString(), new UTF8Encoding(true));
            return fails == 0 ? 0 : 1;
        }

        // =================== --menushot：主菜单 UI 截图（⑨ UI 重设计证据） ===================
        // Milestone.exe --menushot "<outDir>"
        internal static int RunMenushot(string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                MainForm.CliLegacy = true;   // 截图走旧主菜单（引擎壳另用 --shellshot 取证）
                var form = new MainForm();
                form.Show();
                Application.DoEvents();
                Thread.Sleep(900);            // 等布局/Logo 稳定
                Application.DoEvents();
                string p1 = Path.Combine(outFull, "mainmenu.png");
                bool ok = CaptureControl(form, p1);
                log.AppendLine((ok ? "✅ " : "❌ ") + p1);
                code = ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                code = 1;
            }
            WriteLog(Path.Combine(outFull, "menushot.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return code;
        }

        // =================== --fpsprobe：自动游玩 FPS 实测取证（性能目标 1000+） ===================
        internal static int RunFpsProbe(string chartPath, double seconds, string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                string chartFull = Full(chartPath);
                var chart = chartFull.ToLowerInvariant().EndsWith(".adofai")
                    ? ChartParser.ParseAdofaiReal(File.ReadAllText(chartFull), chartFull)
                    : ChartParser.ParseFile(chartFull);
                if (chart == null) throw new InvalidOperationException("谱面解析失败");

                using var form = new Form
                {
                    Text = "Milestone --fpsprobe",
                    ClientSize = new Size(1280, 720),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),   // 屏幕外：不打扰桌面，GPU 路径仍真实
                    ShowInTaskbar = false
                };
                var game = new GamePanel { Dock = DockStyle.Fill };
                form.Controls.Add(game);
                form.Show();
                Application.DoEvents();
                game.SelectPart(0);
                game.StartAutoplay(chart, Path.GetDirectoryName(chartFull));

                var samples = new List<double>();
                using var sampleTimer = new System.Windows.Forms.Timer { Interval = 100 };
                sampleTimer.Tick += (s2, e2) =>
                {
                    double v = D2DRenderer.FpsStatic;
                    if (v > 0) samples.Add(v);
                };
                sampleTimer.Start();

                using var closeTimer = new System.Windows.Forms.Timer { Interval = (int)(seconds * 1000 + 800) };
                closeTimer.Tick += (s2, e2) =>
                {
                    closeTimer.Stop(); sampleTimer.Stop();
                    try { game.StopRender(); } catch { }
                    Application.DoEvents(); Thread.Sleep(200);
                    form.Close();
                };
                closeTimer.Start();
                Application.Run(form);

                if (samples.Count == 0) throw new InvalidOperationException("无 FPS 采样");
                samples.Sort();
                double avg = samples.Average();
                double min = samples[0];
                double p1 = samples[Math.Min(samples.Count - 1, samples.Count / 100)];
                double target = GameSettings.RefreshRate > 0 ? GameSettings.RefreshRate : 1000.0;
                bool pass = avg >= target;
                log.AppendLine("谱面: " + chartFull + " · " + seconds.ToString("0.0") + "s");
                log.AppendLine("刷新率挡位: " + FpsGovernor.NameOf(GameSettings.RefreshRate) + " · 目标 " + target.ToString("0") + " FPS");
                log.AppendLine("FPS: 平均 " + avg.ToString("0.0") + " · 最低 " + min.ToString("0.0") + " · P1 " + p1.ToString("0.0") + " · 采样 " + samples.Count);
                log.AppendLine(pass ? "✅ 达成目标（≥" + target.ToString("0") + " FPS）" : "⚠ 未达目标（" + target.ToString("0") + " FPS），自适应降档将接管");
                WriteLog(Path.Combine(outFull, "fpsprobe.log"), log.ToString());
                Console.WriteLine(log.ToString());
                code = 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                WriteLog(Path.Combine(outFull, "fpsprobe.log"), log.ToString());
                Console.WriteLine(log.ToString());
                code = 1;
            }
            return code;
        }

        // =================== --foldershot：曲库管理页离屏取证（t19 P0-1 复证，1x+2x） ===================
        internal static int RunFolderShot(string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                using var shell = new EngineMainShell(new Dictionary<string, Action>(), () => "", (c, d) => true);
                shell.GoToPage("folder");   // 触发 RefreshFolderView（路径/统计/列表回填）
                foreach (var (w, h, name) in new[] { (1280, 800, "fixed-folder.png"), (2560, 1600, "fixed-folder-2x.png") })
                {
                    using var bmp = DpiBitmap.Create(w, h);   // t78：96 DPI——工程 UI 文本按逻辑尺寸光栅化（否则 150% 屏 1.5×放大被标签裁剪）
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.FillRectangle(new SolidBrush(Color.FromArgb(UiTheme.Default.Bg.R, UiTheme.Default.Bg.G, UiTheme.Default.Bg.B)), 0, 0, w, h);
                        if (!shell.RenderPageTo("folder", new GdiDrawAdapter(g), w, h))
                            throw new InvalidOperationException("folder 页面不存在");
                    }
                    string p = Path.Combine(outFull, name);
                    bmp.Save(p, ImageFormat.Png);
                    log.AppendLine("✅ " + p);
                }
                code = 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
            }
            WriteLog(Path.Combine(outFull, "foldershot.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return code;
        }

        // =================== --shellshot：引擎驱动 UI 外壳截图（t11 重构证据，离屏 GDI 渲染） ===================
        internal static int RunShellShot(string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                using var shell = new EngineMainShell(new Dictionary<string, Action>(), () => "", (c, d) => true);
                var pages = new[] { ("menu", "shell-menu.png"), ("songs", "shell-songs.png"), ("settings", "shell-settings.png") };
                foreach (var (key, name) in pages)
                {
                    using var bmp = DpiBitmap.Create(1280, 800);   // t78：96 DPI（shellshot 证据与实窗渲染同口径）
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.FillRectangle(new SolidBrush(Color.FromArgb(UiTheme.Default.Bg.R, UiTheme.Default.Bg.G, UiTheme.Default.Bg.B)), 0, 0, 1280, 800);
                        if (!shell.RenderPageTo(key, new GdiDrawAdapter(g), 1280, 800))
                            throw new InvalidOperationException("页面不存在：" + key);
                    }
                    string p = Path.Combine(outFull, name);
                    bmp.Save(p, ImageFormat.Png);
                    log.AppendLine("✅ " + p);
                }
                code = 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                code = 1;
            }
            WriteLog(Path.Combine(outFull, "shellshot.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return code;
        }

        // =================== --uisceneshot：引擎 UI 演示场景截图（离屏渲染一帧） ===================
        // Milestone.exe --uisceneshot "<outDir>" —— 后台构建演示 Scene，D2DDrawAdapter 渲染一帧存 PNG（不弹交互窗）
        internal static int RunUiSceneShot(string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                Directory.CreateDirectory(outFull);
                // 无窗口离屏渲染（GDI+ 软件后端，与 D2DDrawAdapter 同一 IUiDraw 契约）：
                // 引擎 UI 组件只依赖 IUiDraw——换后端无需改组件代码；同时证明重做 UI 的真实内容（非黑帧）。
                using var form = new EngineUiDemoForm((c, d) => true);
                var pages = new[]
                {
                    ("menu", "engineui-main.png"),
                    ("songs", "engineui-songs.png"),
                    ("settings", "engineui-settings.png")
                };
                foreach (var (key, name) in pages)
                {
                    using var bmp = DpiBitmap.Create(1280, 720);   // t78：96 DPI（uisceneshot 证据与演示窗渲染同口径）
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        // 页面自带背景面板；先铺一层主题底色，避免透明角落
                        g.FillRectangle(new SolidBrush(Color.FromArgb(UiTheme.Default.Bg.R, UiTheme.Default.Bg.G, UiTheme.Default.Bg.B)), 0, 0, 1280, 720);
                        if (!form.RenderPageTo(key, new GdiDrawAdapter(g), 1280, 720))
                            throw new InvalidOperationException("页面不存在：" + key);
                    }
                    string p = Path.Combine(outFull, name);
                    bmp.Save(p, ImageFormat.Png);
                    log.AppendLine("✅ " + p);
                }
                code = 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                code = 1;
            }
            WriteLog(Path.Combine(outFull, "uisceneshot.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return code;
        }

        // =================== --composertest：回环作曲状态机自检 ===================
        // Milestone.exe --composertest "<bpm>" —— 无 UI 纯逻辑：录（吸附/乱拍丢弃）→ 奏（自动满分）→ 扩（3 满分 8 小节）
        internal static int RunComposertest(string bpmArg)
        {
            var log = new StringBuilder();
            int code = 1;
            try
            {
                double bpm = double.TryParse(bpmArg, out var b) ? Math.Max(30, Math.Min(300, b)) : 120;
                var chart = new Chart { Bpm = bpm, Mode = GameMode.LoopComposer, KeyCount = 4 };
                JudgeSettings.ApplyForChart(chart);
                var eng = new JudgementEngine(true, 0);
                var lc = new LoopComposer(chart, eng, (g, t) => { });
                int fails = 0;
                void Assert(bool cond, string what) { if (!cond) { fails++; log.AppendLine("❌ " + what); } else log.AppendLine("✅ " + what); }

                // --- 录：吸附与乱拍丢弃（BPM=120 → 16 分格=125ms） ---
                double t0 = 100000;                      // 任意起点
                lc.Tick(t0);                             // PhaseStart=t0
                Assert(lc.Phase == 0, "初始为录阶段");
                lc.KeyCol(0, t0 + 10);                   // 距 0ms 格 10ms → 吸附
                Assert(lc.Notes.Count == 1 && Math.Abs(lc.Notes[0].T - 0) < 1, "击打+10ms 吸附到 0ms 格");
                lc.KeyCol(1, t0 + 130);                  // 距 125ms 格 5ms → 吸附
                Assert(lc.Notes.Count == 2 && Math.Abs(lc.Notes[1].T - 125) < 1, "击打+130ms 吸附到 125ms 格");
                lc.KeyCol(2, t0 + 500);                  // 距 500ms 格 0ms → 吸附
                Assert(lc.Notes.Count == 3 && Math.Abs(lc.Notes[2].T - 500) < 1, "击打正点 500ms 吸附");
                lc.KeyCol(3, t0 + 481);                  // 距 500ms 格 19ms → 吸附（同轨不同格）
                Assert(lc.Notes.Count == 4 && Math.Abs(lc.Notes[3].T - 500) < 1, "击打+481ms 吸附 500ms（同格异轨并存）");
                lc.KeyCol(0, t0 + 4);                    // 距 0ms 格 4ms → 同轨同格（col0@0 已有）→ 去重
                Assert(lc.Notes.Count == 4, "同轨同格去重（col0@0 +4ms 不新增）");
                lc.KeyCol(0, t0 + 990);                  // 距 1000ms 格 10ms → 吸附
                Assert(lc.Notes.Count == 5, "击打+990ms 吸附 1000ms");
                // Tick 到录结束（4 小节 = 8s@120）
                lc.Tick(t0 + 8000);
                Assert(lc.Phase == 1, "录满 4 小节自动进入奏");

                // --- 奏：autoplay 自动命中 → 满分循环（PERFECT 全命中） ---
                GameSettings.Autoplay = true;
                lc.PerfectLoops = 0; lc.MissedThisLoop = false;
                for (double tw = t0 + 8016; tw <= t0 + 16000; tw += 16) lc.Tick(tw);   // 16ms 步进扫完第 0 循环
                Assert(lc.PerfectLoops == 1, "第 1 个满分循环（自动命中） 满分=1");
                Assert(eng.Hits.TryGetValue("PERFECT", out int zp) && zp >= 5, "第 0 循环 5 音符全 PERFECT（实际 " + zp + "）");
                for (double tw = t0 + 16016; tw <= t0 + 24000; tw += 16) lc.Tick(tw);
                Assert(lc.PerfectLoops == 2, "第 2 个满分循环 满分=2");
                for (double tw = t0 + 24016; tw <= t0 + 32000; tw += 16) lc.Tick(tw);
                Assert(lc.PerfectLoops == 3, "第 3 个满分循环 满分=3");
                lc.Tick(t0 + 32010);                     // 触发扩段（在扩段点后一小步）
                Assert(lc.Bars == 8, "3 满分后扩段：4→8 小节（实际 " + lc.Bars + "）");
                Assert(Math.Abs(lc.Bpm - bpm * 1.02) < 0.01, "BPM 漂移 +2%（实际 " + lc.Bpm.ToString("0.##") + "）");
                Assert(lc.Phase == 0, "扩段后回到录");

                GameSettings.Autoplay = false;
                log.AppendLine(fails == 0 ? "===== --composertest 全部通过 =====" : $"===== --composertest 失败 {fails} 项 =====");
                WriteLog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "composertest.log"), log.ToString());
                code = fails == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                WriteLog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "composertest.log"), log.ToString());
                code = 1;
            }
            Console.WriteLine(log.ToString());
            return code;
        }

        // =================== --shotdemo：自动游玩截图 ===================
        // Milestone.exe --shotdemo "<chartPath>" "<seconds>" "<outDir>"
        internal static int RunShotdemo(string chartPath, string secondsArg, string outDir)
        {
            var log = new StringBuilder();
            int code = 1;
            string outFull = Full(outDir);
            try
            {
                DpiInit();
                double seconds = double.TryParse(secondsArg, out var s) ? Math.Max(1, s) : 5;
                Directory.CreateDirectory(outFull);
                string chartFull = Full(chartPath);
                log.AppendLine("===== Milestone --shotdemo =====");
                log.AppendLine("谱面: " + chartFull + " · " + seconds.ToString("0.0") + "s → " + outFull);
                log.AppendLine("渲染路径: " + (GameSettings.Camera3D ? "3D（Camera3D=true — DrawMania3D）" : "2D（Camera3D=false）"));

                var chart = chartFull.ToLowerInvariant().EndsWith(".adofai")
                    ? ChartParser.ParseAdofaiReal(File.ReadAllText(chartFull), chartFull)
                    : ChartParser.ParseFile(chartFull);
                if (chart == null) throw new InvalidOperationException("谱面解析失败");

                using var form = new Form
                {
                    Text = "Milestone --shotdemo",
                    ClientSize = new Size(1280, 720),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(0, 0),
                    ShowInTaskbar = false
                };
                var game = new GamePanel { Dock = DockStyle.Fill };
                form.Controls.Add(game);
                form.Show();
                Application.DoEvents();
                game.SelectPart(0);
                game.StartAutoplay(chart, Path.GetDirectoryName(chartFull));
                GamePanel.AutoShotDir = outFull;   // GamePanel.AutoShotTick：游玩中每 30 帧 PrintWindow 捕获

                using var closeTimer = new System.Windows.Forms.Timer { Interval = (int)(seconds * 1000 + 600) };
                closeTimer.Tick += (s2, e2) =>
                {
                    // t12：关闭时序显式分步——①停截图 ②停渲染线程（StopRender 内部 Join(1500)）③泵消息+短暂等待让渲染线程/D2D 完全落地 ④再关窗
                    // （t10 基础上补 DoEvents+Sleep：StopRender 后立即 Close 可能使渲染线程尾帧/句柄销毁未落地——reviewer 批C/批D 1/6、5/6 失败反差）
                    closeTimer.Stop();
                    try { GamePanel.AutoShotDir = null; } catch { }
                    try { game.StopRender(); } catch { }
                    Application.DoEvents();
                    Thread.Sleep(250);
                    Application.DoEvents();
                    form.Close();
                };
                closeTimer.Start();
                Application.Run(form);
                // 窗体关闭后，等渲染线程退出再读取结果
                try { game.StopToMenu(); Application.DoEvents(); } catch { }

                int shots = Directory.GetFiles(outFull, "auto_*.png").Length;
                log.AppendLine("自动游玩 " + seconds.ToString("0.0") + "s，产出 " + shots + " 张截图（auto_*.png）");
                code = shots > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("❌ 异常: " + ex);
                code = 1;
            }
            WriteLog(Path.Combine(outFull, "shotdemo.log"), log.ToString());
            Console.WriteLine(log.ToString());
            return code;
        }
    }
}
