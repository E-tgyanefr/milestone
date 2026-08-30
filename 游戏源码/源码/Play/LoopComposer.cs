using System;
using System.Collections.Generic;
using System.Drawing;

namespace ChartPlayer
{
    /// <summary>
    /// 回环作曲 LoopComposer（创新玩法 M1 MVP）：编玩一体化——**没有预置谱面，谱面是玩家的演奏**。
    ///
    /// 三阶段循环（复用 maimai 环形几何 / JudgeSettings 三档窗口 / JudgementEngine 计分口径）：
    ///  ① 录（Record）：4 小节循环（默认 4/4，BPM=谱面或 120），播放头绕环滚动；
    ///     D F J K（环上 4 分区=4 轨音色）或鼠标点击环任意 16 分格（位置=时间）；
    ///     击打距最近 16 分网格 &gt;180ms 丢弃（防乱拍），≤180ms 吸附落谱。
    ///  ② 奏（Play）：录音回放，玩家实时演奏自己的谱；音符到达环顶判定点，
    ///     用 JudgeSettings 三档窗口（loopCompose 预设=Cytus 口径 ±75/150/220，权重 1/0.7/0.3）
    ///     命中=循环继续，漏=循环断（本轮记中断）。
    ///  ③ 扩（Expand）：连续 3 个满分循环（无 MISS）解锁下一 4 小节（BPM +2% 漂移），回到录。
    ///
    /// 计分 = 演奏 ACC×80% + 已录轨数×10% + 连续循环数×10%（0..100），GRADE=JudgeSettings.GradeFor。
    /// M1 内存运行不落盘（保存 .mil 属 M2）。--shotdemo 时 GameSettings.Autoplay=true → 脚本化「录+奏」
    /// （自动注入示例轨、自动命中——走与人类输入完全相同的 AddNote/Judge 路径）。
    /// </summary>
    public class LoopComposer
    {
        public const int Tracks = 4;
        public const double SnapGateMs = 180;   // 录制吸附门限

        readonly JudgementEngine _eng;
        readonly Action<string, double> _onJudge;   // (grade, nowMs) → GamePanel 爆字/闪光

        public double Bpm;
        public double Bars = 4;
        public double LoopMs => Bars * 4 * 60000.0 / Bpm;   // 4/4：小节数 × 4 拍 × 拍长
        public double BeatMs => 60000.0 / Bpm;
        public double GridMs => BeatMs / 4.0;               // 16 分格

        public int Phase;                    // 0=录 1=奏
        public double PhaseStart = -1;       // 录阶段起点（绝对 ms）
        public double LoopBase;              // 奏阶段循环底部时间
        public List<LcNote> Notes = new List<LcNote>();
        public int PerfectLoops;
        public int LoopsDone;
        public bool MissedThisLoop;
        public string Notice = "";
        public double NoticeUntil = -1e9;
        public string GradeDisplay = "";

        HashSet<int> _autoInjected = new HashSet<int>();    // autoplay 已注入的 16 分格下标

        public class LcNote
        {
            public double T;                 // 循环内相对时间（ms）
            public int Col;                  // 轨（0..3）
            public long Cycle = -1;          // 上次判定的循环序号（-1=本循环未判）
            public string Grade = "";
        }

        public LoopComposer(Chart chart, JudgementEngine eng, Action<string, double> onJudge)
        {
            _eng = eng;
            _onJudge = onJudge;
            Bpm = chart != null && chart.Bpm > 0 ? chart.Bpm : 120;
            GradeDisplay = "";
            // .mil mode=loopcompose：Notes=录音结果（Time=循环内相对 ms，Col=轨）→ 载入即进入奏（重放/再编辑）
            if (chart != null && chart.Notes != null && chart.Notes.Count > 0)
            {
                foreach (var n in chart.Notes)
                {
                    if (n == null) continue;
                    double t = n.Time % LoopMs;
                    int col = Math.Max(0, Math.Min(Tracks - 1, n.Col));
                    if (t < 0) t = 0;
                    Notes.Add(new LcNote { T = t, Col = col });
                }
                Notes.Sort((a, b) => a.T.CompareTo(b.T));
                if (Notes.Count > 0)
                {
                    Phase = 1;
                    PerfectLoops = 0; MissedThisLoop = false;
                    EmitNotice("🎛 奏——演奏（载入 .mil 录音）");
                }
            }
        }

        public double MissWindowMs => JudgeSettings.MissWindow > 0 ? JudgeSettings.MissWindow : 220;

        /// <summary>轨道颜色（D 青 / F 橙 / J 紫 / K 绿——极简霓虹）。</summary>
        public static Color TrackColor(int col) => col switch
        {
            0 => Color.FromArgb(255, 80, 200, 255),
            1 => Color.FromArgb(255, 255, 180, 80),
            2 => Color.FromArgb(255, 190, 120, 255),
            _ => Color.FromArgb(255, 120, 230, 160)
        };

        public int TracksUsed()
        {
            var set = new HashSet<int>();
            foreach (var n in Notes) set.Add(n.Col);
            return set.Count;
        }

        // ================= 输入（录） =================

        /// <summary>键盘 D F J K：col=0..3。吸附到 16 分格（≤180ms 写谱，否则丢弃）。</summary>
        public void KeyCol(int col, double now)
        {
            if (Phase != 0) return;
            double tIn = now - PhaseStart;
            if (tIn < 0 || tIn > LoopMs) return;
            AddNoteSnap(tIn, col);
        }

        /// <summary>鼠标点环：角度→循环内时间（位置=时间），4 象限→轨。走同一吸附路径。</summary>
        public void Mouse(double x, double y, double now, double cx, double cy, double R)
        {
            if (Phase != 0) return;
            double dx = x - cx, dy = y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > R * 1.08) return;
            double ang = Math.Atan2(dx, -dy);          // 0=正上，顺时针
            if (ang < 0) ang += Math.PI * 2;
            double tIn = ang / (Math.PI * 2) * LoopMs;
            int col = (int)(ang / (Math.PI / 2)) % Tracks;
            AddNoteSnap(tIn, col);
        }

        void AddNoteSnap(double tIn, int col)
        {
            double snap = Math.Round(tIn / GridMs) * GridMs;
            if (snap > LoopMs - 1) snap = LoopMs - 1;   // 末格回绕
            if (Math.Abs(tIn - snap) > SnapGateMs) return;   // 乱拍丢弃
            foreach (var n in Notes)                      // 同轨同格去重
                if (n.Col == col && Math.Abs(n.T - snap) < GridMs * 0.5) return;
            Notes.Add(new LcNote { T = snap, Col = col });
            Notes.Sort((a, b) => a.T.CompareTo(b.T));
            SoundFx.Hit("PERFECT");   // 写谱反馈音（SoundFx.Enabled 由全局设置控制）
        }

        // ================= 帧推进 =================

        public void Tick(double now)
        {
            if (PhaseStart < 0) { PhaseStart = now; LoopBase = now; return; }
            bool auto = GameSettings.Autoplay;
            if (Phase == 0)
            {
                double tIn = now - PhaseStart;
                if (auto) AutoInject(now, tIn);
                if (tIn >= LoopMs)
                {
                    if (Notes.Count == 0)
                    {
                        if (auto) { InjectSample(now); }
                        else { PhaseStart += LoopMs; EmitNotice("空录一轮——按 D F J K 或点环写谱"); }
                    }
                    else
                    {
                        Phase = 1;
                        LoopBase = PhaseStart + LoopMs;
                        PerfectLoops = 0; MissedThisLoop = false;
                        EmitNotice("🎛 奏——演奏你自己的谱！");
                    }
                }
            }
            else
            {
                long k = (long)Math.Floor((now - LoopBase) / LoopMs);
                double cycleStart = LoopBase + k * LoopMs;
                // 判定：本轮未判、且已过命中窗的音符（排序后首个未到即 break）
                foreach (var n in Notes)
                {
                    if (n.Cycle == k) continue;
                    double absT = cycleStart + n.T;
                    if (absT > now + 2) break;
                    if (auto)
                    {
                        if (now >= absT - 1) JudgeNote(n, absT, k);          // 自动命中（dev≈0 → PERFECT）
                    }
                    else
                    {
                        if (now > absT + MissWindowMs)
                        {
                            JudgeNote(n, absT + MissWindowMs + 1, k);       // 漏判
                            if (!MissedThisLoop && n.Grade == "MISS")
                            {
                                MissedThisLoop = true;
                                EmitNotice("💥 漏了！循环中断——本循环不计满分");
                            }
                        }
                    }
                }
                // 循环边界结算：上一循环 k-1 在本循环起步窗内结算（此时上一循环所有音符已终判；
                // 漏判最晚在 absT+MissWindow 发生，而 absT_max=prevEnd-GridMs → 判结早于 prevEnd+MissWindow）
                long prevIdx = k - 1;
                if (prevIdx >= 0)
                {
                    double prevEnd = LoopBase + (prevIdx + 1) * LoopMs;
                    if (now >= prevEnd && now < prevEnd + MissWindowMs && _settledCycle != prevIdx)
                    {
                        _settledCycle = prevIdx;
                        LoopsDone++;
                        if (!MissedThisLoop)
                        {
                            PerfectLoops++;
                            if (PerfectLoops >= 3)
                            {
                                Bars = Math.Min(16, Bars + 4);
                                Bpm = Math.Min(200, Bpm * 1.02);
                                Phase = 0;
                                PhaseStart = prevEnd;
                                EmitNotice("🎉 解锁下一 " + Bars + " 小节 · BPM " + Bpm.ToString("0.#") + "！回到录");
                            }
                            else
                            {
                                EmitNotice("✅ 满分循环 " + PerfectLoops + "/3 · 音符 " + Notes.Count + " 个");
                            }
                        }
                        else EmitNotice("↻ 本循环中断 · 满分还需 " + (3 - PerfectLoops) + " 连");
                        MissedThisLoop = false;
                    }
                }
            }
            UpdateScoreDisplay();
        }

        long _settledCycle = long.MinValue;

        /// <summary>奏判定：dev=judgeTime-音符绝对时间 → JudgeSettings 三档（loopCompose 预设）。
        /// Note 同时写入绝对时间与循环内相对时间（判引擎只看 Time/Col；渲染用 T）。</summary>
        void JudgeNote(LcNote n, double judgeTime, long cycle)
        {
            n.Cycle = cycle;
            double cycleStart = LoopBase + cycle * LoopMs;
            var note = new Note { Time = cycleStart + n.T, Col = n.Col, Type = "tap" };
            string g = _eng.Judge(note, judgeTime);
            n.Grade = g;
            _onJudge?.Invoke(g, judgeTime);
        }

        // ================= Autoplay 脚本化「录+奏」 =================

        void AutoInject(double now, double tIn)
        {
            int g0 = (int)Math.Floor(tIn / GridMs);
            if (g0 < 0) return;
            for (int g = Math.Max(0, g0 - 1); g <= g0; g++)
            {
                if (_autoInjected.Contains(g)) continue;
                double tg = g * GridMs;
                if (tg > tIn + 2 || tg >= LoopMs) continue;
                int col = g % 4 == 0 ? (g / 4) % Tracks : (g % 8 == 6 ? ((g / 4) + 1) % Tracks : -1);
                _autoInjected.Add(g);
                if (col < 0) continue;
                bool dup = false;
                foreach (var n in Notes) if (n.Col == col && Math.Abs(n.T - tg) < GridMs * 0.5) { dup = true; break; }
                if (!dup) Notes.Add(new LcNote { T = tg, Col = col });
            }
            Notes.Sort((a, b) => a.T.CompareTo(b.T));
        }

        /// <summary>自动化空录兜底：直接注入示例轨（正拍循环 4 轨 + 16 分反拍），立即进入奏。</summary>
        void InjectSample(double now)
        {
            _autoInjected.Clear();
            Notes.Clear();
            for (int g = 0; g < (int)(LoopMs / GridMs); g++)
            {
                int col = g % 4 == 0 ? (g / 4) % Tracks : (g % 8 == 6 ? ((g / 4) + 1) % Tracks : -1);
                _autoInjected.Add(g);
                if (col >= 0) Notes.Add(new LcNote { T = g * GridMs, Col = col });
            }
            Notes.Sort((a, b) => a.T.CompareTo(b.T));
            Phase = 1;
            LoopBase = PhaseStart + LoopMs;
            PerfectLoops = 0; MissedThisLoop = false;
            EmitNotice("🎛 奏（自动示例轨）");
        }

        // ================= 显示 =================

        void UpdateScoreDisplay()
        {
            double acc = _eng.Acc;
            double score = acc * 0.8
                + Math.Min(1.0, TracksUsed() / (double)Tracks) * 10
                + Math.Min(1.0, PerfectLoops / 3.0) * 10;
            GradeDisplay = "回环分 " + (acc > 0 ? score.ToString("0.0") : "--") + " · " + JudgeSettings.GradeFor(Math.Max(0, Math.Min(100, score)));
        }

        void EmitNotice(string s) { Notice = s; NoticeUntil = Environment.TickCount64 + 2600; }

        public bool NoticeActive => Environment.TickCount64 < NoticeUntil;

        // ================= 渲染（环=时间轮，格线 + 播放头 + 轨色块） =================

        public void Draw(D2DRenderer d2, int W, int H, double now, double cx, double cy, double R)
        {
            if (PhaseStart < 0) PhaseStart = now;
            double tIn = Phase == 0
                ? Math.Max(0, now - PhaseStart)
                : ((now - LoopBase) % LoopMs + LoopMs) % LoopMs;
            long k = (long)Math.Floor((now - LoopBase) / LoopMs);
            double cycleStart = LoopBase + k * LoopMs;

            // 环底
            d2.DrawEllipse((float)cx, (float)cy, (float)R, (float)R, Color.FromArgb(90, 110, 140, 200), 2.5f);
            d2.DrawEllipse((float)cx, (float)cy, (float)(R * 0.62), (float)(R * 0.62), Color.FromArgb(50, 130, 160, 220), 1.2f);

            // 拍线（粗）+ 16 分刻度（细）
            int beatCount = (int)(Bars * 4);
            for (int b = 0; b < beatCount; b++)
            {
                double ab = b / (double)beatCount * Math.PI * 2 - Math.PI / 2;
                d2.DrawLine((float)(cx + Math.Cos(ab) * R * 0.62), (float)(cy + Math.Sin(ab) * R * 0.62),
                            (float)(cx + Math.Cos(ab) * R), (float)(cy + Math.Sin(ab) * R),
                            Color.FromArgb(70, 150, 180, 230), 2.2f);
                for (int s = 1; s < 4; s++)
                {
                    double a2 = (b + s / 4.0) / beatCount * Math.PI * 2 - Math.PI / 2;
                    d2.DrawLine((float)(cx + Math.Cos(a2) * R * 0.92), (float)(cy + Math.Sin(a2) * R * 0.92),
                                (float)(cx + Math.Cos(a2) * R), (float)(cy + Math.Sin(a2) * R),
                                Color.FromArgb(45, 120, 150, 200), 1f);
                }
            }

            // 4 轨分区标签（D F J K）
            for (int t = 0; t < Tracks; t++)
            {
                double mid = (t + 0.5) / Tracks * Math.PI * 2 - Math.PI / 2;
                float lx = (float)(cx + Math.Cos(mid) * R * 0.50), ly = (float)(cy + Math.Sin(mid) * R * 0.50);
                var c = TrackColor(t);
                d2.Text("DFJK"[Math.Min(3, t)].ToString(), lx - 14, ly - 9, 28, 18, Color.FromArgb(230, c.R, c.G, c.B), 13f, true);
            }

            // 已录音符=轨色块：命中后转淡；接近判定点变亮
            foreach (var n in Notes)
            {
                double ang = n.T / LoopMs * Math.PI * 2 - Math.PI / 2;
                float nx = (float)(cx + Math.Cos(ang) * R * 0.74), ny = (float)(cy + Math.Sin(ang) * R * 0.74);
                var tc = TrackColor(n.Col);
                if (n.Cycle == k)
                    d2.FillEllipse(nx, ny, 7, 7, Color.FromArgb(90, tc.R, tc.G, tc.B));
                else
                {
                    double absT = cycleStart + n.T;
                    double remain = absT - now;
                    double prox = Math.Max(0, Math.Min(1, 1 - remain / 600));
                    int alpha = (int)(90 + 165 * prox);
                    d2.FillRoundedRect(nx - 9, ny - 5, 18, 10, 4, Color.FromArgb(alpha, tc.R, tc.G, tc.B));
                    d2.DrawRoundedRect(nx - 9, ny - 5, 18, 10, 4, Color.FromArgb(Math.Min(255, alpha + 40), 255, 255, 255), 1f);
                }
            }

            // 播放头（旋转光带）
            double pa = tIn / LoopMs * Math.PI * 2 - Math.PI / 2;
            d2.DrawArcSegments((float)cx, (float)cy, (float)(R * 0.87), (float)(pa - 0.35), 0.7f, Color.FromArgb(230, 80, 200, 255), 4f, 24);
            float px = (float)(cx + Math.Cos(pa) * R * 0.87), py = (float)(cy + Math.Sin(pa) * R * 0.87);
            d2.FillEllipse(px, py, 6, 6, Color.FromArgb(255, 140, 230, 255));
            d2.DrawEllipse(px, py, 11, 11, Color.FromArgb(120, 140, 230, 255), 1.5f);

            // 环顶判定点（正上三角）
            double ja = -Math.PI / 2;
            float jx = (float)(cx + Math.Cos(ja) * R * 0.87), jy = (float)(cy + Math.Sin(ja) * R * 0.87);
            d2.DrawPolyline(new[] { new PointF(jx, jy - 10), new PointF(jx - 8, jy + 6), new PointF(jx + 8, jy + 6), new PointF(jx, jy - 10) },
                Color.FromArgb(255, 255, 255, 255), 2f, false);

            // 中心信息
            d2.Text(Phase == 0 ? "🎙 录" : "🎛 奏", (float)(cx - 70), (float)(cy - 82), 140, 30, Color.FromArgb(255, 235, 240, 255), 22f, true);
            d2.Text(Bars.ToString("0") + " 小节 · " + Bpm.ToString("0.#") + " BPM · 4/4", (float)(cx - 90), (float)(cy - 52), 180, 20, Color.FromArgb(200, 160, 200, 250), 11f, true);
            d2.Text("轨 " + TracksUsed() + "/4 · 循环 " + LoopsDone + " · 满分 " + PerfectLoops + "/3", (float)(cx - 90), (float)(cy - 30), 180, 18, Color.FromArgb(190, 170, 210, 245), 10f, true);
            if (!string.IsNullOrEmpty(GradeDisplay))
                d2.Text(GradeDisplay, (float)(cx - 100), (float)(cy - 10), 200, 20, Color.FromArgb(255, 255, 220, 120), 11f, true);
            d2.Text(JudgeLine(), (float)(cx - 90), (float)(cy + 10), 180, 18, Color.FromArgb(170, 200, 220, 250), 9.5f, true);
            d2.Text("D F J K / 点环写谱 · 180ms 吸附", (float)(cx - 140), (float)(cy + 92), 280, 20, Color.FromArgb(150, 150, 180, 220), 10f, true);

            if (NoticeActive)
                d2.Text(Notice, (float)(cx - 240), (float)(cy + 60), 480, 24, Color.FromArgb(255, 255, 235, 160), 13f, true);
        }

        string JudgeLine()
        {
            int p = _eng.Hits.TryGetValue("PERFECT", out int a) ? a : 0;
            int g = _eng.Hits.TryGetValue("GOOD", out int b2) ? b2 : 0;
            int b = _eng.Hits.TryGetValue("BAD", out int c2) ? c2 : 0;
            int m = _eng.Hits.TryGetValue("MISS", out int d) ? d : 0;
            return "PERFECT×" + p + " GOOD×" + g + " BAD×" + b + " MISS×" + m;
        }
    }
}
