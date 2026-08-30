using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChartPlayer
{
    public class AiSetupForm : Form
    {
        // ===== 陪玩添加区 =====
        readonly ComboBox _lv = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

        // ===== AI 训练区 =====
        readonly ComboBox _trainLv = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Label _chartLabel = new Label { Text = "未选择谱面", AutoSize = true };
        readonly ProgressBar _trainBar = new ProgressBar();
        readonly Label _trainResult = new Label { Text = "结果：—", AutoSize = false };
        Button _trainBtn;
        Button _trainCancelBtn;
        Chart _trainChart;
        bool _training;

        public List<AiLevel> Picked = new List<AiLevel>();

        public AiSetupForm()
        {
            // 兜底：打开设置窗时读取训练参数（MainForm 启动也会调用；此处不在静态构造里做 IO）
            AiEngine.LoadTrained();

            Text = "AI 陪玩设置"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            ClientSize = Ui.S(660, 470); MaximizeBox = false; MinimizeBox = false;
            BackColor = UiColors.Bg;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var tabs = new TabControl { Dock = DockStyle.Fill, BackColor = UiColors.Bg };
            tabs.TabPages.Add(BuildCompanionTab());
            tabs.TabPages.Add(BuildTrainTab());
            Controls.Add(tabs);

            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = Ui.Pad(8, 4, 8, 8), FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "确定", Width = Ui.P(90), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Width = Ui.P(90), DialogResult = DialogResult.Cancel };
            btnRow.Controls.Add(cancel); btnRow.Controls.Add(ok);
            Controls.Add(btnRow);
        }

        /* ================= 陪玩添加 Tab ================= */
        TabPage BuildCompanionTab()
        {
            var page = new TabPage("👥 陪玩设置") { BackColor = UiColors.Bg, Padding = Ui.Pad(8, 6, 8, 4) };

            var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Ui.Pad(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = "AI等级：", AutoSize = true, Padding = new Padding(0, Ui.P(8), 0, 0) });
            foreach (var lv in AiEngine.Levels) _lv.Items.Add(lv.Name + "（" + lv.Dan + "）");
            _lv.SelectedIndex = 0;
            _lv.Width = Ui.P(380);
            _lv.DropDownWidth = Ui.P(460);
            var btnAdd = new Button { Text = "➕ 添加", Width = Ui.P(70) };
            var btnClear = new Button { Text = "🗑 清空", Width = Ui.P(70) };
            row.Controls.Add(_lv); row.Controls.Add(btnAdd); row.Controls.Add(btnClear);
            page.Controls.Add(row);

            _list.BackColor = UiColors.InputBg; _list.ForeColor = Color.White; _list.BorderStyle = BorderStyle.None;
            page.Controls.Add(_list);

            btnAdd.Click += (s, e) =>
            {
                int i = _lv.SelectedIndex;
                if (i < 0) return;
                var lv = AiEngine.Levels[i];
                if (!Picked.Contains(lv)) { Picked.Add(lv); _list.Items.Add(lv.Name + " · " + lv.Dan); }
            };
            btnClear.Click += (s, e) => { Picked.Clear(); _list.Items.Clear(); };
            return page;
        }

        /* ================= AI 训练 Tab ================= */
        TabPage BuildTrainTab()
        {
            var page = new TabPage("🎯 AI 训练") { BackColor = UiColors.Bg, Padding = Ui.Pad(8, 6, 8, 4) };

            // 等级选择
            page.Controls.Add(new Label { Text = "训练等级：", Left = Ui.P(8), Top = Ui.P(14), AutoSize = true, ForeColor = UiColors.BodyText, BackColor = UiColors.Bg });
            _trainLv.Location = new Point(Ui.P(110), Ui.P(8));
            _trainLv.Width = Ui.P(520); _trainLv.DropDownWidth = Ui.P(560);
            _trainLv.BackColor = UiColors.InputBg; _trainLv.ForeColor = Color.White; _trainLv.FlatStyle = FlatStyle.Flat;
            foreach (var lv in AiEngine.Levels)
                _trainLv.Items.Add(lv.Name + "（" + lv.Dan + "）· 目标 ACC " + lv.PassAcc.ToString("0.0") + "%");
            _trainLv.SelectedIndex = 0;
            page.Controls.Add(_trainLv);

            // 选谱 + 谱面路径显示
            var btnPick = new Button { Text = "📂 选择段位谱面", Left = Ui.P(8), Top = Ui.P(44), Width = Ui.P(150), Height = Ui.P(30), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            _chartLabel.Location = new Point(Ui.P(166), Ui.P(52));
            _chartLabel.ForeColor = UiColors.SubText; _chartLabel.BackColor = UiColors.Bg;
            _chartLabel.AutoEllipsis = false; _chartLabel.AutoSize = true; _chartLabel.MaximumSize = new Size(Ui.P(470), 0);   // t29 全文显示：宽内换行完整显示，弃省略号
            page.Controls.Add(btnPick);
            page.Controls.Add(_chartLabel);

            // 训练 / 取消按钮
            _trainBtn = new Button { Text = "▶ 开始训练", Left = Ui.P(8), Top = Ui.P(84), Width = Ui.P(150), Height = Ui.P(32), BackColor = UiColors.BlueBtn, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _trainCancelBtn = new Button { Text = "■ 取消训练", Left = Ui.P(166), Top = Ui.P(84), Width = Ui.P(120), Height = Ui.P(32), Enabled = false, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            page.Controls.Add(_trainBtn);
            page.Controls.Add(_trainCancelBtn);

            // 进度条
            _trainBar.Location = new Point(Ui.P(8), Ui.P(124));
            _trainBar.Width = Ui.P(600); _trainBar.Height = Ui.P(18);
            _trainBar.Maximum = 100; _trainBar.Value = 0;
            page.Controls.Add(_trainBar);

            // 结果
            _trainResult.Location = new Point(Ui.P(8), Ui.P(152));
            _trainResult.Size = new Size(Ui.P(600), Ui.P(160));
            _trainResult.ForeColor = UiColors.BodyText; _trainResult.BackColor = UiColors.Bg;
            _trainResult.TextAlign = ContentAlignment.TopLeft;
            page.Controls.Add(_trainResult);

            btnPick.Click += (s, e) => PickChart();
            _trainBtn.Click += (s, e) => StartTraining();
            _trainCancelBtn.Click += (s, e) => AiTrainer.RequestCancel();
            return page;
        }

        void PickChart()
        {
            try
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择段位谱面",
                    Filter = "全部支持格式|*.osu;*.mc;*.mil;*.json;*.aff;*.txt;*.sm;*.ssc;*.qua|osu!mania|*.osu|Malody|*.mc;*.json|所有文件|*.*"
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var chart = ChartParser.ParseFile(dlg.FileName);
                _trainChart = chart;
                _chartLabel.Text = chart.Title + "（" + chart.Notes.Count + " 音符 · " + chart.ModeName + "）";
            }
            catch (Exception ex)
            {
                _trainChart = null;
                _chartLabel.Text = "未选择谱面";
                MessageBox.Show("谱面解析失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void StartTraining()
        {
            if (_training) return;
            if (_trainChart == null) { MessageBox.Show("请先选择段位谱面", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            int i = _trainLv.SelectedIndex;
            if (i < 0) return;
            var level = AiEngine.Levels[i];

            _training = true;
            _trainBtn.Enabled = false;
            _trainCancelBtn.Enabled = true;
            _trainBar.Value = 0;
            _trainResult.Text = "训练中…（调参模拟，最多 24 轮）";

            // Progress 在 UI 线程创建 → 回调自动切回 UI 线程
            var progress = new Progress<double>(p =>
            {
                try { _trainBar.Value = Math.Max(0, Math.Min(100, (int)Math.Round(p * 100))); } catch { }
            });

            Task.Run(() =>
            {
                TrainResult res;
                try { res = AiTrainer.Train(_trainChart, level, progress); }
                catch (Exception ex) { res = new TrainResult { Converged = false, Reason = "训练异常：" + ex.Message }; }
                // 跨线程回到 UI
                try { if (!IsDisposed) BeginInvoke((Action)(() => OnTrainDone(res))); } catch { }
            });
        }

        void OnTrainDone(TrainResult res)
        {
            _training = false;
            _trainBtn.Enabled = true;
            _trainCancelBtn.Enabled = false;
            _trainBar.Value = 100;
            if (res == null) { _trainResult.Text = "训练失败：无结果"; return; }

            _trainResult.Text = string.Format(
                "预测 ACC：{0:0.00}%    预测 HP：{1:0.0}    目标 ACC：{2:0.0}%\r\n" +
                "是否过段：{3}    迭代轮数：{4}    旋钮 scale={5:0.00} / missBias={6:0.00}\r\n" +
                "{7}",
                res.Acc, res.HpEnd, res.PassAcc,
                res.Passed ? "过段 ✅" : "未过段 ❌",
                res.Rounds, res.Scale, res.MissBias,
                res.Converged ? "" : "未收敛：" + res.Reason);

            if (res.Converged)
                MessageBox.Show("训练完成，已写入该等级参数（TunedScale/TunedMissBias/TrainedHpEnd）并持久化。\n\n预测 ACC：" + res.Acc.ToString("0.00") + "%\n预测 HP：" + res.HpEnd.ToString("0.0"),
                    "训练完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show("训练未收敛：" + res.Reason, "训练结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.Enable(Handle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_training) AiTrainer.RequestCancel();
            base.OnFormClosing(e);
        }

        /// <summary>AI 演示选择弹窗：可选择 AI 等级 + 判定预设（开始演示时应用所选判定）。</summary>
        public static AiLevel PickLevel(IWin32Window owner)
        {
            using var f = new Form
            {
                Text = "选择 AI 等级", FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ClientSize = Ui.S(430, 210),
                MaximizeBox = false, MinimizeBox = false,
                BackColor = UiColors.Bg, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F)
            };

            // AI 等级
            var cb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Left = Ui.P(16), Top = Ui.P(16), Width = Ui.P(390), DropDownWidth = Ui.P(460),
                BackColor = UiColors.InputBg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            foreach (var lv in AiEngine.Levels) cb.Items.Add(lv.Name + "（" + lv.Dan + "）");
            cb.SelectedIndex = 0;

            // 判定预设
            var jlbl = new Label
            {
                Text = "判定预设：", Left = Ui.P(16), Top = Ui.P(58), AutoSize = true,
                ForeColor = UiColors.BodyText, BackColor = UiColors.Bg
            };
            var jb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Left = Ui.P(100), Top = Ui.P(56), Width = Ui.P(306), DropDownWidth = Ui.P(340),
                BackColor = UiColors.InputBg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            foreach (var kv in JudgeSettings.Presets) jb.Items.Add(kv.Value.display);
            // 选中当前判定预设
            int sel = 0, idx = 0;
            foreach (var kv in JudgeSettings.Presets)
            {
                if (kv.Key == JudgeSettings.PresetKey) { sel = idx; break; }
                idx++;
            }
            if (sel >= jb.Items.Count) sel = 0;
            jb.SelectedIndex = sel;

            var ok = new Button
            {
                Text = "开始演示", Left = Ui.P(16), Top = Ui.P(110), Width = Ui.P(140), Height = Ui.P(34), DialogResult = DialogResult.OK,
                BackColor = UiColors.BlueBtn, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            var cancel = new Button
            {
                Text = "取消", Left = Ui.P(176), Top = Ui.P(110), Width = Ui.P(140), Height = Ui.P(34), DialogResult = DialogResult.Cancel,
                BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat
            };
            f.Controls.Add(cb);
            f.Controls.Add(jlbl);
            f.Controls.Add(jb);
            f.Controls.Add(ok);
            f.Controls.Add(cancel);

            if (f.ShowDialog(owner) == DialogResult.OK && cb.SelectedIndex >= 0)
            {
                // 应用所选判定预设（开始演示时使用该判定）
                var keys = new List<string>(JudgeSettings.Presets.Keys);
                if (jb.SelectedIndex >= 0 && jb.SelectedIndex < keys.Count)
                    JudgeSettings.ApplyPreset(keys[jb.SelectedIndex]);
                return AiEngine.Levels[cb.SelectedIndex];
            }
            return null;
        }
    }
}
