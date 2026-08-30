using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ChartPlayer
{
    public class MainForm : Form
    {
        readonly GamePanel _game = new GamePanel();
        Panel _settingsView;
        SettingsPanel _settings;
        Panel _mpView;
        MpEmbedPanel _mp;
        Panel _editorView;
        ChartEditorPanel _editor;
        AppConfig _cfg;
        PlayerProfile _profile;
        bool _testPlayReturn;   // 编辑器试玩中：退出游玩时返回编辑器而非主菜单

        /// <summary>legacy 返回路径统一入口（t53 批1）：停止游玩/预览状态并回引擎壳主菜单（原 ShowMainMenu 直连 ShowEngineShell）。</summary>
        void ReturnToMenu() { _testPlayReturn = false; try { _game.StopToMenu(); } catch { } ShowEngineShell(); }

        public MainForm()
        {
            Text = "Milestone · 里程碑";
            // 窗口尺寸 = 物理屏幕大小：本应用 PerMonitorV2 感知下 WinForms 窗口的物理尺寸 = Size 设置值，
            // 若按设计尺寸(2560×1463)最大化，窗口会超出屏幕（屏幕只显示左上角，画布 fit 后底部留空白）。
            try
            {
                var b = Screen.PrimaryScreen.Bounds;
                Location = b.Location;
                Size = b.Size;
                WindowState = FormWindowState.Normal;
            }
            catch { WindowState = FormWindowState.Maximized; }
            MinimumSize = new Size(960, 640);
            Font = new Font("Microsoft YaHei UI", 9F);

            Logger.Info("程序启动，日志目录：" + Logger.LogDir);
            _cfg = AppConfig.Load();
            FpsGovernor.PersistAction = r => { try { _cfg.RefreshRate = r; _cfg.Save(); } catch { } };
            if (_cfg.RefreshRate.HasValue) FpsGovernor.Apply(_cfg.RefreshRate.Value);   // 启动恢复刷新率挡位
            _cfg.Apply();
            _profile = PlayerData.LoadProfile();
            AiEngine.LoadTrained();   // 加载 AI 训练校准参数（刚好过段的段位表现）
            Logger.Info("配置加载完成，曲库目录：" + _cfg.ChartsFolder);

            _game.Dock = DockStyle.Fill;   // GamePanel 占满整个客户区（画布纵横比与窗口一致，fit 后 100% 填满）
            BuildSettingsView();
            BuildMpView();
            BuildEditorView();

            Controls.Add(_game);
            Controls.Add(_mpView);
            Controls.Add(_settingsView);
            Controls.Add(_editorView);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F12)
                {
                    // 屏幕采集：整个主窗口截图保存到 屏幕采集 目录（支持游玩/编辑器任意界面）
                    try { ScreenCapture.SaveWindow(Handle); } catch (Exception ex) { Logger.Error("屏幕采集失败", ex); }
                    e.Handled = true; e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    if (_settingsView.Visible || _mpView.Visible || _editorView.Visible)
                    { ReturnToMenu(); e.Handled = true; e.SuppressKeyPress = true; return; }
                }
                // 关键修复：只要有任何菜单/设置/选歌界面可见，就绝不抢焦点，
                // 保证玩家名、延迟等文本框可以连续输入（含负号）。
                bool overlay = _settingsView.Visible || _mpView.Visible || _editorView.Visible;
                if (!overlay) _game.Focus();
            };

            MpLeaderboard.Load();
            WireMp();
            _game.ExitToMenu += () =>
            {
                GpuGuard.EndAiSession(); try { _game.RestartRenderer(); } catch { }   // 离开游玩：恢复常态硬件 GPU
                Logger.Info("ESC 退出游玩，返回" + (_testPlayReturn ? "编辑器" : "主菜单"));
                if (_testPlayReturn)
                {
                    _testPlayReturn = false;
                    double nowMs = _game.CurrentMs;   // t14 P1-1：预览退出回写播放头
                    if (_engineShell != null && !_engineShell.IsDisposed)
                    {
                        // t50：试玩退出回编辑器——引擎窗同窗承载（Unhost 游戏 → Host 编辑器）
                        _engineShell.UnhostContent();
                        _engineShell.HostContent(_editor);
                        _editor.SyncTimeFromPreview(nowMs);
                    }
                    else { ShowEditor(); _editor.SyncTimeFromPreview(nowMs); }
                }
                else
                {
                    // t48：按壳可见性判路径（t53 批1：legacy 分支已删）——壳 Visible=游玩承载在壳内(t50)→UnhostContent 回壳 UI 当前页；
                    // 壳 Hidden=游玩承载在主窗体(Act 路径: 回环作曲/布局编辑器等)→ShowEngineShell()(Reopen 回引擎菜单)；
                    // 修正 t47 误区：壳隐藏时 UnhostContent 空 return（_hostedContent==null）不恢复场景 → 壳 Show 后黑屏
                    if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.Visible)
                        _engineShell.UnhostContent();
                    else ShowEngineShell();
                }
            };
            // AI 演示按 ESC 退出：来自编辑器（t5 AI 游玩）→ 回编辑器；否则到选歌界面
            _game.ExitToLibrary += () =>
            {
                if (_testPlayReturn)
                {
                    _testPlayReturn = false;
                    double nowMs = _game.CurrentMs;   // t14 P1-1：预览退出回写播放头
                    Logger.Info("AI 演示 ESC 退出，返回编辑器");
                    if (_engineShell != null && !_engineShell.IsDisposed)
                    {
                        _engineShell.UnhostContent();
                        _engineShell.HostContent(_editor);
                        _editor.SyncTimeFromPreview(nowMs);
                    }
                    else { ShowEditor(); _editor.SyncTimeFromPreview(nowMs); }
                    return;
                }
                GpuGuard.EndAiSession(); try { _game.RestartRenderer(); } catch { }   // AI 演示退出：恢复常态硬件 GPU
                Logger.Info("AI 演示 ESC 退出，返回选歌");
                // t45：与 ExitToMenu 同口径——引擎模式下回引擎壳（不再跳 legacy 选歌页）；
                // t48：按壳可见性判路径（同 ExitToMenu）——壳 Visible→UnhostContent 回壳 UI；
                // 壳 Hidden（Act 路径: AI 演示/联机等）→ShowEngineShell()(Reopen)——修正 t47 的 UnhostContent 空 return 致壳黑屏
                if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.Visible)
                    _engineShell.UnhostContent();
                else ShowEngineShell();
            };

            _game.SongEnded += r =>
            {
                if (_game.IsAiDemo)
                {
                    Logger.Info("AI 演示完成：" + r.Title + " | " + r.Grade + " | ACC " + r.Acc.ToString("0.00") + "% | 得分 " + r.Score);
                    GpuGuard.EndAiSession(); try { _game.RestartRenderer(); } catch { }   // AI 演示结束：恢复常态硬件 GPU
                    if (_testPlayReturn)
                    {
                        // t5：编辑器 AI 游玩自然结束 → 回编辑器（不弹主菜单对话框）
                        _testPlayReturn = false;
                        double nowMs = _game.CurrentMs;   // t14 P1-1：自然结束≈谱面末，回写播放头
                        if (_engineShell != null && !_engineShell.IsDisposed)
                        {
                            _engineShell.UnhostContent();
                            _engineShell.HostContent(_editor);
                            _editor.SyncTimeFromPreview(nowMs);
                        }
                        else { ShowEditor(); _editor.SyncTimeFromPreview(nowMs); }
                        return;
                    }
                    ReturnToMenu();   // t53 批1：legacy 返回路径统一回引擎壳主菜单
                    MessageBox.Show("🤖 AI 演示完成  评级：" + r.Grade +
                        "\n\n得分：" + r.Score + "\nACC：" + r.Acc.ToString("0.00") + "%\n最大连击：" + r.MaxCombo +
                        "\n\n" + HitsText(r), "AI 演示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (_game.MpActive)
                {
                    Logger.Info("联机对局结束：" + r.Title + " | " + r.Grade + " | ACC " + r.Acc.ToString("0.00") + "% | 得分 " + r.Score);
                    GpuGuard.EndAiSession(); try { _game.RestartRenderer(); } catch { }   // 联机结束：恢复常态硬件 GPU
                    MpLeaderboard.RecordPlayer(MpManager.PlayerName, r.Score, r.Acc, r.MaxCombo, r.Title);
                    string rows = "你：" + r.Score + " 分 · " + r.Acc.ToString("0.00") + "%\n";
                    foreach (var p in _game.MpScores.Values)
                        rows += p.Name + (p.Name == _game.MpSelfName ? "（我）" : "") + "：" + p.Score + " 分 · " + p.Acc.ToString("0.00") + "%\n";
                    // 联机对局结束后，陪玩 AI 像普通对局一样展示结果，并把 AI 成绩并入本地联机排行
                    string aiScore = "";
                    foreach (var a in _game.CompanionAis)
                    {
                        aiScore += "\n🤖 " + a.Name + "：" + (int)a.Score + " 分 · ACC " + a.Acc.ToString("0.00") + "% · 连击 " + a.MaxCombo;
                        MpLeaderboard.RecordPlayer(a.Name, (int)a.Score, a.Acc, a.MaxCombo, r.Title);
                    }
                    ReturnToMenu();   // t53 批1：游玩结束统一回引擎壳主菜单
                    MessageBox.Show("联机对局结束\n\n" + rows + (aiScore.Length > 0 ? "\n\n【陪玩 AI】" + aiScore : ""),
                        "联机结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    MpManager.Leave();
                    return;
                }
                // 普通游玩：GamePanel 内部已显示结算画面，这里只记录日志，不弹窗、不切回主菜单
                Logger.Info("歌曲结束：" + r.Title + " | " + r.Grade + " | ACC " + r.Acc.ToString("0.00") + "% | 得分 " + r.Score + " | 连击 " + r.MaxCombo);
            };
            _game.Skin = SkinSettings.LoadWithPlayerFirst();
            Logger.Info("玩家：" + _profile.Name + " · 就绪");

            // ===== 开发自检参数：Milestone.exe --autoplay "谱面路径" 或 --editor "谱面路径" =====
            try
            {
                string[] cargs = Environment.GetCommandLineArgs();
                Logger.Info("启动参数：" + string.Join(" | ", cargs));
                for (int i = 1; i < cargs.Length; i++)
                {
                    if (cargs[i] == "--autoplay" && File.Exists(cargs[i + 1]))
                        _devAutoplayPath = Path.GetFullPath(cargs[i + 1]);
                    else if (cargs[i] == "--play" && File.Exists(cargs[i + 1]))
                        _devPlayPath = Path.GetFullPath(cargs[i + 1]);
                    else if (cargs[i] == "--humantest" && File.Exists(cargs[i + 1]))
                        _devHumanPath = Path.GetFullPath(cargs[i + 1]);
                    else if (cargs[i] == "--autoshot" && cargs[i + 1].Length > 0)
                        GamePanel.AutoShotDir = Path.GetFullPath(cargs[i + 1]);   // 游玩中自动截图(还原度对比)
                    else if (cargs[i] == "--adofai2")
                        _devAdofaiReal = true;
                    else if (cargs[i] == "--uidebug")
                        _devUiDebug = true;
                    else if (cargs[i] == "--legacysettings")
                        _devUiSettings = true;
                    else if (cargs[i] == "--editor" && File.Exists(cargs[i + 1]))
                        _devEditorPath = Path.GetFullPath(cargs[i + 1]);
                    else if (cargs[i] == "--legacyui")
                        _legacyUi = true;
                    else if (cargs[i] == "--engineui")
                        _engineUi = true;   // 启动进入引擎壳（默认旧 UI）
                }
            }
            catch (Exception ex) { Logger.Error("开发参数解析失败", ex); }
            // t53 批1：--legacyui 参数保留解析（字段删除推迟批4），旧主菜单 UI 已删——按默认引擎 UI 启动
        }

        string _devAutoplayPath, _devPlayPath, _devHumanPath, _devEditorPath;
        bool _devUiDebug, _devUiSettings, _devAdofaiReal;
        bool _legacyUi;                 // --legacyui（兼容保留）：显式回退旧 WinForms 主菜单
        bool _engineUi;                  // --engineui：启动进入引擎壳（默认=旧 UI，用户 2026-08-26 决定）
        EngineMainShell _engineShell;   // 引擎驱动 UI 外壳（t11）
        /// <summary>CLI 截图/工具入口（--menushot 等）强制走旧主菜单，避免壳窗口干扰截图。</summary>
        public static bool CliLegacy;

        /// <summary>加载谱面：--adofai2 时 .adofai 文件走真实 ADOFAI 模式（Routlock=默认）。</summary>
        Chart LoadChartForPlay(string p)
        {
            if (_devAdofaiReal && (p.ToLowerInvariant().EndsWith(".adofai") || p.ToLowerInvariant().EndsWith(".json")))
                return ChartParser.ParseAdofaiReal(System.IO.File.ReadAllText(p), p);
            return ChartParser.ParseFile(p);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            bool devCliRun = _devUiDebug || !string.IsNullOrEmpty(_devAutoplayPath) || !string.IsNullOrEmpty(_devPlayPath)
                          || !string.IsNullOrEmpty(_devHumanPath) || !string.IsNullOrEmpty(_devEditorPath);
            if (_devUiDebug)
            {
                _devUiDebug = false;
                Logger.Info("--uidebug 已弃用（t53 批2：legacy 选歌页已删）");
            }
            if (_devUiSettings)
            {
                _devUiSettings = false;
                ShowSettings(0);
                return;
            }
            if (!string.IsNullOrEmpty(_devAutoplayPath))
            {
                string p = _devAutoplayPath; _devAutoplayPath = null;
                StartAutoplayFromPath(p, false);
            }
            else if (!string.IsNullOrEmpty(_devPlayPath))
            {
                string p = _devPlayPath; _devPlayPath = null;
                try
                {
                    var chart = LoadChartForPlay(p);
                    _game.SelectPart(0);   // CLI 自动化：直接玩第 0 部件（多模式谱面）
                    _game.LoadAndPlay(chart, Path.GetDirectoryName(p));
                    Logger.Info("▶ 游玩：" + chart.Title);
                    HideAllMenus();
                }
                catch (Exception ex) { Logger.Error("游玩启动失败：" + p, ex); }
            }
            else if (!string.IsNullOrEmpty(_devHumanPath))
            {
                string p = _devHumanPath; _devHumanPath = null;
                try
                {
                    var chart = LoadChartForPlay(p);
                    _game.SelectPart(0);   // CLI 自动化：直接玩第 0 部件（多模式谱面）
                    _game.StartHumanTest(chart, Path.GetDirectoryName(p));
                    Logger.Info("🧑 人类模拟：" + chart.Title);
                    HideAllMenus();
                }
                catch (Exception ex) { Logger.Error("人类模拟启动失败：" + p, ex); }
            }
            else if (!string.IsNullOrEmpty(_devEditorPath))
            {
                string p = _devEditorPath; _devEditorPath = null;
                EditChart(p);
            }
            // t11：默认使用引擎驱动 UI 外壳（t53 批1：--legacyui 旧主菜单已删，仅作为兼容占位参数继续解析；
            // 开发者参数 --autoplay/--play/--humantest/--editor/--uidebug/--legacysettings 仍走本窗体直接路径）
            if ((!_legacyUi || _engineUi) && !CliLegacy && !devCliRun) ShowEngineShell();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.Enable(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Logger.Info("程序退出");
            _cfg.Capture();
            _cfg.Save();
            base.OnFormClosed(e);
        }

        void RunUi(Action a) { if (IsDisposed) return; if (IsHandleCreated && InvokeRequired) BeginInvoke(a); else a(); }

        /// <summary>切到指定视图（t53 批1：引擎壳场景转场替代 legacy 光束转场——直接显隐）。</summary>
        void ShowView(Panel v, Action extra = null)
        {
            v.Visible = true;
            HideOthers(v);
            v.BringToFront();
            extra?.Invoke();
        }

        static string HitsText(GameResult r)
        {
            string s = "";
            foreach (var kv in r.Hits) s += kv.Key + " " + kv.Value + "  ";
            return s.Trim();
        }

        void WireMp()
        {
            MpManager.Log += s => RunUi(() => Logger.Info(s));
            MpManager.OnRoster += names => RunUi(() =>
            {
                if (_game.MpActive)
                    foreach (var n in names)
                        if (!_game.MpScores.ContainsKey(n)) _game.MpScores[n] = new MpPlayerState { Name = n };
            });
            MpManager.OnChart += (name, text) => RunUi(() =>
            {
                try { MpManager.CurrentChart = ParseChartText(name, text); Logger.Info("🎵 已收到房主谱面：" + name); Logger.Info("收到房主谱面：" + name); }
                catch (Exception ex) { Logger.Info("⚠ 谱面解析失败：" + ex.Message); Logger.Error("收到谱面解析失败：" + name, ex); }
            });
            MpManager.OnStart += () => RunUi(() =>
            {
                try { if (_mp != null) _mp.EnsureAlive(); } catch { }
                if (MpManager.CurrentChart != null)
                {
                    _testPlayReturn = false;
                    _game.MpSelfName = MpManager.PlayerName;
                    if (GpuGuard.BeginAiSession()) _game.RestartRenderer();   // AI 陪玩会话：AI 服务占显存时本会话临时软件渲染
                    _game.StartMpPlay(MpManager.CurrentChart);
                    Logger.Info("开始联机游玩：" + MpManager.CurrentChart.Title);
                    HideAllMenus();
                }
                else MessageBox.Show("尚未收到谱面，无法开始", "联机");
            });
            MpManager.OnHit += st => RunUi(() => _game.MpScores[st.Name] = st);
            MpManager.OnFinish += st => RunUi(() =>
            {
                _game.MpScores[st.Name] = st;
                if (_game.MpActive && MpManager.CurrentChart != null)
                    MpLeaderboard.RecordPlayer(st.Name, st.Score, st.Acc, st.Combo, MpManager.CurrentChart.Title);
            });
            MpManager.OnError += s => RunUi(() => { Logger.Info("⚠ " + s); Logger.Warn(s); });
        }

        static Chart ParseChartText(string name, string text)
            => ChartParser.ParseText(text, name);

        /* ---------- 主菜单 ---------- */
        /// <summary>创新玩法：回环作曲——新建空谱（BPM=120），编玩一体化（录→奏→扩），无预置谱面。</summary>
        void StartLoopComposer()
        {
            HideAllMenus();
            var chart = new Chart
            {
                Title = "回环作曲",
                Artist = "（你的演奏）",
                Version = "LIVE",
                Mode = GameMode.LoopComposer,
                KeyCount = 4,
                Bpm = 120,
                ModeName = ModeSystem.DisplayName(GameMode.LoopComposer),
                Notes = new List<Note>(),
                Events = new List<ChartEvent>()
            };
            try
            {
                _game.LoadAndPlay(chart, "");
                Logger.Info("🎵 回环作曲：录（D F J K / 点环写谱）→ 奏（实时演奏）→ 扩（3 满分解锁）");
                Logger.Info("回环作曲开始（内存运行，不落盘）");
            }
            catch (Exception ex) { Logger.Error("回环作曲启动失败", ex); MessageBox.Show("启动失败：" + ex.Message, "回环作曲"); }
        }

        /// <summary>布局编辑器：无谱面时自动创建空白游玩界面供拖拽调整，有谱面时显示真实布局。</summary>
        void EnterLayoutEditor()
        {
            HideAllMenus();
            _game.EnterLayoutEdit();
            Logger.Info("📐 布局编辑器：拖动各元素到想要的位置 · ESC 保存并退出");
        }

        static string MenuStatsText()
        {
            var p = PlayerData.Load();
            return "🎮 游玩 " + p.Stats.Plays + " 次 · 累计命中 " + p.Stats.NotesHit +
                " 音符 · 🏆 最高ACC " + p.Stats.MaxAcc.ToString("0.00") + "% · 最佳连击 " + p.Stats.BestCombo + "x";
        }

        void AboutMilestone()
        {
            MessageBox.Show("Milestone（里程碑）v6.0\n\n全音游模式：osu!mania / Malody / Phigros / Arcaea / Cytus / osu!standard / taiko / catch / ADOFAI / IIDX\n\n空格=Pause · R=重开 · A=自动 · S=跳过空白 · M=切换模式部件 · ESC=退出\nF1=显示/隐藏菜单栏", "关于");
        }

        /// <summary>主题切换：循环 UiTheme 0→1→2，应用配色并刷新当前界面。</summary>
        void CycleTheme()
        {
            GameSettings.UiTheme = (GameSettings.UiTheme + 1) % 3;
            ApplyThemeAndRefresh();
        }

        void ApplyThemeAndRefresh()
        {
            UiColors.ApplyTheme(GameSettings.UiTheme);
            // 延迟到当前事件处理返回后再重建视图，避免在控件事件处理中销毁其父容器
            if (IsHandleCreated && !IsDisposed)
            {
                try { BeginInvoke((Action)ApplyThemeRefreshCore); return; }
                catch { }
            }
            ApplyThemeRefreshCore();
        }

        void ApplyThemeRefreshCore()
        {
            RebuildSettings();
            ReturnToMenu();   // t53 批1：主题刷新后回引擎壳（legacy 主菜单已删）
        }

        /// <summary>重建视图：移除旧面板 → 构建新面板 → 加入并隐藏（主题切换刷新用）。</summary>
        void RebuildView(ref Panel view, Action build)
        {
            if (view != null) { Controls.Remove(view); view.Dispose(); view = null; }
            build();
            view.Visible = false;
        }

        void RebuildSettings()
        {
            RebuildView(ref _settingsView, BuildSettingsView);
        }

        /// <summary>✨ 引擎 UI 演示：旧菜单入口改开引擎壳（新深色 UI；与旧演示窗不再是两套界面）。</summary>
        void ShowEngineUiDemo()
        {
            ShowEngineShell();
            return;
        }
        /// <summary>F11 全屏：引擎壳可见时切换壳；否则切换主窗体（无边框最大化）。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.Visible) _engineShell.ToggleFullscreen();
                else ToggleLegacyFullscreen();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void ToggleLegacyFullscreen()
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
            }
        }

        /// <summary>引擎驱动 UI 外壳（t11）：场景 UI 重建主菜单，文字全部来自 UiText（逐字不改）。</summary>
        void ShowEngineShell()
        {
            // 已有外壳（隐藏中）：重建场景页并重新显示（不重复实例化）
            if (_engineShell != null && !_engineShell.IsDisposed)
            {
                try { _engineShell.Reopen(); this.Hide(); return; } catch { }
            }
            try
            {
                // 动作前先隐藏外壳窗口（业务视图显示在 MainForm 上；返回主菜单时重开外壳）
                void Act(Action legacy) { try { _engineShell?.Hide(); } catch { } this.Show(); if (_engineShell != null) { Size = _engineShell.Size; StartPosition = FormStartPosition.Manual; Location = _engineShell.Location; } legacy(); }
                var actions = new Dictionary<string, Action>(StringComparer.Ordinal)
                {
                    [UiText.MenuEditChart] = () =>
                    {
                        // t50：编辑器在引擎窗内打开（同窗承载；不切回 MainForm，可见窗口数=1）
                        if (_engineShell != null && !_engineShell.IsDisposed)
                        {
                            if (!_engineShell.Visible) _engineShell.Show();
                            _engineShell.HostContent(_editor);
                            return;
                        }
                        Act(ShowEditor);
                    },
                    [UiText.MenuDanChallenge] = () => { _engineShell?.GoToPage("dan"); },      // t49：引擎段位页（同分组构建器）
                    [UiText.MenuMp] = () => Act(ShowMp),
                    [UiText.MenuReplay] = () => { _engineShell?.GoToPage("replay"); },   // t49：引擎回放页（同播放逻辑）
                    [UiText.MenuFolder] = () => { _engineShell?.GoToPage("folder"); },   // t49：引擎曲库页（同 FolderPanel 实现）
                    [UiText.MenuSettings] = () => Act(() => ShowSettings(0)),
                    [UiText.MenuCalibration] = () => { _engineShell?.GoToPage("calibration"); },   // t49：引擎校准页（同 GameSettings.Offset）
                    [UiText.MenuAiDemo] = () => Act(AiDemoPickChart),
                    [UiText.MenuLayoutEditor] = () => Act(EnterLayoutEditor),
                    [UiText.MenuPlayerInfo] = () => { _engineShell?.GoToPage("player"); },   // t49：引擎玩家信息页（同 PlayerData）
                    [UiText.MenuMyData] = () => { _engineShell?.GoToPage("mydata"); },       // t49：引擎我的数据页（同 PlayerData.History）
                    [UiText.MenuOpenLog] = () => Act(() => { Logger.Info("用户打开日志文件"); Logger.OpenLog(); }),
                    [UiText.MenuTheme] = () => { CycleTheme(); },   // t60：主题切换引擎内进行（不再 Act 显示旧主窗体）
                    [UiText.MenuAbout] = () => { _engineShell?.GoToPage("about"); },         // t49：引擎关于页（同 UiText.AboutText）
                    [UiText.MenuExit] = () => Application.Exit(),
                    [UiText.MenuLoopComposer] = () => Act(StartLoopComposer),
                    [UiText.MenuMiniMania] = () => Act(() => { var f = new MiniManiaForm(); f.FormClosed += (s2, e2) => ReturnToMenu(); f.Show(this); }),   // t53 批1：迷你 mania 关闭后回引擎菜单
                    [UiText.MenuEngineUiDemo] = () => Act(ShowEngineUiDemo),
                };
                _engineShell = new EngineMainShell(actions, MenuStatsText, (chart, dir) =>
                {
                    if (!PickPart(chart)) return false;
                    _game.LoadAndPlay(chart, dir);
                    if (_engineShell != null && !_engineShell.IsDisposed)
                    {
                        // t50：游玩承载进引擎窗（同窗，可见窗口数=1；主窗体保持隐藏）
                        _engineShell.HostContent(_game);
                    }
                    else
                    {
                        this.Show();           // 游玩开始：显示主窗体承载游戏画面
                        HideAllMenus();
                    }
                    Logger.Info("▶ " + chart.Title + " · " + chart.KeyCount + "K · " + chart.ModeName);
                    return true;
                },
                (chart, dir) =>
                {
                    // t31：✏ 编辑此谱面
                    if (chart == null || string.IsNullOrEmpty(chart.SourcePath)) return false;
                    EditChart(chart.SourcePath);
                    return true;
                },
                () => _game);   // t48：设置引擎页访问游戏/皮肤实例（与 SettingsPanel 同字段）
                _engineShell.SecondaryAction = DispatchSecondary;   // t49：次级页回调（段位/回放/玩家信息/皮肤 → 同一 legacy 实现）
                _engineShell.Show();
                this.Hide();                    // 单窗口体验：外壳打开时隐藏空主窗体
                EngineHosting.LogWindowState("engine-shell show");   // t50：窗口枚举验证——此时可见窗口数应=1（引擎窗）
                HideAllMenus();
                var shell = _engineShell;
                // 关闭外壳：游玩已启动（IsLoaded）→ 不打断游戏；否则回退旧 WinForms 主菜单
                shell.FormClosed += (s, e) =>
                {
                    // t60：关闭引擎窗 = 退出应用（用户要求：绝不回退到未引擎化的旧 WinForms 主界面）
                    if (ReferenceEquals(_engineShell, shell)) _engineShell = null;
                    try { Application.Exit(); } catch { }
                };
            }
            catch (Exception ex) { Logger.Error("引擎 UI 外壳打开失败（纯游玩兜底：无菜单）", ex); try { this.Show(); } catch { } }
        }

        /// <summary>段位挑战：扫描曲库中 IsDan 谱面，按 DanSet 分组选择后开始游玩。
        /// 分组构建与引擎段位页共用 DanSelectDialog.BuildGroups（同输入同结果）；启动共用 StartDanChart。</summary>
        void OpenDanChallenge()
        {
            var list = DanSelectDialog.BuildGroups(_cfg.ChartsFolder);
            if (list.Count == 0)
            {
                MessageBox.Show("未检测到段位谱面（将含 Dan 字样的谱面放入曲库目录即可）", "段位挑战");
                return;
            }
            using var dlg = new DanSelectDialog(list);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected != null)
                StartDanChart(dlg.Selected);
        }

        /// <summary>段位谱启动（legacy 对话框与引擎段位页共用同一实现）。</summary>
        void StartDanChart(Chart c)
        {
            try
            {
                string dir = Path.GetDirectoryName(c.SourcePath) ?? "";
                _game.LoadAndPlay(c, dir);   // HP 段位模式由 GamePanel 依据 IsDan 自动启用
                if (_engineShell != null && !_engineShell.IsDisposed) _engineShell.HostContent(_game);   // t50：同窗承载
                else { this.Show(); HideAllMenus(); }
                Logger.Info("🏆 段位挑战：" + c.Title + (string.IsNullOrEmpty(c.DanName) ? "" : " · " + c.DanName));
                Logger.Info("段位挑战开始：" + c.Title + " | " + c.DanName);
            }
            catch (Exception ex) { Logger.Error("段位挑战启动失败：" + c.SourcePath, ex); MessageBox.Show("启动失败：" + ex.Message, "段位挑战"); }
        }

        /// <summary>在编辑器打开当前谱面（选歌界面「编辑此谱面」入口）。</summary>
        void EditChart(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { _editor.LoadChart(path); }
            catch (Exception ex) { Logger.Error("编辑器打开谱面失败：" + path, ex); MessageBox.Show("打开谱面失败：" + ex.Message, "谱面编辑器"); }
            if (_engineShell != null && !_engineShell.IsDisposed)
            {
                // t50：选歌「✏ 编辑此谱面」→ 引擎窗同窗承载（可见窗口数=1）
                if (!_engineShell.Visible) _engineShell.Show();
                _engineShell.HostContent(_editor);
                return;
            }
            ShowEditor();
        }

        void StartAutoplayFromPath(string path, bool pickPart = true)
        {
            if (string.IsNullOrEmpty(path)) return;
            _testPlayReturn = false;
            try
            {
                var chart = ChartParser.ParseFile(path);
                if (pickPart) { if (!PickPart(chart)) return; }
                else _game.SelectPart(0);   // CLI 自动化：直接玩第 0 部件
                _game.StartAutoplay(chart, Path.GetDirectoryName(path));
                Logger.Info("🎯 自动游玩：" + chart.Title);
                Logger.Info("自动游玩：" + path);
                HideAllMenus();
            }
            catch (Exception ex) { Logger.Error("自动游玩失败：" + path, ex); MessageBox.Show("解析失败：" + ex.Message, "错误"); }
        }

        void AiDemoFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _testPlayReturn = false;
            try
            {
                var chart = ChartParser.ParseFile(path);
                if (!PickPart(chart)) return;
                var lv = AiSetupForm.PickLevel(this);
                if (lv == null) { ReturnToMenu(); return; }   // 取消：返回引擎菜单，避免卡在空白画面
                if (GpuGuard.BeginAiSession()) _game.RestartRenderer();   // AI 演示会话：AI 服务占显存时本会话临时软件渲染
                _game.StartAiDemo(chart, Path.GetDirectoryName(path), lv);
                Logger.Info("🤖 AI 演示：" + chart.Title + "（" + lv.Name + "）");
                Logger.Info("AI 演示开始：" + chart.Title + " | " + lv.Name);
                HideAllMenus();
            }
            catch (Exception ex) { Logger.Error("AI 演示失败：" + path, ex); MessageBox.Show("解析失败：" + ex.Message, "错误"); }
        }

        /* ---------- 设置 ---------- */
        void BuildSettingsView()
        {
            _settingsView = new Panel { Dock = DockStyle.Fill, BackColor = UiColors.Bg };
            _settings = new SettingsPanel(_game, s => Logger.Info("[设置] " + s));   // t53 批1：状态栏已删——设置状态文本走 Logger
            // 布局编辑器：隐藏所有菜单并切到游戏画面
            _settings.RequestLayoutEdit += () => { HideAllMenus(); _game.EnterLayoutEdit(); };
            // 每个设置页顶部的“返回主菜单”
            _settings.GoHome += () => ReturnToMenu();   // t53 批1
            // UI 主题切换后刷新各视图配色
            _settings.ThemeChanged += () => ApplyThemeAndRefresh();
            _settingsView.Controls.Add(_settings);
        }
        void ShowSettings(int tabIndex)
        {
            _settings.RefreshAll();
            if (tabIndex >= 0 && tabIndex < _settings.GetTabCount()) _settings.SelectTab(tabIndex);
            ShowView(_settingsView);
        }

        /* ---------- 联机（内嵌） ---------- */
        void BuildMpView()
        {
            _mpView = new Panel { Dock = DockStyle.Fill, BackColor = UiColors.Bg };
            _mp = new MpEmbedPanel(_game);
            _mp.GoHome += () => ReturnToMenu();   // t53 批1
            _mpView.Controls.Add(_mp);
        }
        void ShowMp()
        {
            _mp.EnsureAlive();
            ShowView(_mpView);
        }

        /* ---------- 谱面编辑器（内嵌） ---------- */
        void BuildEditorView()
        {
            _editorView = new Panel { Dock = DockStyle.Fill, BackColor = UiColors.Bg };
            _editor = new ChartEditorPanel();
            _editor.GoHome += () =>
            {
                if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.ContentHosted)
                {
                    _engineShell.UnhostContent();   // t50：编辑器回首页=回到引擎窗 UI（主窗体保持隐藏）
                    if (_editor.Parent == null) _editorView.Controls.Add(_editor);   // 归还 legacy 面板（下次 legacy 打开可用）
                }
                else ReturnToMenu();   // t53 批1：编辑器回首页=回引擎菜单
            };
            _editor.TestPlay += (chart, dir) =>
            {
                try
                {
                    _testPlayReturn = true;   // 试玩退出后回到编辑器
                    _game.LoadAndPlay(chart, dir ?? "");
                    Logger.Info("▶ 试玩：" + chart.Title + " · ESC 返回编辑器");
                    Logger.Info("编辑器试玩：" + chart.Title);
                    if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.ContentHosted)
                    {
                        // t50：编辑器试玩同窗承载（编辑器页让位 → 游玩页；ESC 后 ExitToMenu 回编辑器）
                        _engineShell.UnhostContent();
                        _engineShell.HostContent(_game);
                    }
                    else HideAllMenus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("试玩失败：" + ex.Message, "谱面编辑器");
                }
            };
            // ===== t5：编辑器自动游玩（GamePanel.StartAutoplay 同窗承载，所见即所玩） =====
            _editor.TestAutoplay += (chart, dir, startMs) =>
            {
                try
                {
                    _testPlayReturn = true;   // 预览退出后回到编辑器
                    _game.StartAutoplayAt(chart, dir ?? "", startMs);   // t14 P1-1：从当前播放头开始
                    if (chart.Notes != null && chart.Notes.Count > 2000)
                        _game.ShowToast("⚠ 大谱面演示可能降帧，建议关闭 3D（V）");   // editor-trilab §2.5.4（captain 放行）
                    Logger.Info("🅰 自动游玩：" + chart.Title + " · ESC 返回编辑器");
                    Logger.Info("编辑器自动游玩：" + chart.Title);
                    if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.ContentHosted)
                    {
                        // t50 同款：编辑器页让位 → 游玩页；ESC 后 ExitToMenu 回编辑器
                        _engineShell.UnhostContent();
                        _engineShell.HostContent(_game);
                    }
                    else HideAllMenus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("自动游玩失败：" + ex.Message, "谱面编辑器");
                }
            };
            // ===== t5：编辑器 AI 游玩（三档规则驱动：0 陪玩=AI 演示 DemoAi / 1~3=陪玩同台 / 差值行对练） =====
            _editor.TestAiPlay += (chart, dir, lv, companions, startMs) =>
            {
                try
                {
                    _testPlayReturn = true;   // 预览退出后回到编辑器
                    if (companions > 0)
                    {
                        _game.LoadAndPlay(chart, dir ?? "");
                        _game.SeekToPublic(startMs);   // t14 P1-1：从当前播放头开始
                        _game.AddCompanionAiMulti(lv, companions);   // t14 压测修复：同等级多陪玩（AddCompanionAi 按等级去重，仅 MpLobby 不同等级场景用）
                        if (chart.Notes != null && chart.Notes.Count > 2000)
                            _game.ShowToast("⚠ 大谱面演示可能降帧，建议关闭 3D（V）");   // editor-trilab §2.5.4（开局后提示，防 ResetState 清除）
                        Logger.Info("🤖 AI 游玩：" + chart.Title + " · 陪玩 " + companions + " · ESC 返回编辑器");
                    }
                    else
                    {
                        _game.StartAiDemo(chart, dir ?? "", lv);
                        _game.SeekToPublic(startMs);   // t14 P1-1：从当前播放头开始
                        if (chart.Notes != null && chart.Notes.Count > 2000)
                            _game.ShowToast("⚠ 大谱面演示可能降帧，建议关闭 3D（V）");   // editor-trilab §2.5.4
                        Logger.Info("🤖 AI 演示：" + chart.Title + " · ESC 返回编辑器");
                    }
                    Logger.Info("编辑器 AI 游玩：" + chart.Title + " · " + (lv != null ? lv.Name : "-") + " · 陪玩 " + companions);
                    if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.ContentHosted)
                    {
                        _engineShell.UnhostContent();
                        _engineShell.HostContent(_game);
                    }
                    else HideAllMenus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("AI 游玩失败：" + ex.Message, "谱面编辑器");
                }
            };
            _editorView.Controls.Add(_editor);
        }
        void ShowEditor()
        {
            _game.StopToMenu();   // 试玩退出回到编辑器：清掉游戏面板残留状态
            if (_engineShell != null && !_engineShell.IsDisposed && _engineShell.Visible)
            {
                // t50：编辑器在引擎窗内打开（隐藏 MainForm 面板不使用；同窗承载，可见窗口数=1）
                _engineShell.HostContent(_editor);
                return;
            }
            ShowView(_editorView);
        }

        void HideOthers(Control keep)
        {
            if (keep != _settingsView) _settingsView.Visible = false;
            if (keep != _mpView) _mpView.Visible = false;
            if (keep != _editorView) _editorView.Visible = false;
        }

        void HideAllMenus()
        {
            _settingsView.Visible = false; _mpView.Visible = false;
            _editorView.Visible = false;
            _game.Visible = true; _game.Focus();
        }

        /* ---------- 加载 ---------- */

        /// <summary>单一谱面多模式：若谱面含多个可玩部件则弹出选择框；返回是否继续游玩。</summary>
        bool PickPart(Chart chart)
        {
            if (chart == null) return false;
            var parts = chart.EffectiveParts();
            var avail = new List<(int idx, ChartPart p)>();
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && ModeSystem.IsAvailable(parts[i].Mode)) avail.Add((i, parts[i]));
            if (avail.Count <= 1) { _game.SelectPart(avail.Count == 1 ? avail[0].idx : 0); return true; }
            using var dlg = new Form
            {
                Text = "选择玩法模式（单一谱面多模式）",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(360, 320),
                BackColor = UiColors.Bg,
                ForeColor = UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 10.5F)
            };
            var lb = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(30, 34, 46),
                ForeColor = Color.White
            };
            foreach (var a in avail)
                lb.Items.Add(a.p.Name + " · " + ModeSystem.DisplayName(a.p.Mode) + " · " + a.p.KeyCount + "K");
            lb.SelectedIndex = 0;
            var ok = new Button
            {
                Text = "开始", Dock = DockStyle.Bottom, Height = 38,
                BackColor = Color.FromArgb(70, 130, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            ok.Click += (s, e) => { _game.SelectPart(avail[Math.Max(0, lb.SelectedIndex)].idx); dlg.DialogResult = DialogResult.OK; };
            var cancel = new Button
            {
                Text = "取消", Dock = DockStyle.Bottom, Height = 32,
                BackColor = Color.FromArgb(45, 50, 62), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            cancel.Click += (s, e) => dlg.DialogResult = DialogResult.Cancel;
            dlg.Controls.Add(lb);
            dlg.Controls.Add(ok);
            dlg.Controls.Add(cancel);
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;
            return dlg.ShowDialog(this) == DialogResult.OK;
        }

        void TryLoad(string path)
        {
            _testPlayReturn = false;
            // 谱面解析放后台线程（多核利用），完成后回主线程开始游玩
            Task.Run(() =>
            {
                try
                {
                    var chart = ChartParser.ParseFile(path);
                    RunUi(() =>
                    {
                        if (chart == null) return;
                        if (!PickPart(chart)) { ReturnToMenu(); return; }   // t53 批1
                        _game.LoadAndPlay(chart, Path.GetDirectoryName(path));
                        Logger.Info("▶ " + chart.Title + " · " + chart.KeyCount + "K · " + chart.ModeName);
                        Logger.Info("加载谱面：" + chart.Title + " | " + chart.KeyCount + "K | " + chart.ModeName + " | " + path);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("谱面解析失败：" + path, ex);
                    RunUi(() =>
                    {
                        MessageBox.Show("解析失败：" + ex.Message, "错误");
                        ReturnToMenu();   // t53 批1
                    });
                }
            });
        }

        void AiDemoPickChart()
        {
            HideAllMenus();
            using var dlg = new OpenFileDialog { Filter = "谱面|*.osu;*.mc;*.sm;*.ssc;*.qua;*.mil;*.aff;*.txt;*.json" };
            if (dlg.ShowDialog(this) != DialogResult.OK) { ReturnToMenu(); return; }   // t53 批1
            AiDemoFromPath(dlg.FileName);
        }

        void PickReplay()
        {
            var files = ReplaySystem.List();
            if (files.Length == 0) { MessageBox.Show("Replay 文件夹暂无回放"); return; }
            using var dlg = new OpenFileDialog
            {
                Filter = "回放|*.json",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replay")
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            StartReplayFromPath(dlg.FileName);
        }

        /// <summary>回放启动（legacy 对话框与引擎回放页共用同一实现）。</summary>
        void StartReplayFromPath(string path)
        {
            var r = ReplaySystem.Load(path);
            if (r == null) { MessageBox.Show("回放文件无效"); return; }
            var pair = FindChartByTitle(r.Title);
            if (pair.Item1 == null) { MessageBox.Show("未找到匹配谱面（请先确认谱面文件存在）"); return; }
            if (!PickPart(pair.Item1)) return;
            _game.PlayReplay(pair.Item1, r, pair.Item2);
            if (_engineShell != null && !_engineShell.IsDisposed) _engineShell.HostContent(_game);   // t50：同窗承载
            Logger.Info("🎬 回放：" + r.Title);
            Logger.Info("开始回放：" + r.Title + " | " + path);
        }

        /// <summary>引擎次级页回调（t49）：段位启动/回放播放/玩家信息编辑/皮肤编辑 → 同一 legacy 实现。</summary>
        bool DispatchSecondary(string key, object payload)
        {
            switch (key)
            {
                case "dan.start":
                    if (payload is Chart c) { StartDanChart(c); return true; }
                    return false;
                case "replay.play":
                    if (payload is string p) { StartReplayFromPath(p); return true; }
                    return false;
                case "player.edit":
                    ActSecondary(() => ShowSettings(7));
                    return true;
                case "skin.edit":
                    ActSecondary(() => ShowSettings(6));
                    return true;
            }
            return false;
        }

        /// <summary>次级页跳回 legacy 视图（与 ShowEngineShell 内 Act 同口径：壳隐藏、主窗体显示、再执行）。</summary>
        void ActSecondary(Action legacy)
        {
            try { _engineShell?.Hide(); } catch { }
            this.Show();
            if (_engineShell != null) { Size = _engineShell.Size; StartPosition = FormStartPosition.Manual; Location = _engineShell.Location; }
            legacy();
        }

        (Chart, string) FindChartByTitle(string title)
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart");
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder, "*.osu"))
                    try
                    {
                        var c = ChartParser.ParseFile(f);
                        if (string.Equals(c.Title, title, StringComparison.Ordinal)) return (c, Path.GetDirectoryName(f));
                    }
                    catch { }
            return (null, null);
        }
    }
}
