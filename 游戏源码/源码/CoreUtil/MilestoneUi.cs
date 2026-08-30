using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>两色 Logo：Mile（白）+ stone（主题色），水平居中，随 DPI 自动缩放。</summary>
    public sealed class MilestoneLogo : Control
    {
        public MilestoneLogo()
        {
            DoubleBuffered = true;
            BackColor = UiColors.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font("Microsoft YaHei UI", 40F, FontStyle.Bold))
            {
                var s1 = g.MeasureString("Mile", f);
                var s2 = g.MeasureString("stone", f);
                float total = s1.Width + s2.Width;
                float x = Math.Max(0, (Width - total) / 2f);
                float y = Math.Max(0, (Height - Math.Max(s1.Height, s2.Height)) / 2f);
                g.DrawString("Mile", f, Brushes.White, x, y);
                using (var b = new SolidBrush(UiColors.Accent))
                    g.DrawString("stone", f, b, x + s1.Width, y);
            }
        }
    }

    /// <summary>渐变蓝主按钮（悬停增亮）。</summary>
    public sealed class GradientButton : Button
    {
        bool _hover;

        public GradientButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = ClientRectangle;
            var c1 = _hover ? Ui.Tint(UiColors.BlueBtn, 1.15) : UiColors.BlueBtn;
            var c2 = _hover ? Ui.Tint(UiColors.Blue, 1.15) : UiColors.Blue;
            using (var path = Ui.Rounded(r, Ui.P(10)))
            {
                var state = g.Save();
                g.SetClip(path);
                UiColors.Gradient(g, r, c1, c2);
                g.Restore(state);
            }
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                // t20 P1-7：文本 autofit——超宽时按比例缩字号（小窗口「戏」字溢出修复）
                var f = Font;
                float tw = g.MeasureString(Text, Font).Width;
                if (tw > r.Width - 8 && r.Width > 40 && Font.Size > 6f)
                    f = new Font(Font.FontFamily, Math.Max(6f, Font.Size * (r.Width - 8) / Math.Max(1, tw)), Font.Style);
                using (var tb = new SolidBrush(ForeColor))
                    g.DrawString(Text, f, tb, r, sf);
            }
        }
    }

    /// <summary>
    /// 段位挑战选择对话框：左侧段位集列表，右侧该集曲目列表，双击或按钮开始游玩。
    /// </summary>
    public sealed class DanSelectDialog : Form
    {
        public class DanGroup
        {
            public string Name = "";
            public readonly List<Chart> Charts = new List<Chart>();
        }

        readonly List<DanGroup> _groups;
        readonly ListBox _setList;
        readonly ListBox _songList;
        public Chart Selected { get; private set; }

        /// <summary>扫描曲库中 IsDan 谱面并按 DanSet 分组排序
        /// （MainForm.OpenDanChallenge 与引擎段位页共用同一构建器——同输入同结果）。</summary>
        public static List<DanGroup> BuildGroups(string chartsFolder)
        {
            var sets = new Dictionary<string, DanGroup>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!string.IsNullOrEmpty(chartsFolder) && Directory.Exists(chartsFolder))
                {
                    var parsed = ChartParser.ParseFolderParallel(chartsFolder);
                    foreach (var c in parsed)
                    {
                        if (c == null || !c.IsDan) continue;
                        string name = string.IsNullOrEmpty(c.DanSet) ? "未分组" : c.DanSet;
                        if (!sets.TryGetValue(name, out var grp)) { grp = new DanGroup { Name = name }; sets[name] = grp; }
                        grp.Charts.Add(c);
                    }
                }
            }
            catch (Exception ex) { Logger.Error("扫描段位谱面失败", ex); }

            var list = new List<DanGroup>(sets.Values);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var g in list) g.Charts.Sort((a, b) => string.Compare(a.DanName + a.Title, b.DanName + b.Title, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public DanSelectDialog(List<DanGroup> groups)
        {
            _groups = groups ?? new List<DanGroup>();
            Text = "🏆 段位挑战";
            Size = Ui.S(780, 500);
            MinimumSize = Ui.S(560, 380);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = UiColors.Bg;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10F);
            FormBorderStyle = FormBorderStyle.Sizable;

            var head = new Label
            {
                Text = "🏆 段位挑战 · 选择段位与曲目（双击曲目直接开始）",
                Dock = DockStyle.Top, Height = Ui.P(46), BackColor = UiColors.HeadBg,
                ForeColor = UiColors.HeadTitle, Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.P(16), 0, 0, 0)
            };

            var setLbl = new Label
            {
                Text = "段位集", Dock = DockStyle.Top, Height = Ui.P(30), BackColor = UiColors.HeadBg,
                ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.P(16), 0, 0, 0)
            };
            _setList = new ListBox
            {
                Dock = DockStyle.Left, Width = Ui.P(250),
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 11F), IntegralHeight = false
            };

            var songLbl = new Label
            {
                Text = "曲目", Dock = DockStyle.Top, Height = Ui.P(30), BackColor = UiColors.HeadBg,
                ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.P(16), 0, 0, 0)
            };
            _songList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 11F), IntegralHeight = false
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.P(64), BackColor = UiColors.HeadBg };
            var btnStart = new Button
            {
                Text = "▶ 开始游玩", Left = Ui.P(16), Top = Ui.P(12), Width = Ui.P(150), Height = Ui.P(40),
                FlatStyle = FlatStyle.Flat, BackColor = UiColors.BlueBtn, ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13F), FlatAppearance = { BorderColor = UiColors.BlueBtn }
            };
            var btnCancel = new Button
            {
                Text = "取消", Left = Ui.P(180), Top = Ui.P(12), Width = Ui.P(100), Height = Ui.P(40),
                FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg,
                Font = new Font("Microsoft YaHei UI", 13F), FlatAppearance = { BorderColor = UiColors.BorderLight }
            };
            Ui.Hover(btnStart); Ui.Hover(btnCancel);
            btnStart.Click += (s, e) => Pick();
            btnCancel.Click += (s, e) => { Selected = null; DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.Add(btnStart);
            bottom.Controls.Add(btnCancel);

            // 顶部/标题区域与列表的层级
            Controls.Add(_songList);
            Controls.Add(songLbl);
            Controls.Add(_setList);
            Controls.Add(setLbl);
            Controls.Add(bottom);
            Controls.Add(head);

            foreach (var grp in _groups)
                _setList.Items.Add(grp.Name + "（" + grp.Charts.Count + "）");
            if (_setList.Items.Count > 0) _setList.SelectedIndex = 0;
            _setList.SelectedIndexChanged += (s, e) => FillSongs();
            _songList.MouseDoubleClick += (s, e) => { if (_songList.SelectedIndex >= 0) Pick(); };
            FillSongs();
        }

        void FillSongs()
        {
            _songList.Items.Clear();
            int i = _setList.SelectedIndex;
            if (i < 0 || i >= _groups.Count) return;
            foreach (var c in _groups[i].Charts)
                _songList.Items.Add((string.IsNullOrEmpty(c.DanName) ? "—" : c.DanName) + "   ·   " + c.Title + "   ·   " + c.KeyCount + "K");
        }

        void Pick()
        {
            int i = _setList.SelectedIndex;
            if (i < 0 || i >= _groups.Count) return;
            int j = _songList.SelectedIndex;
            if (j < 0 || j >= _groups[i].Charts.Count)
            {
                MessageBox.Show("请先选择曲目", "段位挑战", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Selected = _groups[i].Charts[j];
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
