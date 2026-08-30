using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ChartPlayer
{
    /* ================= 引擎壳次级页（t49）：曲库/校准/段位/玩家信息/我的数据/皮肤/回放/关于 =================
       - 布局与 legacy 面板同款（DanSelectDialog/MainForm 各页），文案一律 UiText 逐字。
       - 行为接同一实现：曲库页委托 FolderPanel 纯逻辑类（t53 批2：同方法，公开包装）；校准页同 CalibrationForm 字段
         （GameSettings.Offset）并可打开原校准表单；段位页与 DanSelectDialog 同分组构建器（BuildGroups），
         启动经 SecondaryAction 直达 MainForm 原逻辑；其余数据页读同数据源（PlayerData/SkinSettings/ReplaySystem/UiText.AboutText）。
       - 本文件为 EngineMainShell 分部（partial）：主文件只增加 RegisterSecondaryPages() 一处调用与 partial 关键字。 */

    public sealed partial class EngineMainShell
    {
        /// <summary>次级页回调（MainForm 注入，键值语义）：
        /// "dan.start"(Chart) 段位启动 · "replay.play"(string 路径) 回放播放 · "player.edit" 打开设置存档分区 · "skin.edit" 打开皮肤设置。</summary>
        public Func<string, object, bool> SecondaryAction;

        /// <summary>公开页面跳转（供 MainForm 菜单按钮直达次级页，壳保持可见）；进入时刷新对应数据页（懒加载，避免外壳构造时全量扫描）。</summary>
        public void GoToPage(string key)
        {
            if (key == "folder") RefreshFolderView();
            else if (key == "dan") RefreshDanView();
            else if (key == "replay") RefreshReplayView();
            GoTo(key, TransitionStyle.Slide);
        }

        /* ---------------- 注册（BuildPages 调用） ---------------- */

        void RegisterSecondaryPages()
        {
            _pages["folder"] = BuildFolderPage();
            _pages["calibration"] = BuildCalibrationPage();
            _pages["dan"] = BuildDanPage();
            _pages["player"] = BuildPlayerPage();
            _pages["mydata"] = BuildMyDataPage();
            _pages["skin"] = BuildSkinPage();
            _pages["replay"] = BuildReplayPage();
            _pages["about"] = BuildAboutPage();
        }

        /* ---------------- 通用：页头（标题 + 返回） ---------------- */

        (UiPanel bg, UiLabel title) BeginPage(Scene scene, string canvasName, string titleText, double backY = 96)
        {
            var canvas = NewCanvas(scene, canvasName);
            var bg = canvas.AddChild<UiPanel>();
            bg.Width = 1280; bg.Height = 800; bg.Padding = 48; bg.Background = _theme.Bg; bg.CornerRadius = 0;
            var title = bg.AddChild<UiLabel>();
            title.Text = titleText; title.Color = _theme.TextPrimary; title.FontSize = _theme.FnH1;
            title.Width = 1184; title.Height = 48; title.X = 48; title.Y = 24; title.Align = UiAlign.Left;
            var back = bg.AddChild<UiButton>();
            back.Text = UiText.UiBack; back.Width = 110; back.Height = 34; back.X = 48; back.Y = backY;   // t21 P1-4/5：标题(底72)+24 间距；t24 ③：校准/我的数据 高倍缩放下再放宽 8（backY=104，逐页传入）
            back.CornerRadius = _theme.R2; back.Accent = false; back.FontSize = _theme.FnBody;
            back.Clicked += () => GoTo("menu", TransitionStyle.Fade);
            return (bg, title);
        }

        UiLabel AddLabel(UiPanel bg, string text, double x, double y, double w, double h, double fontSize, bool muted = false, UiAlign align = UiAlign.Left)
        {
            var l = bg.AddChild<UiLabel>();
            l.Text = text; l.Color = muted ? _theme.TextMuted : _theme.TextSecondary; l.FontSize = fontSize;
            l.Width = w; l.Height = h; l.X = x; l.Y = y; l.Align = align;
            return l;
        }

        UiButton AddPageButton(UiPanel bg, string text, double x, double y, double w, double h, bool accent = false)
        {
            var b = bg.AddChild<UiButton>();
            b.Text = text; b.Width = w; b.Height = h; b.X = x; b.Y = y;
            b.CornerRadius = _theme.R2; b.Accent = accent; b.FontSize = _theme.FnBody;
            return b;
        }

        /// <summary>清空布局子元素（UiElement 无 ClearChildren，逐项 Remove）。</summary>
        static void ClearKids(UiStackLayout list)
        {
            while (list != null && list.Children.Count > 0)
                list.Remove(list.Children[0]);
        }

        /* ---------------- ① 曲库管理（FolderPanel 同款布局：信息栏/筛选/列表/两行按钮） ---------------- */

        FolderPanel _folderLogic;
        UiStackLayout _folderList;
        UiLabel _folderPath, _folderStats, _folderHint, _folderListTitle;
        int _folderFilterIdx;
        int _folderSelIdx = -1;

        FolderPanel FolderLogic()
        {
            if (_folderLogic == null)
            {
                _folderLogic = new FolderPanel(AppConfig.Load());
                // t53 批2：FolderPanel 瘦身为纯逻辑类（无 WinForms 宿主）——UI 线程同步回调由引擎窗注入
                _folderLogic.UiSync = a => { try { if (IsHandleCreated) BeginInvoke(a); else a(); } catch { a(); } };
                _folderLogic.FolderChanged += f => { _folderPath.Text = UiText.FolderPathPrefix + f; RefreshFolderView(); InvalidateSongsScan(); };   // t39：切目录/重扫后选歌页同步刷新（同源）
            }
            return _folderLogic;
        }

        Scene BuildFolderPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "FolderPage", UiText.FolderTitle);
            // t19 P0-1：全页垂直流重构——标题(72 底) → 路径(146) → 统计(180/限高20) → 筛选两行(202/242) → 列表(322/裁剪) → 底部按钮双行(560/608)
            _folderPath = AddLabel(bg, "", 48, 146, 1184, 24, _theme.FnBody, true);   // t21：返回按钮 y=96..130 → 路径下移避让；t33：与返回 +12；t35：路径-统计间距再 +12（统计下移）
            _folderPath.AutoShrinkFont = true;   // t29 全文显示：超宽路径缩字号（下限 9）保全文，弃省略号
            _folderStats = AddLabel(bg, "", 48, 180, 1184, 20, _theme.FnBody, true);   // t35：路径→统计间距再 +12（146→180，顶部间距 34≥24）
            _folderStats.AutoShrinkFont = true;  // t29 全文显示：统计行缩字号保全文，弃省略号
            // t29 全文显示：筛选 10 钮改两行 5+5、按文本自适应宽（UiMeasure.ButtonWidth；无省略号、不裁切）
            var filters = new[] { UiText.FolderFilterAll, UiText.FolderFilterOsu, UiText.FolderFilterMc, UiText.FolderFilterSm, UiText.FolderFilterQua, UiText.FolderFilterMil, UiText.FolderFilterAff, UiText.FolderFilterTxt, UiText.FolderFilterJson, UiText.FolderFilterZip };
            for (int i = 0; i < filters.Length; i++)
            {
                int idx = i;
                int rowIdx = i / 5;
                double fw = UiMeasure.ButtonWidth(filters[i], 10, 12, 56);
                double fx = 48;
                for (int j = 0; j < i % 5; j++) fx += UiMeasure.ButtonWidth(filters[rowIdx * 5 + j], 10, 12, 56) + 6;
                var fb = new UiButton { Text = filters[i], Accent = false, CornerRadius = _theme.R2, FontSize = 10, Width = fw, Height = 34, X = fx, Y = 202 + rowIdx * 40 };   // t35：随统计 +12
                fb.Clicked += () => { _folderFilterIdx = idx; RefreshFolderView(); };
                bg.AddChild(fb);
            }
            _folderListTitle = AddLabel(bg, UiText.FolderFileListTitle, 48, 282, 1184, 24, _theme.FnTitle);   // t35：随筛选行2 +12
            _folderListTitle.AutoShrinkFont = true;   // t29 全文显示：列表标题缩字号保全文
            // 列表（行卡堆叠；ClipChildren=true 裁剪溢出——多文件时不再压到提示行/按钮）
            var listScroll = bg.AddChild<UiStackLayout>();
            listScroll.X = 48; listScroll.Y = 322; listScroll.Width = 1184; listScroll.Height = 200;   // t35：标题→列表首行间距再 +12（282→322，顶部间距 40≥24）
            listScroll.Orientation = UiOrientation.Vertical; listScroll.Spacing = 4;
            listScroll.ClipChildren = true;
            _folderList = listScroll;
            _folderHint = AddLabel(bg, UiText.FolderSelectHint, 48, 530, 1184, 22, 10, true);   // t35：随列表 +12
            // t29 全文显示：底部按钮组按文本 AutoSize 自适应宽（UiMeasure.ButtonWidth；无省略号）；行1 选择文件夹/导入/扫描解压/刷新/去选歌；行2 打开目录/删除/复制路径/主菜单
            double bx = 48;
            var row1 = AddPageButton(bg, UiText.FolderChooseFolder, bx, 560, UiMeasure.ButtonWidth(UiText.FolderChooseFolder, _theme.FnBody), 36); bx += row1.Width + 12; row1.Clicked += () => { FolderLogic().PickFolder(); };
            var row1b = AddPageButton(bg, UiText.FolderImportFiles, bx, 560, UiMeasure.ButtonWidth(UiText.FolderImportFiles, _theme.FnBody), 36); bx += row1b.Width + 12; row1b.Clicked += () => { FolderLogic().ImportFiles(); };
            var row1c = AddPageButton(bg, UiText.FolderScanZip, bx, 560, UiMeasure.ButtonWidth(UiText.FolderScanZip, _theme.FnBody), 36); bx += row1c.Width + 12; row1c.Clicked += () => { FolderLogic().ScanZip(); };
            var row1d = AddPageButton(bg, UiText.FolderRefresh, bx, 560, UiMeasure.ButtonWidth(UiText.FolderRefresh, _theme.FnBody), 36); bx += row1d.Width + 12; row1d.Clicked += () => { FolderLogic().RefreshData(); RefreshFolderView(); };
            var row1e = AddPageButton(bg, UiText.FolderGoSongs, bx, 560, UiMeasure.ButtonWidth(UiText.FolderGoSongs, _theme.FnBody), 36); row1e.Clicked += () => GoTo("songs", TransitionStyle.Slide);
            bx = 48;
            var row2 = AddPageButton(bg, UiText.FolderOpenDir, bx, 608, UiMeasure.ButtonWidth(UiText.FolderOpenDir, _theme.FnBody), 36); bx += row2.Width + 12; row2.Clicked += () => { FolderLogic().OpenDir(); };
            var row2b = AddPageButton(bg, UiText.FolderDelete, bx, 608, UiMeasure.ButtonWidth(UiText.FolderDelete, _theme.FnBody), 36); bx += row2b.Width + 12; row2b.Clicked += () => { var fp = FolderLogic(); fp.SelectedIndex = _folderSelIdx; fp.DeleteSelected(); RefreshFolderView(); };
            var row2c = AddPageButton(bg, UiText.FolderCopyPath, bx, 608, UiMeasure.ButtonWidth(UiText.FolderCopyPath, _theme.FnBody), 36); bx += row2c.Width + 12; row2c.Clicked += () => { var fp = FolderLogic(); fp.SelectedIndex = _folderSelIdx; fp.CopySelectedPath(); };
            var row2d = AddPageButton(bg, UiText.FolderGoHome, bx, 608, UiMeasure.ButtonWidth(UiText.FolderGoHome, _theme.FnBody), 36); row2d.Clicked += () => GoTo("menu", TransitionStyle.Fade);
            return scene;   // 数据懒加载：GoToPage("folder") 进入时刷新（避免外壳构造时扫描曲库）
        }

        void RefreshFolderView()
        {
            if (_folderList == null) return;
            var fp = FolderLogic();
            _folderPath.Text = UiText.FolderPathPrefix + (fp.PathText ?? "");
            _folderStats.Text = fp.StatsText ?? "";
            var items = fp.FilteredItems(_folderFilterIdx);
            _folderListTitle.Text = items.Count > 0 ? string.Format(UiText.FolderListTitleFormat, items.Count) : UiText.FolderFileListTitle;
            ClearKids(_folderList);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int idx = i;
                // t29 全文显示：文件名行改 卡片+子标签（AutoShrinkFont 缩字号保全文，下限 9；弃省略号）
                var card = new ClickableCard { Width = 1184, Height = 34, CornerRadius = _theme.R2 };
                card.Background = new RgbaColor(255, 255, 255, 12);
                card.HoverBg = new RgbaColor(255, 255, 255, 26);
                card.SelBg = new RgbaColor(255, 255, 255, 40);
                card.TopAccent = false; card.ShowTitle = false;
                var lab = new UiLabel(FolderTypeName(it.Ext) + "　" + it.Display, _theme.TextSecondary, _theme.FnBody) { Width = 1160, Height = 22 };
                lab.X = 12; lab.Y = 6; lab.Align = UiAlign.Left; lab.AutoShrinkFont = true;
                card.AddChild(lab);
                card.Clicked += () => { _folderSelIdx = idx; _folderHint.Text = UiText.FolderPathPrefixSel + it.Path; };
                _folderList.AddChild(card);
            }
            if (items.Count == 0) _folderHint.Text = UiText.FolderNoCharts;
        }

        static string FolderTypeName(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".osu": return UiText.FolderExtOsu;
                case ".mc": return UiText.FolderExtMalody;
                case ".sm": return UiText.FolderExtSm;
                case ".ssc": return UiText.FolderExtSsc;
                case ".qua": return UiText.FolderExtQuaver;
                case ".mil": return UiText.FolderExtMil;
                case ".aff": return UiText.FolderExtArc;
                case ".txt": return UiText.FolderExtCytus;
                case ".json": return UiText.FolderExtPhigros;
                default: return UiText.FolderExtContainer;
            }
        }

        /* ---------------- ② 校准（CalibrationForm 同字段：偏移 + 开始校准 + 应用） ---------------- */

        Scene BuildCalibrationPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "CalibrationPage", UiText.CalibTitle, 108);   // t26：标题-返回间距 +12（t21 基础 96→108；高倍缩放下 FnH1 34pt 标题与返回贴叠）
            AddLabel(bg, UiText.CalibInfo, 48, 148, 1184, 28, _theme.FnTitle);   // t24 ③：随返回下移 8
            var offsetBox = bg.AddChild<UiNumberBox>();
            offsetBox.Label = UiText.CalibOffsetLabel;
            offsetBox.LabelLeft = true;
            offsetBox.LabelWidth = 130;
            offsetBox.Value = GameSettings.Offset;
            offsetBox.Min = -300; offsetBox.Max = 300; offsetBox.Step = 1;
            offsetBox.X = 48; offsetBox.Y = 198; offsetBox.Width = 320; offsetBox.Height = 30;
            var startBtn = AddPageButton(bg, UiText.CalibStart, 48, 248, 160, 40, true);
            var applyBtn = AddPageButton(bg, UiText.CalibApply, 220, 248, 120, 40);
            var result = AddLabel(bg, "", 48, 308, 1184, 28, _theme.FnBody, true);
            startBtn.Clicked += () =>
            {
                // 同一实现：打开 legacy CalibrationForm（其内部打拍采集与应用逻辑不变）
                try
                {
                    using (var f = new CalibrationForm())
                    {
                        f.StartPosition = FormStartPosition.CenterParent;
                        f.ShowDialog(this);
                    }
                }
                catch (Exception ex) { Logger.Error("校准表单打开失败", ex); }
                offsetBox.SetValueSilent(GameSettings.Offset);   // 对话框内应用后回读同字段
            };
            applyBtn.Clicked += () =>
            {
                GameSettings.Offset = offsetBox.Value;           // 同字段（与 CalibrationForm 应用按钮一致）
                result.Text = string.Format(UiText.CalibAppliedFormat, GameSettings.Offset.ToString("0"));
            };
            return scene;
        }

        /* ---------------- ③ 段位挑战（DanSelectDialog 同布局：段位集/曲目/开始/取消） ---------------- */

        UiStackLayout _danSetList, _danSongList;
        List<DanSelectDialog.DanGroup> _danGroups = new List<DanSelectDialog.DanGroup>();
        int _danSetIdx = -1, _danSongIdx = -1;
        UiLabel _danEmpty;

        Scene BuildDanPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "DanPage", UiText.DanTitle);
            // P1-3（t5）：段位集/曲目标签 y=182→192（+10px 行距），列表随之 +10、高度 -10（尾 542 不变）
            AddLabel(bg, UiText.DanHead, 48, 140, 1184, 28, _theme.FnTitle);
            AddLabel(bg, UiText.DanSetLabel, 48, 192, 250, 26, _theme.FnBody);
            AddLabel(bg, UiText.DanSongLabel, 320, 192, 600, 26, _theme.FnBody);
            var sets = bg.AddChild<UiStackLayout>();
            sets.X = 48; sets.Y = 222; sets.Width = 250; sets.Height = 320;
            sets.Orientation = UiOrientation.Vertical; sets.Spacing = 4;
            _danSetList = sets;
            var songs = bg.AddChild<UiStackLayout>();
            songs.X = 320; songs.Y = 222; songs.Width = 900; songs.Height = 320;
            songs.Orientation = UiOrientation.Vertical; songs.Spacing = 4;
            _danSongList = songs;
            _danEmpty = AddLabel(bg, UiText.DanNoCharts, 48, 230, 1184, 28, _theme.FnBody, true);
            _danEmpty.Visible = false;
            var startBtn = AddPageButton(bg, UiText.DanStart, 48, 566, 160, 42, true);
            startBtn.Clicked += () =>
            {
                if (_danSetIdx < 0 || _danSetIdx >= _danGroups.Count || _danSongIdx < 0 || _danSongIdx >= _danGroups[_danSetIdx].Charts.Count)
                {
                    _danEmpty.Text = UiText.DanPickFirst;
                    _danEmpty.Visible = true;
                    return;
                }
                var c = _danGroups[_danSetIdx].Charts[_danSongIdx];
                bool handled = SecondaryAction?.Invoke("dan.start", c) ?? false;
                if (!handled) Logger.Warn("段位启动回调未注入（SecondaryAction[dan.start]）");
            };
            var cancelBtn = AddPageButton(bg, UiText.DanCancel, 220, 566, 110, 42);
            cancelBtn.Clicked += () => GoTo("menu", TransitionStyle.Fade);
            return scene;
        }

        void RefreshDanView()
        {
            if (_danSetList == null) return;
            _danGroups = DanSelectDialog.BuildGroups(AppConfig.Load().ChartsFolder);
            ClearKids(_danSetList);
            ClearKids(_danSongList);
            _danSetIdx = -1; _danSongIdx = -1;
            if (_danGroups.Count == 0)
            {
                _danEmpty.Text = UiText.DanNoCharts;
                _danEmpty.Visible = true;
                return;
            }
            _danEmpty.Visible = false;
            for (int i = 0; i < _danGroups.Count; i++)
            {
                var g = _danGroups[i];
                int idx = i;
                var b = new UiButton { Text = g.Name + "（" + g.Charts.Count + "）", Accent = false, CornerRadius = _theme.R2, FontSize = _theme.FnBody, Width = 250, Height = 34 };
                b.Clicked += () => { _danSetIdx = idx; _danSongIdx = -1; FillDanSongs(); };
                _danSetList.AddChild(b);
            }
            if (_danGroups.Count > 0) { _danSetIdx = 0; FillDanSongs(); }
        }

        void FillDanSongs()
        {
            ClearKids(_danSongList);
            _danSongIdx = -1;
            if (_danSetIdx < 0 || _danSetIdx >= _danGroups.Count) return;
            var charts = _danGroups[_danSetIdx].Charts;
            for (int j = 0; j < charts.Count; j++)
            {
                var c = charts[j];
                int idx = j;
                var b = new UiButton
                {
                    Text = (string.IsNullOrEmpty(c.DanName) ? "—" : c.DanName) + "   ·   " + c.Title + "   ·   " + c.KeyCount + "K",
                    Accent = false, CornerRadius = _theme.R2, FontSize = _theme.FnBody, Width = 900, Height = 34
                };
                b.Clicked += () => { _danSongIdx = idx; };
                _danSongList.AddChild(b);
            }
        }

        /* ---------------- ④ 玩家信息（只读数据页，与设置存档分区同源 PlayerData.LoadProfile/Load） ---------------- */

        Scene BuildPlayerPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "PlayerPage", UiText.PlayerTitle);
            double y = 150;
            var p = PlayerData.Load();
            var prof = PlayerData.LoadProfile();
            AddLabel(bg, string.Format(UiText.PlayerNameFormat, prof.Name), 48, y, 1184, 28, _theme.FnTitle); y += 46;   // t21 P1-4：名称行与统计行距放宽
            AddLabel(bg, string.Format(UiText.MenuStatsFormat, p.Stats.Plays, p.Stats.NotesHit, p.Stats.MaxAcc.ToString("0.00"), p.Stats.BestCombo), 48, y, 1184, 28, _theme.FnBody); y += 40;
            AddLabel(bg, string.Format(UiText.MyDataScoreFormat, p.Stats.MaxAcc.ToString("0.00"), p.Stats.MaxScore, p.Stats.BestCombo), 48, y, 1184, 28, _theme.FnBody); y += 60;
            // t21 P1-6：⚙ 拆为独立字形标签 + 按钮纯文本——彻底消除 emoji run 游离/空隙（t31 起推进宽已按真 advance，此拆分保留标签间距语义）
            var gear = AddLabel(bg, "⚙", 60, y + 8, 20, 24, _theme.FnBody, false);
            var openBtn = AddPageButton(bg, UiText.PlayerOpenSettings.Replace("⚙ ", ""), 84, y, 240, 40);   // 齿轮标签在 x=60..80，按钮 x=84 起；t29：8 字文案 240 宽足容，去掉省略号（全文）
            openBtn.Clicked += () => { bool handled = SecondaryAction?.Invoke("player.edit", null) ?? false; if (!handled) Logger.Warn("存档分区回调未注入（SecondaryAction[player.edit]）"); };
            return scene;
        }

        /* ---------------- ⑤ 我的数据（只读数据页，与 MainForm.ShowMyData 同数据源） ---------------- */

        Scene BuildMyDataPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "MyDataPage", UiText.MyDataTitle, 108);   // t26：标题-返回间距 +12（t21 基础 96→108）
            var p = PlayerData.Load();
            double y = 150;
            AddLabel(bg, string.Format(UiText.PlayerNameFormat, PlayerData.LoadProfile().Name), 48, y, 1184, 28, _theme.FnTitle); y += 48;   // t24 ④：名称-统计行距 40→48（试玩：贴叠 +8）
            AddLabel(bg, string.Format(UiText.MenuStatsFormat, p.Stats.Plays, p.Stats.NotesHit, p.Stats.MaxAcc.ToString("0.00"), p.Stats.BestCombo), 48, y, 1184, 28, _theme.FnBody); y += 40;
            // P1-2（t5）：最近成绩标题与首行行距 34→44（t1 诊断：9px 贴叠）；t24 ④：44→54（试玩：与首条记录重叠 +10）
            AddLabel(bg, UiText.MyDataRecentTitle, 48, y, 1184, 28, _theme.FnTitle); y += 54;
            if (p.History.Count == 0)
            {
                AddLabel(bg, UiText.MyDataEmpty, 48, y, 1184, 24, _theme.FnBody, true);
            }
            else
            {
                foreach (var h in p.History)
                {
                    AddLabel(bg, h.Date + " · " + h.Title + " · ACC " + h.Acc.ToString("0.00") + "% · " + h.Score, 48, y, 1184, 22, 10, true);
                    y += 26;
                    if (y > 700) break;
                }
            }
            return scene;
        }

        /* ---------------- ⑥ 皮肤（只读当前值，与 SkinSettings 同源） ---------------- */

        Scene BuildSkinPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "SkinPage", UiText.SkinTitle);
            var skin = SkinSettings.LoadWithPlayerFirst();
            double y = 150;
            AddLabel(bg, string.Format(UiText.SkinBgColorFormat, skin.BgColor.R, skin.BgColor.G, skin.BgColor.B), 48, y, 1184, 26, _theme.FnBody); y += 34;
            AddLabel(bg, string.Format(UiText.SkinHitLineFormat, skin.HitLineColor.ToArgb().ToString("X8"), skin.HitLineThickness, skin.HitLineStyle, skin.HitLineGlow), 48, y, 1184, 26, _theme.FnBody); y += 34;
            AddLabel(bg, string.Format(UiText.SkinSlantFormat, skin.Slant), 48, y, 1184, 26, _theme.FnBody); y += 34;
            AddLabel(bg, string.Format(UiText.SkinJudgeFontFormat, skin.JudgeFont), 48, y, 1184, 26, _theme.FnBody); y += 60;
            var openBtn = AddPageButton(bg, UiText.SkinOpenSettings, 48, y, 220, 40);
            openBtn.Clicked += () => { bool handled = SecondaryAction?.Invoke("skin.edit", null) ?? false; if (!handled) Logger.Warn("皮肤设置回调未注入（SecondaryAction[skin.edit]）"); };
            return scene;
        }

        /* ---------------- ⑦ 回放（ReplaySystem 列表 + 播放） ---------------- */

        UiStackLayout _replayList;

        Scene BuildReplayPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "ReplayPage", UiText.ReplayTitle);
            var list = bg.AddChild<UiStackLayout>();
            list.X = 48; list.Y = 150; list.Width = 1184; list.Height = 380;
            list.Orientation = UiOrientation.Vertical; list.Spacing = 4;
            _replayList = list;
            var playBtn = AddPageButton(bg, UiText.ReplayPlay, 48, 550, 160, 40, true);
            playBtn.Clicked += () =>
            {
                if (_replaySelPath == null) return;
                bool handled = SecondaryAction?.Invoke("replay.play", _replaySelPath) ?? false;
                if (!handled) Logger.Warn("回放播放回调未注入（SecondaryAction[replay.play]）");
            };
            var refreshBtn = AddPageButton(bg, UiText.FolderRefresh, 220, 550, 90, 40);
            refreshBtn.Clicked += () => RefreshReplayView();
            RefreshReplayView();
            return scene;
        }

        string _replaySelPath;

        void RefreshReplayView()
        {
            if (_replayList == null) return;
            ClearKids(_replayList);
            _replaySelPath = null;
            var files = ReplaySystem.List();
            if (files.Length == 0)
            {
                var l = _replayList.AddChild<UiLabel>();
                l.Text = UiText.ReplayEmpty; l.Color = _theme.TextMuted; l.FontSize = _theme.FnBody; l.Width = 1184; l.Height = 26;
                return;
            }
            foreach (var f in files)
            {
                string path = f;
                var b = new UiButton
                {
                    Text = Path.GetFileNameWithoutExtension(f),
                    Accent = false, CornerRadius = _theme.R2, FontSize = _theme.FnBody, Width = 1184, Height = 34
                };
                b.Clicked += () => { _replaySelPath = path; };
                _replayList.AddChild(b);
            }
        }

        /* ---------------- ⑧ 关于（只读，UiText.AboutText 同源逐字） ---------------- */

        Scene BuildAboutPage()
        {
            var scene = new Scene();
            var (bg, _) = BeginPage(scene, "AboutPage", UiText.AboutCaption);
            double y = 150;
            foreach (var line in UiText.AboutText.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                AddLabel(bg, line, 48, y, 1184, 26, 12);
                y += 32;
            }
            AddLabel(bg, UiText.AboutContent, 48, y + 10, 1184, 22, 10, true);
            var closeBtn = AddPageButton(bg, UiText.UiBack, 48, y + 46, 110, 36);
            closeBtn.Clicked += () => GoTo("menu", TransitionStyle.Fade);
            return scene;
        }
    }
}
