using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ChartPlayer
{
    /* ================= 制谱 AI 宿主助手：WAV(16-bit PCM) 解析 / 宿主 Chart → 引擎 ChartData 映射 /
       对音/打拍结果展示（可应用）/ AI 检查问题列表（可点跳时间） ================= */

    /// <summary>WAV 解析结果（16-bit PCM）。</summary>
    public sealed class WavParseResult
    {
        public bool Ok;
        public float[] Pcm;
        public int SampleRate;
        public string Error = "";
    }

    public static class ChartAiAssistant
    {
        /* ---------- WAV 解析（无新包：RIFF 手工解析，仅 16-bit PCM；其余格式报错→宿主提示打拍模式） ---------- */

        public static WavParseResult ParseWavPcm16(string path)
        {
            var r = new WavParseResult();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    r.Error = "文件不存在：" + (path ?? "");
                    return r;
                }
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
                    bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
                {
                    r.Error = "不是 RIFF/WAVE 文件";
                    return r;
                }
                int pos = 12;
                int audioFormat = 0, channels = 0, bits = 0, sampleRate = 0;
                byte[] data = null;
                while (pos + 8 <= bytes.Length)
                {
                    string id = Encoding.ASCII.GetString(bytes, pos, 4);
                    int size = BitConverter.ToInt32(bytes, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt ")
                    {
                        if (body + 16 > bytes.Length) { r.Error = "fmt 块截断"; return r; }
                        audioFormat = BitConverter.ToInt16(bytes, body);
                        channels = BitConverter.ToInt16(bytes, body + 2);
                        sampleRate = BitConverter.ToInt32(bytes, body + 4);
                        bits = BitConverter.ToInt16(bytes, body + 14);
                    }
                    else if (id == "data")
                    {
                        int len = Math.Min(size, bytes.Length - body);
                        if (len > 0)
                        {
                            data = new byte[len];
                            Array.Copy(bytes, body, data, 0, len);
                        }
                    }
                    pos = body + size + (size & 1);   // chunk 双字节对齐
                }
                if (audioFormat != 1) { r.Error = "仅支持 PCM 编码（audioFormat=" + audioFormat + "，压缩格式→打拍模式）"; return r; }
                if (bits != 16) { r.Error = "仅支持 16-bit 采样（bits=" + bits + "）"; return r; }
                if (sampleRate <= 0 || channels <= 0) { r.Error = "采样率/声道非法"; return r; }
                if (data == null || data.Length < 2) { r.Error = "data 块无音频数据"; return r; }

                int frames = data.Length / 2 / channels;
                var pcm = new float[frames];
                if (channels == 1)
                {
                    for (int i = 0; i < frames; i++) pcm[i] = BitConverter.ToInt16(data, i * 2) / 32768.0f;
                }
                else
                {
                    for (int i = 0; i < frames; i++)
                    {
                        int acc = 0;
                        for (int c = 0; c < channels; c++) acc += BitConverter.ToInt16(data, (i * channels + c) * 2);
                        pcm[i] = acc / (32768.0f * channels);   // 多声道平均
                    }
                }
                r.Ok = true; r.Pcm = pcm; r.SampleRate = sampleRate;
                return r;
            }
            catch (Exception ex)
            {
                r.Ok = false; r.Error = ex.Message;
                return r;
            }
        }

        /// <summary>便捷 out 形式（Tests/CLI 用）。</summary>
        public static bool TryParseWavPcm16(string path, out float[] pcm, out int sampleRate, out string error)
        {
            var r = ParseWavPcm16(path);
            pcm = r.Pcm; sampleRate = r.SampleRate; error = r.Error;
            return r.Ok;
        }

        /* ---------- 宿主 Chart → 引擎 ChartData + ValidatorOptions（逐部件） ---------- */

        static ChartValidatorMode MapMode(GameMode m)
        {
            switch (m)
            {
                case GameMode.Phigros: return ChartValidatorMode.Phigros;
                case GameMode.Arcaea: return ChartValidatorMode.Arcaea;
                case GameMode.Adofai:
                case GameMode.AdofaiReal: return ChartValidatorMode.Adofai;
                case GameMode.Cytus: return ChartValidatorMode.Cytus;
                case GameMode.OsuStandard: return ChartValidatorMode.OsuStandard;
                case GameMode.Mania:
                case GameMode.Iidx: return ChartValidatorMode.Mania;
                default: return ChartValidatorMode.Generic;
            }
        }

        static bool IsLaneMode(GameMode m)
            => m == GameMode.Mania || m == GameMode.Iidx;

        /// <summary>构建每个部件（EffectiveParts）的引擎校验视图：音符/Lane/BPM/事件/元数据。</summary>
        public static List<(ChartData Chart, ValidatorOptions Options)> BuildEngineParts(Chart chart)
        {
            var list = new List<(ChartData, ValidatorOptions)>();
            if (chart == null) return list;
            var parts = chart.EffectiveParts();
            foreach (var p in parts)
            {
                var ed = new ChartData { Title = chart.Title, Artist = chart.Artist, AudioOffsetMs = chart.Offset };
                double bpm0 = chart.Bpm > 0 ? chart.Bpm : 120;
                ed.Bpm = new BpmTimeline(bpm0);
                var opts = new ValidatorOptions
                {
                    Mode = MapMode(p.Mode),
                    KeyCount = Math.Max(1, p.KeyCount)
                };
                if (p.Events != null && p.Events.Count > 0)
                {
                    opts.Events = new List<ValidatorEvent>(p.Events.Count);
                    foreach (var e in p.Events)
                        opts.Events.Add(new ValidatorEvent { Type = e.Type ?? "", TimeMs = e.Time, EndMs = e.End, Value = e.Value, EndValue = e.EndValue });
                }
                if (p.Notes != null)
                {
                    foreach (var n in p.Notes)
                    {
                        if (n == null) continue;
                        var rn = new RhythmNote(n.Time)
                        {
                            EndMs = double.IsNaN(n.End) ? n.Time : n.End,
                            Type = string.IsNullOrEmpty(n.Type) ? "tap" : n.Type
                        };
                        if (IsLaneMode(p.Mode)) rn.Lane = n.Col;
                        else { rn.Lane = -1; rn.X = n.X; rn.Y = n.Y; }

                        bool metaUsed = false;
                        var meta = new NoteMeta();
                        if (p.Mode == GameMode.Phigros && !double.IsNaN(n.Alpha)) { meta.Alpha = n.Alpha; metaUsed = true; }
                        if ((p.Mode == GameMode.Adofai || p.Mode == GameMode.AdofaiReal) && n.Kind != 0) { meta.AngleDeg = n.Kind; metaUsed = true; }
                        string ty = rn.Type;
                        if (ty.Equals("arc", StringComparison.OrdinalIgnoreCase) || ty.Equals("arctap", StringComparison.OrdinalIgnoreCase))
                        {
                            meta.EndLane = n.EndCol >= 0 ? n.EndCol : n.Col;
                            meta.EndX = n.EndX; meta.EndY = n.EndY;
                            meta.Deco = n.Decor;
                            metaUsed = true;
                        }
                        if (metaUsed) rn.Tag = meta;
                        ed.Notes.Add(rn);
                    }
                }
                list.Add((ed, opts));
            }
            return list;
        }

        /// <summary>🎯 对音所需音符时间（当前编辑部件；跳过非有限/负值）。</summary>
        public static double[] CollectNoteTimes(IEnumerable<Note> notes)
        {
            var list = new List<double>();
            if (notes != null)
                foreach (var n in notes)
                    if (n != null && !double.IsNaN(n.Time) && !double.IsInfinity(n.Time) && n.Time >= 0)
                        list.Add(n.Time);
            return list.ToArray();
        }

        /// <summary>🤖 AI 检查：全部部件跑 ChartValidator，合并排序（Error&gt;Warning&gt;Hint，时间升序）。</summary>
        public static List<ChartIssue> RunAiCheck(Chart chart)
        {
            var issues = new List<ChartIssue>();
            if (chart == null)
            {
                issues.Add(new ChartIssue { Severity = IssueSeverity.Error, Code = "CHART_NULL", TimeMs = 0, Message = "谱面为空" });
                return issues;
            }
            foreach (var (ed, opts) in BuildEngineParts(chart))
            {
                foreach (var i in ChartValidator.Validate(ed, opts)) issues.Add(i);
            }
            issues.Sort((a, b) =>
            {
                if (a.Severity != b.Severity) return a.Severity.CompareTo(b.Severity);
                double ta = double.IsNaN(a.TimeMs) ? double.MaxValue : a.TimeMs;
                double tb = double.IsNaN(b.TimeMs) ? double.MaxValue : b.TimeMs;
                int c = ta.CompareTo(tb);
                return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
            });
            return issues;
        }

        /* ---------- 结果对话框 ---------- */

        sealed class AiDialog : Form
        {
            public AiDialog(string text)
            {
                Text = text;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = MinimizeBox = false;
                BackColor = UiColors.Bg;
                ForeColor = Color.White;
                Font = new Font("Microsoft YaHei UI", 9.5F);
            }
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try { DarkMode.Enable(Handle); } catch { }
            }
        }

        /// <summary>🎯 校准结果：偏移/置信度/BPM/匹配/离拍 + 「应用」按钮（apply 回调）。</summary>
        public static void ShowCalibrationResult(IWin32Window owner, string title, AlignReport rep, Action<double> apply)
        {
            rep = rep ?? new AlignReport();
            var f = new AiDialog(title) { ClientSize = Ui.S(440, 210) };
            var info = new Label
            {
                Dock = DockStyle.Top, Height = Ui.P(150), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(Ui.P(16), Ui.P(10), Ui.P(12), 0), ForeColor = UiColors.Fg,
                Font = new Font("Microsoft YaHei UI", 11F)
            };
            info.Text = "🎯 建议偏移：" + (rep.OffsetMs >= 0 ? "+" : "") + rep.OffsetMs.ToString("0.##") + " ms"
                + "\n置信度：" + (rep.Confidence * 100).ToString("0") + "%        BPM 估计：" + (rep.BpmEstimate > 0 ? rep.BpmEstimate.ToString("0.##") : "--")
                + "\n匹配音符：" + rep.MatchedNotes + " / " + rep.TotalNotes + "        ｜ 离拍音符：" + rep.OffbeatNotes.Count + " 个";
            var applyBtn = new Button
            {
                Text = "✅ 应用偏移", Size = Ui.S(120, 38), BackColor = UiColors.BlueBtn,
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            var closeBtn = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Size = Ui.S(90, 38), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            applyBtn.Click += (s, e) => { if (apply != null) apply(rep.OffsetMs); f.Close(); };
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = Ui.P(52), FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(Ui.P(16), Ui.P(7), 0, 0)
            };
            row.Controls.Add(applyBtn);
            row.Controls.Add(closeBtn);
            f.Controls.Add(info);
            f.Controls.Add(row);
            Ui.Hover(applyBtn);
            Ui.Hover(closeBtn);
            f.ShowDialog(owner);
        }

        /// <summary>🤖 AI 检查结果：Error&gt;Warning&gt;Hint 着色列表，双击行跳到对应时间。</summary>
        public static void ShowIssuesList(IWin32Window owner, string title, List<ChartIssue> issues, Action<double> seekTo)
        {
            issues = issues ?? new List<ChartIssue>();
            var f = new AiDialog(title) { ClientSize = Ui.S(760, 470) };

            int e = 0, w = 0, h = 0;
            foreach (var i in issues)
            {
                if (i == null) continue;
                if (i.Severity == IssueSeverity.Error) e++;
                else if (i.Severity == IssueSeverity.Warning) w++;
                else h++;
            }
            var summary = new Label
            {
                Dock = DockStyle.Top, Height = Ui.P(34), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(Ui.P(12), Ui.P(8), 0, 0),
                ForeColor = issues.Count == 0 ? UiColors.Green : UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            summary.Text = issues.Count == 0
                ? "✅ 未发现问题"
                : "共 " + issues.Count + " 项：错误 " + e + " · 警告 " + w + " · 提示 " + h + "　（双击行 → 跳到该时间）";

            var list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
                BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            list.Columns.Add("严重度", Ui.P(74));
            list.Columns.Add("时间(ms)", Ui.P(96));
            list.Columns.Add("代码", Ui.P(186));
            list.Columns.Add("说明", Ui.P(380));
            for (int i = 0; i < issues.Count; i++)
            {
                var it = issues[i];
                if (it == null) continue;
                var lvi = new ListViewItem(it.Severity.ToString());
                lvi.SubItems.Add(double.IsNaN(it.TimeMs) ? "--" : it.TimeMs.ToString("0.##"));
                lvi.SubItems.Add(it.Code);
                lvi.SubItems.Add(it.Message);
                lvi.ForeColor = it.Severity == IssueSeverity.Error ? Color.FromArgb(255, 118, 118)
                              : it.Severity == IssueSeverity.Warning ? Color.FromArgb(255, 198, 86)
                              : Color.FromArgb(118, 176, 255);
                lvi.Tag = it.TimeMs;
                list.Items.Add(lvi);
            }
            list.DoubleClick += (s, e2) =>
            {
                if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is double t && !double.IsNaN(t))
                {
                    if (seekTo != null) seekTo(t);
                    f.Close();
                }
            };

            var closeBtn = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Size = Ui.S(90, 38), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.P(52), FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(Ui.P(12), Ui.P(7), 0, 0) };
            row.Controls.Add(closeBtn);
            Ui.Hover(closeBtn);
            f.Controls.Add(summary);
            f.Controls.Add(list);
            f.Controls.Add(row);
            f.ShowDialog(owner);
        }
        /* ---------- 🤖 AI 检查 + 规则摘要（t12：本地 AI 服务已禁用 —— 不发起任何网络请求，仅规则检查） ---------- */

        /// <summary>谱面音符总数（全部部件累加）。</summary>
        static int TotalNotes(Chart chart)
        {
            int n = 0;
            if (chart == null) return 0;
            foreach (var p in chart.EffectiveParts()) n += p.Notes == null ? 0 : p.Notes.Count;
            return n;
        }

        /// <summary>
        /// 🤖 AI 检查：规则问题列表（Error&gt;Warning&gt;Hint 着色、双击跳时间）+ 底部「规则摘要」区。
        /// t12：本地 AI 服务已禁用 —— 不再调用 AiChartReview / Ollama / llama-server / LM Studio 等本地服务；
        /// 摘要区直接展示提示文案 + 规则检查统计，「🔄 刷新规则摘要」仅重跑内置规则校验（纯本地计算，零网络调用）。
        /// </summary>
        public static void ShowAiCheckDialog(IWin32Window owner, string title, Chart chart,
            List<ChartIssue> issues, Action<double> seekTo)
        {
            issues = issues ?? new List<ChartIssue>();
            var f = new AiDialog(title) { ClientSize = Ui.S(760, 566) };

            int e = 0, w = 0, h = 0;
            foreach (var i in issues)
            {
                if (i == null) continue;
                if (i.Severity == IssueSeverity.Error) e++;
                else if (i.Severity == IssueSeverity.Warning) w++;
                else h++;
            }
            var summary = new Label
            {
                Dock = DockStyle.Top, Height = Ui.P(32), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(Ui.P(12), Ui.P(7), 0, 0),
                ForeColor = issues.Count == 0 ? UiColors.Green : UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            summary.Text = issues.Count == 0
                ? "✅ 规则检查未发现问题（本地 AI 已禁用，下方为规则摘要）"
                : "共 " + issues.Count + " 项：错误 " + e + " · 警告 " + w + " · 提示 " + h + "　（双击行 → 跳到该时间）";

            var aiLabel = new Label
            {
                Dock = DockStyle.Top, Height = Ui.P(26), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(Ui.P(12), Ui.P(4), 0, 0),
                ForeColor = UiColors.SubText, Font = new Font("Microsoft YaHei UI", 9F)
            };
            aiLabel.Text = "🤖 规则摘要 · 本地 AI 未启用（规则检查仍可用）";

            var list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
                BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            list.Columns.Add("严重度", Ui.P(74));
            list.Columns.Add("时间(ms)", Ui.P(96));
            list.Columns.Add("代码", Ui.P(186));
            list.Columns.Add("说明", Ui.P(380));
            void RebuildList(IReadOnlyList<ChartIssue> src)
            {
                list.Items.Clear();
                for (int i = 0; i < src.Count; i++)
                {
                    var it = src[i];
                    if (it == null) continue;
                    var lvi = new ListViewItem(it.Severity.ToString());
                    lvi.SubItems.Add(double.IsNaN(it.TimeMs) ? "--" : it.TimeMs.ToString("0.##"));
                    lvi.SubItems.Add(it.Code);
                    lvi.SubItems.Add(it.Message);
                    lvi.ForeColor = it.Severity == IssueSeverity.Error ? Color.FromArgb(255, 118, 118)
                                  : it.Severity == IssueSeverity.Warning ? Color.FromArgb(255, 198, 86)
                                  : Color.FromArgb(118, 176, 255);
                    lvi.Tag = it.TimeMs;
                    list.Items.Add(lvi);
                }
            }
            RebuildList(issues);
            list.DoubleClick += (s, e2) =>
            {
                if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is double t && !double.IsNaN(t))
                {
                    if (seekTo != null) seekTo(t);
                    f.Close();
                }
            };

            var aiBox = new TextBox
            {
                Dock = DockStyle.Bottom, Height = Ui.P(108), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = UiColors.InputBg, ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 9.5F),
                Text = BuildRuleSummaryText(chart, issues)
            };

            void RefreshRuleSummary()
            {
                try
                {
                    var fresh = chart == null
                        ? new List<ChartIssue> { new ChartIssue { Severity = IssueSeverity.Error, Code = "CHART_NULL", TimeMs = 0, Message = "谱面为空" } }
                        : ChartAiAssistant.RunAiCheck(chart);
                    RebuildList(fresh);
                    int e3 = 0, w3 = 0, h3 = 0;
                    foreach (var it in fresh)
                    {
                        if (it == null) continue;
                        if (it.Severity == IssueSeverity.Error) e3++;
                        else if (it.Severity == IssueSeverity.Warning) w3++;
                        else h3++;
                    }
                    summary.Text = fresh.Count == 0
                        ? "✅ 规则检查未发现问题（本地 AI 已禁用，下方为规则摘要）"
                        : "共 " + fresh.Count + " 项：错误 " + e3 + " · 警告 " + w3 + " · 提示 " + h3 + "　（双击行 → 跳到该时间）";
                    summary.ForeColor = fresh.Count == 0 ? UiColors.Green : UiColors.BodyText;
                    aiBox.Text = BuildRuleSummaryText(chart, fresh);
                }
                catch (Exception ex)
                {
                    aiBox.Text = AiChartReview.DisabledNotice + "\n规则摘要刷新失败：" + ex.Message;
                }
            }

            var aiBtn = new Button
            {
                Text = "🔄 刷新规则摘要", Size = Ui.S(150, 38), BackColor = UiColors.BlueBtn,
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            var closeBtn = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Size = Ui.S(90, 38), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.P(52), FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(Ui.P(12), Ui.P(7), 0, 0) };
            row.Controls.Add(aiBtn);
            row.Controls.Add(closeBtn);
            Ui.Hover(aiBtn);
            Ui.Hover(closeBtn);

            aiBtn.Click += (s, e2) => RefreshRuleSummary();

            f.Controls.Add(summary);
            f.Controls.Add(aiLabel);
            f.Controls.Add(list);
            f.Controls.Add(aiBox);
            f.Controls.Add(row);

            f.ShowDialog(owner);
        }

        /// <summary>规则摘要文案：本地 AI 禁用提示 + 规则检查统计（纯本地计算，零网络调用）。</summary>
        static string BuildRuleSummaryText(Chart chart, List<ChartIssue> issues)
        {
            var sb = new StringBuilder();
            sb.AppendLine(AiChartReview.DisabledNotice);
            sb.AppendLine("程序已禁用全部本地 AI 服务调用（Ollama / llama-server / LM Studio 等），不会发起任何网络请求。");
            int e = 0, w = 0, h = 0;
            foreach (var i in issues ?? new List<ChartIssue>())
            {
                if (i == null) continue;
                if (i.Severity == IssueSeverity.Error) e++;
                else if (i.Severity == IssueSeverity.Warning) w++;
                else h++;
            }
            sb.Append("内置规则检查已完成：错误 " + e + " · 警告 " + w + " · 提示 " + h
                + " · 音符 " + TotalNotes(chart) + " 个（完整问题列表见上方，双击可跳转）。");
            return sb.ToString();
        }
    }
}

