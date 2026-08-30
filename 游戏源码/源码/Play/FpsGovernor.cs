using System;
using System.Text;

namespace ChartPlayer
{
    /// <summary>
    /// 刷新率挡位 + FPS 治理器（性能目标：任意设备任意谱面稳定 1000 FPS 以上）。
    /// t6（t3 §3.1/§3.2）：挡位表全量—— -1 跟随屏幕 / 50/60/75/90/100/120/144/165/180/200/240/300/360/480/500 / 0 无限制；
    /// 自定义 1..2000Hz 不硬编码（TargetFrameMs 最小帧时钳 0.5ms，1440Hz 等超高刷按自定义档）。
    /// 运行时自适应：实测 FPS 低于目标 80% 持续 ≥1500ms 自动降画质、高于 120% 持续 ≥4000ms 升回
    /// （墙钟时间语义，与刷新率无关；nowMs 由调用方传）。
    /// 「无限制」挡目标 1000+ FPS（帧循环不钳制，画质走自动档动态调档）。
    /// </summary>
    public static class FpsGovernor
    {
        /// <summary>可用刷新率挡位（-1 = 跟随屏幕；0 = 无限制，目标 1000+）。自定义 1..2000 经 TargetFrameMs 通用支持。</summary>
        public static readonly int[] Rates = { -1, 50, 60, 75, 90, 100, 120, 144, 165, 180, 200, 240, 300, 360, 480, 500, 0 };
        /// <summary>挡位显示名（与 Rates 一一对应）。</summary>
        public static readonly string[] RateNames =
        {
            "跟随屏幕", "50Hz", "60Hz", "75Hz", "90Hz", "100Hz", "120Hz", "144Hz", "165Hz",
            "180Hz", "200Hz", "240Hz", "300Hz", "360Hz", "480Hz", "500Hz", "无限制 (1000+)"
        };

        /// <summary>屏幕刷新探测钩子（宿主注入：如 GetDeviceCaps VREFRESH/DXGI；null=回退 60Hz）。</summary>
        public static Func<int> ScreenRateProbe;

        /// <summary>刷新率 → 目标帧时（ms；0=无限制；-1=跟随屏幕探测值；最小帧时 0.5ms 钳制——防超高刷 0/溢出）。</summary>
        public static double TargetFrameMs(int rate)
        {
            if (rate == 0) return 0;
            int hz = rate;
            if (rate < 0)
            {
                int probed = 0;
                try { probed = ScreenRateProbe?.Invoke() ?? 0; } catch { probed = 0; }
                hz = probed > 0 ? probed : 60;   // 跟随屏幕：探测失败回退 60
            }
            hz = Math.Max(1, hz);
            return Math.Max(0.5, 1000.0 / hz);
        }

        /// <summary>刷新率 → 推荐画质挡位（高刷降画质保帧率）：≥240→低；144~239→中；50~143→高；-1/0→自动。</summary>
        public static int PresetForRate(int rate)
        {
            if (rate < 0) return 3;
            if (rate >= 240) return 0;
            if (rate >= 144) return 1;
            if (rate >= 50) return 2;
            return 3;
        }

        /// <summary>当前刷新率（GameSettings.RefreshRate 镜像，-1=跟随屏幕，0=无限制）。</summary>
        public static int CurrentRate => GameSettings.RefreshRate;

        /// <summary>持久化钩子（MainForm 注入：rate → 写 AppConfig）。</summary>
        public static Action<int> PersistAction;

        /// <summary>应用刷新率挡位：目标帧时 + 画质联动（自动档保留自动、起点档位按表）。</summary>
        public static void Apply(int rate)
        {
            GameSettings.RefreshRate = rate;
            GraphicsQuality.FrameTargetMs = TargetFrameMs(rate);
            int preset = PresetForRate(rate);
            GraphicsQuality.ApplyPreset(preset);
            if (preset == 3) GraphicsQuality.Effective = 2;   // 自动档起点=高，AutoAdapt 动态调
            try { PersistAction?.Invoke(rate); } catch { }
        }

        /// <summary>描述当前挡位（HUD/日志用）。</summary>
        public static string Describe()
        {
            int rate = CurrentRate;
            string name = NameOf(rate);
            int preset = GraphicsQuality.Preset;
            return "刷新率：" + name + " · 目标帧时 " + (TargetFrameMs(rate) > 0 ? TargetFrameMs(rate).ToString("0.0") + "ms" : "无限制(1000+)")
                + " · 画质 " + GraphicsQuality.Name(GraphicsQuality.Effective) + "（" + (preset switch { 0 => "低", 1 => "中", 2 => "高", _ => "自动" }) + "）";
        }

        public static string NameOf(int rate)
        {
            for (int i = 0; i < Rates.Length; i++) if (Rates[i] == rate) return RateNames[i];
            return rate + "Hz";
        }

        // ---- 自适应兜底（t6：时间化迟滞——墙钟语义与刷新率无关）----
        static double _downSince = -1, _upSince = -1;

        /// <summary>每帧上报实测 FPS（时间戳取系统时钟；等价 TickFps(fps, nowMs)）。</summary>
        public static void TickFps(double fps) => TickFps(fps, Environment.TickCount64);

        /// <summary>每帧上报实测 FPS：低目标 80% 持续 ≥1500ms → 降画质；高目标 120% 持续 ≥4000ms → 升回。</summary>
        public static void TickFps(double fps, double nowMs)
        {
            if (fps <= 0) return;
            double target = CurrentRate > 0 ? CurrentRate : (CurrentRate < 0 ? (ProbedRate() > 0 ? ProbedRate() : 60) : 1000.0);
            if (fps < target * 0.8)
            {
                if (_downSince < 0) _downSince = nowMs;
                _upSince = -1;
                if (nowMs - _downSince >= 1500) { StepDown(); _downSince = -1; }
            }
            else if (fps > target * 1.2)
            {
                if (_upSince < 0) _upSince = nowMs;
                _downSince = -1;
                if (nowMs - _upSince >= 4000) { StepUp(); _upSince = -1; }
            }
            else { _downSince = -1; _upSince = -1; }
        }

        static int ProbedRate()
        {
            try { return ScreenRateProbe?.Invoke() ?? 0; } catch { return 0; }
        }

        static void StepDown() { if (GraphicsQuality.Effective > 0) GraphicsQuality.Effective--; }
        static void StepUp() { if (GraphicsQuality.Effective < 2) GraphicsQuality.Effective++; }

        /// <summary>自检（--selfcheck 宿主段接入）：挡位表不变量 + 时间化迟滞状态机 + 超高刷钳制。</summary>
        public static string SelfCheck()
        {
            var sb = new StringBuilder();
            int fails = 0;
            void Assert(bool cond, string what)
            {
                if (cond) sb.AppendLine("  ✓ " + what);
                else { fails++; sb.AppendLine("  ✗ " + what); }
            }
            // 挡位表不变量
            Assert(TargetFrameMs(0) == 0, "无限制挡目标帧时=0（不钳制）");
            Assert(TargetFrameMs(-1) > 0 && Math.Abs(TargetFrameMs(-1) - 16.666) < 0.01, "跟随屏幕挡（无探测）回退 60Hz→16.7ms");
            Assert(Math.Abs(TargetFrameMs(60) - 16.666) < 0.01 && Math.Abs(TargetFrameMs(240) - 4.1666) < 0.01, "60Hz→16.7ms / 240Hz→4.17ms");
            Assert(TargetFrameMs(1440) >= 0.5 && Math.Abs(TargetFrameMs(2000) - 0.5) < 1e-9, "超高刷钳制：1440Hz≥0.5ms / 2000Hz=0.5ms 最小帧时");
            bool monotonic = true;
            for (int i = 1; i < Rates.Length - 1; i++)
                if (!(TargetFrameMs(Rates[i]) > TargetFrameMs(Rates[i + 1]))) monotonic = false;
            Assert(monotonic, "挡位表帧时严格单调递减（50→500Hz）");
            Assert(PresetForRate(500) == 0 && PresetForRate(240) == 0 && PresetForRate(300) == 0, "≥240Hz→低画质");
            Assert(PresetForRate(144) == 1 && PresetForRate(165) == 1 && PresetForRate(180) == 1, "144/165/180Hz→中画质");
            Assert(PresetForRate(50) == 2 && PresetForRate(60) == 2 && PresetForRate(120) == 2, "50/60/120Hz→高画质");
            Assert(PresetForRate(-1) == 3 && PresetForRate(0) == 3, "跟随屏幕/无限制→自动");
            Assert(Rates.Length == RateNames.Length && Rates[0] == -1 && Rates[Rates.Length - 1] == 0, "挡位表 17 档齐全：-1 首、0=无限制在末尾");
            // 时间化迟滞状态机（显式 nowMs 序列）
            int before = GraphicsQuality.Effective;
            GraphicsQuality.Effective = 2;
            for (int i = 0; i < 16; i++) FpsGovernor.TickFps(300, i * 100.0);    // 持续 1500ms 低目标 80% → 降档
            Assert(GraphicsQuality.Effective == 1, "持续 ≥1500ms 低于目标 80% → 自动降画质一档");
            for (int i = 0; i < 21; i++) FpsGovernor.TickFps(3000, 10000 + i * 200.0);   // 持续 4000ms 高 120% → 升回
            Assert(GraphicsQuality.Effective == 2, "持续 ≥4000ms 高于目标 120% → 自动升画质回档");
            GraphicsQuality.Effective = before;
            return "[刷新率挡位] 全表 17 档（跟随屏幕/50..500/无限制）· 帧时单调 · 画质映射（≥240→低·144~239→中·50~143→高·-1/0→自动）· 时间化迟滞（1.5s 降/4s 升）· 超高刷 0.5ms 钳制 " + (fails == 0 ? "全部通过" : "失败 " + fails + " 项") + System.Environment.NewLine + sb;
        }
    }
}
