using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ChartPlayer
{
    public class MpLobbyForm : Form
    {
        readonly GamePanel _game;
        readonly TextBox _name = new TextBox { Text = "玩家", BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        readonly TextBox _ip = new TextBox { Text = "", PlaceholderText = "输入房主 IP（如 192.168.1.10）", BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        readonly Button _btnHost = DarkBtn("🌐 创建房间", 150, 34);
        readonly Button _btnJoin = DarkBtn("🔗 加入房间", 150, 34);
        readonly Button _btnAi = DarkBtn("🤝 AI 陪玩设置", 150, 30);
        readonly Button _btnLeave = DarkBtn("🚪 离开房间", 110, 30, false);
        readonly Button _btnSend = DarkBtn("📤 发送谱面（房主）", 170, 30, false);
        readonly Button _btnStart = DarkBtn("▶ 开始联机游玩", 170, 30, false);
        readonly Label _status = new Label { Text = "未联机", AutoSize = true, ForeColor = Color.FromArgb(126, 232, 162), BackColor = UiColors.Bg };
        readonly ListBox _players = new ListBox { Height = Ui.P(80), Dock = DockStyle.Top, BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.None };
        readonly ListBox _leader = new ListBox { Height = Ui.P(90), Dock = DockStyle.Top, BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.None };
        readonly ListBox _leaderboard = new ListBox { Height = Ui.P(100), Dock = DockStyle.Top, BackColor = UiColors.InputBg, ForeColor = Color.White, BorderStyle = BorderStyle.None };
        readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 19, 34), ForeColor = Color.White, BorderStyle = BorderStyle.None };
        bool _subscribed;

        static Button DarkBtn(string text, int w, int h, bool enabled = true)
        {
            var b = new Button
            {
                Text = text, Width = Ui.P(w), Height = Ui.P(h), Enabled = enabled,
                FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg,
                FlatAppearance = { BorderColor = UiColors.BorderLight }
            };
            Ui.Hover(b);
            return b;
        }

        static Label SectionLabel(string t) => new Label
        {
            Text = t, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Ui.P(10), Ui.P(6), 0, 0),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = UiColors.HeadTitle, BackColor = UiColors.Bg
        };

        public MpLobbyForm(GamePanel game)
        {
            _game = game;
            Text = "🌐 局域网联机大厅";
            ClientSize = Ui.S(480, 680);
            MinimumSize = Ui.S(480, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiColors.Bg;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScroll = false;

            // 联机名称默认使用玩家档案名
            var prof = PlayerData.LoadProfile();
            if (!string.IsNullOrEmpty(prof.Name) && prof.Name != "玩家") MpManager.PlayerName = prof.Name;
            _name.Text = MpManager.PlayerName;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Ui.Pad(10, 10, 10, 4), BackColor = UiColors.Bg };
            top.Controls.Add(new Label { Text = "玩家名称：", AutoSize = true, Padding = new Padding(0, Ui.P(8), 0, 0), ForeColor = UiColors.BodyText, BackColor = UiColors.Bg });
            _name.Width = Ui.P(120); top.Controls.Add(_name);
            Controls.Add(top);

            var hostRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Ui.Pad(10, 0, 10, 4), BackColor = UiColors.Bg };
            hostRow.Controls.Add(_btnHost);
            Controls.Add(hostRow);

            var joinRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Ui.Pad(10, 0, 10, 4), BackColor = UiColors.Bg };
            joinRow.Controls.Add(new Label { Text = "主机 IP：", AutoSize = true, Padding = new Padding(0, Ui.P(8), 0, 0), ForeColor = UiColors.BodyText, BackColor = UiColors.Bg });
            _ip.Width = Ui.P(130); joinRow.Controls.Add(_ip);
            joinRow.Controls.Add(_btnJoin);
            Controls.Add(joinRow);

            var aiRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Ui.Pad(10, 0, 10, 4), BackColor = UiColors.Bg };
            aiRow.Controls.Add(_btnAi);
            Controls.Add(aiRow);

            Controls.Add(_status);
            Controls.Add(SectionLabel("房间玩家"));
            Controls.Add(_players);
            Controls.Add(SectionLabel("🏆 当前对局排行"));
            Controls.Add(_leader);
            Controls.Add(SectionLabel("🏆 本地联机排行榜（累计）"));
            Controls.Add(_leaderboard);
            Controls.Add(SectionLabel("📋 日志"));
            Controls.Add(_log);

            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = Ui.Pad(10, 4, 10, 10), BackColor = UiColors.Bg };
            btnRow.Controls.Add(_btnLeave); btnRow.Controls.Add(_btnSend); btnRow.Controls.Add(_btnStart);
            Controls.Add(btnRow);

            _name.TextChanged += (s, e) =>
            {
                string t = _name.Text.Trim();
                if (t.Length > 0 && t != MpManager.PlayerName) MpManager.Rename(t);
            };

            _btnHost.Click += (s, e) =>
            {
                MpManager.PlayerName = _name.Text.Trim();
                if (MpManager.PlayerName.Length == 0) MpManager.PlayerName = "玩家";
                MpManager.StartHost();
            };
            _btnJoin.Click += (s, e) =>
            {
                MpManager.PlayerName = _name.Text.Trim();
                if (MpManager.PlayerName.Length == 0) MpManager.PlayerName = "玩家";
                MpManager.Join(_ip.Text.Trim());
            };
            _btnLeave.Click += (s, e) => { MpManager.Leave(); Close(); };
            _btnSend.Click += (s, e) => SendChartAsHost();
            _btnStart.Click += (s, e) =>
            {
                if (MpManager.CurrentChart == null) { MessageBox.Show("请先「发送谱面」", "联机"); return; }
                MpManager.SendStart();
            };
            _btnAi.Click += (s, e) =>
            {
                using var dlg = new AiSetupForm();
                dlg.ShowDialog(this);
                if (dlg.Picked.Count == 0) return;
                foreach (var lv in dlg.Picked) _game.AddCompanionAi(lv);
                _game.ResetCompanionAis();
                string names = string.Join("、", dlg.Picked.ConvertAll(x => x.Name));
                _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] 🤝 已设置陪玩 AI：" + names + "\r\n");
                RefreshUi();
            };

            _subscribed = true;
            MpManager.Log += OnLog;
            MpManager.OnRoster += OnRoster;
            MpManager.OnHit += OnScores;
            MpManager.OnFinish += OnScores;
            MpManager.OnError += OnLog;

            MpLeaderboard.Load();
            RefreshUi();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.Enable(Handle);
        }

        void RunUi(Action a) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); }

        void OnLog(string s) => RunUi(() => { _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\r\n"); RefreshUi(); });
        void OnRoster(string[] names) => RunUi(() =>
        {
            _players.Items.Clear();
            foreach (var n in names) _players.Items.Add(n);
            RefreshUi();
        });
        void OnScores(MpPlayerState st) => RunUi(RefreshLeader);

        void RefreshLeader()
        {
            _leader.Items.Clear();
            foreach (var p in MpManager.Players.Values.OrderByDescending(x => x.Score))
                _leader.Items.Add(p.Name + "　" + p.Score + "　" + p.Acc.ToString("0.00") + "%" + (p.Finished ? "  ✓" : ""));
        }

        void RefreshLeaderboard()
        {
            _leaderboard.Items.Clear();
            var all = MpLeaderboard.All;
            if (all.Count == 0) { _leaderboard.Items.Add("（暂无联机成绩，完成一局联机游戏后自动记录）"); return; }
            foreach (var e in all.Take(20))
                _leaderboard.Items.Add(e.Date + " · " + e.Name + " · " + e.Score + " 分 · " + e.Acc.ToString("0.00") + "% · " + e.Chart);
        }

        void RefreshUi()
        {
            bool inRoom = MpManager.Role != MpRole.None;
            _btnHost.Enabled = !inRoom;
            _btnJoin.Enabled = !inRoom;
            _btnLeave.Enabled = inRoom;
            _btnSend.Enabled = inRoom && MpManager.Role == MpRole.Host;
            _btnStart.Enabled = inRoom && MpManager.Role == MpRole.Host;
            _status.Text = MpManager.Role switch
            {
                MpRole.Host => "你是房主 · 端口 " + MpManager.Port + " · IP " + MpManager.LocalIps(),
                MpRole.Client => "你已加入房间（等待房主开始）",
                _ => "未联机"
            };
            // 外部（如设置页保存玩家名）修改后，名称框即时同步（不打断正在输入）
            if (!_name.Focused && _name.Text != MpManager.PlayerName) _name.Text = MpManager.PlayerName;
            RefreshLeaderboard();
        }

        void SendChartAsHost()
        {
            using var dlg = new OpenFileDialog { Filter = "谱面|*.osu;*.mc;*.sm;*.ssc;*.qua|osu!|*.osu|Malody|*.mc|SM/Etterna|*.sm;*.ssc|Quaver|*.qua" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var chart = ChartParser.ParseFile(dlg.FileName);
                if (!ModeSystem.IsAvailable(chart.Mode))
                {
                    MessageBox.Show("「" + ModeSystem.DisplayName(chart.Mode) + "」玩法已移除，无法游玩。", "玩法已移除");
                    return;
                }
                MpManager.CurrentChart = chart;
                MpManager.SendChart(Path.GetFileName(dlg.FileName), File.ReadAllText(dlg.FileName));
                _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] 📤 已发送谱面：" + chart.Title + "\r\n");
            }
            catch (Exception ex) { MessageBox.Show("解析失败：" + ex.Message, "错误"); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_subscribed)
            {
                _subscribed = false;
                MpManager.Log -= OnLog;
                MpManager.OnRoster -= OnRoster;
                MpManager.OnHit -= OnScores;
                MpManager.OnFinish -= OnScores;
                MpManager.OnError -= OnLog;
            }
            base.OnFormClosed(e);
        }
    }
}
