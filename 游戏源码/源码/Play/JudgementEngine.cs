using System;
using System.Collections.Generic;

namespace ChartPlayer
{
    /// <summary>
    /// 判定引擎（引擎层第 2 步）：与渲染/UI 无关的判定、计分、ACC、连击、HP 纯逻辑。
    /// 离线训练（AiTrainer）与实战（GamePanel 后续迁移）共用同一实现，保证两者一致。
    /// 判定窗口/权重/HP 模型全部读取 JudgeSettings 全局预设（由 ApplyForChart 按谱面设置）。
    /// </summary>
    public class JudgementEngine
    {
        public double Score { get; private set; }
        public double Acc { get; private set; } = 100;
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public double Hp { get; private set; }
        public Dictionary<string, int> Hits { get; } = new Dictionary<string, int>();
        public int TotalNotes { get; set; }

        /// <summary>MISS 是否计入 ACC 分母（权重 0）：玩家实战 true（osu/Malody 官方口径）；AI 训练 false（MISS 只影响 HP/连击）。</summary>
        public bool MissCountsInAcc { get; set; } = true;

        // 断连击判定名缓存（IIDX BAD 等；判定表可能被运行时替换，用版本号失效）
        static HashSet<string> _breakComboCache;
        static int _breakComboVer = -1;
        static bool BreaksCombo(string g)
        {
            int ver = JudgeSettings.Levels.Count * 31 + JudgeSettings.PresetKey.GetHashCode();
            if (ver != _breakComboVer)
            {
                _breakComboVer = ver;
                _breakComboCache = new HashSet<string>();
                foreach (var l in JudgeSettings.Levels) if (l.BreaksCombo) _breakComboCache.Add(l.Name);
            }
            return _breakComboCache.Contains(g);
        }

        double _weightSum;
        double _accHits;   // ACC 分母（含音符类型权重）

        public JudgementEngine(bool missCountsInAcc = true, int totalNotes = 0)
        {
            MissCountsInAcc = missCountsInAcc;
            TotalNotes = totalNotes;
            Reset();
        }

        public void Reset()
        {
            Score = 0; Acc = 100; Combo = 0; MaxCombo = 0;
            Hp = JudgeSettings.HpMax;
            Hits.Clear();
            _weightSum = 0; _accHits = 0;
            _arcBonus = 0; _arcComboBonus = 0;
        }

        /// <summary>判定一个音符（judgeTime = 击打时间；dev = judgeTime - 音符时间）。返回判定等级名（MISS=漏/坏）。
        /// weightMult = 音符类型权重（默认 1）。</summary>
        public string Judge(Note n, double judgeTime, double weightMult = 1)
        {
            double dev = judgeTime - n.Time;
            string g = AiEngine.GradeNameForDev(Math.Abs(dev));
            if (g == "MISS")
            {
                Combo = 0;
                Hp += JudgeSettings.HpFor("MISS");
                if (MissCountsInAcc) _accHits += weightMult;   // 官方口径：MISS 计入 ACC 分母（权重 0）
            }
            else
            {
                Combo++;
                MaxCombo = Math.Max(MaxCombo, Combo);
                Score += JudgeSettings.LevelScore(g, Combo);
                Hp += JudgeSettings.HpFor(g);
                _weightSum += JudgeSettings.WeightFor(g) * weightMult;
                _accHits += weightMult;
                if (BreaksCombo(g)) Combo = 0;   // IIDX BAD 等：断连击
            }
            CountHit(g);
            RecomputeAcc();
            ClampHp();
            return g;
        }

        /// <summary>Phigros DRAG：只计连击，不计准确率/分数/HP（官方口径：Drag 不计判定）。返回判定名用于显示。</summary>
        public string JudgeDrag(Note n, double judgeTime)
        {
            double dev = judgeTime - n.Time;
            string g = AiEngine.GradeNameForDev(Math.Abs(dev));
            if (g != "MISS")
            {
                Combo++;
                MaxCombo = Math.Max(MaxCombo, Combo);
            }
            CountHit(g);
            return g;
        }

        /// <summary>直接记一次 MISS（漏判/断触）。</summary>
        public void Miss()
        {
            Combo = 0;
            Hp += JudgeSettings.HpFor("MISS");
            CountHit("MISS");
            if (MissCountsInAcc) _accHits++;   // 官方口径：MISS 计入 ACC 分母（权重 0）
            RecomputeAcc();
            ClampHp();
        }

        /// <summary>Arcaea 10M 计分（D10，规格：Arcaea实机游玩界面规格文档 §计分）：
        /// 满分 10,000,000 = 基础分（每 note 均分，合计 1,000,000）+ 连击加成（合计最高 1,000,000，约 100 连满额）
        /// + Pure 附加分（每个大 Pure +1，合计最高 1,000,000）。返回判定名。</summary>
        public string JudgeArcaea(Note n, string g, bool bigPure)
        {
            if (g == "MISS")
            {
                Combo = 0;
                Hp += JudgeSettings.HpFor("MISS");
                if (MissCountsInAcc) _accHits += 1;
                CountHit(g);
                RecomputeAcc();
                ClampHp();
                return g;
            }
            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);
            int total = Math.Max(1, TotalNotes);
            // 基础分：每 note 均分 1M；连击加成：每连击 +10000（合计封顶 1M，约 100 连满额）；附加分：大 Pure +1（上限 1M）
            Score += 1000000.0 / total;                    // 基础分（每 note 均分，合计 1M）
            if (_arcComboBonus < 1000000)
            {
                double add = Math.Min(10000, 1000000 - _arcComboBonus);
                Score += add;                              // 连击加成（每连击 +1M/100=10000，封顶 1M）
                _arcComboBonus += add;
            }
            if (bigPure && _arcBonus < 1000000)
            {
                double add2 = Math.Min(1, 1000000 - _arcBonus);
                Score += add2;                            // 大 Pure 附加（每颗 +1，上限 1M）
                _arcBonus += add2;
            }
            _weightSum += JudgeSettings.WeightFor(g);
            _accHits += 1;
            Hp += JudgeSettings.HpFor(g);
            CountHit(g);
            RecomputeAcc();
            ClampHp();
            return g;
        }

        double _arcBonus = 0;
        double _arcComboBonus = 0;

        /// <summary>以指定判定等级记账（转盘等非时间偏差判定路径）。weightMult 同 Judge。</summary>
        public void JudgeWithGrade(Note n, string g, double weightMult = 1)
        {
            if (g == "MISS")
            {
                Combo = 0;
                Hp += JudgeSettings.HpFor("MISS");
                if (MissCountsInAcc) _accHits += weightMult;
            }
            else
            {
                Combo++;
                MaxCombo = Math.Max(MaxCombo, Combo);
                Score += JudgeSettings.LevelScore(g, Combo);
                Hp += JudgeSettings.HpFor(g);
                _weightSum += JudgeSettings.WeightFor(g) * weightMult;
                _accHits += weightMult;
            }
            CountHit(g);
            RecomputeAcc();
            ClampHp();
        }

        /// <summary>长条完整按完（+50 分）。</summary>
        public void HoldComplete()
        {
            Score += 50;
        }

        /// <summary>osu!std 滑条 tick 命中：+30 基础分并计入连击（引擎 ScoreBoard.ApplyTick 同语义；
        /// 不占 ACC 权重——ACC 只由头判与尾判的判定档决定）。C3 引擎闭环轻量接入。</summary>
        public void ApplyTick() => ApplyTick(30);

        /// <summary>tick/连打计分通用入口：按分值计分并计入连击（taiko drumroll +300 等，D21）。</summary>
        public void ApplyTick(double scorePerTick)
        {
            Score += scorePerTick;
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
            CountHit("tick");
        }

        /// <summary>osu!catch 果汁串 tick（D18）：fruit +30 计连击，droplet +10 不计连击（实机 ScoreV2 口径）。</summary>
        public void ApplyCatchTick(double score, bool countCombo)
        {
            Score += score;
            if (countCombo)
            {
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
            }
            CountHit(countCombo ? "fruit" : "droplet");
        }

        /// <summary>滑条完整度折减尾分：完整度 &lt;1 时调用方在 HoldComplete 前按 (1-Completeness) 折算扣分。</summary>
        public void AdjustScore(double delta)
        {
            Score += delta;
            if (Score < 0) Score = 0;
        }

        /// <summary>长条提前松开（断触 MISS，HP 按 hold 尾惩罚）。weightMult 同 Judge。</summary>
        public void HoldBreak(double weightMult = 1)
        {
            Combo = 0;
            Hp += JudgeSettings.HpForHoldMiss();
            CountHit("MISS");
            if (MissCountsInAcc) _accHits += weightMult;
            RecomputeAcc();
            ClampHp();
        }

        /// <summary>Malody 长条提前松开：仅断连，不计 MISS/不扣 ACC（D20——Malody 实机无尾判，区分 osu lazer 组合误差）。</summary>
        public void BreakCombo()
        {
            Combo = 0;
        }

        /// <summary>时间推进：HP 自然衰减 + 按住回血（段位模式）。</summary>
        public void Drain(double dtSeconds, int heldCount)
        {
            if (dtSeconds <= 0 && heldCount <= 0) return;
            Hp -= JudgeSettings.HpDrainPerSec * dtSeconds;
            if (heldCount > 0) Hp += JudgeSettings.HpHoldTickPerSec * heldCount * dtSeconds;
            ClampHp();
        }

        /// <summary>外部 HP 增减（训练器的 hold 一次性折算等）。</summary>
        public void AddHp(double delta)
        {
            Hp += delta;
            ClampHp();
        }

        /// <summary>HP 是否已归零（段位提前失败）。</summary>
        public bool Dead => Hp <= 0;

        void CountHit(string g)
        {
            Hits.TryGetValue(g, out int c);
            Hits[g] = c + 1;
        }

        void RecomputeAcc()
        {
            if (_accHits > 0) Acc = _weightSum / _accHits * 100;
            else Acc = 100;
        }

        void ClampHp()
        {
            Hp = Math.Max(0, Math.Min(JudgeSettings.HpMax, Hp));
        }
    }
}
