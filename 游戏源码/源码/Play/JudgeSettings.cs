using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ChartPlayer
{
    public class JudgeLevel
    {
        public string Name { get; set; }
        public double Window { get; set; }
        public int Score { get; set; }
        public double Weight { get; set; }
        public bool BreaksCombo { get; set; }   // IIDX BAD 等：判定成功但断连击
    }

    public class JudgePresetDto
    {
        public string Name { get; set; } = "";
        public List<JudgeLevel> Levels { get; set; } = new List<JudgeLevel>();
        public double MissWindow { get; set; } = 200;
        public int ComboBonusMax { get; set; } = 100;
    }

    public static class JudgeSettings
    {
        public static List<JudgeLevel> Levels = MakeLevels(new[] { 40.0, 80, 120, 160 }, 300, 50);
        public static double MissWindow = 200;
        public static int ComboBonusMax = 100;
        public static string PresetKey = "std2";

        // ===== 段位 HP 系统（osu!mania 段位按 osu!lazer ManiaHealthProcessor 建模） =====
        public static double HpMax = 100;
        public static double HpDrainPerSec = 1.6;    // 持续掉血（每秒，Malody 段位用；osu 段位=0，无被动掉血）
        public static double HpPerfect = 1.8;
        public static double HpGreat = 1.2;
        public static double HpGood = 0.5;
        public static double HpBad = -4.5;
        public static double HpMiss = -9.0;
        public static double HpHoldTickPerSec = 0.6;  // 长条按住期间每秒回血（osu 段位=0）

        // ===== osu!mania 段位 HP（lazer 模型，仅 osu 段位谱面启用） =====
        public static bool OsuHpEnabled = false;
        public static double OsuDr = 8;               // HPDrainRate
        public static double OsuHpMultiplier = 1;     // HpMultiplierNormal（按谱面校准）
        public static bool DanHpEnabled = true;       // 段位是否启用 HP（osu=存活制；Malody=仅 ACC，无血量）

        /// <summary>osu!lazer 两段线性 DifficultyRange 映射（min@od0, mid@od5, max@od10）。</summary>
        public static double OsuDifficultyRange(double difficulty, double min, double mid, double max)
        {
            if (difficulty > 5) return mid + (max - mid) * (difficulty - 5) / 5.0;
            if (difficulty < 5) return mid + (mid - min) * (difficulty - 5) / 5.0;
            return mid;
        }

        /// <summary>lazer mania 判定窗口：Floor(DifficultyRange(od,…)) + 0.5。</summary>
        public static double ManiaWindow(double od, double min, double mid, double max)
            => Math.Floor(OsuDifficultyRange(od, min, mid, max)) + 0.5;

        /// <summary>
        /// lazer HpMultiplierNormal 校准：以全 Perfect 模拟计算恢复率，
        /// 直到平均每音符恢复 ≥ hpRecoveryAvailable = DifficultyRange(DR, 0.04, 0.02, 0)。
        /// </summary>
        public static double ComputeOsuHpMultiplier(double dr)
        {
            double recoveryAvail = OsuDifficultyRange(dr, 0.04, 0.02, 0);
            double perPerfect = 0.0055 - dr * 0.0005;
            if (perPerfect <= 0) return 1;
            double m = 1.0;
            for (int i = 0; i < 10000; i++)
            {
                if (perPerfect * m >= recoveryAvail) return m;
                m *= 1.01;
            }
            return Math.Min(100, m);
        }

        /// <summary>按判定等级返回 HP 增量（0~100 刻度；osu 段位走 lazer 模型）。</summary>
        public static double HpFor(string name)
        {
            if (OsuHpEnabled) return OsuHpDelta(name);
            switch (name)
            {
                case "MISS": return HpMiss;
                default:
                    int i = _levelIndex.TryGetValue(name, out int ix) ? ix : Levels.Count;
                    int n = Levels.Count;
                    if (i >= n) i = n - 1;
                    double t = n <= 1 ? 0 : (double)i / (n - 1);
                    if (t <= 0.25) return HpPerfect;
                    if (t <= 0.55) return HpGreat;
                    if (t <= 0.8) return HpGood;
                    return HpBad;
            }
        }

        /// <summary>lazer ManiaHealthProcessor 单次判定 HP 增量（HP 刻度 0..1 → 乘 100）。</summary>
        static double OsuHpDelta(string name)
        {
            double dr = OsuDr;
            switch (name)
            {
                case "Perfect": return (0.0055 - dr * 0.0005) * OsuHpMultiplier * 100;
                case "Great": return (0.005 - dr * 0.0005) * OsuHpMultiplier * 100;
                case "Good": return (0.004 - dr * 0.0004) * OsuHpMultiplier * 100;
                case "Ok": return 0;
                case "Meh": return -(dr + 1) * 0.0016 * 100;
                case "MISS": return -(dr + 1) * 0.0075 * 100;
                default: return 0;
            }
        }

        /// <summary>长条提前松开的 HP 惩罚（lazer：hold 头/尾 MISS 半额）。</summary>
        public static double HpForHoldMiss()
            => OsuHpEnabled ? -(OsuDr + 1) * 0.00375 * 100 : HpMiss;

        public static readonly (string Name, double Min)[] Grades =
        {
            ("SSS", 100), ("SS", 98), ("S", 95), ("A", 90), ("B", 80), ("C", 70), ("D", 0)
        };

        static readonly string[] OsNames = { "Marvelous", "Perfect", "Great", "Good", "Bad" };
        static readonly string[] MalNames = { "Best", "Cool", "Good", "Miss" };

        // 预设值：键名 = 预设标识；值 (n, windows, miss, names, displayName)
        public static readonly Dictionary<string, (int n, double[] w, double miss, string[] names, string display)> Presets = new()
        {
            ["extreme"] = (5, new[] { 1.0, 2, 3, 4, 5 }, 40, null, "Extreme"),
            ["hard"] = (5, new[] { 25.0, 50, 75, 100 }, 160, null, "Hard"),
            ["std2"] = (5, new[] { 40.0, 80, 120, 160 }, 200, null, "标准 2"),
            ["std"] = (5, new[] { 50.0, 100, 150, 220 }, 260, null, "标准"),
            ["osuOD0"] = (5, new[] { 16.0, 64, 97, 127, 151 }, 188, OsNames, "osu!mania OD0"),
            ["osuOD1"] = (5, new[] { 16.0, 61, 94, 124, 148 }, 185, OsNames, "osu!mania OD1"),
            ["osuOD2"] = (5, new[] { 16.0, 58, 91, 121, 145 }, 182, OsNames, "osu!mania OD2"),
            ["osuOD3"] = (5, new[] { 16.0, 55, 88, 118, 142 }, 179, OsNames, "osu!mania OD3"),
            ["osuOD4"] = (5, new[] { 16.0, 52, 85, 115, 139 }, 176, OsNames, "osu!mania OD4"),
            ["osuOD5"] = (5, new[] { 16.0, 49, 82, 112, 136 }, 173, OsNames, "osu!mania OD5"),
            ["osuOD6"] = (5, new[] { 16.0, 46, 79, 109, 133 }, 170, OsNames, "osu!mania OD6"),
            ["osuOD7"] = (5, new[] { 16.0, 43, 76, 106, 130 }, 167, OsNames, "osu!mania OD7"),
            ["osuOD8"] = (5, new[] { 16.0, 40, 73, 103, 127 }, 164, OsNames, "osu!mania OD8"),
            ["osuOD9"] = (5, new[] { 16.0, 37, 70, 100, 124 }, 161, OsNames, "osu!mania OD9"),
            ["osuOD10"] = (5, new[] { 16.0, 34, 67, 97, 121 }, 158, OsNames, "osu!mania OD10"),
            ["qPeaceful"] = (5, new[] { 23.0, 57, 101, 141, 169 }, 218, OsNames, "Quaver Peaceful"),
            ["qLenient"] = (5, new[] { 21.0, 52, 91, 128, 153 }, 198, OsNames, "Quaver Lenient"),
            ["qChill"] = (5, new[] { 19.0, 47, 83, 116, 139 }, 180, OsNames, "Quaver Chill"),
            ["qstd"] = (5, new[] { 18.0, 43, 76, 106, 127 }, 164, OsNames, "Quaver Standard"),
            ["qStrict"] = (5, new[] { 16.0, 39, 69, 96, 115 }, 149, OsNames, "Quaver Strict"),
            ["qTough"] = (5, new[] { 14.0, 35, 62, 87, 104 }, 135, OsNames, "Quaver Tough"),
            ["qExtreme"] = (5, new[] { 13.0, 32, 57, 79, 95 }, 123, OsNames, "Quaver Extreme"),
            ["qImpossible"] = (5, new[] { 8.0, 20, 35, 49, 59 }, 76, OsNames, "Quaver Impossible"),
            ["smJ1"] = (5, new[] { 33.0, 68, 135, 203, 270 }, 200, OsNames, "SM/Etterna J1"),
            ["smJ2"] = (5, new[] { 29.0, 60, 120, 180, 239 }, 200, OsNames, "SM/Etterna J2"),
            ["smJ3"] = (5, new[] { 26.0, 52, 104, 157, 209 }, 200, OsNames, "SM/Etterna J3"),
            ["smJ4"] = (5, new[] { 22.0, 45, 90, 135, 180 }, 200, OsNames, "SM/Etterna J4"),
            ["smJ5"] = (5, new[] { 18.0, 38, 76, 113, 180 }, 200, OsNames, "SM/Etterna J5"),
            ["smJ6"] = (5, new[] { 15.0, 30, 59, 89, 180 }, 200, OsNames, "SM/Etterna J6"),
            ["smJ7"] = (5, new[] { 11.0, 23, 45, 68, 180 }, 200, OsNames, "SM/Etterna J7"),
            ["smJ8"] = (5, new[] { 7.0, 15, 30, 45, 180 }, 200, OsNames, "SM/Etterna J8"),
            ["smJustice"] = (5, new[] { 4.0, 9, 18, 27, 180 }, 200, OsNames, "SM/Etterna Justice"),
            ["malEasy"] = (5, new[] { 56.0, 76, 96, 113, 130 }, 154, OsNames, "Malody Easy"),
            ["malEasyP"] = (5, new[] { 48.0, 68, 88, 105, 122 }, 154, OsNames, "Malody Easy+"),
            ["malNorm"] = (5, new[] { 40.0, 60, 80, 97, 114 }, 154, OsNames, "Malody Normal"),
            ["malNormP"] = (5, new[] { 32.0, 52, 72, 89, 106 }, 154, OsNames, "Malody Normal+"),
            ["malHard"] = (5, new[] { 24.0, 44, 64, 81, 98 }, 154, OsNames, "Malody Hard"),
            ["malPEasy"] = (5, new[] { 52.0, 72, 92, 109, 126 }, 150, OsNames, "Malody P Easy"),
            ["malPEasyP"] = (5, new[] { 44.0, 64, 84, 101, 118 }, 150, OsNames, "Malody P Easy+"),
            ["malPNorm"] = (5, new[] { 36.0, 56, 76, 93, 110 }, 150, OsNames, "Malody P Normal"),
            ["malPNormP"] = (5, new[] { 28.0, 48, 68, 85, 102 }, 150, OsNames, "Malody P Normal+"),
            ["malPHard"] = (5, new[] { 20.0, 40, 60, 77, 94 }, 150, OsNames, "Malody P Hard"),

            // ===== 按表新增：Malody 4.3.7 / V6.6.22 各模式 Judge A-E（4 级判定） =====
            ["malPC_A"] = (4, new[] { 56.0, 96, 130 }, 180, MalNames, "Malody 4.3.7 PC · Judge A"),
            ["malPC_B"] = (4, new[] { 46.0, 86, 120 }, 170, MalNames, "Malody 4.3.7 PC · Judge B"),
            ["malPC_C"] = (4, new[] { 36.0, 76, 110 }, 160, MalNames, "Malody 4.3.7 PC · Judge C"),
            ["malPC_D"] = (4, new[] { 28.0, 68, 102 }, 152, MalNames, "Malody 4.3.7 PC · Judge D"),
            ["malPC_E"] = (4, new[] { 20.0, 60, 94 }, 144, MalNames, "Malody 4.3.7 PC · Judge E"),
            ["malPE_A"] = (4, new[] { 65.0, 105, 140 }, 190, MalNames, "Malody 4.3.7 PE · Judge A"),
            ["malPE_B"] = (4, new[] { 55.0, 95, 130 }, 180, MalNames, "Malody 4.3.7 PE · Judge B"),
            ["malPE_C"] = (4, new[] { 45.0, 85, 120 }, 170, MalNames, "Malody 4.3.7 PE · Judge C"),
            ["malPE_D"] = (4, new[] { 37.0, 77, 112 }, 162, MalNames, "Malody 4.3.7 PE · Judge D"),
            ["malPE_E"] = (4, new[] { 29.0, 69, 104 }, 154, MalNames, "Malody 4.3.7 PE · Judge E"),
            ["malV6S_A"] = (4, new[] { 65.0, 105, 150 }, 190, MalNames, "Malody V6.6.22 Stb · Judge A"),
            ["malV6S_B"] = (4, new[] { 55.0, 95, 140 }, 180, MalNames, "Malody V6.6.22 Stb · Judge B"),
            ["malV6S_C"] = (4, new[] { 45.0, 85, 130 }, 170, MalNames, "Malody V6.6.22 Stb · Judge C"),
            ["malV6S_D"] = (4, new[] { 37.0, 77, 122 }, 162, MalNames, "Malody V6.6.22 Stb · Judge D"),
            ["malV6S_E"] = (4, new[] { 29.0, 69, 114 }, 154, MalNames, "Malody V6.6.22 Stb · Judge E"),
            ["malV6P_A"] = (4, new[] { 56.0, 96, 130 }, 180, MalNames, "Malody V6.6.22 Pro · Judge A"),
            ["malV6P_B"] = (4, new[] { 46.0, 86, 120 }, 170, MalNames, "Malody V6.6.22 Pro · Judge B"),
            ["malV6P_C"] = (4, new[] { 36.0, 76, 110 }, 160, MalNames, "Malody V6.6.22 Pro · Judge C"),
            ["malV6P_D"] = (4, new[] { 28.0, 68, 102 }, 152, MalNames, "Malody V6.6.22 Pro · Judge D"),
            ["malV6P_E"] = (4, new[] { 20.0, 60, 94 }, 144, MalNames, "Malody V6.6.22 Pro · Judge E"),
        };

        public static List<JudgeLevel> MakeLevels(double[] windows, int firstScore, int lastScore)
        {
            var list = new List<JudgeLevel>();
            int n = windows.Length;
            for (int i = 0; i < n; i++)
            {
                double t = n <= 1 ? 0 : (double)i / (n - 1);
                list.Add(new JudgeLevel
                {
                    Name = DefaultName(i, n),
                    Window = windows[i],
                    Score = (int)Math.Round(firstScore - (firstScore - lastScore) * t),
                    Weight = Math.Round(1 - 0.95 * t, 3)
                });
            }
            return list;
        }

        public static string DefaultName(int i, int n)
        {
            if (i == 0) return "PERFECT";
            if (i == n - 1) return "BAD";
            if (n == 3) return "GREAT";
            if (i == 1) return "GREAT";
            if (i == n - 2) return "GOOD";
            return "Lv" + (i + 1);
        }

        public static void ApplyPreset(string key)
        {
            if (!Presets.TryGetValue(key, out var p)) { key = "std2"; p = Presets[key]; }
            PresetKey = key;
            // osuOD 系列：按 osu!lazer 默认 mania 判定窗口公式动态生成（DifficultyRange 两段线性 + Floor + 0.5）
            if (key.StartsWith("osuOD", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.Substring(5), out int od))
            {
                od = Math.Max(0, Math.Min(10, od));
                Levels = new List<JudgeLevel>
                {
                    new JudgeLevel { Name = "Perfect", Window = ManiaWindow(od, 22.4, 19.4, 13.9), Score = 305, Weight = 1.0 },
                    new JudgeLevel { Name = "Great",   Window = ManiaWindow(od, 64, 49, 34),     Score = 300, Weight = 1.0 },
                    new JudgeLevel { Name = "Good",    Window = ManiaWindow(od, 97, 82, 67),     Score = 200, Weight = 2.0 / 3 },
                    new JudgeLevel { Name = "Ok",      Window = ManiaWindow(od, 127, 112, 97),   Score = 100, Weight = 1.0 / 3 },
                    new JudgeLevel { Name = "Meh",     Window = ManiaWindow(od, 151, 136, 121),  Score = 50,  Weight = 1.0 / 6 }
                };
                MissWindow = ManiaWindow(od, 188, 173, 158);
                RebuildLevelIndex();
                return;
            }
            int n = p.n;
            var wins = new double[n];
            for (int i = 0; i < n; i++) wins[i] = i < p.w.Length ? p.w[i] : p.w[Math.Min(i, p.w.Length - 1)];
            Levels = MakeLevels(wins, 300, 50);
            if (p.names != null)
                for (int i = 0; i < n && i < p.names.Length; i++) Levels[i].Name = p.names[i];
            // Malody 4 级判定（Best/Cool/Good/Miss）：第 4 档覆盖 [Good 窗口, Miss 窗口)，
            // 超 Good 记 MISS（断连、ACC 权重 0）；ACC 权重 Best/Cool/Good = 100/80/60
            if (p.names == MalNames && n == 4)
            {
                Levels[3].Name = "MISS";
                Levels[3].Window = p.miss;
                Levels[0].Weight = 1.0; Levels[0].Score = 300;
                Levels[1].Weight = 0.8; Levels[1].Score = 240;
                Levels[2].Weight = 0.6; Levels[2].Score = 180;
                Levels[3].Weight = 0;   Levels[3].Score = 0;
            }
            MissWindow = p.miss;
            RebuildLevelIndex();
        }

        /// <summary>应用自定义判定（持久化用）。</summary>
        public static void ApplyCustom(List<JudgeLevel> levels, double miss, int combo)
        {
            if (levels == null || levels.Count < 3) { ApplyPreset("std2"); return; }
            Levels = new List<JudgeLevel>(levels);
            MissWindow = miss > 0 ? miss : 200;
            ComboBonusMax = combo > 0 ? combo : 100;
            PresetKey = "custom";
            RebuildLevelIndex();
        }

        public static string GradeFor(double acc)
        {
            foreach (var g in Grades) if (acc >= g.Min) return g.Name;
            return "?";
        }

        /// <summary>
        /// 谱面按来源与模式套用对应判定与 HP 模型（参照 osu!lazer / Malody）：
        ///   所有 .osu 谱面：std→300/100/50、taiko→良/可、catch→FRUIT、mania→按文件 OD
        ///   段位 .mc → C 判（Best/Cool/Good，36/76/110ms）且仅 ACC 判定（无血量）
        ///   段位 .osu mania → 额外启用 lazer mania HP（无被动掉血、DR 校准、MISS 掉血）
        /// </summary>
        public static void ApplyForChart(Chart c)
        {
            // 先恢复默认配置
            OsuHpEnabled = false;
            DanHpEnabled = true;
            HpDrainPerSec = 1.6;
            HpHoldTickPerSec = 0.6;
            if (c == null) return;
            try
            {
                var ext = Path.GetExtension(c.SourcePath ?? "").ToLowerInvariant();
                if (ext == ".mc")
                {
                    if (c.IsDan)
                    {
                        ApplyPreset("malPC_C");
                        DanHpEnabled = false;   // Malody 段位：仅 ACC 判定，无血量
                        HpDrainPerSec = 0;
                        HpHoldTickPerSec = 0;
                    }
                    return;
                }
                if (ext == ".osu")
                {
                    if (c.Mode == GameMode.OsuStandard)
                    {
                        // osu!standard 判定：300/100/50 窗口随 OD 线性收窄（stable 公式）
                        double odStd = Math.Max(0, Math.Min(10, c.Od > 0 ? c.Od : 5));
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "300", Window = Math.Max(16, 79.5 - 6 * odStd), Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "100", Window = Math.Max(40, 139.5 - 8 * odStd), Score = 100, Weight = 1.0 / 3 },
                            new JudgeLevel { Name = "50",  Window = Math.Max(70, 199.5 - 10 * odStd), Score = 50, Weight = 1.0 / 6 }
                        };
                        MissWindow = Levels[2].Window;
                        PresetKey = "osuStd";
                        RebuildLevelIndex();
                        return;
                    }
                    // osu!mania：按文件 OD 套用判定（所有 mania 谱面，非段位也生效）
                    int od = Math.Max(0, Math.Min(10, (int)Math.Round(c.Od > 0 ? c.Od : 8)));
                    ApplyPreset("osuOD" + od);
                    if (c.IsDan)
                    {
                        // osu!lazer ManiaHealthProcessor：mania 无被动掉血；恢复与惩罚按 DR 计算
                        OsuHpEnabled = true;
                        DanHpEnabled = true;
                        OsuDr = c.Dr > 0 ? Math.Max(0, Math.Min(10, c.Dr)) : 8;
                        OsuHpMultiplier = ComputeOsuHpMultiplier(OsuDr);
                        HpDrainPerSec = 0;
                        HpHoldTickPerSec = 0;
                    }
                }

                // 其他主流音游：按模式套用对应判定（窗口取自各游戏公开资料/社区标准）
                switch (c.Mode)
                {
                    case GameMode.Phigros:  // Phigros 2.0：Perfect ±80 / Good ±160 / Bad ±180（Drag 不计判定）
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "Perfect", Window = 80, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "Good",    Window = 160, Score = 195, Weight = 0.65 },
                            new JudgeLevel { Name = "Bad",     Window = 180, Score = 0, Weight = 0, BreaksCombo = true }
                        };
                        MissWindow = 180; PresetKey = "phigros";
                        break;
                    case GameMode.Arcaea:   // Arcaea：大PURE ±25 / PURE ±50 / FAR ±100 / LOST
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "PURE+", Window = 25, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "PURE",  Window = 50, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "FAR",   Window = 100, Score = 150, Weight = 0.5 }
                        };
                        MissWindow = 100; PresetKey = "arcaea";
                        break;
                    case GameMode.Cytus:    // Cytus 初代：PERFECT/GOOD/BAD/MISS 四档（无 C.PERFECT——那是 Cytus II 的判定）；
                                            // TP 权重 Perfect 100 / Good 70 / Bad 30；毫秒窗口官方未公布，
                                            // 取社区共识参考值（比 Cytus II 宽容）：PERFECT ±75 / GOOD ±150 / BAD ±220
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "PERFECT", Window = 75, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "GOOD",    Window = 150, Score = 210, Weight = 0.7 },
                            new JudgeLevel { Name = "BAD",     Window = 220, Score = 90, Weight = 0.3 }
                        };
                        MissWindow = 220; PresetKey = "cytus";
                        break;
                    case GameMode.Adofai:   // Routlock（原 ADOFAI 单轨更名保留）：角度判定（PURE 30°/PERFECT 45°/COUNTED 60°，随 BPM 换算 + 20/30/65ms 时间下限）
                                            // 有效窗口 = max(角度换算窗口, 时间下限)（策划调研修正 2026-08-21：
                                            // 时间下限是最低要求，角度窗口更严格时生效；原 min() 语义相反——低 BPM 下反而更严）
                    case GameMode.AdofaiReal:   // 真实 ADOFAI（冰与火之舞）：同一角度判定口径
                    {
                        // 角度→时间换算：一拍 = 180° = 60000/BPM ms → 时间 = 角度°/180 × beatMs
                        // （策划复核 2026-08-22 修正：原 deg/360 系数错一半，窗口整体偏小 2 倍——
                        //   180° 代入应得 1 拍而非 0.5 拍；BPM120 时 PURE=30°→83.3ms、COUNTED=60°→166.7ms）
                        double beatMs = 60000.0 / Math.Max(30, c.Bpm > 0 ? c.Bpm : 120);
                        double AngleMs(double deg, double floor) => Math.Max(floor, deg / 180.0 * beatMs);
                        double pure = AngleMs(30, 25), perfect = AngleMs(45, 30), counted = AngleMs(60, 65);
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "PURE",    Window = pure,    Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "PERFECT", Window = perfect, Score = 225, Weight = 0.75 },
                            new JudgeLevel { Name = "COUNTED", Window = counted, Score = 120, Weight = 0.4 }
                        };
                        MissWindow = counted; PresetKey = "adofai";
                        break;
                    }
                    case GameMode.Iidx:     // beatmania IIDX：PGREAT ±16.67 / GREAT ±33.33 / GOOD ±116.67 / BAD ±250（BAD 断连击）
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "PGREAT", Window = 16.67, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "GREAT",  Window = 33.33, Score = 150, Weight = 0.5 },
                            new JudgeLevel { Name = "GOOD",   Window = 116.67, Score = 0, Weight = 0 },
                            new JudgeLevel { Name = "BAD",    Window = 250, Score = 0, Weight = 0, BreaksCombo = true }
                        };
                        MissWindow = 250; PresetKey = "iidx";
                        break;
                    case GameMode.Maimai:   // maimai DX 判定（官方/社区口径）：PERFECT ±31.25 / GREAT ±62.5 / GOOD ±125（GOOD 断连）
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "PERFECT", Window = 31.25, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "GREAT",   Window = 62.5,  Score = 150, Weight = 0.5 },
                            new JudgeLevel { Name = "GOOD",    Window = 125,   Score = 0, Weight = 0, BreaksCombo = true }
                        };
                        MissWindow = 125; PresetKey = "maimai";
                        break;
                    case GameMode.Mania:    // 编辑器无源 Mania 谱面：标准 2 判定（避免继承上一模式的预设）
                        ApplyPreset("std2");
                        break;
                    case GameMode.LoopComposer:   // 回环作曲（录奏一致性）：Cytus 口径 PERFECT/GOOD/BAD（权重 1/0.7/0.3）
                        Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel { Name = "PERFECT", Window = 75, Score = 300, Weight = 1.0 },
                            new JudgeLevel { Name = "GOOD",    Window = 150, Score = 210, Weight = 0.7 },
                            new JudgeLevel { Name = "BAD",     Window = 220, Score = 90, Weight = 0.3 }
                        };
                        MissWindow = 220; PresetKey = "loopCompose";
                        break;
                    default:                // 其余/未知模式：同样重置为标准判定
                        ApplyPreset("std2");
                        break;
                }
                RebuildLevelIndex();   // switch 内直接改 Levels 的分支（Phigros/Arcaea/Cytus/Adofai/Iidx/Maimai）
            }
            catch { }
        }

        /// <summary>段位过段 ACC 线（供结算 PASS/FAIL 判定与界面显示）：Malody Extra=96、Malody Regular/Dan=95、osu=96。</summary>
        public static double DanPassAccFor(Chart c)
        {
            if (c == null) return 96.0;
            var src = (c.SourcePath ?? "").ToLowerInvariant();
            if (src.EndsWith(".mc"))
            {
                string s = (c.Title ?? "") + " " + (c.DanSet ?? "");
                return s.IndexOf("Extra", StringComparison.OrdinalIgnoreCase) >= 0 ? 96.0 : 95.0;
            }
            return 96.0;
        }

        public static double WeightFor(string name)
        {
            if (_levelIndex.TryGetValue(name, out int idx) && idx < Levels.Count) return Levels[idx].Weight;
            return 0;
        }

        public static int LevelScore(string name, int combo)
        {
            int s = _levelIndex.TryGetValue(name, out int idx) && idx < Levels.Count ? Levels[idx].Score : 0;
            return (int)Math.Round(s * (1 + Math.Min(combo, ComboBonusMax) * 0.01));
        }

        // 判定名 → 下标缓存（Levels 由 ApplyPreset/ApplyCustom/ApplyForChart 整体替换后重建）
        static Dictionary<string, int> _levelIndex = BuildLevelIndex();
        static Dictionary<string, int> BuildLevelIndex()
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < Levels.Count; i++) d[Levels[i].Name] = i;
            return d;
        }
        static void RebuildLevelIndex() => _levelIndex = BuildLevelIndex();
    }

    /// <summary>判定预设的 JSON 导出/导入。</summary>
    public static class JudgePresetIO
    {
        public static void Export(string path, string name)
        {
            var dto = new JudgePresetDto
            {
                Name = name,
                Levels = JudgeSettings.Levels,
                MissWindow = JudgeSettings.MissWindow,
                ComboBonusMax = JudgeSettings.ComboBonusMax
            };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static JudgePresetDto Import(string path)
        {
            return JsonSerializer.Deserialize<JudgePresetDto>(File.ReadAllText(path));
        }
    }
}
