using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ChartPlayer
{
    /* ================= 压力测试套件（t19）：Milestone.exe --stresstest "<outDir>" [级别1-5] =================
       L1 = 合成谱 1k 音符 + BPM100（1 轮自动游玩）
       L2 = 5k + BPM30/400 突变 + 超长 hold（1 轮）
       L3 = 20k + 64 判定线 + 同窗密集 jack + 变速链（判定线场 20k 轮 + 轨道场 jack 轮）
       L4 = L3 × 自动游玩 3 轮 + 引擎 UI 转场 fuzz 500 次（五风格轮换）+ 离屏渲染看门狗
       L5 = L4 × 4 轮 + 随机输入 fuzz（200 次/秒按键/触点）+ 内存增长监测（每轮 GC 采样，连续增长>50MB 记 leak）
       断言：无未处理异常/崩溃；每轮判定总数=音符数；转场 fuzz 每次 Completed 且 5s 虚拟超时判 fail；
       内存不持续增长；渲染看门狗（单次离屏渲染 >5s 判 fail）。
       产物：stresstest.json + stresstest.md + stresstest.log（写入 outDir）。
       写入规范：字符串字面量一律单行（无字符串内裸换行）。 */

    public static class StressTest
    {
        public static int RunStressTest(string outDir, string levelArg)
        {
            int level = 1;
            if (!int.TryParse(levelArg, out level)) level = 1;
            level = Math.Max(1, Math.Min(5, level));

            var log = new StringBuilder();
            var model = new StressModel();
            int code = 1;
            string outFull = outDir;
            try
            {
                if (string.IsNullOrEmpty(outFull)) outFull = "obj/stresstest";
                Directory.CreateDirectory(outFull);

                model.GeneratedUtc = DateTime.UtcNow.ToString("O");
                model.Level = level;
                model.EngineJobsDegree = EngineJobs.Degree;
                log.AppendLine("===== Milestone --stresstest 级别 " + level + "（1.." + level + " 逐级执行）=====");
                log.AppendLine("EngineJobs.Degree=" + model.EngineJobsDegree + " · 机器逻辑核 " + Environment.ProcessorCount);

                // ---------- 素材（合成谱 + 规则集） ----------
                var lane = StressCore.MakeLaneRuleset4();
                var laneProfile = JudgementProfile.OsuMania(8);
                var line = StressCore.MakeLineRuleset64();
                var lineProfile = JudgementProfile.Phigros();
                var l1 = StressCharts.BuildL1();
                var l2 = StressCharts.BuildL2();
                var l3Line = StressCharts.BuildL3Line();
                var l3Lane = StressCharts.BuildL3Lane();
                log.AppendLine("素材：L1=" + l1.Notes.Count + " 音符 · L2=" + l2.Notes.Count + " · L3 判定线场=" + l3Line.Notes.Count + "（线 " + l3Line.Lines.Count + "）· L3 轨道场=" + l3Lane.Notes.Count);

                // ---------- 逐级执行 ----------
                if (level >= 1) RunLevel1(model, log, l1, lane, laneProfile);
                if (level >= 2) RunLevel2(model, log, l2, lane, laneProfile);
                if (level >= 3) RunLevel3(model, log, l3Line, l3Lane, line, lineProfile, lane, laneProfile);
                if (level >= 4) RunLevel4(model, log, l3Line, l3Lane, line, lineProfile, lane, laneProfile);
                if (level >= 5) RunLevel5(model, log, l3Line, l3Lane, line, lineProfile, lane, laneProfile, l1, lane, laneProfile);

                // ---------- 结论 ----------
                bool ok = model.Fails.Count == 0;
                model.Pass = ok;
                log.AppendLine("结论：" + (ok ? "PASS（全部断言通过）" : "FAIL（" + model.Fails.Count + " 项断言失败）"));
                foreach (var f in model.Fails) log.AppendLine("  [FAIL] " + f);

                var jsonOpts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
                string json = JsonSerializer.Serialize(new StressModel.Snapshot(model), jsonOpts);
                File.WriteAllText(Path.Combine(outFull, "stresstest.json"), json, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outFull, "stresstest.md"), BuildMarkdown(model), new UTF8Encoding(false));
                log.AppendLine("产物：stresstest.json / stresstest.md / stresstest.log --> " + Path.GetFullPath(outFull));
                code = ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL stresstest 异常：" + ex.GetType().Name + " | " + ex.Message);
                code = 1;
            }
            try
            {
                File.WriteAllText(Path.Combine(outFull, "stresstest.log"), log.ToString(), new UTF8Encoding(true));
                Console.WriteLine(log.ToString());
            }
            catch { }
            return code;
        }

        /* ---------------- 各级执行 ---------------- */

        static void RunLevel1(StressModel m, StringBuilder log, ChartData l1, LaneRuleset lane, JudgementProfile laneProfile)
        {
            log.AppendLine("--- L1：1k 音符 @BPM100 ---");
            AddRound(m, log, StressCore.RunChart("L1", l1, lane, laneProfile), true);
        }

        static void RunLevel2(StressModel m, StringBuilder log, ChartData l2, LaneRuleset lane, JudgementProfile laneProfile)
        {
            log.AppendLine("--- L2：5k 音符 · BPM30/400 突变 · 超长 hold ---");
            AddRound(m, log, StressCore.RunChart("L2", l2, lane, laneProfile), true);
        }

        static void RunLevel3(StressModel m, StringBuilder log, ChartData l3Line, ChartData l3Lane, LineRuleset line, JudgementProfile lineProfile, LaneRuleset lane, JudgementProfile laneProfile)
        {
            log.AppendLine("--- L3：20k 判定线场 + 64 判定线 + jack 轨道场 + 变速链 ---");
            AddRound(m, log, StressCore.RunChart("L3 判定线场 20k", l3Line, line, lineProfile), true);
            AddRound(m, log, StressCore.RunChart("L3 轨道场 jack", l3Lane, lane, laneProfile), true);
        }

        static void RunLevel4(StressModel m, StringBuilder log, ChartData l3Line, ChartData l3Lane, LineRuleset line, JudgementProfile lineProfile, LaneRuleset lane, JudgementProfile laneProfile)
        {
            log.AppendLine("--- L4：L3 × 3 轮 + 转场 fuzz 500 + UI 离屏渲染看门狗 ---");
            for (int r = 1; r <= 3; r++)
            {
                AddRound(m, log, StressCore.RunChart("L4-" + r + " 判定线场 20k", l3Line, line, lineProfile), true);
                AddRound(m, log, StressCore.RunChart("L4-" + r + " 轨道场 jack", l3Lane, lane, laneProfile), true);
            }
            var tf = StressCore.TransitionFuzz(500, 1901);
            m.TransitionFuzz = new StressModel.TransitionFuzzDto { Runs = tf.Runs, Fails = tf.Fails, MaxVirtualMs = Math.Round(tf.MaxVirtualMs, 2), Seconds = Math.Round(tf.Seconds, 2) };
            if (tf.Fails != 0) m.Fails.Add("转场 fuzz：" + tf.Fails + "/" + tf.Runs + " 未通过（未完成/超 5s/Completed≠1/采样非法）");
            log.AppendLine("转场 fuzz：" + tf.Runs + " 次 · 失败 " + tf.Fails + " · 最大虚拟耗时 " + Math.Round(tf.MaxVirtualMs, 1) + "ms · 墙钟 " + Math.Round(tf.Seconds, 2) + "s");
            m.UiWatchdog = RunUiWatchdog();
            if (m.UiWatchdog.Failures != 0 || !string.IsNullOrEmpty(m.UiWatchdog.Error))
                m.Fails.Add("渲染看门狗：失败 " + m.UiWatchdog.Failures + " 次" + (string.IsNullOrEmpty(m.UiWatchdog.Error) ? "" : "（" + m.UiWatchdog.Error + "）"));
            log.AppendLine("渲染看门狗：" + m.UiWatchdog.Renders + " 次离屏渲染 · 平均 " + Math.Round(m.UiWatchdog.AvgMs, 2) + "ms · 最大 " + Math.Round(m.UiWatchdog.MaxMs, 2) + "ms · 失败 " + m.UiWatchdog.Failures);
        }

        static void RunLevel5(StressModel m, StringBuilder log, ChartData l3Line, ChartData l3Lane, LineRuleset line, JudgementProfile lineProfile, LaneRuleset lane, JudgementProfile laneProfile, ChartData l1, LaneRuleset lane4, JudgementProfile laneProfile4)
        {
            log.AppendLine("--- L5：L4 × 4 轮（每轮 GC 采样）+ 随机输入 fuzz 200/s + 内存监测 ---");
            var mem = new StressModel.MemoryDto();
            StressCore.GcSettle();
            double baseline = StressCore.MemoryMb;
            mem.SamplesMb.Add(Math.Round(baseline, 2));
            for (int r = 1; r <= 4; r++)
            {
                for (int k = 1; k <= 3; k++)
                {
                    AddRound(m, log, StressCore.RunChart("L5-" + r + " 判定线场 20k", l3Line, line, lineProfile), true);
                    AddRound(m, log, StressCore.RunChart("L5-" + r + " 轨道场 jack", l3Lane, lane, laneProfile), true);
                }
                var tf = StressCore.TransitionFuzz(500, 1901 + r);
                if (tf.Fails != 0) m.Fails.Add("L5-" + r + " 转场 fuzz：" + tf.Fails + "/" + tf.Runs + " 未通过");
                double sample = StressCore.MemoryMb;
                mem.SamplesMb.Add(Math.Round(sample, 2));
                log.AppendLine("L5 第 " + r + " 轮后堆占用：" + Math.Round(sample, 2) + " MB（fuzz " + tf.Fails + " 失败）");
            }
            // 随机输入 fuzz：200 次/秒按键/触点（按虚拟谱面时间）
            var fz = StressCore.RunInputFuzzRound("L5 输入 fuzz", l1, lane4, laneProfile4, 200, 1951);
            m.InputFuzz = new StressModel.InputFuzzDto
            {
                Events = fz.Events,
                Seconds = Math.Round(fz.Seconds, 2),
                Notes = fz.Round.Notes,
                Judged = fz.Round.Judged,
                Hit = fz.Round.Hit,
                Miss = fz.Round.Miss,
                JudgedEqualsNotes = fz.Round.JudgedEqualsNotes
            };
            if (fz.Round.Failed) m.Fails.Add("输入 fuzz 异常：" + fz.Round.Error);
            if (!fz.Round.JudgedEqualsNotes) m.Fails.Add("输入 fuzz 判定总数=" + fz.Round.Judged + " ≠ 音符数=" + fz.Round.Notes);
            if (fz.Round.Miss != 0) m.Fails.Add("输入 fuzz 出现 MISS=" + fz.Round.Miss);
            log.AppendLine("输入 fuzz：" + fz.Events + " 事件 · 判定 " + fz.Round.Judged + "/" + fz.Round.Notes + " · miss=" + fz.Round.Miss + " · 墙钟 " + Math.Round(fz.Seconds, 2) + "s");
            // 内存增长监测：连续增长（允许 2MB 采样噪声）且总增长 >50MB → leak
            double minAfterFirst = baseline;
            bool rising = true;
            for (int i = 1; i < mem.SamplesMb.Count; i++)
            {
                if (mem.SamplesMb[i] < minAfterFirst) minAfterFirst = mem.SamplesMb[i];
                if (i >= 2 && mem.SamplesMb[i] < mem.SamplesMb[i - 1] - 2.0) rising = false;
            }
            double growth = 0;
            for (int i = 0; i < mem.SamplesMb.Count; i++) growth = Math.Max(growth, mem.SamplesMb[i] - baseline);
            mem.GrowthMb = Math.Round(growth, 2);
            mem.MonotonicRise = rising;
            mem.Leak = rising && growth > 50;
            m.Memory = mem;
            if (mem.Leak) m.Fails.Add("内存持续增长 " + mem.GrowthMb + " MB > 50MB → leak");
            log.AppendLine("内存监测：基线 " + mem.SamplesMb[0] + " MB · 采样 " + string.Join(" / ", mem.SamplesMb.ToArray()) + " · 增长 " + mem.GrowthMb + " MB · 连续上升 " + mem.MonotonicRise + " · leak=" + mem.Leak);
        }

        /* ---------------- 轮记录与断言 ---------------- */

        static void AddRound(StressModel m, StringBuilder log, StressRoundResult r, bool requirePerfect)
        {
            var dto = new StressModel.RoundDto
            {
                Name = r.ChartName + "（" + r.RulesetName + "）",
                Notes = r.Notes,
                Judged = r.Judged,
                Hit = r.Hit,
                Miss = r.Miss,
                Accuracy = Math.Round(r.Accuracy, 6),
                Seconds = Math.Round(r.Seconds, 2),
                JudgedEqualsNotes = r.JudgedEqualsNotes,
                Perfect = r.Perfect,
                Failed = r.Failed,
                Error = r.Error
            };
            m.Rounds.Add(dto);
            if (r.Failed) m.Fails.Add(r.ChartName + " 异常：" + r.Error);
            else if (!r.JudgedEqualsNotes) m.Fails.Add(r.ChartName + " 判定总数=" + r.Judged + " ≠ 音符数=" + r.Notes);
            else if (requirePerfect && !r.Perfect) m.Fails.Add(r.ChartName + " 未满分（hit=" + r.Hit + " miss=" + r.Miss + "）");
            log.AppendLine("轮[" + r.ChartName + "] 音符 " + r.Notes + " · 判定 " + r.Judged + " · 命中 " + r.Hit + " · miss " + r.Miss + " · " + Math.Round(r.Seconds, 2) + "s" + (r.Failed ? " · 异常：" + r.Error : ""));
        }

        /* ---------------- UI 离屏渲染看门狗（GDI 适配器，无窗口） ---------------- */

        static StressModel.UiWatchdogDto RunUiWatchdog()
        {
            var dto = new StressModel.UiWatchdogDto { Pages = new[] { "menu", "songs", "settings" } };
            try
            {
                using (var form = new EngineUiDemoForm((c, d) => true))
                using (var bmp = DpiBitmap.Create(640, 360))   // t78：96 DPI（看门狗与实窗渲染同口径）
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var draw = new GdiDrawAdapter(g);
                    foreach (var page in dto.Pages)
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            long t0 = Stopwatch.GetTimestamp();
                            form.RenderPageTo(page, draw, 640, 360);
                            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                            dto.Renders++;
                            dto.SumMs += ms;
                            if (ms > dto.MaxMs) dto.MaxMs = ms;
                            if (ms > 5000) dto.Failures++;     // 渲染看门狗：单次 >5s 判 fail
                        }
                    }
                }
                dto.AvgMs = dto.Renders > 0 ? dto.SumMs / dto.Renders : 0;
                dto.MaxMs = Math.Round(dto.MaxMs, 2);
                dto.AvgMs = Math.Round(dto.AvgMs, 2);
            }
            catch (Exception ex)
            {
                dto.Error = ex.GetType().Name + ": " + ex.Message;
                dto.Failures++;
            }
            return dto;
        }

        /* ---------------- 报告 ---------------- */

        static string BuildMarkdown(StressModel m)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Milestone --stresstest 报告（级别 " + m.Level + "）");
            sb.AppendLine();
            sb.AppendLine("- 生成（UTC）：" + m.GeneratedUtc);
            sb.AppendLine("- EngineJobs 并行度：" + m.EngineJobsDegree + " · 逻辑核 " + Environment.ProcessorCount);
            sb.AppendLine();
            sb.AppendLine("## 无头压力轮（判定总数=音符数 断言）");
            sb.AppendLine();
            sb.AppendLine("| 轮次 | 音符 | 判定 | 命中 | MISS | ACC | 耗时 | 判定=音符 | 满分 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var r in m.Rounds)
                sb.AppendLine("| " + r.Name + " | " + r.Notes + " | " + r.Judged + " | " + r.Hit + " | " + r.Miss + " | " + r.Accuracy + " | " + r.Seconds + "s | " + (r.JudgedEqualsNotes ? "✅" : "❌") + " | " + (r.Perfect ? "✅" : "❌") + (r.Failed ? " ⚠" + r.Error : "") + " |");
            sb.AppendLine();
            if (m.TransitionFuzz != null)
            {
                sb.AppendLine("## 转场 fuzz（五风格轮换 · 5s 虚拟超时判 fail）");
                sb.AppendLine();
                sb.AppendLine("- 次数：" + m.TransitionFuzz.Runs + " · 失败：" + m.TransitionFuzz.Fails + " · 最大虚拟耗时：" + m.TransitionFuzz.MaxVirtualMs + "ms · 墙钟：" + m.TransitionFuzz.Seconds + "s");
                sb.AppendLine();
            }
            if (m.UiWatchdog != null)
            {
                sb.AppendLine("## UI 离屏渲染看门狗（单次 >5s 判 fail）");
                sb.AppendLine();
                sb.AppendLine("- 页面：" + string.Join("/", m.UiWatchdog.Pages) + " · 渲染：" + m.UiWatchdog.Renders + " 次 · 平均：" + m.UiWatchdog.AvgMs + "ms · 最大：" + m.UiWatchdog.MaxMs + "ms · 失败：" + m.UiWatchdog.Failures + (string.IsNullOrEmpty(m.UiWatchdog.Error) ? "" : " · " + m.UiWatchdog.Error));
                sb.AppendLine();
            }
            if (m.InputFuzz != null)
            {
                sb.AppendLine("## 随机输入 fuzz（200 次/秒按键/触点）");
                sb.AppendLine();
                sb.AppendLine("- 事件：" + m.InputFuzz.Events + " · 音符：" + m.InputFuzz.Notes + " · 判定：" + m.InputFuzz.Judged + " · 命中：" + m.InputFuzz.Hit + " · MISS：" + m.InputFuzz.Miss + " · 判定=音符：" + (m.InputFuzz.JudgedEqualsNotes ? "✅" : "❌") + " · 墙钟：" + m.InputFuzz.Seconds + "s");
                sb.AppendLine();
            }
            if (m.Memory != null)
            {
                sb.AppendLine("## 内存增长监测（每轮 GC 采样 · 连续增长>50MB 记 leak）");
                sb.AppendLine();
                sb.AppendLine("- 采样（MB）：" + string.Join(" / ", m.Memory.SamplesMb.ToArray()));
                sb.AppendLine("- 总增长：" + m.Memory.GrowthMb + " MB · 连续上升：" + m.Memory.MonotonicRise + " · leak：" + (m.Memory.Leak ? "⚠ 是" : "否"));
                sb.AppendLine();
            }
            sb.AppendLine("## 断言失败项（" + m.Fails.Count + "）");
            sb.AppendLine();
            if (m.Fails.Count == 0) sb.AppendLine("- 无");
            else foreach (var f in m.Fails) sb.AppendLine("- " + f);
            sb.AppendLine();
            sb.AppendLine("**结论：" + (m.Pass ? "通过" : "未通过（见上）") + "**");
            return sb.ToString();
        }
    }

    /// <summary>stresstest 数据模型（JSON 序列化载体）。</summary>
    public sealed class StressModel
    {
        public string GeneratedUtc;
        public int Level;
        public int EngineJobsDegree;
        public List<RoundDto> Rounds = new List<RoundDto>();
        public TransitionFuzzDto TransitionFuzz;
        public UiWatchdogDto UiWatchdog;
        public InputFuzzDto InputFuzz;
        public MemoryDto Memory;
        public List<string> Fails = new List<string>();
        public bool Pass;

        public sealed class Snapshot
        {
            public string GeneratedUtc;
            public int Level;
            public int EngineJobsDegree;
            public List<RoundDto> Rounds;
            public TransitionFuzzDto TransitionFuzz;
            public UiWatchdogDto UiWatchdog;
            public InputFuzzDto InputFuzz;
            public MemoryDto Memory;
            public List<string> Fails;
            public bool Pass;

            public Snapshot(StressModel m)
            {
                GeneratedUtc = m.GeneratedUtc;
                Level = m.Level;
                EngineJobsDegree = m.EngineJobsDegree;
                Rounds = m.Rounds;
                TransitionFuzz = m.TransitionFuzz;
                UiWatchdog = m.UiWatchdog;
                InputFuzz = m.InputFuzz;
                Memory = m.Memory;
                Fails = m.Fails;
                Pass = m.Pass;
            }
        }

        public sealed class RoundDto
        {
            public string Name; public int Notes; public int Judged; public int Hit; public int Miss;
            public double Accuracy; public double Seconds;
            public bool JudgedEqualsNotes; public bool Perfect; public bool Failed; public string Error;
        }
        public sealed class TransitionFuzzDto { public int Runs; public int Fails; public double MaxVirtualMs; public double Seconds; }
        public sealed class UiWatchdogDto
        {
            public string[] Pages; public int Renders; public double AvgMs; public double MaxMs; public int Failures; public string Error; public double SumMs;
        }
        public sealed class InputFuzzDto
        {
            public int Events; public double Seconds; public int Notes; public int Judged; public int Hit; public int Miss; public bool JudgedEqualsNotes;
        }
        public sealed class MemoryDto
        {
            public List<double> SamplesMb = new List<double>();
            public double GrowthMb; public bool MonotonicRise; public bool Leak;
        }
    }
}
