using System;

namespace ChartPlayer
{
    /// <summary>
    /// 画质选项：0=低 1=中 2=高 3=自动。
    /// 目标是让 2000 年后发布的任何设备都能以 ≥60 帧流畅运行：
    /// 低画质关闭所有高开销特效，自动档动态监测帧时间并降档。
    /// </summary>
    public static class GraphicsQuality
    {
        public static int Preset = 3;        // 0 低 | 1 中 | 2 高 | 3 自动（默认自动：任何设备都自适应到流畅）
        public static int Effective = 2;     // 自动档下实际生效档位

        // ===== 各档位派生开关 =====
        public static bool ShowBackgroundArt => Effective >= 1;   // 背景曲绘（GPU 大图混合）
        public static bool BurstEffects => Effective >= 1;        // 按键/命中闪光
        public static bool ScreenShake => Effective >= 1;         // MISS 震动
        public static bool HitLineGlow => Effective >= 2;         // 判定线辉光
        public static bool HoldGlowStyle => Effective >= 2;       // 长条辉光样式
        public static bool RightPanelGraphs => Effective >= 1;    // 右侧 KPS/偏差图
        public static bool TextAA => Effective >= 1;              // 文字抗锯齿
        public static double SlantCap => Effective >= 2 ? 1.0 : Effective == 1 ? 0.7 : 0.4;  // 斜轨强度上限
        // 帧率目标：0 = 无限制（每帧都绘制,CPU/GPU 满载——用户要求占用拉到 100%）
        public static double FrameTargetMs = 0;

        public static void ApplyPreset(int preset)
        {
            Preset = Math.Max(0, Math.Min(3, preset));
            Effective = Preset == 3 ? 2 : Preset;
        }

        /// <summary>
        /// 自动档自适应：由游戏循环每帧上报帧时间。
        /// 连续掉帧 → 降档；长时间非常流畅 → 升档（仅在自动档）。
        /// 返回 true 表示档位发生变化（用于 HUD 提示）。
        /// </summary>
        public static bool AutoAdapt(double frameMs)
        {
            if (Preset != 3) return false;
            int before = Effective;
            if (frameMs > 19.0)
            {
                _badFrames++;
                _goodFrames = 0;
                if (_badFrames >= 90 && Effective > 0)   // 约 1.5~3 秒持续掉帧
                {
                    Effective--;
                    _badFrames = 0;
                }
            }
            else if (frameMs < 9.0)
            {
                _goodFrames++;
                _badFrames = 0;
                if (_goodFrames >= 900 && Effective < 2)   // 约 15 秒持续流畅
                {
                    Effective++;
                    _goodFrames = 0;
                }
            }
            else { _badFrames = 0; _goodFrames = 0; }
            return before != Effective;
        }

        static int _badFrames, _goodFrames;
        static double _badMs, _goodMs;

        /// <summary>t6（t3 §3.2）单一仲裁：游戏循环自适应只经此入口（frameMs=近期帧时 EMA，ms）。
        /// 时间化迟滞（墙钟语义，与刷新率无关）：frameMs&gt;目标×1.25 累计 ≥1500ms → 降档；
        /// frameMs&lt;目标×0.83 累计 ≥4000ms → 升回。仅自动档生效；无限制档以 60fps 为画质仲裁基准。
        /// 与 FpsGovernor.TickFps（外部/自检用）不再双路并存——游戏层只调本入口。</summary>
        public static bool Step(double frameMs)
        {
            if (Preset != 3 || frameMs <= 0) return false;
            double target = FrameTargetMs > 0 ? FrameTargetMs : 16.67;
            int before = Effective;
            if (frameMs > target * 1.25)
            {
                _badMs += frameMs; _goodMs = 0;
                if (_badMs >= 1500 && Effective > 0) { Effective--; _badMs = 0; }
            }
            else if (frameMs < target * 0.83)
            {
                _goodMs += frameMs; _badMs = 0;
                if (_goodMs >= 4000 && Effective < 2) { Effective++; _goodMs = 0; }
            }
            else { _badMs = 0; _goodMs = 0; }
            return before != Effective;
        }

        public static string Name(int level)
            => level <= 0 ? "低" : level == 1 ? "中" : "高";
    }
}
