using System.Collections.Generic;
using System.Windows.Forms;

namespace ChartPlayer
{
    public static class GameSettings
    {
        public static double Speed = 2.00;   // 默认流速 2
        public static float SuperSample = LoadSs();   // 超采样渲染因子：1.4 = 帧率/GPU 满载最佳平衡（超 4096 硬件上限自动钳制回退）
        static float LoadSs()
        {
            try
            {
                var v = System.Environment.GetEnvironmentVariable("CHART_SS");
                if (v != null && float.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 1f && f <= 1.6f) return f;
            }
            catch { }
            return 1.4f;
        }
        public static int SuperSampleStages = LoadStages();   // 乒乓放大级数（GPU 满载：每级一次全屏填充）
        static int LoadStages()
        {
            try
            {
                var v = System.Environment.GetEnvironmentVariable("CHART_SS_STAGES");
                if (v != null && int.TryParse(v, out int n) && n >= 1 && n <= 96) return n;
            }
            catch { }
            return 48;
        }
        public static double Offset = 0;
        public static bool Autoplay = false;
        public static int Volume = 80;
        public static int JudgeBase = 0;   // t58：默认=音符下端——vision 三读 legacy 设置截图证实其收起态显示「音符下端」；cfg 存在时仍以 AppConfig.JudgeBase（=1,音符中心）为准，仅无 cfg 路径（离屏 shellshot/首启）对齐 legacy 画面
        public static bool ShowFps = false;   // 显示 FPS（不显示帧生成延迟）
        public static bool ForceWarp = false;  // 常态硬件 GPU 照常调用（用户 08-26 确认：常态也调用GPU）；仅 AI 演示/陪玩会话中检测到本地 AI 服务占显存时临时置真，离开会话恢复
        public static int RefreshRate = 0;    // 刷新率挡位（0=无限制 1000+；60/120/144/165/240/360），由 FpsGovernor.Apply 设置

        // ===== osu!std 框定游玩区 =====
        /// <summary>游玩界面框定区域：按 osu!standard 的 4:3（512×384）等比居中框定，
        /// 游戏内容只在该区域内渲染（开启后窗口两侧/上下留边）。</summary>
        public static bool OsuStdPlayfield = true;

        /// <summary>osu!standard 官方默认皮肤贴图（接近圈/圆圈，Chart\皮肤\osu_default；无素材时自动回退矢量渲染）。</summary>
        public static bool OsuDefaultSkin = true;

        // ===== Milestone：斜轨 / 3D / 打击表现 =====
        public static bool SlantEnabled = false;     // 斜轨开关（false = 直轨垂直下落，与原音游一致；T 键可开 Malody 风格透视）
        public static bool Camera3D = false;         // 3D 渲染开关（false = 经典 2D，与原音游视角一致）
        public static double CameraPitch = 20;       // 俯仰角（度，0~60）
        public static double CameraYaw = 4;          // 偏航角（度，-30~30）
        public static double CameraDepth = 1.0;      // 透视深度系数（0.4~2.5）
        public static int HitsoundStyle = 0;         // 打击音效风格：0 经典 1 电子 2 木鱼
        public static bool HitsoundPerJudge = true;  // 按判定区分音高/音色
        public static int HitsoundVolume = 70;       // 打击音效音量 0~100
        public static bool ShowHitFx = true;         // 打击特效（爆点/粒子/闪光）
        public static bool ResultScreenEnabled = true; // 结算画面
        public static int UiTheme = 0;               // UI 主题：0 深空蓝 1 极夜紫 2 晨光青
        public static int TransitionStyle = -1;      // 转场风格：-1 随机 0 光束 1 圆环 2 推拉
        public static bool TestModes = false;        // 开发者模式已移除：字段仅 CLI --* 工具入口保留设置（玩法门控/UI/config 均已移除，不再影响任何玩法可见性）

        public static readonly Dictionary<int, Keys[]> KeyMaps = new Dictionary<int, Keys[]>
        {
            [4]  = new[] { Keys.D, Keys.F, Keys.J, Keys.K },
            [5]  = new[] { Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K },
            [6]  = new[] { Keys.S, Keys.D, Keys.F, Keys.J, Keys.K, Keys.L },
            [7]  = new[] { Keys.S, Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K, Keys.L },
            [8]  = new[] { Keys.S, Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K, Keys.L, Keys.OemSemicolon },
            [9]  = new[] { Keys.A, Keys.S, Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K, Keys.L, Keys.OemSemicolon },
            [10] = new[] { Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K, Keys.L, Keys.OemSemicolon }
        };

        public static Keys[] GetKeys(int kc)
        {
            if (!KeyMaps.TryGetValue(kc, out var k))
            {
                k = new Keys[kc];
                for (int i = 0; i < kc; i++) k[i] = Keys.D + i;
                KeyMaps[kc] = k;
            }
            return k;
        }
    }
}
