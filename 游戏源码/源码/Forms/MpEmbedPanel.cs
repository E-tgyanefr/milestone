using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>联机大厅 + 房间（嵌入现有 MpLobbyForm，TopLevel=false 平铺进主窗体）。</summary>
    public class MpEmbedPanel : UserControl
    {
        readonly GamePanel _game;
        MpLobbyForm _form;

        public event Action GoHome;

        public MpEmbedPanel(GamePanel game)
        {
            _game = game;
            Dock = DockStyle.Fill;
            BackColor = UiColors.Bg;
            Build();
        }

        void Build()
        {
            Controls.Clear();
            var bar = new Panel { Dock = DockStyle.Top, Height = Ui.P(40), BackColor = UiColors.HeadBg };
            var back = new Button
            {
                Text = "🏠 主菜单", Left = Ui.P(10), Top = Ui.P(6), Height = Ui.P(34),
                Padding = new Padding(Ui.P(14), 0, Ui.P(14), 0),
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Microsoft YaHei UI", 12F),
                FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg,
                FlatAppearance = { BorderColor = UiColors.BorderLight }
            };
            Ui.Hover(back);
            back.Click += (s, e) => GoHome?.Invoke();
            bar.Controls.Add(back);
            Controls.Add(bar);

            _form = new MpLobbyForm(_game);
            _form.TopLevel = false;
            _form.FormBorderStyle = FormBorderStyle.None;
            _form.Dock = DockStyle.Fill;
            _form.Show();
            Controls.Add(_form);
        }

        /// <summary>嵌入的表单若被内部关闭则重建（每次 Show 前调用）。</summary>
        public void EnsureAlive()
        {
            if (_form == null || _form.IsDisposed || !_form.IsHandleCreated) Build();
        }
    }
}
