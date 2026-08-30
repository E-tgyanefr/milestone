using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChartPlayer
{
    public class CalibrationForm : Form
    {
        readonly Label _info = new Label { Dock = DockStyle.Top, Height = Ui.P(40), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 14F) };
        readonly Label _result = new Label { Dock = DockStyle.Top, Height = Ui.P(60), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 13F) };
        readonly Button _start = new Button { Text = "▶ 开始校准", Width = Ui.P(140), Height = Ui.P(40) };
        readonly Button _apply = new Button { Text = "✅ 应用", Width = Ui.P(100), Height = Ui.P(40), Visible = false };
        NumericUpDown _offsetInput;   // 手动偏移（支持负值）
        bool _running;
        double _t0;
        readonly double[] _beats = new double[8];
        readonly double[] _devs = new double[8];
        int _idx;
        double _suggested;

        public CalibrationForm()
        {
            Text = "自动调整延迟"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            ClientSize = Ui.S(480, 280); MaximizeBox = MinimizeBox = false;
            BackColor = UiColors.Bg;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            _info.Text = "跟随节拍按下任意打击键，共 8 拍";

            // 偏移输入框：屏蔽中文输入法，支持负数；获得焦点全选便于直接覆盖输入
            _offsetInput = new NumericUpDown
            {
                Minimum = -300, Maximum = 300, Value = (decimal)GameSettings.Offset,
                DecimalPlaces = 0, Increment = 1, Width = Ui.P(120),
                ImeMode = ImeMode.Off,
                BackColor = UiColors.InputBg, ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _offsetInput.Enter += (s, e) => _offsetInput.Select(0, _offsetInput.Text.Length);

            var wrap = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            wrap.Controls.Add(_info); wrap.Controls.Add(_result);
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            row.Controls.Add(new Label { Text = "偏移(ms)：", AutoSize = true, Padding = new Padding(0, Ui.P(10), 0, 0), ForeColor = UiColors.BodyText, BackColor = UiColors.Bg });
            row.Controls.Add(_offsetInput);
            row.Controls.Add(_start);
            row.Controls.Add(_apply);
            wrap.Controls.Add(row);
            Controls.Add(wrap);
            KeyPreview = true;
            _start.Click += (s, e) => StartCalib();
            _apply.Click += (s, e) =>
            {
                GameSettings.Offset = (double)_offsetInput.Value;
                _apply.Visible = false;
                _result.Text = "✅ 已应用全局延迟：" + GameSettings.Offset.ToString("0") + " ms";
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.Enable(Handle);
        }

        async void StartCalib()
        {
            _running = true; _idx = 0; _apply.Visible = false;
            _result.Text = "准备…";
            _t0 = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < 8; i++) _beats[i] = 900 + i * 600;
            for (int i = 0; i < 8; i++)
            {
                var wait = _t0 + _beats[i] - (Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                if (wait > 0) await Task.Delay((int)wait);
                SystemSounds.Beep.Play();
                _info.Text = "第 " + (i + 1) + " 拍 / 共 8 拍 — 跟随节拍按任意打击键";
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_running && _idx < 8)
            {
                if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu) return;
                double now = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
                _devs[_idx] = now - (_t0 + _beats[_idx]);
                _idx++;
                _info.Text = "已收录 " + _idx + " 拍" + (_idx < 8 ? " — 继续按拍" : "");
                if (_idx >= 8) Finish();
                return;
            }
            base.OnKeyDown(e);
        }

        void Finish()
        {
            _running = false;
            double sum = 0; foreach (var d in _devs) sum += d;
            double avg = sum / 8;
            double delta = -Math.Round(avg);          // 偏晚(avg>0)→负向调，偏早(avg<0)→正向调
            _suggested = Math.Max(-300, Math.Min(300, GameSettings.Offset + delta));
            _result.Text = "平均偏差 " + (avg >= 0 ? "+" : "") + avg.ToString("0") + " ms\n建议偏移 " + (_suggested >= 0 ? "+" : "") + _suggested.ToString("0") + " ms（可直接在输入框改负数）";
            _offsetInput.Value = (decimal)_suggested;
            _apply.Visible = true;
            _info.Text = "校准完成";
        }
    }
}
