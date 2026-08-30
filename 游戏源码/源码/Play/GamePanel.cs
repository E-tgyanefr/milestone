using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChartPlayer
{
    public partial class GamePanel : Panel
    {
        Chart _chart;

        List<Note> _notes = new List<Note>();

        readonly Dictionary<int, (List<(double t, double v)> ks, double[] pref)> _spdFieldCache = new Dictionary<int, (List<(double t, double v)>, double[])>();

        int _kc;

        int _partIdx;

        bool _playing, _paused, _ended;

        readonly JudgementEngine _eng = new JudgementEngine(true);

        bool _multi;

        List<StageRT> _stagesRT = new List<StageRT>();

        readonly Dictionary<Keys, int> _multiDowns = new Dictionary<Keys, int>();

        double _score => _multi ? _stagesRT.Sum(s => s.Eng.Score) : _eng.Score;


        double _acc => _multi ? (_stagesRT.Sum(s => s.Eng.Acc * s.Eng.TotalNotes) / Math.Max(1, _stagesRT.Sum(s => s.Eng.TotalNotes))) : _eng.Acc;


        int _combo => _eng.Combo;


        int _maxCombo => _multi ? _stagesRT.Max(s => s.Eng.MaxCombo) : _eng.MaxCombo;


        Dictionary<string, int> _hits => _multi ? MultiMergedHits() : _eng.Hits;


        double _hp => _eng.Hp;


        int _autoIdx;

        LoopComposer _loop;

        Dictionary<Keys, int> _keyCol = new Dictionary<Keys, int>();

        AudioPlayer _audio = new AudioPlayer();

        Stopwatch _sw = new Stopwatch();

        PlayerData _pdata = PlayerData.Load();

        string _lastJudge = "", _lastDev = "";

        PointF _lastJudgePos = new PointF(-999, -999);

        long _lastJudgeMono = -1;

        readonly Random _rnd = new Random();

        public double NoteThickness = 100;

        public double PlayScale = 1.0;

        bool _rightPanelPref = true;

        public bool ShowRightPanel
        {
            get
            {
                if (!_rightPanelPref)
                    return false;
                var m = _chart?.Mode ?? GameMode.Mania;
                return m == GameMode.Mania || m == GameMode.Iidx;
            }

            set => _rightPanelPref = value;
        }


        double _kps;

        readonly List<double> _kpsPresses = new List<double>();

        readonly List<(double t, double v)> _kpsHistory = new List<(double, double)>();

        readonly List<double> _allDev = new List<double>();

        double _lastKpsSample;

        int _missIdx;

        int _holdIdx;

        readonly List<Note> _holdNotes = new List<Note>();

        struct M3Quad
        {
            public float x1, y1, x2, y2, x3, y3, x4, y4;
            public int argb;
            public double z;
        }


        readonly List<M3Quad> _mania3DQuads = new List<M3Quad>(512);

        readonly List<Note> _heldNotes = new List<Note>();

        readonly float[] _burst = new float[16];

        readonly float[] _pressFlash = new float[16];

        double _shakeAmt;

        float _judgePop, _comboPop;

        Image _bgImage;

        D2DBitmap _bgD2D;

        float _osuCursorX, _osuCursorY;

        bool _osuMouseDown;

        readonly HashSet<Keys> _osuDownKeys = new HashSet<Keys>();

        Note _osuSpinner;

        double _osuSpinProgress;

        bool OsuHolding => _osuMouseDown || _osuDownKeys.Count > 0;


        double _arcRecollection = 100;

        double _arcRecoLastMs = 0;

        int _adofaiDerails;

        bool _resultPhase;

        double _resultStart;

        GameResult _result;

        string _toast = "";

        double _toastUntil;

        readonly List<Particle> _particles = new List<Particle>();

        readonly List<HitRing> _rings = new List<HitRing>();

        readonly Cam3D _cam = new Cam3D();

        double _lastStepMono;

        double _lastFxMono;

        bool _canSkip, _skipped;

        double _firstNoteTime, _skipTarget, _skipOffset;

        static double MonoMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        static readonly Color[] JudgeColors =
        {
            Color.FromArgb(255, 210, 63),
            Color.FromArgb(255, 234, 106),
            Color.FromArgb(110, 242, 160),
            Color.FromArgb(65, 166, 255),
            Color.FromArgb(255, 90, 90),
            Color.FromArgb(201, 167, 255)
        };

        static readonly Color ColTitle = Color.White;

        static readonly Color ColScore = Color.White;

        static readonly Color ColAcc = Color.FromArgb(127, 208, 160);

        static readonly Color ColBpm = Color.FromArgb(157, 180, 232);

        static readonly Color ColKps = Color.FromArgb(127, 208, 160);

        static readonly Color ColNotes = Color.FromArgb(143, 163, 200);

        static readonly Color ColJudge = Color.FromArgb(255, 210, 63);

        static readonly Color ColDev = Color.White;

        IRenderer _d2d;

        public bool Recording = false;

        public bool Replaying = false;

        List<ReplayEvent> _record = new List<ReplayEvent>();

        List<ReplayEvent> _replayEvents;

        int _replayIdx;

        double _replayStart;

        public AiPlayer DemoAi;

        readonly List<AiPlayer> _ais = new List<AiPlayer>();

        public IReadOnlyList<AiPlayer> CompanionAis => _ais;

        public bool IsAiDemo => DemoAi != null;


        public bool MpActive = false;

        public string MpSelfName = "玩家";

        public readonly Dictionary<string, MpPlayerState> MpScores = new Dictionary<string, MpPlayerState>();

        double _lastMpReport = -1;

        public SkinSettings Skin = new SkinSettings();

        public bool EditLayoutMode = false;

        bool _blankLayout;

        string _dragKey = null;

        bool _dragHitline;

        Point _dragOff = Point.Empty;

        static readonly string[] HudKeys =
        {
            "title",
            "score",
            "acc",
            "bpm",
            "kps",
            "notes",
            "combo",
            "judge",
            "dev"
        };

        static readonly string[] EditKeys =
        {
            "title",
            "score",
            "acc",
            "bpm",
            "kps",
            "notes",
            "dev",
            "combo",
            "judge",
            "hitline"
        };

        static readonly string[] EditNames =
        {
            "标题 title",
            "得分 score",
            "ACC acc",
            "BPM bpm",
            "KPS kps",
            "音符 notes",
            "偏差 dev",
            "连击 combo",
            "判定 judge",
            "判定线 hitline"
        };

        int EditPanelW => Ui.P(260);


        Panel _editPanel;

        ComboBox _editSel;

        NumericUpDown _editX, _editY;

        CheckBox _editShow;

        bool _updatingEdit;

        double _fpsFrames, _fpsStart = -1, _frameMs;

        int _fps;

        public double PaintMs;

        double _lastIdlePaint = -1;

        Font _editFont = new Font("Microsoft YaHei UI", 12F);

        public event Action<GameResult> SongEnded;

        public event Action ExitToMenu;

        public event Action ExitToLibrary;

        public bool IsLoaded => _chart != null;

        public int KeyCount => _kc;


        public GamePanel()
        {
            DoubleBuffered = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(10, 14, 22);
            BuildEditPanel();
            try
            {
                timeBeginPeriod(1);
            }
            catch
            {
            }

            Logger.Info("渲染后端：" + RenderBackend.Describe);
            Application.Idle += OnAppIdle;
            _loopTimer = new System.Windows.Forms.Timer
            {
                Interval = 1
            };
            _loopTimer.Tick += (s, e) => LoopTick();
            _loopTimer.Start();
            try
            {
                _renderThread = new Thread(RenderThreadLoop)
                {
                    IsBackground = true,
                    Name = "GameRender"
                };
                _renderThread.Start();
            }
            catch
            {
            }
        }


        Thread _renderThread;

        readonly object _renderLock = new object ();

        volatile bool _renderThreadAlive = true;

        volatile bool _renderThreadRunning;

        public void RestartRenderer()
        {
            try
            {
                if (_d2d is D2DRenderer d2)
                    d2.MarkRecreate();
            }
            catch
            {
            }
        }


        void RenderThreadLoop()
        {
            try
            {
                timeBeginPeriod(1);
            }
            catch
            {
            }

            while (_renderThreadAlive && !IsDisposed)
            {
                try
                {
                    if (_renderThreadRunning && _d2d != null && _playing && !_paused)
                    {
                        lock (_renderLock)
                        {
                            PaintCore();
                        }
                    }
                    else
                        Thread.Sleep(1);
                }
                catch
                {
                }
            }
        }


        /// <summary>CLI/试玩自动化：显式停止渲染线程（停止标志+Join；此后窗体可安全 Dispose/Close——修复 OnHandleDestroyed 与渲染线程竞态）。</summary>
        public void StopRender()
        {
            _renderThreadAlive = false;
            _renderThreadRunning = false;
            try
            {
                if (Thread.CurrentThread != _renderThread)
                    _renderThread?.Join(1500);
            }
            catch
            {
            }

            _renderThread = null;
        }


        bool _idleLooping;

        void OnAppIdle(object sender, EventArgs e)
        {
            if (_idleLooping)
                return;
            _idleLooping = true;
            try
            {
                LoopTick();
            }
            finally
            {
                _idleLooping = false;
            }
        }


        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint uPeriod);

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        static extern uint timeEndPeriod(uint uPeriod);

        readonly System.Windows.Forms.Timer _loopTimer;

        protected override void Dispose(bool disposing)
        {
            try
            {
                timeEndPeriod(1);
            }
            catch
            {
            }

            _renderThreadAlive = false;
            _renderThreadRunning = false;
            try
            {
                if (Thread.CurrentThread != _renderThread)
                    _renderThread?.Join(1500);
            }
            catch
            {
            }

            _renderThread = null;
            Application.Idle -= OnAppIdle;
            _loopTimer?.Stop();
            _loopTimer?.Dispose();
            _bgImage?.Dispose();
            _bgD2D?.Dispose();
            lock (_renderLock)
            {
                _d2d?.Dispose();
                _d2d = null;
            }

            base.Dispose(disposing);
        }


        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                ImmAssociateContext(Handle, IntPtr.Zero);
            }
            catch
            {
            }

            lock (_renderLock)
            {
                _d2d?.Dispose();
                _d2d = null;
            }

            try
            {
                _d2d = new D2DRenderer(Handle, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                if (_d2d is D2DRenderer d2r && !EditLayoutMode)
                {
                    d2r.SuperSample = GameSettings.SuperSample;
                    d2r.SuperSampleStages = GameSettings.SuperSampleStages;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("D2D 渲染器初始化失败（本局无渲染画面）", ex);
                _d2d = null;
            }
        }


        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hWnd);

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _renderThreadAlive = false;
            _renderThreadRunning = false;
            try
            {
                if (Thread.CurrentThread != _renderThread)
                    _renderThread?.Join(1500);
            }
            catch
            {
            }

            _renderThread = null;
            lock (_renderLock)
            {
                _d2d?.Dispose();
                _d2d = null;
            }

            base.OnHandleDestroyed(e);
        }


        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }


        void LoopTick()
        {
            _renderThreadRunning = _playing && !_paused && _d2d != null;
            if ((_chart == null || _kc <= 0) && !EditLayoutMode)
                return;
            try
            {
                double now = MonoMs();
                double frameMs = GraphicsQuality.FrameTargetMs > 0 ? Math.Max(ScreenFrameMs(), GraphicsQuality.FrameTargetMs) : 0;
                if (_lastIdlePaint < 0)
                    _lastIdlePaint = now;
                if (frameMs > 0 && now - _lastIdlePaint < frameMs)
                    return;
                _lastIdlePaint = now;
                if (_playing && !_paused)
                    Step();
                double fxNow = MonoMs();
                double fxDt = _lastFxMono > 0 ? Math.Min(100, fxNow - _lastFxMono) : 0;
                _lastFxMono = fxNow;
                if (_particles.Count > 0)
                    FxParticles.Update(_particles, fxDt);
                if (_rings.Count > 0)
                    FxParticles.UpdateRings(_rings, fxDt);
                Invalidate();
                if (GraphicsQuality.Step(_frameMs))
                {
                    _qualityNotice = "⚙ 自动画质：" + GraphicsQuality.Name(GraphicsQuality.Effective) + "（帧 " + (_frameMs > 0 ? (1000.0 / _frameMs).ToString("0") : "--") + "）";
                    _qualityNoticeUntil = now + 2600;
                }
            }
            catch (Exception ex)
            {
                if (_tickErrOnce++ == 0)
                    Logger.Error("游戏帧逻辑异常（已跳过）", ex);
            }
        }


        int _tickErrOnce;

        string _qualityNotice = "";

        double _qualityNoticeUntil;

        bool _lastTextAA = true;

        [DllImport("gdi32.dll")]
        static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        const int VREFRESH = 116;

        [DllImport("imm32.dll")]
        static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        static double _screenFrameMs = -1;

        static double ScreenFrameMs()
        {
            if (_screenFrameMs > 0)
                return _screenFrameMs;
            try
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                IntPtr hdc = g.GetHdc();
                int v = GetDeviceCaps(hdc, VREFRESH);
                g.ReleaseHdc(hdc);
                double ms = v >= 30 ? 1000.0 / v : 1000.0 / 60.0;
                _screenFrameMs = Math.Max(2, Math.Min(200, ms));
            }
            catch
            {
                _screenFrameMs = 1000.0 / 60.0;
            }

            return _screenFrameMs;
        }


        void BuildEditPanel()
        {
            _editPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = EditPanelW,
                BackColor = UiColors.HeadBg,
                Visible = false
            };
            _editPanel.Padding = new Padding(Ui.P(10));
            var title = new Label
            {
                Text = "📐 布局微调",
                Dock = DockStyle.Top,
                Height = Ui.P(34),
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = UiColors.HeadTitle,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var selLabel = new Label
            {
                Text = "选择元素",
                Dock = DockStyle.Top,
                Height = Ui.P(24),
                ForeColor = UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 10F),
                Padding = new Padding(Ui.P(2), Ui.P(6), 0, 0)
            };
            _editSel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Top,
                Height = Ui.P(30),
                BackColor = UiColors.InputBg,
                ForeColor = UiColors.Fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 11F)
            };
            _editSel.Items.AddRange(EditNames);
            _editSel.SelectedIndex = 0;
            var xLabel = new Label
            {
                Text = "水平位置 X（%）",
                Dock = DockStyle.Top,
                Height = Ui.P(22),
                ForeColor = UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 10F),
                Padding = new Padding(Ui.P(2), Ui.P(6), 0, 0)
            };
            _editX = new NumericUpDown
            {
                Dock = DockStyle.Top,
                Height = Ui.P(28),
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5m,
                BackColor = UiColors.InputBg,
                ForeColor = UiColors.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 11F)
            };
            var yLabel = new Label
            {
                Text = "垂直位置 Y（%）",
                Dock = DockStyle.Top,
                Height = Ui.P(22),
                ForeColor = UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 10F),
                Padding = new Padding(Ui.P(2), Ui.P(6), 0, 0)
            };
            _editY = new NumericUpDown
            {
                Dock = DockStyle.Top,
                Height = Ui.P(28),
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5m,
                BackColor = UiColors.InputBg,
                ForeColor = UiColors.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 11F)
            };
            _editShow = new CheckBox
            {
                Text = "显示",
                Dock = DockStyle.Top,
                Height = Ui.P(30),
                ForeColor = UiColors.BodyText,
                Font = new Font("Microsoft YaHei UI", 11F),
                Padding = new Padding(Ui.P(2), Ui.P(4), 0, 0)
            };
            var tip = new Label
            {
                Text = "提示：也可直接拖拽画面中的元素\nESC 保存并退出",
                Dock = DockStyle.Top,
                Height = Ui.P(52),
                ForeColor = UiColors.Muted,
                Font = new Font("Microsoft YaHei UI", 9F),
                Padding = new Padding(Ui.P(2), Ui.P(10), 0, 0)
            };
            _editPanel.Controls.Add(_editShow);
            _editPanel.Controls.Add(yLabel);
            _editPanel.Controls.Add(_editY);
            _editPanel.Controls.Add(xLabel);
            _editPanel.Controls.Add(_editX);
            _editPanel.Controls.Add(selLabel);
            _editPanel.Controls.Add(_editSel);
            _editPanel.Controls.Add(tip);
            _editPanel.Controls.Add(title);
            _editSel.SelectedIndexChanged += (s, e) => LoadEditValues();
            _editX.ValueChanged += (s, e) =>
            {
                if (!_updatingEdit)
                    ApplyEditValues();
            };
            _editY.ValueChanged += (s, e) =>
            {
                if (!_updatingEdit)
                    ApplyEditValues();
            };
            _editShow.CheckedChanged += (s, e) =>
            {
                if (!_updatingEdit)
                    ApplyEditValues();
            };
            Controls.Add(_editPanel);
        }


        string CurrentEditKey()
        {
            int i = _editSel != null ? _editSel.SelectedIndex : 0;
            if (i < 0 || i >= EditKeys.Length)
                return EditKeys[0];
            return EditKeys[i];
        }


        void LoadEditValues()
        {
            string key = CurrentEditKey();
            HudPos p;
            if (key == "hitline")
            {
                if (!Skin.Layout.TryGetValue("hitline", out p))
                {
                    p = new HudPos
                    {
                        X = 0.5,
                        Y = 0.5
                    };
                    Skin.Layout["hitline"] = p;
                }
            }
            else
            {
                if (!Skin.Layout.TryGetValue(key, out p))
                {
                    p = new HudPos
                    {
                        X = 0.5,
                        Y = 0.1
                    };
                    Skin.Layout[key] = p;
                }
            }

            _updatingEdit = true;
            _editX.Value = (decimal)Math.Round(p.X * 100, 1);
            _editY.Value = (decimal)Math.Round(p.Y * 100, 1);
            _editShow.Checked = p.Show;
            _updatingEdit = false;
        }


        void ApplyEditValues()
        {
            string key = CurrentEditKey();
            HudPos p;
            if (key == "hitline")
            {
                if (!Skin.Layout.TryGetValue("hitline", out p))
                {
                    p = new HudPos
                    {
                        X = 0.5,
                        Y = 0.5
                    };
                    Skin.Layout["hitline"] = p;
                }

                p.Y = (double)_editY.Value / 100.0;
                p.Y = Math.Max(0.05, Math.Min(0.95, p.Y));
                Skin.Layout["hitline"] = p;
            }
            else
            {
                if (!Skin.Layout.TryGetValue(key, out p))
                {
                    p = new HudPos
                    {
                        X = 0.5,
                        Y = 0.1
                    };
                    Skin.Layout[key] = p;
                }

                p.X = (double)_editX.Value / 100.0;
                p.Y = (double)_editY.Value / 100.0;
                p.Show = _editShow.Checked;
                Skin.Layout[key] = p;
            }

            Invalidate();
        }


        void SyncEditValues()
        {
            if (_editPanel == null || !_editPanel.Visible || _editSel == null)
                return;
            string key = CurrentEditKey();
            if (key != _dragKey && !(key == "hitline" && _dragHitline))
                return;
            HudPos p;
            if (!Skin.Layout.TryGetValue(key, out p))
                return;
            _updatingEdit = true;
            _editX.Value = (decimal)Math.Round(p.X * 100, 1);
            _editY.Value = (decimal)Math.Round(p.Y * 100, 1);
            _editShow.Checked = p.Show;
            _updatingEdit = false;
        }


        public void SelectPart(int idx)
        {
            if (_chart == null)
            {
                _partIdx = idx;
                return;
            }

            if (_chart.Parts == null || _chart.Parts.Count == 0)
            {
                _partIdx = 0;
                return;
            }

            _partIdx = Math.Max(0, Math.Min(idx, _chart.Parts.Count - 1));
        }


        void ApplySelectedPart()
        {
            if (_chart == null)
                return;
            if (_chart.Parts == null || _chart.Parts.Count == 0)
            {
                _partIdx = 0;
                _notes = _chart.Notes;
                _kc = _chart.KeyCount;
                return;
            }

            int idx = Math.Max(0, Math.Min(_partIdx, _chart.Parts.Count - 1));
            _partIdx = idx;
            var p = _chart.Parts[idx];
            _chart.Mode = p.Mode;
            _chart.ModeName = ModeSystem.DisplayName(p.Mode);
            _chart.KeyCount = p.KeyCount;
            _chart.Notes = p.Notes;
            _chart.Events = p.Events;
            _notes = p.Notes;
            _kc = p.KeyCount;
        }


        public void SwitchNextPart()
        {
            if (_chart == null || _chart.Parts == null || _chart.Parts.Count < 2)
                return;
            _partIdx = (_partIdx + 1) % _chart.Parts.Count;
            ApplySelectedPart();
            JudgeSettings.ApplyForChart(_chart);
            ResetState();
            ResetCompanionAis();
            _playing = true;
            _audio.SetVolume(GameSettings.Volume / 100.0);
            _audio.Restart();
            _sw.Restart();
            var p = _chart.Parts[_partIdx];
            ShowToast("模式切换：" + p.Name + " · " + ModeSystem.DisplayName(p.Mode));
            Focus();
        }


        public void LoadAndPlay(Chart chart, string baseDir)
        {
            if (chart == null)
                return;
            _chart = chart;
            ApplySelectedPart();
            if (!ModeSystem.IsAvailable(_chart.Mode))
            {
                Logger.Info("玩法已移除，无法游玩：" + ModeSystem.DisplayName(_chart.Mode));
                return;
            }

            GameSettings.Autoplay = false;
            JudgeSettings.ApplyForChart(chart);
            var audioPath = FindAudio(chart.AudioFile, baseDir);
            _audio.Open(audioPath);
            _audio.SetVolume(GameSettings.Volume / 100.0);
            LoadBackgroundArt(baseDir);
            ResetState();
            BuildStageRuntime(chart);
            _loop = _chart != null && _chart.Mode == GameMode.LoopComposer ? new LoopComposer(_chart, _eng, LoopComposerJudgeFx) : null;
            Recording = true;
            Replaying = false;
            DemoAi = null;
            MpActive = false;
            _playing = true;
            _audio.Restart();
            _sw.Restart();
            Logger.Info("谱面开始：" + (chart.Title ?? "") + " | 首音符 " + (_notes.Count > 0 ? _notes[0].Time.ToString("0") : "-") + "ms");
            if (IsTouchMode(chart.Mode) && !GameSettings.Autoplay)
                ShowToast("位置判定：鼠标点击音符所在位置（键盘列键仍可用）");
            Focus();
        }


        public void StartAutoplay(Chart chart, string baseDir)
        {
            LoadAndPlay(chart, baseDir);
            GameSettings.Autoplay = true;
        }


        public void StartAutoplayAt(Chart chart, string baseDir, double startMs)
        {
            LoadAndPlay(chart, baseDir);
            GameSettings.Autoplay = true;
            if (startMs > 0)
            {
                try
                {
                    SeekTo(startMs);
                }
                catch
                {
                }
            }
        }


        public void SeekToPublic(double ms)
        {
            try
            {
                SeekTo(Math.Max(0, ms));
            }
            catch
            {
            }
        }


        public double CurrentMs => RawMs();


        class HumanEvent
        {
            public double T;
            public bool Down;
            public bool Miss;
            public Keys Key;
            public int Col;
            public bool Mouse;
            public PointF Pt;
            public bool Spin;
        }


        List<HumanEvent> _humanPlan = new List<HumanEvent>();

        int _humanIdx;

        bool _humanTest;

        readonly List<Note> _mouseHeld = new List<Note>();

        public void StartHumanTest(Chart chart, string baseDir, double devMs = 15, double missRate = 0.08, int seed = 12345)
        {
            if (chart == null)
                return;
            LoadAndPlay(chart, baseDir);
            if (!_playing)
                return;
            GameSettings.Autoplay = false;
            var rnd = new Random(seed);
            _humanPlan.Clear();
            bool touch = IsTouchMode(chart.Mode);
            foreach (var n in _notes)
            {
                if (touch)
                {
                    var L = ComputeLayout();
                    bool hold = IsHoldNote(n) && n.End - n.Time > 60;
                    bool miss = rnd.NextDouble() < missRate;
                    double dev = (rnd.NextDouble() * 2 - 1) * devMs;
                    _humanPlan.Add(new HumanEvent { T = n.Time + dev, Down = true, Miss = miss, Mouse = true, Pt = NoteScreenPos(n, L, n.Time) });
                    if (hold)
                        _humanPlan.Add(new HumanEvent { T = n.End + (rnd.NextDouble() * 2 - 1) * devMs, Down = false, Miss = miss, Mouse = true, Pt = NoteScreenPos(n, L, n.End) });
                    continue;
                }

                int col2 = n.Col;
                if (col2 < 0 && _chart != null && _chart.Mode == GameMode.Phigros)
                    col2 = Math.Max(0, Math.Min(_kc - 1, (int)(n.X * _kc)));
                bool osuStd = _chart != null && _chart.Mode == GameMode.OsuStandard;
                if (osuStd && n.Type == "spinner")
                {
                    bool spinMiss = rnd.NextDouble() < missRate;
                    for (double tt = n.Time + 10; tt < n.End - 20; tt += 80)
                    {
                        _humanPlan.Add(new HumanEvent { T = tt + (rnd.NextDouble() * 2 - 1) * 8, Down = true, Miss = spinMiss, Key = Keys.Z, Col = 0, Spin = true });
                        _humanPlan.Add(new HumanEvent { T = tt + 40, Down = false, Miss = spinMiss, Key = Keys.Z, Col = 0, Spin = true });
                    }

                    continue;
                }

                var k = osuStd ? Keys.Z : KeyForCol(col2);
                if (k == Keys.None)
                    continue;
                bool hold2 = IsHoldNote(n) && n.End - n.Time > 60;
                bool miss2 = rnd.NextDouble() < missRate;
                double dev2 = (rnd.NextDouble() * 2 - 1) * devMs;
                _humanPlan.Add(new HumanEvent { T = n.Time + dev2, Down = true, Miss = miss2, Key = k, Col = col2 });
                if (hold2)
                    _humanPlan.Add(new HumanEvent { T = n.End + (rnd.NextDouble() * 2 - 1) * devMs, Down = false, Miss = miss2, Key = k, Col = col2 });
            }

            _humanPlan.Sort((a, b) => a.T.CompareTo(b.T));
            _humanIdx = 0;
            _humanTest = true;
            Logger.Info(string.Format("人类模拟开始：{0} | 计划 {1} 条（偏差±{2}ms 漏键 {3:P0}）", chart.Title, _humanPlan.Count, devMs, missRate));
        }


        public void StartMpPlay(Chart chart)
        {
            if (chart == null)
                return;
            _chart = chart;
            ApplySelectedPart();
            if (!ModeSystem.IsAvailable(_chart.Mode))
            {
                Logger.Info("玩法已移除，无法游玩：" + ModeSystem.DisplayName(_chart.Mode));
                return;
            }

            _audio.Close();
            ResetState();
            Recording = false;
            Replaying = false;
            DemoAi = null;
            GameSettings.Autoplay = false;
            MpActive = true;
            MpScores.Clear();
            MpScores[MpSelfName] = new MpPlayerState
            {
                Name = MpSelfName
            };
            if (MpManager.Players.Count > 0)
                foreach (var kv in MpManager.Players)
                    if (!MpScores.ContainsKey(kv.Key))
                        MpScores[kv.Key] = new MpPlayerState
                        {
                            Name = kv.Key
                        };
            _playing = true;
            _sw.Restart();
            Focus();
        }


        public void StartAiDemo(Chart chart, string baseDir, AiLevel lv)
        {
            if (chart == null)
                return;
            _chart = chart;
            ApplySelectedPart();
            if (!ModeSystem.IsAvailable(_chart.Mode))
            {
                Logger.Info("玩法已移除，无法演示：" + ModeSystem.DisplayName(_chart.Mode));
                return;
            }

            JudgeSettings.ApplyForChart(chart);
            var audioPath = FindAudio(chart.AudioFile, baseDir);
            _audio.Open(audioPath);
            _audio.SetVolume(GameSettings.Volume / 100.0);
            LoadBackgroundArt(baseDir);
            ResetState();
            Recording = false;
            Replaying = false;
            GameSettings.Autoplay = false;
            MpActive = false;
            DemoAi = AiEngine.MakePlayer(lv, lv.Name);
            _playing = true;
            _audio.Restart();
            _sw.Restart();
            Focus();
        }


        public void StopAiDemo()
        {
            DemoAi = null;
            Invalidate();
        }


        public void PlayReplay(Chart chart, ReplayFile r, string baseDir)
        {
            LoadAndPlay(chart, baseDir);
            Replaying = true;
            Recording = false;
            _replayEvents = r.Events ?? new List<ReplayEvent>();
            _replayIdx = 0;
            _replayStart = RawMs() + GameSettings.Offset;
        }


        public void AddCompanionAi(AiLevel lv)
        {
            if (lv == null)
                return;
            if (_ais.Exists(a => a.Level == lv))
                return;
            _ais.Add(AiEngine.MakePlayer(lv, lv.Name));
        }


        public void AddCompanionAiMulti(AiLevel lv, int count)
        {
            if (lv == null)
                return;
            for (int i = 0; i < count; i++)
                _ais.Add(AiEngine.MakePlayer(lv, lv.Name));
        }


        public void ResetCompanionAis()
        {
            foreach (var a in _ais)
                AiEngine.Reset(a);
        }


        public void TogglePause()
        {
            if (!_playing || _ended || MpActive)
                return;
            _paused = !_paused;
            if (_paused)
                _audio.Pause();
            else
                _audio.Play();
        }


        public void Restart()
        {
            if (_chart == null)
                return;
            ResetState();
            ResetMultiStates();
            ResetCompanionAis();
            _playing = true;
            _audio.SetVolume(GameSettings.Volume / 100.0);
            _audio.Restart();
            _sw.Restart();
            Focus();
        }


        public void SetAutoplay(bool v)
        {
            GameSettings.Autoplay = v;
        }


        public void ApplyVolume() => _audio.SetVolume(GameSettings.Volume / 100.0);

        public void RequestExitToMenu() => ExitToMenu?.Invoke();

        public void StopToMenu()
        {
            _playing = false;
            _paused = false;
            _ended = false;
            _audio.Stop();
            _chart = null;
            _notes.Clear();
            _kc = 0;
            _partIdx = 0;
            _dragKey = null;
            _dragHitline = false;
            EditLayoutMode = false;
            _blankLayout = false;
            Replaying = false;
            Recording = false;
            DemoAi = null;
            _ais.Clear();
            MpActive = false;
            MpScores.Clear();
            _kpsPresses.Clear();
            _kpsHistory.Clear();
            _allDev.Clear();
            _kps = 0;
            _lastKpsSample = 0;
            _heldNotes.Clear();
            _holdNotes.Clear();
            _bgImage?.Dispose();
            _bgImage = null;
            _bgD2D?.Dispose();
            _bgD2D = null;
            _shakeAmt = 0;
            _resultPhase = false;
            _result = null;
            _particles.Clear();
            _rings.Clear();
            if (_editPanel != null)
                _editPanel.Visible = false;
            Invalidate();
        }


        /* ================= t63 B3：布局编辑器模式扩展（无谱预览模式 + 重置） ================= */
        ComboBox _editModeCombo;

    }
}
