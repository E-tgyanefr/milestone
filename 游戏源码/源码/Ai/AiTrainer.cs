using System;
using System.Collections.Generic;
using System.Linq;

namespace ChartPlayer
{
    /// <summary>单个等级的训练参数（持久化到 Player/ai_train.json，字典键 = level.Name）。</summary>
    public class AiTrainEntry
    {
        public double Scale { get; set; }
        public double MissBias { get; set; }
        public double HpEnd { get; set; } = -1;
    }

    /// <summary>离线训练结果。</summary>
    public class TrainResult
    {
        public bool Converged;
        public double Acc;
        public double HpEnd;
        public double PassAcc;
        public bool Passed;
        public double Scale;
        public double MissBias;
        public int Rounds;      // 实际迭代轮数
        public string Reason = "";
    }

    /// <summary>
    /// AI 离线训练：调整 TunedScale（偏差缩放）与 TunedMissBias（失误概率偏移）两个旋钮，
    /// 让某个等级在这张段位谱面上"刚好过段"（压线过：HP 落在 [3,14] 且 ACC 贴近 PassAcc）。
    /// 不依赖 UI / 音频；只用 Chart.Notes 做简化离线模拟。
    /// </summary>
    public static class AiTrainer
    {
        // 固定随机种子数组：每轮 3 次模拟取平均，保证可复现
        static readonly int[] Seeds = { 12345, 67890, 24680 };
        const int MaxRounds = 72;   // 自适应步长逐轮减半，更多轮次才能压进窄目标带
        const int SimsPerRound = 3;

        // 训练目标带：结束 HP ∈ [1,25]，预测 ACC ∈ [PassAcc, PassAcc+1.4]（压线过：必须过线且贴线）
        // 注：osu 段位 HP 随整数 MISS 数阶跃（一次 MISS ≈ -6.75），过窄的带会被直接跳过，故放宽到 [1,25]
        const double HpLow = 1.0, HpHigh = 25.0;
        const double AccLoPad = 0.0, AccHiPad = 1.4;

        // 旋钮范围（Scale 下限 0.2：osu!mania OD8 的 PERFECT 窗口仅 16ms，需要把 AI 偏差压到
        // 几毫秒级才能贴住 96 过段线；上限 12：顶级段位需要大缩放模拟出压线判定）
        const double ScaleMin = 0.2, ScaleMax = 12.0;
        const double BiasMin = -0.35, BiasMax = 0.5;

        static volatile bool _cancel;

        /// <summary>请求取消当前训练（取消按钮调用）。</summary>
        public static void RequestCancel() => _cancel = true;

        public static TrainResult Train(Chart chart, AiLevel level, IProgress<double> progress = null)
        {
            _cancel = false;
            var res = new TrainResult { Scale = 1.0, MissBias = 0.0 };
            if (chart == null || level == null || chart.Notes == null || chart.Notes.Count == 0)
            {
                res.Reason = "谱面或等级无效（无音符）";
                return res;
            }

            // 本地按时间排序的副本（不改动原谱面）
            var notes = chart.Notes.OrderBy(n => n.Time).ToList();
            res.PassAcc = level.PassAcc >= 1 ? level.PassAcc : 95.0;

            double scale = Clamp(level.TunedScale, ScaleMin, ScaleMax);
            double bias = Clamp(level.TunedMissBias, BiasMin, BiasMax);

            // Malody 段位无血量（仅 ACC 判定）：训练只校准 ACC，失误率旋钮保持不动
            bool hpMatters = JudgeSettings.DanHpEnabled;
            double acc = 0, hp = 0;
            bool converged = false;
            // 自适应步长：方向翻转时减半（防震荡），保证在噪声估计下仍能逼近目标带
            double scaleStep = 0.3, biasStep = 0.03;
            int lastScaleDir = 0, lastBiasDir = 0;

            for (int round = 0; round < MaxRounds; round++)
            {
                try { progress?.Report((double)round / MaxRounds); } catch { }
                if (_cancel) { res.Reason = "已取消"; res.Acc = acc; res.HpEnd = hp; res.Scale = scale; res.MissBias = bias; return res; }

                (acc, hp) = Evaluate(notes, level, scale, bias);
                res.Acc = acc; res.HpEnd = hp; res.Scale = scale; res.MissBias = bias;
                res.Rounds = round + 1;

                // 已命中目标带 → 收敛（无血量段位只看 ACC）
                if (InAccBand(acc, res.PassAcc) && (!hpMatters || (hp >= HpLow && hp <= HpHigh))) { converged = true; break; }

                // 旋钮-目标映射（每轮两个旋钮同时逼近，方向翻转步长减半）：
                //   ACC 由偏差缩放主控：scale↑ → 判定变差 → ACC↓；scale↓ → ACC↑
                //   HP 由失误率主控：bias↑ → MISS↑ → HP↓；bias↓ → HP↑
                if (acc > res.PassAcc + AccHiPad)
                {
                    int dir = 1;
                    if (lastScaleDir != 0 && dir != lastScaleDir) scaleStep = Math.Max(0.01, scaleStep * 0.5);
                    lastScaleDir = dir;
                    scale = Clamp(scale + scaleStep, ScaleMin, ScaleMax);
                }
                else if (acc < res.PassAcc - AccLoPad)
                {
                    int dir = -1;
                    if (lastScaleDir != 0 && dir != lastScaleDir) scaleStep = Math.Max(0.01, scaleStep * 0.5);
                    lastScaleDir = dir;
                    scale = Clamp(scale - scaleStep, ScaleMin, ScaleMax);
                }
                else lastScaleDir = 0;

                if (hpMatters)
                {
                    if (hp > HpHigh)
                    {
                        int dir = 1;
                        if (lastBiasDir != 0 && dir != lastBiasDir) biasStep = Math.Max(0.005, biasStep * 0.5);
                        lastBiasDir = dir;
                        bias = Clamp(bias + biasStep, BiasMin, BiasMax);
                    }
                    else if (hp < HpLow)
                    {
                        int dir = -1;
                        if (lastBiasDir != 0 && dir != lastBiasDir) biasStep = Math.Max(0.005, biasStep * 0.5);
                        lastBiasDir = dir;
                        bias = Clamp(bias - biasStep, BiasMin, BiasMax);
                    }
                    else lastBiasDir = 0;
                }
            }

            if (converged)
            {
                // 收敛：写回等级参数并持久化
                level.TunedScale = scale;
                level.TunedMissBias = bias;
                level.TrainedHpEnd = hp;
                AiEngine.SaveTrained();
                res.Reason = "";
            }
            else
            {
                res.Reason = Diagnose(notes, level, res.PassAcc, acc, hp);
            }

            res.Converged = converged;
            res.Passed = (!JudgeSettings.DanHpEnabled || hp > 0) && acc >= res.PassAcc;
            try { progress?.Report(1.0); } catch { }
            return res;
        }

        /// <summary>每轮评估：3 次模拟取平均（固定种子，可复现）。</summary>
        static (double acc, double hp) Evaluate(List<Note> notes, AiLevel level, double scale, double bias)
        {
            double a = 0, h = 0;
            for (int s = 0; s < SimsPerRound; s++)
            {
                var r = Simulate(notes, level, scale, bias, new Random(Seeds[s]));
                a += r.acc; h += r.hp;
            }
            return (a / SimsPerRound, h / SimsPerRound);
        }

        /// <summary>
        /// 单次离线模拟：复用 AiEngine.MakePlayer 建 AI，逐音符驱动体力/紧张度与命中，
        /// 判定/计分/ACC/HP 全部走引擎层 JudgementEngine（与实战共用同一实现）。
        /// </summary>
        static (double acc, double hp) Simulate(List<Note> notes, AiLevel level, double scale, double bias, Random rnd)
        {
            // 用克隆等级跑模拟，避免污染共享 level 对象（仅在收敛后才写回）
            var simLevel = new AiLevel(level.Avg, level.Hard, level.Name, level.Dan)
            {
                PassAcc = level.PassAcc,
                TunedScale = scale,
                TunedMissBias = bias
            };
            var ai = AiEngine.MakePlayer(simLevel, level.Name);

            var eng = new JudgementEngine(missCountsInAcc: false, totalNotes: notes.Count);
            var hitWindow = new Queue<double>();     // 最近 1 秒窗口内的击打时间
            double curT = notes[0].Time;

            foreach (var n in notes)
            {
                // ① 时间推进：HP 自然衰减（真实秒数）
                double dt = (n.Time - curT) / 1000.0;
                if (dt > 0) eng.Drain(dt, 0);
                curT = n.Time;

                // ② 秒级密度：最近 1 秒窗口内已击打数（与 GamePanel 一致）
                while (hitWindow.Count > 0 && hitWindow.Peek() <= n.Time - 1000) hitWindow.Dequeue();
                int cnt = hitWindow.Count;

                AiEngine.StaminaTick(ai, cnt, n.Time);
                AiEngine.TensionTick(ai, cnt, n.Time);

                // ③ 命中判定（Chance 已含 TunedMissBias）
                if (rnd.NextDouble() < AiEngine.Chance(ai, cnt))
                {
                    double maxDev = AiEngine.MaxDevFor(ai);   // 已含 TunedScale
                    double dev = (rnd.NextDouble() * 2 - 1) * maxDev;
                    eng.Judge(n, n.Time + dev);
                    // hold 维持回血一次性折算（按住期间每秒 HpHoldTickPerSec，折半）
                    if (n.Type == "hold" && n.End > n.Time)
                        eng.AddHp((n.End - n.Time) / 1000.0 * JudgeSettings.HpHoldTickPerSec * 0.5);
                    hitWindow.Enqueue(n.Time + dev);
                    ai.Devs.Add(dev);
                    if (ai.Devs.Count > 120) ai.Devs.RemoveAt(0);
                    AiEngine.UpdateUr(ai);
                }
                else
                {
                    eng.Miss();   // 与 AiJudge 一致：MISS 不进入 ACC 分母，只掉 HP / 断连击
                }
            }

            // ④ 尾段衰减到谱面结束（含 hold 尾部）
            double endT = 0;
            foreach (var n in notes) endT = Math.Max(endT, n.End);
            if (endT > curT) eng.Drain((endT - curT) / 1000.0, 0);

            return (eng.Acc, eng.Hp);
        }

        /// <summary>无法收敛时的诊断（区分谱面过难 / 过易 / 一般未收敛）。</summary>
        static string Diagnose(List<Note> notes, AiLevel level, double passAcc, double acc, double hp)
        {
            // 最优调参（最小偏差、最少失误）仍过不了 → 谱面过难 / 密度远超等级
            var best = Evaluate(notes, level, ScaleMin, BiasMin);
            if (best.hp <= 0 || best.acc < passAcc - AccLoPad)
                return string.Format("谱面过难/密度远超等级：最优调参(scale=0.6,bias=-0.35)仍 ACC {0:0.00}% / HP {1:0.0}，无法过段", best.acc, best.hp);
            // 最差调参仍远高于目标带 → 谱面过易
            var worst = Evaluate(notes, level, ScaleMax, BiasMax);
            if (worst.acc > passAcc + AccHiPad && worst.hp > HpHigh)
                return string.Format("谱面过易：最差调参(scale=1.8,bias=+0.35)仍 ACC {0:0.00}% / HP {1:0.0}，无法压到目标带", worst.acc, worst.hp);
            return string.Format("{0} 轮内未收敛到目标带：ACC {1:0.00}%（目标 {2:0.0}） / HP {3:0.0}（目标 {4:0.0}~{5:0.0}）", MaxRounds, acc, passAcc, hp, HpLow, HpHigh);
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        static bool InAccBand(double acc, double passAcc) => acc >= passAcc - AccLoPad && acc <= passAcc + AccHiPad;
    }
}
