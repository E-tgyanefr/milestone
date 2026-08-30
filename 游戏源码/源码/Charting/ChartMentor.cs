using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChartPlayer
{
    /* ================= ChartMentor：内置制谱 AI（上手向导 + 起步谱生成 + AI 建议侧边面板） =================
     * 流程：① 选曲/模式（取自编辑器）→ ② 🎯 自动对音校准 → ③ 🚀 生成起步谱 → ④ 🤖 AI 检查 → ⑤ 💾 保存。
     * 复用：ChartAiAssistant.ParseWavPcm16 / Dsp.DetectOnsets+EstimateBpm / StarterChartGenerator（引擎）/
     * ChartAiAssistant.RunAiCheck（t12：本地 AI 服务已禁用 —— 仅规则检查，不发起任何网络请求）。
     * 长时调用全部后台线程 + BeginInvoke 回填，绝不阻塞编辑器 UI；任何异常回退为简洁进度文案。
     * 编辑器通过委托注入（GetAudioPath/GetBpm/GetMode/GetKeyCount/GetOffset/GetChart/ImportGenerated/RunAiCheck/SaveChart）。
     */

    /// <summary>制谱助手侧边面板（非模式窗体，编辑器右侧固定位置）。</summary>
    public sealed class ChartMentor : Form
    {
        // ---------- 编辑器委托 ----------
        /// <summary>当前音频路径。</summary>
        public Func<string> GetAudioPath;
        /// <summary>当前 BPM。</summary>
        public Func<double> GetBpm;
        /// <summary>当前模式。</summary>
        public Func<GameMode> GetMode;
        /// <summary>当前键数。</summary>
        public Func<int> GetKeyCount;
        /// <summary>当前偏移（ms）。</summary>
        public Func<double> GetOffset;
        /// <summary>当前编辑态谱面（BuildChart）。</summary>
        public Func<Chart> GetChart;
        /// <summary>导入生成的起步谱（编辑器 ImportGeneratedChart）。</summary>
        public Action<GameMode, int, double, double, List<Note>, string> ImportGenerated;
        /// <summary>触发编辑器 🤖 AI 检查面板（含 AI 摘要）。</summary>
        public Action RunAiCheck;
        /// <summary>触发编辑器保存（SaveMil）。</summary>
        public Action SaveChart;

        // ---------- 状态 ----------
        double[] _onsets;            // 对音得到的起音序列（null=用 BPM 网格回退）
        bool _hasGridFallback;
        int _genCount, _genHoldCount;

        readonly ComboBox _modeBox;
        readonly NumericUpDown _bpmBox, _gapBox, _kcBox;
        readonly ComboBox _subBox;
        readonly Label _audioLbl, _ruleLbl;
        readonly TextBox _log, _aiBox;
        readonly Button _btnAlign, _btnGenerate, _btnAi, _btnSave, _btnSug, _btnRefresh;

        public ChartMentor()
        {
            Text = "🧙 制谱助手";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = MinimizeBox = false;
            ClientSize = Ui.S(470, 664);
            BackColor = UiColors.Bg;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "🧙 制谱助手 · 上手向导：① 选曲/模式 → ② 对音 → ③ 生成 → ④ AI 检查 → ⑤ 保存",
                Left = Ui.P(12), Top = Ui.P(8), Width = Ui.P(446), Height = Ui.P(36),
                ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 10.5F), BackColor = UiColors.Bg
            };
            Controls.Add(title);

            Controls.Add(MkLabel("模式:", 12, 50));
            _modeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Left = Ui.P(70), Top = Ui.P(46), Width = Ui.P(220),
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            _modeBox.Items.Add("Mania（轨道列）");
            _modeBox.Items.Add("Phigros（判定线 X 分布）");
            _modeBox.Items.Add("Cytus（页网格）");
            _modeBox.Items.Add("maimai 环（Ring 方位）");
            _modeBox.Items.Add("ADOFAI 球路（Path 砖块）");
            _modeBox.SelectedIndex = 0;
            Controls.Add(_modeBox);

            Controls.Add(MkLabel("音频:", 12, 82));
            _audioLbl = new Label
            {
                Left = Ui.P(70), Top = Ui.P(82), Width = Ui.P(300), Height = Ui.P(20),
                ForeColor = UiColors.SubText, BackColor = UiColors.Bg,
                AutoEllipsis = false, AutoSize = true, MaximumSize = new Size(Ui.P(300), 0)   // t29 全文显示：音频名宽内换行完整显示，弃省略号
            };
            Controls.Add(_audioLbl);
            _btnRefresh = new Button
            {
                Text = "🔁 刷新", Left = Ui.P(380), Top = Ui.P(78), Width = Ui.P(78), Height = Ui.P(28),
                BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat
            };
            _btnRefresh.Click += (s, e) => RefreshFromEditor();
            Controls.Add(_btnRefresh);

            Controls.Add(MkLabel("BPM:", 12, 114));
            _bpmBox = new NumericUpDown
            {
                Left = Ui.P(56), Top = Ui.P(110), Width = Ui.P(80), DecimalPlaces = 1, Increment = 1,
                Minimum = 30, Maximum = 400, Value = 120, BackColor = UiColors.InputBg,
                ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10F)
            };
            Controls.Add(_bpmBox);

            Controls.Add(MkLabel("细分:", 150, 114));
            _subBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Left = Ui.P(196), Top = Ui.P(110), Width = Ui.P(76),
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10F)
            };
            _subBox.Items.Add("1/4 拍"); _subBox.Items.Add("1/8 拍"); _subBox.Items.Add("1/16 拍");
            _subBox.SelectedIndex = 0;
            Controls.Add(_subBox);

            Controls.Add(MkLabel("间距下限:", 286, 114));
            _gapBox = new NumericUpDown
            {
                Left = Ui.P(356), Top = Ui.P(110), Width = Ui.P(100), Minimum = 40, Maximum = 1000,
                Value = 120, Increment = 20, BackColor = UiColors.InputBg, ForeColor = UiColors.Fg,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10F)
            };
            Controls.Add(_gapBox);

            Controls.Add(MkLabel("键数:", 12, 146));
            _kcBox = new NumericUpDown
            {
                Left = Ui.P(56), Top = Ui.P(142), Width = Ui.P(80), Minimum = 2, Maximum = 10, Value = 4,
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            Controls.Add(_kcBox);

            _btnAlign = new Button { Text = "② 🎯 自动对音校准", Left = Ui.P(12), Top = Ui.P(178), Width = Ui.P(120), Height = Ui.P(34), BackColor = UiColors.BlueBtn, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnGenerate = new Button { Text = "③ 🚀 生成起步谱", Left = Ui.P(140), Top = Ui.P(178), Width = Ui.P(120), Height = Ui.P(34), BackColor = UiColors.BlueBtn, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnAi = new Button { Text = "④ 🤖 AI 检查", Left = Ui.P(268), Top = Ui.P(178), Width = Ui.P(94), Height = Ui.P(34), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            _btnSave = new Button { Text = "⑤ 💾 保存", Left = Ui.P(370), Top = Ui.P(178), Width = Ui.P(88), Height = Ui.P(34), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            foreach (var b in new[] { _btnAlign, _btnGenerate, _btnAi, _btnSave }) { Controls.Add(b); Ui.Hover(b); }

            Controls.Add(MkLabel("进度 / 对音/生成结果：", 12, 220));
            _log = new TextBox
            {
                Left = Ui.P(12), Top = Ui.P(242), Width = Ui.P(446), Height = Ui.P(128),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _log.Text = "欢迎使用制谱助手：\n1) 先加载音频（编辑器「🎵 加载音频…」）；\n2) 点「🎯 自动对音校准」识别起音与 BPM；\n3) 点「🚀 生成起步谱」得到可编辑初稿；\n4) 「🤖 AI 检查」过一遍规则；5) 保存。";
            Controls.Add(_log);

            _ruleLbl = new Label
            {
                Left = Ui.P(12), Top = Ui.P(378), Width = Ui.P(446), Height = Ui.P(50),
                ForeColor = UiColors.BodyText, BackColor = UiColors.Bg
            };
            _ruleLbl.Text = "规则摘要：—（点「④ 🤖 AI 检查」或下方按钮刷新）";
            Controls.Add(_ruleLbl);

            _btnSug = new Button
            {
                Text = "🔄 生成规则建议（不联网）", Left = Ui.P(12), Top = Ui.P(432), Width = Ui.P(230), Height = Ui.P(30),
                BackColor = UiColors.BlueBtn, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            Ui.Hover(_btnSug);
            Controls.Add(_btnSug);

            _aiBox = new TextBox
            {
                Left = Ui.P(12), Top = Ui.P(468), Width = Ui.P(446), Height = Ui.P(150),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _aiBox.Text = "本地 AI 未启用（规则检查仍可用）。点上方「🔄 生成规则建议」查看内置规则校验结果（纯本地计算，不联网）。";
            Controls.Add(_aiBox);

            var close = new Button { Text = "关闭", Left = Ui.P(380), Top = Ui.P(624), Width = Ui.P(78), Height = Ui.P(30), DialogResult = DialogResult.Cancel, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            Ui.Hover(close);
            Controls.Add(close);

            _btnAlign.Click += (s, e) => AlignCalibrate();
            _btnGenerate.Click += (s, e) => GenerateStarter();
            _btnAi.Click += (s, e) => RunAiCheck?.Invoke();
            _btnSave.Click += (s, e) => SaveChart?.Invoke();
            _btnSug.Click += (s, e) => GenAiSuggestion();

            RefreshFromEditor();
        }

        /* ------------------------- ① 选曲/模式 ------------------------- */

        void RefreshFromEditor()
        {
            try
            {
                string audio = GetAudioPath?.Invoke() ?? "";
                _audioLbl.Text = string.IsNullOrEmpty(audio) ? "（未加载音频 —— 用 BPM 网格节奏）" : System.IO.Path.GetFileName(audio);
                double bpm = GetBpm?.Invoke() ?? 120;
                _bpmBox.Value = (decimal)Math.Max(30, Math.Min(400, bpm));
                int kc = GetKeyCount?.Invoke() ?? 4;
                _kcBox.Value = Math.Max(2, Math.Min(10, kc));
                GameMode m = GetMode?.Invoke() ?? GameMode.Mania;
                _modeBox.SelectedIndex = StarterModeIndex(m);
            }
            catch { }
        }

        static int StarterModeIndex(GameMode m) => m switch
        {
            GameMode.Phigros => 1,
            GameMode.Cytus => 2,
            GameMode.Maimai => 3,
            GameMode.AdofaiReal => 4,
            GameMode.Adofai => 4,
            _ => 0,
        };

        static StarterChartMode ModeOfSelected(int idx) => idx switch
        {
            1 => StarterChartMode.Phigros,
            2 => StarterChartMode.Cytus,
            3 => StarterChartMode.Ring,
            4 => StarterChartMode.Path,
            _ => StarterChartMode.Mania,
        };

        static GameMode HostModeOf(StarterChartMode m) => m switch
        {
            StarterChartMode.Phigros => GameMode.Phigros,
            StarterChartMode.Cytus => GameMode.Cytus,
            StarterChartMode.Ring => GameMode.Maimai,
            StarterChartMode.Path => GameMode.AdofaiReal,
            _ => GameMode.Mania,
        };

        /* ------------------------- ② 自动对音校准 ------------------------- */

        void AlignCalibrate()
        {
            _btnAlign.Enabled = false;
            string audio = GetAudioPath?.Invoke() ?? "";
            Log("🎯 对音校准：读取 " + (string.IsNullOrEmpty(audio) ? "（无音频）" : System.IO.Path.GetFileName(audio)) + " …");
            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(audio))
                    {
                        _hasGridFallback = true;
                        _onsets = null;
                        PostToForm(() => Log("⚠️ 未加载音频：改用 BPM 网格节奏生成（BPM=" + _bpmBox.Value + "，细分见设置）。"));
                        return;
                    }
                    var wav = ChartAiAssistant.ParseWavPcm16(audio);
                    if (!wav.Ok)
                    {
                        _hasGridFallback = true;
                        _onsets = null;
                        PostToForm(() => Log("⚠️ 仅支持 16-bit PCM WAV（" + wav.Error + "）：改用 BPM 网格节奏。"));
                        return;
                    }
                    var ons = Dsp.DetectOnsets(wav.Pcm, wav.SampleRate, new DspOptions { MinSeparationMs = Math.Max(40, (double)_gapBox.Value), SilenceFloor = 1e-4 });
                    double est = ons != null && ons.Length > 20 ? Dsp.EstimateBpm(ons, 60, 240, 5) : 0;
                    PostToForm(() =>
                    {
                        _onsets = ons;
                        if (est > 0) _bpmBox.Value = (decimal)Math.Max(30, Math.Min(400, est));
                        _hasGridFallback = false;
                        Log("✅ 对音完成：BPM≈" + (est > 0 ? est.ToString("0.##") : "（保守估计，用设置值）")
                            + " · 起音 " + (ons == null ? 0 : ons.Length) + " 个（密度下限 " + _gapBox.Value + "ms）"
                            + " · 时长 " + (ons != null && ons.Length > 0 ? (ons[ons.Length - 1] / 1000.0).ToString("0.0") : "0") + "s\n→ 点「🚀 生成起步谱」。");
                        _btnAlign.Enabled = true;
                    });
                }
                catch (Exception ex)
                {
                    _hasGridFallback = true;
                    _onsets = null;
                    PostToForm(() => { Log("⚠️ 对音失败：" + ex.Message + " → 改用 BPM 网格节奏。"); _btnAlign.Enabled = true; });
                }
            });
        }

        /* ------------------------- ③ 生成起步谱 ------------------------- */

        void GenerateStarter()
        {
            _btnGenerate.Enabled = false;
            var mode = ModeOfSelected(_modeBox.SelectedIndex);
            int kc = Math.Max(2, (int)_kcBox.Value);
            double bpm = (double)_bpmBox.Value;
            double minGap = (double)_gapBox.Value;
            int subdiv = _subBox.SelectedIndex == 0 ? 4 : _subBox.SelectedIndex == 1 ? 8 : 16;
            double[] ons = _onsets;   // 快照（后台不跨线程读）
            Log("🚀 生成起步谱：" + mode + " · " + kc + " 键 · BPM=" + bpm.ToString("0.#") + " · 密度下限 " + minGap + "ms · 细分 1/" + subdiv
                + (_hasGridFallback ? "（节奏 = BPM 网格模式）" : "（节奏 = 对音起音）") + " …");
            Task.Run(() =>
            {
                try
                {
                    var opts = new StarterOptions
                    {
                        Seed = 20260825,
                        MinGapMs = minGap,
                        HoldGapMs = 900,
                        SnapSubdiv = subdiv,
                    };
                    var cd = StarterChartGenerator.Generate(mode, kc, bpm, ons, opts);
                    var hostMode = HostModeOf(mode);
                    var notes = ToHostNotes(cd, hostMode, bpm);
                    int holds = 0; foreach (var n in notes) if (n.End > n.Time + 1) holds++;
                    PostToForm(() =>
                    {
                        _genCount = notes.Count;
                        _genHoldCount = holds;
                        ImportGenerated?.Invoke(hostMode, kc, bpm, 0, notes, "起步谱-" + mode);
                        Log("✅ 已生成 " + notes.Count + " 音（长条 " + holds + "）并导入编辑器 —— 谱师可微调音符/事件。");
                        UpdateRuleSummary();
                        _btnGenerate.Enabled = true;
                    });
                }
                catch (Exception ex)
                {
                    PostToForm(() => { Log("⚠️ 生成失败：" + ex.Message); _btnGenerate.Enabled = true; });
                }
            });
        }

        /// <summary>引擎 ChartData → 宿主 Note 列表（按模式映射宿主字段：Mania=Col / Phigros·Cytus=X,Y / maimai=Col 1..8 / ADOFAI=Col 0 + X）。</summary>
        static List<Note> ToHostNotes(ChartData src, GameMode hostMode, double bpm)
        {
            var list = new List<Note>();
            if (src == null) return list;
            foreach (var n in src.Notes)
            {
                if (n == null) continue;
                string ty = string.IsNullOrEmpty(n.Type) ? "tap" : n.Type;
                var note = new Note
                {
                    Time = n.TimeMs,
                    End = Math.Max(n.TimeMs, n.EndMs),
                    Type = ty,
                    Bpm = bpm,
                };
                switch (hostMode)
                {
                    case GameMode.Phigros:
                        note.Col = 0; note.Line = 0; note.X = Math.Max(0, Math.Min(1, n.X)); note.Y = 0.5;
                        break;
                    case GameMode.Cytus:
                        note.Col = 0; note.X = Math.Max(0, Math.Min(1, n.X)); note.Y = 0.5;
                        break;
                    case GameMode.Maimai:
                        note.Col = 1 + Math.Max(0, Math.Min(7, (int)Math.Round(n.Lane >= 0 ? n.Lane : 0)));
                        break;
                    case GameMode.AdofaiReal:
                    case GameMode.Adofai:
                        note.Col = 0; note.X = n.Lane >= 0 ? Math.Max(0, Math.Min(1, n.Lane)) : 0.5; note.Kind = 0;
                        break;
                    default: // Mania / Iidx
                        note.Col = Math.Max(0, Math.Min(9, (int)Math.Round(n.Lane >= 0 ? n.Lane : 0)));
                        break;
                }
                list.Add(note);
            }
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            return list;
        }

        /* ------------------------- ④ 规则摘要 / 规则建议（本地 AI 已禁用，零网络） ------------------------- */

        void UpdateRuleSummary()
        {
            int gen = _genCount;
            var chart = GetChart?.Invoke();
            if (chart == null)
            {
                _ruleLbl.Text = "规则摘要：暂无谱面（点「③ 生成起步谱」或直接用编辑器建谱）。";
                return;
            }
            var issues = ChartAiAssistant.RunAiCheck(chart);
            int e = 0, w = 0, h = 0;
            foreach (var it in issues ?? new List<ChartIssue>())
            {
                if (it.Severity == IssueSeverity.Error) e++;
                else if (it.Severity == IssueSeverity.Warning) w++;
                else h++;
            }
            string next;
            if (e > 0) next = "→ 下一步：先修复 " + e + " 个 Error（点「④ 🤖 AI 检查」定位跳转）。";
            else if (gen > 0) next = "→ 下一步：微调音符/事件 → 「④ 🤖 AI 检查」→ 「⑤ 💾 保存」。";
            else next = "→ 下一步：点「③ 生成起步谱」或手动建谱。";
            _ruleLbl.Text = "规则摘要：错误 " + e + " · 警告 " + w + " · 提示 " + h
                + (gen > 0 ? "（已生成 " + gen + " 音，长条 " + _genHoldCount + "）" : "") + "\n" + next;
        }

        /// <summary>生成规则建议（t12：本地 AI 服务已禁用 —— 同步重跑内置规则校验，纯本地计算，零网络调用）。</summary>
        void GenAiSuggestion()
        {
            _btnSug.Enabled = false;
            try
            {
                var chart = GetChart?.Invoke();
                if (chart == null)
                {
                    _aiBox.Text = AiChartReview.DisabledNotice + "\n暂无可审查的谱面（先生成起步谱，或切换到编辑态）。";
                    return;
                }
                var issues = ChartAiAssistant.RunAiCheck(chart);
                var lines = new List<string>();
                foreach (var it in issues)
                    if (it != null)
                        lines.Add("[" + it.Severity + "] " + it.Code + (double.IsNaN(it.TimeMs) ? "" : " @" + it.TimeMs.ToString("0.##") + "ms") + " " + it.Message);
                _aiBox.Text = AiChartReview.BuildDisabledText(lines);
                _ruleLbl.Text = "规则摘要：错误 " + IssuesOf(lines, "Error") + " · 警告 " + IssuesOf(lines, "Warning")
                    + " · 提示 " + IssuesOf(lines, "Hint") + "（本地 AI 未启用 · 规则检查纯本地计算）";
            }
            catch (Exception ex)
            {
                _aiBox.Text = AiChartReview.DisabledNotice + "\n规则建议生成失败：" + ex.Message;
            }
            finally
            {
                _btnSug.Enabled = true;
            }
        }

        static int IssuesOf(List<string> lines, string sev)
        {
            int n = 0;
            if (lines != null) foreach (var l in lines) if (l != null && l.StartsWith("[" + sev + "]")) n++;
            return n;
        }

        /* ------------------------- 工具 ------------------------- */

        static Label MkLabel(string text, int x, int y)
            => new Label
            {
                Text = text, Left = Ui.P(x), Top = Ui.P(y), AutoSize = true,
                ForeColor = UiColors.BodyText, BackColor = UiColors.Bg
            };

        void Log(string line)
        {
            string t = DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine + _log.Text;
            if (t.Length > 4000) t = t.Substring(0, 4000);
            _log.Text = t;
        }

        void PostToForm(Action a)
        {
            try
            {
                if (IsDisposed) return;
                BeginInvoke((Action)a);
            }
            catch { }
        }
    }
}
