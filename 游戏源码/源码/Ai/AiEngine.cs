using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ChartPlayer
{
    public class AiLevel
    {
        public double Avg, Hard;
        public string Name, Dan;
        public AiLevel(double avg, double hard, string name, string dan)
        { Avg = avg; Hard = hard; Name = name; Dan = dan; }

        // ===== AI 训练（段位过段条件） =====
        public double PassAcc = 95.0;          // 过段 ACC 门槛（%），启动时按 Dan 组内序号填充
        public double TunedScale = 1.0;        // 偏差缩放旋钮（训练调参，越大偏差越大）
        public double TunedMissBias = 0.0;     // 失误概率偏移旋钮（训练调参，越大 MISS 越多）
        public double TrainedHpEnd = -1;       // 最近训练预测 HP 终值（-1 表示未训练）
    }

    public class AiPlayer
    {
        public AiLevel Level;
        public string Name;
        public bool Active = true;
        public int NoteIdx;
        public List<double> HitTimes = new List<double>();
        public double Stamina = 100;
        public bool Exhausted;
        public double ExhaustUntil, OverLimitTime;
        public double LastTick = double.NaN;
        public double Score;
        public int Combo, MaxCombo, Judged, HitCount;
        public double Acc = 100, WeightSum;
        public Dictionary<string, int> Hits = new Dictionary<string, int>();
        public int KpsNow;

        // ===== 紧张度系统 =====
        public double Tension;             // 紧张度 0~100
        public double LastTickT = double.NaN;
        public List<double> Devs = new List<double>();   // 命中偏差记录（用于计算 UR）
        public double Ur;                 // 不稳定率（= 偏差标准差 × 10）

        // ===== 对拍模式 =====
        public bool StreamMode;           // 高密度时可能进入；进入后紧张度每秒 -5%

        // ===== 超神模式 =====
        public bool GodMode;              // 超神中：所有 KPS 阈值 +5
        public bool GodUsed;              // 每局只能触发一次

        // ===== 击打计划（每个 AI 独立，避免多 AI 共享谱面标记导致击打完全一致） =====
        public List<(int Idx, bool Miss, double Dev, double Time)> Plan = new List<(int, bool, double, double)>();
    }

    public static class AiEngine
    {
        /// <summary>
        /// osu!mania + Malody AI 预设（依据 kps_summary 统计表制定）：
        /// Avg=平均KPS(非零区间)，Hard=最大KPS(整秒)；
        /// 标题含 REFORM 的相同 osu!mania 难度已合并取平均；预设标题 = 各难度名。
        /// </summary>
        public static readonly AiLevel[] Levels =
        {
            /* ================= osu!mania ================= */

            // Dan ~ REFORM ~ INTRO
            new(3.55,  6.25, "INTRO-1st", "Dan ~ REFORM ~ INTRO"),
            new(4.97,  10.75, "INTRO-2nd", "Dan ~ REFORM ~ INTRO"),
            new(6.83,  12.60, "INTRO-3rd", "Dan ~ REFORM ~ INTRO"),

            // osu!mania 标准段位（4K Dan）
            new(8.31,  16.00, "1st Dan", "osu!mania 4K Dan"),
            new(10.11, 20.00, "2nd Dan", "osu!mania 4K Dan"),
            new(10.47, 19.00, "3rd Dan", "osu!mania 4K Dan"),
            new(12.36, 23.00, "4th Dan", "osu!mania 4K Dan"),
            new(13.15, 27.00, "5th Dan", "osu!mania 4K Dan"),
            new(14.90, 32.00, "6th Dan", "osu!mania 4K Dan"),
            new(14.97, 27.50, "7th Dan", "osu!mania 4K Dan"),
            new(15.95, 27.00, "8th Dan", "osu!mania 4K Dan"),
            new(16.72, 29.00, "9th Dan", "osu!mania 4K Dan"),
            new(17.51, 32.50, "10th Dan", "osu!mania 4K Dan"),

            // osu!mania Extra
            new(18.73, 34.00, "Luminal", "osu!mania Extra"),
            new(19.55, 36.00, "Tachyon", "osu!mania Extra"),

            // Dan ~ REFORM ~ 标准难度（同名已合并）
            new(8.84,  16.00, "~ 1st ~", "Dan ~ REFORM ~"),
            new(10.29, 18.00, "~ 2nd ~", "Dan ~ REFORM ~"),
            new(9.70,  21.70, "~ 3rd ~", "Dan ~ REFORM ~"),
            new(11.15, 21.00, "~ 4th ~", "Dan ~ REFORM ~"),
            new(12.43, 22.50, "~ 5th ~", "Dan ~ REFORM ~"),
            new(14.03, 23.00, "~ 6th ~", "Dan ~ REFORM ~"),
            new(15.74, 27.60, "~ 7th ~", "Dan ~ REFORM ~"),
            new(15.22, 27.80, "~ 8th ~", "Dan ~ REFORM ~"),
            new(17.02, 29.80, "~ 9th ~", "Dan ~ REFORM ~"),
            new(16.21, 28.00, "~ 10th ~", "Dan ~ REFORM ~"),

            // Dan ~ REFORM ~ EXTRA
            new(19.12, 29.10, "~ EXTRA-ALPHA ~", "Dan ~ REFORM ~ EXTRA"),
            new(19.37, 33.20, "~ EXTRA-BETA ~", "Dan ~ REFORM ~ EXTRA"),
            new(20.98, 34.80, "~ EXTRA-GAMMA ~", "Dan ~ REFORM ~ EXTRA"),
            new(24.09, 38.00, "~ EXTRA-DELTA ~", "Dan ~ REFORM ~ EXTRA"),
            new(24.70, 40.00, "~ EXTRA-EPSILON ~", "Dan ~ REFORM ~ EXTRA"),
            new(21.44, 45.00, "~ EXTRA-ZETA ~", "Dan ~ REFORM ~ EXTRA"),

            // Dan ~ REFORM ~ FINAL
            new(28.10, 44.00, "~ FINAL - ZETA ~", "Dan ~ REFORM ~ FINAL"),
            new(29.68, 48.00, "~ FINAL - ETA ~", "Dan ~ REFORM ~ FINAL"),

            // ultra.Dan ~ REFORM ~
            new(16.45, 28.00, "u.Dan1", "ultra.Dan REFORM"),
            new(19.87, 30.00, "u.Dan2", "ultra.Dan REFORM"),
            new(18.95, 34.00, "u.Dan3", "ultra.Dan REFORM"),

            // wds0 Dan Part.1
            new(16.83, 30.00, "wds0 1", "wds0 Dan Part.1"),
            new(18.48, 31.00, "wds0 2", "wds0 Dan Part.1"),
            new(19.53, 36.00, "wds0 3", "wds0 Dan Part.1"),
            new(18.42, 34.00, "wds0 4", "wds0 Dan Part.1"),
            new(16.79, 35.00, "wds0 5 (Flair)", "wds0 Dan Part.1"),
            new(20.56, 35.00, "wds0 NB", "wds0 Dan Part.1"),

            // wds0 Dan Part.2
            new(19.68, 38.00, "wds0 6", "wds0 Dan Part.2"),
            new(22.47, 37.00, "wds0 7", "wds0 Dan Part.2"),
            new(23.89, 42.00, "wds0 8", "wds0 Dan Part.2"),
            new(25.61, 39.00, "wds0 9", "wds0 Dan Part.2"),
            new(25.70, 45.00, "wds0 10", "wds0 Dan Part.2"),
            new(25.55, 43.00, "wds0 JB", "wds0 Dan Part.2"),
            new(29.53, 53.00, "wds0 Final", "wds0 Dan Part.2"),

            /* ================= Malody ================= */

            // Malody Regular（M.D.C.E Team v3，马拉松）
            new(6.65, 19.00, "Regular-0", "Malody Regular (M.D.C.E Team v3)"),
            new(7.44, 22.00, "Regular-1", "Malody Regular (M.D.C.E Team v3)"),
            new(10.10, 45.00, "Regular-2", "Malody Regular (M.D.C.E Team v3)"),
            new(5.67, 35.00, "Regular-3", "Malody Regular (M.D.C.E Team v3)"),
            new(3.80, 15.00, "Regular-4", "Malody Regular (M.D.C.E Team v3)"),
            new(4.11, 17.00, "Regular-5", "Malody Regular (M.D.C.E Team v3)"),
            new(4.52, 13.00, "Regular-6", "Malody Regular (M.D.C.E Team v3)"),
            new(6.29, 25.00, "Regular-7", "Malody Regular (M.D.C.E Team v3)"),
            new(5.11, 21.00, "Regular-8", "Malody Regular (M.D.C.E Team v3)"),
            new(5.07, 18.00, "Regular-9", "Malody Regular (M.D.C.E Team v3)"),
            new(4.65, 27.00, "Regular-10", "Malody Regular (M.D.C.E Team v3)"),

            // Malody 4K Dan（-Muses- & MoMoEven）
            new(6.70, 14.00, "Dan-1", "Malody 4K Dan"),
            new(8.81, 16.00, "Dan-2", "Malody 4K Dan"),
            new(9.72, 19.00, "Dan-3", "Malody 4K Dan"),
            new(10.99, 28.00, "Dan-4", "Malody 4K Dan"),
            new(12.40, 21.00, "Dan-5", "Malody 4K Dan"),
            new(13.31, 25.00, "Dan-6", "Malody 4K Dan"),
            new(15.94, 25.00, "Dan-7", "Malody 4K Dan"),
            new(15.36, 27.00, "Dan-8", "Malody 4K Dan"),
            new(17.67, 30.00, "Dan-9", "Malody 4K Dan"),
            new(15.95, 27.00, "Dan-10", "Malody 4K Dan"),

            // Malody 4K Extra（-Muses- & MoMoEven）
            new(14.59, 28.00, "Extra-0", "Malody 4K Extra"),
            new(14.83, 28.00, "Extra-1", "Malody 4K Extra"),
            new(15.45, 28.00, "Extra-2", "Malody 4K Extra"),
            new(14.47, 30.00, "Extra-3", "Malody 4K Extra"),
            new(17.40, 36.00, "Extra-4", "Malody 4K Extra"),
            new(19.07, 34.00, "Extra-5", "Malody 4K Extra"),
            new(19.36, 38.00, "Extra-6", "Malody 4K Extra"),
            new(22.20, 39.00, "Extra-Final", "Malody 4K Extra"),

            // Malody 4K Extra v2（teradora & Muses）
            new(5.94, 21.00, "Extra-1 v2", "Malody 4K Extra v2"),
            new(7.83, 26.00, "Extra-2 v2", "Malody 4K Extra v2"),
            new(4.32, 16.00, "Extra-3 v2", "Malody 4K Extra v2"),
            new(3.61, 12.00, "Extra-4 v2", "Malody 4K Extra v2"),
            new(4.44, 13.00, "Extra-5 v2", "Malody 4K Extra v2"),
            new(3.52, 10.00, "Extra-6 v2", "Malody 4K Extra v2"),
            new(4.38, 11.00, "Extra-7 v2", "Malody 4K Extra v2"),
            new(6.92, 28.00, "Extra-8 v2", "Malody 4K Extra v2"),
            new(5.75, 19.00, "Extra-9 v2", "Malody 4K Extra v2"),

            // Malody Extra v3（M.D.C.E Team）
            new(3.89, 17.00, "Extra-1 v3", "Malody Extra v3"),
            new(4.21, 18.00, "Extra-2 v3", "Malody Extra v3"),
            new(4.32, 16.00, "Extra-3 v3", "Malody Extra v3"),
            new(4.66, 15.00, "Extra-4 v3", "Malody Extra v3"),
            new(4.59, 17.00, "Extra-5 v3", "Malody Extra v3"),
            new(4.41, 16.00, "Extra-6 v3", "Malody Extra v3"),
            new(4.82, 16.00, "Extra-7 v3", "Malody Extra v3"),
            new(5.20, 26.00, "Extra-8 v3", "Malody Extra v3"),
            new(4.07, 14.00, "Extra-9 v3", "Malody Extra v3"),
            new(3.86, 15.00, "Extra-Final v3", "Malody Extra v3")
        };

        /// <summary>静态构造：为每个等级按同 Dan 组内序号填充 PassAcc（纯计算，无 IO）。</summary>
        static AiEngine()
        {
            var idx = new Dictionary<string, int>();
            foreach (var lv in Levels)
            {
                string dan = lv.Dan ?? "";
                if (!idx.TryGetValue(dan, out int i)) i = 0;
                lv.PassAcc = PassAccFor(lv, i);
                idx[dan] = i + 1;
            }
        }

        /// <summary>
        /// 段位过段 ACC 门槛（按用户指定的过段标准，全线统一、不分层级递减）：
        ///   Malody Regular / 4K Dan → 95%
        ///   Malody Extra（v1/v2/v3）→ 96%
        ///   osu!mania 全系（4K Dan / Extra / REFORM / ultra / wds0）→ 96%
        /// </summary>
        public static double PassAccFor(AiLevel lv, int indexInSet)
        {
            if (lv == null || string.IsNullOrEmpty(lv.Dan)) return 96.0;
            string dan = lv.Dan;
            if (dan.IndexOf("Malody", StringComparison.OrdinalIgnoreCase) >= 0)
                return dan.IndexOf("Extra", StringComparison.OrdinalIgnoreCase) >= 0 ? 96.0 : 95.0;
            return 96.0;   // osu!mania 系
        }

        const double Recover = 0.10;        // 低负载时每 1000ms 固定恢复 10%
        const double ExhaustCd = 12000;     // 力竭冷却时长
        const double OverloadFactor = 1.4;  // 内部超载线 = Hard × 1.4
        const double StaminaCap = 60;       // 体力低于 60% 时上限封顶到 Hard
        const double StreamEnterPerSec = 0.10;  // 高密度时每秒 10% 概率进入对拍模式
        const double StreamTensionDrop = 5.0;   // 对拍模式下紧张度每秒 -5%
        const double GodChancePerSec = 0.04;    // 紧张度≥80% 时每秒 4% 概率触发超神（每局一次）
        const double GodBoost = 5;              // 超神时所有 KPS 阈值 +5
        const double UrMin = 100;               // UR 最小值

        static readonly Random _rnd = new Random(20240818);   // 固定种子：训练/演示结果可复现

        /// <summary>等级紧张系数：等级越高（Hard 越大）系数越小 → 紧张升得越慢、UR 越低。</summary>
        public static double TensionScale(AiLevel lv)
            => Math.Max(0.2, Math.Min(1.0, 1.0 - (lv.Hard - 5) * 0.015));

        /// <summary>当前有效上限：体力 ≥60% 可到超载线；体力 &lt;60% 封顶在 Hard；超神时 Hard+5。</summary>
        static double CurrentLine(AiPlayer ai)
        {
            var lv = ai.Level;
            double hard = lv.Hard + (ai.GodMode ? GodBoost : 0);
            return ai.Stamina < StaminaCap ? hard : hard * OverloadFactor;
        }

        public static AiPlayer MakePlayer(AiLevel lv, string name)
        {
            var ai = new AiPlayer { Level = lv, Name = name };
            Reset(ai);
            return ai;
        }

        public static void Reset(AiPlayer ai)
        {
            ai.NoteIdx = 0;
            ai.HitTimes.Clear();
            ai.Stamina = 100;
            ai.Exhausted = false;
            ai.ExhaustUntil = 0;
            ai.OverLimitTime = 0;
            ai.LastTick = double.NaN;
            ai.Score = 0; ai.Combo = 0; ai.MaxCombo = 0;
            ai.Judged = 0; ai.HitCount = 0;
            ai.Acc = 100; ai.WeightSum = 0;
            ai.KpsNow = 0;
            ai.Hits.Clear();
            ai.Tension = 0;
            ai.LastTickT = double.NaN;
            ai.Devs.Clear();
            ai.Ur = UrMin;
            ai.StreamMode = false;
            ai.GodMode = false;
            ai.GodUsed = false;
            ai.Plan.Clear();
            foreach (var l in JudgeSettings.Levels) ai.Hits[l.Name] = 0;
            ai.Hits["MISS"] = 0;
        }

        public static void StaminaTick(AiPlayer ai, int cnt, double t)
        {
            var lv = ai.Level;
            if (double.IsNaN(ai.LastTick)) ai.LastTick = t;
            double dt = Math.Max(0, Math.Min(1000, t - ai.LastTick));
            ai.LastTick = t;

            if (ai.Exhausted)
            {
                if (t >= ai.ExhaustUntil) { ai.Exhausted = false; ai.ExhaustUntil = 0; }
                else { ai.OverLimitTime = 0; return; }
            }

            double boost = ai.GodMode ? GodBoost : 0;
            double avg = lv.Avg + boost;
            double hard = lv.Hard + boost;
            double line = CurrentLine(ai);

            double drain;
            if (cnt < avg) drain = -dt * Recover;
            else if (cnt < hard) drain = dt * 0.05;
            else if (cnt < line) drain = dt * (ai.StreamMode ? 0.10 : 0.20);   // 对拍消耗减半
            else drain = dt * (ai.StreamMode ? 0.40 : 0.80);                     // 对拍超载消耗减半

            ai.Stamina = Math.Max(0, Math.Min(100, ai.Stamina - drain));

            if (cnt >= line)
            {
                ai.OverLimitTime += dt;
                if (ai.OverLimitTime >= 2000)
                { ai.Exhausted = true; ai.ExhaustUntil = t + ExhaustCd; ai.Stamina = 0; ai.OverLimitTime = 0; }
            }
            else ai.OverLimitTime = 0;
        }

        /// <summary>
        /// 紧张度：正常随时间增长（等级越高越慢）；高密度时可能进入对拍模式，
        /// 进入后紧张度每秒 -5% 且打得更稳。超神每局只能触发一次，触发后本局不解除。
        /// </summary>
        public static void TensionTick(AiPlayer ai, int cnt, double t)
        {
            if (double.IsNaN(ai.LastTickT)) ai.LastTickT = t;
            double dt = Math.Max(0, Math.Min(1000, t - ai.LastTickT));
            ai.LastTickT = t;
            var lv = ai.Level;
            double boost = ai.GodMode ? GodBoost : 0;
            double scale = TensionScale(lv);

            if (ai.StreamMode)
            {
                // 对拍模式：紧张度每秒 -5%
                ai.Tension = Math.Max(0, ai.Tension - StreamTensionDrop * dt / 1000.0);
            }
            else
            {
                // 高密度时可能进入对拍模式
                if (cnt >= lv.Hard + boost && !ai.Exhausted && _rnd.NextDouble() < StreamEnterPerSec * dt / 1000.0)
                    ai.StreamMode = true;

                double rate = 0.4 * scale;
                if (cnt >= lv.Hard + boost) rate = 1.3 * scale;
                else if (cnt >= lv.Avg + boost) rate = 0.8 * scale;
                ai.Tension = Math.Max(0, Math.Min(100, ai.Tension + rate * dt / 1000.0));
            }

            // 超神模式：每局只能触发一次，触发后本局不解除
            if (!ai.GodUsed && !ai.GodMode && ai.Tension >= 80 && !ai.Exhausted &&
                _rnd.NextDouble() < GodChancePerSec * dt / 1000.0)
            {
                ai.GodMode = true;
                ai.GodUsed = true;
            }
        }

        public static double Chance(AiPlayer ai, int cnt)
        {
            var lv = ai.Level;
            double boost = ai.GodMode ? GodBoost : 0;
            double avg = lv.Avg + boost;
            double hard = lv.Hard + boost;
            double line = CurrentLine(ai);
            double sBoost = ai.StreamMode ? 0.06 : 0;   // 对拍模式命中更稳

            double p;
            if (ai.Exhausted) p = cnt >= hard ? 0 : (cnt < avg ? 1 : 0.85);
            else if (cnt < avg) p = 1;
            else if (cnt < hard) p = Math.Min(1, 1 - 0.15 * ((cnt - avg) / Math.Max(1e-9, hard - avg)) + sBoost);
            else if (cnt < line) p = Math.Min(0.97, 0.85 - 0.34 * ((cnt - hard) / Math.Max(1e-9, line - hard)) + sBoost);
            else p = 0.51;
            // 训练校准（确定性）：正偏差均匀降低任何密度段的命中率（模拟真实玩家的随机失误），
            // 负偏差提高高密度带内命中率（更稳）。由调用方用随机数与返回值比较，保证模拟可复现。
            return lv.TunedMissBias > 0 ? Math.Max(0, p - lv.TunedMissBias)
                 : lv.TunedMissBias < 0 ? Math.Min(1, p - lv.TunedMissBias)
                 : p;
        }

        /// <summary>基准偏差窗口（等级越高窗口越严）。</summary>
        public static double MaxDev(AiLevel lv) => Math.Max(4, Math.Round(50 - lv.Hard * 0.9));

        /// <summary>实际偏差窗口：紧张放大，高等级放大更小；对拍模式更稳；训练缩放 TunedScale 直接作用。</summary>
        public static double MaxDevFor(AiPlayer ai)
        {
            double m = MaxDev(ai.Level) * (1 + ai.Tension * 0.008 * TensionScale(ai.Level));
            if (ai.StreamMode) m *= 0.8;
            m *= ai.Level.TunedScale;   // 训练调参：偏差缩放
            return Math.Max(4, m);
        }

        /// <summary>标准差（样本）。</summary>
        public static double StdDev(IReadOnlyList<double> xs)
        {
            if (xs == null || xs.Count < 2) return 0;
            double sum = 0; foreach (var x in xs) sum += x;
            double mean = sum / xs.Count;
            double ss = 0; foreach (var x in xs) ss += (x - mean) * (x - mean);
            return Math.Sqrt(ss / (xs.Count - 1));
        }

        /// <summary>更新 UR：纯偏差标准差 ×10（不锁最小值、不乘等级系数，跟随真实击打精度）。</summary>
        public static void UpdateUr(AiPlayer ai)
        {
            ai.Ur = StdDev(ai.Devs) * 10;
        }

        public static string GradeNameForDev(double adev)
        {
            for (int i = 0; i < JudgeSettings.Levels.Count; i++)
                if (adev <= JudgeSettings.Levels[i].Window) return JudgeSettings.Levels[i].Name;
            return "MISS";
        }

        // ===== 训练参数持久化（Player/ai_train.json） =====

        static string TrainPath => Path.Combine(AppConfig.DefaultPlayerFolder, "ai_train.json");

        /// <summary>读取训练参数并应用到 Levels（MainForm 启动 / 设置窗打开时调用；IO 失败静默忽略）。</summary>
        public static void LoadTrained()
        {
            try
            {
                if (!File.Exists(TrainPath)) return;
                var dict = JsonSerializer.Deserialize<Dictionary<string, AiTrainEntry>>(File.ReadAllText(TrainPath));
                if (dict == null) return;
                foreach (var lv in Levels)
                {
                    if (lv == null || lv.Name == null) continue;
                    if (dict.TryGetValue(lv.Name, out var e) && e != null)
                    {
                        lv.TunedScale = e.Scale;
                        lv.TunedMissBias = e.MissBias;
                        lv.TrainedHpEnd = e.HpEnd;
                    }
                }
            }
            catch { }
        }

        /// <summary>把已训练的等级参数合并写回 Player/ai_train.json（保留其他等级已有条目）。</summary>
        public static void SaveTrained()
        {
            try
            {
                var dict = new Dictionary<string, AiTrainEntry>();
                try
                {
                    if (File.Exists(TrainPath))
                    {
                        var old = JsonSerializer.Deserialize<Dictionary<string, AiTrainEntry>>(File.ReadAllText(TrainPath));
                        if (old != null)
                            foreach (var kv in old)
                                if (kv.Key != null && kv.Value != null) dict[kv.Key] = kv.Value;
                    }
                }
                catch { }
                foreach (var lv in Levels)
                {
                    if (lv == null || lv.Name == null) continue;
                    // 仅保存已训练过的等级（TrainedHpEnd>=0 或旋钮偏离默认值）
                    if (lv.TrainedHpEnd >= 0 || Math.Abs(lv.TunedScale - 1.0) > 1e-9 || Math.Abs(lv.TunedMissBias) > 1e-9)
                        dict[lv.Name] = new AiTrainEntry { Scale = lv.TunedScale, MissBias = lv.TunedMissBias, HpEnd = lv.TrainedHpEnd };
                }
                Directory.CreateDirectory(AppConfig.DefaultPlayerFolder);
                File.WriteAllText(TrainPath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
