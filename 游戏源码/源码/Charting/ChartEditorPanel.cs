using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 谱面编辑器（参考 osu! / Malody 编辑器交互）：
    ///  - 有轨模式：垂直轨道 + 时间轴，单击放音符、拖动改时间、右键删除、拖底部边缘拉长条
    ///  - 无轨模式：播放头时间（底部时间条 + 滚轮 ±1 拍）+ 自由点击空间，
    ///    按原游戏编辑面重做（Cytus 2D 场 / Phigros 多判定线 / osu!standard 位置场）
    ///  - 节拍网格吸附、BPM/偏移/键数可调、音频预览播放
    ///  - 单一谱面多模式：一个工程可含多个玩法部件（部件框切换/增删）
    ///  - 多模式：Mania / Phigros / Arcaea / Cytus / osu!standard / ADOFAI / IIDX
    ///    （模式列表统一由引擎 ModeSystem 派生）
    ///  - 保存原生 Milestone .mil；Mania 额外导出 Malody .mc / osu!mania .osu
    ///  - Ctrl+Z 撤销，Ctrl+S 保存 .mil，空格播放/暂停
    /// </summary>
    public class ChartEditorPanel : UserControl
    {
        // 缓动名称表（RPE 命名；下标与 _easeBox/_evEaseBox 显示项一一对应）
        static readonly string[] EaseNames = { "", "EaseIn", "EaseOut", "EaseInOut", "EaseOutBack", "EaseOutBounce", "EaseInBack", "EaseInBounce", "EaseInOutBack" };
        static readonly string[] EaseLabels = { "Linear 线性", "EaseIn 加速", "EaseOut 减速", "EaseInOut 缓入缓出", "EaseOutBack 回弹", "EaseOutBounce 弹跳", "EaseInBack", "EaseInBounce", "EaseInOutBack" };

        List<Note> _notes = new List<Note>();
        List<ChartEvent> _events = new List<ChartEvent>();
        string _title = "未命名谱面", _artist = "未知作者", _version = "";
        string _danName = "", _danSet = "";
        double _bpm = 120, _offset = 0;
        int _kc = 4;
        GameMode _mode = GameMode.Mania;
        string _noteType = "tap";   // 当前放置的音符类型
        string _audioPath = "", _chartPath = "";
        bool _dirty;

        // ===== 🧙 制谱助手（ChartMentor 侧边面板） =====
        ChartMentor _mentorPanel;

        // ===== 制谱 AI：打拍校准捕获状态（🎯 自动校准偏移 → 非 WAV 时打拍模式） =====
        bool _aiTapActive;
        readonly List<double> _aiTaps = new List<double>();

        // ===== 单一谱面多模式（部件） =====
        List<ChartPart> _parts = new List<ChartPart>();   // 空=单模式工程；>1 时保存 parts 数组
        // 多场同屏（t27）：场布局与编辑态（stage i ⇔ parts[i]）
        List<Stage> _stageLayouts = new List<Stage>();
        bool _multiMode;      // 开启多场编辑（>1 场时保存写 stages）
        NumericUpDown _stX, _stY, _stW, _stH;
        int _partIndex;                                    // 当前编辑部件索引
        ComboBox _partBox;                                 // 工具栏部件切换框
        bool _partUpdating;                                // 部件框同步防递归

        readonly AudioPlayer _audio = new AudioPlayer();
        bool _playing;
        double _playAnchorTime, _playAnchorPos;   // 播放起始：编辑时间 ↔ 音频位置
        double _time;                              // 当前编辑时间（ms）——无轨模式下即播放头时间
        double _scrollMs;                          // 有轨=画布顶部时间；无轨=底部时间条左缘时间（ms）
        double _pxPerMs = 0.15;                    // 有轨纵向缩放；无轨复用为时间条横向缩放
        int _snapDiv = 4;
        bool _snapOn = true;
        bool _showGrid = true;

        Note _hoverNote, _selNote;
        double _dragGrabT;
        double _downY;
        internal double _dragStartX, _dragStartY;   // 无轨交互辅助（osu 滑条绘制起点 / 时间条抓取）

        // ===== 右侧动画菜单（非线性动画 / 多判定线 / 3D arc）=====
        internal double PlayRate = 1.0;              // 预览播放速率（0.25x~4x）
        internal bool NotesOnlyMode;                 // T58 D2i：ALT+N 音符视图切换（隐藏事件区）
        internal int ActiveLine = 0;                 // Phigros 当前编辑判定线
        internal int LineCount = 1;                  // Phigros 判定线数量（1~64）
        internal List<int> LineParents = new List<int>();   // Phigros 父子线（phimakor：-1=无父线）
        internal bool Arc3D = true;                  // Arcaea 播放路径 2D/3D（arc 高度可视化）
        internal string EaseNew = "";                // 新事件的缓动曲线（""=线性）

        // ===== Phigros 线编辑（phimakor 拆线/绑线）=====
        internal int SplitLineAt(double atMs)
        {
            if (_mode != GameMode.Phigros) return -1;
            PushUndo();
            int line = ActiveLine;
            // 音符按时间切分
            var moved = _notes.Where(n => n != null && n.Line == line && n.Time >= atMs - 0.5).ToList();
            foreach (var n in moved) n.Line = LineCount;
            // 事件按时间切分
            var evs = _events.Where(e => e != null && e.Line == line && e.Time >= atMs - 0.5).ToList();
            foreach (var e in evs) e.Line = LineCount;
            // 新线父线默认继承原线
            while (LineParents.Count <= LineCount) LineParents.Add(-1);
            if (LineParents.Count > line) LineParents[LineCount] = LineParents[line];
            LineCount++;
            if (ActiveLine >= LineCount) ActiveLine = LineCount - 1;
            SyncLineBox(); SyncLineCountFromData();
            MarkDirty(); _canvas.Invalidate(); RefreshEvtList();
            return LineCount - 1;
        }

        /// <summary>绑线：把 source 线合并进 target 线（音符/事件归 target，源线删除）。phimakor bind。</summary>
        internal void BindLine(int target, int source)
        {
            if (_mode != GameMode.Phigros || target == source) return;
            if (target < 0 || source < 0 || target >= LineCount || source >= LineCount) return;
            PushUndo();
            foreach (var n in _notes) if (n != null && n.Line == source) n.Line = target;
            foreach (var e in _events) if (e != null && e.Line == source) e.Line = target;
            // 移除源线，之后的线前移
            for (int i = source + 1; i < LineCount; i++)
            {
                foreach (var n in _notes) if (n != null && n.Line == i) n.Line = i - 1;
                foreach (var e in _events) if (e != null && e.Line == i) e.Line = i - 1;
            }
            if (LineParents.Count > source) LineParents.RemoveAt(source);
            LineCount = Math.Max(1, LineCount - 1);
            if (ActiveLine >= LineCount) ActiveLine = LineCount - 1;
            SyncLineBox(); SyncLineCountFromData();
            MarkDirty(); _canvas.Invalidate(); RefreshEvtList();
        }

        // 撤销栈：同时快照 Notes 与 Events（事件撤销），随部件切换清空
        sealed class UndoState { public List<Note> Notes; public List<ChartEvent> Events; }
        readonly List<UndoState> _undoStack = new List<UndoState>();
        readonly List<UndoState> _redoStack = new List<UndoState>();
        const int MaxUndo = 100;

        // ===== UI =====
        FlowLayoutPanel _toolbar;
        readonly EditorCanvas _canvas;
        Panel _animPanel;                       // 右侧动画菜单（RPE/Phira 风格；⑨ Tab 化：事件/判定线/音符/Arcaea）
        TabControl _rightTabs;
        TabPage _tabEvt, _tabLine, _tabNote, _tabArc;
        ToolTip _tipTooltip;                     // t43：长说明行悬停全串 tooltip
        // ===== t5 三栏工作台：左资源栏 / 中画布容器 / 右 7-Tab 面板（属性/事件/判定线/音符/Arcaea/预览/AI）=====
        Panel _leftPanel, _canvasHost;
        FlowLayoutPanel _leftFlow;
        Panel _leftRail, _foldLeft, _foldRight;
        Button _btnDrawer;
        TabPage _tabProp, _tabPreview, _tabAi;
        ComboBox _aiLevelBox;
        NumericUpDown _aiCompanionBox;
        Label _propLbl;
        int _leftMode, _rightMode;               // 0=开 1=左图标轨(48px) 2=收
        int _prevLeftMode, _prevRightMode;       // F9 专注恢复
        const int MaxRecentCharts = 8;
        readonly List<string> _recentCharts = new List<string>();
        NumericUpDown _rateBox;                 // 预览速率
        ComboBox _easeBox;                      // 缓动曲线（新事件）
        NumericUpDown _lineCountBox;            // Phigros 判定线数量
        ComboBox _lineBox;                      // Phigros 当前编辑线
        Label _phLineLabel, _lineCountLabel, _lineBoxLabel;
        // Phigros 判定线流速 / 亮度·透明度（右侧菜单，写入 speed/alpha 事件到当前线）
        NumericUpDown _lineSpeedBox, _lineAlphaBox;
        Button _speedBtn, _alphaBtn;
        Label _lineSpeedLabel, _lineAlphaLabel;
        ComboBox _lineParentBox;                // Phigros 父子线（phimakor）
        Label _lineParentLabel;
        bool _lineParentUpdating;
        // D2o：判定线元数据（名称/分组/Z/Cover）
        internal List<PhigrosLineMeta> LineMeta = new List<PhigrosLineMeta>();
        Label _lineMetaTitle;
        TextBox _lineNameBox;
        NumericUpDown _lineGroupBox, _lineZBox;
        CheckBox _lineCoverChk;
        bool _lineMetaUpdating;
        CheckBox _arc3dChk;                     // Arcaea arc 3D 播放路径
        ListView _evtList;                      // 事件键帧列表（当前编辑线 / 选中 arc）
        Label _evtListTitle;
        NumericUpDown _evTimeBox, _evEndBox, _evValBox, _evEndValBox;
        ComboBox _evEaseBox;
        Button _evAddBtn, _evDelBtn;
        bool _evUiUpdating;
        // ===== 动画菜单分区（② 按玩法分类：Phigros/Arcaea 专属区 vs 全玩法通用区）=====
        readonly List<Control> _lineSec = new List<Control>();      // Phigros 判定线区（含拆线/绑线/元数据/流速·透明度提示）
        readonly List<Control> _arcBaseSec = new List<Control>();   // Arcaea arc 3D 路径区（高度编辑区由 RefreshArcSection 控制）
        readonly List<Control> _presetSec = new List<Control>();   // RPE 预制事件（Phigros/Arcaea）
        readonly List<Control> _qeSec = new List<Control>();       // 快捷编辑（全玩法：作用于选中事件）
        readonly List<Control> _fillSec = new List<Control>();     // 填充曲线音符（Phigros/Arcaea）
        readonly List<Control> _batchSec = new List<Control>();    // 执行列表（Phigros/Arcaea 音符操作）
        // Arcaea arc 3D 高度编辑
        Label _arcSecTitle, _arcYLabel, _arcEndYLabel, _arc3Label;
        NumericUpDown _arcYBox, _arcEndYBox;
        ListView _arc3List;
        NumericUpDown _arc3ZBox;
        Button _arc3AddBtn, _arc3DelBtn;
        Label _arc3ZLabel;   // arc3 高度: 标签（与高度编辑区随选中态显示）
        // ===== D2r：Phigros 音符编辑面板（选中音符：时间/X%/Y%/方向/真值/宽度/透明度/可视）=====
        Label _noteSecTitle, _noteTimeLabel, _noteXLabel, _noteYLabel, _noteSideLabel, _noteTrueLabel, _noteWidthLabel, _noteAlphaLabel, _noteVisLabel;
        NumericUpDown _noteXBox, _noteYBox, _noteWidthBox, _noteAlphaBox, _noteVisBox;
        ComboBox _noteSideBox, _noteTrueBox;
        bool _noteUiUpdating;
        System.Windows.Forms.Timer _selTimer;
        Label _status;
        NumericUpDown _bpmBox, _offsetBox, _adofaiAngleBox;
        ComboBox _modeBox, _kcBox, _snapBox, _typeBox, _evtBox, _osuCurveBox;
        CheckBox _snapChk, _gridChk;
        Button _btnPlay, _btnSaveMc, _btnExportOsu, _btnExportAff;
        NumericUpDown _zoomBox;                   // ⑤⑥：时间缩放（px/s = PxPerMs×1000；0.003~8 px/ms）
        bool _zoomUpdating;
        Label _osuCurveLabel, _adofaiAngleLabel;
        bool _kcUpdating, _modeUpdating;   // 防止组合框同步时递归触发事件
        bool _osuCurveUpdating, _adofaiAngleUpdating;     // 滑条类型/角度框同步防递归
        char _osuSliderType = 'L';                        // 当前 osu 滑条类型（新滑条 / 选中滑条）
        readonly ToolTip _tip = new ToolTip();
        string _audioName = "（未加载音频）";

        public event Action GoHome;
        /// <summary>试玩请求：把当前编辑内容交给游戏面板游玩（参数2=音频目录）。</summary>
        public event Action<Chart, string> TestPlay;
        /// <summary>t5：自动游玩请求——当前编辑内容交给游戏面板完整自动演示（参数2=音频目录；参数3=起始播放头 ms，t14 P1-1）。</summary>
        public event Action<Chart, string, double> TestAutoplay;
        /// <summary>t5：AI 游玩请求——chart, 音频目录, AI 等级, 陪玩数(0=纯 AI 演示), 起始播放头 ms（t14 P1-1）。规则驱动 AiEngine，不接本地 LLM。</summary>
        public event Action<Chart, string, AiLevel, int, double> TestAiPlay;

        public ChartEditorPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = UiColors.Bg;
            LoadPanelPrefs();                       // t5：折叠档 + 最近谱面（AppConfig 持久化）
            _canvas = new EditorCanvas(this);
            _canvas.Dock = DockStyle.Fill;
            _canvasHost = new Panel { Dock = DockStyle.Fill, BackColor = UiColors.Bg };
            _canvasHost.Controls.Add(_canvas);
            BuildLeftPanel();                       // t5：左资源栏容器 + 48px 图标轨 + 折叠把手
            BuildRightPanelShell();                 // t5：右栏容器 + 7 Tab 壳（内容由工具组迁入 + BuildAnimPanel 填充）
            BuildToolbar();
            BuildAnimPanel();
            Controls.Add(_canvasHost);
            Controls.Add(_toolbar);
            Controls.Add(_leftPanel);
            Controls.Add(_animPanel);
            // 注意：不得在此调用 _toolbar.BringToFront()——WinForms 按 Controls 集合逆序 dock：
            // BringToFront 把工具栏移到 index 0（最后被 dock），此时 Fill 画布已占满剩余矩形，
            // 工具栏只拿到 0 高度却保留 AutoSize 高度浮在画布顶部，遮挡场区上沿（播放头/音符/判定线
            // 在窗口前段全部被盖住——"播放头横线未渲染/无轨播放头缺失"真因）。画布在 index 0 先 dock
            // 是正确顺序：animPanel(Right) → leftPanel(Left) → toolbar(Top) → canvasHost(Fill) 剩余空间。
            ApplyPanelModes();                      // t5：按折叠档/窗口宽应用三栏宽度
            UpdateStatus();
        }

        /// <summary>右侧动画菜单：预览速率 / 缓动曲线 / Phigros 多判定线 / 事件键帧列表（RPE 风格）/ Arcaea 3D。
        /// P0：面板内容超出时滚动（预制事件/执行列表/音符编辑区在底部，AutoScroll 保证可达）。
        /// ⑨ UI 重设计：右侧面板 Tab 化（事件 / 判定线 / 音符 / Arcaea——分区按玩法，内容与旧版一致）。</summary>
        void BuildAnimPanel()
        {
            // t5 三栏：右栏壳与 7 个 Tab（属性/事件/判定线/音符/Arcaea/预览/AI）已在 BuildRightPanelShell 创建；
            // 此处只填充各 Tab 内容（事件/判定线/音符/Arcaea 内容与旧版一致，迁栏不删功能）。
            FlowLayoutPanel flow = TabFlow(_tabEvt);

            Label L(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(Ui.P(255), 0), ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 10F) };   // t43（t41 P1-1/2）：长说明行宽内换行全文——右栏窄面板不再裁切/行间穿插
            Button MkBtn(string t, Action a, Color? bg = null)
            {
                var b = new Button
                {
                    Text = t, Height = Ui.P(30), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(Ui.P(10), 0, Ui.P(10), 0), Font = new Font("Microsoft YaHei UI", 10F),
                    FlatStyle = FlatStyle.Flat, BackColor = bg ?? UiColors.BtnBg, ForeColor = UiColors.Fg,
                    FlatAppearance = { BorderColor = UiColors.BorderLight }
                };
                Ui.Hover(b);
                b.Click += (s, e) => a();
                return b;
            }
            if (_tipTooltip == null) _tipTooltip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 400 };   // t43：长说明行悬停（ToolTip 为 Component 无 IsDisposed——用 null 判断）全串
            var head = new Label { Text = "⚙ 动画菜单", AutoSize = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) };
            flow.Controls.Add(head);
            flow.SetFlowBreak(head, true);

            // ===== t5 三栏：事件 Tab 顶部事件工具行（原工具栏组③迁入） =====
            var evtRow = new Label { Text = "📅 事件", AutoSize = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) };
            flow.Controls.Add(evtRow);
            flow.SetFlowBreak(evtRow, true);
            flow.Controls.Add(L("事件:"));
            _evtBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(100), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10F) };
            flow.Controls.Add(_evtBox);
            flow.Controls.Add(MkBtn("➕ 事件", AddEventAtCurrentTime));
            flow.Controls.Add(MkBtn("⚡ 事件", ShowEventEditor));
            flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);

            // ① 动画速率（预览播放速度倍率）已迁至「预览」Tab（t5 三栏）
            // ② 缓动曲线（RPE 命名；作用于新事件 / 拖线关键帧）
            flow.Controls.Add(L("缓动曲线:"));
            _easeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(170), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10F) };
            _easeBox.Items.AddRange(EaseLabels);
            _easeBox.SelectedIndex = 0;
            _easeBox.SelectedIndexChanged += (s, e) => EaseNew = EaseNames[Math.Max(0, _easeBox.SelectedIndex)];
            flow.Controls.Add(_easeBox);
            var easeTip = L("（拖判定线/添加事件时写入）");
            easeTip.ForeColor = UiColors.Muted;
            flow.Controls.Add(easeTip);
            flow.SetFlowBreak(easeTip, true);

            // ③ Phigros 多判定线 ➜ 【判定线 Tab】
            flow = TabFlow(_tabLine);
            _phLineLabel = L("判定线（Phigros）:");
            _phLineLabel.ForeColor = Color.FromArgb(255, 140, 200, 255);
            flow.Controls.Add(_phLineLabel);
            _lineSec.Add(_phLineLabel);
            flow.SetFlowBreak(_phLineLabel, true);
            _lineCountLabel = L("数量:");
            flow.Controls.Add(_lineCountLabel);
            _lineSec.Add(_lineCountLabel);
            _lineCountBox = new NumericUpDown { Minimum = 1, Maximum = 64, Value = 1, Width = Ui.P(56), Font = new Font("Microsoft YaHei UI", 10F) };
            _lineCountBox.ValueChanged += (s, e) =>
            {
                LineCount = Math.Max(1, (int)_lineCountBox.Value);
                while (LineParents.Count < LineCount) LineParents.Add(-1);
                if (LineParents.Count > LineCount) LineParents.RemoveRange(LineCount, LineParents.Count - LineCount);
                // D2o：元数据随线数同步
                while (LineMeta.Count < LineCount) LineMeta.Add(new PhigrosLineMeta());
                if (LineMeta.Count > LineCount) LineMeta.RemoveRange(LineCount, LineMeta.Count - LineCount);
                if (ActiveLine >= LineCount) { ActiveLine = LineCount - 1; SyncLineBox(); }
                RefreshEvtList();
                RefreshLineParentUI();
                RefreshLineMetaUI();
                _canvas.Invalidate();
            };
            flow.Controls.Add(_lineCountBox);
            _lineSec.Add(_lineCountBox);
            _lineBoxLabel = L("编辑线:");
            flow.Controls.Add(_lineBoxLabel);
            _lineSec.Add(_lineBoxLabel);
            _lineBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(110), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10F) };
            for (int i = 1; i <= 64; i++) _lineBox.Items.Add("线" + i);
            _lineBox.SelectedIndex = 0;
            _lineBox.SelectedIndexChanged += (s, e) => { ActiveLine = Math.Max(0, _lineBox.SelectedIndex); RefreshEvtList(); _canvas.Invalidate(); };
            flow.Controls.Add(_lineBox);
            _lineSec.Add(_lineBox);
            var lineTip = L("（数量无上限；放置的音符/新事件归属此线，各线独立动画）");
            lineTip.ForeColor = UiColors.Muted; lineTip.Margin = new Padding(0, Ui.P(6), 0, Ui.P(6));   // t46：tip 行间距拉开防互压
            flow.Controls.Add(lineTip);
            _lineSec.Add(lineTip);
            flow.SetFlowBreak(lineTip, true);

            // ③c phimakor 拆线/绑线 + 父子线
            var splitBtn = new Button { Text = "✂ 拆线（播放头）", Height = Ui.P(28), Width = Ui.P(160), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9.5F) };   // t46：加宽单行完整（emoji 绘制宽致文字折行越按钮高）
            Ui.Hover(splitBtn);
            splitBtn.Click += (s, e) => { if (SplitLineAt(_time) >= 0) RefreshLineParentUI(); };
            flow.Controls.Add(splitBtn);
            _lineSec.Add(splitBtn);
            var bindBtn = new Button { Text = "🔗 绑线（并入上一线）", Height = Ui.P(28), Width = Ui.P(170), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9.5F) };   // t46：加宽单行完整
            Ui.Hover(bindBtn);
            bindBtn.Click += (s, e) => { if (ActiveLine > 0) { BindLine(ActiveLine - 1, ActiveLine); SyncLineBox(); RefreshLineParentUI(); } };
            flow.Controls.Add(bindBtn);
            _lineSec.Add(bindBtn);
            var splitTip = L("拆线=当前线在播放头处一分为二（音符/事件按时间切分）· 绑线=当前线并入上一线");
            splitTip.ForeColor = UiColors.Muted; splitTip.Margin = new Padding(0, Ui.P(6), 0, Ui.P(6));   // t46：tip 行间距拉开
            flow.Controls.Add(splitTip);
            _lineSec.Add(splitTip);
            flow.SetFlowBreak(splitTip, true);
            _lineParentLabel = L("父线（跟随移动/旋转）:");
            _lineParentLabel.ForeColor = Color.FromArgb(255, 140, 200, 255);
            flow.Controls.Add(_lineParentLabel);
            _lineSec.Add(_lineParentLabel);
            _lineParentBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(120), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5F) };
            _lineParentBox.SelectedIndexChanged += (s, e) => ApplyLineParent();
            flow.Controls.Add(_lineParentBox);
            _lineSec.Add(_lineParentBox);
            var parentTip = L("（父线动画叠加到本线：子线随父线移动/旋转，phimakor 父子线）");
            parentTip.ForeColor = UiColors.Muted; parentTip.Margin = new Padding(0, Ui.P(6), 0, Ui.P(6));   // t46：tip 行间距拉开
            flow.Controls.Add(parentTip);
            _lineSec.Add(parentTip);
            flow.SetFlowBreak(parentTip, true);
            RefreshLineParentUI();

            // ③d D2o：判定线元数据（RPE line-management：名称/分组/Z 渲染顺序/Cover 遮罩）
            _lineMetaTitle = L("判定线信息（当前线）:");
            _lineMetaTitle.ForeColor = Color.FromArgb(255, 150, 210, 255);
            flow.Controls.Add(_lineMetaTitle);
            _lineSec.Add(_lineMetaTitle);
            flow.SetFlowBreak(_lineMetaTitle, true);
            var lineNameLabel = L("名称:");
            flow.Controls.Add(lineNameLabel);
            _lineSec.Add(lineNameLabel);
            _lineNameBox = new TextBox { Width = Ui.P(150), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 9.5F) };
            _lineNameBox.TextChanged += (s, e) => ApplyLineMeta();
            flow.Controls.Add(_lineNameBox);
            _lineSec.Add(_lineNameBox);
            flow.SetFlowBreak(_lineNameBox, true);
            var lineGroupLabel = L("分组:");
            flow.Controls.Add(lineGroupLabel);
            _lineSec.Add(lineGroupLabel);
            _lineGroupBox = new NumericUpDown { Minimum = 0, Maximum = 64, Value = 0, Width = Ui.P(64), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _lineGroupBox.ValueChanged += (s, e) => ApplyLineMeta();
            flow.Controls.Add(_lineGroupBox);
            _lineSec.Add(_lineGroupBox);
            var lineZLabel = L("Z顺序:");
            flow.Controls.Add(lineZLabel);
            _lineSec.Add(lineZLabel);
            _lineZBox = new NumericUpDown { Minimum = -100, Maximum = 100, Value = 0, Width = Ui.P(72), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _lineZBox.ValueChanged += (s, e) => ApplyLineMeta();
            flow.Controls.Add(_lineZBox);
            _lineSec.Add(_lineZBox);
            flow.SetFlowBreak(_lineZBox, true);
            _lineCoverChk = new CheckBox { Text = "Cover 遮罩（跨线音符判定前隐藏）", AutoSize = false, Size = new Size(Ui.P(260), Ui.P(24)), TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 9.5F) };   // t46：定宽单行（AutoSize 折两行越界）
            _lineCoverChk.CheckedChanged += (s, e) => ApplyLineMeta();
            flow.Controls.Add(_lineCoverChk);
            _lineSec.Add(_lineCoverChk);
            flow.SetFlowBreak(_lineCoverChk, true);

            // ③b Phigros 判定线流速 / 亮度·透明度（实机：判定线动画三要素）
            _lineSpeedLabel = L("流速 (speed):");
            _lineSpeedLabel.ForeColor = Color.FromArgb(255, 140, 200, 255);
            flow.Controls.Add(_lineSpeedLabel);
            _lineSec.Add(_lineSpeedLabel);
            _lineSpeedBox = new NumericUpDown { Minimum = 50, Maximum = 300, DecimalPlaces = 2, Value = 100, Increment = 10, Width = Ui.P(64), Font = new Font("Microsoft YaHei UI", 9.5F) };
            flow.Controls.Add(_lineSpeedBox);
            _lineSec.Add(_lineSpeedBox);
            _speedBtn = new Button { Text = "＋写流速", Height = Ui.P(28), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9.5F) };
            Ui.Hover(_speedBtn);
            _speedBtn.Click += (s, e) => AddLineEventAtPlayhead("speed", (double)_lineSpeedBox.Value / 100.0, 1.0);
            flow.Controls.Add(_speedBtn);
            _lineSec.Add(_speedBtn);
            var speedTip = L("（在播放头写入当前编辑线的流速关键帧，×1.00=原速）");
            speedTip.ForeColor = UiColors.Muted; speedTip.Margin = new Padding(0, Ui.P(6), 0, Ui.P(6));   // t46：tip 行间距拉开
            flow.Controls.Add(speedTip);
            _lineSec.Add(speedTip);
            flow.SetFlowBreak(speedTip, true);

            _lineAlphaLabel = L("亮度/透明度 (alpha):");
            _lineAlphaLabel.ForeColor = Color.FromArgb(255, 140, 200, 255);
            flow.Controls.Add(_lineAlphaLabel);
            _lineSec.Add(_lineAlphaLabel);
            _lineAlphaBox = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 100, Increment = 5, Width = Ui.P(64), Font = new Font("Microsoft YaHei UI", 9.5F) };
            flow.Controls.Add(_lineAlphaBox);
            _lineSec.Add(_lineAlphaBox);
            _alphaBtn = new Button { Text = "＋写透明度", Height = Ui.P(28), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9.5F) };
            Ui.Hover(_alphaBtn);
            _alphaBtn.Click += (s, e) => AddLineEventAtPlayhead("alpha", (double)_lineAlphaBox.Value / 100.0, 1.0);
            flow.Controls.Add(_alphaBtn);
            _lineSec.Add(_alphaBtn);
            var alphaTip = L("（100=全亮，0=隐线；播放头处写入当前编辑线）");
            alphaTip.ForeColor = UiColors.Muted; alphaTip.Margin = new Padding(0, Ui.P(6), 0, Ui.P(6));   // t46：tip 行间距拉开
            flow.Controls.Add(alphaTip);
            _lineSec.Add(alphaTip);
            flow.SetFlowBreak(alphaTip, true);

            // ④ 事件键帧列表（RPE 风格：当前编辑线的事件表）➜ 【事件 Tab】
            flow = TabFlow(_tabEvt);
            _evtListTitle = L("事件键帧（当前线）:");
            _evtListTitle.ForeColor = Color.FromArgb(255, 200, 220, 255);
            flow.Controls.Add(_evtListTitle);
            flow.SetFlowBreak(_evtListTitle, true);
            _evtList = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = true, Height = Ui.P(150),
                Width = Ui.P(296), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle
            };
            _evtList.Columns.Add("类型", Ui.P(58));
            _evtList.Columns.Add("时间", Ui.P(52));
            _evtList.Columns.Add("结束", Ui.P(44));
            _evtList.Columns.Add("值", Ui.P(52));
            _evtList.Columns.Add("结束值", Ui.P(52));
            _evtList.Columns.Add("缓动", Ui.P(38));
            _evtList.SelectedIndexChanged += (s, e) => FillEvtEdit();
            flow.Controls.Add(_evtList);
            flow.SetFlowBreak(_evtList, true);

            // 编辑行：时间/结束/值/结束值/缓动
            flow.Controls.Add(L("时间:"));
            _evTimeBox = new NumericUpDown { Minimum = 0, Maximum = 600000, Width = Ui.P(70), Font = new Font("Microsoft YaHei UI", 9.5F) };
            flow.Controls.Add(_evTimeBox);
            flow.Controls.Add(L("结束:"));
            _evEndBox = new NumericUpDown { Minimum = 0, Maximum = 600000, Width = Ui.P(70), Font = new Font("Microsoft YaHei UI", 9.5F) };
            flow.Controls.Add(_evEndBox);
            flow.SetFlowBreak(_evEndBox, true);
            flow.Controls.Add(L("值:"));
            _evValBox = new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 2, Width = Ui.P(80), Font = new Font("Microsoft YaHei UI", 9.5F) };
            flow.Controls.Add(_evValBox);
            flow.Controls.Add(L("结束值:"));
            _evEndValBox = new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 2, Width = Ui.P(80), Font = new Font("Microsoft YaHei UI", 9.5F) };
            flow.Controls.Add(_evEndValBox);
            flow.SetFlowBreak(_evEndValBox, true);
            flow.Controls.Add(L("缓动:"));
            _evEaseBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(150), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5F) };
            _evEaseBox.Items.AddRange(EaseLabels);
            _evEaseBox.SelectedIndex = 0;
            flow.Controls.Add(_evEaseBox);
            flow.SetFlowBreak(_evEaseBox, true);
            _evTimeBox.ValueChanged += (s, e) => ApplyEvtEdit();
            _evEndBox.ValueChanged += (s, e) => ApplyEvtEdit();
            _evValBox.ValueChanged += (s, e) => ApplyEvtEdit();
            _evEndValBox.ValueChanged += (s, e) => ApplyEvtEdit();
            _evEaseBox.SelectedIndexChanged += (s, e) => ApplyEvtEdit();

            _evAddBtn = new Button { Text = "＋ 添加（播放头）", Height = Ui.P(30), Width = Ui.P(140), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 10F) };
            Ui.Hover(_evAddBtn);
            _evAddBtn.Click += (s, e) => AddEvtAtPlayhead();
            flow.Controls.Add(_evAddBtn);
            _evDelBtn = new Button { Text = "🗑 删除选中", Height = Ui.P(30), Width = Ui.P(140), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 10F) };
            Ui.Hover(_evDelBtn);
            _evDelBtn.Click += (s, e) => DeleteEvtSelected();
            flow.Controls.Add(_evDelBtn);
            flow.SetFlowBreak(_evDelBtn, true);

            // ⑤ Arcaea 3D 播放路径 + arc 高度编辑 ➜ 【Arcaea Tab】
            flow = TabFlow(_tabArc);
            _arc3dChk = new CheckBox { Text = "arc 3D 播放路径（高度）", AutoSize = true, Checked = true, ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 10F) };
            _arc3dChk.CheckedChanged += (s, e) => { Arc3D = _arc3dChk.Checked; _canvas.Invalidate(); };
            flow.Controls.Add(_arc3dChk);
            _arcBaseSec.Add(_arc3dChk);
            var arcTip = L("（arc/天键高度：天地判定线之间任意放置）");
            arcTip.ForeColor = UiColors.Muted;
            flow.Controls.Add(arcTip);
            _arcBaseSec.Add(arcTip);
            flow.SetFlowBreak(arcTip, true);

            _arcSecTitle = L("arc 3D 高度（选中 arc）:");
            _arcSecTitle.ForeColor = Color.FromArgb(255, 140, 220, 255);
            flow.Controls.Add(_arcSecTitle);
            flow.SetFlowBreak(_arcSecTitle, true);
            _arcYLabel = L("起点高度:");
            flow.Controls.Add(_arcYLabel);
            _arcYBox = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 0, Value = 100, Width = Ui.P(64), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _arcYBox.ValueChanged += (s, e) => ApplyArcY();
            flow.Controls.Add(_arcYBox);
            _arcEndYLabel = L("终点高度:");
            flow.Controls.Add(_arcEndYLabel);
            _arcEndYBox = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 0, Value = 100, Width = Ui.P(64), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _arcEndYBox.ValueChanged += (s, e) => ApplyArcY();
            flow.Controls.Add(_arcEndYBox);
            flow.SetFlowBreak(_arcEndYBox, true);

            _arc3Label = L("中间控制点（高度 0~100）:");
            flow.Controls.Add(_arc3Label);
            flow.SetFlowBreak(_arc3Label, true);
            _arc3List = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = true, Height = Ui.P(84),
                Width = Ui.P(296), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle
            };
            _arc3List.Columns.Add("#", Ui.P(30));
            _arc3List.Columns.Add("时间%", Ui.P(70));
            _arc3List.Columns.Add("高度", Ui.P(70));
            _arc3List.SelectedIndexChanged += (s, e) => FillArc3Z();
            flow.Controls.Add(_arc3List);
            flow.SetFlowBreak(_arc3List, true);
            _arc3ZLabel = L("高度:");
            flow.Controls.Add(_arc3ZLabel);
            _arc3ZBox = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 0, Value = 50, Width = Ui.P(64), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _arc3ZBox.ValueChanged += (s, e) => ApplyArc3Z();
            flow.Controls.Add(_arc3ZBox);
            _arc3AddBtn = new Button { Text = "＋ 控制点", Height = Ui.P(28), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9.5F) };
            Ui.Hover(_arc3AddBtn);
            _arc3AddBtn.Click += (s, e) => AddArc3Point();
            flow.Controls.Add(_arc3AddBtn);
            _arc3DelBtn = new Button { Text = "🗑 删选中", Height = Ui.P(28), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9.5F) };
            Ui.Hover(_arc3DelBtn);
            _arc3DelBtn.Click += (s, e) => DeleteArc3Point();
            flow.Controls.Add(_arc3DelBtn);
            flow.SetFlowBreak(_arc3DelBtn, true);

            // 选中同步定时器（画布选中变化 → 刷新 arc 高度区 / 音符编辑区）
            _selTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _selTimer.Tick += (s, e) => { RefreshArcSection(); RefreshNoteSection(); };
            _selTimer.Start();

            // ===== D3：预制事件按钮组（RPE "预制事件"：倍速 2x/0.5x、淡出/淡入、旋转 180°、平移 X）➜ 【音符 Tab】=====
            flow = TabFlow(_tabNote);
            var presetTitle = L("预制事件（播放头·当前线）:");
            presetTitle.ForeColor = Color.FromArgb(255, 190, 220, 255);
            flow.Controls.Add(presetTitle);
            _presetSec.Add(presetTitle);
            flow.SetFlowBreak(presetTitle, true);
            (string text, string type, double v, double ev)[]
            presets =
            {
                ("🚀 倍速2x", "speed", 2.0, 2.0),
                ("🐢 倍速0.5x", "speed", 0.5, 0.5),
                ("🌑 淡出", "alpha", 0.0, 0.0),
                ("🌞 淡入", "alpha", 1.0, 1.0),
                ("↻ 旋转180", "rotate", 180.0, 180.0),
                ("➡ 平移X", "moveX", 0.75, 0.75)
            };
            foreach (var (text, ptype, pv, pev) in presets)
            {
                var b = new Button { Text = text, Height = Ui.P(26), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9F) };
                Ui.Hover(b);
                b.Click += (s, e) => AddLineEventAtPlayhead(ptype, pv, pev);
                flow.Controls.Add(b);
                _presetSec.Add(b);
            }
            flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);

            // ===== T56 D2g：快捷编辑组（粘合/切割/随机/递推——作用于选中事件；RPE tools-bar）=====
            var qeTitle = L("快捷编辑（选中事件）:");
            qeTitle.ForeColor = Color.FromArgb(255, 230, 210, 160);
            flow.Controls.Add(qeTitle);
            _qeSec.Add(qeTitle);
            flow.SetFlowBreak(qeTitle, true);
            (string qeText, Action qeOp)[] qeOps =
            {
                ("🧩 粘合", EvtGlue),
                ("✂ 切割", EvtSplitAt),
                ("🎲 随机", EvtRandom),
                ("🔁 递推", EvtRecur)
            };
            foreach (var (qeText, qeOp) in qeOps)
            {
                var b = new Button { Text = qeText, Height = Ui.P(26), Width = Ui.P(88), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9F) };
                Ui.Hover(b);
                b.Click += (s, e) => qeOp();
                flow.Controls.Add(b);
                _qeSec.Add(b);
            }
            flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);

            // ===== D2q：填充曲线音符（RPE curve-fill-notes：Ctrl+F/G 锚起点/终点 + 密度/种类/形状）=====
            var fillTitle = L("填充曲线音符（选中音符做锚）:");
            fillTitle.ForeColor = Color.FromArgb(255, 190, 230, 200);
            flow.Controls.Add(fillTitle);
            _fillSec.Add(fillTitle);
            flow.SetFlowBreak(fillTitle, true);
            var fillStartBtn = new Button { Text = "⭕ 起点(Ctrl+F)", Height = Ui.P(26), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9F) };
            Ui.Hover(fillStartBtn);
            fillStartBtn.Click += (s, e) => SetFillAnchorStart();
            flow.Controls.Add(fillStartBtn);
            _fillSec.Add(fillStartBtn);
            var fillEndBtn = new Button { Text = "⭕ 终点(Ctrl+G)", Height = Ui.P(26), Width = Ui.P(92), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9F) };
            Ui.Hover(fillEndBtn);
            fillEndBtn.Click += (s, e) => SetFillAnchorEnd();
            flow.Controls.Add(fillEndBtn);
            _fillSec.Add(fillEndBtn);
            var fillGenBtn = new Button { Text = "✨ 生成", Height = Ui.P(26), Width = Ui.P(60), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BlueBtn, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9.5F) };
            Ui.Hover(fillGenBtn);
            fillGenBtn.Click += (s, e) => FillCurveNotes();
            flow.Controls.Add(fillGenBtn);
            _fillSec.Add(fillGenBtn);
            flow.SetFlowBreak(fillGenBtn, true);
            var fillTypeLabel = L("种类:");
            flow.Controls.Add(fillTypeLabel);
            _fillSec.Add(fillTypeLabel);
            var fillTypeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(88), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9F) };
            foreach (var t in new[] { "tap", "flick", "drag" }) fillTypeCombo.Items.Add(t);
            fillTypeCombo.SelectedIndex = 0;
            fillTypeCombo.SelectedIndexChanged += (s, e) => FillType = (string)fillTypeCombo.SelectedItem;
            flow.Controls.Add(fillTypeCombo);
            _fillSec.Add(fillTypeCombo);
            var fillShapeLabel = L("形状:");
            flow.Controls.Add(fillShapeLabel);
            _fillSec.Add(fillShapeLabel);
            var fillShapeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(74), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9F) };
            foreach (var t in new[] { "linear", "sine", "quad" }) fillShapeCombo.Items.Add(t);
            fillShapeCombo.SelectedIndex = 0;
            fillShapeCombo.SelectedIndexChanged += (s, e) => FillShape = (string)fillShapeCombo.SelectedItem;
            flow.Controls.Add(fillShapeCombo);
            _fillSec.Add(fillShapeCombo);
            flow.SetFlowBreak(fillShapeCombo, true);
            var fillDensityLabel = L("密度:");
            flow.Controls.Add(fillDensityLabel);
            _fillSec.Add(fillDensityLabel);
            var fillDensityBox = new NumericUpDown { Minimum = 1, Maximum = 64, Value = 2, Width = Ui.P(56), Font = new Font("Microsoft YaHei UI", 9F) };
            fillDensityBox.ValueChanged += (s, e) => FillDensity = Math.Max(1, (int)fillDensityBox.Value);
            flow.Controls.Add(fillDensityBox);
            _fillSec.Add(fillDensityBox);
            var fillTip = L("（密度=横线间距/填充间距；生成中间音符，端点保留）");
            fillTip.ForeColor = UiColors.Muted;
            flow.Controls.Add(fillTip);
            _fillSec.Add(fillTip);
            flow.SetFlowBreak(fillTip, true);

            // ===== D2p：执行列表（RPE batch-edit-basics；作用于选中音符多选集）=====
            var batchTitle = L("执行列表（选中音符）:");
            batchTitle.ForeColor = Color.FromArgb(255, 200, 220, 255);
            flow.Controls.Add(batchTitle);
            _batchSec.Add(batchTitle);
            flow.SetFlowBreak(batchTitle, true);
            string[] batchOps = { "MirrorY", "MirrorMid", "SideSwitch", "SideUp", "SideDown", "ToReal", "ToFake", "ToTap", "ToFlick", "ToDrag", "AttachX" };
            foreach (var op in batchOps)
            {
                var b = new Button { Text = op, Height = Ui.P(26), Width = Ui.P(88), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9F) };
                Ui.Hover(b);
                b.Click += (s, e) => BatchEdit(op);
                flow.Controls.Add(b);
                _batchSec.Add(b);
            }
            flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);

            // ===== D2r：Phigros 音符编辑面板（实机判据 rpe_f3_60：时间/X坐标/下落朝向/真值/宽度/可视线/透明度）=====
            _noteSecTitle = L("音符编辑（Phigros·选中）:");
            _noteSecTitle.ForeColor = Color.FromArgb(255, 255, 205, 140);
            flow.Controls.Add(_noteSecTitle);
            flow.SetFlowBreak(_noteSecTitle, true);
            _noteTimeLabel = L("时间: --");
            flow.Controls.Add(_noteTimeLabel);
            flow.SetFlowBreak(_noteTimeLabel, true);
            _noteXLabel = L("X%:");
            flow.Controls.Add(_noteXLabel);
            _noteXBox = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 50, Width = Ui.P(70), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteXBox.ValueChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteXBox);
            _noteYLabel = L("Y%:");
            flow.Controls.Add(_noteYLabel);
            _noteYBox = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 50, Width = Ui.P(70), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteYBox.ValueChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteYBox);
            flow.SetFlowBreak(_noteYBox, true);
            _noteSideLabel = L("方向:");
            flow.Controls.Add(_noteSideLabel);
            _noteSideBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(90), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteSideBox.Items.Add("Up"); _noteSideBox.Items.Add("Down");
            _noteSideBox.SelectedIndex = 0;
            _noteSideBox.SelectedIndexChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteSideBox);
            _noteTrueLabel = L("真值:");
            flow.Controls.Add(_noteTrueLabel);
            _noteTrueBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(90), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteTrueBox.Items.Add("Real"); _noteTrueBox.Items.Add("Fake");
            _noteTrueBox.SelectedIndex = 0;
            _noteTrueBox.SelectedIndexChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteTrueBox);
            flow.SetFlowBreak(_noteTrueBox, true);
            _noteWidthLabel = L("宽度:");
            flow.Controls.Add(_noteWidthLabel);
            _noteWidthBox = new NumericUpDown { Minimum = 1, Maximum = 1000, DecimalPlaces = 2, Value = 100, Increment = 10, Width = Ui.P(70), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteWidthBox.ValueChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteWidthBox);
            _noteAlphaLabel = L("透明度(%):");
            flow.Controls.Add(_noteAlphaLabel);
            _noteAlphaBox = new NumericUpDown { Minimum = 0, Maximum = 255, DecimalPlaces = 0, Value = 255, Width = Ui.P(70), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteAlphaBox.ValueChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteAlphaBox);
            flow.SetFlowBreak(_noteAlphaBox, true);
            _noteVisLabel = L("可视时间(ms):");
            flow.Controls.Add(_noteVisLabel);
            _noteVisBox = new NumericUpDown { Minimum = 0, Maximum = 1000000, DecimalPlaces = 0, Value = 999999, Width = Ui.P(90), Font = new Font("Microsoft YaHei UI", 9.5F) };
            _noteVisBox.ValueChanged += (s, e) => ApplyNoteEdit();
            flow.Controls.Add(_noteVisBox);
            flow.SetFlowBreak(_noteVisBox, true);
            // ===== t5 三栏：预览 Tab（播放运输 + 自动游玩 + AI 游玩） =====
            var prevFlow = TabFlow(_tabPreview);
            var prevHead = new Label { Text = "▶ 预览（同窗承载游玩面板）", AutoSize = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) };
            prevFlow.Controls.Add(prevHead);
            prevFlow.SetFlowBreak(prevHead, true);
            prevFlow.Controls.Add(L("预览速率:"));
            _rateBox = new NumericUpDown { Minimum = 25, Maximum = 400, Value = 100, Increment = 25, Width = Ui.P(84), Font = new Font("Microsoft YaHei UI", 10F) };
            _rateBox.ValueChanged += (s, e) => { PlayRate = (double)_rateBox.Value / 100.0; _canvas.Invalidate(); };
            prevFlow.Controls.Add(_rateBox);
            var prevRateX = L("×（编辑器播放）");
            prevFlow.Controls.Add(prevRateX);
            prevFlow.SetFlowBreak(prevRateX, true);
            _btnPlay = MkBtn("▶ 播放", TogglePlay);
            prevFlow.Controls.Add(_btnPlay);
            prevFlow.Controls.Add(MkBtn("⏮ 开头", () => { _playing = false; SyncPlayButton(); Seek(0); }));
            prevFlow.Controls.Add(MkBtn("🎵 加载音频…", LoadAudio));
            prevFlow.SetFlowBreak(prevFlow.Controls[prevFlow.Controls.Count - 1], true);
            prevFlow.Controls.Add(MkBtn("🎮 试玩", PlayTest));
            prevFlow.Controls.Add(MkBtn("🅰 自动游玩", PlayAutoplay, UiColors.BlueBtn));
            prevFlow.Controls.Add(MkBtn("🤖 AI 游玩", PlayAi, UiColors.BlueBtn));
            prevFlow.SetFlowBreak(prevFlow.Controls[prevFlow.Controls.Count - 1], true);
            prevFlow.Controls.Add(L("AI 等级:"));
            _aiLevelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(160), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10F) };
            try
            {
                var levels = AiEngine.Levels;
                if (levels != null)
                    foreach (var lv in levels)
                        if (lv != null) _aiLevelBox.Items.Add(lv.Name + "（" + lv.Dan + "）");
            }
            catch { }
            if (_aiLevelBox.Items.Count == 0) _aiLevelBox.Items.Add("1st Dan（默认）");
            // t13 P2-2（captain 放行）：默认等级 = 1st Dan（editor-trilab §3.2 口径；数组第 0 项是 INTRO-1st）
            int defaultIdx = 0;
            for (int li = 0; li < _aiLevelBox.Items.Count; li++)
                if (_aiLevelBox.Items[li].ToString().StartsWith("1st Dan")) { defaultIdx = li; break; }
            _aiLevelBox.SelectedIndex = defaultIdx;
            prevFlow.Controls.Add(_aiLevelBox);
            prevFlow.SetFlowBreak(_aiLevelBox, true);
            prevFlow.Controls.Add(L("陪玩数(0=纯演示):"));
            _aiCompanionBox = new NumericUpDown { Minimum = 0, Maximum = 3, Value = 0, Width = Ui.P(60), Font = new Font("Microsoft YaHei UI", 10F) };
            prevFlow.Controls.Add(_aiCompanionBox);
            prevFlow.SetFlowBreak(_aiCompanionBox, true);
            var aiTip = L("AI 游玩 = 引擎规则驱动（AiEngine），不调用本地 AI 服务（t12）。");
            aiTip.ForeColor = UiColors.Muted;
            prevFlow.Controls.Add(aiTip);
            prevFlow.SetFlowBreak(aiTip, true);
            var keyTip = L("F5 自动游玩 · F6 AI 游玩 · F8 左栏 · F9 专注 · F10 右栏");
            keyTip.ForeColor = UiColors.Muted;
            prevFlow.Controls.Add(keyTip);

            // ===== t5 三栏：AI Tab（校准/检查/助手；t12 后均规则可用） =====
            var aiTabFlow = TabFlow(_tabAi);
            var aiHead = new Label { Text = "🤖 AI 与校准（本地 AI 未启用，规则检查仍可用）", AutoSize = true, MaximumSize = new Size(Ui.P(255), 0), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) };   // t43（t41 P1-3）：255 宽换行全文（右端不再裁切）
            aiTabFlow.Controls.Add(aiHead);
            aiTabFlow.SetFlowBreak(aiHead, true);
            aiTabFlow.Controls.Add(MkBtn("🎯 自动校准偏移", AutoCalibrateOffset));
            aiTabFlow.Controls.Add(MkBtn("🤖 AI 检查", RunAiChartCheck));
            aiTabFlow.Controls.Add(MkBtn("🧙 制谱助手", OpenMentorPanel));

            // ===== t5 三栏：属性 Tab（当前工程/选中态速览） =====
            var propFlow = TabFlow(_tabProp);
            var propHead = new Label { Text = "📋 属性速览", AutoSize = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) };
            propFlow.Controls.Add(propHead);
            propFlow.SetFlowBreak(propHead, true);
            _propLbl = new Label { AutoSize = true, MaximumSize = new Size(Ui.P(255), 0), ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 9.5F) };   // t43：与 L() 同宽换行全文
            propFlow.Controls.Add(_propLbl);
            propFlow.SetFlowBreak(_propLbl, true);
            // t13 P1-3（captain 放行）：属性 Tab 跳转按钮（速览 → 对应编辑页）
            var jumpTip = L("跳转编辑页:");
            jumpTip.ForeColor = UiColors.Muted;
            propFlow.Controls.Add(jumpTip);
            propFlow.SetFlowBreak(jumpTip, true);
            propFlow.Controls.Add(MkBtn("🎵 音符", () => { if (_rightTabs != null) _rightTabs.SelectedTab = _tabNote; }));
            propFlow.Controls.Add(MkBtn("📅 事件", () => { if (_rightTabs != null) _rightTabs.SelectedTab = _tabEvt; }));
            propFlow.Controls.Add(MkBtn("📏 判定线", () =>
            {
                if (_rightTabs == null) return;
                if (!_rightTabs.TabPages.Contains(_tabLine)) { SyncAnimPanel(); }   // 判定线为 Phigros 专属 Tab（物理增删）
                if (_rightTabs.TabPages.Contains(_tabLine)) _rightTabs.SelectedTab = _tabLine;
            }));
            var propTip = L("选中音符/事件的属性在「音符」「事件」「判定线」页编辑。");
            propTip.ForeColor = UiColors.Muted;
            propFlow.Controls.Add(propTip);

            // t43（t41 P1-1/2/3）：长说明行悬停全串 tooltip（换行全文 + 悬停原文双保险）
            if (_tipTooltip != null)
                foreach (var t in new[] { easeTip, lineTip, splitTip, parentTip, speedTip, alphaTip, aiTip, keyTip, propTip, aiHead, _propLbl })
                    _tipTooltip.SetToolTip(t, t.Text);

            // ⑨ Tab 化：各分区 flow 已加入对应 TabPage（TabFlow），无需再挂 _animPanel
        }

        Note SelectedArc()
        {
            var n = _selNote;
            if (n == null || n.Type != "arc" || _mode != GameMode.Arcaea) return null;
            return n;
        }

        void RefreshArcSection()
        {
            if (_arcYBox == null) return;
            bool show = _mode == GameMode.Arcaea;
            var n = show ? SelectedArc() : null;
            foreach (var c in new Control[] { _arcSecTitle, _arcYLabel, _arcYBox, _arcEndYLabel, _arcEndYBox, _arc3Label, _arc3List, _arc3ZLabel, _arc3ZBox, _arc3AddBtn, _arc3DelBtn }) c.Visible = show && n != null;
            if (n == null) return;
            _arcYBox.Value = Math.Max(0, Math.Min(100, (decimal)Math.Round(n.Y * 100)));
            _arcEndYBox.Value = Math.Max(0, Math.Min(100, (decimal)Math.Round(n.EndY * 100)));
            RefreshArc3List();
        }

        /// <summary>D2r：刷新 Phigros 音符编辑面板（选中音符时显示并回填）。</summary>
        void RefreshNoteSection()
        {
            if (_noteSecTitle == null) return;
            bool show = _mode == GameMode.Phigros && _selNote != null;
            foreach (var c in new Control[] { _noteSecTitle, _noteTimeLabel, _noteXLabel, _noteXBox, _noteYLabel, _noteYBox,
                _noteSideLabel, _noteSideBox, _noteTrueLabel, _noteTrueBox, _noteWidthLabel, _noteWidthBox, _noteAlphaLabel, _noteAlphaBox, _noteVisLabel, _noteVisBox })
                c.Visible = show;
            if (!show) return;
            var n = _selNote;
            _noteUiUpdating = true;
            try
            {
                _noteTimeLabel.Text = "时间: " + BeatTimeText(n.Time, BeatMs) + "（" + n.Time.ToString("0") + "ms）";
                _noteXBox.Value = Math.Max(0, Math.Min(100, (decimal)Math.Round(n.X * 100, 1)));
                _noteYBox.Value = Math.Max(0, Math.Min(100, (decimal)Math.Round(n.Y * 100, 1)));
                _noteSideBox.SelectedIndex = n.Side == 1 ? 1 : 0;
                _noteTrueBox.SelectedIndex = n.Decor ? 1 : 0;
                _noteWidthBox.Value = Math.Max(1, Math.Min(1000, (decimal)Math.Round(n.Width * 100, 2)));
                _noteAlphaBox.Value = Math.Max(0, Math.Min(255, (decimal)Math.Round(n.Alpha * 255)));
                _noteVisBox.Value = Math.Max(0, Math.Min(1000000, (decimal)n.VisMs));
            }
            finally { _noteUiUpdating = false; }
        }

        /// <summary>D2r：应用选中 Phigros 音符编辑（时间/X%/Y%/方向/真值/宽度/透明度/可视时间）。</summary>
        void ApplyNoteEdit()
        {
            if (_noteUiUpdating) return;
            var n = _selNote;
            if (n == null || _mode != GameMode.Phigros) return;
            PushUndo();
            n.X = Math.Max(0, Math.Min(1, (double)_noteXBox.Value / 100.0));
            n.Y = Math.Max(0, Math.Min(1, (double)_noteYBox.Value / 100.0));
            n.Side = _noteSideBox.SelectedIndex == 1 ? 1 : 0;
            n.Decor = _noteTrueBox.SelectedIndex == 1;   // Fake=Decor
            n.Width = Math.Max(0.01, (double)_noteWidthBox.Value / 100.0);
            n.Alpha = Math.Max(0, Math.Min(1, (double)_noteAlphaBox.Value / 255.0));
            n.VisMs = Math.Max(0, (double)_noteVisBox.Value);
            MarkDirty();
            _canvas.Invalidate();
        }

        void RefreshArc3List()
        {
            if (_arc3List == null) return;
            var n = SelectedArc();
            _arc3List.BeginUpdate();
            _arc3List.Items.Clear();
            if (n != null && n.Arc3 != null)
                for (int i = 0; i < n.Arc3.Count; i++)
                {
                    var it = new ListViewItem((i + 1).ToString());
                    it.SubItems.Add(((int)Math.Round(n.Arc3[i].Y * 100)).ToString());
                    it.SubItems.Add(((int)Math.Round(n.Arc3[i].Z * 100)).ToString());
                    it.Tag = i;
                    _arc3List.Items.Add(it);
                }
            _arc3List.EndUpdate();
            FillArc3Z();
        }

        void FillArc3Z()
        {
            if (_arc3ZBox == null) return;
            var n = SelectedArc();
            if (n == null || n.Arc3 == null || _arc3List.SelectedItems.Count == 0) { _arc3ZBox.Enabled = false; return; }
            int idx = (int)_arc3List.SelectedItems[0].Tag;
            if (idx < 0 || idx >= n.Arc3.Count) { _arc3ZBox.Enabled = false; return; }
            _arc3ZBox.Enabled = true;
            _arc3ZBox.Value = Math.Max(0, Math.Min(100, (decimal)Math.Round(n.Arc3[idx].Z * 100)));
        }

        void ApplyArcY()
        {
            var n = SelectedArc();
            if (n == null) return;
            PushUndo();
            n.Y = Math.Max(0, Math.Min(1, (double)_arcYBox.Value / 100.0));
            n.EndY = Math.Max(0, Math.Min(1, (double)_arcEndYBox.Value / 100.0));
            MarkDirty();
            _canvas.Invalidate();
        }

        void ApplyArc3Z()
        {
            var n = SelectedArc();
            if (n == null || n.Arc3 == null || _arc3List.SelectedItems.Count == 0 || !_arc3ZBox.Enabled) return;
            int idx = (int)_arc3List.SelectedItems[0].Tag;
            if (idx < 0 || idx >= n.Arc3.Count) return;
            PushUndo();
            var p = n.Arc3[idx];
            n.Arc3[idx] = (p.X, p.Y, Math.Max(0, Math.Min(1, (double)_arc3ZBox.Value / 100.0)));
            MarkDirty();
            _canvas.Invalidate();
            RefreshArc3List();
        }

        void AddArc3Point()
        {
            var n = SelectedArc();
            if (n == null) return;
            if (n.Arc3 == null) n.Arc3 = new List<(double X, double Y, double Z)>();
            if (n.Arc3.Count >= 4) return;
            PushUndo();
            double dur = Math.Max(1, n.End - n.Time);
            double tFrac = Clamp01((_time - n.Time) / dur);
            n.Arc3.Add((Clamp01((n.X + n.EndX) / 2.0), tFrac, 0.5));
            MarkDirty();
            _canvas.Invalidate();
            RefreshArc3List();
        }

        void DeleteArc3Point()
        {
            var n = SelectedArc();
            if (n == null || n.Arc3 == null || _arc3List.SelectedItems.Count == 0) return;
            int idx = (int)_arc3List.SelectedItems[0].Tag;
            if (idx < 0 || idx >= n.Arc3.Count) return;
            PushUndo();
            n.Arc3.RemoveAt(idx);
            MarkDirty();
            _canvas.Invalidate();
            RefreshArc3List();
        }

        /// <summary>事件键帧列表：显示当前编辑线（Phigros）的全部事件（含全局事件，标记 ※）。</summary>
        void RefreshEvtList()
        {
            if (_evtList == null) return;
            _evUiUpdating = true;
            try
            {
                _evtList.BeginUpdate();
                _evtList.Items.Clear();
                ChartEvent sel = SelectedEvt();
                foreach (var ev in _events)
                {
                    if (ev == null) continue;
                    if (_mode == GameMode.Phigros && ev.Line != ActiveLine && ev.Line >= 0) continue;
                    bool global = ev.Line < 0;
                    var it = new ListViewItem((global ? "※" : "") + EventAbbr(ev.Type));
                    it.SubItems.Add(((int)Math.Round(ev.Time)).ToString());
                    it.SubItems.Add(double.IsNaN(ev.End) ? "—" : ((int)Math.Round(ev.End)).ToString());
                    it.SubItems.Add(ev.Value.ToString("0.##"));
                    it.SubItems.Add(double.IsNaN(ev.EndValue) ? "—" : ev.EndValue.ToString("0.##"));
                    it.SubItems.Add(string.IsNullOrEmpty(ev.Ease) ? "线性" : ev.Ease);
                    it.Tag = ev;
                    if (ev == sel) it.Selected = true;
                    _evtList.Items.Add(it);
                }
                _evtList.EndUpdate();
            }
            finally { _evUiUpdating = false; }
            FillEvtEdit();
        }

        ChartEvent SelectedEvt()
        {
            if (_evtList != null && _evtList.SelectedItems.Count > 0 && _evtList.SelectedItems[0].Tag is ChartEvent ev)
                return ev;
            return null;
        }

        void FillEvtEdit()
        {
            if (_evUiUpdating || _evTimeBox == null) return;
            var ev = SelectedEvt();
            _evUiUpdating = true;
            try
            {
                bool has = ev != null;
                foreach (var c in new Control[] { _evTimeBox, _evEndBox, _evValBox, _evEndValBox, _evEaseBox }) c.Enabled = has;
                if (!has) return;
                _evTimeBox.Value = Math.Max(_evTimeBox.Minimum, Math.Min(_evTimeBox.Maximum, (decimal)ev.Time));
                _evEndBox.Value = Math.Max(_evEndBox.Minimum, Math.Min(_evEndBox.Maximum, (decimal)(double.IsNaN(ev.End) ? ev.Time : ev.End)));
                _evValBox.Value = Math.Max(_evValBox.Minimum, Math.Min(_evValBox.Maximum, (decimal)ev.Value));
                _evEndValBox.Value = Math.Max(_evEndValBox.Minimum, Math.Min(_evEndValBox.Maximum, (decimal)(double.IsNaN(ev.EndValue) ? ev.Value : ev.EndValue)));
                int ei = Array.IndexOf(EaseNames, ev.Ease);
                _evEaseBox.SelectedIndex = Math.Max(0, ei);
            }
            finally { _evUiUpdating = false; }
        }

        void ApplyEvtEdit()
        {
            if (_evUiUpdating) return;
            var ev = SelectedEvt();
            if (ev == null) return;
            PushUndo();
            ev.Time = Math.Max(0, (double)_evTimeBox.Value);
            ev.End = (double)_evEndBox.Value > ev.Time ? (double)_evEndBox.Value : double.NaN;
            ev.Value = (double)_evValBox.Value;
            ev.EndValue = !double.IsNaN(ev.End) ? (double)_evEndValBox.Value : double.NaN;
            ev.Ease = EaseNames[Math.Max(0, _evEaseBox.SelectedIndex)];
            _events.Sort((a, b) => a.Time.CompareTo(b.Time));
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        void AddEvtAtPlayhead()
        {
            PushUndo();
            string type = "moveY";
            // 用工具栏事件类型框（若有）
            if (_evtBox != null && _evtBox.SelectedItem is string ts && !string.IsNullOrEmpty(ts)) type = EventTypeFromAbbr(ts);
            int line = _mode == GameMode.Phigros ? ActiveLine : -1;
            var ev = new ChartEvent { Time = _time, End = double.NaN, Type = type, Value = 0, EndValue = 0, Line = line, Ease = EaseNew };
            _events.Add(ev);
            _events.Sort((a, b) => a.Time.CompareTo(b.Time));
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        /// <summary>在播放头写入当前编辑判定线（Phigros）的 speed/alpha 等事件（右侧菜单流速/亮度/透明度）。</summary>
        void AddLineEventAtPlayhead(string type, double value, double endValue)
        {
            PushUndo();
            int line = _mode == GameMode.Phigros ? ActiveLine : -1;
            // 同一线同一时刻已有同类事件则改值（避免堆积）
            var exist = _events.Find(ev => ev.Type == type && ev.Line == line && Math.Abs(ev.Time - _time) < 1);
            if (exist != null)
            {
                exist.Value = value; exist.EndValue = endValue; exist.End = double.NaN; exist.Ease = EaseNew;
            }
            else
            {
                _events.Add(new ChartEvent { Time = _time, End = double.NaN, Type = type, Value = value, EndValue = endValue, Line = line, Ease = EaseNew });
                _events.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        void DeleteEvtSelected()
        {
            var ev = SelectedEvt();
            if (ev == null) return;
            PushUndo();
            _events.Remove(ev);
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        static string EventTypeFromAbbr(string abbr) => abbr switch
        {
            "MOVE X" or "moveX" => "moveX",
            "MOVE Y" or "moveY" => "moveY",
            "ROTATE" or "rotate" => "rotate",
            "ALPHA" or "alpha" => "alpha",
            "SPEED" or "speed" => "speed",
            _ => abbr.ToLowerInvariant()
        };

        /// <summary>按模式显示/隐藏动画菜单分区（② 动画按玩法分类：事件键帧/柱可视化全玩法通用；
        /// Phigros 判定线区/预制事件/填充曲线/执行列表/音符编辑=Phigros·Arcaea 专属；Arcaea 3D 专属）。</summary>
        void SyncAnimPanel()
        {
            bool ph = _mode == GameMode.Phigros;
            bool ar = _mode == GameMode.Arcaea;
            // ⑨ Tab 化：整 Tab 按玩法显隐（判定线=Phigros、Arcaea=Arcaea；事件/音符常显）
            // 注：TabPage.Visible 对 TabControl 不可靠——用 TabPages.Remove/Insert 物理增删
            if (_rightTabs != null)
            {
                if (_tabLine != null)
                {
                    bool hasLine = _rightTabs.TabPages.IndexOf(_tabLine) >= 0;
                    if (!ph && hasLine) _rightTabs.TabPages.Remove(_tabLine);
                    else if (ph && !hasLine) _rightTabs.TabPages.Insert(1, _tabLine);
                }
                if (_tabArc != null)
                {
                    bool hasArc = _rightTabs.TabPages.IndexOf(_tabArc) >= 0;
                    if (!ar && hasArc) _rightTabs.TabPages.Remove(_tabArc);
                    else if (ar && !hasArc) _rightTabs.TabPages.Add(_tabArc);
                }
            }
            // Phigros 判定线区（含拆线/绑线/元数据/流速·透明度/提示）：仅 Phigros
            foreach (var c in _lineSec) c.Visible = ph;
            if (ph) { RefreshLineParentUI(); RefreshLineMetaUI(); }
            // 事件键帧列表：全玩法可见（各玩法有事件类别；标题按玩法分类显示）
            if (_evtListTitle != null)
            {
                _evtListTitle.Visible = true;
                _evtListTitle.Text = ph ? "事件键帧（当前线）:" : "事件键帧（" + ModeSystem.DisplayName(_mode) + "）:";
            }
            if (_evtList != null) _evtList.Visible = true;
            if (_evTimeBox != null) { _evTimeBox.Visible = true; _evEndBox.Visible = true; _evValBox.Visible = true; _evEndValBox.Visible = true; _evEaseBox.Visible = true; _evAddBtn.Visible = true; _evDelBtn.Visible = true; }
            // 分区分类：RPE 特色编辑区（预制/填充/执行列表）→ Phigros·Arcaea；其余玩法隐藏（其事件类别=通用 bpm/mode/twirl 等）
            foreach (var c in _presetSec) c.Visible = ph || ar;
            foreach (var c in _fillSec) c.Visible = ph || ar;
            foreach (var c in _batchSec) c.Visible = ph || ar;
            foreach (var c in _arcBaseSec) c.Visible = ar;
            if (_noteSecTitle != null) RefreshNoteSection();   // D2r：音符编辑面板随选中态显示
            RefreshArcSection();   // Arcaea arc 高度编辑区随选中态/模式显示
            if (_animPanel != null) _animPanel.Visible = _rightMode != 2;   // 右侧动画菜单：全玩法可见（分区按玩法分类；t5 折叠档 2=收，尊重用户折叠）
            RefreshEvtList();   // 全玩法刷新（Phigros 按当前线过滤；其余=全局事件）
        }

        internal void SyncLineBox()
        {
            if (_lineBox == null) return;
            _lineBox.SelectedIndex = Math.Max(0, Math.Min(_lineBox.Items.Count - 1, ActiveLine));
            RefreshLineMetaUI();   // D2o：切线时回填判定线信息
        }

        /// <summary>刷新父线下拉框（phimakor 父子线：各线可选另一线为父，父线动画叠加）。</summary>
        internal void RefreshLineParentUI()
        {
            if (_lineParentBox == null) return;
            _lineParentUpdating = true;
            try
            {
                _lineParentBox.Items.Clear();
                _lineParentBox.Items.Add("（无父线）");
                for (int i = 0; i < LineCount; i++)
                    if (i != ActiveLine) _lineParentBox.Items.Add("线" + (i + 1));
                while (LineParents.Count <= ActiveLine) LineParents.Add(-1);
                int cur = LineParents[ActiveLine];
                int sel = cur >= 0 && cur != ActiveLine ? cur < ActiveLine ? cur + 1 : cur : 0;
                _lineParentBox.SelectedIndex = Math.Max(0, Math.Min(_lineParentBox.Items.Count - 1, sel));
            }
            finally { _lineParentUpdating = false; }
        }

        void ApplyLineParent()
        {
            if (_lineParentUpdating || _lineParentBox == null) return;
            while (LineParents.Count <= ActiveLine) LineParents.Add(-1);
            int idx = _lineParentBox.SelectedIndex;
            int parent = idx <= 0 ? -1 : (idx - 1 < ActiveLine ? idx - 1 : idx);
            if (LineParents[ActiveLine] == parent) return;
            PushUndo();
            LineParents[ActiveLine] = parent;
            MarkDirty();
            _canvas.Invalidate();
            RefreshLineParentUI();
        }

        /// <summary>D2o：回填判定线信息区（名称/分组/Z/Cover）到当前编辑线元数据。</summary>
        internal void RefreshLineMetaUI()
        {
            if (_lineNameBox == null) return;
            _lineMetaUpdating = true;
            try
            {
                while (LineMeta.Count <= ActiveLine) LineMeta.Add(new PhigrosLineMeta());
                var m = LineMeta[ActiveLine];
                _lineNameBox.Text = m.Name ?? "";
                _lineGroupBox.Value = Math.Max(0, Math.Min(64, m.Group));
                _lineZBox.Value = Math.Max(-100, Math.Min(100, m.Z));
                _lineCoverChk.Checked = m.Cover;
            }
            finally { _lineMetaUpdating = false; }
        }

        /// <summary>D2o：应用判定线信息区到当前编辑线元数据。</summary>
        void ApplyLineMeta()
        {
            if (_lineMetaUpdating || _lineNameBox == null) return;
            while (LineMeta.Count <= ActiveLine) LineMeta.Add(new PhigrosLineMeta());
            var m = LineMeta[ActiveLine];
            PushUndo();
            m.Name = _lineNameBox.Text ?? "";
            m.Group = (int)_lineGroupBox.Value;
            m.Z = (int)_lineZBox.Value;
            m.Cover = _lineCoverChk.Checked;
            MarkDirty();
            _canvas.Invalidate();
        }

        double BeatMs => 60000.0 / Math.Max(1, _bpm);
        double SnapStep => BeatMs / Math.Max(1, _snapDiv);
        double SnapTime(double t) => _snapOn ? Math.Round(t / SnapStep) * SnapStep : Math.Round(t);

        /* ================= 模式 / 工具常量 ================= */
        /// <summary>模式显示名列表：统一由引擎 ModeSystem 派生（已移除玩法自动不再出现；
        /// Editable=false 的玩法（回环作曲）也无对应编辑分支，不进入编辑器列表）。</summary>
        static readonly string[] ModeNames = ModeSystem.Available.Where(m => m.Editable).Select(m => m.Display).ToArray();
        static readonly ModeSystem.ModeInfo[] ModeInfos = ModeSystem.Available.Where(m => m.Editable).ToArray();

        /// <summary>节拍细分列表（1/1 … 1/64），吸附与网格细分共用。</summary>
        static readonly int[] SnapDivs = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 };
        const int SnapDefaultIndex = 3;   // 1/4

        /* ===== 编辑模式分类：有轨（列式时间轴） vs 无轨（位置场）——统一由引擎 ModeSystem 提供 ===== */
        static bool IsTrackless(GameMode m) => ModeSystem.IsTrackless(m);
        static bool IsTracked(GameMode m) => ModeSystem.IsTracked(m);

        static GameMode ModeFromIndex(int i)
        {
            if (i >= 0 && i < ModeInfos.Length) return ModeInfos[i].Mode;
            return GameMode.Mania;
        }

        /// <summary>把 GameMode 映射为可编辑模式列表中的下标（注册表顺序，已移除玩法不在列表中）。</summary>
        static int ModeToIndex(GameMode m)
        {
            for (int i = 0; i < ModeInfos.Length; i++)
                if (ModeInfos[i].Mode == m) return i;
            return 0;
        }

        /// <summary>把可能来自旧文件的模式规整为注册表内的模式（已移除玩法回退 Mania）。</summary>
        static GameMode NormalizeMode(GameMode m)
        {
            foreach (var mi in ModeSystem.All) if (mi.Mode == m) return ModeSystem.IsAvailable(m) ? m : GameMode.Mania;
            return GameMode.Mania;
        }

        /// <summary>模式固定键数：返回 -1 表示可调（Mania 4~10K）；0 表示 x 连续编辑（OsuStandard）。</summary>
        static int ModeKeyCount(GameMode m)
            => m == GameMode.Mania ? -1 : m == GameMode.OsuStandard ? 0 : ModeSystem.KeyCount(m);

        /// <summary>键数是否可调（仅 Mania 4~10K；其它模式固定）。</summary>
        static bool ModeKeyAdjustable(GameMode m) => ModeSystem.Of(m).Adjustable;

        static string ModeDisplayName(GameMode m) => ModeSystem.DisplayName(m);

        /// <summary>某音符类型在当前模式下是否可用。</summary>
        static bool TypeAvailable(string t, GameMode m) => t switch
        {
            "arc" => m == GameMode.Arcaea,
            "spin" => m == GameMode.OsuStandard,
            "flick" => m == GameMode.Phigros,
            "drag" => m == GameMode.Cytus || m == GameMode.Phigros,
            "hold" => true,
            _ => t == "tap"
        };

        /// <summary>长条类音符（含 spin 转盘；零长度占位时不算长条，见 IsLong）。</summary>
        internal static bool IsLongType(string t) => t == "hold" || t == "slide" || t == "arc" || t == "spin";
        internal bool IsLong(Note n) => n != null && IsLongType(n.Type) && n.End > n.Time;
        internal int ClampCol(int col) => Math.Max(0, Math.Min(CanvasKc - 1, col));

        internal static double Clamp01(double v) { if (double.IsNaN(v) || v < 0) return 0; return v > 1 ? 1 : v; }

        /// <summary>画布列数（编辑用）：无轨走连续场（不再使用列视觉）；有轨用键数。</summary>
        internal int CanvasKc
        {
            get
            {
                if (IsTrackless(_mode)) return 24;
                return Math.Max(1, _kc);
            }
        }

        /// <summary>是否无轨「连续位置场」编辑（供画布渲染/交互分派）。</summary>
        internal bool IsTracklessMode => IsTrackless(_mode);

        /// <summary>是否有轨（Mania/IIDX/ADOFAI/Arcaea 等，画布按列下落）。</summary>
        internal bool IsTrackedMode => !IsTrackless(_mode);

        /// <summary>无轨编辑：把连续场坐标 (fx, fy)（0..1）写入音符 X/Y/Col（按当前模式约定）。</summary>
        internal void ApplyTracklessField(Note n, double fx, double fy, int col)
        {
            if (n == null) return;
            fx = Clamp01(fx); fy = Clamp01(fy);
            switch (_mode)
            {
                case GameMode.Phigros:
                    n.X = fx; n.Y = 0.5; n.Col = -1; break;   // 判定线上自由位置（Col=-1 不按列）
                case GameMode.Cytus:
                    n.X = fx; n.Y = fy; n.Col = ((int)(fx * 4) + 4) % 4; break;
                case GameMode.OsuStandard:
                    n.X = fx; n.Y = fy; n.Col = 0; break;   // osu 连续 X/Y，Col 固定 0
                case GameMode.Maimai:
                    // QA-5：maimai 8 分区——X 落点量化到 8 列（Col 1~8 外圈），Y 固定 0.5
                    int mk = Math.Max(1, CanvasKc);
                    n.X = fx; n.Y = 0.5; n.Col = Math.Max(1, Math.Min(mk, (int)Math.Floor(fx * mk) + 1)); break;
            }
        }

        /// <summary>状态栏/键数框的模式专用显示。</summary>
        string KcDisplay()
        {
            if (IsTrackless(_mode))
                return "连续";
            return _mode switch
            {
                GameMode.Adofai => "Routlock 单轨",
                GameMode.AdofaiReal => "ADOFAI 单轨",
                _ => Math.Max(1, _kc) + "K"
            };
        }

        /* ================= 事件（各音游特色） ================= */

        /// <summary>对应此字段的事件类型（bpm/mode 全模式可用）。</summary>
        static string[] EventTypesForMode(GameMode m) => m switch
        {
            GameMode.Mania => new[] { "bpm", "mode", "noteSpeed", "scroll" },
            GameMode.Arcaea => new[] { "bpm", "mode", "noteSpeed", "scroll" },
            GameMode.Iidx => new[] { "bpm", "mode", "noteSpeed", "scroll" },
            GameMode.Phigros => new[] { "moveX", "moveY", "rotate", "alpha", "speed", "zoom", "bpm", "mode" },
            GameMode.Cytus => new[] { "speed", "bpm", "mode" },        // speed=页拍数
            GameMode.OsuStandard => new[] { "bpm", "mode" },
            GameMode.Adofai => new[] { "bpm", "mode", "twirl" },
            GameMode.AdofaiReal => new[] { "bpm", "mode", "twirl" },       // twirl=旋转段
            _ => new[] { "bpm", "mode" }
        };

        /// <summary>事件时间轴/柱可视化列通道（按玩法分类）：Phigros 保持实机五柱
        /// （RPE 判据 + edsim 几何依赖固定 5 列顺序：moveX/moveY/rotate/alpha/speed）；
        /// 其余玩法 = EventTypesForMode（如 ADOFAI = bpm/mode/twirl 三柱）。</summary>
        internal static string[] EventKindsForMode(GameMode m) => m == GameMode.Phigros
            ? new[] { "moveX", "moveY", "rotate", "alpha", "speed" }
            : EventTypesForMode(m);

        /// <summary>事件类型 → 柱宽/横拖值域（ValueToWidth01 与拖拽改值共用；未知类型 0..1）。</summary>
        internal static (double lo, double hi) EventRangeForKind(string t) => t switch
        {
            "bpm" => (30, 300),
            "speed" => (0.1, 4),
            "noteSpeed" => (0.1, 4),
            "rotate" => (-360, 360),
            "scroll" => (-1, 1),
            _ => (0, 1)
        };

        /// <summary>Arcaea 天键高度比（n.Y 0..1：0=地面、1=天空顶/天空线；NaN/缺失回退 1.0）。
        /// 天空键自由放置——编辑器 3D 渲染/命中与游玩端 ArcaeaSkyHeightRatio 同一公式。</summary>
        internal static double ArcaeaSkyHeightRatio(Note n) => n == null || double.IsNaN(n.Y) ? 1.0 : Math.Max(0, Math.Min(1, n.Y));

        /// <summary>事件类型 → 标记线颜色（类型着色）。</summary>
        internal static Color EventColor(string t) => t switch
        {
            "bpm" => Color.FromArgb(255, 255, 190, 80),
            "mode" => Color.FromArgb(255, 255, 120, 220),
            "moveX" => Color.FromArgb(255, 255, 110, 110),
            "moveY" => Color.FromArgb(255, 110, 210, 130),
            "rotate" => Color.FromArgb(255, 120, 170, 255),
            "alpha" => Color.FromArgb(255, 200, 160, 255),
            "speed" => Color.FromArgb(255, 110, 230, 230),
            "zoom" => Color.FromArgb(255, 190, 210, 255),
            "noteSpeed" => Color.FromArgb(255, 255, 150, 90),
            "scroll" => Color.FromArgb(255, 150, 235, 150),
            "appear" => Color.FromArgb(255, 210, 210, 210),
            "twirl" => Color.FromArgb(255, 255, 140, 200),
            _ => Color.FromArgb(255, 200, 200, 200)
        };

        /// <summary>事件类型 → 图标缩写。</summary>
        internal static string EventAbbr(string t) => t switch
        {
            "bpm" => "BPM",
            "mode" => "MODE",
            "moveX" => "X",
            "moveY" => "Y",
            "rotate" => "↻",
            "alpha" => "α",
            "speed" => "v",
            "zoom" => "Z",
            "noteSpeed" => "NS",
            "scroll" => "SC",
            "appear" => "AP",
            "twirl" => "TW",
            _ => "?"
        };

        /// <summary>RPE 拍:0/1 时间显示：拍从 1 计、0 小分归一 "0/1"、其余 "N:k/4"（拍长=beatMs）。
        /// 实机判据（rpe_f3_60/f120）：起始时间 0:0/1、结束时间 1:0/1、1:1/2 等。</summary>
        internal static string BeatTimeText(double tMs, double beatMs)
        {
            if (beatMs <= 1) return (tMs / 1000.0).ToString("0.00") + "s";
            double beat = tMs / beatMs;                       // 0 基拍
            long n = (long)Math.Floor(beat) + 1;              // 拍从 1 计
            double frac = beat - Math.Floor(beat);
            int k = (int)Math.Round(frac * 4.0);              // 小分（1/4 拍）
            if (k >= 4) { k = 0; n += 1; }
            return n + ":" + (k == 0 ? "0/1" : k + "/4");
        }

        /* ---------- 工具栏 ---------- */
        void BuildToolbar()
        {
            _toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
                Padding = Ui.Pad(8, 6, 8, 6), BackColor = UiColors.HeadBg
            };

            Button B(string t, Action a)
            {
                var b = new Button
                {
                    Text = t, Height = Ui.P(32), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(Ui.P(10), 0, Ui.P(10), 0),
                    Font = new Font("Microsoft YaHei UI", 11F), FlatStyle = FlatStyle.Flat,
                    BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg,
                    FlatAppearance = { BorderColor = UiColors.BorderLight }
                };
                Ui.Hover(b);
                b.Click += (s, e) => a();
                return b;
            }
            Label L(string t) => new Label { Text = t, AutoSize = true, Padding = new Padding(0, Ui.P(8), Ui.P(2), 0), ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 10.5F) };
            ComboBox C(int w) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(w), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10.5F) };

            _bpmBox = new NumericUpDown
            {
                Minimum = 30, Maximum = 999, Value = (decimal)_bpm, Width = Ui.P(90), DecimalPlaces = 1,
                Increment = 1, BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10.5F), Height = Ui.P(28)
            };
            _bpmBox.ValueChanged += (s, e) => { _bpm = (double)_bpmBox.Value; MarkDirty(); };
            _offsetBox = new NumericUpDown
            {
                Minimum = -500, Maximum = 500, Value = (decimal)_offset, Width = Ui.P(90),
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10.5F), ImeMode = ImeMode.Off, Height = Ui.P(28)
            };
            _offsetBox.ValueChanged += (s, e) => { _offset = (double)_offsetBox.Value; MarkDirty(); };
            _kcBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(76), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10.5F) };
            _kcBox.SelectedIndexChanged += (s, e) => { if (_kcUpdating) return; _kc = _kcBox.SelectedIndex + 4; MarkDirty(); };
            _snapBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.P(86), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10.5F) };
            foreach (var d in SnapDivs) _snapBox.Items.Add("1/" + d);
            _snapBox.SelectedIndex = SnapDefaultIndex;   // 1/4
            _snapBox.SelectedIndexChanged += (s, e) => { _snapDiv = SnapDivs[Math.Max(0, Math.Min(SnapDivs.Length - 1, _snapBox.SelectedIndex))]; };
            _snapChk = new CheckBox { Text = "吸附", Checked = true, ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 10.5F), Height = Ui.P(28), Padding = new Padding(Ui.P(4), Ui.P(4), 0, 0) };
            _snapChk.CheckedChanged += (s, e) => _snapOn = _snapChk.Checked;
            _gridChk = new CheckBox { Text = "网格", Checked = true, ForeColor = UiColors.BodyText, Font = new Font("Microsoft YaHei UI", 10.5F), Height = Ui.P(28), Padding = new Padding(Ui.P(4), Ui.P(4), 0, 0) };
            _gridChk.CheckedChanged += (s, e) => _showGrid = _gridChk.Checked;

            // ===== t30 分组工具栏：组卡片（谱面 / 音符 / 事件 / AI 与校准 / 音频·运输 / 视图）=====
            // t5 三栏：G(title, parent)——组卡片可挂到工具栏或左栏/右栏 Tab 流
            FlowLayoutPanel G(string title, FlowLayoutPanel parent)
            {
                var g = new FlowLayoutPanel
                {
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                    Margin = new Padding(Ui.P(2), 0, Ui.P(2), 0), BackColor = UiColors.HeadBg
                };
                var gh = new Label
                {
                    Text = title, AutoSize = true, Margin = new Padding(0),
                    Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                    ForeColor = UiColors.HeadTitle,
                    Padding = new Padding(Ui.P(10), Ui.P(8), Ui.P(2), 0)
                };
                g.Controls.Add(gh);
                g.SetFlowBreak(gh, true);
                parent.Controls.Add(g);
                parent.SetFlowBreak(g, true);
                return g;
            }

            // 组间垂直细分隔线
            Panel Sep()
            {
                var s = new Panel { Width = Ui.P(1), Height = Ui.P(30), BackColor = UiColors.BorderLight, Margin = new Padding(Ui.P(7), Ui.P(4), Ui.P(7), Ui.P(2)) };
                return s;
            }

            // ---- 组① 谱面：文件 / 模式 / 部件 / 多场 / 谱面属性（t5 三栏 → 左资源栏）----
            var gChart = G("谱面", _leftFlow);
            gChart.Controls.Add(B("🎨 新建", NewChart));
            gChart.Controls.Add(B("📂 打开谱面", OpenChart));
            gChart.Controls.Add(B("✏️ 属性", ShowChartProperties));
            gChart.Controls.Add(B("💾 保存 .mil", () => SaveMil(null)));
            _btnSaveMc = B("📤 导出 .mc", () => SaveMc(null));
            _btnExportOsu = B("📤 导出 .osu", ExportOsu);
            _btnExportAff = B("📤 导出 .aff", () => SaveAff(null));
            gChart.Controls.Add(_btnSaveMc);
            gChart.Controls.Add(_btnExportOsu);
            gChart.Controls.Add(_btnExportAff);
            gChart.Controls.Add(L("模式:"));
            _modeBox = C(150);
            foreach (var n in ModeNames) _modeBox.Items.Add(n);
            _modeBox.SelectedIndex = 0;
            _modeBox.SelectedIndexChanged += (s, e) => { if (_modeUpdating) return; ApplyMode(ModeFromIndex(_modeBox.SelectedIndex)); };
            gChart.Controls.Add(_modeBox);
            gChart.Controls.Add(L("部件:"));
            _partBox = C(150);
            _partBox.SelectedIndexChanged += (s, e) => { if (_partUpdating || _partBox.SelectedIndex < 0) return; SwitchPart(_partBox.SelectedIndex); };
            gChart.Controls.Add(_partBox);
            gChart.Controls.Add(B("➕ 部件", AddPart));
            gChart.Controls.Add(B("🗑 删部件", DeletePart));
            gChart.Controls.Add(B("🎛 多场", ToggleMultiMode));
            gChart.Controls.Add(L("X:"));
            _stX = StNum(); _stX.ValueChanged += (s, e) => ApplyStageRect();
            gChart.Controls.Add(_stX);
            gChart.Controls.Add(L("Y:"));
            _stY = StNum(); _stY.ValueChanged += (s, e) => ApplyStageRect();
            gChart.Controls.Add(_stY);
            gChart.Controls.Add(L("W:"));
            _stW = StNum(); _stW.ValueChanged += (s, e) => ApplyStageRect();
            gChart.Controls.Add(_stW);
            gChart.Controls.Add(L("H:"));
            _stH = StNum(); _stH.ValueChanged += (s, e) => ApplyStageRect();
            gChart.Controls.Add(_stH);
            gChart.Controls.Add(L("BPM:")); gChart.Controls.Add(_bpmBox);
            gChart.Controls.Add(L("偏移:")); gChart.Controls.Add(_offsetBox);
            gChart.Controls.Add(L("键数:")); gChart.Controls.Add(_kcBox);

            // ---- 组② 音符：类型 / 滑条 / 角度 / 吸附网格（t5 三栏 → 左资源栏）----
            var gNote = G("音符", _leftFlow);
            gNote.Controls.Add(L("类型:"));
            _typeBox = C(90);
            _typeBox.SelectedIndexChanged += (s, e) => { if (_typeBox.SelectedItem != null) _noteType = (string)_typeBox.SelectedItem; };
            gNote.Controls.Add(_typeBox);
            _osuCurveLabel = L("滑条:");
            gNote.Controls.Add(_osuCurveLabel);
            _osuCurveBox = C(104);
            _osuCurveBox.Items.Add("直线 L");
            _osuCurveBox.Items.Add("完美圆 P");
            _osuCurveBox.Items.Add("贝塞尔 B");
            _osuCurveBox.Items.Add("Catmull C");
            _osuCurveBox.SelectedIndex = 0;
            _osuCurveBox.SelectedIndexChanged += (s, e) =>
            {
                if (_osuCurveUpdating) return;
                _osuSliderType = "LPBC"[Math.Max(0, _osuCurveBox.SelectedIndex)];
                if (_mode == GameMode.OsuStandard && _selNote != null && _selNote.Type == "hold")
                {
                    PushUndo();
                    _selNote.SliderType = _osuSliderType;
                    MarkDirty();
                    _canvas.Invalidate();
                }
            };
            gNote.Controls.Add(_osuCurveBox);
            _adofaiAngleLabel = L("角度:");
            gNote.Controls.Add(_adofaiAngleLabel);
            _adofaiAngleBox = new NumericUpDown
            {
                Minimum = -360, Maximum = 360, Value = 0, Width = Ui.P(80), Increment = 15,
                BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10.5F), Height = Ui.P(28)
            };
            _adofaiAngleBox.ValueChanged += (s, e) =>
            {
                if (_adofaiAngleUpdating) return;
                if (_selNote == null || (_mode != GameMode.Adofai && _mode != GameMode.AdofaiReal)) return;
                PushUndo();
                _selNote.Kind = NormalizeAdofaiAngle((double)_adofaiAngleBox.Value);
                MarkDirty();
                _canvas.Invalidate();
            };
            gNote.Controls.Add(_adofaiAngleBox);
            gNote.Controls.Add(L("细分:")); gNote.Controls.Add(_snapBox);
            gNote.Controls.Add(_snapChk); gNote.Controls.Add(_gridChk);

            // ---- t5 三栏：最近谱面（近 8 条，AppConfig 持久化）----
            var gRecent = G("最近谱面", _leftFlow);
            BuildRecentChartButtons(gRecent);

            // ---- 组③ 事件 / 组④ AI 与校准 / 组⑤ 音频·运输：t5 三栏已整体迁入右栏
            // 「事件」Tab（事件下拉/➕/⚡）、「AI」Tab（校准/检查/助手）、「预览」Tab（播放/自动游玩/AI 游玩）——功能零删除。

            // ---- 组⑥ 视图（时间缩放）→ 顶部工具栏保留 ----
            var gView = G("视图", _toolbar);
            gView.Controls.Add(L("缩放:"));
            _zoomBox = new NumericUpDown
            {
                Minimum = 3, Maximum = 8000, DecimalPlaces = 0, Increment = 25, Value = 150, Width = Ui.P(70),
                Font = new Font("Microsoft YaHei UI", 10.5F), Height = Ui.P(28)
            };
            _zoomBox.ValueChanged += (s, e) =>
            {
                if (_zoomUpdating) return;
                PxPerMs = (double)_zoomBox.Value / 1000.0;
                if (_canvas != null) _canvas.Invalidate();
            };
            gView.Controls.Add(_zoomBox);
            var zoomTip = L("(px/s·Ctrl+滚轮)");
            zoomTip.ForeColor = UiColors.Muted;
            gView.Controls.Add(zoomTip);
            _toolbar.Controls.Add(Sep());

            // ===== t5 三栏：工具栏保留高频快捷（保存/试玩/主菜单）+ 双收态「≡ 面板」 =====
            _toolbar.Controls.Add(B("💾 保存 .mil", () => SaveMil(null)));
            _toolbar.Controls.Add(B("🎮 试玩", PlayTest));
            _toolbar.Controls.Add(B("🏠 主菜单", () => GoHome?.Invoke()));
            _btnDrawer = B("≡ 面板", RestorePanelsFromFocus);
            _btnDrawer.Visible = false;
            _toolbar.Controls.Add(_btnDrawer);

            _status = new Label { AutoSize = true, Padding = new Padding(Ui.P(4), Ui.P(8), 0, 0), ForeColor = UiColors.Muted, Font = new Font("Microsoft YaHei UI", 10F) };
            _toolbar.Controls.Add(_status);

            SyncModeControls();
            SyncZoomBoxPublic();
        }

        /// <summary>⑤⑥：缩放框回读（Ctrl+滚轮/代码改 PxPerMs 后同步显示）。</summary>
        internal void SyncZoomBoxPublic()
        {
            if (_zoomBox == null) return;
            _zoomUpdating = true;
            try
            {
                _zoomBox.Value = Math.Max(_zoomBox.Minimum, Math.Min(_zoomBox.Maximum, (decimal)Math.Round(PxPerMs * 1000)));
            }
            finally { _zoomUpdating = false; }
        }

        /// <summary>根据当前 _mode/_kc 同步工具栏控件（模式框、键数框、类型/Kind 框、导出按钮）。不标记脏。</summary>
        void SyncModeControls()
        {
            bool adjustable = ModeKeyAdjustable(_mode);
            _kc = adjustable ? Math.Max(4, Math.Min(10, _kc)) : ModeKeyCount(_mode);

            _kcUpdating = true;
            _kcBox.Items.Clear();
            if (adjustable)
            {
                for (int k = 4; k <= 10; k++) _kcBox.Items.Add(k + "K");
                _kcBox.SelectedIndex = _kc - 4;
                _kcBox.Enabled = true;
            }
            else
            {
                _kcBox.Items.Add(KcDisplay());
                _kcBox.SelectedIndex = 0;
                _kcBox.Enabled = false;
            }
            _kcUpdating = false;

            _modeUpdating = true;
            _modeBox.SelectedIndex = ModeToIndex(_mode);
            _modeUpdating = false;

            RefreshTypeBox();
            RefreshEventBox();

            // 滑条类型（osu）/ 角度框（ADOFAI）可见性与启用态
            SyncOsuCurveBox();
            SyncAdofaiAngleBox();

            bool mania = _mode == GameMode.Mania;
            _btnSaveMc.Enabled = mania;
            _btnExportOsu.Enabled = mania;
            _tip.SetToolTip(_btnSaveMc, mania ? "导出 Malody .mc（下落式）" : "仅下落式模式支持");
            _tip.SetToolTip(_btnExportOsu, mania ? "导出 osu!mania .osu（下落式）" : "仅下落式模式支持");
            bool arcaea = _mode == GameMode.Arcaea;
            _btnExportAff.Enabled = arcaea;
            _tip.SetToolTip(_btnExportAff, arcaea ? "导出 Arcaea .aff（官方/ArcCreate 兼容）" : "仅 Arcaea 模式支持");
        }

        void RefreshTypeBox()
        {
            _typeBox.Items.Clear();
            _typeBox.Items.Add("tap");
            _typeBox.Items.Add("hold");
            if (_mode == GameMode.Arcaea) _typeBox.Items.Add("arc");
            if (_mode == GameMode.Cytus) _typeBox.Items.Add("drag");                                        // Cytus：drag（视作 tap）
            if (_mode == GameMode.Phigros) { _typeBox.Items.Add("drag"); _typeBox.Items.Add("flick"); }      // Phigros：drag 只计连击 / flick 同 tap 判定
            if (_mode == GameMode.OsuStandard) _typeBox.Items.Add("spin");                                  // osu 转盘
            int idx = _typeBox.Items.IndexOf(_noteType);
            _typeBox.SelectedIndex = idx >= 0 ? idx : 0;
            _noteType = (string)_typeBox.SelectedItem;
        }

        /// <summary>同步 osu 滑条类型下拉（仅 OsuStandard 显示；选中滑条时反映其类型）。</summary>
        void SyncOsuCurveBox()
        {
            if (_osuCurveBox == null) return;
            _osuCurveBox.Visible = _osuCurveLabel.Visible = _mode == GameMode.OsuStandard;
            if (_mode != GameMode.OsuStandard) return;
            if (_selNote != null && _selNote.Type == "hold" && _selNote.SliderType != '\0')
                _osuSliderType = _selNote.SliderType;
            int idx = "LPBC".IndexOf(_osuSliderType);
            if (idx < 0) idx = 0;
            _osuCurveUpdating = true;
            _osuCurveBox.SelectedIndex = idx;
            _osuCurveUpdating = false;
        }

        /// <summary>同步 ADOFAI 角度框（仅 ADOFAI 显示；选中 tile 启用并显示其 Kind）。</summary>
        void SyncAdofaiAngleBox()
        {
            if (_adofaiAngleBox == null) return;
            bool show = _mode == GameMode.Adofai || _mode == GameMode.AdofaiReal;
            _adofaiAngleBox.Visible = _adofaiAngleLabel.Visible = show;
            if (!show) return;
            _adofaiAngleBox.Enabled = _selNote != null;
            _adofaiAngleUpdating = true;
            _adofaiAngleBox.Value = _selNote != null ? (decimal)NormalizeAdofaiAngle(_selNote.Kind) : 0m;
            _adofaiAngleUpdating = false;
        }

        /// <summary>公开版角度框同步（画布节点点击选中后调用）。</summary>
        internal void SyncAdofaiAngleBoxPublic() => SyncAdofaiAngleBox();

        /// <summary>ADOFAI 角度规整到 [-180, 180]（Kind 存转角偏移；0=默认回退线性累计）。</summary>
        internal static int NormalizeAdofaiAngle(double deg)
        {
            int d = (int)Math.Round(deg);
            d = ((d + 180) % 360 + 360) % 360 - 180;
            return d;
        }

        /// <summary>按当前模式过滤事件类型下拉（按模式过滤 + 各音游特色）。</summary>
        void RefreshEventBox()
        {
            if (_evtBox == null) return;
            string cur = _evtBox.SelectedItem as string;
            var types = EventTypesForMode(_mode);
            _evtBox.Items.Clear();
            foreach (var t in types) _evtBox.Items.Add(t);
            int idx = Array.IndexOf(types, cur);
            if (idx < 0) idx = types.Length > 1 ? 1 : 0;
            _evtBox.SelectedIndex = idx;
        }

        void ApplyMode(GameMode m)
        {
            _mode = m;
            _kc = ModeKeyAdjustable(m) ? Math.Max(4, Math.Min(10, _kc)) : ModeKeyCount(m);
            if (!TypeAvailable(_noteType, m)) _noteType = "tap";
            SyncModeControls();
            SyncAnimPanel();
            SyncLineCountFromData();
            RefreshPartBox();
            _time = 0; _scrollMs = 0;
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>从音符/事件推导判定线数量（Phigros 多线，无上限；切换/载入时调用）。</summary>
        void SyncLineCountFromData()
        {
            int m = 1;
            foreach (var n in _notes) if (n != null && n.Line >= m) m = n.Line + 1;
            foreach (var ev in _events) if (ev != null && ev.Line >= m) m = ev.Line + 1;
            LineCount = Math.Max(1, Math.Min(64, m));
            if (ActiveLine >= LineCount) ActiveLine = LineCount - 1;
            while (LineParents.Count < LineCount) LineParents.Add(-1);
            if (LineParents.Count > LineCount) LineParents.RemoveRange(LineCount, LineParents.Count - LineCount);
            // D2o：判定线元数据随线数同步（缺口补空）
            while (LineMeta.Count < LineCount) LineMeta.Add(new PhigrosLineMeta());
            if (LineMeta.Count > LineCount) LineMeta.RemoveRange(LineCount, LineMeta.Count - LineCount);
            if (_lineCountBox != null) { _lineCountBox.Value = LineCount; }
            SyncLineBox();
            RefreshLineParentUI();
            RefreshLineMetaUI();
        }

        /* ---------- 状态 / 数据 ---------- */
        void UpdateStatus()
        {
            double end = _notes.Count > 0 ? _notes.Max(n => n.End) : 0;
            string evt = _events.Count > 0 ? " · " + _events.Count + " 事件" : "";
            string track = IsTrackless(_mode) ? "无轨" : "有轨";
            _status.Text = "🎵 " + _title + " · " + ModeDisplayName(_mode) + " " + KcDisplay() + " · " + track + " · " + _notes.Count + " 音符 · 时长 " + (end / 1000.0).ToString("0.0") + "s" + evt + " · 音频 " + _audioName + (_dirty ? " · ●未保存" : "");
            if (_propLbl != null)
            {
                string sel = _selNote != null
                    ? "选中音符：" + (_selNote.Type ?? "tap") + " @ " + _selNote.Time.ToString("0") + "ms"
                        + (_selNote.End > _selNote.Time ? " → " + _selNote.End.ToString("0") + "ms" : "")
                        + (_selNote.X >= 0 ? " · X=" + _selNote.X.ToString("0.00") : "") + (_selNote.Y >= 0 ? " · Y=" + _selNote.Y.ToString("0.00") : "")
                    : "未选中音符";
                _propLbl.Text = "工程：" + _title + "\n模式：" + ModeDisplayName(_mode) + " · " + KcDisplay()
                    + "\nBPM " + _bpm.ToString("0.#") + " · 偏移 " + _offset.ToString("0.##") + "ms"
                    + "\n音符 " + _notes.Count + " · 事件 " + _events.Count
                    + "\n" + sel;
            }
        }

        internal void MarkDirty() { _dirty = true; if (_canvas != null) _canvas.EvStructDirty = true; UpdateStatus(); }

        internal void PushUndo()
        {
            _undoStack.Add(new UndoState { Notes = CloneNotes(), Events = CloneEvents() });
            if (_undoStack.Count > MaxUndo) _undoStack.RemoveAt(0);
            _redoStack.Clear();   // 新操作后重做历史失效（C4：重做补齐）
        }
        List<Note> CloneNotes() => _notes.ConvertAll(n => new Note
        {
            Time = n.Time, End = n.End, Col = n.Col,
            X = n.X, Y = n.Y, EndX = n.EndX, EndY = n.EndY, EndCol = n.EndCol,
            Type = n.Type, Kind = n.Kind, Line = n.Line,
            SliderType = n.SliderType, Repeats = n.Repeats,
            Curve = n.Curve == null ? null : new List<(double X, double Y)>(n.Curve),
            Arc3 = n.Arc3 == null ? null : new List<(double X, double Y, double Z)>(n.Arc3),
            Side = n.Side, Width = n.Width, Alpha = n.Alpha, VisMs = n.VisMs,
            Decor = n.Decor,   // P0 数据保真：arc 虚实撤销不丢（此前 CloneNotes 未复制）
            Bpm = n.Bpm        // researcher 诊断点名：ADOFAI 变速段音符 BPM 快照不丢
        });

        /// <summary>深拷贝单个音符（Ctrl+C 剪贴板用）。</summary>
        internal Note CloneNotes(Note n) => n == null ? null : new Note
        {
            Time = n.Time, End = n.End, Col = n.Col,
            X = n.X, Y = n.Y, EndX = n.EndX, EndY = n.EndY, EndCol = n.EndCol,
            Type = n.Type, Kind = n.Kind, Line = n.Line,
            SliderType = n.SliderType, Repeats = n.Repeats,
            Curve = n.Curve == null ? null : new List<(double X, double Y)>(n.Curve),
            Arc3 = n.Arc3 == null ? null : new List<(double X, double Y, double Z)>(n.Arc3),
            Side = n.Side, Width = n.Width, Alpha = n.Alpha, VisMs = n.VisMs,
            Decor = n.Decor,   // P0 数据保真：arc 虚实剪贴板不丢
            Bpm = n.Bpm        // researcher 诊断点名：ADOFAI 变速段音符 BPM 剪贴板不丢
        };

        /// <summary>复制剪贴板（Ctrl+C）。</summary>
        internal Note ClipboardNote;

        /// <summary>粘贴音符（Ctrl+V）：PushUndo + 去重 + 选中新音符（C4 补齐）。</summary>
        internal void PasteNote(Note p)
        {
            if (p == null) return;
            PushUndo();
            bool dup = false;
            foreach (var n in _notes)
                if (Math.Abs(n.Time - p.Time) < 1e-6 && n.Col == p.Col && Math.Abs(n.X - p.X) < 1e-6 && Math.Abs(n.Y - p.Y) < 1e-6)
                { dup = true; break; }
            if (!dup) { _notes.Add(p); _selNote = p; }
            _notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            MarkDirty();
            Invalidate();
        }
        List<ChartEvent> CloneEvents() => _events.ConvertAll(ev => new ChartEvent
        {
            Time = ev.Time, End = ev.End, Type = ev.Type, Value = ev.Value, EndValue = ev.EndValue,
            Line = ev.Line, Ease = ev.Ease, Group = ev.Group,
            // P0 数据保真：Next + Bezier（深拷贝数组）—— undo/重做快照不丢贝塞尔/next（D2f/D2j 损失记录）
            Next = ev.Next,
            Bezier = ev.Bezier == null ? null : (double[])ev.Bezier.Clone()
        });

        void Undo()
        {
            if (_undoStack.Count == 0) return;
            var state = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(new UndoState { Notes = CloneNotes(), Events = CloneEvents() });
            if (_redoStack.Count > MaxUndo) _redoStack.RemoveAt(0);
            _notes.Clear();
            _notes.AddRange(state.Notes ?? new List<Note>());
            _events.Clear();
            _events.AddRange(state.Events ?? new List<ChartEvent>());
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>重做（Ctrl+Y；C4 补齐——与 Undo 对称，PushUndo 后失效）。</summary>
        void Redo()
        {
            if (_redoStack.Count == 0) return;
            var state = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(new UndoState { Notes = CloneNotes(), Events = CloneEvents() });
            if (_undoStack.Count > MaxUndo) _undoStack.RemoveAt(0);
            _notes.Clear();
            _notes.AddRange(state.Notes ?? new List<Note>());
            _events.Clear();
            _events.AddRange(state.Events ?? new List<ChartEvent>());
            MarkDirty();
            _canvas.Invalidate();
        }

        public void NewChart()
        {
            if (_dirty && !ConfirmDiscard()) return;
            if (!ShowNewChartDialog(out var mode, out var kc)) return;
            _mode = mode;
            _kc = ModeKeyAdjustable(mode) ? kc : ModeKeyCount(mode);
            _notes = new List<Note>();
            _events = new List<ChartEvent>();
            LineParents = new List<int>();
            LineMeta = new List<PhigrosLineMeta>();   // D2o：新谱面元数据清零
            _parts = new List<ChartPart>();   // 单模式工程起步；可后续加部件
            _partIndex = 0;
            _undoStack.Clear(); _redoStack.Clear();
            _title = "未命名谱面"; _artist = "未知作者"; _version = "";
            _danName = ""; _danSet = "";
            _bpm = 120; _offset = 0;
            _noteType = "tap";
            _chartPath = "";
            _bpmBox.Value = 120; _offsetBox.Value = 0;
            SyncModeControls();
            SyncAnimPanel();
            SyncLineCountFromData();
            RefreshPartBox();
            _time = 0; _scrollMs = 0;
            _playing = false; SyncPlayButton();
            _dirty = false;
            UpdateStatus();
            _canvas.Invalidate();
        }

        /// <summary>新建谱面/添加模式的小对话框：选择模式与键数。</summary>
        bool ShowNewChartDialog(out GameMode mode, out int kc, string title = "新建谱面")
        {
            mode = GameMode.Mania; kc = 4;
            using var f = new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ClientSize = new Size(Ui.P(360), Ui.P(180)), Font = new Font("Microsoft YaHei UI", 10.5F),
                BackColor = UiColors.Bg, ForeColor = UiColors.Fg
            };
            var l1 = new Label { Text = "模式：", AutoSize = true, Location = new Point(Ui.P(20), Ui.P(26)), ForeColor = UiColors.BodyText };
            var cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(Ui.P(90), Ui.P(22)), Width = Ui.P(210), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            foreach (var n in ModeNames) cbMode.Items.Add(n);
            cbMode.SelectedIndex = 0;
            var l2 = new Label { Text = "键数：", AutoSize = true, Location = new Point(Ui.P(20), Ui.P(66)), ForeColor = UiColors.BodyText };
            var numKc = new NumericUpDown { Minimum = 1, Maximum = 10, Value = 4, Location = new Point(Ui.P(90), Ui.P(62)), Width = Ui.P(90), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle };
            cbMode.SelectedIndexChanged += (s, e) =>
            {
                var m = ModeFromIndex(cbMode.SelectedIndex);
                bool adjustable = ModeKeyAdjustable(m);
                numKc.Enabled = adjustable;
                numKc.Value = adjustable
                    ? Math.Max(4, Math.Min(10, numKc.Value))
                    : Math.Max(1, ModeKeyCount(m));
            };
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(Ui.P(130), Ui.P(110)), Size = new Size(Ui.P(100), Ui.P(34)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(Ui.P(245), Ui.P(110)), Size = new Size(Ui.P(100), Ui.P(34)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            Ui.Hover(ok); Ui.Hover(cancel);
            f.Controls.AddRange(new Control[] { l1, cbMode, l2, numKc, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;
            if (f.ShowDialog(this) != DialogResult.OK) return false;
            mode = ModeFromIndex(cbMode.SelectedIndex);
            kc = Math.Max(4, Math.Min(10, (int)numKc.Value));
            return true;
        }

        bool ConfirmDiscard()
            => MessageBox.Show("当前谱面有未保存的修改，确定放弃？", "谱面编辑器", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

        /// <summary>谱面属性编辑弹窗：标题/艺术家/版本/段位名（C4 补齐，社区编辑器标配）。</summary>
        void ShowChartProperties()
        {
            using var f = new Form
            {
                Text = "谱面属性", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ClientSize = new Size(Ui.P(400), Ui.P(230)), Font = new Font("Microsoft YaHei UI", 10.5F),
                BackColor = UiColors.Bg, ForeColor = UiColors.Fg
            };
            Label Lbl(string t, int y) => new Label { Text = t, AutoSize = true, Location = new Point(Ui.P(20), Ui.P(y)), ForeColor = UiColors.BodyText };
            TextBox Txt(string v, int y) => new TextBox { Text = v, Location = new Point(Ui.P(110), Ui.P(y)), Width = Ui.P(260), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle };
            var tTitle = Txt(_title, 20);
            var tArtist = Txt(_artist, 60);
            var tVersion = Txt(_version, 100);
            var tDan = Txt(_danName, 140);
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(Ui.P(130), Ui.P(180)), Size = new Size(Ui.P(100), Ui.P(34)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(Ui.P(245), Ui.P(180)), Size = new Size(Ui.P(100), Ui.P(34)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            Ui.Hover(ok); Ui.Hover(cancel);
            f.Controls.AddRange(new Control[] { Lbl("标题：", 24), tTitle, Lbl("艺术家：", 64), tArtist, Lbl("版本：", 104), tVersion, Lbl("段位名：", 144), tDan, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;
            if (f.ShowDialog(this) != DialogResult.OK) return;
            _title = tTitle.Text.Trim(); _artist = tArtist.Text.Trim(); _version = tVersion.Text.Trim(); _danName = tDan.Text.Trim();
            if (_title.Length == 0) _title = "未命名谱面";
            MarkDirty();
            UpdateStatus();
        }

        /* ---------- 单一谱面多模式（部件） ---------- */

        /// <summary>把当前编辑状态写回活动部件（模式/键数/音符/事件）。</summary>
        void SaveActivePart()
        {
            if (_parts.Count == 0) return;
            int idx = Math.Max(0, Math.Min(_partIndex, _parts.Count - 1));
            var p = _parts[idx];
            p.Mode = _mode;
            p.KeyCount = _kc;
            p.Notes = new List<Note>(_notes);
            p.Events = new List<ChartEvent>(_events);
            p.LineParents = new List<int>(LineParents);
            p.LineMeta = new List<PhigrosLineMeta>(LineMeta);
            if (string.IsNullOrEmpty(p.Name)) p.Name = ModeDisplayName(_mode);
        }

        /// <summary>把部件数据载入编辑器状态（模式/键数/音符/事件 + UI 同步）。</summary>
        void ApplyPartToEditor(ChartPart p)
        {
            if (p == null) return;
            _mode = NormalizeMode(p.Mode);
            _kc = ModeKeyAdjustable(_mode) ? Math.Max(4, Math.Min(10, p.KeyCount)) : ModeKeyCount(_mode);
            _notes = p.Notes ?? new List<Note>();
            _events = p.Events ?? new List<ChartEvent>();
            LineParents = p.LineParents == null ? new List<int>() : new List<int>(p.LineParents);
            LineMeta = p.LineMeta == null ? new List<PhigrosLineMeta>() : new List<PhigrosLineMeta>(p.LineMeta);
            RecoverTracklessFields(_notes, _mode);
            _noteType = "tap";
            SyncModeControls();
            SyncAnimPanel();
            SyncLineCountFromData();
            _time = 0; _scrollMs = 0;
            _playing = false; SyncPlayButton();
            _canvas.Invalidate();
        }

        /// <summary>切换到第 idx 个部件（保存当前 → 载入目标 → 清撤销栈）。</summary>
        void SwitchPart(int idx)
        {
            if (_parts.Count == 0 || idx < 0 || idx >= _parts.Count || idx == _partIndex) return;
            SaveActivePart();
            _partIndex = idx;
            ApplyPartToEditor(_parts[idx]);
            RefreshPartBox();
            SyncStageBoxes();
            _undoStack.Clear(); _redoStack.Clear();
            UpdateStatus();
        }

        /// <summary>刷新部件框条目并保持选中项。</summary>
        void RefreshPartBox()
        {
            if (_partBox == null) return;
            _partUpdating = true;
            _partBox.Items.Clear();
            foreach (var p in _parts)
            {
                if (p == null) continue;
                string name = string.IsNullOrEmpty(p.Name) ? ModeDisplayName(p.Mode) : p.Name;
                _partBox.Items.Add(name + " · " + ModeDisplayName(p.Mode) + " · " + Math.Max(1, p.KeyCount) + "K");
            }
            if (_parts.Count == 0)
                _partBox.Items.Add("单模式 · " + ModeDisplayName(_mode) + " · " + Math.Max(1, _kc) + "K");
            if (_partIndex >= 0 && _partIndex < _partBox.Items.Count) _partBox.SelectedIndex = _partIndex;
            else if (_partBox.Items.Count > 0) _partBox.SelectedIndex = 0;
            _partUpdating = false;
        }

        /// <summary>添加一个空部件（选择模式+键数），并切换到它。</summary>
        void AddPart()
        {
            if (!ShowNewChartDialog(out var mode, out var kc, "添加模式部件")) return;
            SaveActivePart();
            _parts.Add(new ChartPart
            {
                Name = ModeDisplayName(mode),
                Mode = mode,
                KeyCount = ModeKeyAdjustable(mode) ? kc : ModeKeyCount(mode),
                Notes = new List<Note>(),
                Events = new List<ChartEvent>()
            });
            _partIndex = _parts.Count - 1;
            ApplyPartToEditor(_parts[_partIndex]);
            RefreshPartBox();
            if (_multiMode)
            {
                _stageLayouts.Add(new Stage
                {
                    Id = _partIndex,
                    Name = ModeDisplayName(mode),
                    X = (double)_partIndex / Math.Max(1, _parts.Count), Y = 0,
                    W = 1.0 / Math.Max(1, _parts.Count), H = 1
                });
                SyncStageBoxes();
            }
            _undoStack.Clear(); _redoStack.Clear();
            MarkDirty();
        }

        /// <summary>删除当前部件（≥1 保留；=1 时清空为单模式工程）。</summary>
        void DeletePart()
        {
            if (_parts.Count == 0) return;
            if (_parts.Count == 1)
            {
                _parts.Clear();
                _partIndex = 0;
                RefreshPartBox();
                MarkDirty();
                _canvas.Invalidate();
                return;
            }
            if (MessageBox.Show("删除当前部件「" + _parts[_partIndex].Name + "」？", "谱面编辑器", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SaveActivePart();
            _parts.RemoveAt(_partIndex);
            _partIndex = Math.Max(0, _partIndex - 1);
            ApplyPartToEditor(_parts[_partIndex]);
            RefreshPartBox();
            if (_multiMode)
            {
                while (_stageLayouts.Count > _parts.Count) _stageLayouts.RemoveAt(_stageLayouts.Count - 1);
                SyncStageBoxes();
            }
            _undoStack.Clear(); _redoStack.Clear();
            MarkDirty();
        }

        /* ---------- 多场同屏（t27）---------- */

        NumericUpDown StNum()
        {
            return new NumericUpDown
            {
                Width = Ui.P(52), Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1m,
                Font = new Font("Microsoft YaHei UI", 10F), BackColor = Color.White, ForeColor = Color.Black
            };
        }

        void ToggleMultiMode()
        {
            _multiMode = !_multiMode;
            if (_multiMode)
            {
                int n = Math.Max(1, _parts.Count == 0 ? 1 : _parts.Count);
                while (_stageLayouts.Count < n)
                {
                    int idx = _stageLayouts.Count;
                    _stageLayouts.Add(new Stage
                    {
                        Id = idx,
                        Name = idx < _parts.Count ? _parts[idx].Name : "",
                        X = (double)idx / n, Y = 0, W = 1.0 / n, H = 1
                    });
                }
            }
            SyncStageBoxes();
            _canvas?.Invalidate();
        }

        void SyncStageBoxes()
        {
            if (_stX == null) return;
            var st = CurrentStageLayout();
            if (st == null) return;
            _stX.Value = (decimal)Math.Round(st.X * 100, 1);
            _stY.Value = (decimal)Math.Round(st.Y * 100, 1);
            _stW.Value = (decimal)Math.Round(st.W * 100, 1);
            _stH.Value = (decimal)Math.Round(st.H * 100, 1);
        }

        Stage CurrentStageLayout()
        {
            if (_stageLayouts.Count == 0) return null;
            int idx = Math.Max(0, Math.Min(_stageLayouts.Count - 1, _partIndex));
            return _stageLayouts[idx];
        }

        void ApplyStageRect()
        {
            var st = CurrentStageLayout();
            if (st == null || _stX == null) return;
            st.X = Math.Clamp((double)_stX.Value / 100.0, 0, 0.95);
            st.Y = Math.Clamp((double)_stY.Value / 100.0, 0, 0.95);
            st.W = Math.Clamp((double)_stW.Value / 100.0, 0.05, 1);
            st.H = Math.Clamp((double)_stH.Value / 100.0, 0.05, 1);
            MarkDirty();
            _canvas?.Invalidate();
        }

        void SyncStagesFromChart(Chart chart)
        {
            if (chart == null) return;
            _stageLayouts = new List<Stage>();
            if (chart.Stages != null && chart.Stages.Count > 0)
            {
                _multiMode = true;
                foreach (var s in chart.Stages)
                    if (s != null) _stageLayouts.Add(new Stage
                    {
                        Id = s.Id, Name = s.Name, Mode = s.Mode, KeyCount = s.KeyCount,
                        X = s.X, Y = s.Y, W = s.W, H = s.H, KeyMap = s.KeyMap
                    });
            }
            else _multiMode = false;
            SyncStageBoxes();
            _canvas?.Invalidate();
        }

        /* ---------- 音频 ---------- */
        void LoadAudio()
        {
            using var d = new OpenFileDialog { Filter = "音频|*.mp3;*.ogg;*.wav;*.m4a;*.flac;*.aac;*.opus|所有文件|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _audioPath = d.FileName;
            _audioName = Path.GetFileName(d.FileName);
            _audio.Open(d.FileName);
            _audio.SetVolume(GameSettings.Volume / 100.0);
            MarkDirty();
        }

        /// <summary>🎯 自动校准偏移：WAV(16-bit PCM) 解析音频对音（有音符时优先）；非 WAV / 无音符 → 打拍模式（边放边按 ≥8 次）。</summary>
        async void AutoCalibrateOffset()
        {
            if (string.IsNullOrEmpty(_audioPath))
            {
                MessageBox.Show(this, "请先加载音频（🎵 加载音频…）。", "🎯 自动校准偏移", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_playing) { _playing = false; if (_audio.HasMedia) _audio.Pause(); SyncPlayButton(); }

            var noteTimes = ChartAiAssistant.CollectNoteTimes(_notes);
            string path = _audioPath;

            _status.Text = "🎯 正在解析音频…";
            var wav = await Task.Run(() => ChartAiAssistant.ParseWavPcm16(path));
            if (wav.Ok && noteTimes.Length > 0)
            {
                _status.Text = "🎯 正在对音（" + wav.SampleRate + "Hz · " + wav.Pcm.Length + " 样本 · " + noteTimes.Length + " 音符）…";
                var rep = await Task.Run(() => BeatAlignEngine.Analyze(wav.Pcm, wav.SampleRate, noteTimes));
                _status.Text = "🎯 对音完成：偏移 " + rep.OffsetMs.ToString("0.##") + "ms · 置信度 " + (rep.Confidence * 100).ToString("0") + "%";
                ChartAiAssistant.ShowCalibrationResult(this, "🎯 自动校准偏移（音频对音）", rep, ApplyOffsetSuggestion);
                return;
            }

            // 非 WAV（或 WAV 无音符）→ 打拍模式
            if (!_audio.HasMedia)
            {
                MessageBox.Show(this, "音频不可用：" + (wav.Ok ? "谱面无音符（请先放置音符，或改用打拍模式）" : wav.Error),
                    "🎯 自动校准偏移", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _aiTaps.Clear();
            _aiTapActive = true;
            _time = 0;
            _audio.Seek(0);
            _playAnchorTime = 0;
            _playAnchorPos = _audio.PositionMs;
            _audio.Play();
            _playing = true;
            SyncPlayButton();
            _status.Text = "🎯 打拍校准：跟随节拍按任意键 ≥8 次（Esc 取消）";
            _canvas.Invalidate();
            MessageBox.Show(this, "打拍模式：音频已播放，请跟随节拍按任意打击键 ≥8 次（Esc 取消）",
                "🎯 自动校准偏移（打拍模式）", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>🧙 制谱助手：打开侧边面板（上手向导：对音 → 生成 → AI 检查 → 保存；重复点击置前）。</summary>
        void OpenMentorPanel()
        {
            if (_mentorPanel != null && !_mentorPanel.IsDisposed)
            {
                _mentorPanel.BringToFront(); _mentorPanel.Activate(); return;
            }
            _mentorPanel = new ChartMentor
            {
                GetAudioPath = () => _audioPath,
                GetBpm = () => _bpm,
                GetMode = () => _mode,
                GetKeyCount = () => _kc,
                GetOffset = () => _offset,
                GetChart = BuildChart,
                ImportGenerated = ImportGeneratedChart,
                RunAiCheck = RunAiChartCheck,
                SaveChart = () => SaveMil(null),
            };
            _mentorPanel.Show(this);
            _mentorPanel.Location = new Point(Right + 8, Math.Max(0, Top + 40));
            _mentorPanel.Closed += (s, e) => { _mentorPanel = null; };
        }

        /// <summary>🧙 制谱助手导入起步谱：替换当前编辑态音符/模式/键数/BPM（留撤销栈，不改模式外字段）。</summary>
        public void ImportGeneratedChart(GameMode mode, int keyCount, double bpm, double offset, List<Note> notes, string title)
        {
            PushUndo();
            _mode = mode;
            _kc = ModeKeyAdjustable(mode) ? Math.Max(4, Math.Min(10, keyCount)) : ModeKeyCount(mode);
            _bpm = Math.Max(30, Math.Min(400, bpm));
            _offset = offset;
            _notes = notes ?? new List<Note>();
            _title = string.IsNullOrEmpty(title) ? "未命名谱面" : title;
            _bpmBox.Value = (decimal)_bpm;
            _offsetBox.Value = (decimal)_offset;
            SyncModeControls();
            SyncAnimPanel();
            SyncLineCountFromData();
            RefreshPartBox();
            MarkDirty();
            _status.Text = "🧙 已导入起步谱：" + _title + "（" + _notes.Count + " 音）";
            _canvas.Invalidate();
        }

        /// <summary>🤖 AI 检查：当前编辑态构建 Chart → ChartValidator（通用 + 模式规则）→ 着色列表（双击跳时间）。</summary>
        void RunAiChartCheck()
        {
            try
            {
                var chart = BuildChart();   // 当前编辑态（保存/试玩同源）
                var issues = ChartAiAssistant.RunAiCheck(chart);
                // t12：问题列表 + 规则摘要（本地 AI 服务已禁用，不发起任何网络请求）
                ChartAiAssistant.ShowAiCheckDialog(this, "🤖 AI 检查（" + chart.Title + "）", chart, issues, t =>
                {
                    _playing = false;
                    if (_audio.HasMedia) _audio.Pause();
                    SyncPlayButton();
                    Seek(Math.Max(0, t));
                    _canvas.Invalidate();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "AI 检查失败：" + ex.Message, "🤖 AI 检查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>应用校准偏移（替换式；钳到偏移框范围，ValueChanged 回写 _offset + MarkDirty）。</summary>
        void ApplyOffsetSuggestion(double off)
        {
            double clamped = Math.Max((double)_offsetBox.Minimum, Math.Min((double)_offsetBox.Maximum, Math.Round(off)));
            _offsetBox.Value = (decimal)clamped;
            _status.Text = "🎯 已应用偏移：" + clamped.ToString("0.##") + " ms";
        }

        // ===== 制谱 AI：打拍按键捕获（EditorCanvas.OnKeyDown 经 _ed 调用） =====
        internal bool AiTapActive => _aiTapActive;

        internal void AiTapCancel()
        {
            _aiTapActive = false;
            _playing = false;
            if (_audio.HasMedia) _audio.Pause();
            SyncPlayButton();
            _status.Text = "🎯 打拍校准已取消（已收 " + _aiTaps.Count + " 拍）";
            _canvas.Invalidate();
        }

        /// <summary>收录一次打拍；达到 8 次 → 停止播放并弹校准结果。返回 true = 已完成。</summary>
        internal bool AiTapRecord()
        {
            if (!_aiTapActive) return false;
            double t = _audio.HasMedia ? _audio.PositionMs : NowMs();
            _aiTaps.Add(t);
            if (_aiTaps.Count >= 8)
            {
                _aiTapActive = false;
                _playing = false;
                if (_audio.HasMedia) _audio.Pause();
                SyncPlayButton();
                var taps = _aiTaps.ToArray();
                _status.Text = "🎯 打拍 ×" + taps.Length + " 完成，计算中…";
                var rep = BeatAlignEngine.AlignFromTaps(taps);
                _status.Text = "🎯 打拍偏移 " + rep.OffsetMs.ToString("0.##") + "ms · 置信度 " + (rep.Confidence * 100).ToString("0") + "%";
                ChartAiAssistant.ShowCalibrationResult(this, "🎯 自动校准偏移（打拍 ×" + taps.Length + "）", rep, ApplyOffsetSuggestion);
                return true;
            }
            _status.Text = "🎯 打拍校准：" + _aiTaps.Count + "/8+ 拍（Esc 取消）";
            _canvas.Invalidate();
            return false;
        }

        void TogglePlay()
        {
            if (_notes.Count == 0 && _audioPath.Length == 0) return;
            _playing = !_playing;
            if (_playing)
            {
                if (_audio.HasMedia)
                {
                    _audio.Seek(_time);
                    _playAnchorTime = _time;
                    _playAnchorPos = _audio.PositionMs;
                    _audio.Play();
                }
                else
                {
                    _playAnchorTime = _time;
                    _playAnchorPos = Environment.TickCount;
                }
            }
            else if (_audio.HasMedia) _audio.Pause();
            SyncPlayButton();
        }

        void SyncPlayButton() => _btnPlay.Text = _playing ? "⏸ 暂停" : "▶ 播放";

        void Seek(double t)
        {
            _time = Math.Max(0, t);
            if (_playing && _audio.HasMedia) _audio.Seek(_time);
            _canvas.Invalidate();
        }

        double NowMs()
        {
            if (!_playing) return _time;
            double baseNow = _audio.HasMedia ? _playAnchorTime + (_audio.PositionMs - _playAnchorPos)
                                             : _playAnchorTime + (Environment.TickCount - _playAnchorPos);
            // 动画速率：预览播放速度倍率（仅作用于播放头推进，音频不动）
            return _playAnchorTime + (baseNow - _playAnchorTime) * PlayRate;
        }

        /* ---------- 打开 / 保存 ---------- */
        /* ================= t5 三栏工作台：容器 / 折叠 / 自适应 / 最近谱面 / 自动游玩·AI 游玩 ================= */

        /// <summary>取/建 Tab 页的滚动内容流（复用同页首个 FlowLayoutPanel）。</summary>
        FlowLayoutPanel TabFlow(TabPage tp)
        {
            foreach (Control c in tp.Controls)
                if (c is FlowLayoutPanel f) return f;
            var fl = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = Ui.Pad(2, 4, 2, 4), BackColor = UiColors.HeadBg };
            tp.Controls.Add(fl);
            return fl;
        }

        /// <summary>左资源栏容器：内容流 + 48px 图标轨（折叠档1）+ 8px 折叠把手。</summary>
        void BuildLeftPanel()
        {
            _leftPanel = new Panel { Dock = DockStyle.Left, Width = LeftWidthOf(ClientSize.Width), BackColor = UiColors.HeadBg };
            _leftFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = Ui.Pad(2, 4, 2, 4), BackColor = UiColors.HeadBg };
            _leftPanel.Controls.Add(_leftFlow);
            _leftRail = new Panel { Dock = DockStyle.Fill, BackColor = UiColors.HeadBg, Visible = false };
            var railExpand = new Button { Text = "◀", Location = new Point(Ui.P(4), Ui.P(8)), Size = Ui.S(34, 30), FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg };
            Ui.Hover(railExpand);
            railExpand.Click += (s, e) => ToggleLeftPanel();
            _leftRail.Controls.Add(railExpand);
            var railHint = new Label { Text = "曲\n谱\n·\n音\n符", Location = new Point(Ui.P(8), Ui.P(48)), AutoSize = true, ForeColor = UiColors.Muted, Font = new Font("Microsoft YaHei UI", 9F) };
            _leftRail.Controls.Add(railHint);
            _leftPanel.Controls.Add(_leftRail);
            _foldLeft = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = UiColors.BorderLight, Cursor = Cursors.Hand };
            _foldLeft.Click += (s, e) => ToggleLeftPanel();
            _foldLeft.MouseEnter += (s, e) => _foldLeft.BackColor = UiColors.BlueBtn;
            _foldLeft.MouseLeave += (s, e) => _foldLeft.BackColor = UiColors.BorderLight;
            _leftPanel.Controls.Add(_foldLeft);
        }

        /// <summary>右栏壳：300(自适应)宽 + 7 Tab（属性/事件/判定线/音符/Arcaea/预览/AI）+ 8px 折叠把手。</summary>
        void BuildRightPanelShell()
        {
            _animPanel = new Panel { Dock = DockStyle.Right, Width = RightWidthOf(ClientSize.Width), BackColor = UiColors.HeadBg };
            _rightTabs = new TabControl
            {
                Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons,
                BackColor = UiColors.HeadBg, ForeColor = UiColors.Fg,
                Font = new Font("Microsoft YaHei UI", 9F),
                // t24 P1-1：标签按文字自适应（AutoSize）——t20 的 ItemSize 38 定宽在右栏收窄档溢出（t23 捕获 283 逻辑宽：
                // 7×38+内距≈274 > 273 可用，仅差 1px 即触发 FlatButtons 挤压/裁切，f23-14/17 实锤）；
                // Normal 模式各标签按文字自适配（7 标签合计 ≈237 逻辑宽 < 默认 300 档可用 290），Multiline 兜底：
                // 极窄右栏（最小 240 档）自动折成两行——不挤压、不出滚动箭头、无标签裁切。
                SizeMode = TabSizeMode.Normal, Multiline = true, Padding = new Point(2, 4)
            };
            _tabProp = new TabPage("属性"); _tabEvt = new TabPage("事件"); _tabLine = new TabPage("判定线");
            _tabNote = new TabPage("音符"); _tabArc = new TabPage("Arcaea"); _tabPreview = new TabPage("预览"); _tabAi = new TabPage("AI");
            foreach (var tp in new[] { _tabProp, _tabEvt, _tabLine, _tabNote, _tabArc, _tabPreview, _tabAi })
            {
                tp.BackColor = UiColors.HeadBg;
                _rightTabs.TabPages.Add(tp);
            }
            _animPanel.Controls.Add(_rightTabs);
            _foldRight = new Panel { Dock = DockStyle.Left, Width = 8, BackColor = UiColors.BorderLight, Cursor = Cursors.Hand };
            _foldRight.Click += (s, e) => ToggleRightPanel();
            _foldRight.MouseEnter += (s, e) => _foldRight.BackColor = UiColors.BlueBtn;
            _foldRight.MouseLeave += (s, e) => _foldRight.BackColor = UiColors.BorderLight;
            _animPanel.Controls.Add(_foldRight);
        }

        static int ClampI(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
        /// <summary>editor-trilab D6：L = clamp(round(W×200/1280), 160, 240)。</summary>
        int LeftWidthOf(int w) => ClampI((int)Math.Round(w * 200.0 / 1280.0), 160, 240);
        /// <summary>editor-trilab D6：R = clamp(round(W×300/1280), 240, 360)。</summary>
        int RightWidthOf(int w) => ClampI((int)Math.Round(w * 300.0 / 1280.0), 240, 360);

        /// <summary>应用三栏宽度：折叠档 + 画布最小 640 自动折叠（先左收轨、再右收）；变更才持久化。</summary>
        void ApplyPanelModes()
        {
            if (_leftPanel == null || _animPanel == null) return;
            int W = Math.Max(320, ClientSize.Width);
            int lw = _leftMode == 0 ? LeftWidthOf(W) : _leftMode == 1 ? 48 : 0;
            int rw = _rightMode == 0 ? RightWidthOf(W) : 0;
            if (W - lw - rw < 640)                       // 画布最小 640：先左收为图标轨，仍不足再右收
            {
                if (_leftMode == 0) lw = 48;
                if (W - lw - rw < 640) rw = 0;
            }
            _leftPanel.Width = lw;
            _animPanel.Width = rw;
            _leftPanel.Visible = lw > 0;
            _animPanel.Visible = rw > 0;
            _leftFlow.Visible = _leftMode == 0;
            _leftRail.Visible = _leftMode == 1 && lw > 0;
            if (_btnDrawer != null) _btnDrawer.Visible = lw == 0 && rw == 0;
            SavePanelPrefs();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try { ApplyPanelModes(); } catch { }
        }

        /// <summary>F8/把手：左栏 0→1→2→0 循环（开→图标轨→收）。</summary>
        internal void ToggleLeftPanel()
        {
            _leftMode = (_leftMode + 1) % 3;
            UpdateStatus();
            ApplyPanelModes();
        }

        /// <summary>F10/把手：右栏开↔收。</summary>
        internal void ToggleRightPanel()
        {
            _rightMode = _rightMode == 0 ? 2 : 0;
            UpdateStatus();
            ApplyPanelModes();
        }

        /// <summary>F9：专注模式（双收）↔ 恢复。</summary>
        internal void ToggleFocusPanels()
        {
            if (_leftMode != 2 || _rightMode != 2)
            {
                _prevLeftMode = _leftMode; _prevRightMode = _rightMode;
                _leftMode = 2; _rightMode = 2;
            }
            else { _leftMode = _prevLeftMode; _rightMode = _prevRightMode; }
            UpdateStatus();
            ApplyPanelModes();
        }

        /// <summary>双收态「≡ 面板」：恢复面板（工具栏按钮）。</summary>
        internal void RestorePanelsFromFocus() { if (_leftMode == 2 && _rightMode == 2) ToggleFocusPanels(); }

        /* ---- 折叠持久化 + 最近谱面（AppConfig，变更才落盘） ---- */

        static AppConfig _editorCfg;
        static AppConfig EditorCfg => _editorCfg ?? (_editorCfg = AppConfig.Load());

        void LoadPanelPrefs()
        {
            try
            {
                var cfg = EditorCfg;
                _leftMode = ClampI(cfg.PanelLeftMode ?? 0, 0, 2);
                _rightMode = ClampI(cfg.PanelRightMode ?? 0, 0, 2);
                _recentCharts.Clear();
                if (cfg.RecentCharts != null)
                    foreach (var p in cfg.RecentCharts)
                        if (!string.IsNullOrEmpty(p) && _recentCharts.Count < MaxRecentCharts) _recentCharts.Add(p);
            }
            catch { }
        }

        void SavePanelPrefs()
        {
            if (_leftPanel == null) return;   // ctor 装配中不落盘
            try
            {
                var cfg = EditorCfg;
                bool recentChanged = (cfg.RecentCharts?.Count ?? 0) != _recentCharts.Count;
                if (!recentChanged && cfg.RecentCharts != null)
                    for (int i = 0; i < _recentCharts.Count; i++)
                        if (!string.Equals(cfg.RecentCharts[i], _recentCharts[i], StringComparison.OrdinalIgnoreCase)) { recentChanged = true; break; }
                if (cfg.PanelLeftMode == _leftMode && cfg.PanelRightMode == _rightMode && !recentChanged) return;
                cfg.PanelLeftMode = _leftMode;
                cfg.PanelRightMode = _rightMode;
                cfg.RecentCharts = _recentCharts.Count > 0 ? new List<string>(_recentCharts) : null;
                cfg.Save();
            }
            catch { }
        }

        /// <summary>打开/保存成功后记录最近谱面（去重、置顶、限 8 条）。</summary>
        void AddRecentChart(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { path = Path.GetFullPath(path); } catch { return; }
            _recentCharts.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _recentCharts.Insert(0, path);
            while (_recentCharts.Count > MaxRecentCharts) _recentCharts.RemoveAt(_recentCharts.Count - 1);
            SavePanelPrefs();
            RefreshRecentButtons();
        }

        FlowLayoutPanel _recentGroup;
        void RefreshRecentButtons()
        {
            if (_recentGroup == null) return;
            BuildRecentChartButtons(_recentGroup);
        }

        /// <summary>重建最近谱面按钮组（保留组标题 label，重建其余内容）。</summary>
        void BuildRecentChartButtons(FlowLayoutPanel g)
        {
            _recentGroup = g;
            for (int i = g.Controls.Count - 1; i > 0; i--) g.Controls.RemoveAt(i);
            if (_recentCharts == null || _recentCharts.Count == 0)
            {
                var empty = new Label { Text = "（暂无最近谱面）", AutoSize = true, ForeColor = UiColors.Muted, Font = new Font("Microsoft YaHei UI", 9F) };
                g.Controls.Add(empty);
                return;
            }
            foreach (var p in _recentCharts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                string name = Path.GetFileName(p);   // t51：去 24 字符硬截断+省略号（禁省略号红线）——Label 行换行全文
                var b = new Label
                {
                    Text = "📄 " + name, AutoSize = true, MaximumSize = new Size(Ui.P(168), 0),
                    ForeColor = UiColors.Fg, Font = new Font("Microsoft YaHei UI", 9F),
                    Padding = new Padding(Ui.P(6), Ui.P(2), Ui.P(6), Ui.P(2)),
                    BackColor = UiColors.BtnBg, Cursor = Cursors.Hand, Tag = p
                };
                if (_tipTooltip == null) _tipTooltip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 400 };   // ToolTip(Component) 无 IsDisposed——用 null 判断
                _tipTooltip.SetToolTip(b, "📄 " + name + "\n" + p);   // t51：悬停全名+路径
                b.MouseEnter += (s, e) => b.BackColor = Ui.Tint(UiColors.BtnBg, 1.28);
                b.MouseLeave += (s, e) => b.BackColor = UiColors.BtnBg;
                string path = p;
                b.Click += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) LoadChart(path);
                    else
                    {
                        MessageBox.Show("文件不存在：" + path, "最近谱面");
                        _recentCharts.Remove(path);
                        SavePanelPrefs();
                        RefreshRecentButtons();
                    }
                };
                g.Controls.Add(b);
            }
            var clear = new Button
            {
                Text = "🗑 清空列表", Height = Ui.P(22), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Ui.P(6), 0, Ui.P(6), 0), Font = new Font("Microsoft YaHei UI", 9F),
                FlatStyle = FlatStyle.Flat, BackColor = UiColors.BtnBg, ForeColor = UiColors.Muted,
                FlatAppearance = { BorderColor = UiColors.BorderLight }
            };
            Ui.Hover(clear);
            clear.Click += (s, e) =>
            {
                _recentCharts.Clear();
                SavePanelPrefs();
                RefreshRecentButtons();
            };
            g.Controls.Add(clear);
        }

        /* ---- t5：自动游玩 / AI 游玩（同窗承载，规则驱动，不接 LLM） ---- */

        /// <summary>🅰 自动游玩：当前编辑内容 → GamePanel.StartAutoplay 同窗承载完整演示（F5/预览 Tab）。</summary>
        internal void PlayAutoplay()
        {
            if (_notes.Count == 0) { MessageBox.Show("谱面还没有音符", "谱面编辑器"); return; }
            if (string.IsNullOrEmpty(_audioPath)) { MessageBox.Show("请先加载音频（🎵 加载音频…）", "谱面编辑器"); return; }
            PauseForPreview();
            var chart = BuildChart();
            chart.Parts = null;              // 自动游玩仅当前活动部件（与试玩同源）
            double startMs = _time;          // t14 P1-1：从当前播放头位置开始预览
            _status.Text = "🅰 自动游玩预览中… ESC 返回编辑器";
            TestAutoplay?.Invoke(chart, Path.GetDirectoryName(_audioPath), startMs);
        }

        /// <summary>🤖 AI 游玩：三档（纯演示 DemoAi / 陪玩 1~3 / 对练差值），AiEngine 规则驱动（F6/预览 Tab）。</summary>
        internal void PlayAi()
        {
            if (_notes.Count == 0) { MessageBox.Show("谱面还没有音符", "谱面编辑器"); return; }
            if (string.IsNullOrEmpty(_audioPath)) { MessageBox.Show("请先加载音频（🎵 加载音频…）", "谱面编辑器"); return; }
            PauseForPreview();
            var lv = SelectedAiLevel();
            int companions = _aiCompanionBox == null ? 0 : Math.Max(0, Math.Min(3, (int)_aiCompanionBox.Value));
            var chart = BuildChart();
            chart.Parts = null;
            double startMs = _time;          // t14 P1-1：从当前播放头位置开始预览
            _status.Text = "🤖 AI 游玩（" + (lv != null ? lv.Name : "1st Dan") + " · 陪玩 " + companions + "）预览中… ESC 返回";
            TestAiPlay?.Invoke(chart, Path.GetDirectoryName(_audioPath), lv, companions, startMs);
        }

        AiLevel SelectedAiLevel()
        {
            try
            {
                var levels = AiEngine.Levels;
                if (levels == null || levels.Length == 0) return null;
                int idx = _aiLevelBox != null ? Math.Max(0, Math.Min(levels.Length - 1, _aiLevelBox.SelectedIndex)) : 0;
                return levels[idx];
            }
            catch { return null; }
        }

        /// <summary>t14 P1-1：预览退出后回写播放头（_time = 预览端当前时间；自然结束≈谱面末）。</summary>
        internal void SyncTimeFromPreview(double ms)
        {
            try
            {
                if (double.IsFinite(ms) && ms >= 0)
                {
                    _time = ms;
                    if (_canvas != null) _canvas.Invalidate();
                    UpdateStatus();
                }
            }
            catch { }
        }

        /// <summary>进入预览前冻结编辑器播放（防双声源；退出后不自动恢复——editor-trilab D8）。</summary>
        void PauseForPreview()
        {
            try
            {
                _playing = false;
                if (_audio != null && _audio.HasMedia) _audio.Pause();
                SyncPlayButton();
                _canvas.Invalidate();
            }
            catch { }
        }

        void OpenChart()
        {
            using var d = new OpenFileDialog { Filter = ChartFilter() };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            LoadChart(d.FileName);
        }

        static string ChartFilter()
            => "Milestone/所有谱面|*.mil;*.osu;*.mc;*.sm;*.ssc;*.qua;*.aff;*.txt;*.json;*.adofai" +
               "|Milestone (*.mil)|*.mil" +
               "|osu!mania (*.osu)|*.osu" +
               "|Malody (*.mc)|*.mc" +
               "|StepMania (*.sm;*.ssc)|*.sm;*.ssc" +
               "|Quaver (*.qua)|*.qua" +
               "|Arcaea (*.aff)|*.aff" +
               "|ADOFAI (*.adofai)|*.adofai" +
               "|其他文本 (*.txt;*.json)|*.txt;*.json" +
               "|所有文件|*.*";

        /// <summary>解析谱面文件并载入编辑器（MainForm / 选歌界面"编辑此谱面"调用）。失败弹 MessageBox。</summary>
        public void LoadChart(string path)
        {
            try
            {
                // 策划方案A(2026-08-21 23:45 下发):.adofai 优先 ParseAdofaiReal——打开即启用俯视路径编辑器
                // (Routlock 保留旧轮盘预览;真实 ADOFAI 走 E-2 俯视棋盘:点选/追加/角度拖拽)
                var chart = path.ToLowerInvariant().EndsWith(".adofai")
                    ? ChartParser.ParseAdofaiReal(System.IO.File.ReadAllText(path), path)
                    : ChartParser.ParseFile(path);
                // 单一谱面多模式：至少一个部件可玩即可编辑（跳过已移除玩法部件）
                bool anyAvail = chart != null && ((chart.Parts != null && chart.Parts.Count > 0)
                    ? chart.Parts.Any(p => p != null && ModeSystem.IsAvailable(p.Mode))
                    : ModeSystem.IsAvailable(chart.Mode));
                if (chart == null || !anyAvail)
                {
                    MessageBox.Show("「" + (chart != null ? ModeSystem.DisplayName(chart.Mode) : "null") + "」玩法已移除，无法编辑。\n注册表接口（ModeSystem）已保留，开发者可在未来重新接入该玩法。", "玩法已移除");
                    return;
                }
                if (_dirty && !ConfirmDiscard()) return;

                // 元数据共享（BPM/偏移/标题/艺术家/音频）
                _title = string.IsNullOrEmpty(chart.Title) ? Path.GetFileNameWithoutExtension(path) : chart.Title;
                _artist = chart.Artist ?? "";
                _version = chart.Version ?? "";
                _danName = chart.DanName ?? "";
                _danSet = chart.DanSet ?? "";
                _bpm = chart.Bpm > 0 ? chart.Bpm : 120;
                _offset = chart.Offset;

                // 单一谱面多模式：有 parts 时载入全部可玩部件；否则单模式工程
                _parts = new List<ChartPart>();
                _partIndex = 0;
                if (chart.Parts != null && chart.Parts.Count > 0)
                {
                    foreach (var p in chart.Parts)
                    {
                        if (p == null) continue;
                        var pm = NormalizeMode(p.Mode);
                        if (!ModeSystem.IsAvailable(pm)) continue;
                        _parts.Add(new ChartPart
                        {
                            Name = string.IsNullOrEmpty(p.Name) ? ModeDisplayName(pm) : p.Name,
                            Mode = pm,
                            KeyCount = ModeKeyAdjustable(pm) ? Math.Max(4, Math.Min(10, p.KeyCount)) : ModeKeyCount(pm),
                            Notes = new List<Note>(p.Notes ?? new List<Note>()),
                            Events = new List<ChartEvent>(p.Events ?? new List<ChartEvent>()),
                            LineParents = p.LineParents == null ? new List<int>() : new List<int>(p.LineParents)
                        });
                    }
                    if (_parts.Count > 0)
                    {
                        var first = _parts[0];
                        _mode = first.Mode;
                        _kc = first.KeyCount;
                        _notes = first.Notes;
                        _events = first.Events;
                        RecoverTracklessFields(_notes, first.Mode);
                        _noteType = "tap";
                    }
                }
                else
                {
                    // 单模式工程
                    var m = NormalizeMode(chart.Mode);
                    _mode = m;
                    _kc = ModeKeyAdjustable(m) ? Math.Max(4, Math.Min(10, chart.KeyCount)) : ModeKeyCount(m);
                    _notes = chart.Notes ?? new List<Note>();
                    _events = chart.Events ?? new List<ChartEvent>();
                    LineParents = chart.LineParents == null ? new List<int>() : new List<int>(chart.LineParents);
                    LineMeta = chart.LineMeta == null ? new List<PhigrosLineMeta>() : new List<PhigrosLineMeta>(chart.LineMeta);
                    RecoverTracklessFields(_notes, m);
                    _noteType = "tap";
                }

                _bpmBox.Value = (decimal)Math.Min(999, Math.Max(30, _bpm));
                _offsetBox.Value = (decimal)Math.Min(500, Math.Max(-500, _offset));
                SyncModeControls();
                SyncAnimPanel();
                SyncLineCountFromData();
                RefreshPartBox();

                // 多场同屏（t27）：载入 stages → 编辑器多场态
                SyncStagesFromChart(chart);

                // 音频：相对路径解析到谱面目录
                var dir = Path.GetDirectoryName(path) ?? "";
                _audioPath = ""; _audioName = "（未加载音频）";
                if (!string.IsNullOrEmpty(chart.AudioFile))
                {
                    var ap = Path.Combine(dir, chart.AudioFile);
                    if (File.Exists(ap))
                    {
                        _audioPath = ap;
                        _audioName = Path.GetFileName(ap);
                        _audio.Open(ap);
                        _audio.SetVolume(GameSettings.Volume / 100.0);
                    }
                }
                _chartPath = path;
                AddRecentChart(path);   // t5：打开成功 → 最近谱面（去重置顶，限 8 条）
                _undoStack.Clear(); _redoStack.Clear();
                _time = 0; _scrollMs = 0;
                _playing = false; SyncPlayButton();
                _dirty = false;
                if (_canvas != null) _canvas.EvStructDirty = true;   // t6：载入新谱 → 事件桶结构重建
                UpdateStatus();
                _canvas.Invalidate();
            }
            catch (Exception ex)
            {
                // t9：CLI 无头（--edshot/--edsim 设 SuppressSaveToast）不得弹模态框死等——记日志直接失败返回
                if (SuppressSaveToast) { Logger.Error("打开谱面失败：" + ex.Message, ex); return; }
                MessageBox.Show("打开失败：" + ex.Message, "谱面编辑器");
            }
        }

        /// <summary>载入后按模式恢复无轨连续字段（X/Y 连续，各模式按实机约定补列）。</summary>
        void RecoverTracklessFields(List<Note> notes, GameMode m)
        {
            if (notes == null || !IsTrackless(m)) return;
            foreach (var n in notes)
            {
                if (n == null) continue;
                n.X = Clamp01(n.X);
                n.Y = Clamp01(n.Y);
                switch (m)
                {
                    case GameMode.Cytus:
                        n.Col = ((int)(n.X * 4) + 4) % 4; break;
                    case GameMode.Maimai:
                        // QA-5：从 X 恢复 8 分区（Col 1~8）
                        n.Col = Math.Max(1, Math.Min(8, (int)Math.Floor(n.X * 8) + 1));
                        n.Y = 0.5;
                        break;
                    case GameMode.OsuStandard:
                        // 滑条：Curve 全点（头+控制点+尾）与 X/Y/EndX/EndY 对齐
                        if (n.Type == "hold")
                        {
                            if (n.SliderType == '\0') n.SliderType = 'L';
                            if (n.Curve == null || n.Curve.Count < 2)
                                n.Curve = new List<(double X, double Y)> { (Clamp01(n.X), Clamp01(n.Y)), (Clamp01(n.EndX), Clamp01(n.EndY)) };
                            else
                            {
                                n.Curve[0] = (Clamp01(n.X), Clamp01(n.Y));
                                n.Curve[n.Curve.Count - 1] = (Clamp01(n.EndX), Clamp01(n.EndY));
                            }
                        }
                        break;
                }
            }
        }

        /* ---------- 保存 .mil（原生 Milestone） ---------- */
        string RelativeAudioName() => string.IsNullOrEmpty(_audioPath) ? "" : Path.GetFileName(_audioPath);

        /// <summary>按给定模式/键数取画布列数（无轨连续场视觉 24；其余键数）。</summary>
        static int CanvasKcFor(GameMode m, int kc)
        {
            if (IsTrackless(m)) return 24;
            return Math.Max(1, kc);
        }

        /// <summary>按给定模式/键数把编辑态音符/事件写成 Chart（保存 / 试玩 / 部件共用）。</summary>
        Chart BuildPartChart(GameMode mode, int kc, List<Note> notes, List<ChartEvent> events)
        {
            notes = notes ?? new List<Note>();
            events = events ?? new List<ChartEvent>();
            int canvasKc = CanvasKcFor(mode, kc);
            var c = new Chart
            {
                Mode = mode,
                KeyCount = Math.Max(1, kc),
                ModeName = ModeDisplayName(mode)
            };

            foreach (var n in notes)
            {
                if (n == null) continue;
                int col = Math.Max(0, Math.Min(Math.Max(1, canvasKc) - 1, n.Col));
                string type = string.IsNullOrEmpty(n.Type) ? "tap" : n.Type;
                if (type == "double") type = "tap";   // 防御：double 不应出现在数据里
                double end = n.End;
                if (IsLongType(type) && end <= n.Time) { type = "tap"; end = n.Time; }
                else if (type == "tap" || type == "flick" || type == "drag") end = n.Time;   // flick/drag 同 tap 无尾

                // 各模式 Kind：ADOFAI 转角原样保留；其余 0
                int kind = mode switch
                {
                    GameMode.Adofai => n.Kind,
                    GameMode.AdofaiReal => n.Kind,
                    _ => 0
                };

                var nn = new Note
                {
                    Time = n.Time, End = end, Col = col, Type = type,
                    Kind = kind,
                    Line = n.Line   // Phigros 多判定线：保留音符所属线
                };
                switch (mode)
                {
                    case GameMode.Phigros:
                        // QA-1：Phigros 音符 X 位置必须落盘（此前缺失 → 重载全部贴左端 X=0）
                        nn.X = Clamp01(n.X); nn.Y = 0.5;
                        // D2r：音符编辑面板字段（Side/Width/Alpha/VisMs）随保存链带走
                        nn.Side = n.Side;
                        nn.Width = Math.Max(0.1, n.Width);
                        nn.Alpha = Math.Max(0, Math.Min(1, n.Alpha));
                        nn.VisMs = n.VisMs > 0 ? n.VisMs : 999999;
                        break;
                    case GameMode.Arcaea:
                        if (type == "arc")
                        {
                            nn.X = (col + 0.5) / 6.0; nn.EndX = nn.X;
                            int ec = n.EndCol >= 0 && n.EndCol < canvasKc ? n.EndCol : -1;
                            nn.EndCol = ec == col ? -1 : ec;   // 终点轨道（-1=同起点）
                            // QA-2：arc 高度/虚实/3D 控制点必须落盘（此前丢失 → 重载贴地/变实弧/控制点消失）
                            nn.Y = Clamp01(n.Y); nn.EndY = Clamp01(n.EndY);
                            nn.Decor = n.Decor;
                            if (n.Arc3 != null && n.Arc3.Count > 0)
                                nn.Arc3 = new List<(double X, double Y, double Z)>(n.Arc3);
                            if (n.Curve != null && n.Curve.Count > 0)
                                nn.Curve = new List<(double X, double Y)>(n.Curve);   // 可选中间控制点
                        }
                        else
                        {
                            // QA-2：天键/地键 tap/hold 的自由 X 与悬浮高度必须落盘
                            nn.X = Clamp01(n.X); nn.Y = Clamp01(n.Y);
                        }
                        break;
                    case GameMode.Cytus:
                        nn.X = Clamp01(n.X); nn.Y = Clamp01(n.Y);   // 连续位置场（X/Y 连续）
                        break;
                    case GameMode.OsuStandard:
                        if (type == "spin")
                        {
                            // 转盘：居中，End=拖长终点
                            nn.X = 0.5; nn.Y = 0.5; nn.EndX = 0.5; nn.EndY = 0.5;
                        }
                        else
                        {
                            double x = Clamp01(n.X), y = Clamp01(n.Y);   // 连续 X/Y（4:3 场）
                            nn.X = x; nn.Y = y; nn.EndX = x; nn.EndY = y;
                            if (type == "hold")
                            {
                                // 滑条：全点写入（起点+控制点+终点），SliderType/Repeats 保留
                                nn.SliderType = n.SliderType != '\0' ? n.SliderType : 'L';
                                nn.Repeats = n.Repeats >= 1 ? n.Repeats : 1;
                                nn.EndX = Clamp01(n.EndX); nn.EndY = Clamp01(n.EndY);
                                nn.Curve = n.Curve != null && n.Curve.Count >= 2
                                    ? new List<(double X, double Y)>(n.Curve)
                                    : new List<(double X, double Y)> { (x, y), (Clamp01(n.EndX), Clamp01(n.EndY)) };
                            }
                        }
                        break;
                    // Adofai/Iidx：只写 Col/Type/Kind，无额外形状字段
                }
                c.Notes.Add(nn);
            }
            c.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (var ev in events)
                c.Events.Add(new ChartEvent
                {
                    Time = ev.Time, End = ev.End, Type = ev.Type, Value = ev.Value, EndValue = ev.EndValue,
                    Line = ev.Line, Ease = ev.Ease, Group = ev.Group,
                    // P0 数据保真：Next + Bezier（深拷贝）—— 保存链不丢；Group（T59 D2j 绑定组）随行
                    Next = ev.Next,
                    Bezier = ev.Bezier == null ? null : (double[])ev.Bezier.Clone()
                });
            return c;
        }

        /// <summary>按当前编辑态构建 Chart（保存 / 试玩共用）。单一谱面多模式（&gt;1 部件）时附带 parts 数组。
        /// internal：--edsim/自动化经 SaveMil 复用（无需重复实现）。</summary>
        internal Chart BuildChart()
        {
            SaveActivePart();   // 当前编辑状态写回活动部件（若有）
            var c = BuildPartChart(_mode, _kc, _notes, _events);
            c.Title = _title;
            c.Artist = _artist;
            c.Version = _version;
            c.Bpm = _bpm;
            c.Offset = _offset;
            c.AudioFile = RelativeAudioName();
            c.DanName = _danName;
            c.DanSet = _danSet;
            c.LineParents = new List<int>(LineParents);
            if (c.LineParents.Count < LineCount)
                for (int i = c.LineParents.Count; i < LineCount; i++) c.LineParents.Add(-1);
            // D2o：判定线元数据落盘（根）
            c.LineMeta = new List<PhigrosLineMeta>(LineMeta);
            if (c.LineMeta.Count < LineCount)
                for (int i = c.LineMeta.Count; i < LineCount; i++) c.LineMeta.Add(new PhigrosLineMeta());

            // 单一谱面多模式：>1 部件时写出 parts（根 = 第 0 部件，与解析器镜像约定一致）
            if (_parts.Count > 1)
            {
                c.Parts = new List<ChartPart>();
                foreach (var p in _parts)
                {
                    if (p == null) continue;
                    var pc = BuildPartChart(p.Mode, p.KeyCount, p.Notes, p.Events);
                    // QA-7：部件级 Phigros 父线必须落盘（此前恒空 → 多部件工程父子线丢失）
                    if (p.Mode == GameMode.Phigros && p.LineParents != null)
                        pc.LineParents = new List<int>(p.LineParents);
                    // D2o：部件级判定线元数据
                    if (p.Mode == GameMode.Phigros && p.LineMeta != null)
                        pc.LineMeta = new List<PhigrosLineMeta>(p.LineMeta);
                    c.Parts.Add(new ChartPart
                    {
                        Name = string.IsNullOrEmpty(p.Name) ? ModeDisplayName(p.Mode) : p.Name,
                        Mode = p.Mode,
                        KeyCount = p.KeyCount,
                        Notes = pc.Notes,
                        Events = pc.Events,
                        LineParents = pc.LineParents,
                        LineMeta = pc.LineMeta
                    });
                }
            }
            // 多场同屏（t27）：多场编辑态写回 stages（与 parts 同索引；模式/键数同步）
            if (_multiMode && _parts.Count > 1 && _stageLayouts.Count > 0)
            {
                c.Stages = new List<Stage>();
                for (int i = 0; i < Math.Min(_parts.Count, _stageLayouts.Count); i++)
                {
                    var sl = _stageLayouts[i];
                    var pp = _parts[i];
                    var pm = pp != null ? pp.Mode : _mode;
                    c.Stages.Add(new Stage
                    {
                        Id = i,
                        Name = string.IsNullOrEmpty(sl.Name) ? (pp != null && !string.IsNullOrEmpty(pp.Name) ? pp.Name : ModeDisplayName(pm)) : sl.Name,
                        Mode = pm,
                        KeyCount = pp != null ? pp.KeyCount : _kc,
                        X = sl.X, Y = sl.Y, W = sl.W, H = sl.H,
                        KeyMap = sl.KeyMap
                    });
                }
            }
            return c;
        }

        /// <summary>把 Chart 序列化为原生 Milestone .mil JSON（统一走 ChartParser，单一事实来源）。</summary>
        static string SerializeMil(Chart c) => ChartParser.SerializeMil(c);

        /// <summary>保存原生 Milestone .mil。path==null 时弹出保存对话框（默认目录=音频目录）。</summary>
        internal void SaveMil(string path)
        {
            if (path == null)
            {
                using var d = new SaveFileDialog { Filter = "Milestone 谱面|*.mil", FileName = Sanitize(_title) + ".mil" };
                if (!string.IsNullOrEmpty(_audioPath)) d.InitialDirectory = Path.GetDirectoryName(_audioPath);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }
            try
            {
                File.WriteAllText(path, SerializeMil(BuildChart()), Encoding.UTF8);
                _chartPath = path;
                AddRecentChart(path);   // t5：保存成功 → 最近谱面
                _dirty = false;
                UpdateStatus();
                if (!SuppressSaveToast) MessageBox.Show("已保存：" + Path.GetFileName(path), "谱面编辑器");
            }
            catch (Exception ex)
            {
                if (!SuppressSaveToast) MessageBox.Show("保存失败：" + ex.Message, "谱面编辑器");
            }
        }

        /// <summary>CLI/自动化：保存时不弹保存成功提示（--edsim 等无头入口防挂起）。</summary>
        internal static bool SuppressSaveToast;

        /// <summary>保存 Malody .mc（时间按当前 BPM 转为拍号，分辨率为 1/480）。仅下落式模式。</summary>
        public void SaveMc(string path)
        {
            if (_mode != GameMode.Mania) { MessageBox.Show("仅下落式模式支持导出 .mc", "谱面编辑器"); return; }
            if (path == null)
            {
                using var d = new SaveFileDialog { Filter = "Malody 谱面|*.mc", FileName = Sanitize(_title) + ".mc" };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }
            try
            {
                int BeatNum(double t) => (int)Math.Round(Math.Max(0, t - _offset) * _bpm / 60000.0 * 480.0);
                var sb = new StringBuilder();
                sb.Append("{\n  \"meta\": {\n    \"song\": {\n      \"title\": ").Append(Js(_title))
                  .Append(",\n      \"artist\": ").Append(Js(_artist))
                  .Append(",\n      \"file\": ").Append(Js(Path.GetFileName(_audioPath))).Append("\n    },\n")
                  .Append("    \"mode_ext\": ").Append(_kc).Append(",\n    \"offset\": ").Append(_offset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append("\n  },\n")
                  .Append("  \"time\": [\n    { \"beat\": [0, 0, 1], \"bpm\": ").Append(_bpm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" }\n  ],\n")
                  .Append("  \"note\": [\n");
                for (int i = 0; i < _notes.Count; i++)
                {
                    var n = _notes[i];
                    sb.Append("    { \"beat\": [").Append(BeatNum(n.Time)).Append(", 0, 480], \"column\": ").Append(n.Col);
                    if (n.Type == "hold") sb.Append(", \"endbeat\": [").Append(BeatNum(n.End)).Append(", 0, 480]");
                    sb.Append(i < _notes.Count - 1 ? " },\n" : " }\n");
                }
                sb.Append("  ]\n}\n");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                _dirty = false;
                UpdateStatus();
                MessageBox.Show("已保存：" + Path.GetFileName(path), "谱面编辑器");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "谱面编辑器");
            }
        }

        static string Js(string s) => JsonSerializer.Serialize(s ?? "");
        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length == 0 ? "chart" : s;
        }

        /// <summary>导出 Arcaea .aff（官方/ArcCreate 兼容：AudioOffset/timing/tap/hold/arc/arctap）。仅 Arcaea 模式。</summary>
        public void SaveAff(string path)
        {
            if (_mode != GameMode.Arcaea) { MessageBox.Show("仅 Arcaea 模式支持导出 .aff", "谱面编辑器"); return; }
            if (path == null)
            {
                using var d = new SaveFileDialog { Filter = "Arcaea 谱面|*.aff", FileName = Sanitize(_title) + ".aff" };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }
            try
            {
                string aff = ChartParser.SerializeArcaea(BuildChart());
                if (SuppressSaveToast) { File.WriteAllText(path, aff, Encoding.UTF8); return; }
                File.WriteAllText(path, aff, Encoding.UTF8);
                MessageBox.Show("已导出：" + Path.GetFileName(path), "谱面编辑器");
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "谱面编辑器");
            }
        }

        void ExportOsu()
        {
            if (_mode != GameMode.Mania) { MessageBox.Show("仅下落式模式支持导出 .osu", "谱面编辑器"); return; }
            using var d = new SaveFileDialog { Filter = "osu!mania 谱面|*.osu", FileName = Sanitize(_title) + ".osu" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var sb = new StringBuilder();
                sb.Append("osu file version 14\n\n[General]\nAudioFilename: ").Append(Path.GetFileName(_audioPath))
                  .Append("\nMode: 3\nPreviewTime: -1\n\n[Metadata]\nTitle:").Append(_title)
                  .Append("\nTitleUnicode:").Append(_title)
                  .Append("\nArtist:").Append(_artist)
                  .Append("\nArtistUnicode:").Append(_artist)
                  .Append("\nCreator:ChartPlayer\nVersion:Edited\n\n[Difficulty]\nCircleSize:").Append(_kc)
                  .Append("\nSliderMultiplier:1.4\nHPDrainRate:8\nOverallDifficulty:8\nApproachRate:5\n\n[TimingPoints]\n")
                  .Append(_offset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append((60000.0 / _bpm).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(",4,1,0,100,1,0\n\n[HitObjects]\n");
                foreach (var n in _notes)
                {
                    int x = (int)(512.0 * (n.Col + 0.5) / _kc);
                    int t = (int)Math.Round(n.Time);
                    if (n.Type == "hold")
                    {
                        int e2 = (int)Math.Round(n.End);
                        sb.Append(x).Append(",192,").Append(t).Append(",128,0,").Append(e2).Append(":0:0:0:0:\n");
                    }
                    else sb.Append(x).Append(",192,").Append(t).Append(",1,0,0:0:0:0:\n");
                }
                File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("已导出：" + Path.GetFileName(d.FileName), "谱面编辑器");
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "谱面编辑器");
            }
        }

        void PlayTest()
        {
            if (_notes.Count == 0) { MessageBox.Show("谱面还没有音符", "谱面编辑器"); return; }
            var chart = BuildChart();   // 试玩当前编辑内容（所见即所玩）
            chart.Parts = null;         // 试玩仅游玩当前活动部件（根 = 活动部件）
            TestPlay?.Invoke(chart, Path.GetDirectoryName(_audioPath));
        }

        /* ---------- 画布内部接口（供 EditorCanvas 调用） ---------- */
        internal List<Note> Notes => _notes;
        internal List<ChartEvent> EventList => _events;
        internal int Kc => _kc;
        internal GameMode Mode => _mode;
        internal string NoteType => _noteType;
        internal double Time { get => _time; set { _time = Math.Max(0, value); } }
        internal double ScrollMs { get => _scrollMs; set => _scrollMs = Math.Max(0, value); }
        internal double PxPerMs
        {
            get => _pxPerMs;
            set { _pxPerMs = Math.Max(0.003, Math.Min(8, value)); SyncZoomBoxPublic(); }   // ⑤⑥：0.003≈333s 全谱概览，8=精细；任意入口同步缩放框
        }
        internal bool ShowGrid => _showGrid;
        internal bool Playing => _playing;
        internal double SnapStepMs => SnapStep;
        internal double BeatMsVal => BeatMs;
        internal double NowTime => NowMs();
        internal double SnapTimeFor(double t) => SnapTime(t);

        /// <summary>按模式生成列头文字（有轨编号/名称）。</summary>
        internal string ColumnLabel(int i)
        {
            switch (_mode)
            {
                case GameMode.Arcaea:
                    var arc = new[] { "天1", "天2", "地1", "地2", "地3", "地4" };
                    return arc[Math.Max(0, Math.Min(arc.Length - 1, i))];
                case GameMode.Adofai:
                    return "0°";   // 单列时间轴，角度默认 0（不做角度编辑）
                case GameMode.AdofaiReal:
                    return "0°";   // 真实 ADOFAI：单列时间轴，角度走角度框编辑
                case GameMode.Iidx:
                    return i == 0 ? "转盘" : i.ToString();   // 转盘列 + 1~7 键
                default:
                    return (i + 1).ToString();
            }
        }

        /// <summary>
        /// 按类型/模式给音符配色：
        /// tap 轨道色 / slide 青色 / arc 描边紫 / spin 橙色 / flick 金黄 / drag 亮蓝。
        /// </summary>
        internal Color NoteColor(Note n)
        {
            return n.Type switch
            {
                "slide" => Color.FromArgb(255, 80, 200, 220),
                "arc" => Color.FromArgb(255, 200, 120, 255),
                "spin" => Color.FromArgb(255, 255, 170, 60),
                "flick" => Color.FromArgb(255, 255, 205, 70),
                "drag" => Color.FromArgb(255, 120, 220, 255),
                _ => SkinSettings.DefaultLanes()[Math.Max(0, n.Col) % 10]
            };
        }

        internal Note HitTestNote(double x, double y, int kc, double playX, double laneW, double topPad)
        {
            for (int i = _notes.Count - 1; i >= 0; i--)
            {
                var n = _notes[i];
                double ny = topPad + (n.Time - _scrollMs) * _pxPerMs;
                double th = Math.Max(14, _pxPerMs * SnapStep * 0.9);
                int col = ClampCol(n.Col);
                double nx = playX + col * laneW;
                double hitW = laneW;
                double top = ny - th, bottom = ny;
                if (IsLong(n))
                    bottom = Math.Max(bottom, topPad + (n.End - _scrollMs) * _pxPerMs);
                if (x >= nx - hitW / 2 + 2 && x <= nx + hitW / 2 - 2 &&
                    y >= top - 3 && y <= Math.Min(bottom, _canvas.ClientSize.Height) + 3)
                    return n;
            }
            return null;
        }

        internal ChartEvent HitTestEvent(double tMs, double windowMs)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
                if (Math.Abs(_events[i].Time - tMs) < windowMs) return _events[i];
            return null;
        }

        internal void DeleteEvent(ChartEvent ev)
        {
            if (ev == null) return;
            PushUndo();
            _events.Remove(ev);
            MarkDirty();
            _canvas.Invalidate();
        }

        void AddEventAtCurrentTime()
        {
            var type = _evtBox.SelectedItem as string;
            if (string.IsNullOrEmpty(type)) type = "bpm";
            PushUndo();
            _events.Add(new ChartEvent { Time = _time, End = double.NaN, Type = type, Value = 0, EndValue = 0 });
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>缓动曲线（非线性动画，RPE/Phira 命名，与游玩端同口径）。</summary>
        public static double ApplyEaseStatic(string ease, double t)
        {
            if (string.IsNullOrEmpty(ease) || ease == "Linear") return t;
            switch (ease)
            {
                case "EaseIn":
                case "EaseInCubic": return t * t * t;
                case "EaseOut":
                case "EaseOutCubic":
                {
                    double u = 1 - t;
                    return 1 - u * u * u;
                }
                case "EaseInOut":
                case "EaseInOutCubic": return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                case "EaseInBack":
                {
                    const double c1 = 1.70158, c3 = c1 + 1;
                    return c3 * t * t * t - c1 * t * t;
                }
                case "EaseOutBack":
                {
                    const double c1 = 1.70158, c3 = c1 + 1;
                    double u = t - 1;
                    return 1 + c3 * u * u * u + c1 * u * u;
                }
                case "EaseInBounce": return 1 - ApplyEaseStatic("EaseOutBounce", 1 - t);
                case "EaseOutBounce":
                {
                    const double n1 = 7.5625, d1 = 2.75;
                    if (t < 1 / d1) return n1 * t * t;
                    if (t < 2 / d1) { t -= 1.5 / d1; return n1 * t * t + 0.75; }
                    if (t < 2.5 / d1) { t -= 2.25 / d1; return n1 * t * t + 0.9375; }
                    t -= 2.625 / d1; return n1 * t * t + 0.984375;
                }
                case "EaseInOutBack":
                {
                    const double c1 = 1.70158, c2 = c1 * 1.525;
                    double u;
                    if (t < 0.5) { u = 2 * t; return (u * u * ((c2 + 1) * u - c2)) / 2; }
                    u = 2 * t - 2;
                    return (u * u * ((c2 + 1) * u + c2) + 2) / 2;
                }
                case "in":
                case "EaseInQuad": return t * t;
                case "out":
                case "EaseOutQuad": { double u = 1 - t; return 1 - u * u; }
                case "inout":
                case "EaseInOutQuad": return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                case "back":
                {
                    double c = 1.70158, u = t - 1;
                    return u * u * ((c + 1) * u + c) + 1;
                }
                case "bounce":
                {
                    const double n1 = 7.5625, d1 = 2.75;
                    if (t < 1 / d1) return n1 * t * t;
                    if (t < 2 / d1) { t -= 1.5 / d1; return n1 * t * t + 0.75; }
                    if (t < 2.5 / d1) { t -= 2.25 / d1; return n1 * t * t + 0.9375; }
                    t -= 2.625 / d1; return n1 * t * t + 0.984375;
                }
                default: return t;
            }
        }

        /// <summary>在 atTime 处写入/更新判定线事件（PhiMaker 式拖线关键帧：同类型且时间接近则改值，否则新建）。
        /// Phigros：事件归属当前编辑线（ActiveLine），缓动曲线取动画菜单所选。</summary>
        internal void SetLineEventAt(string type, double value, double atTime)
        {
            int line = _mode == GameMode.Phigros ? ActiveLine : -1;
            double tol = Math.Max(1, SnapStepVal * 0.5);
            ChartEvent ev = null;
            foreach (var e in _events)
                if (e != null && e.Type == type && e.Line == line && Math.Abs(e.Time - atTime) < tol) { ev = e; break; }
            if (ev == null)
            {
                PushUndo();
                ev = new ChartEvent { Time = atTime, End = double.NaN, Type = type, Value = value, EndValue = value, Line = line, Ease = EaseNew };
                _events.Add(ev);
                _events.Sort((x, y) => x.Time.CompareTo(y.Time));
            }
            else
            {
                ev.Value = value; ev.EndValue = value; ev.End = double.NaN; ev.Ease = EaseNew;
            }
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>「⚡ 事件」：弹出事件列表编辑器（类型按模式过滤；添加/右键删除/双击编辑数值）。</summary>
        void ShowEventEditor()
        {
            try
            {
                using var f = new Form
                {
                    Text = "事件编辑器", StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                    ClientSize = new Size(Ui.P(520), Ui.P(388)),
                    Font = new Font("Microsoft YaHei UI", 10.5F),
                    BackColor = UiColors.Bg, ForeColor = UiColors.Fg
                };
                var typeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(Ui.P(14), Ui.P(14)), Width = Ui.P(140), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
                foreach (var t in EventTypesForMode(_mode)) typeBox.Items.Add(t);
                if (typeBox.Items.Count > 0) typeBox.SelectedIndex = 0;

                var list = new ListView
                {
                    View = View.Details, FullRowSelect = true, GridLines = true,
                    Location = new Point(Ui.P(14), Ui.P(52)), Size = new Size(Ui.P(492), Ui.P(282)),
                    BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle
                };
                list.Columns.Add("类型", Ui.P(90));
                list.Columns.Add("时间(ms)", Ui.P(100));
                list.Columns.Add("值", Ui.P(80));
                list.Columns.Add("结束值", Ui.P(80));
                list.Columns.Add("结束时间", Ui.P(95));
                list.Columns.Add("绑定组", Ui.P(55));

                void RefreshList()
                {
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (var ev in _events)
                    {
                        if (ev == null) continue;
                        var it = new ListViewItem(ev.Type ?? "bpm");
                        it.SubItems.Add(((int)Math.Round(ev.Time)).ToString());
                        it.SubItems.Add(ev.Value.ToString("0.###"));
                        it.SubItems.Add(double.IsNaN(ev.EndValue) ? "" : ev.EndValue.ToString("0.###"));
                        it.SubItems.Add(double.IsNaN(ev.End) ? "" : ((int)Math.Round(ev.End)).ToString());
                        it.SubItems.Add(ev.Group != 0 ? ev.Group.ToString() : "");
                        it.Tag = ev;
                        list.Items.Add(it);
                    }
                    list.EndUpdate();
                }
                RefreshList();

                var addBtn = new Button { Text = "添加", Location = new Point(Ui.P(160), Ui.P(12)), Size = new Size(Ui.P(80), Ui.P(32)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
                Ui.Hover(addBtn);
                addBtn.Click += (s, e) =>
                {
                    var type = typeBox.SelectedItem as string ?? "bpm";
                    PushUndo();
                    _events.Add(new ChartEvent { Time = _time, End = double.NaN, Type = type, Value = 0, EndValue = 0 });
                    MarkDirty(); _canvas.Invalidate(); RefreshList();
                };
                var delBtn = new Button { Text = "删除", Location = new Point(Ui.P(248), Ui.P(12)), Size = new Size(Ui.P(80), Ui.P(32)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
                Ui.Hover(delBtn);
                delBtn.Click += (s, e) =>
                {
                    if (list.SelectedItems.Count == 0) return;
                    if (list.SelectedItems[0].Tag is ChartEvent ev) { DeleteEvent(ev); RefreshList(); }
                };
                var closeBtn = new Button { Text = "关闭", DialogResult = DialogResult.OK, Location = new Point(Ui.P(420), Ui.P(342)), Size = new Size(Ui.P(86), Ui.P(32)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
                Ui.Hover(closeBtn);

                // 右键删除
                var menu = new ContextMenuStrip();
                var delItem = new ToolStripMenuItem("删除事件");
                delItem.Click += (s, e) =>
                {
                    if (list.SelectedItems.Count == 0) return;
                    if (list.SelectedItems[0].Tag is ChartEvent ev) { DeleteEvent(ev); RefreshList(); }
                };
                menu.Items.Add(delItem);
                list.ContextMenuStrip = menu;

                // 双击编辑数值（起始值/结束值/结束时间）
                list.MouseDoubleClick += (s, e) =>
                {
                    if (list.SelectedItems.Count == 0) return;
                    if (list.SelectedItems[0].Tag is ChartEvent ev && PromptEventEdit(ev))
                    {
                        MarkDirty(); _canvas.Invalidate(); RefreshList();
                    }
                };

                f.Controls.AddRange(new Control[] { typeBox, list, addBtn, delBtn, closeBtn });
                f.AcceptButton = closeBtn; f.CancelButton = closeBtn;
                f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开事件编辑器失败：" + ex.Message, "谱面编辑器");
            }
        }

        /// <summary>双击编辑事件数值：弹出小对话框改 Value / EndValue / End / 绑定组（T59 D2j：改组号即把本事件头尾/缓动同步到组内其余事件）。</summary>
        bool PromptEventEdit(ChartEvent ev)
        {
            using var g = new Form
            {
                Text = "编辑事件", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ClientSize = new Size(Ui.P(340), Ui.P(246)),
                Font = new Font("Microsoft YaHei UI", 10.5F),
                BackColor = UiColors.Bg, ForeColor = UiColors.Fg
            };
            var l1 = new Label { Text = "起始值 Value：", AutoSize = true, Location = new Point(Ui.P(18), Ui.P(24)), ForeColor = UiColors.BodyText };
            var v1 = new NumericUpDown { DecimalPlaces = 3, Minimum = -100000, Maximum = 100000, Value = (decimal)(double.IsNaN(ev.Value) ? 0 : ev.Value), Location = new Point(Ui.P(140), Ui.P(20)), Width = Ui.P(160), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle };
            var l2 = new Label { Text = "结束值 EndValue：", AutoSize = true, Location = new Point(Ui.P(18), Ui.P(62)), ForeColor = UiColors.BodyText };
            var v2 = new NumericUpDown { DecimalPlaces = 3, Minimum = -100000, Maximum = 100000, Value = (decimal)(double.IsNaN(ev.EndValue) ? 0 : ev.EndValue), Location = new Point(Ui.P(140), Ui.P(58)), Width = Ui.P(160), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle };
            var l3 = new Label { Text = "结束时间 End(ms)：", AutoSize = true, Location = new Point(Ui.P(18), Ui.P(100)), ForeColor = UiColors.BodyText };
            var v3 = new NumericUpDown { DecimalPlaces = 0, Minimum = -1000000, Maximum = 100000000, Value = (decimal)(double.IsNaN(ev.End) ? ev.Time : ev.End), Location = new Point(Ui.P(140), Ui.P(96)), Width = Ui.P(160), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle };
            var l4 = new Label { Text = "绑定组：", AutoSize = true, Location = new Point(Ui.P(18), Ui.P(138)), ForeColor = UiColors.BodyText };
            var g4 = new NumericUpDown { DecimalPlaces = 0, Minimum = 0, Maximum = 65535, Value = ev.Group, Location = new Point(Ui.P(140), Ui.P(134)), Width = Ui.P(160), BackColor = UiColors.InputBg, ForeColor = UiColors.Fg, BorderStyle = BorderStyle.FixedSingle };
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(Ui.P(104), Ui.P(182)), Size = new Size(Ui.P(100), Ui.P(32)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(Ui.P(216), Ui.P(182)), Size = new Size(Ui.P(100), Ui.P(32)), BackColor = UiColors.BtnBg, ForeColor = UiColors.Fg, FlatStyle = FlatStyle.Flat };
            Ui.Hover(ok); Ui.Hover(cancel);
            g.Controls.AddRange(new Control[] { l1, v1, l2, v2, l3, v3, l4, g4, ok, cancel });
            g.AcceptButton = ok; g.CancelButton = cancel;
            if (g.ShowDialog(this) != DialogResult.OK) return false;
            ev.Value = (double)v1.Value;
            ev.EndValue = (double)v2.Value;
            ev.End = (double)v3.Value;
            ev.Group = (int)Math.Round((double)g4.Value);
            // 同组同类型联动（T59 D2j：修改其一 → 同组头尾/缓动同步）
            SyncEventGroup(ev);
            return true;
        }

        /// <summary>绑定组联动（T59 D2j）：把 ev 的头尾/缓动同步到同组同类型其余事件（Group=0 无操作）。</summary>
        internal void SyncEventGroup(ChartEvent ev)
        {
            if (ev == null || ev.Group == 0) return;
            foreach (var e in _events)
                if (e != null && e != ev && e.Group == ev.Group && e.Type == ev.Type)
                {
                    e.Value = ev.Value;
                    e.EndValue = ev.EndValue;
                    e.End = ev.End;
                    e.Ease = ev.Ease;
                }
        }

        /// <summary>放置音符：有轨按列 col；无轨按连续场 (fx, fy)（0..1）。t 会被吸附。返回新音符（失败返回 null）。</summary>
        internal Note PlaceNoteAt(double fx, double fy, int col, double t, string typeOverride = null)
        {
            t = SnapTime(Math.Max(0, t));
            string type = typeOverride ?? (string.IsNullOrEmpty(_noteType) ? "tap" : _noteType);

            // 有轨：同列同拍去重
            if (!IsTrackless(_mode))
            {
                int dataCol = Math.Max(0, Math.Min(CanvasKc - 1, col));
                foreach (var n in _notes)
                    if (Math.Abs(n.Time - t) < 4 && n.Col == dataCol) return null;
                PushUndo();
                var tracked = new Note { Time = t, End = t, Col = dataCol, Type = type, Kind = KindForPlace(type), Line = 0 };
                _notes.Add(tracked);
                MarkDirty();
                _canvas.Invalidate();
                return tracked;
            }

            // 无轨：连续场坐标
            PushUndo();
            var nn = new Note { Time = t, End = t, Type = type, Line = _mode == GameMode.Phigros ? ActiveLine : 0 };
            switch (_mode)
            {
                case GameMode.OsuStandard when type == "spin":
                {
                    nn.Type = "spin"; nn.Kind = 0;
                    nn.X = 0.5; nn.Y = 0.5; nn.EndX = 0.5; nn.EndY = 0.5;
                    nn.End = t + BeatMs;
                    ApplyOsuShape(nn);
                    break;
                }
                default:
                {
                    nn.Kind = KindForPlace(type);
                    bool isLong = IsLongType(type);
                    nn.End = isLong ? t + BeatMs : t;
                    ApplyTracklessField(nn, fx, fy, col);
                    if (_mode == GameMode.OsuStandard) ApplyOsuShape(nn);   // 初始直线滑条（按住拖拽再画）
                    break;
                }
            }
            _notes.Add(nn);
            MarkDirty();
            _canvas.Invalidate();
            return nn;
        }

        /// <summary>放置音符的 Kind：ADOFAI 转角随选中音符由角度框设置；放置时恒 0。</summary>
        int KindForPlace(string type) => 0;

        internal void MoveNote(Note n, double t, bool tail)
        {
            t = SnapTime(Math.Max(0, t));
            if (tail)
            {
                double end = Math.Max(n.Time + SnapStep * 0.5, t);
                if (end - n.Time < SnapStep * 0.6)
                {
                    n.Type = "tap"; n.End = n.Time; ClearOsuShape(n);
                }
                else
                {
                    if (n.Type == "tap" || n.Type == "flick") n.Type = "hold";   // 拖长交互：tap/flick→hold
                    n.End = end;
                    ApplyOsuShape(n);
                }
            }
            else
            {
                double d = t - n.Time;
                bool wasLong = IsLong(n);
                n.Time = t;
                if (wasLong) n.End = Math.Max(n.End + d, n.Time + SnapStep);
                else n.End = n.Time;
                ApplyOsuShape(n);
            }
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>osu 选择框旋转（osu-master）：绕滑条头中心旋转全部控制点。</summary>
        internal void RotateOsuNote(Note n, double rad)
        {
            if (n == null) return;
            double c = Math.Cos(rad), s = Math.Sin(rad);
            double hx = n.X, hy = n.Y;
            // 旋转控制点（相对头部）
            if (n.Curve != null)
                for (int i = 0; i < n.Curve.Count; i++)
                {
                    double x = n.Curve[i].X - hx, y = n.Curve[i].Y - hy;
                    n.Curve[i] = (Clamp01(hx + x * c - y * s), Clamp01(hy + x * s + y * c));
                }
            double ex = n.EndX - hx, ey = n.EndY - hy;
            n.EndX = Clamp01(hx + ex * c - ey * s);
            n.EndY = Clamp01(hy + ex * s + ey * c);
            ApplyOsuShape(n);
            MarkDirty(); _canvas.Invalidate();
        }

        /// <summary>osu 选择框缩放（osu-master）：绕滑条头中心缩放全部控制点。</summary>
        internal void ScaleOsuNote(Note n, double k)
        {
            if (n == null) return;
            k = Math.Max(0.1, Math.Min(8, k));
            double hx = n.X, hy = n.Y;
            if (n.Curve != null)
                for (int i = 0; i < n.Curve.Count; i++)
                {
                    double x = n.Curve[i].X - hx, y = n.Curve[i].Y - hy;
                    n.Curve[i] = (Clamp01(hx + x * k), Clamp01(hy + y * k));
                }
            n.EndX = Clamp01(hx + (n.EndX - hx) * k);
            n.EndY = Clamp01(hy + (n.EndY - hy) * k);
            ApplyOsuShape(n);
            MarkDirty(); _canvas.Invalidate();
        }

        /// <summary>无轨移动音符：只改空间位置（X/Y/角度），时间不变。</summary>
        internal void MoveNoteField(Note n, double fx, double fy, int col)
        {
            if (n == null) return;
            ApplyTracklessField(n, fx, fy, col);
            if (_mode == GameMode.OsuStandard && n.Type == "hold")
            {
                if (n.Curve == null || n.Curve.Count < 2)
                {
                    // 滑条头移动：若尾尚未拖出则同步头尾
                    n.EndX = n.X; n.EndY = n.Y;
                    n.Curve = new List<(double X, double Y)> { (n.X, n.Y), (n.X, n.Y) };
                }
                else
                {
                    // 拖动头部：更新滑条首点，保持尾点不变
                    n.Curve[0] = (Clamp01(n.X), Clamp01(n.Y));
                }
            }
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>设置音符结束时间（时长微调/拖尾）。</summary>
        internal void SetNoteEnd(Note n, double end)
        {
            if (n == null) return;
            n.End = Math.Max(n.Time, end);
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>ADOFAI 网格重排（t5）：把 tile from 移到排序位置 to（时间重排；Kind 转角保留——
        /// 网格路径位置由角度序列自动重算）。时间 = 目标前后邻 tile 中点（边界用 ±1 细分步长）。</summary>
        internal void ReorderAdofaiNote(Note n, int from, int to)
        {
            if (n == null || from == to) return;
            var s = _notes.Where(x => x != null).OrderBy(x => x.Time).ToList();
            int i = s.IndexOf(n);
            if (i < 0 || i != from || to < 0 || to >= s.Count) return;
            s.RemoveAt(i);
            double newT;
            int k = to;
            if (k >= s.Count) newT = s.Count > 0 ? s[s.Count - 1].Time + SnapStep * 3 : SnapTime(_time);
            else if (k <= 0) newT = Math.Max(0, s[0].Time - SnapStep * 3);
            else newT = (s[k - 1].Time + s[k].Time) / 2.0;
            newT = SnapTime(Math.Max(0, newT));
            if (Math.Abs(newT - n.Time) < 0.5) return;   // 目标位无实际变化
            PushUndo();
            n.Time = newT;
            n.End = n.Time;
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>设置 OsuStandard 滑条终点（按住拖拽画直线滑条）。</summary>
        internal void SetOsuSliderEnd(Note n, double fx, double fy)
        {
            if (n == null || _mode != GameMode.OsuStandard) return;
            if (n.SliderType == '\0') n.SliderType = _osuSliderType != '\0' ? _osuSliderType : 'L';
            n.Repeats = 1;
            n.EndX = Clamp01(fx); n.EndY = Clamp01(fy);
            n.Curve = new List<(double X, double Y)> { (Clamp01(n.X), Clamp01(n.Y)), (Clamp01(fx), Clamp01(fy)) };
            MarkDirty();
            _canvas.Invalidate();
        }

        /* ================= 模式编辑增强：osu 滑条 / arcaea arc / ADOFAI ================= */

        /// <summary>osu 滑条全控制点列表（头 + 中间控制点 + 尾），与 X/Y/EndX/EndY 对齐。</summary>
        internal List<(double X, double Y)> OsuCurvePoints(Note n)
        {
            var pts = new List<(double X, double Y)>();
            if (n == null) return pts;
            if (n.Curve != null && n.Curve.Count >= 2)
            {
                foreach (var p in n.Curve) pts.Add((Clamp01(p.X), Clamp01(p.Y)));
            }
            else
            {
                pts.Add((Clamp01(n.X), Clamp01(n.Y)));
                pts.Add((Clamp01(n.EndX), Clamp01(n.EndY)));
            }
            if (pts.Count >= 2)
            {
                pts[0] = (Clamp01(n.X), Clamp01(n.Y));
                pts[pts.Count - 1] = (Clamp01(n.EndX), Clamp01(n.EndY));
            }
            return pts;
        }

        /// <summary>把 osu 滑条全点写回 Curve，并同步头尾到 X/Y/EndX/EndY。</summary>
        void WriteOsuCurve(Note n, List<(double X, double Y)> pts)
        {
            if (n == null || pts == null || pts.Count < 2) return;
            n.Curve = new List<(double X, double Y)>(pts);
            n.X = Clamp01(pts[0].X); n.Y = Clamp01(pts[0].Y);
            n.EndX = Clamp01(pts[pts.Count - 1].X); n.EndY = Clamp01(pts[pts.Count - 1].Y);
        }

        /// <summary>在 osu 滑条双击处插入控制点（最多 4 点：头 + 2 控制点 + 尾）。</summary>
        internal bool InsertOsuCtrlPoint(Note n, double fx, double fy)
        {
            if (n == null || _mode != GameMode.OsuStandard || n.Type != "hold") return false;
            var pts = OsuCurvePoints(n);
            if (pts.Count >= 4) return false;
            pts.Insert(pts.Count - 1, (Clamp01(fx), Clamp01(fy)));
            WriteOsuCurve(n, pts);
            return true;
        }

        /// <summary>移动 osu 滑条控制点（idx 为 Curve 全点索引）。</summary>
        internal void MoveOsuCtrlPoint(Note n, int idx, double fx, double fy)
        {
            if (n == null) return;
            var pts = OsuCurvePoints(n);
            if (idx < 0 || idx >= pts.Count) return;
            pts[idx] = (Clamp01(fx), Clamp01(fy));
            WriteOsuCurve(n, pts);
        }

        /// <summary>删除 osu 滑条控制点（至少保留头尾 2 点；头尾不可删）。</summary>
        internal void DeleteOsuCtrlPoint(Note n, int idx)
        {
            if (n == null) return;
            var pts = OsuCurvePoints(n);
            if (pts.Count <= 2) return;
            if (idx <= 0 || idx >= pts.Count - 1) return;
            pts.RemoveAt(idx);
            WriteOsuCurve(n, pts);
        }

        /// <summary>设置 arc 终点轨道（EndCol）。</summary>
        internal void SetArcEndCol(Note n, int col)
        {
            if (n == null) return;
            n.EndCol = ClampCol(col);
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>清除 arc 终点轨道（EndCol=-1，与起点同轨）。</summary>
        internal void ClearArcEnd(Note n)
        {
            if (n == null) return;
            n.EndCol = -1;
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>插入 arc 中间控制点（Arcaea 3D：Arc3 存 (轨, 时间比例, 天地高度)；其余模式沿用 Curve 2D），最多 2 个。</summary>
        internal bool InsertArcCtrlPoint(Note n, double colNorm, double tFrac)
        {
            if (n == null) return false;
            if (_mode == GameMode.Arcaea)
            {
                if (n.Arc3 == null) n.Arc3 = new List<(double X, double Y, double Z)>();
                if (n.Arc3.Count >= 2) return false;
                n.Arc3.Add((Clamp01(colNorm), Clamp01(tFrac), 0.5));
                MarkDirty();
                _canvas.Invalidate();
                return true;
            }
            if (n.Curve == null) n.Curve = new List<(double X, double Y)>();
            if (n.Curve.Count >= 2) return false;
            n.Curve.Add((Clamp01(colNorm), Clamp01(tFrac)));
            MarkDirty();
            _canvas.Invalidate();
            return true;
        }

        /// <summary>移动 arc 中间控制点（Arcaea：改轨/时间，高度 Z 不动）。</summary>
        internal void MoveArcCtrlPoint(Note n, int idx, double colNorm, double tFrac)
        {
            if (n == null) return;
            if (_mode == GameMode.Arcaea && n.Arc3 != null)
            {
                if (idx < 0 || idx >= n.Arc3.Count) return;
                var p = n.Arc3[idx];
                n.Arc3[idx] = (Clamp01(colNorm), Clamp01(tFrac), p.Z);
                MarkDirty();
                _canvas.Invalidate();
                return;
            }
            if (n.Curve == null || idx < 0 || idx >= n.Curve.Count) return;
            n.Curve[idx] = (Clamp01(colNorm), Clamp01(tFrac));
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>删除 arc 中间控制点。</summary>
        internal void DeleteArcCtrlPoint(Note n, int idx)
        {
            if (n == null) return;
            if (_mode == GameMode.Arcaea && n.Arc3 != null)
            {
                if (idx < 0 || idx >= n.Arc3.Count) return;
                n.Arc3.RemoveAt(idx);
                MarkDirty();
                _canvas.Invalidate();
                return;
            }
            if (n.Curve == null || idx < 0 || idx >= n.Curve.Count) return;
            n.Curve.RemoveAt(idx);
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>OsuStandard 音符形状：hold 补两点直线 Curve/SliderType，spin 居中（其它模式无副作用）。</summary>
        void ApplyOsuShape(Note n)
        {
            if (_mode != GameMode.OsuStandard || n == null) return;
            if (n.Type == "hold")
            {
                // 滑条：类型沿用当前选择（未设则默认直线 L）；头尾两点 Curve；若已由拖拽画线（Curve≥2）则保留
                if (n.SliderType == '\0') n.SliderType = _osuSliderType != '\0' ? _osuSliderType : 'L';
                n.Repeats = 1;
                if (n.Curve == null || n.Curve.Count < 2)
                {
                    double x = Clamp01(n.X), y = Clamp01(n.Y);
                    n.EndX = x; n.EndY = y;
                    n.Curve = new List<(double X, double Y)> { (x, y), (x, y) };
                }
            }
            else if (n.Type == "spin")
            {
                n.SliderType = '\0';
                n.Repeats = 1;
                n.X = 0.5; n.Y = 0.5; n.EndX = 0.5; n.EndY = 0.5;
                n.Curve = null;
            }
            else ClearOsuShape(n);
        }

        /// <summary>清除 OsuStandard 滑条/转盘专用字段。</summary>
        void ClearOsuShape(Note n)
        {
            if (n == null) return;
            n.SliderType = '\0';
            n.Curve = null;
        }

        internal void DeleteNote(Note n)
        {
            PushUndo();
            _notes.Remove(n);
            if (_selNote == n) SelNote = null;
            MarkDirty();
            _canvas.Invalidate();
        }

        // ---- C4：多选（Shift+点击加入/移除；Delete/方向键/复制粘贴作用于全部选中）----
        readonly HashSet<Note> _selNotes = new HashSet<Note>();

        internal void ToggleMultiSelect(Note n)
        {
            if (n == null) return;
            if (!_selNotes.Remove(n)) _selNotes.Add(n);
            SelNote = n;
        }

        internal IEnumerable<Note> SelectedNotes()
        {
            if (_selNotes.Count == 0) return _selNote != null ? new[] { _selNote } : Array.Empty<Note>();
            return _selNotes;
        }

        internal void ClearMultiSelect() => _selNotes.Clear();

        internal void DeleteSelected()
        {
            var sel = SelectedNotes().ToList();
            if (sel.Count == 0) return;
            PushUndo();
            foreach (var n in sel) _notes.Remove(n);
            _selNotes.Clear();
            SelNote = null;
            MarkDirty();
            _canvas.Invalidate();
        }

        internal void NudgeSelectedMulti(double ms)
        {
            var sel = SelectedNotes().ToList();
            if (sel.Count == 0) return;
            PushUndo();
            foreach (var n in sel) MoveNote(n, n.Time + ms, false);
        }

        internal void UndoRequest() => Undo();
        internal void RedoRequest() => Redo();

        internal void NudgeSelected(double ms)
        {
            if (_selNote == null) return;
            PushUndo();
            MoveNote(_selNote, _selNote.Time + ms, false);
        }

        /// <summary>选中长条音符：Shift+滚轮微调时长（±1 细分步长）。</summary>
        internal void NudgeSelectedDuration(double ms)
        {
            if (_selNote == null || !IsLong(_selNote)) return;
            PushUndo();
            double end = Math.Max(_selNote.Time + SnapStep * 0.5, _selNote.End + ms);
            if (end - _selNote.Time < SnapStep * 0.6) return;
            _selNote.End = end;
            MarkDirty();
            _canvas.Invalidate();
        }

        internal Note SelNote
        {
            get => _selNote;
            set
            {
                _selNote = value;
                try { SyncOsuCurveBox(); SyncAdofaiAngleBox(); } catch { }
                _canvas.Invalidate();
            }
        }
        internal Note HoverNote { get => _hoverNote; set { _hoverNote = value; if (value != null) _canvas.Invalidate(); } }
        internal int CurveHoverIdx = -1;           // 曲线视图悬停/拖拽中的关键帧索引（通道*100000+序号；-1=无）
        internal int DragKind;                     // 0 无 | 1 移动音符 | 2 拉长条尾 | 3 标尺 seek | 4 中键滚动
                                                  // 无轨：5 移动空间位置 | 6 时间条 seek | 7 osu 滑条画线
                                                  // 增强：9 osu 控制点拖动 | 12 arc 终点手柄 | 13 arc 控制点
        internal int DragPointIndex = -1;          // 正在拖动的控制点索引（osu Curve 全点 / arc Curve / 贝塞尔手柄拖拽=30）
        internal double DragGrabT { get => _dragGrabT; set => _dragGrabT = value; }
        internal double DownY { get => _downY; set => _downY = value; }
        internal double DragStartX { get => _dragStartX; set => _dragStartX = value; }
        internal double DragStartY { get => _dragStartY; set => _dragStartY = value; }
        internal double DragEndX, DragEndY;       // 框选（DragKind=29）终点
        internal ChartEvent DragEvt;              // 当前拖拽主事件（组拖锚/贝塞尔手柄=30）
        internal int DragEvtChannel = -1;         // 贝塞尔手柄拖拽：事件通道索引（0=moveX..4=speed）
        internal double SnapStepVal => SnapStep;
        internal double PxMs => _pxPerMs;

        // ===== P0 事件级多选（RPE 范式补齐 2：Ctrl/Shift 点选 + 框选 + 组拖 + 批删；D2d 复刻） =====
        readonly HashSet<ChartEvent> _evtSel = new HashSet<ChartEvent>();
        internal IEnumerable<ChartEvent> SelectedEvts() => _evtSel;
        internal void ToggleEvtSelect(ChartEvent ev) { if (ev == null) return; if (!_evtSel.Remove(ev)) _evtSel.Add(ev); _canvas.Invalidate(); }
        internal void AddEvtSelect(ChartEvent ev) { if (ev != null && _evtSel.Add(ev)) _canvas.Invalidate(); }
        internal void ClearEvtSelect() { if (_evtSel.Count > 0) { _evtSel.Clear(); _canvas.Invalidate(); } }
        internal int EvtSelCount => _evtSel.Count;
        internal ChartEvent SelEvt
        {
            get => _selEvt;
            set
            {
                _selEvt = value;
                // 同步右侧事件键帧列表选中行（画布点选 → 列表高亮）
                try
                {
                    if (_evtList != null && value != null)
                        foreach (ListViewItem it in _evtList.Items)
                        {
                            bool match = ReferenceEquals(it.Tag, value);
                            if (match && !it.Selected) it.Selected = true;
                            else if (!match && it.Selected) it.Selected = false;
                        }
                }
                catch { }
                _canvas.Invalidate();
            }
        }
        ChartEvent _selEvt;
        /// <summary>Delete 批删选中事件（P0：框选/组拖后一键清除）。</summary>
        internal void DeleteSelectedEvts()
        {
            if (_evtSel.Count == 0) return;
            PushUndo();
            _events.RemoveAll(e => _evtSel.Contains(e));
            _evtSel.Clear();
            SelEvt = null;
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        // ===== T56 D2g：快捷编辑组（粘合/切割/随机/递推——作用于选中事件 SelEvt 或选中集首事件）=====
        ChartEvent QeEvt() => _selEvt ?? (_evtSel.Count > 0 ? _evtSel.First() : null);
        static (double lo, double hi) ChannelRange(string type) => type switch
        {
            "rotate" => (-360.0, 360.0),
            "speed" => (0.1, 4.0),
            _ => (0.0, 1.0)
        };

        /// <summary>粘合：上一同类型事件的尾部填入本头（prev.End = ev.Time；瞬间→区间，EndValue 延续本头值）。</summary>
        internal void EvtGlue()
        {
            var ev = QeEvt();
            if (ev == null) return;
            ChartEvent prev = null;
            foreach (var e in _events)
                if (e != null && e != ev && e.Type == ev.Type && e.Time <= ev.Time
                    && (prev == null || e.Time > prev.Time)) prev = e;
            if (prev == null) return;
            PushUndo();
            prev.End = ev.Time;
            prev.EndValue = ev.Value;
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        /// <summary>切割/拆分：当前时刻（拍吸附）把区间事件分成两个按时间分配的事件
        /// （T56 官方用例：0=>150 从 2 拍切开 → 0=>50 + 50=>150；中点值按缓动/贝塞尔求值）。</summary>
        internal void EvtSplitAt()
        {
            var ev = QeEvt();
            if (ev == null) return;
            if (double.IsNaN(ev.End) || ev.End <= ev.Time) return;
            double t = SnapTime(Math.Max(ev.Time + 1, Math.Min(_time, ev.End)));
            if (t <= ev.Time || t >= ev.End) return;
            double p = (t - ev.Time) / (ev.End - ev.Time);
            double mid = ev.Value + (ev.EndValue - ev.Value) * EditorCanvas.EvalEventEase(ev, p);
            PushUndo();
            var left = new ChartEvent { Time = ev.Time, End = t, Type = ev.Type, Value = ev.Value, EndValue = mid, Line = ev.Line, Ease = ev.Ease, Group = ev.Group };
            var right = new ChartEvent { Time = t, End = ev.End, Type = ev.Type, Value = mid, EndValue = ev.EndValue, Line = ev.Line, Ease = ev.Ease, Next = ev.Next, Group = ev.Group };
            int idx = _events.IndexOf(ev);
            _events[idx] = left;
            _events.Insert(idx + 1, right);
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        /// <summary>随机：尾值（EndValue）在通道值域内随机；瞬间事件转化为 1 拍区间并随机尾值。</summary>
        internal void EvtRandom()
        {
            var ev = QeEvt();
            if (ev == null) return;
            PushUndo();
            var (lo, hi) = ChannelRange(ev.Type);
            var rnd = new Random();
            double rv = lo + rnd.NextDouble() * (hi - lo);
            if (double.IsNaN(ev.End) || ev.End <= ev.Time)
            {
                ev.End = Math.Max(ev.Time + BeatMs, SnapTime(ev.Time + BeatMs));
            }
            ev.EndValue = rv;
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        /// <summary>递推：按最近两个同类型区间的增量生成下一段
        /// （T56 官方用例：0=>50 + 50=>100 → 100=>150：Value=上一尾值，EndValue=尾值+段增量）。</summary>
        internal void EvtRecur()
        {
            var ev = QeEvt();
            if (ev == null) return;
            var same = _events
                .Where(e => e != null && e.Type == ev.Type && e.Time <= ev.Time)
                .OrderBy(e => e.Time)
                .ToList();
            if (same.Count < 2) return;
            var b = same[same.Count - 1];
            var a = same[same.Count - 2];
            double dur = (!double.IsNaN(b.End) && b.End > b.Time) ? b.End - b.Time : BeatMs;
            double step = b.EndValue - b.Value;                    // 增量（瞬间事件 EndValue 缺失时用上两事件值差）
            if (Math.Abs(step) < 1e-12 && !double.IsNaN(a.EndValue))
                step = b.Value - a.Value;
            double newT = (!double.IsNaN(b.End) && b.End > b.Time) ? Math.Max(b.End, SnapTime(b.End)) : Math.Max(b.Time + BeatMs, SnapTime(b.Time + BeatMs));
            double v0 = b.EndValue;
            double v1 = v0 + step;
            PushUndo();
            _events.Add(new ChartEvent { Time = newT, End = newT + dur, Type = ev.Type, Value = v0, EndValue = v1, Line = ev.Line, Ease = ev.Ease });
            MarkDirty();
            _canvas.Invalidate();
            RefreshEvtList();
        }

        /// <summary>RPE 执行列表批量编辑（D2p 复刻，官方指南 batch-edit-basics 语义；作用于 SelectedNotes 多选集）。
        /// ops：MirrorY/MirrorMid/SideSwitch/SideUp/SideDown/ToReal/ToFake/ToTap/ToFlick/ToDrag/AttachX。</summary>
        internal void BatchEdit(string op)
        {
            var sel = SelectedNotes().ToList();
            if (sel.Count == 0) { MessageBox.Show("先选中音符（Shift+点击多选 / 框选）", "执行列表"); return; }
            PushUndo();
            bool ph = _mode == GameMode.Phigros;
            // MirrorMid：按选中集中位 X 翻转（排序中间 X）
            double midX = 0.5;
            if (op == "MirrorMid")
            {
                var xs = sel.Select(n => n.X).OrderBy(x => x).ToList();
                midX = xs[(xs.Count - 1) / 2];
            }
            foreach (var n in sel)
            {
                if (n == null) continue;
                switch (op)
                {
                    case "MirrorY": n.Y = 1 - n.Y; n.EndY = 1 - n.EndY; break;
                    case "MirrorMid": n.X = 2 * midX - n.X; n.EndX = 2 * midX - n.EndX; break;
                    case "SideSwitch": n.Side = n.Side == 1 ? 0 : 1; break;
                    case "SideUp": n.Side = 0; break;
                    case "SideDown": n.Side = 1; break;
                    case "ToReal": n.Decor = false; break;
                    case "ToFake": n.Decor = true; break;
                    case "ToTap": if (n.Type != "hold" && n.Type != "arc") { n.Type = "tap"; n.End = n.Time; } break;
                    case "ToFlick": if (n.Type != "hold" && n.Type != "arc") { n.Type = "flick"; n.End = n.Time; } break;
                    case "ToDrag": if (n.Type != "hold" && n.Type != "arc") { n.Type = "drag"; n.End = n.Time; } break;
                    case "AttachX": n.X = Math.Round(n.X * 16.0) / 16.0; n.EndX = Math.Round(n.EndX * 16.0) / 16.0; break;   // 1/16 竖线吸附
                }
            }
            MarkDirty();
            _canvas.Invalidate();
        }

        // ===== D2q：填充曲线音符（RPE 官方指南 curve-fill-notes；Ctrl+F/G 锚起点/终点）=====
        internal Note FillAnchorStart, FillAnchorEnd;      // 锚（Hold 取头部时间）
        internal string FillType = "tap";                  // 种类：tap/flick/drag
        internal string FillShape = "linear";              // 曲线：linear/sine/quad
        internal int FillDensity = 2;                      // 密度=横线间距/填充间距（整数）
        /// <summary>Ctrl+F：以选中音符为填充起点锚（Hold 取头部）。</summary>
        internal void SetFillAnchorStart()
        {
            FillAnchorStart = _selNote;
            if (_selNote == null && !SuppressSaveToast) MessageBox.Show("先选中一个音符作为填充起点（Ctrl+F）", "填充曲线");
        }
        /// <summary>Ctrl+G：以选中音符为填充终点锚（Hold 取头部）。</summary>
        internal void SetFillAnchorEnd()
        {
            FillAnchorEnd = _selNote;
            if (_selNote == null && !SuppressSaveToast) MessageBox.Show("先选中一个音符作为填充终点（Ctrl+G）", "填充曲线");
        }
        /// <summary>生成：起止锚间按密度生成中间音符；X 线性插值、Y 按形状（linear 直线/sine 拱形/quad 抛物线）。</summary>
        internal void FillCurveNotes()
        {
            if (FillAnchorStart == null || FillAnchorEnd == null)
            {
                if (!SuppressSaveToast) MessageBox.Show("先用 Ctrl+F/G 选择起点与终点音符", "填充曲线");
                return;
            }
            var a = FillAnchorStart; var b = FillAnchorEnd;
            // Hold 取头部时间；起点晚于终点时交换
            double t0 = a.Time, t1 = b.Time;
            if (t1 < t0) { (t0, t1) = (t1, t0); }
            if (t1 - t0 < SnapStep * 0.5) { if (!SuppressSaveToast) MessageBox.Show("起止时间过近", "填充曲线"); return; }
            PushUndo();
            int steps = Math.Max(2, (int)Math.Round((t1 - t0) / SnapStep * FillDensity));
            for (int i = 1; i < steps; i++)   // 不含端点
            {
                double p = i / (double)steps;
                double x = a.X + (b.X - a.X) * p;
                double y = a.Y + (b.Y - a.Y) * p;
                switch (FillShape)
                {
                    case "sine": y = a.Y + (b.Y - a.Y) * (0.5 - 0.5 * Math.Cos(p * Math.PI)); break;   // 拱形
                    case "quad": y = a.Y + (b.Y - a.Y) * p * p; break;                                 // 抛物线
                }
                var nn = new Note
                {
                    Time = t0 + (t1 - t0) * p, End = t0 + (t1 - t0) * p,
                    Col = a.Col, X = Math.Max(0, Math.Min(1, x)), Y = Math.Max(0, Math.Min(1, y)),
                    Type = FillType, Line = a.Line,
                    Side = a.Side, Width = a.Width, Alpha = a.Alpha, VisMs = a.VisMs
                };
                _notes.Add(nn);
            }
            _notes.Sort((x2, y2) => x2.Time.CompareTo(y2.Time));
            MarkDirty();
            _canvas.Invalidate();
        }

        internal void SeekDuringPlay(double t)
        {
            _time = Math.Max(0, t);
            if (_playing)
            {
                if (_audio.HasMedia)
                {
                    _audio.Seek(_time);
                    _playAnchorTime = _time;
                    _playAnchorPos = _audio.PositionMs;
                }
                else { _playAnchorTime = _time; _playAnchorPos = Environment.TickCount; }
            }
        }

        /// <summary>无轨：保证播放头位于时间条可见窗口内（必要时平移窗口）。</summary>
        internal void EnsureTimeVisible(double windowMs)
        {
            if (windowMs <= 0) return;
            if (_time < _scrollMs) _scrollMs = Math.Max(0, _time - windowMs * 0.15);
            else if (_time > _scrollMs + windowMs) _scrollMs = Math.Max(0, _time - windowMs * 0.85);
        }

        internal void TogglePlayPublic() => TogglePlay();
        internal void PlayStop()
        {
            _playing = false;
            if (_audio.HasMedia) _audio.Pause();
            SyncPlayButton();
        }

        /// <summary>T58 D2i：A=反转 X（选中音符 X→1-X）。</summary>
        internal void FlipSelectedX()
        {
            var n = _selNote;
            if (n == null) return;
            PushUndo();
            n.X = Math.Max(0, Math.Min(1, 1 - n.X));
            MarkDirty();
            _canvas.Invalidate();
        }

        /// <summary>T58 D2i：S=翻转下落（Side Up↔Down）。</summary>
        internal void ToggleSelectedSide()
        {
            var n = _selNote;
            if (n == null) return;
            PushUndo();
            n.Side = n.Side == 0 ? 1 : 0;
            MarkDirty();
            _canvas.Invalidate();
        }
    }

    /* ================= 画布（D2D 渲染 + 交互） ================= */
    sealed class EditorCanvas : Control
    {
        readonly ChartEditorPanel _ed;
        IRenderer _d2d;
        const double TopPad = 30, SidePad = 16;
        const double TimeBarH = 30;             // 无轨底部时间条高度

        // —— 事件柱/螺旋每帧绘制复用缓冲（零 GC：仅首帧扩容） ——
        readonly List<ChartEvent> _tlEvBuf = new List<ChartEvent>();
        readonly List<ChartEvent> _tlChainBuf = new List<ChartEvent>();
        readonly List<ChartEvent> _tlChainTmp = new List<ChartEvent>();   // 稳定归并排序 scratch（保持 OrderBy 稳定语义）
        readonly List<(double t, double v01)> _tlPtBuf = new List<(double, double)>();
        readonly PointF[] _tlDiamond = new PointF[4];
        PointF[] _tlSegPts = new PointF[24];    // 区间事件缓动曲线折线（≤17 点）；配 DrawPolyline(count) 重载
        readonly PointF[] _spiralPts = new PointF[65];

        // —— 事件求值缓存（t9：IF 大谱 O(n²) 修复——EdEvalEvent 原先每次调用全量扫描全部事件；
        //          按 (type, line) 分组 + 每帧 memo：一次绘制内同 (type,line) 结果恒定，音符/线各取 O(1)）——
        readonly Dictionary<string, List<ChartEvent>> _evGlobals = new Dictionary<string, List<ChartEvent>>();          // type → Line<0
        readonly Dictionary<(string type, int line), List<ChartEvent>> _evPerLine = new Dictionary<(string, int), List<ChartEvent>>();
        readonly Dictionary<(string type, int line), double> _evMemo = new Dictionary<(string, int), double>();
        readonly List<ChartEvent> _evSortTmp = new List<ChartEvent>();
        bool _evMemoDirty = true;
        bool _evStructDirty = true;   // t6：桶结构（分组/排序）失效——仅编辑入口（MarkDirty）设置；每帧仅失效 memo
        internal bool EvStructDirty { get => _evStructDirty; set { _evStructDirty = value; if (value) _spdDirty = true; } }
        // —— ② Phigros 预览 speed 场积分缓存（与游玩端 SpdDispCached 同语义：speed 事件线性插值梯形积分；
        //    随事件结构失效重建；Disp(now,hitTime)×ppms = 音符视觉位移 —— 预览速度事件生效）——
        readonly Dictionary<int, (List<(double t, double v)> ks, double[] pref)> _spdPref = new Dictionary<int, (List<(double t, double v)>, double[])>();
        bool _spdDirty = true;

        public EditorCanvas(ChartEditorPanel ed)
        {
            _ed = ed;
            BackColor = UiColors.ScreenBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            var t = new System.Windows.Forms.Timer { Interval = 30 };
            t.Tick += (s, e) =>
            {
                // IF 大谱性能：空闲（未播放）不重绘。原实现每 30ms 无条件 Invalidate()——
                // 4455 音符 × 每音符 2~3 个 D2D 几何 ≈ ~10k 几何/帧（≈30ms/帧）→ UI 线程 100% 自旋，
                // edshot/静态下 DoEvents 泵消息导致无限重绘（"IF 卡死"真因，非 O(n²) 求值）。
                // 播放中才需连续重绘；静态状态变化由各操作处理函数显式 Invalidate()。
                if (_ed.Playing)
                {
                    double now = _ed.NowTime;
                    if (_ed.IsTracklessMode)
                    {
                        // 无轨：播放头随音频位置走，时间条窗口跟随
                        _ed.Time = now;
                        _ed.EnsureTimeVisible(TimeWindowMs(ClientSize.Width));
                    }
                    else if (_ed.Mode == GameMode.Arcaea)
                    {
                        // Arcaea 3D 编辑视图：判定线时刻=now（与游玩一致）——音符从远处向判定线移动，红线固定在判定线
                        _ed.ScrollMs = Math.Max(0, now);
                        _ed.Time = now;
                    }
                    else
                    {
                        // 有轨：自动跟随 + 滚动
                        double target = now - (ClientSize.Height - TopPad) * 0.55 / _ed.PxPerMs;
                        _ed.ScrollMs = Math.Max(0, target);
                        _ed.Time = now;
                    }
                    Invalidate();
                }
            };
            t.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _d2d?.Dispose();
            try { _d2d = new D2DRenderer(Handle, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)); }
            catch { _d2d = null; }
            if (_d2d is D2DRenderer d2) d2.FitEnabled = false;   // 编辑器保持 1:1 坐标（命中检测未做逆变换）
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            _d2d?.Dispose(); _d2d = null;
            base.OnHandleDestroyed(e);
        }
        protected override void Dispose(bool disposing)
        {
            _d2d?.Dispose();
            base.Dispose(disposing);
        }

        /* ---------- 布局 ---------- */
        double TimeWindowMs(int W) => W / Math.Max(0.003, _ed.PxPerMs);   // ⑤⑥：与 PxPerMs 钳制同步下限（0.003≈333s 全谱概览）
        double XToTime(double x, int W) => _ed.ScrollMs + x / Math.Max(1, W) * TimeWindowMs(W);

        /// <summary>⑤⑥：时间条音符刻度命中（与 DrawTimeBar 刻度同几何）——音符 tick = 时间拖拽侧边拖柄。</summary>
        Note HitTimeTick(double mx, int W, double tolerance)
        {
            double windowMs = TimeWindowMs(W);
            Note best = null;
            double bestD = double.MaxValue;
            foreach (var n in _ed.Notes)
            {
                if (n == null) continue;
                double nx = (n.Time - _ed.ScrollMs) / Math.Max(1, windowMs) * W;
                if (nx < -tolerance || nx > W + tolerance) continue;
                double d = Math.Abs(nx - mx);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best != null && bestD <= tolerance ? best : null;
        }
        /// <summary>列区 y → 时间（垂直事件柱几何辅助）。</summary>
        static double YToTimeLocal(double y, RectangleF f, double windowMs, double scroll)
            => scroll + (y - f.Y) / Math.Max(1, f.Height) * windowMs;

        /// <summary>ADOFAI 累计转角（度）：音符 Kind 存转角，逐音符累计；全 0 时回退线性累计（时间比例 × 180°）。</summary>
        static double[] AdofaiCumulative(List<Note> list, double beatMs)
        {
            int cnt = list.Count;
            bool hasAngle = false;
            foreach (var n in list) if (n.Kind != 0) { hasAngle = true; break; }
            var cum = new double[cnt];
            double acc = 0, t0 = list[0].Time;
            for (int i = 0; i < cnt; i++)
            {
                if (hasAngle) acc += list[i].Kind;
                else acc = 180.0 * (list[i].Time - t0) / Math.Max(1, beatMs);
                cum[i] = acc;
            }
            return cum;
        }

        (double playX, double laneW) LayoutRect()
        {
            double availW = ClientSize.Width - SidePad * 2;
            double playW = Math.Max(200, Math.Min(1100, availW));
            double playX = SidePad + (availW - playW) / 2;
            return (playX, playW / Math.Max(1, _ed.CanvasKc));
        }

        /* ---------- Arcaea 3D 编辑视图（引擎化：场景 + 相机，四方位=相机绕场景旋转，几何一致） ---------- */
        // 3D 布局参数（同 GamePanel.DrawArcaea：统一透视，判定线=深度1，消失点=深度→0）
        double A3GndY, A3PvpY, A3SkyY, A3SkyH, A3GlaneW, A3GCx, A3Pt0, A3Pk, A3AntennaY, A3ZA, A3Ahead;
        // 移动摄像头：屏幕平移偏移 + 缩放（中键拖动平移，Shift+滚轮缩放）
        double A3CamX, A3CamY, A3CamZ = 1.0;
        // 预设方位：0=正前方 1=上方(俯视) 2=左侧 3=右侧 —— 同一场景的不同相机（引擎化）
        int A3View = 0;
        // 引擎相机（Camera3D：位置+朝向+透视/正交，世界点→屏幕）
        Camera3D A3Cam = new Camera3D();
        // 鼠标位置（用于坐标显示）
        int A3MouseX = -1, A3MouseY = -1;
        // 点击创建模式（Arcade-plus 式：Idle/Tap/Hold/Arc/ArcTap）
        string A3CreateMode = "tap";
        // Arc 两点创建暂存：起点 (时间, 轨道, 高度比例) + 是否已点起点
        bool A3ArcStartPlaced = false;
        double A3ArcStartT, A3ArcStartLane, A3ArcStartH;
        bool A3ArcColorBlue = true;   // 蓝/红
        bool A3ArcVoid = false;       // 实/虚
        void Arc3DSetup(int W, int H)
        {
            // 正前方（view 0）：判定线按 v11 参考画面比例（画布 0.817 → 全屏时屏幕 y≈1307）；其余视图=画布底部
            A3GndY = A3View == 0 ? H * 0.8174 : H - 26;
            double skyMove = 0.55;
            A3SkyY = A3GndY - Math.Clamp(skyMove, 0.12, 0.88) * (A3GndY - TopPad);
            A3Pk = 1.2;                                         // slant 0.3 * 4（与游玩默认一致）
            A3Pt0 = (1 + A3Pk) * 1000.0 / Math.Max(0.1, GameSettings.Speed);
            A3PvpY = A3GndY - (A3GndY - A3SkyY) * (1 + A3Pk);   // 消失点（远端，顶部屏外）
            A3SkyH = Math.Max(1, A3GndY - A3SkyY);
            A3GlaneW = W * 0.62 / 4.0;
            A3GCx = W / 2.0;
            A3AntennaY = 774;                                   // 天线（与游玩画面一致：v11 参考线画布 y=774 → 屏幕 y≈819）
            A3ZA = (A3AntennaY - A3PvpY) / (A3GndY - A3PvpY);
            A3Ahead = A3Pt0 / A3Pk + 400;
            // —— 引擎相机配置（世界坐标 (wx, wy, wz) = ((x3d-gCx)/轨道宽, h/天空高, z)）——
            // 场景：轨道 x∈[-0.5,0.5]（中心 0），高度 y∈[0,1]，深度 z∈[0,1]
            // 正前方（view 0）不用相机：用游玩同款"道路透视"投影（见 A3Proj）——与 v11 参考画面一致
            //   （x=gCx+(x3d-gCx)·z，y=pvpY+(gndY-pvpY)·z−h·z；消失点在顶部屏外，天线≈画布 774）
            A3Cam.ScreenW = Math.Max(1, W);
            A3Cam.ScreenH = Math.Max(1, H);
            double camZ = Math.Max(0.25, Math.Min(3.0, A3CamZ));
            switch (A3View)
            {
                case 1:   // 上方俯视：正交相机在 +y，看向 -y（x-z 平面）
                {
                    A3Cam.Orthographic = true;
                    A3Cam.OrthoSize = 0.5 / camZ;               // 半高 0.5 → z∈[0,1] 占满屏高，x3d∈[-0.5,0.5] 在屏内
                    A3Cam.Position = new Vec3(0, 2.6, 0.5);
                    A3Cam.SetLookAt(new Vec3(0, 0, 0.5), new Vec3(0, 0, -1));
                    A3Cam.Position += A3Cam.Right * (A3CamX / Math.Max(1, A3GlaneW * 2)) + A3Cam.Up * (A3CamY / Math.Max(1, A3SkyH));
                    break;
                }
                case 2:   // 左侧：正交相机在 -x，看向 +x（z-y 平面，深度右深左浅→镜像）
                {
                    A3Cam.Orthographic = true;
                    A3Cam.OrthoSize = 0.5 / camZ;               // 半高 0.5 → h∈[0,1] 占满屏高，宽=0.5·aspect 覆盖 z∈[0,1]
                    A3Cam.Position = new Vec3(-2.6, 0.5, 0.5);
                    A3Cam.SetLookAt(new Vec3(0, 0.5, 0.5), new Vec3(0, 1, 0));
                    A3Cam.Position += A3Cam.Right * (A3CamX / Math.Max(1, A3GlaneW * 2)) + A3Cam.Up * (A3CamY / Math.Max(1, A3SkyH));
                    break;
                }
                case 3:   // 右侧：正交相机在 +x，看向 -x（z-y 平面）
                {
                    A3Cam.Orthographic = true;
                    A3Cam.OrthoSize = 0.5 / camZ;
                    A3Cam.Position = new Vec3(2.6, 0.5, 0.5);
                    A3Cam.SetLookAt(new Vec3(0, 0.5, 0.5), new Vec3(0, 1, 0));
                    A3Cam.Position += A3Cam.Right * (A3CamX / Math.Max(1, A3GlaneW * 2)) + A3Cam.Up * (A3CamY / Math.Max(1, A3SkyH));
                    break;
                }
                default:  // 正前方：道路透视（游玩同款），缩放/平移在 A3Proj 内处理
                {
                    A3Cam.Orthographic = false;
                    A3Cam.FovY = 55.0;
                    A3Cam.Position = new Vec3(0, 0.55, 2.2);
                    A3Cam.SetLookAt(new Vec3(0, 0.28, 0.5), new Vec3(0, 1, 0));
                    break;
                }
            }
        }
        // 时间→深度：判定线(scroll)=z 1，未来(t>scroll)在远处(z→0)——与游玩画面一致的 DepthOfRemain 透视
        // （近大远小、越近越快；不再线性整窗等分 → 播放无压缩感）
        double A3ZOfT(double t, double scroll, double windowMs) => A3Pt0 / (A3Pt0 + Math.Max(-A3Pt0 * 0.98, t - scroll));
        double A3TOfZ(double z, double scroll, double windowMs) => scroll + A3Pt0 * (1.0 / Math.Max(0.04, Math.Min(1.6, z)) - 1.0);
        // 世界坐标（引擎）：wx=(x3d-gCx)/轨道全宽 ∈[-0.5,0.5]，wy=h/skyH ∈[0,1]，wz=z ∈[0,1]
        double A3WX(double x3d) => (x3d - A3GCx) / Math.Max(1, 4.0 * A3GlaneW);
        double A3WY(double h) => h / Math.Max(1, A3SkyH);
        double A3X3dOf(double lane) => A3GCx + (lane - 2.0) * A3GlaneW;           // 轨道列(0-4) → 平面 x
        double A3X3dFree(double nx) => A3GCx + (nx - 0.5) * (4.0 * A3GlaneW);     // 天键自由 x（0..1）
        // 四方位投影：正前方=游玩同款道路透视（与 v11 参考画面一致）；其余视图=引擎相机（场景绕相机旋转）。
        // 道路透视：屏幕 x = gCx+(x3d-gCx)·z（近大远小），屏幕 y = pvpY+(gndY-pvpY)·z − h·z（高度近大远小）；
        // 消失点在顶部屏外（z→0 → (gCx, pvpY)），判定线=z 1 在底部，天线=zAntenna 深度（画布 y≈774）。
        (double x, double y) A3Proj(double x3d, double h, double z)
        {
            double wz = Math.Max(0.03, Math.Min(1.7, z));
            if (A3View == 0)
            {
                double s = Math.Max(0.25, Math.Min(3.0, A3CamZ));          // Shift+滚轮缩放（绕屏幕中心）
                double px = A3GCx + (x3d - A3GCx) * wz + A3CamX;           // 中键平移
                double py = A3PvpY + (A3GndY - A3PvpY) * wz - h * wz + A3CamY;
                return (A3Cam.ScreenW / 2.0 + (px - A3Cam.ScreenW / 2.0) * s,
                        A3Cam.ScreenH / 2.0 + (py - A3Cam.ScreenH / 2.0) * s);
            }
            var p = A3Cam.WorldToScreen(new Vec3(A3WX(x3d), A3WY(h), wz));
            return (p.x, p.y);
        }
        // 逆映射（引擎）：屏幕点 → 射线，与地面平面(wy=0)求交（正前方/俯视）或与中轴平面(wx=0)求交（侧视）
        // → (时间, 轨道列, 高度比例, 深度)
        (double t, double lane, double h, double z) A3UnprojectFull(double x, double y, double scroll, double windowMs)
        {
            if (A3View == 0)
            {
                // 正前方（道路透视逆映射）：先按地面 h=0 求深度；落在天空区(z<zAntenna)再用半空平面 h=0.5·skyH 求交
                double s = Math.Max(0.25, Math.Min(3.0, A3CamZ));
                double cx = (x - A3Cam.ScreenW / 2.0) / s + A3Cam.ScreenW / 2.0 - A3CamX;
                double cy = (y - A3Cam.ScreenH / 2.0) / s + A3Cam.ScreenH / 2.0 - A3CamY;
                double wz = (cy - A3PvpY) / Math.Max(1e-6, A3GndY - A3PvpY);
                double hh = 0;
                if (wz < A3ZA)
                    wz = (cy - A3PvpY) / Math.Max(1e-6, A3GndY - A3PvpY - 0.5 * A3SkyH);   // 天空区：半空平面
                wz = Math.Max(0.04, Math.Min(1.6, wz));
                double x3d = A3GCx + (cx - A3GCx) / wz;
                double ln = 2.0 + (x3d - A3GCx) / Math.Max(1, A3GlaneW);
                bool sky = wz < A3ZA;
                double t = sky ? A3TOfZ(wz / A3ZA, scroll, windowMs) : A3TOfZ(wz, scroll, windowMs);
                return (t, ln, sky ? 0.5 : hh, wz);
            }
            var (o, d) = A3Cam.ScreenToRay(x, y);
            if (A3View == 2 || A3View == 3)
            {
                // 侧视：与中轴平面 wx=0（轨道中间）求交 → (wy, wz)
                double tt = Camera3D.RayPlane(o, d, new Vec3(1, 0, 0), new Vec3(0, 0, 0));
                if (double.IsNaN(tt) || tt <= 0) return (scroll, 2.0, 0.5, 1.0);
                var p = o + d * tt;
                double wz = Math.Max(0.04, Math.Min(1.6, p.Z));
                double wy = Math.Max(0, Math.Min(1, p.Y));
                return (A3TOfZ(wz, scroll, windowMs), 2.0, wy, wz);
            }
            // 正前方/俯视：先与地面平面 wy=0 求交；无交（视线朝上/天空区）→ 与半空平面 wy=0.5 求交
            double tg = Camera3D.RayPlane(o, d, new Vec3(0, 1, 0), new Vec3(0, 0, 0));
            if (double.IsNaN(tg) || tg <= 0)
                tg = Camera3D.RayPlane(o, d, new Vec3(0, 1, 0), new Vec3(0, 0.5, 0));
            if (double.IsNaN(tg) || tg <= 0) return (scroll, 2.0, 0, 1.0);
            var pg = o + d * tg;
            double wzg = Math.Max(0.04, Math.Min(1.6, pg.Z));
            double lane = 4.0 * pg.X + 2.0;
            return (A3TOfZ(wzg, scroll, windowMs), lane, 0, wzg);
        }
        (double t, double lane) A3Unproject(double x, double y, double scroll, double windowMs)
        {
            var r = A3UnprojectFull(x, y, scroll, windowMs);
            return (r.t, r.lane);
        }
        // 鼠标 → 深度 z（当前视图）
        double A3MouseZ(double x, double y) => A3UnprojectFull(x, y, _ed.ScrollMs, TimeWindowMs(ClientSize.Width)).z;
        /// <summary>编辑器 3D arc 横向位置：起点轨 → Arc3 控制点 X → 终点轨 分段线性（控制点可真正改变弧走向）。</summary>
        double A3XArcAt(Note n, double kk, int e0, int e1)
        {
            double x0 = A3X3dOf(e0), x1 = A3X3dOf(e1);
            if (n.Arc3 == null || n.Arc3.Count == 0) return x0 + (x1 - x0) * kk;
            var pts = new List<(double t, double x)> { (0, x0) };
            foreach (var p in n.Arc3) pts.Add((Math.Max(0, Math.Min(1, p.Y)), A3X3dFree(Math.Max(0, Math.Min(1, p.X)))));
            pts.Add((1, x1));
            pts.Sort((a, b) => a.t.CompareTo(b.t));
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (kk >= pts[i].t && kk <= pts[i + 1].t)
                {
                    double span = Math.Max(1e-6, pts[i + 1].t - pts[i].t);
                    double k2 = (kk - pts[i].t) / span;
                    return pts[i].x + (pts[i + 1].x - pts[i].x) * k2;
                }
            }
            return x1;
        }

        /// <summary>无轨编辑场包围盒（不含底部时间条）。Cytus/Phigros=全幅自由场；osu=4:3。
        /// P0-垂直事件柱 + ② 按玩法分类泛化：所有无轨玩法的场宽收窄为左侧约 62%，右侧约 38% 让给事件柱列区。</summary>
        RectangleF FieldRect(int W, int H)
        {
            // 场地高度钳制到可见区（高 DPI 虚拟化下窗口底部会被裁切，防止判定线/时间条出屏）
            double availW = W - SidePad * 2;
            double availH = Math.Min(H - TopPad - TimeBarH - 16, 1000);
            switch (_ed.Mode)
            {
                case GameMode.OsuStandard:
                {
                    // osu 4:3——在 62% 预留区内约束并居中
                    double maxW = (availW - 16) * 0.62;
                    double h = Math.Max(160, availH);
                    double w = h * 512.0 / 384.0;
                    if (w > maxW) { w = maxW; h = w * 384.0 / 512.0; }
                    return new RectangleF((float)(SidePad + 8 + (maxW - w) / 2), (float)(TopPad + 8 + (availH - h) / 2), (float)w, (float)h);
                }
                case GameMode.Phigros:
                case GameMode.Cytus:
                case GameMode.Maimai:
                {
                    // 事件柱（按玩法分类）：场宽 62%，右列 38%
                    double w = (availW - 16) * 0.62;
                    return new RectangleF((float)(SidePad + 8), (float)(TopPad + 8), (float)w, (float)availH);
                }
                default: // 其余：全幅自由场
                {
                    return new RectangleF((float)(SidePad + 8), (float)(TopPad + 8), (float)(availW - 16), (float)availH);
                }
            }
        }

        /* ---------- 绘制 ---------- */
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_d2d == null) return;
            _evMemoDirty = true;   // t9：每次绘制重建事件求值缓存（NowTime 随帧变化；编辑改动亦在此失效）
            // t6 IF 性能：_evMemo 仅按帧常量 NowTime 求值失效（桶结构在 MarkDirty 时失效——
            // 见 MarkDirty()/AddLineEventAt 等编辑入口设置的 _evStructDirty）；避免每帧对 14010 事件
            // 重复 O(E log E) 分组+排序（IF Timeline evt=8ms 的根因）。
            int W = ClientSize.Width, H = ClientSize.Height;
            _d2d.Resize(W, H);
            if (!_d2d.Begin()) return;
            _d2d.Clear(UiColors.ScreenBg);
            try
            {
                if (_ed.IsTracklessMode) DrawTracklessField(W, H);
                else DrawTracked(W, H);
            }
            catch (Exception ex)
            {
                Logger.Error("编辑器绘制异常：" + (_ed.Mode.ToString()) + " | " + ex.Message, ex);
            }
            _d2d.End();
            EditorAutoShotTick();
        }

        /// <summary>编辑器自动截图(--autoshot 目录时,与游玩端共用):渲染提交后自捕获,最多 6 张。
        /// 解决外部 PrintWindow 截到 D2D 旧帧的问题(编辑器画面验证/还原度对比)。</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        int _edShotCounter, _edShotCount;
        void EditorAutoShotTick()
        {
            if (string.IsNullOrEmpty(GamePanel.AutoShotDir)) return;
            if (_edShotCount >= 6) return;
            if (++_edShotCounter < 30) return;
            _edShotCounter = 0;
            try
            {
                string path = Path.Combine(GamePanel.AutoShotDir, $"ed_{DateTime.Now:HHmmssfff}.png");
                using (var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = false;
                        try { ok = PrintWindow(Handle, hdc, 2); } catch { }
                        g.ReleaseHdc(hdc);
                        if (ok) { bmp.Save(path, ImageFormat.Png); _edShotCount++; }
                    }
                }
            }
            catch { }
        }

        // ===== 有轨模式绘制（保持现状：垂直轨道 + 时间轴） =====
        void DrawTracked(int W, int H)
        {
            if (_ed.Mode == GameMode.Arcaea) { DrawArcaea3D(W, H); return; }   // Arcaea：3D 透视编辑视图（与游玩画面一致）
            var (playX, laneW) = LayoutRect();
            int canvasKc = _ed.CanvasKc;
            double now = _ed.NowTime;
            double scroll = _ed.ScrollMs;
            double ppm = _ed.PxPerMs;
            double beatMs = _ed.BeatMsVal;

            // 时间标尺 + 节拍网格（每小节加粗线 + 小节号左缘标注；细分线浅色；吸附按所选细分）
            _d2d.FillRect(0, 0, W, (float)TopPad, Color.FromArgb(255, 12, 17, 27));
            double measureMs = beatMs * 4;   // 4/4 每小节
            double step = _ed.SnapStepMs;
            if (_ed.ShowGrid && step > 1)   // 细分线（浅色，按所选细分步长）
            {
                double s0 = Math.Floor(scroll / step) * step;
                // 细分网格只画在轨道区域内（不再贯穿全宽）
                double gx0 = playX, gx1 = playX + laneW * canvasKc;
                for (double st = s0; st <= scroll + (H - TopPad) / ppm + step; st += step)
                {
                    double y = TopPad + (st - scroll) * ppm;
                    if (y < TopPad || y > H) continue;
                    _d2d.DrawLine((float)gx0, (float)y, (float)gx1, (float)y, Color.FromArgb(22, 34, 52), 1f);
                }
            }
            int m0 = (int)Math.Floor((scroll - 1000) / measureMs);
            int m1 = (int)Math.Ceiling((scroll + (H - TopPad) / ppm + 1000) / measureMs);
            for (int mi = m0; mi <= m1; mi++)   // 小节线（加粗，仅轨道区域）+ 小节号 + 时间标签
            {
                double mt = mi * measureMs;
                double y = TopPad + (mt - scroll) * ppm;
                if (y < TopPad - 2 || y > H + 2) continue;
                if (_ed.ShowGrid) _d2d.DrawLine((float)playX, (float)y, (float)(playX + laneW * canvasKc), (float)y, Color.FromArgb(90, 70, 96, 140), 2f);
                _d2d.Text("M" + (mi + 1), 8, (float)(y - 10), 44, 14, Color.FromArgb(255, 180, 200, 235), 9f);
                double sec = mt / 1000.0;
                _d2d.Text((sec / 60).ToString("0") + ":" + (sec % 60).ToString("00"), 50, (float)(y + 8), 60, 16, Color.FromArgb(255, 140, 158, 190), 9f);
            }

            // 轨道 + 列头（按模式显示不同标注；IIDX 转盘列红色标记）
            double laneTop = TopPad;
            for (int i = 0; i < canvasKc; i++)
            {
                bool turntable = _ed.Mode == GameMode.Iidx && i == 0;
                _d2d.FillRect((float)(playX + i * laneW + 1), (float)laneTop, (float)(laneW - 2), (float)(H - laneTop), turntable ? Color.FromArgb(46, 22, 28) : Color.FromArgb(18, 20, 28));
                if (i > 0) _d2d.DrawLine((float)(playX + i * laneW), (float)laneTop, (float)(playX + i * laneW), H, Color.FromArgb(255, 38, 52, 76), 1f);
                _d2d.Text(_ed.ColumnLabel(i), (float)(playX + i * laneW + laneW / 2), (float)(laneTop - 12), (float)Math.Max(24, laneW), 14, turntable ? Color.FromArgb(255, 255, 110, 110) : Color.FromArgb(255, 120, 138, 168), 9f, true);
            }

            // ② 按玩法分类：有轨模式事件柱（轨道右侧窄柱列，与轨道同纵向时间轴；如 ADOFAI = twirl/bpm/mode 柱）
            DrawTrackedEventStrip(W, H, playX, laneW, canvasKc);

            // 音符
            foreach (var n in _ed.Notes)
            {
                if (n == null) continue;
                double yHead = TopPad + (n.Time - scroll) * ppm;
                bool isLong = _ed.IsLong(n);
                Color typeColor = _ed.NoteColor(n);
                int col = _ed.ClampCol(n.Col);
                if (col < 0 || col >= canvasKc) continue;   // 列数变化时钳制/隐藏
                if (n.Type == "arc") DrawArcPath(n, playX, laneW, ppm, canvasKc, typeColor);   // arc 贝塞尔样条（先画路径再画头）
                DrawLaneNote(n, col, playX, laneW, yHead, isLong, typeColor, H, ppm);
            }

            // ADOFAI：路径折线预览（相邻 tile 按 Kind 转角折转；twirl 段虚线）
            // E-2：AdofaiReal 用俯视棋盘路径主视图（大画布路径，可点击追加/点选），Routlock 保留小轮盘预览
            if (_ed.Mode == GameMode.AdofaiReal) DrawAdofaiTopdown(W, H);
            else if (_ed.Mode == GameMode.Adofai) DrawAdofaiPreview(W, H);

            // Mania 密度柱状图（lazer 范式）：轨道右侧窄条，每小节每列音符计数，热点高亮
            if (_ed.Mode == GameMode.Mania && canvasKc > 0)
            {
                double denW = Math.Min(46, W - (playX + laneW * canvasKc) - 6);
                if (denW > 14)
                {
                    double denX = playX + laneW * canvasKc + 4;
                    int dm0 = (int)Math.Floor(scroll / measureMs);
                    int dm1 = (int)Math.Ceiling((scroll + (H - TopPad) / ppm) / measureMs);
                    for (int mi = dm0; mi <= dm1; mi++)
                    {
                        double y0 = TopPad + (mi * measureMs - scroll) * ppm;
                        double y1 = TopPad + ((mi + 1) * measureMs - scroll) * ppm;
                        if (y1 < TopPad || y0 > H) continue;
                        int maxCount = 0;
                        var counts = new int[canvasKc];
                        foreach (var n in _ed.Notes)
                        {
                            if (n == null) continue;
                            int ci = _ed.ClampCol(n.Col);
                            if (ci < 0 || ci >= canvasKc) continue;
                            if (n.Time >= mi * measureMs && n.Time < (mi + 1) * measureMs)
                            { counts[ci]++; maxCount = Math.Max(maxCount, counts[ci]); }
                        }
                        if (maxCount <= 0) continue;
                        double cellH = (y1 - y0) / canvasKc;
                        for (int ci = 0; ci < canvasKc; ci++)
                        {
                            if (counts[ci] <= 0) continue;
                            double hgt = Math.Max(2, cellH * Math.Min(1, counts[ci] / (double)Math.Max(1, maxCount)) * 0.9);
                            var dc = counts[ci] >= 4 ? Color.FromArgb(200, 255, 120, 120)
                                   : counts[ci] >= 2 ? Color.FromArgb(180, 255, 200, 110)
                                   : Color.FromArgb(150, 130, 200, 255);
                            _d2d.FillRect((float)denX, (float)(y1 - hgt - 1), (float)denW, (float)hgt, dc);
                        }
                    }
                }
            }

            // 事件标记（类型着色 + 图标缩写）
            foreach (var ev in _ed.EventList)
            {
                if (ev == null) continue;
                double y = TopPad + (ev.Time - scroll) * ppm;
                if (y < TopPad - 8 || y > H + 8) continue;
                var ec = ChartEditorPanel.EventColor(ev.Type);
                _d2d.DrawLine((float)playX, (float)y, (float)(playX + laneW * canvasKc), (float)y, ec, 1f, 1);
                _d2d.Text(ChartEditorPanel.EventAbbr(ev.Type), 8, (float)(y - 8), 70, 14, ec, 8f);
            }

            // 播放头
            double ph = TopPad + (now - scroll) * ppm;
            if (ph >= TopPad && ph <= H)
            {
                _d2d.DrawLine(0, (float)ph, W, (float)ph, Color.FromArgb(255, 255, 90, 90), 2f);
                _d2d.FillRect(0, (float)(ph - 7), 38, 14, Color.FromArgb(220, 160, 40, 40));
                _d2d.Text((now / 1000.0).ToString("0.00") + "s", 4, (float)ph, 34, 12, Color.White, 8f);
            }

            // 轨道外框 + 提示
            _d2d.DrawRect((float)playX, (float)TopPad, (float)(laneW * canvasKc), (float)(H - TopPad), Color.FromArgb(255, 60, 78, 104), 1f);
            if (_ed.Notes.Count == 0)
                _d2d.Text("单击轨道放置音符 · 拖动移动 · 拖底部边缘拉长条 · 右键删除 · 滚轮滚动 · 空格播放 · Ctrl+Z 撤销",
                    W / 2f, H / 2f, W - 40, 20, Color.FromArgb(255, 130, 150, 180), 12f, true);
        }

        /// <summary>② 按玩法分类：有轨模式事件柱可视化——轨道右侧窄柱列（该玩法事件类别：
        /// ADOFAI=twirl/bpm/mode、IIDX/Mania/Arcaea=noteSpeed/scroll/bpm/mode……），与轨道同一纵向时间轴。
        /// 纯可视化（键帧点选/拖拽走右侧面板事件列表）；边距不足或 Mania（右侧=密度柱）/Arcaea（3D 视图）不画。</summary>
        void DrawTrackedEventStrip(int W, int H, double playX, double laneW, int canvasKc)
        {
            try
            {
                if (_ed.Mode == GameMode.Mania || _ed.Mode == GameMode.Arcaea) return;
                double x0 = playX + laneW * canvasKc + 6;
                double right = W - SidePad;
                if (right - x0 < 108) return;          // 右侧边距不足：保持赛道全宽（不挤压轨道）
                string[] kinds = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                int kn = Math.Max(1, kinds.Length);
                double cw = (right - x0 - 8) / kn;
                double scroll = _ed.ScrollMs;
                double ppm = _ed.PxPerMs;
                double beatMs = _ed.BeatMsVal;
                double TimeToY(double t) => TopPad + (t - scroll) * ppm;

                _d2d.FillRect((float)x0, (float)TopPad - 2, (float)(right - x0 + 4), (float)(H - TopPad + 2), Color.FromArgb(150, 6, 10, 18));
                _d2d.Text("事件柱（" + ModeSystem.DisplayName(_ed.Mode) + "）· " + string.Join("/", kinds),
                    (float)x0, 4, (float)(right - x0), 14, Color.FromArgb(220, 160, 190, 230), 8f);
                for (int k = 0; k < kn; k++)
                {
                    double cx0 = x0 + k * (cw + 2);
                    var ec = ChartEditorPanel.EventColor(kinds[k]);
                    _d2d.Text(kinds[k], (float)cx0, (float)(TopPad - 14), (float)cw, 12, ec, 7.5f);
                    if (beatMs > 1)
                    {
                        double b0 = Math.Floor(scroll / beatMs) * beatMs;
                        for (double bt = b0; bt <= scroll + (H - TopPad) / ppm + beatMs; bt += beatMs)
                        {
                            float gy = (float)TimeToY(bt);
                            if (gy < TopPad || gy > H) continue;
                            _d2d.DrawLine((float)cx0, gy, (float)(cx0 + cw), gy, Color.FromArgb(28, 120, 140, 180), 1f);
                        }
                    }
                    // 该列事件键帧（圆形点，x=列内值归一）
                    var (lo, hi) = ChartEditorPanel.EventRangeForKind(kinds[k]);
                    double span = Math.Max(1e-9, hi - lo);
                    foreach (var ev in _ed.EventList)
                    {
                        if (ev == null || ev.Type != kinds[k]) continue;
                        float gy = (float)TimeToY(ev.Time);
                        if (gy < TopPad - 6 || gy > H + 6) continue;
                        double v01 = Math.Max(0, Math.Min(1, (ev.Value - lo) / span));
                        float gx = (float)(cx0 + v01 * cw);
                        bool isRange = !double.IsNaN(ev.End) && ev.End > ev.Time;
                        if (isRange)
                            _d2d.DrawLine((float)cx0, gy, (float)(cx0 + cw), gy, Color.FromArgb(90, ec.R, ec.G, ec.B), 1.2f);
                        _d2d.FillEllipse(gx - 3, gy - 3, 6, 6, ec);
                    }
                }
                // 播放头横线
                float phy = (float)TimeToY(_ed.NowTime);
                if (phy >= TopPad && phy <= H)
                    _d2d.DrawLine((float)x0, phy, (float)right, phy, Color.FromArgb(220, 255, 90, 90), 1.6f);
            }
            catch { }
        }

        // ===== Arcaea 3D 编辑视图（引擎化：场景 + 相机，四方位=相机绕场景旋转，几何一致） =====
        void DrawArcaea3D(int W, int H)
        {
            Arc3DSetup(W, H);
            int canvasKc = _ed.CanvasKc;
            double now = _ed.NowTime;
            double scroll = _ed.ScrollMs;
            double beatMs = _ed.BeatMsVal;
            double windowMs = TimeWindowMs(W);

            // 背景（暗紫渐变，同游玩）
            _d2d.FillVerticalGradient(0, 0, W, H, Color.FromArgb(255, 26, 18, 40), Color.FromArgb(255, 8, 7, 14));

            // 时间标尺（顶部保留：小节号 + 时间标签）
            _d2d.FillRect(0, 0, W, (float)TopPad, Color.FromArgb(255, 12, 17, 27));
            double measureMs = beatMs * 4;
            double step = _ed.SnapStepMs;
            int m0 = (int)Math.Floor((scroll - 1000) / measureMs);
            int m1 = (int)Math.Ceiling((scroll + windowMs + 1000) / measureMs);
            for (int mi = m0; mi <= m1; mi++)
            {
                double mt = mi * measureMs;
                double x = (mt - scroll) / Math.Max(1, windowMs) * W;
                if (x < -8 || x > W + 8) continue;
                _d2d.Text("M" + (mi + 1), (float)(x + 4), 2, 44, 14, Color.FromArgb(255, 180, 200, 235), 9f);
                double sec = mt / 1000.0;
                _d2d.Text((sec / 60).ToString("0") + ":" + (sec % 60).ToString("00"), (float)(x + 4), 15, 60, 12, Color.FromArgb(255, 140, 158, 190), 8f);
            }

            // 轨道：世界四边形 (x3d 0..4, h=0, z 0.02..1) 由引擎相机投影 → 正前方=斜轨梯形、俯视=矩形、侧视=深度带
            double zTop = 0.02;
            Color laneBg = Color.FromArgb(26, 36, 58);
            {
                var p0 = A3Proj(A3X3dOf(0), 0, 1.0);
                var p1 = A3Proj(A3X3dOf(4), 0, 1.0);
                var p2 = A3Proj(A3X3dOf(4), 0, zTop);
                var p3 = A3Proj(A3X3dOf(0), 0, zTop);
                _d2d.FillQuad((float)p0.x, (float)p0.y, (float)p1.x, (float)p1.y, (float)p2.x, (float)p2.y, (float)p3.x, (float)p3.y, laneBg);
                for (int i = 1; i < 4; i++)
                {
                    var a = A3Proj(A3X3dOf(i), 0, 1.0);
                    var b = A3Proj(A3X3dOf(i), 0, zTop);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.FromArgb(45, 60, 90), 1f);
                }
                // 轨道两侧边（更清晰）
                {
                    var a = A3Proj(A3X3dOf(0), 0, 1.0);
                    var b = A3Proj(A3X3dOf(0), 0, zTop);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.FromArgb(70, 92, 132), 1.2f);
                    a = A3Proj(A3X3dOf(4), 0, 1.0);
                    b = A3Proj(A3X3dOf(4), 0, zTop);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.FromArgb(70, 92, 132), 1.2f);
                }
            }

            // 网格：小节线（俯视=横线，侧视=竖线，正前方=横线）
            if (_ed.ShowGrid && step > 1)
            {
                double s0 = Math.Floor(scroll / step) * step;
                for (double st = s0; st <= scroll + windowMs + step; st += step)
                {
                    double z = A3ZOfT(st, scroll, windowMs);
                    if (z < 0.05 || z > 1.6) continue;
                    bool isM = Math.Abs(st / measureMs - Math.Round(st / measureMs)) < 0.01;
                    var c = isM ? Color.FromArgb(90, 70, 96, 140) : Color.FromArgb(22, 34, 52);
                    float th = isM ? 2f : 1f;
                    if (A3View == 2 || A3View == 3)
                    {
                        // 侧视：竖线（深度方向，轨道中轴）表示时间刻度
                        var a = A3Proj(A3X3dOf(2), 0, z);
                        var b = A3Proj(A3X3dOf(2), A3SkyH, z);
                        _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, c, th);
                    }
                    else
                    {
                        var a = A3Proj(A3X3dOf(0), 0, z);
                        var b = A3Proj(A3X3dOf(4), 0, z);
                        _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, c, th);
                    }
                }
            }

            // 天线（天空线，淡青）——地面深度 zAntenna 处的参考线（h=0，与判定点一致）
            // 俯视/侧视下该线即"天空区/轨道区"的分界（放置与坐标显示同语义）
            {
                var a = A3Proj(A3X3dOf(0), 0, A3ZA);
                var b = A3Proj(A3X3dOf(4), 0, A3ZA);
                _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.FromArgb(255, 205, 232, 255), 5f);
                // 亮边增强可见性
                _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.FromArgb(120, 150, 200, 255), 9f);
                if (A3View == 2 || A3View == 3)
                {
                    // 侧视：天线是深度 zAntenna 处的一条线 → 画竖参考线示意分界（轨道中轴）
                    var c = A3Proj(A3X3dOf(2), A3SkyH, A3ZA);
                    var d = A3Proj(A3X3dOf(2), 0, A3ZA);
                    _d2d.DrawLine((float)d.x, (float)d.y, (float)c.x, (float)c.y, Color.FromArgb(255, 205, 232, 255), 2f);
                }
            }
            // 判定线（地线，白色）——深度 z=1（底部；侧视=竖线）
            {
                if (A3View == 2 || A3View == 3)
                {
                    var a = A3Proj(A3X3dOf(2), 0, 1.0);
                    var b = A3Proj(A3X3dOf(2), A3SkyH, 1.0);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.White, 2.4f);
                }
                else
                {
                    var a = A3Proj(A3X3dOf(0), 0, 1.0);
                    var b = A3Proj(A3X3dOf(4), 0, 1.0);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, Color.White, 2.4f);
                }
            }

            // 音符：arc 3D 棱柱（同游玩），地键/天键/hold 3D 投影
            foreach (var n in _ed.Notes)
            {
                if (n == null) continue;
                if (n.Type == "arc") { DrawArcPath3D(n, scroll, windowMs, W); continue; }
                DrawLaneNote3D(n, scroll, windowMs);
            }

            // 事件标记（画在轨道平面上，按深度；侧视=竖线标记）
            foreach (var ev in _ed.EventList)
            {
                if (ev == null) continue;
                double z = A3ZOfT(ev.Time, scroll, windowMs);
                if (z < 0.05 || z > 1.6) continue;
                var ec = ChartEditorPanel.EventColor(ev.Type);
                if (A3View == 2 || A3View == 3)
                {
                    var a = A3Proj(A3X3dOf(2), 0, z);
                    var b = A3Proj(A3X3dOf(2), A3SkyH, z);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, ec, 1f, 1);
                    _d2d.Text(ChartEditorPanel.EventAbbr(ev.Type), (float)(a.x - 8), (float)(a.y - 8), 70, 14, ec, 8f);
                }
                else
                {
                    var a = A3Proj(A3X3dOf(0), 0, z);
                    var b = A3Proj(A3X3dOf(4), 0, z);
                    _d2d.DrawLine((float)a.x, (float)a.y, (float)b.x, (float)b.y, ec, 1f, 1);
                    _d2d.Text(ChartEditorPanel.EventAbbr(ev.Type), 8, (float)(a.y - 8), 70, 14, ec, 8f);
                }
            }

            // 播放头（now → 深度 → 红线；侧视=竖线）
            double pz = A3ZOfT(now, scroll, windowMs);
            if (pz >= 0.05 && pz <= 1.6)
            {
                var pc = Color.FromArgb(255, 255, 90, 90);
                if (A3View == 2 || A3View == 3)
                {
                    var pa = A3Proj(A3X3dOf(2), 0, pz);
                    var pb = A3Proj(A3X3dOf(2), A3SkyH, pz);
                    _d2d.DrawLine((float)pa.x, (float)pa.y, (float)pb.x, (float)pb.y, pc, 2f);
                    _d2d.Text((now / 1000.0).ToString("0.00") + "s", (float)(pa.x - 20), (float)(pa.y - 12), 44, 14, Color.FromArgb(220, 160, 40, 40), 8f);
                }
                else
                {
                    var pa = A3Proj(A3X3dOf(0), 0, pz);
                    var pb = A3Proj(A3X3dOf(4), 0, pz);
                    _d2d.DrawLine((float)pa.x, (float)pa.y, (float)pb.x, (float)pb.y, pc, 2f);
                    _d2d.Text((now / 1000.0).ToString("0.00") + "s", 4, (float)(pa.y - 7), 38, 14, Color.FromArgb(220, 160, 40, 40), 8f);
                }
            }

            // 提示 + 方位 + 鼠标坐标
            if (_ed.Notes.Count == 0)
                _d2d.Text("单击轨道放置音符 · 拖动移动 · 拖底部边缘拉长条 · 右键删除 · 滚轮滚动 · 空格播放 · Ctrl+Z 撤销",
                    W / 2f, H / 2f, W - 40, 20, Color.FromArgb(255, 130, 150, 180), 12f, true);
            string[] viewNames = { "正前方", "上方(俯视)", "左侧", "右侧" };
            string[] modeNames = { "tap", "hold", "arc", "arctap" };
            string modeDisp = A3CreateMode;
            string arcExtra = "";
            if (A3CreateMode == "arc")
                arcExtra = "  [" + (A3ArcColorBlue ? "蓝" : "红") + "/" + (A3ArcVoid ? "虚" : "实") + "]" + (A3ArcStartPlaced ? " · 点击终点" : " · 点击起点");
            _d2d.Text("方位：" + viewNames[Math.Max(0, Math.Min(3, A3View))] + " [1-4] · 创建模式：" + modeDisp + arcExtra + " [T/H/A/S切换 C=颜色 V=虚实]",
                8, H - 20, W - 16, 14, Color.FromArgb(160, 120, 150, 200), 10f);
            // arc 两点创建预览：起点标记 + 到鼠标的预览线
            if (A3CreateMode == "arc" && A3ArcStartPlaced)
            {
                var col3 = A3ArcColorBlue ? Color.FromArgb(255, 59, 156, 255) : Color.FromArgb(255, 255, 88, 88);
                var sp = A3Proj(A3X3dOf(Math.Max(0, Math.Min(4, A3ArcStartLane))), A3SkyH * A3ArcStartH, A3ZOfT(A3ArcStartT, scroll, windowMs));
                _d2d.DrawRect((float)(sp.x - 6), (float)(sp.y - 6), 12, 12, col3, 2.5f);
                if (A3MouseX >= 0 && A3MouseY >= 0)
                {
                    var up = A3Unproject(A3MouseX, A3MouseY, scroll, windowMs);
                    var ep = A3Proj(A3X3dOf(Math.Max(0, Math.Min(4, up.lane))), A3SkyH * ArcHeightFromY(A3MouseY), A3ZOfT(_ed.SnapTimeFor(Math.Max(0, up.t)), scroll, windowMs));
                    _d2d.DrawLine((float)sp.x, (float)sp.y, (float)ep.x, (float)ep.y, Color.FromArgb(150, col3.R, col3.G, col3.B), 1.5f);
                    _d2d.DrawRect((float)(ep.x - 6), (float)(ep.y - 6), 12, 12, Color.FromArgb(120, col3.R, col3.G, col3.B), 2f);
                }
            }
            // 鼠标坐标：时间 / 轨道 / 深度 / 判定区域（引擎化统一语义：深度在天线线以远 = 天空区）
            if (A3MouseX >= 0 && A3MouseY >= 0)
            {
                var up = A3UnprojectFull(A3MouseX, A3MouseY, scroll, windowMs);
                double zz = up.z;
                bool skyZone = zz < A3ZA;
                string zone = skyZone ? "天空区(天键)" : "轨道区(地键)";
                string hInfo = (A3View == 2 || A3View == 3) ? string.Format("  h={0:0.00}", up.h) : "";
                _d2d.Text(string.Format("鼠标: t={0:0}ms  轨={1:0.00}  z={2:0.00}{3}  [{4}]",
                    up.t, up.lane, zz, hInfo, zone), 8, H - 40, W - 16, 14, Color.FromArgb(220, 200, 220, 255), 10f);
            }
        }

        /// <summary>编辑器 3D 视图音符：地键（轨道上白块）、天键（空中菱形，判定点=天线）、hold（立体长条）。四视图适配。</summary>
        void DrawLaneNote3D(Note n, double scroll, double windowMs)
        {
            double t = n.Time;
            int col = _ed.ClampCol(n.Col);
            bool sky = col < 2;                       // Col 0-1 天键
            // 天键：与游玩一致 —— 深度 = zAntenna×DepthOfRemain（判定时刻 remain=0 → 恰在天线上），高度随下落比例
            double z = sky ? A3ZA * A3ZOfT(t, scroll, windowMs) : A3ZOfT(t, scroll, windowMs);
            if (z < 0.05 || z > 1.7) return;
            double remain = t - scroll;
            double h = sky ? A3SkyH * ChartEditorPanel.ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, remain / A3Ahead)) : 0;
            double lane = sky ? 0.5 : (col - 2) + 0.5;
            double x3d = sky ? A3X3dFree(n.X) : A3X3dOf(lane);
            var pr = A3Proj(x3d, h, z);
            double x = pr.x, y = pr.y;
            Color typeColor = _ed.NoteColor(n);
            bool sel = _ed.SelNote == n;
            double ds = Math.Max(5, (A3GndY - TopPad) * 0.028);   // 音符尺寸（视图稳定）

            if (n.Type == "hold")
            {
                double z2 = sky ? A3ZA * A3ZOfT(n.End, scroll, windowMs) : A3ZOfT(n.End, scroll, windowMs);
                double remain2 = n.End - scroll;
                double h2 = sky ? A3SkyH * ChartEditorPanel.ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, remain2 / A3Ahead)) : 0;
                // hold：世界四边形（宽沿 x3d 方向）→ 四视图统一 3D 投影
                double w1 = Math.Max(4, A3GlaneW * 0.14), w2 = Math.Max(4, A3GlaneW * 0.14);
                var L1 = A3Proj(x3d - w1, h, z);
                var R1 = A3Proj(x3d + w1, h, z);
                var L2 = A3Proj(x3d - w2, h2, z2);
                var R2 = A3Proj(x3d + w2, h2, z2);
                if (sky)
                    _d2d.FillQuad((float)L1.x, (float)L1.y, (float)R1.x, (float)R1.y, (float)R2.x, (float)R2.y, (float)L2.x, (float)L2.y, Color.FromArgb(140, typeColor.R, typeColor.G, typeColor.B));
                else
                    _d2d.FillQuad((float)L1.x, (float)L1.y, (float)R1.x, (float)R1.y, (float)R2.x, (float)R2.y, (float)L2.x, (float)L2.y, Color.FromArgb(120, 255, 255, 255));
                _d2d.DrawLine((float)L1.x, (float)L1.y, (float)L2.x, (float)L2.y, Color.FromArgb(160, typeColor.R, typeColor.G, typeColor.B), 1.2f);
                _d2d.DrawLine((float)R1.x, (float)R1.y, (float)R2.x, (float)R2.y, Color.FromArgb(160, typeColor.R, typeColor.G, typeColor.B), 1.2f);
            }

            if (sky)
            {
                _d2d.FillPolygon(new[] {
                    new PointF((float)x, (float)(y - ds)),
                    new PointF((float)(x + ds), (float)y),
                    new PointF((float)x, (float)(y + ds)),
                    new PointF((float)(x - ds), (float)y) }, typeColor);
                if (sel)
                    _d2d.DrawPolyline(new[] {
                        new PointF((float)x, (float)(y - ds - 3)),
                        new PointF((float)(x + ds + 3), (float)y),
                        new PointF((float)x, (float)(y + ds + 3)),
                        new PointF((float)(x - ds - 3), (float)y) }, Color.White, 2f, true);
            }
            else
            {
                double w = Math.Max(5, (A3GndY - TopPad) * 0.05);
                double th = w * 0.8;
                _d2d.FillRect((float)(x - w / 2), (float)(y - th / 2), (float)w, (float)th, typeColor);
                if (sel)
                    _d2d.DrawRect((float)(x - w / 2 - 2), (float)(y - th / 2 - 2), (float)(w + 4), (float)(th + 4), Color.White, 2f);
            }
        }

        /// <summary>实心棱柱带渲染（arc 通用）：上亮下暗双四边形 + X 剖面线 + 棱线 + 端线 + 中心高光。</summary>
        void DrawRibbon(PointF[] path, PointF[] Lp, PointF[] Rp, Color fillHi, Color fillLo, Color xLine, Color hiEdge, Color loEdge)
        {
            int n = path.Length - 1;
            for (int s = 0; s < n; s++)
            {
                _d2d.FillQuad(Lp[s].X, Lp[s].Y, path[s].X, path[s].Y, path[s + 1].X, path[s + 1].Y, Lp[s + 1].X, Lp[s + 1].Y, fillHi);
                _d2d.FillQuad(path[s].X, path[s].Y, Rp[s].X, Rp[s].Y, Rp[s + 1].X, Rp[s + 1].Y, path[s + 1].X, path[s + 1].Y, fillLo);
            }
            for (int s = 0; s < n; s++)
            {
                _d2d.DrawLine(Lp[s].X, Lp[s].Y, Rp[s + 1].X, Rp[s + 1].Y, xLine, 1.5f);
                _d2d.DrawLine(Rp[s].X, Rp[s].Y, Lp[s + 1].X, Lp[s + 1].Y, xLine, 1.5f);
            }
            for (int s = 0; s < n; s++)
            {
                _d2d.DrawLine(Lp[s].X, Lp[s].Y, Lp[s + 1].X, Lp[s + 1].Y, hiEdge, 2f);
                _d2d.DrawLine(Rp[s].X, Rp[s].Y, Rp[s + 1].X, Rp[s + 1].Y, loEdge, 2f);
            }
            _d2d.DrawLine(Lp[0].X, Lp[0].Y, Rp[0].X, Rp[0].Y, loEdge, 1.8f);
            _d2d.DrawLine(Lp[n].X, Lp[n].Y, Rp[n].X, Rp[n].Y, hiEdge, 1.8f);
            for (int s = 0; s < n; s++)
                _d2d.DrawLine(path[s].X, path[s].Y, path[s + 1].X, path[s + 1].Y, Color.FromArgb(80, 255, 255, 255), 1f);
        }

        /// <summary>编辑器 3D 视图 arc：世界空间实心棱柱带（宽度沿弧在 x3d-h 平面内的法向展开，四视图统一 3D 投影）。
        /// 上亮下暗 + X 剖面线 + 棱线（同游玩）+ 手柄。</summary>
        void DrawArcPath3D(Note n, double scroll, double windowMs, int W)
        {
            try
            {                int e0 = Math.Max(0, Math.Min(3, n.Col - 2));
                int e1 = n.EndCol >= 0 ? Math.Max(0, Math.Min(3, n.EndCol - 2)) : e0;
                bool leftSide = (e0 + e1) / 2.0 < 2.0;
                var col = leftSide ? Color.FromArgb(255, 59, 156, 255) : Color.FromArgb(255, 255, 88, 88);
                int SEG = 14;
                double hw = A3GlaneW * 0.21;                     // 世界宽度（x3d-h 平面内法向展开）
                var wpts = new (double x, double h, double z)[SEG + 1];
                double dur = Math.Max(1, n.End - n.Time);
                double ArcH(double kk)
                {
                    double y0 = n.Y, y1 = n.EndY;
                    if (n.Arc3 == null || n.Arc3.Count == 0) return y0 + (y1 - y0) * kk;
                    var pts = new List<(double t, double h)> { (0, y0) };
                    foreach (var p in n.Arc3) pts.Add((Math.Max(0, Math.Min(1, p.Y)), Math.Max(0, Math.Min(1, p.Z))));
                    pts.Add((1, y1));
                    pts.Sort((a, b) => a.t.CompareTo(b.t));
                    for (int i = 0; i < pts.Count - 1; i++)
                        if (kk >= pts[i].t && kk <= pts[i + 1].t)
                        {
                            double span = Math.Max(1e-6, pts[i + 1].t - pts[i].t);
                            double k2 = (kk - pts[i].t) / span;
                            return pts[i].h + (pts[i + 1].h - pts[i].h) * k2;
                        }
                    return y1;
                }
                for (int s = 0; s <= SEG; s++)
                {
                    double kk = s / (double)SEG;
                    double t = n.Time + dur * kk;
                    double z = Math.Min(1.3, A3ZOfT(t, scroll, windowMs));
                    double h = A3SkyH * ArcH(kk);
                    double x3d = A3XArcAt(n, kk, e0, e1);
                    wpts[s] = (x3d, h, z);
                }
                // 实心棱柱带：宽度沿 x3d-h 平面内垂直于弧切线的方向（世界法向），上亮下暗（以中心线分界）
                // C4：Decor（虚弧）整体降透明度（实机 void arc 更淡）
                double opq = n.Decor ? 0.45 : 1.0;
                var edge = leftSide ? Color.FromArgb((int)(255 * opq), 18, 56, 130) : Color.FromArgb((int)(255 * opq), 150, 30, 30);
                var fillHi = Color.FromArgb((int)(235 * opq), Math.Min(255, col.R + 48), Math.Min(255, col.G + 48), Math.Min(255, col.B + 48));
                var fillLo = Color.FromArgb((int)(235 * opq), (int)(col.R * 0.5), (int)(col.G * 0.5), (int)(col.B * 0.5));
                var xLine = Color.FromArgb((int)(150 * opq), edge.R, edge.G, edge.B);
                var hiEdge = Color.FromArgb((int)(255 * opq), Math.Min(255, edge.R + 90), Math.Min(255, edge.G + 90), Math.Min(255, edge.B + 90));
                var loEdge = Color.FromArgb((int)(255 * opq), (int)(edge.R * 0.45), (int)(edge.G * 0.45), (int)(edge.B * 0.45));
                var path = new PointF[SEG + 1];
                var Lp2 = new PointF[SEG + 1]; var Rp2 = new PointF[SEG + 1];
                for (int s = 0; s <= SEG; s++)
                {
                    var pr = A3Proj(wpts[s].x, wpts[s].h, wpts[s].z);
                    path[s] = new PointF((float)pr.x, (float)pr.y);
                    // 世界法向：x3d-h 平面内垂直切线
                    int a = Math.Max(0, s - 1), b = Math.Min(SEG, s + 1);
                    double dx = wpts[b].x - wpts[a].x;
                    double dh = wpts[b].h - wpts[a].h;
                    double len = Math.Max(1e-4, Math.Sqrt(dx * dx + dh * dh));
                    double nx = -dh / len, ny = dx / len;
                    double w = hw * Math.Max(0.05, Math.Pow(Math.Max(0.05, wpts[s].z), 1.3));
                    var pl = A3Proj(wpts[s].x - nx * w, wpts[s].h - ny * w, wpts[s].z);
                    var pr2 = A3Proj(wpts[s].x + nx * w, wpts[s].h + ny * w, wpts[s].z);
                    Lp2[s] = new PointF((float)pl.x, (float)pl.y);
                    Rp2[s] = new PointF((float)pr2.x, (float)pr2.y);
                }
                DrawRibbon(path, Lp2, Rp2, fillHi, fillLo, xLine, hiEdge, loEdge);

                // 选中：控制点手柄 + 终点手柄（3D 投影位置）
                if (_ed.SelNote == n)
                {
                    if (n.Arc3 != null)
                        foreach (var c in n.Arc3)
                        {
                            double kk = Math.Max(0, Math.Min(1, c.Y));
                            double tt = n.Time + dur * kk;
                            double zz = A3ZOfT(tt, scroll, windowMs);
                            double hh = A3SkyH * Math.Max(0, Math.Min(1, c.Z));
                            double x3d = A3X3dFree(Math.Max(0, Math.Min(1, c.X)));
                            var pr = A3Proj(x3d, hh, zz);
                            _d2d.DrawRect((float)(pr.x - 5), (float)(pr.y - 5), 10, 10, Color.White, 2f);
                        }
                    var epr = A3Proj(A3X3dOf(e1), A3SkyH * n.EndY, A3ZOfT(n.End, scroll, windowMs));
                    _d2d.DrawRect((float)(epr.x - 6), (float)(epr.y - 6), 12, 12, Color.White, 2f);
                    _d2d.FillEllipse((float)epr.x, (float)epr.y, 4, 4, Color.White);
                }
            }
            catch { }
        }

        // 常规模式音符：柱状长条 + 头部方块
        void DrawLaneNote(Note n, int col, double playX, double laneW, double yHead, bool isLong, Color typeColor, int H, double ppm)
        {
            if (isLong)
            {
                double yTail = TopPad + (n.End - _ed.ScrollMs) * ppm;
                double y1 = Math.Max(yTail, TopPad), y2 = Math.Min(yHead, H);
                if (y2 > y1 && y1 < H && y2 > TopPad)
                {
                    if (n.Type == "arc")
                    {
                        // arc：不画长条矩形（路径已由 DrawArcPath 渲染为棱柱带）
                    }
                    else
                        _d2d.FillRect((float)(playX + col * laneW + laneW * 0.32), (float)y1, (float)(laneW * 0.36), (float)(y2 - y1), Color.FromArgb(130, typeColor.R, typeColor.G, typeColor.B));
                }
            }
            if (yHead < TopPad - 30 || yHead > H + 30) return;
            bool sel = _ed.SelNote == n;
            float nx = (float)(playX + col * laneW);
            float th = (float)Math.Max(10, ppm * _ed.SnapStepVal * 0.9);
            if (n.Type == "arc")
            {
                // arc 头：skytap 菱形（与游玩画面一致），中心在音符时间点
                float cx = nx + (float)laneW / 2f;
                float ds = th * 0.9f;
                _d2d.FillPolygon(new[] {
                    new PointF(cx, (float)yHead - ds),
                    new PointF(cx + ds, (float)yHead),
                    new PointF(cx, (float)yHead + ds),
                    new PointF(cx - ds, (float)yHead) }, typeColor);
                if (sel)
                {
                    var rim = new[] {
                        new PointF(cx, (float)yHead - ds - 3),
                        new PointF(cx + ds + 3, (float)yHead),
                        new PointF(cx, (float)yHead + ds + 3),
                        new PointF(cx - ds - 3, (float)yHead) };
                    _d2d.DrawPolyline(rim, Color.White, 2f, true);
                }
            }
            else
                _d2d.FillRect(nx + 3, (float)(yHead - th / 2), (float)(laneW - 6), th, typeColor);
            if (sel)
                _d2d.DrawRect(nx + 1, (float)(yHead - th / 2) - 3, (float)(laneW - 2), th + 6, Color.White, 2f);
            if (isLong)
            {
                double yTail = TopPad + (n.End - _ed.ScrollMs) * ppm;
                if (yTail > TopPad && yTail < H)
                    _d2d.FillRect(nx + 3, (float)(yTail - 4), (float)(laneW - 6), 8, Color.White);
            }
        }

        /// <summary>arc（Arcaea）：与游玩画面一致的实心棱柱带（法向展开、上亮下暗、带内 X 剖面线、棱线）+ 手柄。</summary>
        void DrawArcPath(Note n, double playX, double laneW, double ppm, int kc, Color col)
        {
            try
            {
                ArcGeometry(n, playX, laneW, ppm, kc, out var start, out var end, out var ctrls);
                var all = new List<PointF> { start };
                all.AddRange(ctrls);
                all.Add(end);
                var samples = BezierScreen(all, 26);
                if (samples.Count >= 2)
                {
                    // 实心棱柱带：法向展开宽度（近粗远细由编辑器横向比例决定），上亮下暗 → 立体感
                    double w = laneW * 0.26;
                    var Lp = new PointF[samples.Count];
                    var Rp = new PointF[samples.Count];
                    for (int i = 0; i < samples.Count; i++)
                    {
                        double dx, dy;
                        if (i < samples.Count - 1) { dx = samples[i + 1].X - samples[i].X; dy = samples[i + 1].Y - samples[i].Y; }
                        else { dx = samples[i].X - samples[i - 1].X; dy = samples[i].Y - samples[i - 1].Y; }
                        double len = Math.Max(1e-4, Math.Sqrt(dx * dx + dy * dy));
                        double nx = -dy / len, ny = dx / len;
                        Lp[i] = new PointF((float)(samples[i].X - nx * w), (float)(samples[i].Y - ny * w));
                        Rp[i] = new PointF((float)(samples[i].X + nx * w), (float)(samples[i].Y + ny * w));
                    }
                    var fillHi = Color.FromArgb(235, Math.Min(255, col.R + 48), Math.Min(255, col.G + 48), Math.Min(255, col.B + 48));
                    var fillLo = Color.FromArgb(235, (int)(col.R * 0.5), (int)(col.G * 0.5), (int)(col.B * 0.5));
                    var edge = Color.FromArgb(255, Math.Max(0, col.R - 90), Math.Max(0, col.G - 90), Math.Max(0, col.B - 90));
                    var xLine = Color.FromArgb(150, edge.R, edge.G, edge.B);
                    var hiEdge = Color.FromArgb(255, Math.Min(255, edge.R + 90), Math.Min(255, edge.G + 90), Math.Min(255, edge.B + 90));
                    var loEdge = Color.FromArgb(255, (int)(edge.R * 0.45), (int)(edge.G * 0.45), (int)(edge.B * 0.45));
                    DrawRibbon(samples.ToArray(), Lp, Rp, fillHi, fillLo, xLine, hiEdge, loEdge);
                }
                if (_ed.SelNote == n)
                {
                    foreach (var c in ctrls)
                        _d2d.DrawRect(c.X - 5, c.Y - 5, 10, 10, Color.White, 2f);
                    _d2d.DrawRect(end.X - 6, end.Y - 6, 12, 12, Color.White, 2f);
                    _d2d.FillEllipse(end.X, end.Y, 4, 4, Color.White);
                }
            }
            catch { }
        }

        /// <summary>ADOFAI 路径折线预览（右上角小轮盘；twirl 段虚线 + 跳过提示）。</summary>
        void DrawAdofaiPreview(int W, int H)
        {
            try
            {
                var list = new List<Note>(_ed.Notes);
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                int cnt = list.Count;
                if (cnt == 0) return;
                double beatMs = _ed.BeatMsVal;
                double t0 = list[0].Time;

                // twirl 区间（两两配对；奇数个时最后一段开放）
                var twirls = new List<(double a, double b)>();
                var te = new List<double>();
                foreach (var ev in _ed.EventList) if (ev != null && ev.Type == "twirl") te.Add(ev.Time);
                te.Sort();
                for (int i = 0; i + 1 < te.Count; i += 2) twirls.Add((te[i], te[i + 1]));
                if (te.Count % 2 == 1) twirls.Add((te[te.Count - 1], double.MaxValue));
                bool InTwirl(double t)
                {
                    foreach (var (a, b) in twirls) if (t > a && t < b) return true;
                    return false;
                }

                // 累计转角（度）：Kind 存转角；全 0 时回退线性累计
                var cum = AdofaiCumulative(list, beatMs);

                float cx = W / 2f, cy = (float)(TopPad + (H - TopPad) * 0.32);
                float R = Math.Min(W * 0.14f, (float)((H - TopPad) * 0.3));
                _d2d.FillEllipse(cx, cy, R + 6, R + 6, Color.FromArgb(150, 8, 12, 22));
                _d2d.DrawEllipse(cx, cy, R + 6, R + 6, Color.FromArgb(200, 90, 220, 255), 1.5f);

                PointF Pos(int i)
                {
                    double deg = 90.0 + cum[i];   // 90°=正下方起点
                    double a = deg * Math.PI / 180.0;
                    return new PointF(cx + (float)(Math.Cos(a) * R), cy + (float)(Math.Sin(a) * R));
                }

                for (int i = 0; i < cnt - 1; i++)
                {
                    var p0 = Pos(i); var p1 = Pos(i + 1);
                    bool twirl = InTwirl(list[i].Time) || InTwirl(list[i + 1].Time);
                    _d2d.DrawLine(p0.X, p0.Y, p1.X, p1.Y,
                        twirl ? Color.FromArgb(170, 255, 140, 200) : Color.FromArgb(220, 140, 220, 255),
                        twirl ? 1.6f : 2.2f, twirl ? 2 : 0);
                }
                for (int i = 0; i < cnt; i++)
                {
                    var p = Pos(i);
                    _d2d.FillEllipse(p.X, p.Y, _ed.SelNote == list[i] ? 6 : 4, _ed.SelNote == list[i] ? 6 : 4,
                        _ed.SelNote == list[i] ? Color.White : Color.FromArgb(200, 200, 210, 230));
                }
                _d2d.Text("路径预览", cx, cy - R - 12, 90, 14, Color.FromArgb(200, 160, 190, 230), 9f, true);
                // twirl 跳过提示
                for (int i = 0; i < cnt - 1; i++)
                    if (InTwirl(list[i].Time) && !InTwirl(list[i + 1].Time))
                        _d2d.Text("TW 跳过", Pos(i).X, Pos(i).Y - 12, 64, 12, Color.FromArgb(220, 255, 140, 200), 8f, true);
            }
            catch { }
        }

        /// <summary>E-2：ADOFAI 俯视棋盘路径主视图——路径在画布大区域按累计角度铺开（每 tile 一段菱形），
        /// 支持点选 tile；末尾虚线提示可点击追加（追加交互在 TracklessMouseDown 的 AdofaiReal 分支）。</summary>
        /* ================= ADOFAI 网格路径编辑器（t5 原版式：固定方格 + 格心 tile + 方向箭头 + 角度数字） ================= */

        /// <summary>ADOFAI 网格几何（与游玩 DrawAdofaiReal 同一累计角输入）：tile 位置 = 从原点沿
        /// "累计角吸附 8 向"的格间行走（screen y 向下；0°=右/90°=下）；自动缩放+全谱居中。
        /// 数据不变（Time/Kind），仅显示/编辑方式原版化。</summary>
        bool AdofaiGridGeom(int W, int H, out PointF[] pos, out double cell, out double ox, out double oy,
            out List<Note> list, out double[] cum)
        {
            pos = null; cell = 64; ox = 0; oy = 0; list = null; cum = null;
            var ln = new List<Note>(_ed.Notes);
            ln.RemoveAll(n => n == null);
            ln.Sort((a, b) => a.Time.CompareTo(b.Time));
            int n = ln.Count;
            if (n == 0) return false;
            list = ln;
            cum = AdofaiCumulative(ln, _ed.BeatMsVal);
            var ux = new double[n]; var uy = new double[n];
            for (int i = 1; i < n; i++)
            {
                int k = (int)Math.Round(cum[i] / 45.0);
                double a = k * 45.0 * Math.PI / 180.0;
                ux[i] = ux[i - 1] + Math.Cos(a);
                uy[i] = uy[i - 1] + Math.Sin(a);
            }
            double minX = ux.Min(), maxX = ux.Max(), minY = uy.Min(), maxY = uy.Max();
            double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
            double availW = Math.Max(160, W - 80), availH = Math.Max(120, H - TopPad - 80);
            cell = Math.Max(16, Math.Min(Math.Min(availW / (spanX + 3), availH / (spanY + 3)), 90.0));
            ox = (W - spanX * cell) / 2 - minX * cell;
            oy = TopPad + 24 + (availH - spanY * cell) / 2 - minY * cell;
            pos = new PointF[n];
            for (int i = 0; i < n; i++)
                pos[i] = new PointF((float)(ox + ux[i] * cell), (float)(oy + uy[i] * cell));
            return true;
        }

        /// <summary>ADOFAI 网格点击解析（原版式）：返回 (命中 tile 索引, 是否追加, 追加/插入的 Kind 角度, 插入位置)。
        /// 优先级：命中 tile &gt; 末格 8 邻=追加 &gt; 非末格 8 邻=插入（该段之后）；格点方向必须恰为 45° 整数倍（格间行走一致）。</summary>
        (int hit, bool append, double newKind, int insert) AdofaiGridHit(float mx, float my, int W, int H,
            out PointF[] pos, out double cell, out double ox, out double oy, out List<Note> list, out double[] cum)
        {
            var r = (-1, false, 0.0, -1);
            if (!AdofaiGridGeom(W, H, out pos, out cell, out ox, out oy, out list, out cum)) return r;
            int n = pos.Length;
            for (int i = 0; i < n; i++)
                if (Math.Abs(pos[i].X - mx) <= cell * 0.42 && Math.Abs(pos[i].Y - my) <= cell * 0.42)
                    return (i, false, 0, -1);
            int ccx = (int)Math.Round((mx - ox) / cell), ccy = (int)Math.Round((my - oy) / cell);
            // out 参数不可被局部函数捕获——拷贝到局部再内联
            var pPos = pos; double pOx = ox, pOy = oy, pCell = cell;
            bool occupied(int cx, int cy)
            {
                for (int i = 0; i < n; i++)
                    if ((int)Math.Round((pPos[i].X - pOx) / pCell) == cx && (int)Math.Round((pPos[i].Y - pOy) / pCell) == cy) return true;
                return false;
            }
            if (occupied(ccx, ccy)) return r;
            // ② 末格 8 邻 → 追加
            int lx = (int)Math.Round((pPos[n - 1].X - pOx) / pCell), ly = (int)Math.Round((pPos[n - 1].Y - pOy) / pCell);
            if (Math.Abs(ccx - lx) <= 1 && Math.Abs(ccy - ly) <= 1 && (ccx != lx || ccy != ly))
            {
                double dirDeg = Math.Atan2(ccy - ly, ccx - lx) * 180.0 / Math.PI;   // 45° 整数倍（8 邻格心）
                double newKind = ChartEditorPanel.NormalizeAdofaiAngle(Math.Round(dirDeg / 15.0) * 15.0 - cum[n - 1]);
                return (-1, true, newKind, -1);
            }
            // ③ 非末格 8 邻 → 插入（该段之后）
            for (int i = 0; i + 1 < n; i++)
            {
                int ix = (int)Math.Round((pPos[i].X - pOx) / pCell), iy = (int)Math.Round((pPos[i].Y - pOy) / pCell);
                if (!(Math.Abs(ccx - ix) <= 1 && Math.Abs(ccy - iy) <= 1 && (ccx != ix || ccy != iy))) continue;
                // 且不是末格的追加候选（追加优先已排除：若同时邻末格则上面已返回）
                double dirDeg2 = Math.Atan2(ccy - iy, ccx - ix) * 180.0 / Math.PI;
                double kind = ChartEditorPanel.NormalizeAdofaiAngle(Math.Round(dirDeg2 / 15.0) * 15.0 - cum[i]);
                return (-1, false, kind, i + 1);
            }
            return r;
        }

        void DrawAdofaiTopdown(int W, int H)
        {
            try
            {
                // 背景板（压住底部赛道路径，保证网格可读）
                _d2d.FillRect(0, (float)TopPad, W, H - (float)TopPad, Color.FromArgb(232, 10, 12, 22));
                if (!AdofaiGridGeom(W, H, out var pos, out double cell, out double ox, out double oy, out var list, out var cum))
                {
                    // 空谱：默认 15×11 网格 + 提示
                    double dc = Math.Min(W / 15.0, (H - TopPad - 30) / 11.0);
                    double dx = (W - 15 * dc) / 2, dy = TopPad + 18;
                    for (int c = 0; c <= 15; c++) _d2d.DrawLine((float)(dx + c * dc), (float)dy, (float)(dx + c * dc), (float)(dy + 11 * dc), Color.FromArgb(60, 70, 90, 130), 1f);
                    for (int yy = 0; yy <= 11; yy++) _d2d.DrawLine((float)dx, (float)(dy + yy * dc), (float)(dx + 15 * dc), (float)(dy + yy * dc), Color.FromArgb(60, 70, 90, 130), 1f);
                    _d2d.Text("ADOFAI 网格路径编辑：点击空格放置起始 tile（方向=该格相对前格方位）", W * 0.5f, (float)(dy + 5 * dc), 560, 18, Color.FromArgb(200, 170, 200, 240), 12f, true);
                    _d2d.Text("点 tile=选中+拖=改角度 · Ctrl+拖=重排 · 点空格=追加/插入 · 右键=删除", W * 0.5f, (float)(dy + 6.6 * dc), 560, 16, Color.FromArgb(160, 140, 180, 220), 10f, true);
                    return;
                }
                int n = pos.Length;
                // —— 网格（路径包围盒 ±2 格） ——
                double lox = ox, loy = oy, lcell = cell;   // out 参数不可被 lambda 捕获
                int gx0 = (int)Math.Floor(pos.Min(p => (p.X - lox) / lcell)) - 2;
                int gx1 = (int)Math.Ceiling(pos.Max(p => (p.X - lox) / lcell)) + 2;
                int gy0 = (int)Math.Floor(pos.Min(p => (p.Y - loy) / lcell)) - 2;
                int gy1 = (int)Math.Ceiling(pos.Max(p => (p.Y - loy) / lcell)) + 2;
                for (int c = gx0; c <= gx1; c++)
                    _d2d.DrawLine((float)(ox + c * cell), (float)(oy + gy0 * cell), (float)(ox + c * cell), (float)(oy + gy1 * cell), Color.FromArgb(56, 64, 86, 124), 1f);
                for (int yy = gy0; yy <= gy1; yy++)
                    _d2d.DrawLine((float)(ox + gx0 * cell), (float)(oy + yy * cell), (float)(ox + gx1 * cell), (float)(oy + yy * cell), Color.FromArgb(56, 64, 86, 124), 1f);

                // —— Twirl 区间（粉紫虚线；配对两两）与 checkpoint/hitsound 装饰 ——
                var te = new List<double>();
                foreach (var ev in _ed.EventList) if (ev != null && ev.Type == "twirl") te.Add(ev.Time);
                te.Sort();
                bool InTwirl(double t)
                {
                    for (int i = 0; i + 1 < te.Count; i += 2)
                        if (t > te[i] && t < te[i + 1]) return true;
                    return te.Count % 2 == 1 && t > te[te.Count - 1];
                }

                // —— 路径线（段 i→i+1；Twirl 区间粉虚线） ——
                for (int i = 0; i + 1 < n; i++)
                {
                    bool tw = InTwirl(list[i].Time) || InTwirl(list[i + 1].Time);
                    _d2d.DrawLine(pos[i].X, pos[i].Y, pos[i + 1].X, pos[i + 1].Y,
                        tw ? Color.FromArgb(210, 255, 120, 200) : Color.FromArgb(200, 150, 190, 255),
                        tw ? 2.0f : 3.0f, tw ? 2 : 0);
                }

                // —— 末端追加提示：末格 8 邻未占用格（半透明绿框） ——
                {
                    int Lx(int i) => (int)Math.Round((pos[i].X - ox) / cell);
                    int Ly(int i) => (int)Math.Round((pos[i].Y - oy) / cell);
                    int lx = Lx(n - 1), ly = Ly(n - 1);
                    int[] dxs = { 1, 1, 0, -1, -1, -1, 0, 1 }, dys = { 0, 1, 1, 1, 0, -1, -1, -1 };
                    for (int k = 0; k < 8; k++)
                    {
                        int dcx = lx + dxs[k], dcy = ly + dys[k];
                        bool occ = false;
                        for (int i = 0; i < n; i++) if (Lx(i) == dcx && Ly(i) == dcy) { occ = true; break; }
                        if (occ) continue;
                        _d2d.DrawRect((float)(ox + dcx * cell - cell * 0.32), (float)(oy + dcy * cell - cell * 0.32),
                            (float)(cell * 0.64), (float)(cell * 0.64), Color.FromArgb(120, 120, 255, 160), 1.4f);
                    }
                }

                // —— tile：圆角方块 + 方向箭头（累计角）+ 角度数字（Kind）/#序号/时间 ——
                for (int i = 0; i < n; i++)
                {
                    var nt = list[i];
                    bool sel = _ed.SelNote == nt;
                    double a = cum[i] * Math.PI / 180.0;   // 方向（屏幕 y 向下；0°=右）
                    double cdx = Math.Cos(a), cdy = Math.Sin(a);
                    double s2 = cell * 0.30;
                    float cx2 = pos[i].X, cy2 = pos[i].Y;
                    // 底色：按类型；选中=亮描边；Decor=空心
                    Color tc = nt.Type == "hold" ? Color.FromArgb(240, 255, 170, 90)
                             : nt.Decor ? Color.FromArgb(150, 120, 130, 160)
                             : Color.FromArgb((i % 2 == 0) ? 232 : 214, 110, 140, 240);
                    _d2d.FillRoundedRect(cx2 - (float)s2, cy2 - (float)s2, (float)(s2 * 2), (float)(s2 * 2), 4f, tc);
                    _d2d.DrawRoundedRect(cx2 - (float)s2, cy2 - (float)s2, (float)(s2 * 2), (float)(s2 * 2), 4f,
                        sel ? Color.White : Color.FromArgb(200, 210, 225, 255), sel ? 2.2f : 1.2f);
                    // 方向箭头（tile 前进方向 = cum[i]）
                    float ax2 = (float)(cx2 + cdx * s2 * 0.52), ay2 = (float)(cy2 + cdy * s2 * 0.52);
                    float hx2 = (float)(ax2 + cdx * s2 * 0.52), hy2 = (float)(ay2 + cdy * s2 * 0.52);
                    float px2 = -(float)cdy, py2 = (float)cdx;   // 法向
                    _d2d.FillPolygon(new[]
                    {
                        new PointF(ax2 + (float)(cdx * s2 * 0.52), ay2 + (float)(cdy * s2 * 0.52)),
                        new PointF((float)(ax2 - cdx * s2 * 0.18) + px2 * (float)(s2 * 0.42), (float)(ay2 - cdy * s2 * 0.18) + py2 * (float)(s2 * 0.42)),
                        new PointF((float)(ax2 - cdx * s2 * 0.18) - px2 * (float)(s2 * 0.42), (float)(ay2 - cdy * s2 * 0.18) - py2 * (float)(s2 * 0.42))
                    }, Color.FromArgb(255, 255, 255, 255));
                    // 角度数字（Kind：数字化角度）+ #序 / 时间
                    _d2d.Text((nt.Kind >= 0 ? "+" : "") + nt.Kind.ToString("0.#") + "°",
                        cx2 - (float)s2, cy2 - (float)(s2 + 14), (float)(s2 * 2), 12, Color.FromArgb(255, 255, 214, 120), 8f, true);
                    _d2d.Text(i + "# " + (nt.Time / 1000.0).ToString("0.00"),
                        cx2 - (float)s2, cy2 + (float)(s2 + 3), (float)(s2 * 2), 11, Color.FromArgb(200, 170, 195, 235), 7.5f, true);
                    // checkpoint / hitsound 装饰标记
                    foreach (var ev in _ed.EventList)
                    {
                        if (ev == null) continue;
                        if (Math.Abs(ev.Time - nt.Time) > 1) continue;
                        if (ev.Type == "checkpoint")
                            _d2d.FillRect(cx2 - (float)s2, cy2 - (float)(s2 - 6), (float)(s2 * 2), 3f, Color.FromArgb(255, 90, 230, 130));
                        else if (ev.Type == "hitsound")
                            _d2d.FillEllipse(cx2 - (float)s2, cy2 - (float)(s2 - 6), 6, 6, Color.FromArgb(255, 150, 200, 255));
                    }
                }

                _d2d.Text("ADOFAI 网格路径（原版式）：点 tile=选中 · 拖=改角度(15°吸附) · Ctrl+拖=重排 · 点空格=追加/中间插入 · 右键=删除 · 滚轮=±15°",
                    12, (float)(TopPad + 2), W - 24, 16, Color.FromArgb(220, 170, 200, 240), 9f);
            }
            catch { }
        }

        /// <summary>ADOFAI 路径预览节点命中（与 DrawAdofaiPreview 同几何）：命中节点返回对应音符。</summary>
        Note AdofaiPreviewHit(float mx, float my, int W, int H)
        {
            try
            {
                var list = new List<Note>(_ed.Notes);
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                int cnt = list.Count;
                if (cnt == 0) return null;
                double beatMs = _ed.BeatMsVal;
                double t0 = list[0].Time;
                var cum = AdofaiCumulative(list, beatMs);
                float cx = W / 2f, cy = (float)(TopPad + (H - TopPad) * 0.32);
                float R = Math.Min(W * 0.14f, (float)((H - TopPad) * 0.3));
                for (int i = 0; i < cnt; i++)
                {
                    double deg = 90.0 + cum[i];
                    double a = deg * Math.PI / 180.0;
                    float px = cx + (float)(Math.Cos(a) * R), py = cy + (float)(Math.Sin(a) * R);
                    if (Math.Abs(mx - px) < 12 && Math.Abs(my - py) < 12) return list[i];
                }
            }
            catch { }
            return null;
        }

        void DrawTracklessField(int W, int H)
        {
            // 播放头/事件求值时间源：静态预览（edshot/未播放）必须用编辑时间 _ed.Time——
            // NowMs() 走音频时钟分支（_playing 时 PositionMs/TickCount），静态/无音频时可能为 0 或与
            // 编辑时间不一致，导致 t=(now-scroll)/windowMs<0 被 DrawFieldPlayhead 越过裁剪跳过（横线不渲染/不稳定）。
            // 播放中 _ed.Time 由 EditorCanvas 30ms 定时器每帧同步为 NowTime（见 ctor tick：_ed.Time=now），
            // 显式区分两态：播放取 NowTime（精确音频时钟），静态取 _ed.Time（编辑时间=edshot 设置值）。
            double now = _ed.Playing ? _ed.NowTime : _ed.Time;
            double scroll = _ed.ScrollMs;
            double beatMs = _ed.BeatMsVal;
            double windowMs = TimeWindowMs(W);
            var f = FieldRect(W, H);

            // 顶部信息条
            _d2d.FillRect(0, 0, W, (float)TopPad, Color.FromArgb(255, 12, 17, 27));
            _d2d.Text("播放头 " + (now / 1000.0).ToString("0.00") + "s · 滚轮±1拍 · 点击放置 · 拖动改位置 · Ctrl+拖=改时间(沿时间条) · 右键删除 · 空格播放", 10, 8, W - 20, 16, Color.FromArgb(255, 160, 178, 208), 9f);
            // ⑤⑥：时间拖拽中的实时时间标签（Ctrl/Alt+拖）
            if (_ed.DragKind == 31 && _ed.SelNote != null)
                _d2d.Text("⏱ 时间 " + (_ed.SelNote.Time / 1000.0).ToString("0.00") + "s（Ctrl+拖=时间轴分量，缩放拉远=全谱定位）",
                    10, 26, W - 20, 16, Color.FromArgb(255, 255, 210, 120), 9f);

            // 场背景
            DrawFieldBackground(f);

            // 事件柱（② 按玩法分类泛化：各玩法事件类别沿垂直时间轴；T58 D2i：ALT+N 音符视图隐藏事件区）
            if (!_ed.NotesOnlyMode)
                DrawEventTimeline(f, W);
            // 音符（IF 大谱性能：按时间窗裁剪——仅绘制播放头前后可见带内的音符。
            // 预览流速 ppms=f.Height*0.0011（≈0.5px/ms），场高对应 ±~500ms；极端旋转时 dx 偏移可达场宽/2（≈+1s）。
            // 取 ±3s 保守窗：带外音符屏幕位置必在场区外，跳过可省 4455→~100 次/帧 D2D 几何创建）
            if (_ed.Mode == GameMode.Phigros && _ed.Notes.Count > 200)
            {
                double cull = 3000;
                foreach (var n in _ed.Notes)
                {
                    if (n == null) continue;
                    double head = n.Time, tail = double.IsNaN(n.End) ? n.Time : Math.Max(n.Time, n.End);
                    if (tail < now - cull || head > now + cull) continue;   // 整条（含 hold 带）在窗内无交集
                    DrawFieldNote(n, f, W, now, beatMs);
                }
            }
            else
                foreach (var n in _ed.Notes) if (n != null) DrawFieldNote(n, f, W, now, beatMs);

            // 播放头指示线（横线，随时间从上→下移动）
            DrawFieldPlayhead(f, W, now, scroll, windowMs);

            // 底部时间条
            DrawTimeBar(W, H, scroll, windowMs, now, beatMs);

            if (_ed.Notes.Count == 0)
                _d2d.Text("点击场放置音符 · 拖动移动 · 右键删除 · 滚轮±1拍（选中音符=±1细分） · Shift+滚轮=时长",
                    W / 2f, f.Y + f.Height / 2 - 30, W - 60, 20, Color.FromArgb(255, 130, 150, 180), 12f, true);
        }

        /// <summary>编辑器事件求值（与游玩端同口径）：[Time,End] 按缓动曲线插值 Value→EndValue；结束后保持 EndValue。
        /// 全局事件（Line&lt;0）作用于所有判定线；同一时刻专属线事件优先。t9：分组缓存+逐帧 memo（大谱 O(1) 查询）。</summary>
        double EdEvalEvent(string type, double def, int line = -1)
        {
            EnsureEvCache();
            if (_evMemo.TryGetValue((type, line), out var memo)) return memo;
            double v = EvalEventScan(type, def, line);
            _evMemo[(type, line)] = v;
            return v;
        }

        /// <summary>重建事件分组缓存。桶结构（分组/排序）仅在 _evStructDirty 时重建（编辑入口设置）；
        /// _evMemo 值缓存每帧失效一次（NowTime 帧常量）。避免大谱（IF 14010 事件）每帧重复 O(E log E) 分组+排序。</summary>
        void EnsureEvCache()
        {
            if (_evStructDirty)
            {
                _evGlobals.Clear();
                _evPerLine.Clear();
                foreach (var e in _ed.EventList)
                {
                    if (e == null || string.IsNullOrEmpty(e.Type)) continue;
                    if (e.Line < 0)
                    {
                        if (!_evGlobals.TryGetValue(e.Type, out var g)) { g = new List<ChartEvent>(); _evGlobals[e.Type] = g; }
                        g.Add(e);
                    }
                    else
                    {
                        if (!_evPerLine.TryGetValue((e.Type, e.Line), out var l)) { l = new List<ChartEvent>(); _evPerLine[(e.Type, e.Line)] = l; }
                        l.Add(e);
                    }
                }
                foreach (var g in _evGlobals.Values) StableSortEvents(g, _evSortTmp);
                foreach (var l in _evPerLine.Values) StableSortEvents(l, _evSortTmp);
                _evStructDirty = false;
            }
            if (!_evMemoDirty) return;
            _evMemo.Clear();
            _evMemoDirty = false;
        }

        /// <summary>慢速求值：全局桶+专属桶两指针合并（时间升序；同时刻专属优先——与旧 "spec 置顶" 语义等价）。</summary>
        double EvalEventScan(string type, double def, int line)
        {
            ChartEvent active = null;
            bool activeHeld = false;
            double bestT = double.MinValue;
            bool bestSpec = false;
            _evGlobals.TryGetValue(type, out var gl);
            List<ChartEvent> pl = null;
            if (line >= 0) _evPerLine.TryGetValue((type, line), out pl);
            else _evPerLine.TryGetValue((type, 0), out pl);
            int gn = gl?.Count ?? 0, pn = pl?.Count ?? 0;
            int i = 0, j = 0;
            while (i < gn || j < pn)
            {
                List<ChartEvent> bucket;
                int idx;
                bool spec;
                if (j >= pn) { bucket = gl; idx = i++; spec = false; }
                else if (i >= gn) { bucket = pl; idx = j++; spec = line >= 0; }   // line<0 查询：线 0 事件与全局同权（与旧口径一致）
                else if (gl[i].Time <= pl[j].Time) { bucket = gl; idx = i++; spec = false; }
                else { bucket = pl; idx = j++; spec = line >= 0; }
                var e = bucket[idx];
                if (e.Time > _ed.NowTime) break;      // 升序：后续均为未来事件（与旧 continue 等价，且提前退出）
                bool ended = !double.IsNaN(e.End) && e.End > e.Time && _ed.NowTime > e.End;
                if (e.Time > bestT || (e.Time == bestT && spec && !bestSpec))
                {
                    active = e; activeHeld = ended; bestT = e.Time; bestSpec = spec;
                }
            }
            if (active == null) return def;
            if (activeHeld) return active.EndValue;
            if (!double.IsNaN(active.End) && active.End > active.Time)
            {
                double k = (_ed.NowTime - active.Time) / (active.End - active.Time);
                k = EvalEventEase(active, k);   // t8②：贝塞尔事件走曲线（原仅按缓动名）
                return active.Value + (active.EndValue - active.Value) * k;
            }
            return active.Value;
        }

        /// <summary>② 预览 speed 场：事件在时刻 t 的取值（t 在区间内按缓动插值；t≥End 返回 EndValue；瞬间事件返回 Value。
        /// 与游玩端 EventValueAt 同语义。</summary>
        double EventValueAt(ChartEvent ev, double t)
        {
            if (double.IsNaN(ev.End) || ev.End <= ev.Time) return ev.Value;
            if (t >= ev.End) return ev.EndValue;
            if (t <= ev.Time) return ev.Value;
            double k = (t - ev.Time) / (ev.End - ev.Time);
            k = EvalEventEase(ev, k);
            return ev.Value + (ev.EndValue - ev.Value) * k;
        }

        /// <summary>② 预览 speed 场关键帧（按线）：全局（Line&lt;0）+ 该线专属事件，区间事件按缓动曲线 SEG=4 采样；
        /// 与游玩端 BuildSpeedKeyframes 同语义（t0 前取末事件求值、尾保持 EndValue、同刻去重）。</summary>
        List<(double t, double v)> BuildSpdKeys(int line, double t0, double t1)
        {
            var ks = new List<(double t, double v)>();
            var evs = new List<ChartEvent>();
            foreach (var ev in _ed.EventList)
            {
                if (ev == null || ev.Type != "speed") continue;
                if (ev.Line >= 0 && ev.Line != line) continue;
                evs.Add(ev);
            }
            if (evs.Count == 0) return ks;
            evs.Sort((a, b) => a.Time.CompareTo(b.Time));
            double curV = 1.0;
            ChartEvent last = null;
            foreach (var ev in evs) { if (ev.Time > t0) break; last = ev; }
            if (last != null) curV = EventValueAt(last, t0);
            ks.Add((t0, curV));
            const int SEG = 4;
            foreach (var ev in evs)
            {
                if (double.IsNaN(ev.End) || ev.End <= ev.Time) continue;
                if (ev.End <= t0 || ev.Time >= t1) continue;
                double s = Math.Max(ev.Time, t0), e = Math.Min(ev.End, t1);
                if (e <= s) continue;
                for (int i = 0; i <= SEG; i++)
                {
                    double u = (double)i / SEG;
                    double tt = s + (e - s) * u;
                    ks.Add((tt, EventValueAt(ev, tt)));
                }
            }
            var le = evs[evs.Count - 1];
            if (!double.IsNaN(le.End) && le.End > t0 && le.End < t1)
                ks.Add((le.End, le.EndValue));
            ks.Add((t1, le.EndValue));
            ks.Sort((a, b) => a.t.CompareTo(b.t));
            var uniq = new List<(double t, double v)>();
            foreach (var k in ks)
                if (uniq.Count == 0 || k.t > uniq[uniq.Count - 1].t + 1e-6)
                    uniq.Add(k);
            return uniq;
        }

        /// <summary>② 预览 speed 场：按线重建梯形积分前缀表（仅速度事件；其余线/无事件=恒速 1.0）。</summary>
        void EnsureSpdCache()
        {
            _spdDirty = false;
            _spdPref.Clear();
            int maxLine = 1;
            foreach (var ev in _ed.EventList) if (ev != null && ev.Line >= maxLine) maxLine = ev.Line + 1;
            maxLine = Math.Max(1, Math.Min(64, maxLine));
            double endT = 600000;
            for (int line = 0; line < maxLine; line++)
            {
                var ks = BuildSpdKeys(line, 0, endT);
                if (ks.Count < 2) continue;
                var pref = new double[ks.Count];
                double acc = 0;
                for (int i = 1; i < ks.Count; i++)
                {
                    double span = ks[i].t - ks[i - 1].t;
                    if (span > 1e-9) acc += (ks[i - 1].v + ks[i].v) * span / 2.0;
                    pref[i] = acc;
                }
                _spdPref[line] = (ks, pref);
            }
        }

        /// <summary>② 预览 speed 场位移（虚拟距离 ms×倍率）：[t0,t1] 梯形积分；与游玩端 SpdDispCached 同语义。</summary>
        double SpdDispCached(int line, double t0, double t1)
        {
            if (t1 <= t0) return 0;
            EnsureSpdCacheIfNeeded();
            if (!_spdPref.TryGetValue(line, out var f)) return t1 - t0;
            var ks = f.ks; var pref = f.pref;
            if (ks.Count < 2) return t1 - t0;
            double F(double t)
            {
                if (t <= ks[0].t) return 0;
                int lo = 0, hi = ks.Count - 1;
                while (lo + 1 < hi) { int m = (lo + hi) >> 1; if (ks[m].t <= t) lo = m; else hi = m; }
                double a = ks[lo].t, b = ks[lo + 1].t;
                double baseV = pref[lo];
                if (t >= b) return baseV + (ks[lo].v + ks[lo + 1].v) * (b - a) / 2.0;
                double p = (t - a) / Math.Max(1e-9, b - a);
                double vt = ks[lo].v + (ks[lo + 1].v - ks[lo].v) * p;
                return baseV + (ks[lo].v + vt) * (t - a) / 2.0;
            }
            return F(t1) - F(t0);
        }

        void EnsureSpdCacheIfNeeded() { if (_spdDirty) EnsureSpdCache(); }

        /// <summary>Phigros 父子线叠加变换：沿父链累加父线的 moveX/moveY/rotate 偏移（防环最多 8 层）。</summary>
        (double moveX, double moveY, double rot) ParentLineTransform(int line, int maxLine)
        {
            double pMoveX = 0, pMoveY = 0, pRot = 0;
            int cur = line;
            var lp = _ed.LineParents;
            for (int depth = 0; depth < 8 && cur >= 0 && cur < lp.Count; depth++)
            {
                int par = lp[cur];
                if (par < 0 || par >= maxLine || par == cur) break;
                pMoveX += EdEvalEvent("moveX", 0.5, par) - 0.5;
                pMoveY += EdEvalEvent("moveY", 0.5, par) - 0.5;
                pRot += EdEvalEvent("rotate", 0, par);
                cur = par;
            }
            return (pMoveX, pMoveY, pRot);
        }

        /// <summary>Phigros 判定线几何（编辑场内，指定线）：线段端点 + 线心 + 旋转角。
        /// 父子线（phimakor）：子线在自身动画基础上叠加父线的 moveX/moveY/rotate（沿父线变换传播）。</summary>
        void PhigrosLineGeom(RectangleF f, int line, out PointF a, out PointF b, out double cX, out double cY, out double rotRad)
        {
            var (pMoveX, pMoveY, pRot) = ParentLineTransform(line, _ed.LineCount);
            double moveY = EdEvalEvent("moveY", 0.5, line) + pMoveY;
            double moveX = EdEvalEvent("moveX", 0.5, line) + pMoveX;
            rotRad = (EdEvalEvent("rotate", 0, line) + pRot) * Math.PI / 180.0;
            double lineY = f.Y + f.Height * Math.Clamp(moveY, 0.02, 0.98);
            double lineX = f.X + f.Width * moveX;
            double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
            PointF P(double x, double y)
            {
                double dx = x - lineX, dy = y - lineY;
                return new PointF((float)(lineX + dx * cosR - dy * sinR), (float)(lineY + dx * sinR + dy * cosR));
            }
            a = P(f.X - 10, lineY);
            b = P(f.X + f.Width + 10, lineY);
            cX = lineX; cY = lineY;
        }

        static double DistToSeg(double x, double y, PointF a, PointF b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - a.X) * abx + (y - a.Y) * aby) / len2)) : 0;
            double dx = x - (a.X + abx * t), dy = y - (a.Y + aby * t);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Phigros 音符屏幕位置（多判定线：按音符所属线求值，PhiMaker 式预览沿线法向落下；子线叠加父线变换）。</summary>
        bool PhigrosNoteScreenPosAt(Note n, RectangleF f, double now, double hitTime, out float px, out float py)
        {
            px = f.X; py = f.Y;
            int ln = Math.Max(0, n.Line);
            var (pMoveX, pMoveY, pRot) = ParentLineTransform(ln, _ed.LineCount);
            double moveY = EdEvalEvent("moveY", 0.5, ln) + pMoveY;
            double moveX = EdEvalEvent("moveX", 0.5, ln) + pMoveX;
            double rot = (EdEvalEvent("rotate", 0, ln) + pRot) * Math.PI / 180.0;
            double lineY = f.Y + f.Height * Math.Clamp(moveY, 0.02, 0.98);
            double lineX = f.X + f.Width * moveX;
            double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
            double ppms = f.Height * 0.0011;                          // 预览基准流速（接近默认游玩速度）
            double yOff = -SpdDispCached(ln, now, hitTime) * ppms;   // ② 预览 speed 场积分：速度事件生效（无事件=恒速，与旧行为一致）
            double dx = f.X + n.X * f.Width - lineX;
            px = (float)(lineX + dx * cosR - yOff * sinR);
            py = (float)(lineY + dx * sinR + yOff * cosR);
            return true;
        }

        /// <summary>在播放头处写入/更新 Phigros 判定线事件（拖线产生 moveY/rotate 关键帧，PhiMaker 式）。</summary>
        void SetLineEventAtPlayhead(string type, double value)
        {
            _ed.SetLineEventAt(type, value, _ed.Time);
            Invalidate();
        }

        /// <summary>Phigros 全部判定线（编辑场内，PhiMaker 式多线）：各线随自己的事件移动/旋转（全部纯白，同实机），当前编辑线高亮。
        /// 父子线：子线叠加父线变换。</summary>
        void DrawPhigrosLine(RectangleF f)
        {
            int lines = Math.Max(1, Math.Min(64, _ed.LineCount));
            // D2o：LineRenderOrder——按 LineMeta.Z 降序绘制（Z 大者在上；无元数据线 Z=0 随后）
            var order = Enumerable.Range(0, lines)
                .Select(i => (i, z: _ed.LineMeta != null && i < _ed.LineMeta.Count ? _ed.LineMeta[i].Z : 0))
                .OrderByDescending(t => t.z).ThenBy(t => t.i).ToList();
            foreach (var (line0, _) in order)
            {
                int line = line0;
                var (pMoveX, pMoveY, pRot) = ParentLineTransform(line, lines);
                double moveY = EdEvalEvent("moveY", 0.5, line) + pMoveY;
                double moveX = EdEvalEvent("moveX", 0.5, line) + pMoveX;
                double rot = (EdEvalEvent("rotate", 0, line) + pRot) * Math.PI / 180.0;
                float lineY = f.Y + (float)(f.Height * Math.Clamp(moveY, 0.02, 0.98));
                float lineX = f.X + (float)(f.Width * moveX);
                double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
                PointF P(double x, double y)
                {
                    double dx = x - lineX, dy = y - lineY;
                    return new PointF((float)(lineX + dx * cosR - dy * sinR), (float)(lineY + dx * sinR + dy * cosR));
                }
                var a = P(f.X - 10, lineY);
                var b = P(f.X + f.Width + 10, lineY);
                bool active = line == _ed.ActiveLine;
                _d2d.DrawLine(a.X, a.Y, b.X, b.Y, Color.FromArgb(active ? 90 : 34, 255, 255, 255), active ? 9f : 6f);
                _d2d.DrawLine(a.X, a.Y, b.X, b.Y, Color.FromArgb(active ? 255 : 150, 255, 255, 255), active ? 3f : 1.6f);
                // 落点刻度
                for (int i = 0; i <= 4; i++)
                {
                    double xc = lineX + (i - 2.0) * f.Width / 8.0;
                    var t1 = P(xc, lineY);
                    var t2 = P(xc, lineY + 8);
                    _d2d.DrawLine(t1.X, t1.Y, t2.X, t2.Y, Color.FromArgb(active ? 120 : 40, 255, 255, 255), active ? 2f : 1f);
                }
                if (active)
                {
                    string lineName = _ed.LineMeta != null && line < _ed.LineMeta.Count
                        && !string.IsNullOrEmpty(_ed.LineMeta[line].Name) ? _ed.LineMeta[line].Name : "线" + (line + 1);
                    _d2d.Text(lineName, a.X + 6, a.Y - 20, 90, 16, Color.FromArgb(255, 255, 255), 10f);
                }
                // 父子线标记：子线显示 →父线 n（青色小字）
                if (_ed.LineParents != null && line < _ed.LineParents.Count && _ed.LineParents[line] >= 0)
                {
                    int par = _ed.LineParents[line];
                    _d2d.Text("→线" + (par + 1), a.X + 6, a.Y + 6, 60, 14, Color.FromArgb(220, 120, 220, 255), 9f);
                }
            }
        }

        void DrawFieldBackground(RectangleF f)
        {
            var m = _ed.Mode;
            float cx = f.X + f.Width / 2, cy = f.Y + f.Height / 2;
            float ringR = Math.Min(f.Width, f.Height) / 2 - 10;
            switch (m)
            {
                case GameMode.Phigros:
                {
                    // Phigros：无轨自由场 + 随事件移动/旋转的白色判定线（无轨道）
                    _d2d.FillRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(220, 8, 11, 18));
                    _d2d.DrawRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(50, 90, 110, 150), 1.5f);
                    DrawPhigrosLine(f);
                    _d2d.Text("Phigros：点击判定线位置放置（时间=播放头）· 拖动判定线=moveY · Shift+拖动=rotate · 拖 hold 尾=时长", f.X + 8, f.Y + 10, f.Width - 16, 16, Color.FromArgb(255, 140, 158, 190), 9f);
                    break;
                }
                case GameMode.Cytus:
                {
                    _d2d.FillRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(220, 12, 16, 26));
                    for (int i = 1; i < 8; i++)
                    {
                        float y = f.Y + f.Height * i / 8f;
                        _d2d.DrawLine(f.X, y, f.X + f.Width, y, Color.FromArgb(24, 90, 110, 150), 1f);
                    }
                    _d2d.DrawRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(80, 90, 110, 150), 1.5f);
                    break;
                }
                case GameMode.OsuStandard:
                {
                    _d2d.FillRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(200, 10, 14, 24));
                    _d2d.DrawRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(80, 150, 170, 210), 1.5f);
                    // osu 吸附网格（osu-master PositionSnapGrid：细分网格辅助对齐，16 格）
                    if (_ed.ShowGrid)
                    {
                        int g = 16;
                        for (int i = 1; i < g; i++)
                        {
                            float gx = f.X + f.Width * i / g;
                            float gy = f.Y + f.Height * i / g;
                            _d2d.DrawLine(gx, f.Y, gx, f.Y + f.Height, Color.FromArgb(16, 130, 150, 190), 1f);
                            _d2d.DrawLine(f.X, gy, f.X + f.Width, gy, Color.FromArgb(16, 130, 150, 190), 1f);
                        }
                        // 中心十字（点击吸附基准）
                        float gcx = f.X + f.Width / 2, gcy = f.Y + f.Height / 2;
                        _d2d.DrawLine(gcx - 10, gcy, gcx + 10, gcy, Color.FromArgb(60, 160, 180, 220), 1.2f);
                        _d2d.DrawLine(gcx, gcy - 10, gcx, gcy + 10, Color.FromArgb(60, 160, 180, 220), 1.2f);
                    }
                    break;
                }
                case GameMode.Maimai:
                {
                    // QA-5：maimai 环形场——8 分区按钮环 + 中心 touch 区（与游玩端同语义）
                    _d2d.FillRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(210, 10, 14, 26));
                    _d2d.DrawRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(70, 110, 140, 190), 1.5f);
                    float mcx = f.X + f.Width / 2, mcy = f.Y + f.Height / 2;
                    float mrr = Math.Min(f.Width, f.Height) / 2 - 10;
                    _d2d.DrawEllipse(mcx, mcy, mrr, mrr, Color.FromArgb(110, 120, 160, 210), 2.5f);
                    for (int k = 1; k <= 8; k++)
                    {
                        double a = (k * 45 % 360) * Math.PI / 180.0;
                        float bx = (float)(mcx + Math.Sin(a) * mrr), by = (float)(mcy - Math.Cos(a) * mrr);
                        float br = Math.Max(6, mrr * 0.13f);
                        _d2d.FillEllipse(bx, by, br, br, Color.FromArgb(160, 40, 90, 190));
                        _d2d.DrawEllipse(bx, by, br, br, Color.FromArgb(150, 200, 220, 255), 1.5f);
                    }
                    _d2d.FillEllipse(mcx, mcy, Math.Max(5, mrr * 0.12f), Math.Max(5, mrr * 0.12f), Color.FromArgb(150, 200, 110, 60));
                    _d2d.Text("maimai：点击环上放置音符（8 分区）· 拖动=移动 · 拖 hold 尾=时长", f.X + 8, f.Y + 10, f.Width - 16, 16, Color.FromArgb(255, 140, 158, 190), 9f);
                    break;
                }
            }
        }

        /// <summary>无轨音符屏幕位置（与绘制/命中一致）。</summary>
        bool FieldNotePos(Note n, RectangleF f, int W, out float px, out float py, out float r)
        {
            px = 0; py = 0; r = 8;
            var m = _ed.Mode;
            float cx = f.X + f.Width / 2, cy = f.Y + f.Height / 2;
            float ringR = Math.Min(f.Width, f.Height) / 2 - 10;
            switch (m)
            {
                case GameMode.Cytus:
                {
                    px = f.X + (float)(n.X * f.Width); py = f.Y + (float)(n.Y * f.Height); r = 9; break;
                }
                case GameMode.Phigros:
                {
                    // PhiMaker 式预览：音符随剩余时间向判定线落下（命中时刻恰在线上）
                    PhigrosNoteScreenPosAt(n, f, _ed.Time, n.Time, out px, out py);
                    r = 9; break;
                }
                case GameMode.OsuStandard:
                {
                    px = f.X + (float)(n.X * f.Width); py = f.Y + (float)(n.Y * f.Height); r = Math.Max(7, f.Height * 0.09f); break;
                }
                case GameMode.Maimai:
                {
                    // QA-5：maimai 8 分区环形——按 Col 方位角放置（与游玩端 MaimaiButtonPos 同语义，1=右上 45° 顺时针）
                    int mk = Math.Max(1, _ed.CanvasKc);
                    int mi = Math.Max(1, Math.Min(mk, n.Col));
                    double ang = (mi * 45 % 360) * Math.PI / 180.0;
                    double rr = Math.Min(f.Width, f.Height) / 2 - 10;
                    px = (float)(cx + Math.Sin(ang) * rr);
                    py = (float)(cy - Math.Cos(ang) * rr);
                    r = Math.Max(7, f.Height * 0.06f);
                    break;
                }
                default: return false;
            }
            return true;
        }

        void DrawFieldNote(Note n, RectangleF f, int W, double now, double beatMs)
        {
            var m = _ed.Mode;
            bool sel = _ed.SelNote == n;
            bool isLong = _ed.IsLong(n);
            Color col = _ed.NoteColor(n);
            float cx = f.X + f.Width / 2, cy = f.Y + f.Height / 2;
            float ringR = Math.Min(f.Width, f.Height) / 2 - 10;

            switch (m)
            {
                case GameMode.Phigros:
                {
                    // Phigros 音符：横置扁胶囊（rpe_f3_60 实机判据：长轴平行判定线，宽:高≈5:1+，约114×10px）
                    // tap 白芯浅蓝边 / flick 红胶囊+上箭头 / drag 粉胶囊；hold 头胶囊+竖直带（尾柄保留，可拖拽改时长）
                    PhigrosNoteScreenPosAt(n, f, _ed.Time, n.Time, out float px, out float py);
                    float w = Math.Max(18, f.Width * 0.13f);
                    if (n.Width > 0 && Math.Abs(n.Width - 1.0) > 1e-6) w *= (float)n.Width;   // ③ 宽度字段（面板宽度，1.0=默认）
                    float h = Math.Max(6, w * 0.24f);   // 用户"太细"：编辑/游玩同宽度比加粗（对应游玩 tapH=tapW*0.24）
                    float rad = h / 2;
                    // ③ 透明度字段（0..1，面板已备；默认 1=不透明，0=隐藏——命中/拖拽仍可用）
                    float noteAlpha = 1f;
                    if (n.Alpha >= 0 && n.Alpha < 1) noteAlpha = (float)n.Alpha;
                    // ③ 可视时间字段（命中前展示窗口；999999=默认全显）
                    if (n.VisMs > 0 && n.VisMs < 999999 && n.Time - _ed.Time > n.VisMs) break;
                    if (isLong)
                    {
                        // hold（用户标准：横置扁胶囊，与其他音符一致——不画竖带；尾部小白柄表示时长方向）
                        _d2d.FillRoundedRect(px - w / 2, py - h / 2, w, h, h / 2, Color.FromArgb((int)(235 * noteAlpha), 190, 255, 250));
                        _d2d.DrawRoundedRect(px - w / 2, py - h / 2, w, h, h / 2, Color.FromArgb((int)(255 * noteAlpha), 150, 200, 255), 1.4f);
                        PhigrosNoteScreenPosAt(n, f, _ed.Time, n.End, out float pxt, out float pyt);
                        // 尾柄（可拖拽改时长）——表示保留时长方向
                        _d2d.FillEllipse(pxt, pyt, Math.Max(3, w * 0.06f), Math.Max(3, w * 0.06f), Color.White);
                        return;   // hold 分支完成：防止走下方 else 的 tap 白胶囊覆盖青白
                    }
                    if (n.Type == "drag")
                    {
                        // DRAG：半透明粉胶囊（实机：触到判定线即可）
                        _d2d.FillRoundedRect(px - w / 2, py - h / 2, w, h, rad, Color.FromArgb((int)(110 * noteAlpha), 255, 175, 220));
                        _d2d.DrawRoundedRect(px - w / 2, py - h / 2, w, h, rad, Color.FromArgb((int)(180 * noteAlpha), 255, 210, 240), 1.5f);
                    }
                    else if (n.Type == "flick")
                    {
                        var red = Color.FromArgb(255, 255, 77, 109);
                        _d2d.FillRoundedRect(px - w / 2, py - h / 2, w, h, rad, red);
                        // 上箭头（flick 朝向）：胶囊上方白色三角
                        _d2d.FillPolygon(new[] { new PointF(px, py - h / 2 - Math.Max(4,h*0.35f)), new PointF(px - 6, py - h / 2 - 1), new PointF(px + 6, py - h / 2 - 1) }, Color.White);
                    }
                    else
                    {
                        // tap：白芯浅蓝边横置扁胶囊（实机 rpe_f3_60：宽:高≈5:1+）
                        _d2d.FillRoundedRect(px - w / 2, py - h / 2, w, h, rad, Color.FromArgb((int)(255 * noteAlpha), 245, 250, 255));
                        _d2d.DrawRoundedRect(px - w / 2, py - h / 2, w, h, rad, Color.FromArgb((int)(255 * noteAlpha), 150, 200, 255), 1.4f);
                    }
                    if (sel) _d2d.DrawEllipse(px, py, 12, 12, Color.White, 2f);
                    break;
                }
                case GameMode.Cytus:
                {
                    float px = f.X + (float)(n.X * f.Width), py = f.Y + (float)(n.Y * f.Height);
                    if (isLong) _d2d.FillRect(px - 3, f.Y, 6, f.Height, Color.FromArgb(110, col.R, col.G, col.B));
                    _d2d.FillEllipse(px, py, 8, 8, col);
                    if (n.Type == "drag") _d2d.DrawEllipse(px, py, 10, 10, col, 1.5f);
                    if (sel) _d2d.DrawEllipse(px, py, 12, 12, Color.White, 2f);
                    break;
                }
                case GameMode.OsuStandard:
                    DrawOsuFieldNote(n, f, col, sel);
                    break;
                case GameMode.Maimai:
                {
                    // QA-5：maimai 8 分区环形音符（与游玩端 MaimaiButtonPos 同语义）
                    if (!FieldNotePos(n, f, W, out float mpx, out float mpy, out float mr)) break;
                    _d2d.FillEllipse(mpx, mpy, mr, mr, col);
                    _d2d.DrawEllipse(mpx, mpy, mr + 1.5f, mr + 1.5f, Color.FromArgb(180, 255, 255, 255), 1f);
                    if (isLong)
                    {
                        // hold：径向短线
                        float mx2 = f.X + f.Width / 2, my2 = f.Y + f.Height / 2;
                        _d2d.DrawLine(mpx, mpy, mx2, my2, Color.FromArgb(110, col.R, col.G, col.B), 3f);
                    }
                    if (sel) _d2d.DrawEllipse(mpx, mpy, mr + 4, mr + 4, Color.White, 2f);
                    break;
                }
            }

            // 选中长条：显示结束时间标签
            if (sel && isLong)
                _d2d.Text("结束 " + (n.End / 1000.0).ToString("0.00") + "s", f.X + 8, f.Y + 6, 130, 14, Color.FromArgb(255, 255, 210, 80), 9f);
        }

        /// <summary>事件时间轴（② 按玩法分类泛化的垂直事件柱）：场景右侧约 38% 列区——
        /// x=通道（列数=该玩法事件类别数：Phigros 实机五柱 moveX/moveY/rotate/alpha/speed、
        /// Cytus speed/bpm/mode、ADOFAI bpm/mode/twirl……）、y=时间从上到下推进、柱宽=值、柱内嵌黄色缓动曲线。
        /// 菱形=瞬间键帧（蓝=勾定/End=NaN），横条=区间事件，横线=当前播放头。几何：ColumnGeom/ValueToWidth01/EaseCurvePts/TimeToY。</summary>
        void DrawEventTimeline(RectangleF f, int W)
        {
            try
            {
                double windowMs = TimeWindowMs(W);
                double scroll = _ed.ScrollMs;
                double t0 = scroll, t1 = scroll + windowMs;
                // —— 列区几何（右侧 38%）：ColumnGeom(k) = 第 k 通道列矩形 ——
                double colX0 = f.Right + 12;
                double colW = Math.Max(40, W - SidePad - colX0);          // 列区总宽
                string[] kinds = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                int kn = Math.Max(1, kinds.Length);
                Color[] cols = new Color[kn];
                for (int i = 0; i < kn; i++) cols[i] = ChartEditorPanel.EventColor(kinds[i]);
                // 数值范围（rotate/speed/noteSpeed/bpm/scroll 按类型；其余归一 0..1）
                (double lo, double hi) Range(int k) => ChartEditorPanel.EventRangeForKind(kinds[k]);
                // —— 垂直柱几何工具（RPE 复刻核心） ——
                // 列几何：kn 列等分列区，列内留 2px 间隙
                RectangleF ColumnGeom(int k)
                {
                    float cw = (float)((colW - 8) / kn);
                    return new RectangleF((float)(colX0 + k * (cw + 2)), f.Y, cw, f.Height);
                }
                // 值 → 0..1 归一（柱宽比例）
                double ValueToWidth01(int k, double v)
                {
                    var (lo, hi) = Range(k);
                    return Math.Max(0, Math.Min(1, (v - lo) / (hi - lo)));
                }
                // 时间 → y（顶部 scroll → 底部 scroll+windowMs）
                double TimeToY(double t) => f.Y + (t - t0) / Math.Max(1, windowMs) * f.Height;
                // 缓动曲线采样：事件 → 列表（(时间, 值01)），区间事件按缓动形状 16 段（复用缓冲，零 GC）
                List<(double t, double v01)> EaseCurvePts(ChartEvent ev, int k)
                {
                    var pts = _tlPtBuf;
                    pts.Clear();
                    bool isRange = !double.IsNaN(ev.End) && ev.End > ev.Time;
                    if (isRange)
                    {
                        for (int s = 0; s <= 16; s++)
                        {
                            double p = s / 16.0;
                            double v = ev.Value + (ev.EndValue - ev.Value) * EvalEventEase(ev, p);
                            pts.Add((ev.Time + (ev.End - ev.Time) * p, ValueToWidth01(k, v)));
                        }
                    }
                    else pts.Add((ev.Time, ValueToWidth01(k, ev.Value)));
                    return pts;
                }

                // 背景 + 标题
                _d2d.FillRect((float)colX0 - 6, f.Y - 2, (float)(colW + 10), f.Height + 4, Color.FromArgb(150, 6, 10, 18));
                _d2d.Text((_ed.Mode == GameMode.Phigros ? "事件柱（线" + (_ed.ActiveLine + 1) + "）· " : "事件柱（" + ModeSystem.DisplayName(_ed.Mode) + "）· ")
                    + string.Join("/", kinds) + "（点=键帧，竖拖时间·横拖值，右键=缓动）",
                    (float)colX0 - 4, f.Y - 16, (float)(colW + 2), 12, Color.FromArgb(220, 160, 190, 230), 8f);
                double beatMs = _ed.BeatMsVal;
                // rpe_f60 判据：列区左侧拍标尺（每 4 拍标数字；列间共享一列标尺）
                if (beatMs > 1)
                {
                    double b0 = Math.Floor(scroll / beatMs) * beatMs;
                    for (double bt = b0; bt <= t1; bt += beatMs)
                    {
                        float gy = (float)TimeToY(bt);
                        if (gy < f.Y || gy > f.Y + f.Height) continue;
                        long beatIdx = (long)Math.Round(bt / beatMs);
                        if (beatIdx % 4 == 0)
                            _d2d.Text((beatIdx / 4 + 1).ToString(), (float)(colX0 - 24), gy - 6, 22, 12, Color.FromArgb(180, 150, 170, 210), 8f);
                    }
                }
                _tlEvBuf.Clear();
                // t6：直接按线取桶（EnsureEvCache 已按 (type,line) 分组排序）——免每帧全量扫描 _ed.EventList（IF 14010 事件）
                if (_evStructDirty) { _evMemoDirty = true; EnsureEvCache(); }
                var lineL = _ed.ActiveLine;
                foreach (var kv in _evPerLine)
                    if (kv.Key.line == lineL) foreach (var e in kv.Value) _tlEvBuf.Add(e);
                foreach (var g in _evGlobals.Values) foreach (var e in g) _tlEvBuf.Add(e);
                var evs = _tlEvBuf;
                // 每列网格拍横线 + 中值参考线
                for (int k = 0; k < kinds.Length; k++)
                {
                    var r = ColumnGeom(k);
                    if (beatMs > 1)
                    {
                        double b0 = Math.Floor(scroll / beatMs) * beatMs;
                        for (double bt = b0; bt <= t1; bt += beatMs)
                        {
                            float gy = (float)TimeToY(bt);
                            if (gy < f.Y || gy > f.Y + f.Height) continue;
                            _d2d.DrawLine(r.Left, gy, r.Right, gy, Color.FromArgb(30, 120, 140, 180), 1f);
                        }
                    }
                    float midY = f.Y + f.Height * 0.5f;   // 中值线=整柱中线（值 0.5）
                    _d2d.DrawLine(r.Left, midY, r.Right, midY, Color.FromArgb(22, 140, 160, 200), 1f);
                    _d2d.Text(kinds[k], r.Left, f.Y - 14, r.Width, 12, cols[k], 7.5f);
                    // 柱子：逐事件填充（柱宽=值01×列宽，从列左缘起）（复用缓冲，零 GC；稳定排序保 OrderBy 语义）
                    _tlChainBuf.Clear();
                    for (int ei = 0; ei < evs.Count; ei++)
                        if (evs[ei].Type == kinds[k]) _tlChainBuf.Add(evs[ei]);
                    StableSortEvents(_tlChainBuf, _tlChainTmp);
                    var chain = _tlChainBuf;
                    foreach (var ev in chain)
                    {
                        var pts = EaseCurvePts(ev, k);
                        bool isRange = pts.Count > 1;
                        if (isRange)
                        {
                            // 区间事件：采样柱轮廓（多段矩形近似）+ 缓动曲线描边（黄色）
                            for (int s = 0; s < pts.Count - 1; s++)
                            {
                                float y0 = (float)TimeToY(pts[s].t), y1 = (float)TimeToY(pts[s + 1].t);
                                float w0 = (float)(pts[s].v01 * r.Width);
                                _d2d.FillRect(r.Left, y0, Math.Max(2, w0), Math.Max(1, y1 - y0),
                                    Color.FromArgb(90, cols[k].R, cols[k].G, cols[k].B));
                            }
                            // 缓动曲线折线（黄色）（复用缓冲，零 GC）
                            if (_tlSegPts.Length < pts.Count) _tlSegPts = new PointF[pts.Count];
                            for (int s = 0; s < pts.Count; s++)
                                _tlSegPts[s] = new PointF(r.Left + (float)(pts[s].v01 * r.Width), (float)TimeToY(pts[s].t));
                            _d2d.DrawPolyline(_tlSegPts, pts.Count, Color.FromArgb(230, 255, 220, 90), 1.4f, false);
                            // 首/尾拖柄（顶部=头、底部=尾）
                            float yt = (float)TimeToY(ev.Time), yb = (float)TimeToY(ev.End);
                            _d2d.FillEllipse(r.Left + (float)(pts[0].v01 * r.Width), yt, 6, 6, Color.White);
                            _d2d.FillEllipse(r.Left + (float)(pts[pts.Count - 1].v01 * r.Width), yb, 6, 6, Color.White);
                        }
                        else
                        {
                            // 瞬间件：柱宽→一小段带 + 菱形键帧
                            float x = r.Left + (float)(pts[0].v01 * r.Width);
                            float y = (float)TimeToY(ev.Time);
                            if (double.IsNaN(ev.End) || Math.Abs(ev.EndValue - ev.Value) < 1e-9)
                            {
                                // 勾定（End=NaN/头尾同值）：蓝色菱形（D2n 实机判据）（复用缓冲）
                                _tlDiamond[0] = new PointF(x, y - 6); _tlDiamond[1] = new PointF(x + 6, y);
                                _tlDiamond[2] = new PointF(x, y + 6); _tlDiamond[3] = new PointF(x - 6, y);
                                _d2d.FillPolygon(_tlDiamond, Color.FromArgb(255, 80, 160, 255));
                                _d2d.Text(ev.Value.ToString("0.##"), x + 7, y - 8, 46, 11, Color.FromArgb(255, 140, 200, 255), 7f);
                            }
                            else
                            {
                                _d2d.FillEllipse(x - 5, y - 5, 10, 10, cols[k]);
                                _d2d.Text(ev.Value.ToString("0.##"), x + 7, y - 8, 46, 11, cols[k], 7f);
                            }
                            // 选中高亮：白环 + 白色菱形描边（Ctrl/Shift 点选 / 框选命中 / 主选中）
                            bool isSel = _ed.SelectedEvts().Contains(ev) || _ed.SelEvt == ev;
                            if (isSel)
                            {
                                _d2d.DrawEllipse(x, y, 14, 14, Color.White, 2f);
                            }
                        }
                    }
                    // 选中事件（含 Bezier）手柄：4 控制点方点（首尾锁，D2f 复刻）
                    if (_ed.SelEvt != null && _ed.SelEvt.Type == kinds[k] && _ed.SelEvt.Bezier != null && _ed.SelEvt.Bezier.Length >= 8)
                    {
                        var bev = _ed.SelEvt;
                        double dur = Math.Max(1, (double.IsNaN(bev.End) ? bev.Time : bev.End) - bev.Time);
                        for (int bi = 0; bi < 4; bi++)
                        {
                            double bx = bev.Bezier[bi * 2], by = bev.Bezier[bi * 2 + 1];
                            float hx = r.Left + (float)(by * r.Width);
                            float hy = (float)TimeToY(bev.Time + bx * dur);
                            _d2d.FillRect(hx - 5, hy - 5, 10, 10, bi == 0 || bi == 3 ? Color.FromArgb(80, 200, 255) : Color.FromArgb(255, 220, 220, 220));
                            _d2d.DrawRect(hx - 5, hy - 5, 10, 10, Color.White, 1.2f);
                        }
                    }
                    // 播放头横线
                    float phy = (float)TimeToY(_ed.NowTime);
                    if (phy >= f.Y && phy <= f.Y + f.Height)
                        _d2d.DrawLine(r.Left, phy, r.Right, phy, Color.FromArgb(220, 255, 200, 60), 1.5f);
                    // rpe_f60 判据：柱底部值标签（当前线该通道最近事件的值；无事件显示 0）
                    {
                        var lastEv = chain.LastOrDefault();
                        double vShow = lastEv != null
                            ? (_ed.NowTime <= lastEv.Time ? lastEv.Value
                                : (!double.IsNaN(lastEv.End) && _ed.NowTime <= lastEv.End
                                    ? lastEv.Value + (lastEv.EndValue - lastEv.Value) * EvalEventEase(lastEv, Math.Max(0, Math.Min(1, (_ed.NowTime - lastEv.Time) / Math.Max(1, lastEv.End - lastEv.Time))))
                                    : lastEv.Value))
                            : 0;
                        _d2d.Text(vShow.ToString(lastEv != null && lastEv.Type == "rotate" ? "0.0" : "0.##"),
                            r.Left + r.Width / 2 - 14, f.Y + f.Height + 2, 28, 12, cols[k], 8f, true);
                    }
                }
                // 框选橡皮筋（DragKind=29）
                if (_ed.DragKind == 29)
                {
                    float xa = (float)Math.Min(_ed.DragStartX, _ed.DragEndX), xb = (float)Math.Max(_ed.DragStartX, _ed.DragEndX);
                    float ya = (float)Math.Min(_ed.DragStartY, _ed.DragEndY), yb = (float)Math.Max(_ed.DragStartY, _ed.DragEndY);
                    _d2d.DrawRect(xa, ya, xb - xa, yb - ya, Color.FromArgb(180, 255, 220, 120), 1.2f);
                    _d2d.FillRect(xa, ya, xb - xa, yb - ya, Color.FromArgb(18, 255, 220, 120));
                }
            }
            catch { }
        }

        /// <summary>事件按 Time 稳定归并排序（等价 OrderBy 稳定语义；复用 tmp 缓冲，零逐帧分配）。</summary>
        static void StableSortEvents(List<ChartEvent> list, List<ChartEvent> tmp)
        {
            int n = list.Count;
            if (n < 2) return;
            if (tmp.Capacity < n) tmp.Capacity = n;
            while (tmp.Count < n) tmp.Add(null);
            for (int len = 1; len < n; len <<= 1)
            {
                for (int lo = 0; lo < n; lo += len << 1)
                {
                    int mid = Math.Min(lo + len, n), hi = Math.Min(lo + (len << 1), n);
                    int i = lo, j = mid, k = lo;
                    while (i < mid && j < hi)
                    {
                        if (list[i].Time <= list[j].Time) { tmp[k++] = list[i++]; }
                        else tmp[k++] = list[j++];
                    }
                    while (i < mid) tmp[k++] = list[i++];
                    while (j < hi) tmp[k++] = list[j++];
                    for (int q = lo; q < hi; q++) list[q] = tmp[q];
                }
            }
        }

        /// <summary>编辑器缓动求值（委托 ApplyEaseStatic——同一套公式，避免双实现漂移）。</summary>
        static double ApplyEaseEditor(string ease, double t) => ChartEditorPanel.ApplyEaseStatic(ease, t);

        /// <summary>事件缓动求值：贝塞尔事件（ev.Bezier≠null → 4 控制点曲线）优先，否则按缓动名（t8 ② ApplyCurveEditor 采样）。
        /// internal：ChartEditorPanel 快捷编辑（切割中点求值）复用。</summary>
        internal static double EvalEventEase(ChartEvent ev, double t)
        {
            if (ev != null && ev.Bezier != null && ev.Bezier.Length >= 8)
                return BezierEval(ev.Bezier, Math.Max(0, Math.Min(1, t)));
            return ApplyEaseEditor(ev != null ? ev.Ease : null, t);
        }

        /// <summary>RPE 贝塞尔缓动求值：4 控制点 (x,y)，端点 (0,0)→(1,1)（Bezier[0/1]=0、Bezier[6/7]=1），x 单调不减。
        /// x(u)=t 二分 40 次 → y(u)（RPE bezierPoints 语义：两点制转换后的主项目 4 点格式）。</summary>
        static double BezierEval(double[] bz, double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double X(double u) => 3 * (1 - u) * (1 - u) * u * bz[2] + 3 * (1 - u) * u * u * bz[4] + u * u * u;
            double Y(double u) => 3 * (1 - u) * (1 - u) * u * bz[3] + 3 * (1 - u) * u * u * bz[5] + u * u * u;
            double lo = 0, hi = 1;
            for (int i = 0; i < 40; i++)
            {
                double u = (lo + hi) / 2;
                if (X(u) < t) lo = u; else hi = u;
            }
            return Y((lo + hi) / 2);
        }
        void DrawOsuFieldNote(Note n, RectangleF f, Color col, bool sel)
        {
            float px = f.X + (float)(n.X * f.Width);
            float py = f.Y + (float)(n.Y * f.Height);
            float r = Math.Max(6, f.Height * 0.08f);
            if (n.Type == "spin")
            {
                DrawSpiral(px, py, r, col);
                if (sel) _d2d.DrawEllipse(px, py, r + 4, r + 4, Color.White, 2f);
                return;
            }
            if (n.Type == "hold")
            {
                // 按 SliderType 采样路径（L 折线 / B 贝塞尔 / C Catmull-Rom / P 三点圆）
                var samples = SampleOsuScreen(n, f, 48);
                if (samples.Count >= 2)
                    _d2d.DrawPolyline(samples.ToArray(), col, 3f, false);
                // 完美圆：附淡色参考圆
                if (n.SliderType == 'P')
                {
                    var ctrl = OsuCtrlScreenPts(n, f);
                    if (ctrl.Count >= 3)
                    {
                        var (cx2, cy2, r2) = CircumCircle(ctrl[0], ctrl[1], ctrl[2]);
                        if (r2 > 1) _d2d.DrawEllipse(cx2, cy2, (float)r2, (float)r2, Color.FromArgb(70, col.R, col.G, col.B), 1f);
                    }
                }
                // 终点圆
                var tail = samples[samples.Count - 1];
                _d2d.DrawEllipse(tail.X, tail.Y, r * 0.6f, r * 0.6f, Color.White, 2f);
                // 控制点方块（选中时；索引 1..n-1，头由音符本体拖动）
                if (sel)
                {
                    var ctrlPts = OsuCtrlScreenPts(n, f);
                    for (int i = 1; i < ctrlPts.Count; i++)
                        _d2d.DrawRect(ctrlPts[i].X - 5, ctrlPts[i].Y - 5, 10, 10, Color.White, 2f);
                }
            }
            _d2d.FillEllipse(px, py, r, r, col);
            if (n.Type == "hold") _d2d.DrawEllipse(px, py, r, r, Color.White, 1.5f);
            if (sel) _d2d.DrawEllipse(px, py, r + 4, r + 4, Color.White, 2f);
            // osu-master SelectionBox：选中时显示包围盒 + 旋转/缩放手柄（角=缩放，右上圆=旋转）
            if (sel)
            {
                var (bx, by, bw, bh) = OsuSelBox(n, f, r);
                _d2d.DrawRect(bx, by, bw, bh, Color.FromArgb(140, 200, 220, 255), 1.2f);
                // 四角缩放柄（零分配的显式四点，替代逐帧 new 元组数组）
                _d2d.FillEllipse(bx, by, 4, 4, Color.FromArgb(255, 255, 220, 120));
                _d2d.FillEllipse(bx + bw, by, 4, 4, Color.FromArgb(255, 255, 220, 120));
                _d2d.FillEllipse(bx, by + bh, 4, 4, Color.FromArgb(255, 255, 220, 120));
                _d2d.FillEllipse(bx + bw, by + bh, 4, 4, Color.FromArgb(255, 255, 220, 120));
                // 右上旋转柄
                _d2d.FillEllipse(bx + bw, by - 14, 5, 5, Color.FromArgb(255, 140, 220, 255));
                _d2d.DrawLine(bx + bw, by, bx + bw, by - 14, Color.FromArgb(160, 140, 220, 255), 1.2f);
            }
        }

        // 阿基米德螺旋（转盘标记）：半径随角度从 r 递减到 ~0（复用缓冲，零 GC）
        void DrawSpiral(float cx, float cy, float r, Color c)
        {
            int steps = 64;
            float turns = 2.5f;
            var pts = _spiralPts;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double a = t * turns * Math.PI * 2;
                double rr = Math.Max(0.5, r * (1 - t));
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr));
            }
            _d2d.DrawPolyline(pts, c, 2f, false);
        }

        /* ================= 四模式编辑增强：采样 / 命中 / 辅助 ================= */

        // ---- 通用几何 ----
        static double NormalizeAngle(double a)
        {
            a = a % (2 * Math.PI);
            if (a > Math.PI) a -= 2 * Math.PI;
            if (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static bool CircumCircleD(double x1, double y1, double x2, double y2, double x3, double y3, out double cx, out double cy, out double r)
        {
            cx = cy = r = 0;
            double d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
            if (Math.Abs(d) < 1e-9) return false;
            double a = x1 * x1 + y1 * y1, b = x2 * x2 + y2 * y2, c = x3 * x3 + y3 * y3;
            cx = (a * (y2 - y3) + b * (y3 - y1) + c * (y1 - y2)) / d;
            cy = (a * (x3 - x2) + b * (x1 - x3) + c * (x2 - x1)) / d;
            r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
            return r > 1e-6;
        }

        static (float cx, float cy, float r) CircumCircle(PointF a, PointF b, PointF c)
        {
            if (CircumCircleD(a.X, a.Y, b.X, b.Y, c.X, c.Y, out var cx, out var cy, out var r))
                return ((float)cx, (float)cy, (float)r);
            return (0, 0, 0);
        }

        static double DistToSegment(float px, float py, float x1, float y1, float x2, float y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            if (dx == 0 && dy == 0) { double a = px - x1, b = py - y1; return Math.Sqrt(a * a + b * b); }
            double t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            double ex = x1 + t * dx - px, ey = y1 + t * dy - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        // ---- osu 滑条采样（归一化 0..1） ----
        (double X, double Y) BezierPointNorm(List<(double X, double Y)> p, double t)
        {
            var tmp = new List<(double X, double Y)>(p);
            while (tmp.Count > 1)
            {
                var next = new List<(double X, double Y)>();
                for (int i = 0; i < tmp.Count - 1; i++)
                    next.Add((tmp[i].X + (tmp[i + 1].X - tmp[i].X) * t, tmp[i].Y + (tmp[i + 1].Y - tmp[i].Y) * t));
                tmp = next;
            }
            return tmp[0];
        }

        (double X, double Y) CatmullPointNorm(List<(double X, double Y)> p, double t, int i)
        {
            var p0 = p[Math.Max(0, i - 1)];
            var p1 = p[i];
            var p2 = p[Math.Min(p.Count - 1, i + 1)];
            var p3 = p[Math.Min(p.Count - 1, i + 2)];
            double t2 = t * t, t3 = t2 * t;
            double x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
            double y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
            return (x, y);
        }

        /// <summary>三点定圆弧采样（P 完美圆：用三次贝塞尔等价弧近似，采样圆弧）。</summary>
        List<(double X, double Y)> PerfectCircleNorm(List<(double X, double Y)> ctrl, int segs)
        {
            var pts = new List<(double X, double Y)>();
            try
            {
                if (ctrl.Count < 3) return pts;
                var A = ctrl[0]; var B = ctrl[1]; var C = ctrl[2];
                if (!CircumCircleD(A.X, A.Y, B.X, B.Y, C.X, C.Y, out var cx, out var cy, out var r)) return pts;
                double aA = Math.Atan2(A.Y - cy, A.X - cx);
                double aB = Math.Atan2(B.Y - cy, B.X - cx);
                double aC = Math.Atan2(C.Y - cy, C.X - cx);
                double dB = NormalizeAngle(aB - aA);
                double dC = NormalizeAngle(aC - aA);
                double sweep;
                if (Math.Abs(dC) < 1e-9 && Math.Abs(dB) > 1e-9)
                    sweep = dB > 0 ? 2 * Math.PI : -2 * Math.PI;   // A==C：整圆
                else
                {
                    sweep = dC;
                    bool bIn = sweep >= 0 ? (dB >= -1e-9 && dB <= sweep + 1e-9)
                                          : (dB <= 1e-9 && dB >= sweep - 1e-9);
                    if (!bIn && Math.Abs(dB) > 1e-9)
                        sweep = dC - Math.Sign(dC) * 2 * Math.PI;   // 走补弧以经过 B
                }
                for (int s = 0; s <= segs; s++)
                {
                    double a = aA + sweep * (s / (double)segs);
                    pts.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
                }
            }
            catch { }
            return pts;
        }

        /// <summary>osu 滑条路径采样（归一化）：L 折线 / B De Casteljau / C Catmull-Rom / P 三点圆。</summary>
        List<(double X, double Y)> SampleOsuNorm(Note n, int segs)
        {
            var pts = new List<(double X, double Y)>();
            try
            {
                var ctrl = _ed.OsuCurvePoints(n);
                if (ctrl.Count < 2) return pts;
                char st = n.SliderType;
                if (st == 'B')
                {
                    var b = ctrl;
                    if (b.Count > 4) b = b.GetRange(0, 4);
                    for (int s = 0; s <= segs; s++) pts.Add(BezierPointNorm(b, s / (double)segs));
                }
                else if (st == 'P')
                {
                    pts = PerfectCircleNorm(ctrl, segs);
                    if (pts.Count < 2) foreach (var p in ctrl) pts.Add(p);   // 共线退化回退折线
                }
                else if (st == 'C')
                {
                    int span = Math.Max(1, ctrl.Count - 1);
                    int per = Math.Max(1, segs / span);
                    for (int i = 0; i < span; i++)
                        for (int k = 0; k <= per; k++)
                            pts.Add(CatmullPointNorm(ctrl, k / (double)per, i));
                }
                else
                {
                    foreach (var p in ctrl) pts.Add(p);   // 'L' 折线
                }
            }
            catch { }
            if (pts.Count < 2) pts = _ed.OsuCurvePoints(n);
            return pts;
        }

        List<PointF> OsuCtrlScreenPts(Note n, RectangleF f)
        {
            var list = new List<PointF>();
            foreach (var p in _ed.OsuCurvePoints(n))
                list.Add(new PointF(f.X + (float)(p.X * f.Width), f.Y + (float)(p.Y * f.Height)));
            return list;
        }

        /// <summary>osu-master SelectionBox 包围盒（归一化坐标 + 屏幕矩形）：hold 滑条含全部控制点。</summary>
        (float bx, float by, float bw, float bh) OsuSelBox(Note n, RectangleF f, float r)
        {
            double minX = n.X, minY = n.Y, maxX = n.X, maxY = n.Y;
            if (n.Type == "hold")
                foreach (var p in OsuCtrlScreenPts(n, f))
                {
                    minX = Math.Min(minX, (p.X - f.X) / f.Width); maxX = Math.Max(maxX, (p.X - f.X) / f.Width);
                    minY = Math.Min(minY, (p.Y - f.Y) / f.Height); maxY = Math.Max(maxY, (p.Y - f.Y) / f.Height);
                }
            return (f.X + (float)(minX * f.Width) - r - 4, f.Y + (float)(minY * f.Height) - r - 4,
                    (float)((maxX - minX) * f.Width) + 2 * (r + 4), (float)((maxY - minY) * f.Height) + 2 * (r + 4));
        }

        List<PointF> SampleOsuScreen(Note n, RectangleF f, int segs)
        {
            var list = new List<PointF>();
            foreach (var p in SampleOsuNorm(n, segs))
                list.Add(new PointF(f.X + (float)(p.X * f.Width), f.Y + (float)(p.Y * f.Height)));
            return list;
        }

        // ---- osu 滑条命中 ----
        int HitOsuCtrlPt(Note n, float x, float y, RectangleF f)
        {
            var pts = OsuCtrlScreenPts(n, f);
            for (int i = 1; i < pts.Count; i++)   // 跳过头部（头由音符本体拖拽）
                if (Math.Abs(pts[i].X - x) <= 9 && Math.Abs(pts[i].Y - y) <= 9) return i;
            return -1;
        }

        bool NearOsuPath(Note n, float x, float y, RectangleF f)
        {
            var samples = SampleOsuScreen(n, f, 48);
            for (int i = 0; i < samples.Count - 1; i++)
                if (DistToSegment(x, y, samples[i].X, samples[i].Y, samples[i + 1].X, samples[i + 1].Y) <= 12) return true;
            return false;
        }

        int NearestOsuCtrlPt(Note n, float x, float y, RectangleF f)
        {
            var pts = OsuCtrlScreenPts(n, f);
            int bestIdx = 1; double best = double.MaxValue;
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = pts[i].X - x, dy = pts[i].Y - y;
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>命中 osu 滑条：kind 0 无 | 1 控制点(idx) | 2 路径中间(返回最近控制点 idx)。</summary>
        (Note note, int kind, int idx) HitOsuSlider(float x, float y, RectangleF f)
        {
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null || n.Type != "hold") continue;
                int c = HitOsuCtrlPt(n, x, y, f);
                if (c >= 1) return (n, 1, c);
            }
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null || n.Type != "hold") continue;
                if (NearOsuPath(n, x, y, f)) return (n, 2, NearestOsuCtrlPt(n, x, y, f));
            }
            return (null, 0, -1);
        }

        // ---- arc（Arcaea，有轨）几何 / 采样 / 命中 ----
        void ArcGeometry(Note n, double playX, double laneW, double ppm, int kc, out PointF start, out PointF end, out List<PointF> ctrls)
        {
            double windowMs = TimeWindowMs(ClientSize.Width);
            double scroll = _ed.ScrollMs;
            if (_ed.Mode == GameMode.Arcaea)
            {
                // 3D 编辑视图：起点/控制点/终点按 3D 投影（同游玩画面）
                int e0 = Math.Max(0, Math.Min(3, n.Col - 2));
                int e1 = n.EndCol >= 0 ? Math.Max(0, Math.Min(3, n.EndCol - 2)) : e0;
                double dur = Math.Max(1, n.End - n.Time);
                double z0 = A3ZOfT(n.Time, scroll, windowMs);
                var p0 = A3Proj(A3X3dOf(e0), A3SkyH * n.Y, z0);
                start = new PointF((float)p0.x, (float)p0.y);
                double z1 = A3ZOfT(n.End, scroll, windowMs);
                var p1 = A3Proj(A3X3dOf(e1), A3SkyH * n.EndY, z1);
                end = new PointF((float)p1.x, (float)p1.y);
                ctrls = new List<PointF>();
                if (n.Arc3 != null)
                    foreach (var c in n.Arc3)
                    {
                        double kk = Math.Max(0, Math.Min(1, c.Y));
                        double t = n.Time + dur * kk;
                        double z = A3ZOfT(t, scroll, windowMs);
                        double h = A3SkyH * Math.Max(0, Math.Min(1, c.Z));
                        double x3d = A3X3dFree(Math.Max(0, Math.Min(1, c.X)));
                        var pc = A3Proj(x3d, h, z);
                        ctrls.Add(new PointF((float)pc.x, (float)pc.y));
                    }
                return;
            }
            start = new PointF((float)(playX + (n.Col + 0.5) * laneW), (float)(TopPad + (n.Time - scroll) * ppm));
            int endCol = n.EndCol >= 0 ? n.EndCol : n.Col;
            end = new PointF((float)(playX + (endCol + 0.5) * laneW), (float)(TopPad + (n.End - scroll) * ppm));
            ctrls = new List<PointF>();
            double dur2 = Math.Max(1, n.End - n.Time);
            if (n.Arc3 != null && n.Arc3.Count > 0)
            {
                // 3D arc：控制点 (X=轨, Y=时间比例, Z=天地间高度)；Arc3D 打开时高度横向展开（伪 3D 预览）
                foreach (var c in n.Arc3)
                {
                    double x = playX + ChartEditorPanel.Clamp01(c.X) * laneW * kc;
                    double t = n.Time + ChartEditorPanel.Clamp01(c.Y) * dur2;
                    double spread = _ed.Arc3D ? (ChartEditorPanel.Clamp01(c.Z) - 0.5) * laneW * 2.2 : 0;
                    ctrls.Add(new PointF((float)(x + spread), (float)(TopPad + (t - scroll) * ppm)));
                }
            }
            else if (n.Curve != null)
            {
                foreach (var c in n.Curve)
                {
                    double x = playX + ChartEditorPanel.Clamp01(c.X) * laneW * kc;
                    double t = n.Time + ChartEditorPanel.Clamp01(c.Y) * dur2;
                    ctrls.Add(new PointF((float)x, (float)(TopPad + (t - scroll) * ppm)));
                }
            }
        }

        List<PointF> BezierScreen(List<PointF> p, int segs)
        {
            var pts = new List<PointF>();
            if (p.Count < 2) return pts;
            for (int s = 0; s <= segs; s++)
            {
                double t = s / (double)segs;
                var tmp = new List<PointF>(p);
                while (tmp.Count > 1)
                {
                    var next = new List<PointF>();
                    for (int i = 0; i < tmp.Count - 1; i++)
                        next.Add(new PointF(tmp[i].X + (tmp[i + 1].X - tmp[i].X) * (float)t, tmp[i].Y + (tmp[i + 1].Y - tmp[i].Y) * (float)t));
                    tmp = next;
                }
                pts.Add(tmp[0]);
            }
            return pts;
        }

        /// <summary>命中 arc 部件：kind 0 无 | 1 控制点(idx) | 2 终点手柄 | 3 路径。</summary>
        (Note note, int kind, int idx) HitArcHandle(float x, float y, double playX, double laneW, double ppm, int kc)
        {
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null || n.Type != "arc") continue;
                ArcGeometry(n, playX, laneW, ppm, kc, out var s, out var e, out var cs);
                for (int k = 0; k < cs.Count; k++)
                    if (Math.Abs(cs[k].X - x) <= 9 && Math.Abs(cs[k].Y - y) <= 9) return (n, 1, k);
            }
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null || n.Type != "arc") continue;
                ArcGeometry(n, playX, laneW, ppm, kc, out var s, out var e, out var cs);
                if (Math.Abs(e.X - x) <= 10 && Math.Abs(e.Y - y) <= 10) return (n, 2, 0);
            }
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null || n.Type != "arc") continue;
                ArcGeometry(n, playX, laneW, ppm, kc, out var s, out var e, out var cs);
                var all = new List<PointF> { s }; all.AddRange(cs); all.Add(e);
                var samples = BezierScreen(all, 24);
                for (int k = 0; k < samples.Count - 1; k++)
                    if (DistToSegment(x, y, samples[k].X, samples[k].Y, samples[k + 1].X, samples[k + 1].Y) <= 10) return (n, 3, 0);
            }
            return (null, 0, -1);
        }

        void DrawFieldPlayhead(RectangleF f, int W, double now, double scroll, double windowMs)
        {
            // 横线播放头：时间推进=从上到下（与 Phigros 实机 RPE 一致）
            // Y = f.Y 对应 now=scroll（窗口顶部），Y = f.Y+f.Height 对应 now=scroll+windowMs（窗口底部）
            if (windowMs > 0)
            {
                double t = (now - scroll) / windowMs;
                if (t < -0.05 || t > 1.05) return; // 超出可视区域则跳过
                float y = (float)(f.Y + t * f.Height);
                _d2d.DrawLine(f.X, y, f.X + f.Width, y, Color.FromArgb(220, 255, 90, 90), 2f);
            }
        }

        void DrawTimeBar(int W, int H, double scroll, double windowMs, double now, double beatMs)
        {
            float y0 = (float)(H - TimeBarH);
            _d2d.FillRect(0, y0, W, (float)TimeBarH, Color.FromArgb(255, 12, 17, 27));
            _d2d.DrawLine(0, y0, W, y0, Color.FromArgb(255, 60, 78, 104), 1f);

            // 拍刻度（每 4 拍小节加粗并标时间）
            if (beatMs > 1)
            {
                double b0 = Math.Floor(scroll / beatMs) * beatMs;
                for (double bt = b0; bt <= scroll + windowMs + beatMs; bt += beatMs)
                {
                    double x = (bt - scroll) / windowMs * W;
                    if (x < 0 || x > W) continue;
                    bool measure = ((long)Math.Round(bt / beatMs)) % 4 == 0;
                    _d2d.DrawLine((float)x, y0, (float)x, (float)H, measure ? Color.FromArgb(160, 120, 150, 200) : Color.FromArgb(60, 70, 96, 140), 1f);
                    if (measure)
                    {
                        double sec = bt / 1000.0;
                        // RPE 拍:0/1 时间显示（仅 Phigros；其余模式保持 分:秒）
                        string label = _ed.Mode == GameMode.Phigros
                            ? ChartEditorPanel.BeatTimeText(bt, beatMs)
                            : (sec / 60).ToString("0") + ":" + (sec % 60).ToString("00");
                        _d2d.Text(label, (float)x + 2, y0 + 2, 70, 12, Color.FromArgb(255, 140, 158, 190), 8f);
                    }
                }
            }

            // 事件标记（类型着色 + 缩写）
            foreach (var ev in _ed.EventList)
            {
                if (ev == null) continue;
                double x = (ev.Time - scroll) / windowMs * W;
                if (x < -8 || x > W + 8) continue;
                var ec = ChartEditorPanel.EventColor(ev.Type);
                _d2d.DrawLine((float)x, y0, (float)x, H, ec, 1f);
                _d2d.Text(ChartEditorPanel.EventAbbr(ev.Type), (float)x - 8, y0 + 14, 40, 12, ec, 7f, true);
            }

            // ⑤⑥：音符时间刻度（全谱概览——整曲时间轴上每音符一个 1px 色标；选中音符高亮）
            foreach (var n in _ed.Notes)
            {
                if (n == null) continue;
                double nx = (n.Time - scroll) / windowMs * W;
                if (nx < -1 || nx > W + 1) continue;
                var nc2 = _ed.NoteColor(n);
                bool sel2 = _ed.SelNote == n;
                _d2d.FillRect((float)(nx - (sel2 ? 1.5f : 0.5f)), y0, sel2 ? 3f : 1f, sel2 ? 10 : 6,
                    sel2 ? Color.FromArgb(255, 255, 210, 120) : Color.FromArgb(190, nc2.R, nc2.G, nc2.B));
            }

            // 播放头
            double phx = (now - scroll) / windowMs * W;
            if (phx >= 0 && phx <= W)
            {
                _d2d.DrawLine((float)phx, y0, (float)phx, H, Color.FromArgb(255, 255, 90, 90), 2f);
                _d2d.FillRect((float)(phx - 20), y0 + 1, 40, 12, Color.FromArgb(220, 160, 40, 40));
                string phLabel = _ed.Mode == GameMode.Phigros
                    ? ChartEditorPanel.BeatTimeText(now, beatMs)
                    : (now / 1000.0).ToString("0.00") + "s";
                _d2d.Text(phLabel, (float)(phx - 20), y0 + 2, 40, 11, Color.White, 8f, true);
            }
        }

        /* ---------- 无轨点击 → 连续场坐标 ---------- */
        /// <summary>无轨点击 → (fx, fy, col, t)。</summary>
        void TracklessFieldAt(double px, double py, RectangleF f, int W, out double fx, out double fy, out int col, out double t)
        {
            fx = 0.5; fy = 0.5; col = 0; t = _ed.Time;
            var m = _ed.Mode;
            double lx = ChartEditorPanel.Clamp01((px - f.X) / Math.Max(1, f.Width));
            double ly = ChartEditorPanel.Clamp01((py - f.Y) / Math.Max(1, f.Height));

            switch (m)
            {
                case GameMode.Cytus:
                    fx = lx; fy = ly; col = ((int)(fx * 4) + 4) % 4; break;
                case GameMode.Phigros:
                    fx = lx; fy = 0.5; col = -1; break;   // 判定线上自由位置
                case GameMode.OsuStandard:
                    fx = lx; fy = ly; col = 0;
                    // C4：osu 空间吸附——网格开启时吸附到 16 格（与渲染网格一致，lazer PositionSnapGrid 语义）
                    if (_ed.ShowGrid)
                    {
                        const int g = 16;
                        fx = Math.Round(fx * g) / g;
                        fy = Math.Round(fy * g) / g;
                    }
                    break;
                case GameMode.Maimai:
                    // QA-5：maimai 落点=水平位置量化 8 分区（Col 1~8）
                    fx = lx; fy = 0.5; col = Math.Max(1, Math.Min(Math.Max(1, _ed.CanvasKc), (int)Math.Floor(lx * Math.Max(1, _ed.CanvasKc)) + 1)); break;
            }
        }

        Note HitTestFieldNote(double x, double y, RectangleF f, int W)
        {
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null) continue;
                if (!FieldNotePos(n, f, W, out var px, out var py, out var r)) continue;
                if (x >= px - r - 4 && x <= px + r + 4 && y >= py - r - 4 && y <= py + r + 4) return n;
            }
            return null;
        }

        ChartEvent HitTestEventAt(double x, int W, double windowMs)
        {
            for (int i = _ed.EventList.Count - 1; i >= 0; i--)
            {
                var ev = _ed.EventList[i];
                if (ev == null) continue;
                double ex = (ev.Time - _ed.ScrollMs) / windowMs * W;
                if (Math.Abs(ex - x) < 6) return ev;
            }
            return null;
        }

        /// <summary>事件柱键帧命中（P0 垂直几何）：按列（x 通道）+ y=TimeToY 反解命中返回 (通道, 序号, 事件)；未命中 null。</summary>
        (int k, int idx, ChartEvent ev)? HitCurveKeyframe(double mx, double my, int W)
        {
            try
            {
                if (_ed.IsTracklessMode == false) return null;
                var f = FieldRect(W, ClientSize.Height);
                double windowMs = TimeWindowMs(W);
                double t0 = _ed.ScrollMs, t1 = t0 + windowMs;
                // —— 垂直柱列区几何（与 DrawEventTimeline 同源） ——
                double colX0 = f.Right + 12;
                double colW = Math.Max(40, W - SidePad - colX0);
                if (mx < colX0 - 4 || mx > colX0 + colW + 6 || my < f.Y - 8 || my > f.Y + f.Height + 4) return null;
                string[] kinds = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                int kn = Math.Max(1, kinds.Length);
                double cwf = (colW - 8) / kn;               // 列宽（命中 x 判据，先定列）
                int k = (int)((mx - colX0) / (cwf + 2));
                if (k < 0 || k >= kn) return null;
                var chain = _ed.EventList.Where(e => e != null && e.Type == kinds[k]
                    && (e.Line == _ed.ActiveLine || e.Line < 0)).OrderBy(e => e.Time).ToList();
                double TimeToY(double t) => f.Y + (t - t0) / Math.Max(1, windowMs) * f.Height;
                double colLeft = colX0 + k * (cwf + 2);
                for (int i = 0; i < chain.Count; i++)
                {
                    var ev = chain[i];
                    double y = TimeToY(ev.Time);
                    // 命中判据：y 时间 ±9px 且鼠标在本列内（x 距列左缘 ≤ 列宽+9px——列区域 + 边缘容差）
                    if (Math.Abs(y - my) < 9 && mx >= colLeft - 9 && mx <= colLeft + cwf + 9)
                        return (k, i, ev);
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>贝塞尔手柄命中（D2f 复刻）：选中事件含 Bezier → 4 控制点方点（首尾锁），按垂直柱几何定位，返回 (通道, 手柄索引)。</summary>
        (int k, int hi)? HitBezierHandle(double mx, double my, int W)
        {
            try
            {
                if (_ed.IsTracklessMode == false) return null;
                var ev = _ed.SelEvt;
                if (ev == null || ev.Bezier == null || ev.Bezier.Length < 8) return null;
                var f = FieldRect(W, ClientSize.Height);
                double windowMs = TimeWindowMs(W);
                double t0 = _ed.ScrollMs;
                double colX0 = f.Right + 12;
                double colW = Math.Max(40, W - SidePad - colX0);
                string[] kinds = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                int kn = Math.Max(1, kinds.Length);
                double cw = (colW - 8) / kn;
                int k = Array.IndexOf(kinds, ev.Type);
                if (k < 0) return null;
                double cxx = colX0 + k * (cw + 2);
                double TimeToY(double t) => f.Y + (t - t0) / Math.Max(1, windowMs) * f.Height;
                double dur = Math.Max(1, (double.IsNaN(ev.End) ? ev.Time : ev.End) - ev.Time);
                for (int hi = 0; hi < 4; hi++)
                {
                    double bx = ev.Bezier[hi * 2], by = ev.Bezier[hi * 2 + 1];
                    double hx = cxx + by * cw;
                    double hy = TimeToY(ev.Time + bx * dur);
                    if (Math.Abs(mx - hx) < 9 && Math.Abs(my - hy) < 9)
                        return (k, hi);
                }
                return null;
            }
            catch { return null; }
        }

        /* ---------- 交互 ---------- */
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            try
            {
                if (_ed.Mode == GameMode.Arcaea && (ModifierKeys & Keys.Shift) != 0)
                {
                    // Shift+滚轮：3D 摄像头缩放（编辑视图移动摄像头）
                    Arc3DSetup(ClientSize.Width, ClientSize.Height);
                    A3CamZ = Math.Max(0.3, Math.Min(3.0, A3CamZ * (e.Delta > 0 ? 1.12 : 0.89)));
                    Invalidate();
                    return;
                }
                if ((ModifierKeys & Keys.Control) != 0)
                {
                    // ⑤⑥：平滑指数缩放（每档 ±1.2×，跨滚轮步连续；0.003~8 px/ms）
                    _ed.PxPerMs = _ed.PxPerMs * Math.Pow(1.2, e.Delta / 120.0);
                    _ed.SyncZoomBoxPublic();
                }
                else if ((_ed.Mode == GameMode.Adofai || _ed.Mode == GameMode.AdofaiReal) && _ed.SelNote != null)
                {
                    // ADOFAI：滚轮 = 调整选中节点转角（官方编辑器节点编辑）
                    double dir = e.Delta > 0 ? 1 : -1;
                    _ed.PushUndo();
                    _ed.SelNote.Kind = ChartEditorPanel.NormalizeAdofaiAngle(_ed.SelNote.Kind + dir * 15.0);
                    _ed.MarkDirty();
                    _ed.SyncAdofaiAngleBoxPublic();
                    Invalidate();
                }
                else if (_ed.IsTracklessMode)
                {
                    double dir = e.Delta > 0 ? 1 : -1;
                    if (_ed.SelNote != null)
                    {
                        if ((ModifierKeys & Keys.Shift) != 0 && _ed.IsLong(_ed.SelNote))
                            _ed.NudgeSelectedDuration(dir * _ed.SnapStepVal);   // Shift+滚轮=时长
                        else
                            _ed.NudgeSelected(dir * _ed.SnapStepVal);           // 选中音符+滚轮=±1 细分步长时间
                    }
                    else
                    {
                        _ed.SeekDuringPlay(Math.Max(0, _ed.Time + dir * _ed.BeatMsVal));   // 滚轮±1 拍
                        _ed.EnsureTimeVisible(TimeWindowMs(ClientSize.Width));
                    }
                }
                else
                    _ed.ScrollMs = Math.Max(0, _ed.ScrollMs - e.Delta / 4.0 / _ed.PxPerMs * 3);
                Invalidate();
            }
            catch { }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            // AdofaiReal 俯视编辑器：Tracked 模式但有独立俯视交互(点选/追加/角度)——按 Trackless 分发
            if (_ed.IsTracklessMode || _ed.Mode == GameMode.AdofaiReal)
            {
                try { TracklessMouseDown(e); } catch { }
                return;
            }

            var (playX, laneW) = LayoutRect();
            double t = _ed.ScrollMs + (e.Y - TopPad) / _ed.PxPerMs;
            bool arc3d = _ed.Mode == GameMode.Arcaea;
            double windowMs = TimeWindowMs(ClientSize.Width);
            if (arc3d) { Arc3DSetup(ClientSize.Width, ClientSize.Height); var up = A3Unproject(e.X, e.Y, _ed.ScrollMs, windowMs); t = up.t; }
            int colAt = arc3d ? ArcColFromXY(e.X, e.Y) : (int)((e.X - playX) / laneW);

            if (e.Button == MouseButtons.Middle)
            {
                _ed.DragKind = arc3d ? 14 : 4;   // Arcaea 3D：中键=移动摄像头
                _ed.DownY = e.Y;
                _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                return;
            }
            if (e.Y <= TopPad)   // 标尺：点击/拖动 seek
            {
                _ed.DragKind = 3;
                SeekAt(e.Y);
                return;
            }
            // ADOFAI：点击路径预览上的节点 = 选中该音符（官方编辑器节点编辑）
            // E-2：Routlock(Adofai) 走轮盘预览——点节点选中（AdofaiReal 由 TracklessMouseDown 网格分发，不在此路径）
            if (_ed.Mode == GameMode.Adofai && e.Button == MouseButtons.Left)
            {
                var hitN = AdofaiPreviewHit(e.X, e.Y, ClientSize.Width, ClientSize.Height);
                if (hitN != null)
                {
                    _ed.SelNote = hitN;
                    _ed.SyncAdofaiAngleBoxPublic();
                    Invalidate();
                    return;
                }
            }
            if (e.Button == MouseButtons.Right)
            {
                // 先删音符本体（头部）
                var hit0 = arc3d ? Arc3DHitNote(e.X, e.Y) : _ed.HitTestNote(e.X, e.Y, _ed.CanvasKc, playX, laneW, TopPad);
                if (hit0 != null) { _ed.DeleteNote(hit0); return; }
                // arc 控制点右键删除 / 终点手柄右键复位（Arcaea）
                if (arc3d)
                {
                    var ah = HitArcHandle(e.X, e.Y, playX, laneW, _ed.PxPerMs, _ed.CanvasKc);
                    if (ah.note != null)
                    {
                        _ed.PushUndo();
                        if (ah.kind == 1) _ed.DeleteArcCtrlPoint(ah.note, ah.idx);
                        else if (ah.kind == 2) _ed.ClearArcEnd(ah.note);
                        Invalidate();
                        return;
                    }
                }
                var ev = _ed.HitTestEvent(t, 12 / _ed.PxPerMs);
                if (ev != null) { _ed.DeleteEvent(ev); return; }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // 双击 arc 路径插入控制点（仅当不在头部）
            if (arc3d && e.Clicks >= 2)
            {
                if (Arc3DHitNote(e.X, e.Y) == null)
                {
                    var dh = HitArcHandle(e.X, e.Y, playX, laneW, _ed.PxPerMs, _ed.CanvasKc);
                    if (dh.note != null && dh.kind == 3)
                    {
                        double dur = Math.Max(1, dh.note.End - dh.note.Time);
                        var up = A3Unproject(e.X, e.Y, _ed.ScrollMs, windowMs);
                        double colNorm = up.lane / 4.0;   // lane 0..4 ↔ 归一 X 0..1（与 X3dFree 一致）
                        double tFrac = (up.t - dh.note.Time) / dur;
                        _ed.PushUndo();
                        if (_ed.InsertArcCtrlPoint(dh.note, colNorm, tFrac)) _ed.SelNote = dh.note;
                        Invalidate();
                        return;
                    }
                }
            }

            // 头部（音符本体）：时间 / 移动 / 拉尾
            var n = arc3d ? Arc3DHitNote(e.X, e.Y) : _ed.HitTestNote(e.X, e.Y, _ed.CanvasKc, playX, laneW, TopPad);
            if (n != null)
            {
                bool tailZone = false;
                if (arc3d)
                {
                    double z = A3ZOfT(n.End, _ed.ScrollMs, windowMs);
                    int col = _ed.ClampCol(n.Col);
                    bool sky = col < 2;
                    double lane = sky ? 0.5 : (col - 2) + 0.5;
                    double x3d = sky ? A3X3dFree(n.X) : A3X3dOf(lane);
                    double yTail = A3Proj(x3d, 0, z).y;
                    if (Math.Abs(e.Y - yTail) < 14) tailZone = true;
                }
                else
                {
                    double yHead = TopPad + (n.Time - _ed.ScrollMs) * _ed.PxPerMs;
                    double th = Math.Max(10, _ed.PxPerMs * _ed.SnapStepVal * 0.9);
                    if (_ed.IsLong(n))
                    {
                        double yTail = TopPad + (n.End - _ed.ScrollMs) * _ed.PxPerMs;
                        if (Math.Abs(e.Y - yTail) < 12) tailZone = true;
                    }
                    else if (e.Y > yHead + th / 2 - 2) tailZone = true;
                }
                _ed.SelNote = n;
                _ed.DragKind = tailZone ? 2 : 1;
                _ed.DragGrabT = _ed.NowTime - n.Time;
                return;
            }

            // arc 控制点 / 终点手柄 / 路径（Arcaea）
            if (arc3d)
            {
                var hh = HitArcHandle(e.X, e.Y, playX, laneW, _ed.PxPerMs, _ed.CanvasKc);
                if (hh.note != null)
                {
                    _ed.SelNote = hh.note;
                    if (hh.kind == 1) { _ed.PushUndo(); _ed.DragKind = 13; _ed.DragPointIndex = hh.idx; }
                    else if (hh.kind == 2) { _ed.PushUndo(); _ed.DragKind = 12; }
                    // kind 3：路径中间仅选中
                    _ed.DragGrabT = _ed.NowTime - hh.note.Time;
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    return;
                }
            }

            // 空白：按创建模式放置（Arcade-plus 式：Tap/Hold/Arc/ArcTap）
            if (!arc3d)
            {
                int col2 = colAt;
                if (col2 < 0 || col2 >= _ed.CanvasKc) return;
                _ed.PlaceNoteAt(0.5, 0.5, col2, t);
                base.OnMouseDown(e);
                return;
            }
            // —— Arcaea 3D 模式化创建 ——
            var up3 = A3Unproject(e.X, e.Y, _ed.ScrollMs, windowMs);
            double snapT = _ed.SnapTimeFor(Math.Max(0, up3.t));
            bool skyZone = ArcIsSkyZone(e.X, e.Y);
            switch (A3CreateMode)
            {
                case "arc":
                {
                    // 两点创建弧：第一次点击=起点(轨/时间/高度)，第二次=终点
                    if (!A3ArcStartPlaced)
                    {
                        A3ArcStartPlaced = true;
                        A3ArcStartT = snapT;
                        A3ArcStartLane = up3.lane;
                        A3ArcStartH = ArcHeightFromY(e.Y);
                        _ed.SelNote = null;
                        Invalidate();
                    }
                    else
                    {
                        _ed.PushUndo();
                        int sCol = Math.Max(0, Math.Min(5, (int)Math.Round(A3ArcStartLane) + 2));
                        int eCol = Math.Max(0, Math.Min(5, (int)Math.Round(up3.lane) + 2));
                        double eT = Math.Max(snapT, A3ArcStartT + _ed.SnapStepVal * 0.5);
                        var arc = _ed.PlaceNoteAt(0.5, 0.5, sCol, A3ArcStartT);
                        if (arc != null)
                        {
                            arc.Type = "arc";
                            arc.End = eT;
                            arc.EndCol = eCol == sCol ? -1 : eCol;
                            arc.Y = Math.Max(0, Math.Min(1, A3ArcStartH));
                            arc.EndY = Math.Max(0, Math.Min(1, ArcHeightFromY(e.Y)));
                            arc.Col = sCol;
                            arc.Decor = A3ArcVoid;
                            _ed.SelNote = arc;
                        }
                        A3ArcStartPlaced = false;
                        Invalidate();
                    }
                    base.OnMouseDown(e);
                    return;
                }
                case "arctap":
                {
                    // 天键(arc tap)：天空区或轨道区均可，按点击位置放自由 X 天键
                    _ed.PushUndo();
                    int sc = skyZone ? (e.X < A3GCx ? 0 : 1) : Math.Max(2, Math.Min(5, (int)Math.Round(up3.lane) + 2));
                    var nt = _ed.PlaceNoteAt(0.5, 0.5, sc, snapT);
                    if (nt != null)
                    {
                        nt.X = Math.Max(0, Math.Min(1, up3.lane / 4.0));
                        nt.Type = "tap";
                        if (skyZone) nt.Y = Math.Max(0, Math.Min(1, ArcHeightFromY(e.Y)));   // 天键悬浮高度
                        _ed.SelNote = nt;
                    }
                    Invalidate();
                    base.OnMouseDown(e);
                    return;
                }
                default:   // tap / hold
                {
                    _ed.PushUndo();
                    int sc = skyZone ? (e.X < A3GCx ? 0 : 1) : Math.Max(2, Math.Min(5, (int)Math.Round(up3.lane) + 2));
                    var nt = _ed.PlaceNoteAt(0.5, 0.5, sc, snapT);
                    if (nt != null)
                    {
                        nt.X = Math.Max(0, Math.Min(1, up3.lane / 4.0));
                        if (A3CreateMode == "hold")
                        {
                            nt.Type = "hold";
                            nt.End = snapT + _ed.BeatMsVal;
                            nt.EndCol = -1;
                        }
                        else if (A3CreateMode == "tap")
                        {
                            // QA-6：tap 模式强制 tap（防工具栏类型框污染成 hold/arc）
                            nt.Type = "tap";
                            nt.End = nt.Time;
                            nt.EndCol = -1;
                        }
                        if (skyZone) nt.Y = Math.Max(0, Math.Min(1, ArcHeightFromY(e.Y)));
                        _ed.SelNote = nt;
                    }
                    Invalidate();
                    base.OnMouseDown(e);
                    return;
                }
            }
        }

        /// <summary>3D 视图：鼠标位置 → 天键悬浮高度比例（0=地面 1=天空顶）。引擎化：侧视用射线与中轴平面交点高度（精确）。</summary>
        double ArcHeightFromXY(double x, double y)
        {
            if (A3View == 2 || A3View == 3)
            {
                // 侧视：射线与中轴平面(wx=0)交点即精确高度
                return A3UnprojectFull(x, y, _ed.ScrollMs, TimeWindowMs(ClientSize.Width)).h;
            }
            if (A3View == 1)
            {
                // 俯视：无高度信息 → 按深度给中间高度
                double z = A3MouseZ(x, y);
                return Math.Max(0, Math.Min(1, (z - A3ZA) / Math.Max(0.05, 1.0 - A3ZA)));
            }
            // 正前方：天线以上越高越接近天空顶（天线=判定点，其上为天空）
            var ant = A3Proj(A3X3dOf(0), 0, A3ZA);
            double aY = ant.y;
            if (y >= aY) return 0;
            return Math.Max(0, Math.Min(1, (aY - y) / Math.Max(1, aY - TopPad) * 1.05));
        }
        double ArcHeightFromY(double y) => ArcHeightFromXY(A3MouseX >= 0 ? A3MouseX : A3GCx, y);

        /// <summary>3D 视图：鼠标位置是否在天空区。引擎化统一语义：射线与场景交点深度在天线深度线(zAntenna)以远 = 天空区。
        /// （与游玩一致：天键判定点=天线深度线；四方位同一条世界线，几何一致）</summary>
        bool ArcIsSkyZone(double x, double y)
        {
            var up = A3UnprojectFull(x, y, _ed.ScrollMs, TimeWindowMs(ClientSize.Width));
            return up.z < A3ZA;   // 更远(深度更小)= 天空区
        }

        /// <summary>3D 编辑视图：鼠标 → 轨道列（天空区=天键 0/1，轨道区=地键 2-5）。
        /// 引擎化统一语义：深度在天线线以远 = 天空区（天键列按横向分左右）；否则 = 轨道区（列由射线交点横向反算）。</summary>
        int ArcColFromXY(double x, double y)
        {
            double windowMs = TimeWindowMs(ClientSize.Width);
            var up = A3UnprojectFull(x, y, _ed.ScrollMs, windowMs);
            if (up.z < A3ZA)
            {
                // 天空区：天键列（按屏幕横向中心分左右）
                return x < A3GCx ? 0 : 1;
            }
            int col = (int)Math.Round(up.lane) + 2;
            return Math.Max(2, Math.Min(5, col));
        }

        /// <summary>3D 编辑视图：命中音符（地键/天键按投影位置；arc 按路径）。</summary>
        Note Arc3DHitNote(double x, double y)
        {
            double windowMs = TimeWindowMs(ClientSize.Width);
            double scroll = _ed.ScrollMs;
            for (int i = _ed.Notes.Count - 1; i >= 0; i--)
            {
                var n = _ed.Notes[i];
                if (n == null) continue;
                if (n.Type == "arc")
                {
                    ArcGeometry(n, 0, 0, 0, 0, out var s, out var e, out var cs);
                    var all = new List<PointF> { s }; all.AddRange(cs); all.Add(e);
                    var samples = BezierScreen(all, 24);
                    for (int k = 0; k < samples.Count - 1; k++)
                        if (DistToSegment((float)x, (float)y, samples[k].X, samples[k].Y, samples[k + 1].X, samples[k + 1].Y) <= 12) return n;
                    continue;
                }
                int col = _ed.ClampCol(n.Col);
                bool sky = col < 2;
                double z = sky ? A3ZA * A3ZOfT(n.Time, scroll, windowMs) : A3ZOfT(n.Time, scroll, windowMs);
                if (z < 0.05 || z > 1.7) continue;
                double remain = n.Time - scroll;
                double h = sky ? A3SkyH * ChartEditorPanel.ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, remain / A3Ahead)) : 0;
                double lane = sky ? 0.5 : (col - 2) + 0.5;
                double x3d = sky ? A3X3dFree(n.X) : A3X3dOf(lane);
                var pr = A3Proj(x3d, h, z);
                double px = pr.x, py = pr.y;
                double r = Math.Max(16, A3GlaneW * 0.3 * z);
                if (Math.Abs(px - x) <= r && Math.Abs(py - y) <= r) return n;
            }
            return null;
        }

        void TracklessMouseDown(MouseEventArgs e)
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            var f = FieldRect(W, H);
            double windowMs = TimeWindowMs(W);

            if (e.Button == MouseButtons.Middle) { _ed.DragKind = 4; _ed.DownY = e.Y; return; }

            // 底部时间条：命中音符刻度=时间拖拽（⑤⑥ 侧边拖柄）；否则 seek（ADOFAIReal 网格编辑器无此条，跳过）
            if (e.Y >= H - TimeBarH && _ed.Mode != GameMode.AdofaiReal)
            {
                if (e.Button == MouseButtons.Left && _ed.IsTracklessMode)   // ADOFAIReal（Trackless 分发但无底部时间条刻度）不拦截
                {
                    var tickN = HitTimeTick(e.X, W, 6.0);
                    if (tickN != null)
                    {
                        _ed.SelNote = tickN;
                        _ed.PushUndo();
                        _ed.DragKind = 31;
                        _ed.DragGrabT = tickN.Time - XToTime(e.X, W);   // 保持抓取偏移
                        Invalidate();
                        return;
                    }
                }
                _ed.DragKind = 6;
                _ed.SeekDuringPlay(Math.Max(0, XToTime(e.X, W)));
                Invalidate();
                return;
            }

            // ADOFAI 网格编辑（t5 原版式：点 tile=选中+角度拖拽 · 点空格=追加/中间插入 · 右键=删除）
            if (_ed.Mode == GameMode.AdofaiReal && e.Button == MouseButtons.Left)
            {
                var gh = AdofaiGridHit(e.X, e.Y, W, H, out _, out _, out _, out _, out var gl, out var gc);
                if (gh.hit >= 0 && gh.hit < gl.Count)
                {
                    _ed.SelNote = gl[gh.hit];
                    _ed.SyncAdofaiAngleBoxPublic();
                    _ed.DragKind = 25;                       // 网格角度拖拽（GridDragIdx 记拖拽 tile 索引，供重排）
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    _ed.DragPointIndex = gh.hit;
                    Invalidate();
                    return;
                }
                if (gh.append || gh.insert >= 0)
                {
                    double newT;
                    if (gh.append) newT = gl[gl.Count - 1].Time + _ed.SnapStepVal;
                    else
                    {
                        int lo2 = Math.Max(0, gh.insert - 1), hi2 = Math.Min(gl.Count - 1, gh.insert);
                        newT = _ed.SnapTimeFor((gl[lo2].Time + gl[hi2].Time) / 2.0);
                    }
                    var nn = _ed.PlaceNoteAt(0.5, 0.5, 0, newT);
                    if (nn != null)
                    {
                        nn.Kind = ChartEditorPanel.NormalizeAdofaiAngle(gh.newKind);   // PlaceNoteAt 已 PushUndo（快照在之前）——勿重复
                        _ed.SelNote = nn;
                        _ed.SyncAdofaiAngleBoxPublic();
                        _ed.MarkDirty();
                    }
                    Invalidate();
                    return;
                }
                return;   // 网格点未命中：不产生任何放置（网格编辑器独占）
            }
            if (_ed.Mode == GameMode.AdofaiReal && e.Button == MouseButtons.Right)
            {
                var gh2 = AdofaiGridHit(e.X, e.Y, W, H, out _, out _, out _, out _, out var gl2, out _);
                if (gh2.hit >= 0 && gh2.hit < gl2.Count) _ed.DeleteNote(gl2[gh2.hit]);
                Invalidate();
                return;
            }
            if (_ed.Mode == GameMode.AdofaiReal) return;   // 其余 ADOFAI 鼠标交互不进入通用无轨处理

            if (e.Button == MouseButtons.Right)
            {
                // osu 控制点右键删除（头尾不可删，至少保留 2 点）
                if (_ed.Mode == GameMode.OsuStandard)
                {
                    var hs = HitOsuSlider(e.X, e.Y, f);
                    if (hs.note != null && hs.kind == 1 && hs.idx >= 1)
                    {
                        _ed.PushUndo();
                        _ed.DeleteOsuCtrlPoint(hs.note, hs.idx);
                        Invalidate();
                        return;
                    }
                }
                var n = HitTestFieldNote(e.X, e.Y, f, W);
                if (n != null) _ed.DeleteNote(n);
                else
                {
                    var ev = HitTestEventAt(e.X, W, windowMs);
                    if (ev != null) _ed.DeleteEvent(ev);
                    else
                    {
                        // 事件曲线关键帧右键：切换缓动（RPE 补齐 1）或删除
                        var cev = HitCurveKeyframe(e.X, e.Y, W);
                        if (cev != null)
                        {
                            var menu = new ContextMenuStrip();
                            foreach (var easeName in new[] { "Linear", "EaseIn", "EaseOut", "EaseInOut", "EaseInBack", "EaseOutBack", "EaseInBounce", "EaseOutBounce" })
                            {
                                var item = new ToolStripMenuItem(easeName + (cev.Value.ev.Ease == easeName ? " ✓" : ""));
                                var eName = easeName;
                                item.Click += (s, ev2) =>
                                {
                                    _ed.PushUndo();
                                    cev.Value.ev.Ease = eName;
                                    _ed.MarkDirty();
                                    Invalidate();
                                };
                                menu.Items.Add(item);
                            }
                            menu.Items.Add(new ToolStripSeparator());
                            var del = new ToolStripMenuItem("删除关键帧");
                            del.Click += (s, ev2) => { _ed.PushUndo(); _ed.DeleteEvent(cev.Value.ev); Invalidate(); };
                            menu.Items.Add(del);
                            menu.Show(this, e.Location);
                            return;
                        }
                    }
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // 事件柱（② 按玩法分类：全无轨玩法）：点击/拖拽键帧（P0 垂直几何）——Ctrl/Shift=多选切换；空白列区=框选；选中贝塞尔事件=手柄拖拽
            if (_ed.IsTracklessMode)
            {
                // ① 贝塞尔手柄命中（选中事件含 Bezier → 4 控制点，首尾锁）
                if (_ed.SelEvt != null && _ed.SelEvt.Bezier != null && _ed.SelEvt.Bezier.Length >= 8)
                {
                    var bh = HitBezierHandle(e.X, e.Y, W);
                    if (bh != null && bh.Value.hi >= 1 && bh.Value.hi <= 2)
                    {
                        _ed.PushUndo();
                        _ed.DragKind = 30;
                        _ed.DragPointIndex = bh.Value.hi;
                        _ed.DragEvt = _ed.SelEvt;
                        _ed.DragEvtChannel = bh.Value.k;
                        _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                        Invalidate();
                        return;
                    }
                }
                var cev = HitCurveKeyframe(e.X, e.Y, W);
                if (cev != null)
                {
                    var ev = cev.Value.ev;
                    bool multi = (ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0;
                    if (multi)
                    {
                        _ed.ToggleEvtSelect(ev);
                        _ed.DragKind = 0;
                        Invalidate();
                        return;
                    }
                    // 单击：点选（已在选中集内 → 保持集合作组拖锚；否则清空重新选中）
                    _ed.SelEvt = ev;
                    if (!_ed.SelectedEvts().Contains(ev)) { _ed.ClearEvtSelect(); _ed.AddEvtSelect(ev); }
                    _ed.PushUndo();
                    _ed.DragKind = 26;                       // 键帧拖拽
                    _ed.DragEvt = ev;
                    _ed.DragPointIndex = cev.Value.k;        // 通道索引（按该玩法事件类别列）
                    _ed.CurveHoverIdx = cev.Value.idx;
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    _ed.SeekDuringPlay(ev.Time);
                    Invalidate();
                    return;
                }
                // 空白列区：开始橡皮筋框选
                double colX0 = f.Right + 12;
                double colW = Math.Max(40, W - SidePad - colX0);
                if (e.X >= colX0 - 4 && e.X <= colX0 + colW + 6 && e.Y >= f.Y - 4 && e.Y <= f.Y + f.Height + 4)
                {
                    if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0) _ed.ClearEvtSelect();
                    _ed.DragKind = 29;
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    _ed.DragEndX = e.X; _ed.DragEndY = e.Y;
                    Invalidate();
                    return;
                }
            }

            // osu 滑条：双击路径插入控制点
            if (_ed.Mode == GameMode.OsuStandard && e.Clicks >= 2)
            {
                var ins = HitOsuSlider(e.X, e.Y, f);
                if (ins.note != null && ins.kind == 2)
                {
                    TracklessFieldAt(e.X, e.Y, f, W, out var ifx, out var ify, out var icol, out _);
                    if (icol >= 0)
                    {
                        _ed.PushUndo();
                        if (_ed.InsertOsuCtrlPoint(ins.note, ifx, ify)) _ed.SelNote = ins.note;
                        Invalidate();
                        return;
                    }
                }
            }

            // osu-master SelectionBox 手柄：已选中音符时，角=缩放、右上圆=旋转（需先计算包围盒）
            if (_ed.Mode == GameMode.OsuStandard && _ed.SelNote != null)
            {
                var sel2 = _ed.SelNote;
                float sr = Math.Max(6, f.Height * 0.08f);
                var (sbx, sby, sbw, sbh) = OsuSelBox(sel2, f, sr);
                // 旋转柄（右上）
                if (Math.Abs(e.X - (sbx + sbw)) < 9 && Math.Abs(e.Y - (sby - 14)) < 9)
                {
                    _ed.DragKind = 23;   // 旋转
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    _ed.DragGrabT = _ed.NowTime - sel2.Time;
                    return;
                }
                // 四角缩放柄
                foreach (var (hx, hy) in new[] { (sbx, sby), (sbx + sbw, sby), (sbx, sby + sbh), (sbx + sbw, sby + sbh) })
                {
                    if (Math.Abs(e.X - hx) < 10 && Math.Abs(e.Y - hy) < 10)
                    {
                        _ed.PushUndo();
                        _ed.DragKind = 24;   // 缩放
                        _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                        _ed.DragGrabT = _ed.NowTime - sel2.Time;
                        return;
                    }
                }
            }

            // 选中/拖动已有音符（头部）；Shift=加入/移出多选（C4）；Ctrl/Alt+拖=时间（⑤⑥）
            var hit = HitTestFieldNote(e.X, e.Y, f, W);
            if (hit != null)
            {
                if ((ModifierKeys & (Keys.Control | Keys.Alt)) != 0)
                {
                    // ⑤⑥：时间轴分量——Ctrl/Alt+拖 = 沿底部时间条改时间（全谱定位；缩放拉远=整曲概览）
                    _ed.SelNote = hit;
                    _ed.PushUndo();
                    _ed.DragKind = 31;
                    _ed.DragGrabT = hit.Time - XToTime(e.X, W);   // 保持抓取偏移（不跳变）
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    return;
                }
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    _ed.ToggleMultiSelect(hit);
                    base.OnMouseDown(e);
                    return;
                }
                _ed.SelNote = hit;
                // Phigros hold：拖尾部圆柄改时长（沿判定线法向）
                if (_ed.Mode == GameMode.Phigros && _ed.IsLong(hit))
                {
                    PhigrosNoteScreenPosAt(hit, f, _ed.Time, hit.End, out float tpx, out float tpy);
                    if (Math.Sqrt((e.X - tpx) * (e.X - tpx) + (e.Y - tpy) * (e.Y - tpy)) < 18)
                    {
                        _ed.PushUndo();
                        _ed.DragKind = 22;
                        return;
                    }
                }
                _ed.PushUndo();
                _ed.DragKind = 5;   // 移动空间位置
                _ed.DragGrabT = _ed.NowTime - hit.Time;
                _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                return;
            }

            // Phigros：拖动判定线 = 写 moveY 关键帧（Shift=rotate、Alt=moveX，PhiMaker 式；作用于当前编辑线）
            if (_ed.Mode == GameMode.Phigros)
            {
                PhigrosLineGeom(f, _ed.ActiveLine, out var la, out var lb, out _, out _, out _);
                if (DistToSeg(e.X, e.Y, la, lb) < 16)
                {
                    _ed.PushUndo();
                    _ed.DragKind = (ModifierKeys & Keys.Shift) != 0 ? 21 : (ModifierKeys & Keys.Alt) != 0 ? 27 : 20;
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    return;
                }
            }

            // osu 滑条：拖控制点 / 拖曲线中间区域找最近控制点
            if (_ed.Mode == GameMode.OsuStandard)
            {
                var hs = HitOsuSlider(e.X, e.Y, f);
                if (hs.note != null)
                {
                    _ed.SelNote = hs.note;
                    _ed.PushUndo();
                    _ed.DragKind = 9;
                    _ed.DragPointIndex = hs.idx;
                    _ed.DragGrabT = _ed.NowTime - hs.note.Time;
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    return;
                }
            }

            // 空白：放置音符
            TracklessFieldAt(e.X, e.Y, f, W, out var fx, out var fy, out var col, out var t);
            // Phigros 自由位置（Col=-1）不是"场外"；其余模式 col<0 表示环外/场外点击
            if (col < 0 && _ed.Mode != GameMode.Phigros) return;

            // osu 滑条：按住拖动画出直线滑条
            if (_ed.Mode == GameMode.OsuStandard && _ed.NoteType == "hold")
            {
                var nn = _ed.PlaceNoteAt(fx, fy, col, t);
                if (nn != null) { _ed.SelNote = nn; _ed.DragKind = 7; _ed.DragStartX = e.X; _ed.DragStartY = e.Y; }
                return;
            }
            _ed.PlaceNoteAt(fx, fy, col, t);
        }

        void SeekAt(double y)
        {
            double t;
            if (_ed.Mode == GameMode.Arcaea)
            {
                Arc3DSetup(ClientSize.Width, ClientSize.Height);
                t = Math.Max(0, A3Unproject(ClientSize.Width / 2.0, y, _ed.ScrollMs, TimeWindowMs(ClientSize.Width)).t);
            }
            else
                t = Math.Max(0, _ed.ScrollMs + (y - TopPad) / _ed.PxPerMs);
            _ed.SeekDuringPlay(t);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_ed.IsTracklessMode || _ed.Mode == GameMode.AdofaiReal)
            {
                try { TracklessMouseMove(e); } catch { }
                return;
            }

            A3MouseX = e.X; A3MouseY = e.Y;   // 记录鼠标位置（坐标显示）

            var (playX, laneW) = LayoutRect();
            bool arc3d = _ed.Mode == GameMode.Arcaea;
            double windowMs = TimeWindowMs(ClientSize.Width);
            if (arc3d) Arc3DSetup(ClientSize.Width, ClientSize.Height);
            switch (_ed.DragKind)
            {
                case 14:   // Arcaea 3D：中键移动摄像头（平移）
                {
                    double dx = e.X - _ed.DragStartX, dy = e.Y - _ed.DragStartY;
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    A3CamX += dx; A3CamY += dy;
                    Invalidate();
                    return;
                }
                case 4:   // 中键滚动
                {
                    double dy = _ed.DownY - e.Y;
                    _ed.ScrollMs = Math.Max(0, _ed.ScrollMs + dy / _ed.PxPerMs);
                    _ed.DownY = e.Y;
                    Invalidate();
                    return;
                }
                case 3:   // 标尺 seek
                    if (e.Button == MouseButtons.Left) SeekAt(e.Y);
                    return;
                case 1:   // 移动音符（保持抓取偏移）——时间拖拽 + 有轨横向改列（C4 补齐，Malody NoteArt 范式）
                case 2:   // 拉长条尾
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    double t = arc3d
                        ? Math.Max(0, A3Unproject(e.X, e.Y, _ed.ScrollMs, windowMs).t)
                        : Math.Max(0, _ed.ScrollMs + (e.Y - TopPad) / _ed.PxPerMs);
                    if (_ed.DragKind == 2) _ed.MoveNote(n, t, true);
                    else
                    {
                        _ed.MoveNote(n, t - _ed.DragGrabT, false);
                        // 有轨模式：横向拖拽改列（时间拖拽期间随 X 改列；Arcaea 3D 用 lane 语义，跳过）
                        if (!arc3d && _ed.IsTrackedMode && laneW > 1 && n.Col >= 0)
                        {
                            int newCol = (int)((e.X - playX) / laneW);
                            int kc = _ed.CanvasKc;
                            newCol = Math.Max(0, Math.Min(kc - 1, newCol));
                            if (newCol != n.Col)
                            {
                                n.Col = newCol;
                                if (n.EndCol >= 0) n.EndCol = newCol;   // 长条/arc 尾列联动
                                _ed.Invalidate();
                            }
                        }
                    }
                    _ed.Invalidate();
                    return;
                }
                case 12:   // arc 终点手柄 → EndCol + End（终点整体拖拽）
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    int col;
                    double t;
                    if (arc3d)
                    {
                        var up = A3Unproject(e.X, e.Y, _ed.ScrollMs, windowMs);
                        col = (int)Math.Round(up.lane) + 2;
                        t = _ed.SnapTimeFor(Math.Max(0, up.t));
                    }
                    else
                    {
                        col = (int)((e.X - playX) / laneW);
                        t = _ed.SnapTimeFor(Math.Max(0, _ed.ScrollMs + (e.Y - TopPad) / _ed.PxPerMs));
                    }
                    _ed.SetArcEndCol(n, col);
                    _ed.SetNoteEnd(n, Math.Max(n.Time + _ed.SnapStepVal * 0.5, t));
                    Invalidate();
                    return;
                }
                case 13:   // arc 控制点拖动
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    int kc = _ed.CanvasKc;
                    double colNorm, tFrac;
                    double dur = Math.Max(1, n.End - n.Time);
                    if (arc3d)
                    {
                        var up = A3Unproject(e.X, e.Y, _ed.ScrollMs, windowMs);
                        colNorm = up.lane / 4.0;   // lane 0..4 ↔ 归一 X 0..1（与 X3dFree 一致）
                        tFrac = (up.t - n.Time) / dur;
                    }
                    else
                    {
                        colNorm = (e.X - playX) / Math.Max(1, laneW * kc);
                        tFrac = (_ed.ScrollMs + (e.Y - TopPad) / _ed.PxPerMs - n.Time) / dur;
                    }
                    _ed.MoveArcCtrlPoint(n, _ed.DragPointIndex, colNorm, tFrac);
                    Invalidate();
                    return;
                }
            }
            // 悬停
            var hit = _ed.HitTestNote(e.X, e.Y, _ed.CanvasKc, playX, laneW, TopPad);
            _ed.HoverNote = hit;
            base.OnMouseMove(e);
        }

        void TracklessMouseMove(MouseEventArgs e)
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            var f = FieldRect(W, H);
            double windowMs = TimeWindowMs(W);
            switch (_ed.DragKind)
            {
                case 31:   // ⑤⑥：无轨时间拖拽（Ctrl/Alt+拖 = 沿底部时间条改时间；缩放拉远=全谱定位；hold 尾随 delta 联动）
                {
                    var n31 = _ed.SelNote;
                    if (n31 == null) return;
                    double t31 = Math.Max(0, XToTime(e.X, W) + _ed.DragGrabT);
                    _ed.MoveNote(n31, t31, false);   // 内部：SnapTime 吸附 + hold End 随 delta + osu 曲线校正
                    Invalidate();
                    return;
                }
                case 25:   // ADOFAI 网格角度拖拽（t5）：光标格心相对前一 tile 的方位角 = 该 tile 到达航向（15° 吸附）；
                            // Ctrl+拖 = 重排（拖到已有 tile 格 → 时间重排，Kind 保留）
                {
                    if (e.Button != MouseButtons.Left || _ed.SelNote == null) return;
                    var n25 = _ed.SelNote;
                    if (!AdofaiGridGeom(W, H, out var gp25, out double gcell25, out double gox25, out double goy25, out var gl25, out var gc25)) return;
                    int ti = Math.Max(0, Math.Min(gp25.Length - 1, _ed.DragPointIndex));
                    int gx2 = (int)Math.Round((e.X - gox25) / gcell25), gy2 = (int)Math.Round((e.Y - goy25) / gcell25);
                    // 重排：Ctrl+拖到已有 tile 格（j≠ti）
                    if ((ModifierKeys & Keys.Control) != 0)
                    {
                        int j = -1;
                        for (int k = 0; k < gp25.Length; k++)
                            if ((int)Math.Round((gp25[k].X - gox25) / gcell25) == gx2 && (int)Math.Round((gp25[k].Y - goy25) / gcell25) == gy2) { j = k; break; }
                        if (j >= 0 && j != ti) _ed.ReorderAdofaiNote(n25, ti, j);
                        Invalidate();
                        return;
                    }
                    // 角度：ti 的到达航向 = 光标格相对前一 tile 格（ti==0 用起点格）
                    double ppx25 = ti > 0 ? gp25[ti - 1].X : (float)gox25;
                    double ppy25 = ti > 0 ? gp25[ti - 1].Y : (float)goy25;
                    double deg = Math.Atan2((goy25 + gy2 * gcell25) - ppy25, (gox25 + gx2 * gcell25) - ppx25) * 180.0 / Math.PI;
                    deg = Math.Round(deg / 15.0) * 15.0;   // ADOFAI 砖块夹角 15° 整数倍（实机合法性约束）
                    double cumPrev = ti > 0 ? gc25[ti - 1] : 0;
                    double newKind = ChartEditorPanel.NormalizeAdofaiAngle(deg - cumPrev);
                    if (Math.Abs(newKind - n25.Kind) > 0.01)
                    {
                        _ed.PushUndo();
                        n25.Kind = ChartEditorPanel.NormalizeAdofaiAngle(newKind);
                        _ed.SyncAdofaiAngleBoxPublic();
                        _ed.MarkDirty();
                    }
                    Invalidate();
                    return;
                }
                case 26:   // 事件柱键帧拖拽（P0 垂直几何）：竖拖改时间（吸附拍），横拖改值；组拖=选中集整体同步
                {
                    if (e.Button != MouseButtons.Left) return;
                    var ev = _ed.DragEvt;
                    if (ev == null) return;
                    int k = _ed.DragPointIndex;                 // 通道索引（按该玩法事件类别列）
                    string[] kinds26 = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                    if (k < 0 || k >= kinds26.Length) return;
                    // —— 垂直柱几何（与绘制同源） ——
                    double colX0 = f.Right + 12;
                    double colW = Math.Max(40, W - SidePad - colX0);
                    double cw = (colW - 8) / kinds26.Length;
                    double cxx = colX0 + k * (cw + 2);
                    (double lo, double hi) = ChartEditorPanel.EventRangeForKind(kinds26[k]);
                    double TimeToY(double t) => f.Y + (t - _ed.ScrollMs) / Math.Max(1, windowMs) * f.Height;
                    // 新时间：拖拽中保持抓取点相对偏移，拍吸附
                    double newT = _ed.ScrollMs + ((e.Y - (_ed.DragStartY - TimeToY(ev.Time))) - f.Y) / Math.Max(1, f.Height) * windowMs;
                    double beatMs = _ed.BeatMsVal;
                    if (beatMs > 1) newT = Math.Round(newT / beatMs) * beatMs;
                    newT = Math.Max(0, newT);
                    // 新值：列内横向 = 值域映射
                    double v01 = Math.Max(0, Math.Min(1, (e.X - cxx) / Math.Max(1, cw)));
                    double newV = lo + v01 * (hi - lo);
                    bool changed = Math.Abs(newT - ev.Time) > 0.01 || Math.Abs(newV - ev.Value) > 0.001;
                    if (changed)
                    {
                        // RPE undo：一次拖动=一步——不在此处 PushUndo（MouseDown 已压一次快照）
                        double dt = newT - ev.Time;
                        double dv0 = newV - ev.Value;
                        // 组拖：选中集内全部事件同步移动（时间用同一 delta，值按各自通道域钳制）
                        var sel = _ed.SelectedEvts().ToList();
                        if (sel.Count > 1 && sel.Contains(ev))
                        {
                            foreach (var se in sel)
                            {
                                se.Time = Math.Max(0, se.Time + dt);
                                if (se.Type == ev.Type)
                                {
                                    // 各通道值域（按事件类型：rotate 度 / speed·noteSpeed 0.1..4 / bpm 30..300 / 其余 0..1）
                                    var (clo, chi) = ChartEditorPanel.EventRangeForKind(se.Type);
                                    se.Value = Math.Max(clo, Math.Min(chi, se.Value + dv0));
                                }
                            }
                        }
                        else
                        {
                            ev.Time = newT;
                            ev.Value = newV;
                        }
                        // 绑定组联动（T59 D2j）：同组同类型事件值随拖同步（时间不随动；选中集成员不重复施加）
                        if (ev.Group != 0)
                        {
                            var (go, gh) = ChartEditorPanel.EventRangeForKind(ev.Type);
                            foreach (var ge in _ed.EventList)
                                if (ge != null && ge != ev && (sel.Count <= 1 || !sel.Contains(ge))
                                    && ge.Group == ev.Group && ge.Type == ev.Type)
                                    ge.Value = Math.Max(go, Math.Min(gh, ge.Value + dv0));
                        }
                        _ed.MarkDirty();
                        _ed.Time = newT;
                    }
                    Invalidate();
                    return;
                }
                case 29:   // 事件柱框选（P0/D2d 复刻）：橡皮筋矩形，MouseUp 收口选中集
                {
                    if (e.Button != MouseButtons.Left) return;
                    _ed.DragEndX = e.X; _ed.DragEndY = e.Y;
                    Invalidate();
                    return;
                }
                case 30:   // 贝塞尔手柄拖拽（D2f 复刻）：x=时间比例 0..1、y=缓动输出经通道值域反解；首尾点锁定
                {
                    if (e.Button != MouseButtons.Left) return;
                    var ev = _ed.DragEvt;
                    if (ev == null || ev.Bezier == null || ev.Bezier.Length < 8) return;
                    int hi = _ed.DragPointIndex;
                    if (hi < 1 || hi > 2) return;
                    int k = _ed.DragEvtChannel;
                    string[] kinds30 = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                    if (k < 0 || k >= kinds30.Length) return;
                    double colX0 = f.Right + 12;
                    double colW = Math.Max(40, W - SidePad - colX0);
                    double cw = (colW - 8) / kinds30.Length;
                    double cxx = colX0 + k * (cw + 2);
                    // x = 时间比例（按事件时间窗）, y = 缓动输出比例（按 值域→0..1）
                    double dur = Math.Max(1, (double.IsNaN(ev.End) ? ev.Time : ev.End) - ev.Time);
                    double bx = Math.Max(0, Math.Min(1, (YToTimeLocal(e.Y, f, windowMs, _ed.ScrollMs) - ev.Time) / dur));
                    double by = Math.Max(0, Math.Min(1, (e.X - cxx) / Math.Max(1, cw)));
                    bool changed = Math.Abs(ev.Bezier[hi * 2] - bx) > 0.001 || Math.Abs(ev.Bezier[hi * 2 + 1] - by) > 0.001;
                    if (changed)
                    {
                        // RPE undo：一次拖动=一步——不在此处 PushUndo（MouseDown 已压一次快照）
                        ev.Bezier[hi * 2] = bx;
                        ev.Bezier[hi * 2 + 1] = by;
                        _ed.MarkDirty();
                    }
                    Invalidate();
                    return;
                }
                case 4:   // 中键滚动时间窗口
                {
                    double dy = _ed.DownY - e.Y;
                    _ed.ScrollMs = Math.Max(0, _ed.ScrollMs + dy / _ed.PxPerMs * 8);
                    _ed.DownY = e.Y;
                    Invalidate();
                    return;
                }
                case 6:   // 时间条 seek
                    if (e.Button == MouseButtons.Left)
                    {
                        _ed.SeekDuringPlay(Math.Max(0, XToTime(e.X, W)));
                        Invalidate();
                    }
                    return;
                case 7:   // osu 滑条拖拽画线
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    TracklessFieldAt(e.X, e.Y, f, W, out var fx, out var fy, out var col, out _);
                    if (col >= 0) _ed.SetOsuSliderEnd(n, fx, fy);
                    Invalidate();
                    return;
                }
                case 20:   // Phigros 判定线 moveY 关键帧（拖动线上下）
                {
                    var f2 = FieldRect(W, H);
                    SetLineEventAtPlayhead("moveY", Math.Max(0.02, Math.Min(0.98, (e.Y - f2.Y) / f2.Height)));
                    return;
                }
                case 27:   // Phigros 判定线 moveX 关键帧（Alt+拖动：横向拖动改 X）
                {
                    var f2 = FieldRect(W, H);
                    SetLineEventAtPlayhead("moveX", Math.Max(0.0, Math.Min(1.0, (e.X - f2.X) / f2.Width)));
                    return;
                }
                case 21:   // Phigros 判定线 rotate 关键帧（Shift+拖动：线心指向鼠标）
                {
                    var f2 = FieldRect(W, H);
                    PhigrosLineGeom(f2, _ed.ActiveLine, out _, out _, out double cX, out double cY, out _);
                    double deg = Math.Atan2(e.Y - cY, e.X - cX) * 180.0 / Math.PI;
                    SetLineEventAtPlayhead("rotate", Math.Round(deg, 1));
                    return;
                }
                case 22:   // Phigros hold 尾（沿判定线法向改时长；按音符所属线）
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    var f2 = FieldRect(W, H);
                    int ln = Math.Max(0, n.Line);
                    double moveY = EdEvalEvent("moveY", 0.5, ln);
                    double rotRad = EdEvalEvent("rotate", 0, ln) * Math.PI / 180.0;
                    double lineY = f2.Y + f2.Height * Math.Clamp(moveY, 0.02, 0.98);
                    double lineX = f2.X + f2.Width * EdEvalEvent("moveX", 0.5, ln);
                    double cosR = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
                    double dx = f2.X + n.X * f2.Width - lineX;
                    double lpX = lineX + dx * cosR, lpY = lineY + dx * sinR;   // 该列在线上的点
                    double yOff = (e.X - lpX) * (-sinR) + (e.Y - lpY) * cosR;  // 鼠标沿法向投影
                    double ppms = f2.Height * 0.0011;
                    // ② 拖尾改时长：目标虚拟位移 = -yOff/ppms，按 speed 场二分反解 end（无 speed 事件=恒速，与旧公式一致）
                    double want = Math.Max(0, -yOff) / ppms;
                    double loT = Math.Max(0, _ed.Time - 600000), hiT = _ed.Time;
                    for (int it = 0; it < 24; it++)
                    {
                        double mid = (loT + hiT) / 2.0;
                        if (SpdDispCached(ln, mid, _ed.Time) > want) loT = mid; else hiT = mid;
                    }
                    double end = Math.Max(n.Time + _ed.SnapStepVal * 0.5, _ed.SnapTimeFor(loT));
                    _ed.SetNoteEnd(n, _ed.SnapTimeFor(end));
                    Invalidate();
                    return;
                }
                case 5:   // 移动空间位置
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    TracklessFieldAt(e.X, e.Y, f, W, out var fx, out var fy, out var col, out _);
                    if (col >= 0 || _ed.Mode == GameMode.Phigros) _ed.MoveNoteField(n, fx, fy, col);
                    Invalidate();
                    return;
                }
                case 9:   // osu 滑条控制点拖动
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    TracklessFieldAt(e.X, e.Y, f, W, out var fx, out var fy, out var col, out _);
                    if (col >= 0) _ed.MoveOsuCtrlPoint(n, _ed.DragPointIndex, fx, fy);
                    Invalidate();
                    return;
                }
                case 23:   // osu 选择框旋转（osu-master SelectionBox：绕音符中心旋转整条滑条）
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    double ccx = f.X + n.X * f.Width, ccy = f.Y + n.Y * f.Height;
                    double a0 = Math.Atan2(e.Y - ccy, e.X - ccx);
                    double a1 = Math.Atan2(_ed.DragStartY - ccy, _ed.DragStartX - ccx);
                    double dAng = a0 - a1;
                    _ed.RotateOsuNote(n, dAng);
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    Invalidate();
                    return;
                }
                case 24:   // osu 选择框缩放（绕音符中心，角拖动等比缩放）
                {
                    var n = _ed.SelNote;
                    if (n == null) return;
                    double ccx = f.X + n.X * f.Width, ccy = f.Y + n.Y * f.Height;
                    double d0 = Math.Max(1, Math.Sqrt(Math.Pow(_ed.DragStartX - ccx, 2) + Math.Pow(_ed.DragStartY - ccy, 2)));
                    double d1 = Math.Max(1, Math.Sqrt(Math.Pow(e.X - ccx, 2) + Math.Pow(e.Y - ccy, 2)));
                    double k = d1 / d0;
                    _ed.ScaleOsuNote(n, k);
                    _ed.DragStartX = e.X; _ed.DragStartY = e.Y;
                    Invalidate();
                    return;
                }
            }
            // 悬停
            _ed.HoverNote = HitTestFieldNote(e.X, e.Y, f, W);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            // P0 事件框选收口：DragKind=29 → 按橡皮筋矩形收集命中键帧（区间事件按起点判定）
            if (_ed.DragKind == 29 && _ed.IsTracklessMode)
            {
                try
                {
                    int W = ClientSize.Width, H = ClientSize.Height;
                    var f = FieldRect(W, H);
                    double windowMs = TimeWindowMs(W);
                    double t0 = _ed.ScrollMs;
                    double xa = Math.Min(_ed.DragStartX, _ed.DragEndX), xb = Math.Max(_ed.DragStartX, _ed.DragEndX);
                    double ya = Math.Min(_ed.DragStartY, _ed.DragEndY), yb = Math.Max(_ed.DragStartY, _ed.DragEndY);
                    string[] kinds = ChartEditorPanel.EventKindsForMode(_ed.Mode);
                    double colX0 = f.Right + 12;
                    double colW = Math.Max(40, W - SidePad - colX0);
                    double cw = (colW - 8) / Math.Max(1, kinds.Length);
                    var evs = _ed.EventList.Where(q => q != null && (q.Line == _ed.ActiveLine || q.Line < 0)).ToList();
                    foreach (var ev in evs)
                    {
                        int k = Array.IndexOf(kinds, ev.Type);
                        if (k < 0) continue;
                        double cxx = colX0 + k * (cw + 2);
                        double y = f.Y + (ev.Time - t0) / Math.Max(1, windowMs) * f.Height;
                        // 键帧在框内（x 只需在列内，y 按时间判定）
                        if (e.X >= 0 && y >= ya - 9 && y <= yb + 9 && cxx + cw >= xa - 4 && cxx <= xb + 4)
                            _ed.AddEvtSelect(ev);
                    }
                }
                catch { }
            }
            if (_ed.DragKind != 0)
            {
                _ed.DragKind = 0;
                _ed.DragPointIndex = -1;
                _ed.CurveHoverIdx = -1;
                _ed.DragEvt = null;
                _ed.DragEvtChannel = -1;
                _ed.DownY = double.MaxValue;
            }
            base.OnMouseUp(e);
        }

        /// <summary>T58 D2i：RPE 快捷键音符放置——鼠标→场坐标+时间映射（无轨按 y 反解吸附横线；有轨按列宽量化列），放置后自动选中。</summary>
        Note PlaceNoteAtMouse(string type)
        {
            var p = PointToClient(Cursor.Position);
            int W = ClientSize.Width, H = ClientSize.Height;
            var f = FieldRect(W, H);
            double fx = Math.Max(0, Math.Min(1, (p.X - f.X) / Math.Max(1, f.Width)));
            double fy = Math.Max(0, Math.Min(1, (p.Y - f.Y) / Math.Max(1, f.Height)));
            double t;
            int col;
            if (_ed.IsTracklessMode)
            {
                t = _ed.ScrollMs + Math.Max(0, p.Y - f.Y) / Math.Max(1, f.Height) * TimeWindowMs(W);
                col = 0;
            }
            else
            {
                t = _ed.Time;
                double laneW = f.Width / Math.Max(1, _ed.CanvasKc > 0 ? _ed.CanvasKc : 4);
                col = (int)Math.Floor((p.X - f.X) / Math.Max(1, laneW));
            }
            var n = _ed.PlaceNoteAt(fx, fy, col, t, type);
            if (n != null)
            {
                _ed.SelNote = n;
                Invalidate();
            }
            return n;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // ===== 制谱 AI：打拍校准（🎯 自动校准偏移 → 非 WAV 打拍模式：边放音频边按 ≥8 次 / Esc 取消；经 _ed 面板接口） =====
            if (_ed.AiTapActive)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _ed.AiTapCancel();
                    e.Handled = true;
                    return;
                }
                if (!e.Alt && !e.Control && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu)
                {
                    _ed.AiTapRecord();
                    e.Handled = true;
                    return;
                }
            }
            if (_ed.Mode == GameMode.Arcaea && !e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.D1: A3View = 0; A3CamX = 0; A3CamY = 0; A3CamZ = 1; e.Handled = true; Invalidate(); return;
                    case Keys.D2: A3View = 1; A3CamX = 0; A3CamY = 0; A3CamZ = 1; e.Handled = true; Invalidate(); return;
                    case Keys.D3: A3View = 2; A3CamX = 0; A3CamY = 0; A3CamZ = 1; e.Handled = true; Invalidate(); return;
                    case Keys.D4: A3View = 3; A3CamX = 0; A3CamY = 0; A3CamZ = 1; e.Handled = true; Invalidate(); return;
                    // 创建模式（Arcade-plus 式）：T=tap H=hold A=arc S=arctap C=切换arc颜色 V=实/虚
                    case Keys.T: A3CreateMode = "tap"; A3ArcStartPlaced = false; e.Handled = true; Invalidate(); return;
                    case Keys.H: A3CreateMode = "hold"; A3ArcStartPlaced = false; e.Handled = true; Invalidate(); return;
                    case Keys.A: A3CreateMode = "arc"; A3ArcStartPlaced = false; e.Handled = true; Invalidate(); return;
                    case Keys.S: A3CreateMode = "arctap"; A3ArcStartPlaced = false; e.Handled = true; Invalidate(); return;
                    case Keys.C: A3ArcColorBlue = !A3ArcColorBlue; e.Handled = true; Invalidate(); return;
                    case Keys.V:
                        // C4：选中 arc 时切换其虚实（Decor）；未选中则改创建默认
                        if (_ed.SelNote != null && _ed.SelNote.Type == "arc")
                        {
                            _ed.PushUndo();
                            _ed.SelNote.Decor = !_ed.SelNote.Decor;
                            e.Handled = true; Invalidate(); return;
                        }
                        A3ArcVoid = !A3ArcVoid; e.Handled = true; Invalidate(); return;
                    case Keys.Escape: A3ArcStartPlaced = false; break;                }
            }
            switch (e.KeyCode)
            {
                case Keys.Space: _ed.TogglePlayPublic(); e.Handled = true; return;
                // ===== t5 三栏/预览快捷键：F5 自动游玩 · F6 AI 游玩 · F8 左栏 · F9 专注 · F10 右栏 =====
                case Keys.F5: _ed.PlayAutoplay(); e.Handled = true; return;
                case Keys.F6: _ed.PlayAi(); e.Handled = true; return;
                case Keys.F8: _ed.ToggleLeftPanel(); e.Handled = true; return;
                case Keys.F9: _ed.ToggleFocusPanels(); e.Handled = true; return;
                case Keys.F10: _ed.ToggleRightPanel(); e.Handled = true; return;
                // ===== T56 D2g / T58 D2i：RPE 快捷键组（运输 + 音符放置）=====
                case Keys.I:
                    // RPE 运输：I=播放
                    if (!_ed.Playing) _ed.TogglePlayPublic();
                    e.Handled = true; return;
                case Keys.O:
                    // O=停止播放+全局时间回播放起点（恢复）
                    _ed.PlayStop();
                    _ed.SeekDuringPlay(0);
                    _ed.EnsureTimeVisible(TimeWindowMs(ClientSize.Width));
                    Invalidate();
                    e.Handled = true; return;
                case Keys.P:
                    // P=停止不恢复
                    _ed.PlayStop();
                    e.Handled = true; return;
                case Keys.OemOpenBrackets:
                    // [ = 从头预览（回起点+播放）
                    _ed.SeekDuringPlay(0);
                    if (!_ed.Playing) _ed.TogglePlayPublic();
                    Invalidate();
                    e.Handled = true; return;
                case Keys.J:
                case Keys.K:
                case Keys.L:
                    // Ctrl+J/K/L = 倍速 1.0/0.75/0.5（预览速率）
                    if (e.Control)
                    {
                        _ed.PlayRate = e.KeyCode == Keys.J ? 1.0 : e.KeyCode == Keys.K ? 0.75 : 0.5;
                        Invalidate();
                        e.Handled = true; return;
                    }
                    break;
                case Keys.Q:
                case Keys.W:
                case Keys.E:
                case Keys.R:
                    // T58 D2i：Q/W/E/R 光标处放置 tap/drag/flick/hold
                    if (!e.Control && _ed.Mode == GameMode.Phigros)
                    {
                        PlaceNoteAtMouse(e.KeyCode == Keys.Q ? "tap" : e.KeyCode == Keys.W ? "drag" : e.KeyCode == Keys.E ? "flick" : "hold");
                        e.Handled = true; return;
                    }
                    break;
                case Keys.A:
                    // T58 D2i：A=反转 X（选中 Phigros 音符；Arcaea 模式 A 已用于创建 arc）
                    if (!e.Control && _ed.Mode == GameMode.Phigros && _ed.SelNote != null)
                    {
                        _ed.FlipSelectedX();
                        Invalidate();
                        e.Handled = true; return;
                    }
                    break;
                case Keys.N:
                    // T58 D2i：ALT+N = 音符/共同编辑视图切换（隐藏事件区）
                    if (e.Alt && _ed.Mode == GameMode.Phigros)
                    {
                        _ed.NotesOnlyMode = !_ed.NotesOnlyMode;
                        _ed.Invalidate(); Invalidate();
                        e.Handled = true; return;
                    }
                    break;
                case Keys.Delete:
                    // P0 事件级批删（框选/组拖选中集）优先：Phigros 事件多选
                    if (_ed.Mode == GameMode.Phigros && _ed.EvtSelCount > 0) { _ed.DeleteSelectedEvts(); e.Handled = true; return; }
                    // C4：多选时批量删除
                    if (_ed.SelectedNotes().Count() > 1) { _ed.DeleteSelected(); e.Handled = true; return; }
                    if (_ed.SelNote != null) { _ed.DeleteNote(_ed.SelNote); }
                    e.Handled = true; return;
                case Keys.Escape:
                    // P0：Esc 取消事件选择（高亮白环清除）→ 播放停止
                    if (_ed.Mode == GameMode.Phigros && _ed.EvtSelCount > 0) { _ed.ClearEvtSelect(); _ed.SelEvt = null; e.Handled = true; return; }
                    _ed.PlayStop();
                    e.Handled = true; return;
                case Keys.B:
                    // Phigros 拆线/绑线（phimakor）：B=在播放头拆线；Ctrl+B=并入上一线
                    if (_ed.Mode == GameMode.Phigros)
                    {
                        if (e.Control)
                        {
                            if (_ed.ActiveLine > 0) { _ed.BindLine(_ed.ActiveLine - 1, _ed.ActiveLine); _ed.SyncLineBox(); }
                        }
                        else if (_ed.SplitLineAt(_ed.Time) >= 0) { }
                        _ed.RefreshLineParentUI();
                        e.Handled = true; return;
                    }
                    break;
                case Keys.Z:
                    if (e.Control) { _ed.UndoRequest(); e.Handled = true; return; }
                    break;
                case Keys.Y:
                    if (e.Control) { _ed.RedoRequest(); e.Handled = true; return; }
                    break;
                case Keys.C:
                    if (e.Control && _ed.SelNote != null) { _ed.ClipboardNote = _ed.CloneNotes(_ed.SelNote); _ed.Invalidate(); e.Handled = true; return; }
                    break;
                case Keys.F:
                    // D2q：Ctrl+F 填充曲线起点锚
                    if (e.Control && _ed.Mode == GameMode.Phigros) { _ed.SetFillAnchorStart(); e.Handled = true; return; }
                    break;
                case Keys.G:
                    // D2q：Ctrl+G 填充曲线终点锚
                    if (e.Control && _ed.Mode == GameMode.Phigros) { _ed.SetFillAnchorEnd(); e.Handled = true; return; }
                    break;
                case Keys.V:
                    if (e.Control && _ed.ClipboardNote != null)
                    {
                        // 以播放头为基准偏移粘贴（复制时的相对时间保持）
                        var c = _ed.ClipboardNote;
                        double dt = _ed.Time - c.Time;
                        var p = _ed.CloneNotes(c);
                        p.Time = Math.Max(0, p.Time + dt);
                        if (p.End > p.Time) p.End = Math.Max(p.Time + _ed.SnapStepVal, p.End + dt);
                        _ed.PasteNote(p);
                        e.Handled = true; return;
                    }
                    break;
                case Keys.S:
                    if (e.Control) { _ed.SaveMil(null); e.Handled = true; return; }
                    // T58 D2i：S=翻转下落（Side Up↔Down，选中 Phigros 音符）
                    if (_ed.Mode == GameMode.Phigros && _ed.SelNote != null)
                    {
                        _ed.ToggleSelectedSide();
                        Invalidate();
                        e.Handled = true; return;
                    }
                    break;
                case Keys.Left:
                    if (_ed.SelectedNotes().Count() > 1) { _ed.NudgeSelectedMulti(-_ed.SnapStepVal); e.Handled = true; return; }
                    _ed.NudgeSelected(-_ed.SnapStepVal); e.Handled = true; return;
                case Keys.Right:
                    if (_ed.SelectedNotes().Count() > 1) { _ed.NudgeSelectedMulti(_ed.SnapStepVal); e.Handled = true; return; }
                    _ed.NudgeSelected(_ed.SnapStepVal); e.Handled = true; return;
                case Keys.Oemplus:
                case Keys.Add:
                    // C4：osu 滑条 Repeats +1（选中滑条时；QA-③：End 随往返次数联动——单程时长不变，总时长=单程×Repeats）
                    if (_ed.Mode == GameMode.OsuStandard && _ed.SelNote != null && _ed.IsLong(_ed.SelNote))
                    {
                        var sn = _ed.SelNote;
                        _ed.PushUndo();
                        double oneWay = (sn.End - sn.Time) / Math.Max(1, sn.Repeats);
                        sn.Repeats = sn.Repeats + 1;
                        sn.End = sn.Time + oneWay * sn.Repeats;
                        _ed.MarkDirty(); _ed.Invalidate(); e.Handled = true; return;
                    }
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    // C4：osu 滑条 Repeats -1（选中滑条时，下限 1；QA-③：End 随往返次数联动）
                    if (_ed.Mode == GameMode.OsuStandard && _ed.SelNote != null && _ed.IsLong(_ed.SelNote))
                    {
                        var sn2 = _ed.SelNote;
                        if (sn2.Repeats <= 1) break;
                        _ed.PushUndo();
                        double oneWay2 = (sn2.End - sn2.Time) / Math.Max(1, sn2.Repeats);
                        sn2.Repeats = sn2.Repeats - 1;
                        sn2.End = sn2.Time + oneWay2 * sn2.Repeats;
                        _ed.MarkDirty(); _ed.Invalidate(); e.Handled = true; return;
                    }
                    break;
            }
            base.OnKeyDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
            => keyData == Keys.Space || keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Delete || keyData == Keys.Escape
            || keyData == Keys.D1 || keyData == Keys.D2 || keyData == Keys.D3 || keyData == Keys.D4
            || keyData == Keys.T || keyData == Keys.H || keyData == Keys.A || keyData == Keys.S
            || keyData == Keys.C || keyData == Keys.V || keyData == Keys.B
            || keyData == Keys.I || keyData == Keys.O || keyData == Keys.P || keyData == Keys.OemOpenBrackets
            || keyData == Keys.Q || keyData == Keys.W || keyData == Keys.E || keyData == Keys.R || keyData == Keys.N
            || base.IsInputKey(keyData);
    }
}
