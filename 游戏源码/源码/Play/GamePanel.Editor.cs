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
    public partial class GamePanel
    {
        void BuildModeEditExtras()
        {
            if (_editModeCombo != null)
                return;
            _editModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Top,
                Height = Ui.P(30),
                BackColor = UiColors.InputBg,
                ForeColor = UiColors.Fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 11F)
            };
            foreach (var mi in ModeSystem.Available)
                _editModeCombo.Items.Add(mi.Display);
            _editModeCombo.SelectedIndexChanged += (s, e2) =>
            {
                if (_updatingEdit)
                    return;
                int idx = _editModeCombo.SelectedIndex;
                var list = new List<ModeSystem.ModeInfo>(ModeSystem.Available);
                if (idx >= 0 && idx < list.Count)
                {
                    _kc = list[idx].Keys;
                    _chart = null;
                    _blankLayout = true;
                    LCRebuild();
                    ResetState();
                    Invalidate();
                }
            };
            _editPanel.Controls.Add(_editModeCombo);
        }


        void ResetCurrentModeLayout()
        {
            var id = ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania);
            if (Skin.ModeLayout != null && Skin.ModeLayout.Remove(id))
                LCRebuild();
            SyncEditValues();
            Invalidate();
        }


        void ResetCurrentElement()
        {
            var id = ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania);
            string key = null;
            if (_editSel != null && _editSel.SelectedIndex >= 0 && _editSel.SelectedIndex < EditKeys.Length)
                key = EditKeys[_editSel.SelectedIndex];
            if (key == null)
                return;
            if (Skin.ModeLayout != null && Skin.ModeLayout.TryGetValue(id, out var d) && d != null && d.Remove(key))
                LCRebuild();
            else if (key == "hitline")
            {
                Skin.Layout.Remove("hitline");
            }
            else if (Skin.Layout.Remove(key))
            {
            }

            LoadEditValues();
            SyncEditValues();
            Invalidate();
        }


        public void EnterLayoutEdit()
        {
            _blankLayout = _chart == null;
            if (_blankLayout)
                _kc = 4;
            EditLayoutMode = true;
            BuildModeEditExtras();
            if (_blankLayout && _editModeCombo != null && _updatingEdit == false && _editModeCombo.SelectedIndex < 0)
                _editModeCombo.SelectedIndex = 0;
            _playing = false;
            _paused = false;
            _audio.Pause();
            _dragKey = null;
            _dragHitline = false;
            if (_editPanel != null)
            {
                _editPanel.Visible = true;
                _editPanel.BringToFront();
                LoadEditValues();
            }

            Focus();
            Invalidate();
        }


        public void ExitLayoutEdit()
        {
            EditLayoutMode = false;
            _blankLayout = false;
            _dragKey = null;
            _dragHitline = false;
            if (_editPanel != null)
                _editPanel.Visible = false;
            Skin.Save();
            Invalidate();
            ExitToMenu?.Invoke();
        }


        void ResetState()
        {
            _notes.RemoveAll(n => n == null || double.IsNaN(n.Time) || double.IsInfinity(n.Time) || double.IsNaN(n.End) || double.IsInfinity(n.End));
            foreach (var n in _notes)
            {
                if (n.Time < 0)
                    n.Time = 0;
                if (n.End < n.Time)
                    n.End = n.Time;
                if (_chart == null || _chart.Mode != GameMode.Maimai)
                    n.Col = Math.Max(0, Math.Min(_kc - 1, n.Col));
            }

            PrepareModeNotes();
            _notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (var n in _notes)
            {
                n.Judged = false;
                n.Held = false;
                n.Completed = false;
                n.Judgment = null;
                n.Dev = 0;
                n.AiPlanned = false;
                n.AiMiss = false;
                n.AiDev = 0;
                n.AiTime = 0;
            }

            _eng.Reset();
            _eng.MissCountsInAcc = true;
            _eng.TotalNotes = _notes.Count;
            _autoIdx = 0;
            _lastJudge = "";
            _lastDev = "";
            _lastMpReport = -1;
            _record = new List<ReplayEvent>();
            _kpsPresses.Clear();
            _kpsHistory.Clear();
            _allDev.Clear();
            _kps = 0;
            _lastKpsSample = 0;
            _keyCol.Clear();
            var keys = GameSettings.GetKeys(_kc);
            for (int i = 0; i < keys.Length; i++)
                _keyCol[keys[i]] = i;
            if (_chart != null && (_chart.Mode == GameMode.Adofai || _chart.Mode == GameMode.AdofaiReal))
            {
                _keyCol.Clear();
                _keyCol[Keys.Space] = 0;
                _keyCol[Keys.D] = 0;
            }

            _ended = false;
            _paused = false;
            _missIdx = 0;
            _holdIdx = 0;
            _humanTest = false;
            _humanIdx = 0;
            _humanPlan.Clear();
            _heldNotes.Clear();
            _mouseHeld.Clear();
            _holdNotes.Clear();
            _osuTickCache.Clear();
            _arcRecollection = 100;
            _arcRecoLastMs = 0;
            _adofaiDerails = 0;
            foreach (var n in _notes)
                if (IsHoldNote(n))
                    _holdNotes.Add(n);
            for (int i = 0; i < 16; i++)
            {
                _burst[i] = 0;
                _pressFlash[i] = 0;
            }

            _shakeAmt = 0;
            _judgePop = 0;
            _comboPop = 0;
            _resultPhase = false;
            _result = null;
            _resultStart = 0;
            _particles.Clear();
            _rings.Clear();
            _toast = "";
            _toastUntil = 0;
            _firstNoteTime = _notes.Count > 0 ? _notes[0].Time : double.MaxValue;
            _canSkip = _notes.Count > 0 && _firstNoteTime >= 3000;
            _skipped = false;
            _skipTarget = 0;
            _skipOffset = 0;
            _osuCursorX = _osuCursorY = 0;
            _osuMouseDown = false;
            _osuDownKeys.Clear();
            _osuSpinner = null;
            _osuSpinProgress = 0;
            _phigLineCount = 0;
            ResetLoopComposer();
            RebuildSpdFieldCache();
        }


        void RebuildSpdFieldCache()
        {
            _spdFieldCache.Clear();
            if (_chart == null || _chart.Events == null || _chart.Events.Count == 0)
            {
                RebuildEventIndex();
                return;
            }

            _chart.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
            RebuildEventIndex();
            int lineCount = 1;
            foreach (var ev in _chart.Events)
                if (ev.Line >= lineCount)
                    lineCount = ev.Line + 1;
            lineCount = Math.Max(1, Math.Min(64, lineCount));
            double endT = _chart.EndTime > 0 ? _chart.EndTime : 600000;
            for (int line = 0; line < lineCount; line++)
            {
                var ks = BuildSpeedKeyframes(line, 0, endT);
                if (ks.Count == 0)
                    continue;
                var pref = new double[ks.Count];
                double acc = 0;
                for (int i = 1; i < ks.Count; i++)
                {
                    double span = ks[i].t - ks[i - 1].t;
                    if (span > 1e-9)
                        acc += (ks[i - 1].v + ks[i].v) * span / 2.0;
                    pref[i] = acc;
                }

                _spdFieldCache[line] = (ks, pref);
            }
        }


        double SpdDispCached(int line, double t0, double t1)
        {
            if (t1 <= t0)
                return 0;
            if (!_spdFieldCache.TryGetValue(line, out var f))
                return t1 - t0;
            var ks = f.ks;
            var pref = f.pref;
            if (ks.Count < 2)
                return t1 - t0;
            double F(double t)
            {
                if (t <= ks[0].t)
                    return 0;
                int lo = 0, hi = ks.Count - 1;
                while (lo + 1 < hi)
                {
                    int m = (lo + hi) >> 1;
                    if (ks[m].t <= t)
                        lo = m;
                    else
                        hi = m;
                }

                double a = ks[lo].t, b = ks[lo + 1].t;
                double baseV = pref[lo];
                if (t >= b)
                    return baseV + (ks[lo].v + ks[lo + 1].v) * (b - a) / 2.0;
                double p = (t - a) / (b - a);
                double vt = ks[lo].v + (ks[lo + 1].v - ks[lo].v) * p;
                return baseV + (ks[lo].v + vt) * (t - a) / 2.0;
            }

            return F(t1) - F(t0);
        }


        double RawMs() => _audio.HasMedia ? _audio.PositionMs : _sw.Elapsed.TotalMilliseconds + _skipOffset;

        void SeekTo(double t)
        {
            t = Math.Max(0, t);
            double now = RawMs();
            double delta = t - now;
            if (_audio.HasMedia)
            {
                try
                {
                    _audio.Seek(t);
                }
                catch
                {
                    _skipOffset += delta;
                }
            }
            else
                _skipOffset += delta;
            Invalidate();
        }


        public bool SkipIntro()
        {
            if (!_canSkip || _skipped || _chart == null)
                return false;
            double now = RawMs();
            if (now >= _firstNoteTime - 1800)
                return false;
            _skipped = true;
            _skipTarget = Math.Max(0, _firstNoteTime - 1500);
            double delta = _skipTarget - now;
            if (_audio.HasMedia)
            {
                try
                {
                    _audio.Seek(_skipTarget);
                }
                catch
                {
                    _skipOffset += delta;
                }
            }
            else
                _skipOffset += delta;
            Logger.Info("跳过开头空白：" + (now / 1000.0).ToString("0.0") + "s → " + (_skipTarget / 1000.0).ToString("0.0") + "s");
            Invalidate();
            return true;
        }


        Image FindBackgroundImage(string baseDir)
        {
            try
            {
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                    return null;
                var exts = new[]
                {
                    ".jpg",
                    ".jpeg",
                    ".png",
                    ".webp",
                    ".bmp"
                };
                string bn = Path.GetFileNameWithoutExtension(_chart?.AudioFile ?? "");
                var names = new List<string>();
                if (!string.IsNullOrEmpty(bn))
                    foreach (var e2 in exts)
                        names.Add(Path.Combine(baseDir, bn + e2));
                foreach (var e2 in exts)
                {
                    names.Add(Path.Combine(baseDir, "background" + e2));
                    names.Add(Path.Combine(baseDir, "bg" + e2));
                    names.Add(Path.Combine(baseDir, "cover" + e2));
                }

                foreach (var p in names)
                    if (File.Exists(p))
                    {
                        try
                        {
                            using var src = new Bitmap(p);
                            if (src.Width >= 32 && src.Height >= 32)
                                return new Bitmap(src);
                        }
                        catch
                        {
                        }
                    }

                foreach (var f in Directory.GetFiles(baseDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    var e3 = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(exts, e3) < 0)
                        continue;
                    if (Path.GetFileName(f).StartsWith("skin", StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        using var src = new Bitmap(f);
                        if (src.Width >= 64 && src.Height >= 64)
                            return new Bitmap(src);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return null;
        }


        void LoadBackgroundArt(string baseDir)
        {
            _bgImage?.Dispose();
            _bgImage = null;
            _bgD2D?.Dispose();
            _bgD2D = null;
            try
            {
                var img = FindBackgroundImage(baseDir);
                if (img == null)
                    return;
                _bgImage = img;
                if (_d2d != null)
                    _bgD2D = _d2d.CreateBitmap(new Bitmap(img));
            }
            catch
            {
            }
        }


        void Step()
        {
            double now = RawMs() + GameSettings.Offset;
            MultiStageStep(now);
            double mono = MonoMs();
            double stepDt = _lastStepMono > 0 ? Math.Min(100, mono - _lastStepMono) / 1000.0 : 0;
            _lastStepMono = mono;
            while (_kpsPresses.Count > 0 && _kpsPresses[0] < mono - 1000)
                _kpsPresses.RemoveAt(0);
            _kps = _kpsPresses.Count;
            if (mono - _lastKpsSample > 100)
            {
                _lastKpsSample = mono;
                _kpsHistory.Add((mono, _kps));
                if (_kpsHistory.Count > 180)
                    _kpsHistory.RemoveAt(0);
            }

            for (int c = 0; c < 16; c++)
            {
                if (_pressFlash[c] > 0)
                    _pressFlash[c] = Math.Max(0, _pressFlash[c] - 0.18f);
                if (_burst[c] > 0)
                    _burst[c] = Math.Max(0, _burst[c] - 0.12f);
            }

            if (_shakeAmt > 0)
                _shakeAmt = Math.Max(0, _shakeAmt - 0.10);
            if (_judgePop > 0)
                _judgePop = Math.Max(0, _judgePop - 0.14f);
            if (_comboPop > 0)
                _comboPop = Math.Max(0, _comboPop - 0.12f);
            if (DanActive && JudgeSettings.DanHpEnabled && _playing && !_paused && !_ended && stepDt > 0)
            {
                _eng.Drain(stepDt, _heldNotes.Count);
                if (_eng.Dead)
                    Finish();
            }

            if (DemoAi != null && _playing && !_paused)
            {
                while (DemoAi.HitTimes.Count > 0 && DemoAi.HitTimes[0] < now - 1000)
                    DemoAi.HitTimes.RemoveAt(0);
                AiEngine.StaminaTick(DemoAi, DemoAi.HitTimes.Count, now);
                AiEngine.TensionTick(DemoAi, DemoAi.HitTimes.Count, now);
                AiStep(DemoAi, now, true);
            }
            else if (GameSettings.Autoplay && _playing && !_paused)
            {
                while (_autoIdx < _notes.Count && _notes[_autoIdx].Time <= now)
                {
                    Hit(_notes[_autoIdx], _notes[_autoIdx].Time);
                    _autoIdx++;
                }
            }
            else if (_humanTest && _playing && !_paused)
            {
                while (_humanIdx < _humanPlan.Count && _humanPlan[_humanIdx].T <= now)
                {
                    var hp = _humanPlan[_humanIdx++];
                    if (hp.Miss)
                        continue;
                    if (hp.Mouse)
                    {
                        if (hp.Down)
                            TapAt(hp.Pt, now);
                        else
                            MouseReleaseHeld(hp.Pt, now);
                    }
                    else if (hp.Spin)
                    {
                        if (hp.Down)
                            HandleDown(hp.Key, now);
                        else
                            HandleUp(hp.Key, now);
                    }
                    else if (hp.Down)
                        HandleDownAt(hp.Key, hp.Col, now);
                    else
                        HandleUpAt(hp.Key, hp.Col, now);
                }
            }

            if (Replaying && _replayEvents != null && _playing && !_paused)
            {
                double rt = RawMs() + GameSettings.Offset - _replayStart;
                while (_replayIdx < _replayEvents.Count && _replayEvents[_replayIdx].T <= rt)
                {
                    var ev = _replayEvents[_replayIdx++];
                    if (TryCode(ev.Code, out var key, out var col))
                    {
                        if (ev.Down)
                            HandleDownAt(key, col, _replayStart + ev.T);
                        else
                            HandleUpAt(key, col, _replayStart + ev.T);
                    }
                }
            }

            if (_ais.Count > 0 && _playing && !_paused)
                foreach (var a in _ais)
                    if (a.Active)
                    {
                        while (a.HitTimes.Count > 0 && a.HitTimes[0] < now - 1000)
                            a.HitTimes.RemoveAt(0);
                        AiEngine.StaminaTick(a, a.HitTimes.Count, now);
                        AiEngine.TensionTick(a, a.HitTimes.Count, now);
                        AiStep(a, now, false);
                    }

            if ((GameSettings.Autoplay || DemoAi != null) && _playing && !_paused && _heldNotes.Count > 0)
                for (int i = _heldNotes.Count - 1; i >= 0; i--)
                {
                    var hn = _heldNotes[i];
                    if (hn.End <= now)
                    {
                        _heldNotes.RemoveAt(i);
                        hn.Held = false;
                        hn.Completed = true;
                        _eng.HoldComplete();
                    }
                }

            for (int i = _mouseHeld.Count - 1; i >= 0; i--)
            {
                var hn = _mouseHeld[i];
                if (hn.End <= now)
                {
                    _mouseHeld.RemoveAt(i);
                    int k = _heldNotes.IndexOf(hn);
                    if (k >= 0)
                    {
                        _heldNotes.RemoveAt(k);
                        hn.Held = false;
                        hn.Completed = true;
                        if (_chart != null && _chart.Mode == GameMode.OsuStandard)
                        {
                            var ot = OsuSliderTicks(hn);
                            int last = ot.Length - 1;
                            if (last > 0 && !double.IsNaN(ot[last]))
                            {
                                ot[last] = double.NaN;
                                _eng.ApplyTick();
                            }

                            double comp = OsuSliderCompleteness(hn);
                            if (comp < 0.999)
                                _eng.AdjustScore(-50 * (1 - comp));
                        }

                        _eng.HoldComplete();
                    }
                }
            }

            StepNewModes(now, stepDt);
            if (_chart != null && _chart.Mode == GameMode.Arcaea && _playing && !_paused && _arcRecollection < 30)
            {
                if (_arcRecoLastMs <= 0)
                    _arcRecoLastMs = MonoMs();
                while (MonoMs() - _arcRecoLastMs >= 700)
                {
                    _arcRecoLastMs += 700;
                    _arcRecollection = Math.Max(0, _arcRecollection - 0.5);
                }

                if (_arcRecollection <= 0 && DanActive && JudgeSettings.DanHpEnabled)
                    Finish();
            }

            while (_missIdx < _notes.Count && now - _notes[_missIdx].Time > JudgeSettings.MissWindow)
            {
                var n = _notes[_missIdx];
                if (!n.Judged)
                {
                    if (IsOsuSpinner(n) && n.Held && now < n.End)
                    {
                        _missIdx++;
                        continue;
                    }

                    if (_chart != null && _chart.Mode == GameMode.Phigros && n.Type == "drag")
                    {
                        n.Judged = true;
                        n.Judgment = "drag";
                        _missIdx++;
                        continue;
                    }

                    Hit(n, now);
                }

                _missIdx++;
            }

            if (now >= _chart.EndTime)
                Finish();
        }


        void AiStep(AiPlayer ai, double now, bool drive)
        {
            while (ai.NoteIdx < _notes.Count && _notes[ai.NoteIdx].Time <= now + JudgeSettings.MissWindow)
            {
                var n = _notes[ai.NoteIdx];
                if (_rnd.NextDouble() < AiEngine.Chance(ai, ai.HitTimes.Count))
                {
                    double maxDev = AiEngine.MaxDevFor(ai);
                    double dev = (_rnd.NextDouble() * 2 - 1) * maxDev;
                    ai.Plan.Add((ai.NoteIdx, false, dev, n.Time + dev));
                }
                else
                    ai.Plan.Add((ai.NoteIdx, true, 0, n.Time));
                ai.NoteIdx++;
            }

            while (ai.Plan.Count > 0 && ai.Plan[0].Time <= now)
            {
                var plan = ai.Plan[0];
                ai.Plan.RemoveAt(0);
                var n = _notes[plan.Idx];
                if (plan.Miss)
                {
                    if (!drive)
                        AiJudge(ai, n, "MISS");
                    continue;
                }

                ai.HitTimes.Add(plan.Time);
                string g = AiEngine.GradeNameForDev(Math.Abs(plan.Dev));
                if (drive)
                    Hit(n, plan.Time);
                else
                    AiJudge(ai, n, g);
                ai.Devs.Add(plan.Dev);
                if (ai.Devs.Count > 120)
                    ai.Devs.RemoveAt(0);
                AiEngine.UpdateUr(ai);
            }

            ai.KpsNow = ai.HitTimes.Count;
        }


        void AiJudge(AiPlayer ai, Note n, string g)
        {
            if (g == "MISS")
                ai.Combo = 0;
            else
            {
                ai.Combo++;
                ai.MaxCombo = Math.Max(ai.MaxCombo, ai.Combo);
                ai.Score += JudgeSettings.LevelScore(g, ai.Combo);
                ai.WeightSum += JudgeSettings.WeightFor(g);
                ai.HitCount++;
            }

            if (!ai.Hits.ContainsKey(g))
                ai.Hits[g] = 0;
            ai.Hits[g]++;
            ai.Judged++;
            ai.Acc = ai.HitCount > 0 ? ai.WeightSum / ai.HitCount * 100 : 100;
        }


        Keys KeyForCol(int col)
        {
            foreach (var kv in _keyCol)
                if (kv.Value == col)
                    return kv.Key;
            return Keys.None;
        }


        bool SpaceIsGameKey()
        {
            return _keyCol != null && _keyCol.ContainsKey(Keys.Space);
        }


        double MaimaiTypeWeight(Note n) => 1;

        void Hit(Note n, double judgeTime)
        {
            if (n.Judged)
                return;
            double dev = judgeTime - n.Time;
            if (_chart != null && _chart.Mode == GameMode.Phigros && n.Type == "drag")
            {
                n.Judged = true;
                n.Judgment = EngFor(n).JudgeDrag(n, judgeTime);
                n.Dev = dev;
                _lastJudge = n.Judgment;
                _lastDev = (dev >= 0 ? "+" : "") + dev.ToString("0.0") + "ms";
                _burst[n.Col] = Math.Max(_burst[n.Col], 1f);
                _judgePop = 1f;
                _comboPop = 1f;
                if (Skin.SoundEffects)
                    SoundFx.Hit(n.Judgment);
                return;
            }

            if (_chart != null && _chart.Mode == GameMode.Phigros && n.Type == "flick")
            {
                double pW = JudgeSettings.Levels.Count > 0 ? JudgeSettings.Levels[0].Window : 80;
                string fg = Math.Abs(dev) <= pW ? "Perfect" : "MISS";
                EngFor(n).JudgeWithGrade(n, fg, MaimaiTypeWeight(n));
                n.Judged = true;
                n.Judgment = fg;
                n.Dev = dev;
                _lastJudge = fg;
                _lastDev = (dev >= 0 ? "+" : "") + dev.ToString("0.0") + "ms";
                if (fg == "MISS")
                {
                    if (GraphicsQuality.ScreenShake && Skin.ScreenShake)
                        _shakeAmt = Math.Max(_shakeAmt, 1.0);
                    _burst[n.Col] = Math.Max(_burst[n.Col], 0.4f);
                    _judgePop = Math.Max(_judgePop, 0.6f);
                    if (Skin.SoundEffects)
                        SoundFx.PlayMiss();
                }
                else
                {
                    _burst[n.Col] = Math.Max(_burst[n.Col], 1f);
                    _judgePop = 1f;
                    _comboPop = 1f;
                    if (Skin.SoundEffects)
                        SoundFx.Hit(fg);
                }

                return;
            }

            string g0;
            bool arcaea = _chart != null && _chart.Mode == GameMode.Arcaea;
            if (arcaea)
            {
                g0 = AiEngine.GradeNameForDev(Math.Abs(dev));
                if (g0 == "MISS")
                    _arcRecollection = Math.Max(0, _arcRecollection - 9);
                else if (g0 == "FAR")
                    _arcRecollection = Math.Min(100, _arcRecollection + 0.6);
                else
                    _arcRecollection = Math.Min(100, _arcRecollection + 1.6);
                g0 = EngFor(n).JudgeArcaea(n, g0, g0 == "PURE+");
            }
            else
            {
                if (_chart != null && _chart.Mode == GameMode.AdofaiReal && n.Bpm > 0 && Math.Abs(n.Bpm - (_chart.Bpm > 0 ? _chart.Bpm : 120)) > 0.5)
                {
                    double beatMs = 60000.0 / Math.Max(30, n.Bpm);
                    double AngleMs(double deg, double floor) => Math.Max(floor, deg / 180.0 * beatMs);
                    var saved = JudgeSettings.Levels;
                    try
                    {
                        JudgeSettings.Levels = new List<JudgeLevel>
                        {
                            new JudgeLevel
                            {
                                Name = "PURE",
                                Window = AngleMs(30, 25),
                                Score = 300,
                                Weight = 1.0
                            },
                            new JudgeLevel
                            {
                                Name = "PERFECT",
                                Window = AngleMs(45, 30),
                                Score = 225,
                                Weight = 0.75
                            },
                            new JudgeLevel
                            {
                                Name = "COUNTED",
                                Window = AngleMs(60, 65),
                                Score = 120,
                                Weight = 0.4
                            }
                        };
                        g0 = EngFor(n).Judge(n, judgeTime, MaimaiTypeWeight(n));
                    }
                    finally
                    {
                        JudgeSettings.Levels = saved;
                    }
                }
                else
                {
                    g0 = EngFor(n).Judge(n, judgeTime, MaimaiTypeWeight(n));
                }
            }

            string g = g0;
            n.Judged = true;
            n.Judgment = g;
            n.Dev = dev;
            _lastJudge = g;
            _lastDev = (dev >= 0 ? "+" : "") + dev.ToString("0.0") + "ms";
            if (g == "MISS")
            {
                if (GraphicsQuality.ScreenShake && Skin.ScreenShake)
                    _shakeAmt = Math.Max(_shakeAmt, 1.0);
                _burst[n.Col] = Math.Max(_burst[n.Col], 0.4f);
                _judgePop = Math.Max(_judgePop, 0.6f);
                if (Skin.SoundEffects)
                    SoundFx.PlayMiss();
                if (_chart != null && _chart.Mode == GameMode.AdofaiReal)
                {
                    _adofaiDerails++;
                    double restartT = 0;
                    double failT = n.Time;
                    if (_chart.Events != null)
                        foreach (var ev in _chart.Events)
                            if (ev != null && ev.Type == "checkpoint" && ev.Time <= failT)
                                restartT = Math.Max(restartT, ev.Time);
                    if (restartT <= 0)
                        restartT = Math.Max(0, failT - 2 * 60000.0 / Math.Max(30, _chart.Bpm > 0 ? _chart.Bpm : 120));
                    SeekTo(restartT);
                    _lastJudge = "掉轨";
                    _lastDev = "偏离轨道，回检查点重开";
                    ShowToast("掉轨 ×" + _adofaiDerails + " · 回检查点重开");
                }
            }
            else
            {
                _burst[n.Col] = Math.Max(_burst[n.Col], 1f);
                _judgePop = 1f;
                _comboPop = 1f;
                if (Skin.SoundEffects)
                    SoundFx.Hit(g);
                if (IsHoldNote(n))
                {
                    n.Held = true;
                    if (_multi && _stagesRT.Count > 0)
                    {
                        int hs2 = Math.Max(0, Math.Min(_stagesRT.Count - 1, n.Field));
                        if (!_stagesRT[hs2].Holds.Contains(n))
                            _stagesRT[hs2].Holds.Add(n);
                    }
                    else
                        _heldNotes.Add(n);
                }
            }

            if (DanActive && JudgeSettings.DanHpEnabled && _eng.Dead)
                Finish();
            try
            {
                var L0 = ComputeLayout();
                _lastJudgePos = NoteScreenPos(n, L0, judgeTime);
                _lastJudgeMono = (long)MonoMs();
            }
            catch
            {
            }

            if (GameSettings.ShowHitFx && Skin.BurstEffects && GraphicsQuality.BurstEffects)
            {
                var L = ComputeLayout();
                var hp = _lastJudgePos;
                if (g == "MISS")
                {
                    FxParticles.SpawnRing(_rings, hp.X, hp.Y, Color.FromArgb(180, 255, 90, 90), true);
                    FxParticles.SpawnBurst(_particles, _rnd, hp.X, hp.Y, Color.FromArgb(200, 255, 90, 90), 8);
                }
                else
                {
                    bool phigros = _chart != null && _chart.Mode == GameMode.Phigros;
                    var col = phigros ? Color.White : Skin.NoteColor(n.Col);
                    FxParticles.SpawnRing(_rings, hp.X, hp.Y, col, false);
                    FxParticles.SpawnBurst(_particles, _rnd, hp.X, hp.Y, col, 12);
                }
            }

            _kpsPresses.Add(MonoMs());
            _allDev.Add(dev);
            if (_allDev.Count > 200)
                _allDev.RemoveAt(0);
            if (MpActive)
            {
                double tm = RawMs();
                if (tm - _lastMpReport > 120 || _lastMpReport < 0)
                {
                    _lastMpReport = tm;
                    if (MpScores.TryGetValue(MpSelfName, out var self))
                    {
                        self.Score = (int)_score;
                        self.Combo = _combo;
                        self.Acc = _acc;
                    }

                    MpManager.ReportHit((int)_score, _combo, _acc);
                }
            }
        }


        int LowerBound(double t)
        {
            int lo = 0, hi = _notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_notes[mid].Time < t)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }


        void HandleDown(Keys key, double now)
        {
            if (Replaying || DemoAi != null || GameSettings.Autoplay || !_playing || _paused)
                return;
            if (_multi)
            {
                MultiKeyDown(key, now);
                return;
            }

            switch (_chart?.Mode ?? GameMode.Mania)
            {
                case GameMode.OsuStandard:
                    if (key == Keys.Z || key == Keys.X || key == Keys.Space)
                    {
                        if (_osuDownKeys.Add(key))
                            OsuTap(now);
                    }

                    return;
                case GameMode.LoopComposer:
                    if (_loop != null && _keyCol.TryGetValue(key, out var lcol))
                    {
                        _loop.KeyCol(lcol, now);
                        return;
                    }

                    break;
            }

            if (!_keyCol.TryGetValue(key, out var col))
                return;
            HandleDownAt(key, col, now);
        }


        void HandleUp(Keys key, double now)
        {
            if (Replaying || !_playing)
                return;
            if (_multi)
            {
                MultiKeyUp(key, now);
                return;
            }

            switch (_chart?.Mode ?? GameMode.Mania)
            {
                case GameMode.OsuStandard:
                    _osuDownKeys.Remove(key);
                    if (!OsuHolding)
                        OsuRelease(now);
                    return;
            }

            if (!_keyCol.TryGetValue(key, out var col))
                return;
            HandleUpAt(key, col, now);
        }


        void HandleDownAt(Keys key, int col, double now)
        {
            _pressFlash[col] = Math.Max(_pressFlash[col], 1f);
            double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
            int i = LowerBound(now - lastW);
            double earlyLimit = now + lastW + 100;
            if (_chart != null && _chart.Mode == GameMode.Phigros)
            {
                int kc2 = Math.Max(1, _kc);
                double lo = (col - 0.5) / kc2, hi = (col + 1.5) / kc2;
                for (; i < _notes.Count; i++)
                {
                    var n = _notes[i];
                    if (n.Time > earlyLimit)
                        break;
                    if (n.Judged)
                        continue;
                    if (n.Type == "drag")
                    {
                        Hit(n, now);
                        return;
                    }

                    if (n.Col < 0)
                    {
                        if (n.X < lo || n.X > hi)
                            continue;
                    }
                    else if (n.Col != col)
                        continue;
                    Hit(n, now);
                    return;
                }

                return;
            }

            bool cytusGate = _chart != null && _chart.Mode == GameMode.Cytus;
            PlayLayout cytL = default;
            double cytScanY = 0, cytGate = 0;
            if (cytusGate)
            {
                cytL = ComputeLayout();
                cytScanY = CytusScanY(now, cytL);
                cytGate = CytusGate(cytL, now);
            }

            for (; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time > earlyLimit)
                    break;
                if (n.Col != col || n.Judged)
                    continue;
                if (cytusGate && Math.Abs(cytScanY - CytusNoteY(n, cytL)) > cytGate)
                    continue;
                Hit(n, now);
                return;
            }
        }


        void HandleUpAt(Keys key, int col, double now)
        {
            for (int i = 0; i < _heldNotes.Count; i++)
            {
                var n = _heldNotes[i];
                if (_chart != null && _chart.Mode == GameMode.Phigros && n.Col < 0)
                {
                    int kc2 = Math.Max(1, _kc);
                    double lo = (col - 0.5) / kc2, hi = (col + 1.5) / kc2;
                    if (n.X < lo || n.X > hi)
                        continue;
                }
                else if (n.Col != col)
                    continue;
                ReleaseHeld(n, now);
                return;
            }
        }


        void ReleaseHeld(Note n, double now)
        {
            int i = _heldNotes.IndexOf(n);
            if (i < 0)
                return;
            _heldNotes.RemoveAt(i);
            n.Held = false;
            n.Completed = true;
            double tailTol = 80;
            bool isMalody = _chart != null && _chart.Mode == GameMode.Mania && (_chart.SourcePath ?? "").ToLowerInvariant().EndsWith(".mc");
            if (now < n.End - tailTol)
            {
                if (isMalody)
                {
                    _eng.BreakCombo();
                    _lastJudge = "断连";
                    _lastDev = "长条提前松开（Malody 无尾判）";
                    _judgePop = Math.Max(_judgePop, 0.5f);
                    return;
                }

                _eng.HoldBreak(MaimaiTypeWeight(n));
                if (DanActive && JudgeSettings.DanHpEnabled && _eng.Dead)
                    Finish();
                if (GraphicsQuality.ScreenShake && Skin.ScreenShake)
                    _shakeAmt = Math.Max(_shakeAmt, 1.0);
            }
            else if (_chart != null && _chart.Mode == GameMode.Mania && !isMalody)
            {
                double eh = Math.Abs(n.Dev);
                double et = Math.Abs(now - n.End);
                double combo = eh + et;
                int od = Math.Max(0, Math.Min(10, (int)Math.Round(_chart.Od > 0 ? _chart.Od : 8)));
                double P = 16;
                double G = JudgeSettings.ManiaWindow(od, 64, 49, 34);
                double Good = JudgeSettings.ManiaWindow(od, 97, 82, 67);
                double Ok = JudgeSettings.ManiaWindow(od, 127, 112, 97);
                double Meh = JudgeSettings.ManiaWindow(od, 151, 136, 121);
                double Miss = JudgeSettings.ManiaWindow(od, 188, 173, 158);
                string tier;
                if (combo > Miss)
                    tier = "MISS";
                else if (now < n.End)
                    tier = (eh <= Meh && combo <= Meh * 2) ? "MEH" : "MISS";
                else if (eh <= P * 1.2 && combo <= P * 2.4)
                    tier = "PERFECT";
                else if (eh <= G * 1.1 && combo <= G * 2.2)
                    tier = "GREAT";
                else if (eh <= Good && combo <= Good * 2)
                    tier = "GOOD";
                else if (eh <= Ok && combo <= Ok * 2)
                    tier = "OK";
                else if (eh <= Meh && combo <= Meh * 2)
                    tier = "MEH";
                else
                    tier = "MISS";
                if (tier == "MISS")
                {
                    _eng.HoldBreak(MaimaiTypeWeight(n));
                    if (DanActive && JudgeSettings.DanHpEnabled && _eng.Dead)
                        Finish();
                    if (GraphicsQuality.ScreenShake && Skin.ScreenShake)
                        _shakeAmt = Math.Max(_shakeAmt, 1.0);
                }
                else
                {
                    _eng.HoldComplete();
                    if (tier != n.Judgment)
                    {
                        n.Judgment = tier;
                        _lastJudge = tier;
                    }
                }
            }
            else
                _eng.HoldComplete();
        }


        void PrepareModeNotes()
        {
            if (_chart == null)
                return;
            try
            {
                if (_chart.Mode == GameMode.OsuStandard)
                {
                    foreach (var n in _notes)
                    {
                        if (n == null || n.Type == "tap")
                            continue;
                        if (n.SliderType != '\0' || (n.Curve != null && n.Curve.Count >= 2))
                            n.Type = "hold";
                        else
                            n.Type = "spinner";
                    }

                    return;
                }

                if (_chart.Mode == GameMode.Cytus)
                {
                    double bpm = _chart.Bpm > 0 ? _chart.Bpm : 120;
                    foreach (var n in _notes)
                    {
                        if (n == null)
                            continue;
                        CytusPageAt(n.Time, out int page, out _);
                        bool down = (page % 2) == 0;
                        double y = Math.Max(0, Math.Min(1, n.Y));
                        double pageStart = CytusPageStartMs(page);
                        double pageMs = CytusPageMsAt(pageStart);
                        double head = pageStart + (down ? y * pageMs : (1 - y) * pageMs);
                        double dur = Math.Max(0, n.End - n.Time);
                        n.Time = head;
                        n.End = head + dur;
                    }

                    _notes.Sort((a, b) => a.Time.CompareTo(b.Time));
                }
            }
            catch
            {
            }
        }


        static bool IsOsuSlider(Note n) => n != null && n.Type == "hold";

        static bool IsOsuSpinner(Note n) => n != null && n.Type == "spinner";

        void StepNewModes(double now, double dt)
        {
            if (_chart == null)
                return;
            switch (_chart.Mode)
            {
                case GameMode.OsuStandard:
                    OsuSpinnerStep(now);
                    OsuSliderTickStep(now);
                    break;
                case GameMode.LoopComposer:
                    _loop?.Tick(now);
                    break;
            }
        }


        readonly D2DBitmap[] _osuSkin = new D2DBitmap[6];

        bool _osuSkinTried;

        static string OsuSkinDir
        {
            get
            {
                var dirs = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart", "皮肤", "osu_default"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Chart", "皮肤", "osu_default"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "osu_default")
                };
                foreach (var d in dirs)
                    if (Directory.Exists(d))
                        return d;
                return "";
            }
        }


        bool OsuSkinReady()
        {
            if (!_osuSkinTried)
            {
                _osuSkinTried = true;
                string dir = OsuSkinDir;
                if (dir.Length == 0)
                    return false;
                try
                {
                    _osuSkin[0] = _d2d.CreateBitmap(new Bitmap(Path.Combine(dir, "hitcircle.png")));
                    _osuSkin[1] = _d2d.CreateBitmap(new Bitmap(Path.Combine(dir, "hitcircleoverlay.png")));
                    _osuSkin[2] = _d2d.CreateBitmap(new Bitmap(Path.Combine(dir, "approachcircle.png")));
                    if (File.Exists(Path.Combine(dir, "sliderb0.png")))
                        _osuSkin[3] = _d2d.CreateBitmap(new Bitmap(Path.Combine(dir, "sliderb0.png")));
                    if (File.Exists(Path.Combine(dir, "sliderfollowcircle.png")))
                        _osuSkin[4] = _d2d.CreateBitmap(new Bitmap(Path.Combine(dir, "sliderfollowcircle.png")));
                    if (File.Exists(Path.Combine(dir, "sliderscorepoint.png")))
                        _osuSkin[5] = _d2d.CreateBitmap(new Bitmap(Path.Combine(dir, "sliderscorepoint.png")));
                }
                catch
                {
                    return false;
                }

                return _osuSkin[0] != null;
            }

            return _osuSkin[0] != null;
        }


        bool OsuSkinImg(int idx, out D2DBitmap img, out float size)
        {
            img = idx >= 0 && idx < _osuSkin.Length ? _osuSkin[idx] : null;
            size = 128;
            return img != null;
        }


        readonly Dictionary<Note, double[]> _osuTickCache = new Dictionary<Note, double[]>();

        double[] OsuSliderTicks(Note n)
        {
            if (_osuTickCache.TryGetValue(n, out var cached))
                return cached;
            var list = new List<double>
            {
                n.Time
            };
            double dur = n.End - n.Time;
            if (dur > 20 && _chart != null)
            {
                double rate = _chart.SliderTickRate > 0 ? _chart.SliderTickRate : 1;
                double beatMs = 60000.0 / Math.Max(30, _chart.Bpm > 0 ? _chart.Bpm : 120);
                double interval = beatMs / rate;
                if (interval > 1)
                    for (double t = n.Time + interval; t < n.End - 1e-6; t += interval)
                        list.Add(t);
            }

            if (Math.Abs(list[list.Count - 1] - n.End) > 1e-6)
                list.Add(n.End);
            var arr = list.ToArray();
            _osuTickCache[n] = arr;
            return arr;
        }


        void OsuSliderTickStep(double now)
        {
            if (_heldNotes.Count == 0)
                return;
            double window = JudgeSettings.Levels.Count > 0 ? JudgeSettings.Levels[0].Window : 80;
            for (int i = 0; i < _heldNotes.Count; i++)
            {
                var n = _heldNotes[i];
                if (_chart == null || _chart.Mode != GameMode.OsuStandard)
                    continue;
                if (!n.Held || n.Completed)
                    continue;
                var ticks = OsuSliderTicks(n);
                for (int k = 1; k < ticks.Length - 1; k++)
                {
                    if (Math.Abs(now - ticks[k]) <= window)
                    {
                        _eng.ApplyTick();
                        ticks[k] = double.NaN;
                        _judgePop = Math.Max(_judgePop, 0.5f);
                        if (Skin.SoundEffects)
                            SoundFx.Hit("tick");
                    }
                }
            }
        }


        double OsuSliderCompleteness(Note n)
        {
            var ticks = OsuSliderTicks(n);
            int hit = 0;
            foreach (var t in ticks)
                if (double.IsNaN(t) || t <= n.Time + 1e-9)
                    hit++;
            return ticks.Length > 0 ? (double)hit / ticks.Length : 1;
        }


        void OsuSpinnerStep(double now)
        {
            if (_osuSpinner != null && !_osuSpinner.Judged && now >= _osuSpinner.End)
                OsuSpinComplete(_osuSpinner, _osuSpinProgress);
        }


        void OsuSpinComplete(Note n, double spins)
        {
            if (n == null || n.Judged)
                return;
            try
            {
                n.Judged = true;
                n.Completed = true;
                n.Held = false;
                double od = _chart != null && _chart.Od > 0 ? Math.Max(0, Math.Min(10, _chart.Od)) : 5;
                double minRpm = od < 5 ? 1.5 + 0.2 * od : 1.25 + 0.25 * od;
                double required = Math.Max(1, (n.End - n.Time) / 1000.0 * minRpm + 0.5);
                double progress = spins / required;
                string g = OsuSpinGrade(progress);
                n.Judgment = g;
                _lastJudge = g;
                _lastDev = "SPIN " + ((int)(Math.Max(0, progress) * 100)).ToString() + "%";
                if (progress <= 1e-9)
                {
                    n.Judgment = "MISS";
                    _lastJudge = "MISS";
                    _lastDev = "转盘 0%";
                    _eng.JudgeWithGrade(n, "MISS");
                    _judgePop = Math.Max(_judgePop, 0.6f);
                    if (Skin.SoundEffects)
                        SoundFx.PlayMiss();
                    return;
                }

                _eng.JudgeWithGrade(n, g);
                int bi = Math.Min(15, Math.Max(0, n.Col));
                _burst[bi] = Math.Max(_burst[bi], 1f);
                _judgePop = 1f;
                _comboPop = 1f;
                if (Skin.SoundEffects)
                    SoundFx.Hit(g);
                if (GameSettings.ShowHitFx && Skin.BurstEffects && GraphicsQuality.BurstEffects)
                {
                    var L = ComputeLayout();
                    var hp = HitPointFor(n, L);
                    FxParticles.SpawnRing(_rings, hp.X, hp.Y, Skin.NoteColor(n.Col), false);
                    FxParticles.SpawnBurst(_particles, _rnd, hp.X, hp.Y, Skin.NoteColor(n.Col), 10);
                }

                _kpsPresses.Add(MonoMs());
            }
            finally
            {
                _osuSpinner = null;
                _osuSpinProgress = 0;
            }
        }


        string OsuSpinGrade(double progress)
        {
            var L = JudgeSettings.Levels;
            if (L == null || L.Count == 0)
                return "300";
            if (progress >= 1.0)
                return L[0].Name;
            if (progress >= 0.5)
                return L.Count >= 2 ? L[1].Name : L[0].Name;
            return L.Count >= 3 ? L[2].Name : L[L.Count - 1].Name;
        }


        void OsuTap(double now)
        {
            if (!_playing || _paused)
                return;
            if (_osuSpinner != null && !_osuSpinner.Judged && now < _osuSpinner.End)
            {
                _osuSpinProgress += 1.0;
                _pressFlash[0] = Math.Max(_pressFlash[0], 0.6f);
                return;
            }

            double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
            int i = LowerBound(now - lastW);
            double earlyLimit = now + lastW + 100;
            Note best = null;
            double bestDist = double.MaxValue;
            for (; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time > earlyLimit)
                    break;
                if (n.Judged)
                    continue;
                double d = Math.Abs(now - n.Time);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = n;
                }
            }

            if (best == null)
                return;
            if (IsOsuSpinner(best))
            {
                best.Held = true;
                _osuSpinner = best;
                _osuSpinProgress = 1.0;
                return;
            }

            if (IsOsuSlider(best))
            {
                Hit(best, now);
                return;
            }

            Hit(best, now);
        }


        void OsuRelease(double now)
        {
            for (int i = _heldNotes.Count - 1; i >= 0; i--)
            {
                var n = _heldNotes[i];
                if (_chart == null || _chart.Mode != GameMode.OsuStandard)
                    continue;
                _heldNotes.RemoveAt(i);
                n.Held = false;
                if (now >= n.End - 100)
                {
                    n.Completed = true;
                    double comp = OsuSliderCompleteness(n);
                    if (comp < 0.999)
                        _eng.AdjustScore(-50 * (1 - comp));
                    _eng.HoldComplete();
                }
                else
                {
                    double comp = OsuSliderCompleteness(n);
                    if (comp < 1e-9)
                    {
                        _eng.HoldBreak();
                        _lastJudge = "MISS";
                        _lastDev = "滑条未开始（完成度 0）";
                        if (GraphicsQuality.ScreenShake && Skin.ScreenShake)
                            _shakeAmt = Math.Max(_shakeAmt, 1.0);
                        if (Skin.SoundEffects)
                            SoundFx.PlayMiss();
                    }
                    else
                    {
                        _eng.BreakCombo();
                        _lastJudge = "break";
                        _lastDev = "滑条提前松开（断连）";
                        _judgePop = Math.Max(_judgePop, 0.5f);
                        if (comp < 0.999)
                            _eng.AdjustScore(-50 * (1 - comp));
                    }
                }
            }
        }


        bool TryCode(string code, out Keys key, out int col)
        {
            key = Keys.None;
            col = -1;
            if (string.IsNullOrEmpty(code))
                return false;
            try
            {
                if (code.StartsWith("Key"))
                    key = (Keys)Enum.Parse(typeof(Keys), code.Substring(3), true);
                else if (code == "Space")
                    key = Keys.Space;
                else if (code == "Semicolon")
                    key = Keys.OemSemicolon;
                else if (!Enum.TryParse(code, true, out key))
                    return false;
            }
            catch
            {
                return false;
            }

            foreach (var kv in _keyCol)
                if (kv.Key == key)
                {
                    col = kv.Value;
                    return true;
                }

            return false;
        }


        public static string KeyCodeName(Keys k)
        {
            if (k == Keys.Space)
                return "Space";
            if (k == Keys.OemSemicolon)
                return "Semicolon";
            return "Key" + k.ToString();
        }


        void Finish()
        {
            if (_ended)
                return;
            _ended = true;
            _playing = false;
            _audio.Pause();
            Logger.Info("帧率统计：" + _fps + " FPS · 平均 " + _frameMs.ToString("0.0") + "ms · " + RenderBackend.Describe);
            bool demo = DemoAi != null;
            double prevAcc = _pdata.Stats.MaxAcc;
            int prevScore = _pdata.Stats.MaxScore;
            var res = new GameResult
            {
                Title = _chart.Title,
                Artist = _chart.Artist,
                ModeName = _chart.ModeName,
                Score = (int)_score,
                Acc = _acc,
                MaxCombo = _maxCombo,
                Hits = new Dictionary<string, int>(_hits),
                Grade = JudgeSettings.GradeFor(_acc),
                IsDan = _chart.IsDan,
                DanPass = JudgeSettings.DanHpEnabled ? _hp > 0 : _acc >= JudgeSettings.DanPassAccFor(_chart),
                HpEnd = _hp,
                TotalNotes = _notes.Count,
                PrevBestAcc = prevAcc,
                NewBest = _acc > prevAcc || _score > prevScore
            };
            if (_chart != null && _chart.Mode == GameMode.AdofaiReal)
            {
                int judged = 0;
                foreach (var n in _notes)
                    if (n.Judged)
                        judged++;
                double completion = _notes.Count > 0 ? judged * 100.0 / _notes.Count : 0;
                int perfect = _hits.TryGetValue("PURE", out var hp2) ? hp2 : 0;
                int countTotal = 0;
                foreach (var lv in JudgeSettings.Levels)
                    if (_hits.TryGetValue(lv.Name, out var lc))
                        countTotal += lc;
                double precision = 100.0 + perfect * 0.01 - Math.Max(0, countTotal - perfect) * 0.05;
                res.MaxCombo = (int)Math.Round(completion);
                res.Acc = Math.Max(0, Math.Min(110, precision));
                res.Hits["完成度"] = (int)Math.Round(completion);
            }

            if (MpActive)
            {
                if (MpScores.TryGetValue(MpSelfName, out var self))
                {
                    self.Score = (int)_score;
                    self.Combo = _maxCombo;
                    self.Acc = _acc;
                    self.Finished = true;
                }

                MpManager.ReportFinish((int)_score, _maxCombo, _acc);
            }

            if (!demo && !MpActive && Recording && !GameSettings.Autoplay && !Replaying && _record.Count > 0)
                ReplaySystem.Save(new ReplayFile { Title = _chart.Title, Artist = _chart.Artist, KeyCount = _kc, Events = _record });
            if (!demo && !MpActive && !GameSettings.Autoplay)
                _pdata.Record(res);
            if (!demo && !MpActive && !GameSettings.Autoplay && !Replaying && !_humanTest && _chart != null)
            {
                try
                {
                    SaveHumanPlay(res);
                }
                catch
                {
                }
            }

            SongEnded?.Invoke(res);
            if (!demo && !MpActive && GameSettings.ResultScreenEnabled)
            {
                _resultPhase = true;
                _resultStart = MonoMs();
                _result = res;
                if (res.NewBest || res.Grade == "SSS" || res.Grade == "SS" || res.Grade == "S")
                    SpawnConfetti(ClientSize.Width, ClientSize.Height);
            }
        }


        void SaveHumanPlay(GameResult res)
        {
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "人类试玩");
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch
                {
                    dir = Path.Combine(Directory.GetCurrentDirectory(), "人类试玩");
                    Directory.CreateDirectory(dir);
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string path = Path.Combine(dir, "记录_" + stamp + ".json");
                var hits = new System.Text.Json.Nodes.JsonObject();
                foreach (var kv in res.Hits)
                    hits[kv.Key] = kv.Value;
                var root = new System.Text.Json.Nodes.JsonObject
                {
                    ["title"] = res.Title,
                    ["artist"] = res.Artist,
                    ["mode"] = res.ModeName,
                    ["playedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["score"] = res.Score,
                    ["acc"] = Math.Round(res.Acc, 2),
                    ["maxCombo"] = res.MaxCombo,
                    ["totalNotes"] = res.TotalNotes,
                    ["hits"] = hits
                };
                if (_adofaiDerails > 0)
                    root["derails"] = _adofaiDerails;
                File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
                Logger.Info("人类试玩已存档：" + path);
            }
            catch (Exception ex)
            {
                Logger.Error("人类试玩存档失败：" + ex.Message);
            }
        }


        string FindAudio(string name, string baseDir)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var cand = new List<string>
            {
                Path.Combine(baseDir, name),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart", name),
                name
            };
            foreach (var p in cand)
                if (File.Exists(p))
                    return p;
            return null;
        }


        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (EditLayoutMode)
            {
                if (e.KeyCode == Keys.Escape)
                    ExitLayoutEdit();
                e.Handled = true;
                return;
            }

            if (_resultPhase)
            {
                switch (e.KeyCode)
                {
                    case Keys.R:
                    case Keys.Enter:
                        Restart();
                        e.Handled = true;
                        return;
                    case Keys.Back:
                        StopToMenu();
                        ExitToLibrary?.Invoke();
                        e.Handled = true;
                        return;
                    case Keys.Escape:
                        RequestExitToMenu();
                        e.Handled = true;
                        return;
                }

                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Escape && DemoAi != null)
            {
                StopAiDemo();
                _playing = false;
                _paused = false;
                _ended = false;
                _audio.Stop();
                ExitToLibrary?.Invoke();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Escape && _chart != null && _playing)
            {
                _playing = false;
                _paused = false;
                _ended = false;
                _audio.Stop();
                RequestExitToMenu();
                e.Handled = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Space:
                    if (SpaceIsGameKey())
                        break;
                    if (_playing && !_ended && !MpActive)
                        TogglePause();
                    e.Handled = true;
                    return;
                case Keys.P:
                    if (_playing && !_ended && !MpActive)
                        TogglePause();
                    e.Handled = true;
                    return;
                case Keys.R:
                    if (_chart != null)
                        Restart();
                    e.Handled = true;
                    return;
                case Keys.A:
                    if (_playing && !_paused && !MpActive)
                        SetAutoplay(!GameSettings.Autoplay);
                    e.Handled = true;
                    return;
                case Keys.S:
                    if (_playing && !_paused && SkipIntro())
                    {
                        e.Handled = true;
                        return;
                    }

                    break;
                case Keys.T:
                    if (_playing && !_paused && (_chart?.Mode ?? GameMode.Mania) == GameMode.Mania)
                    {
                        GameSettings.SlantEnabled = !GameSettings.SlantEnabled;
                        ShowToast("斜轨 " + (GameSettings.SlantEnabled ? "开" : "关"));
                    }

                    e.Handled = true;
                    return;
                case Keys.V:
                    if (_playing && !_paused)
                    {
                        GameSettings.Camera3D = !GameSettings.Camera3D;
                        ShowToast("3D 渲染 " + (GameSettings.Camera3D ? "开" : "关"));
                    }

                    e.Handled = true;
                    return;
                case Keys.M:
                    if (_playing && !_paused && !_multi && _chart != null && _chart.Parts != null && _chart.Parts.Count > 1)
                        SwitchNextPart();
                    e.Handled = true;
                    return;
            }

            if (_playing)
            {
                double now = RawMs() + GameSettings.Offset;
                if (Recording && !GameSettings.Autoplay && !Replaying)
                    _record.Add(new ReplayEvent { T = RawMs(), Code = KeyCodeName(e.KeyCode), Down = true });
                HandleDown(e.KeyCode, now);
            }

            base.OnKeyDown(e);
        }


        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (EditLayoutMode)
            {
                base.OnKeyUp(e);
                return;
            }

            if (_playing)
            {
                double now = RawMs() + GameSettings.Offset;
                if (Recording && !GameSettings.Autoplay && !Replaying)
                    _record.Add(new ReplayEvent { T = RawMs(), Code = KeyCodeName(e.KeyCode), Down = false });
                HandleUp(e.KeyCode, now);
            }

            base.OnKeyUp(e);
        }


        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }


        double HitLineY
        {
            get
            {
                if (Skin.Layout.TryGetValue("hitline", out var hp))
                    return Math.Max(0.05, Math.Min(0.95, hp.Y));
                return 0.5;
            }
        }


        RectangleF HudBox(string key)
        {
            var p = Skin.Layout.TryGetValue(key, out var pos) ? pos : new HudPos
            {
                X = 0.5,
                Y = 0.1
            };
            using var g2 = CreateGraphics();
            var sz = g2.MeasureString(key, _editFont);
            float w = sz.Width + 24, h = 32;
            float cx = (float)(ClientSize.Width * p.X), cy = (float)(ClientSize.Height * p.Y);
            return new RectangleF(cx - w / 2, cy - h / 2, w, h);
        }


        RectangleF HitLineBox()
        {
            int W = ClientSize.Width;
            var area = PlayAreaRect();
            double topY = Math.Max(area.Y, 70);
            double hitY = Math.Max(topY + 24, Math.Min(area.Y + area.Height - 24, area.Y + area.Height * HitLineY));
            double rpW = ShowRightPanel ? 300 : 0;
            double rpX = W - rpW;
            double playW = Math.Max(180, Math.Min(area.Width, rpX * PlayScale));
            double playX = area.X + (area.Width - playW) / 2;
            return new RectangleF((float)playX, (float)(hitY - 8), (float)playW, 16);
        }


        protected override void OnMouseDown(MouseEventArgs e)
        {
            var vp = _d2d.ClientToVirtual(e.X, e.Y);
            if (EditLayoutMode)
            {
                if (HitLineBox().Contains(vp))
                {
                    _dragHitline = true;
                    return;
                }

                for (int i = HudKeys.Length - 1; i >= 0; i--)
                {
                    var box = HudBox(HudKeys[i]);
                    if (box.Contains(vp))
                    {
                        _dragKey = HudKeys[i];
                        _dragOff = new Point((int)(vp.X - box.X), (int)(vp.Y - box.Y));
                        return;
                    }
                }

                return;
            }

            if (_resultPhase)
            {
                Focus();
                if (ResultButtonRect(ClientSize.Width, ClientSize.Height, 0).Contains(vp))
                {
                    Restart();
                    return;
                }

                if (ResultButtonRect(ClientSize.Width, ClientSize.Height, 1).Contains(vp))
                {
                    StopToMenu();
                    RequestExitToMenu();
                    return;
                }

                base.OnMouseDown(e);
                return;
            }

            if (!EditLayoutMode && _chart != null && IsTouchMode(_chart.Mode) && !Replaying && DemoAi == null && !GameSettings.Autoplay && _playing && !_paused)
            {
                if (e.Button == MouseButtons.Left)
                {
                    TapAt(vp, RawMs() + GameSettings.Offset);
                    return;
                }
            }

            if (!EditLayoutMode && _chart != null && _chart.Mode == GameMode.LoopComposer && !Replaying && DemoAi == null && !GameSettings.Autoplay && _playing && !_paused)
            {
                if (e.Button == MouseButtons.Left)
                {
                    var LL = ComputeLayout();
                    _loop?.Mouse(vp.X, vp.Y, RawMs() + GameSettings.Offset, LL.CenterX, LL.CenterY, Math.Max(120, Math.Min(LL.AreaW, LL.AreaH) * 0.36));
                    return;
                }
            }

            if (!EditLayoutMode && _chart != null && _chart.Mode == GameMode.OsuStandard && !Replaying && DemoAi == null && !GameSettings.Autoplay && _playing && !_paused)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _osuMouseDown = true;
                    OsuTap(RawMs() + GameSettings.Offset);
                    return;
                }
            }

            Focus();
            base.OnMouseDown(e);
        }


        protected override void OnMouseMove(MouseEventArgs e)
        {
            var vp = _d2d.ClientToVirtual(e.X, e.Y);
            if (EditLayoutMode && _dragHitline)
            {
                if (!Skin.Layout.TryGetValue("hitline", out var pos))
                {
                    pos = new HudPos
                    {
                        X = 0.5,
                        Y = 0.5
                    };
                    Skin.Layout["hitline"] = pos;
                }

                pos.Y = Math.Max(0.05, Math.Min(0.95, vp.Y / (float)ClientSize.Height));
                Invalidate();
                SyncEditValues();
                return;
            }

            if (EditLayoutMode && _dragKey != null)
            {
                float nx = vp.X - _dragOff.X + _dragKey.Length / 2f + 12;
                float ny = vp.Y - _dragOff.Y + 16;
                var pos = Skin.Layout[_dragKey];
                pos.X = Math.Max(0.02, Math.Min(0.98, nx / ClientSize.Width));
                pos.Y = Math.Max(0.02, Math.Min(0.97, ny / ClientSize.Height));
                Invalidate();
                SyncEditValues();
            }

            if (!EditLayoutMode && _chart != null && _chart.Mode == GameMode.OsuStandard)
            {
                _osuCursorX = vp.X;
                _osuCursorY = vp.Y;
            }

            base.OnMouseMove(e);
        }


        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (EditLayoutMode && _dragHitline)
            {
                _dragHitline = false;
                Skin.Save();
            }

            if (EditLayoutMode && _dragKey != null)
            {
                _dragKey = null;
                Skin.Save();
            }

            if (!EditLayoutMode && _chart != null && IsTouchMode(_chart.Mode) && e.Button == MouseButtons.Left && !Replaying && DemoAi == null && !GameSettings.Autoplay && _mouseHeld.Count > 0)
            {
                double now = RawMs() + GameSettings.Offset;
                while (_mouseHeld.Count > 0)
                {
                    var n = _mouseHeld[0];
                    _mouseHeld.RemoveAt(0);
                    ReleaseHeld(n, now);
                }

                base.OnMouseUp(e);
                return;
            }

            if (!EditLayoutMode && _chart != null && _chart.Mode == GameMode.OsuStandard && e.Button == MouseButtons.Left && !Replaying)
            {
                _osuMouseDown = false;
                if (!OsuHolding)
                    OsuRelease(RawMs() + GameSettings.Offset);
            }

            base.OnMouseUp(e);
        }


        HudPos HudPosFor(string key)
        {
            if (!Skin.Layout.TryGetValue(key, out var p))
                p = new HudPos
                {
                    X = 0.5,
                    Y = 0.1
                };
            if (key == "judge")
            {
                switch (Skin.JudgePosMode)
                {
                    case 1:
                        return new HudPos
                        {
                            X = 0.5,
                            Y = Math.Max(0.10, Math.Min(0.97, HitLineY + 0.08)),
                            Show = p.Show
                        };
                    case 2:
                        return new HudPos
                        {
                            X = 0.5,
                            Y = 0.10,
                            Show = p.Show
                        };
                    case 3:
                        return new HudPos
                        {
                            X = p.X,
                            Y = p.Y,
                            Show = false
                        };
                }
            }
            else if (key == "dev")
            {
                switch (Skin.DevPosMode)
                {
                    case 1:
                        return new HudPos
                        {
                            X = 0.5,
                            Y = Math.Max(0.14, Math.Min(0.97, HitLineY + 0.13)),
                            Show = p.Show
                        };
                    case 2:
                        return new HudPos
                        {
                            X = 0.5,
                            Y = 0.15,
                            Show = p.Show
                        };
                    case 3:
                        return new HudPos
                        {
                            X = p.X,
                            Y = p.Y,
                            Show = false
                        };
                }
            }

            return p;
        }


        void DrawHud(string key, string text, Color c, float fontSize, bool center)
        {
            var p = HudPosFor(key);
            if (!p.Show)
                return;
            if (_chart != null && _chart.Mode == GameMode.Arcaea && !EditLayoutMode)
            {
                float cw = ClientSize.Width, ch = ClientSize.Height;
                switch (key)
                {
                    case "score":
                    {
                        float sw = _d2d.MeasureText(text, fontSize);
                        _d2d.Text(text, cw * 0.985f - sw, ch * 0.035f, sw + 40, fontSize + 8, c, fontSize, false);
                        return;
                    }

                    case "title":
                        _d2d.Text(text, cw * 0.985f, ch * 0.085f, 900, 22, c, 13f, true);
                        return;
                    case "combo":
                        if (_combo >= 2)
                            _d2d.Text(text, cw * 0.030f, ch * 0.115f, 560, fontSize + 10, c, fontSize, false);
                        return;
                    case "judge":
                        _d2d.Text(text, cw * 0.030f, ch * 0.055f, 420, 30, c, Math.Max(12, Math.Min(30, fontSize * 0.72f)), false);
                        return; // t61：判定字绘制在场景四边形之后（PaintCore: DrawArcaea 先画 quad/key 线，后 DrawHud），已在四边形上层
                    case "dev":
                        _d2d.Text(text, cw * 0.030f, ch * 0.095f, 400, 20, c, 10f, false);
                        return;
                    case "acc":
                        _d2d.Text(text, cw * 0.030f, ch * 0.200f, 400, fontSize + 8, c, fontSize, false);
                        return;
                    case "bpm":
                        _d2d.Text(text, cw * 0.030f, ch * 0.250f, 400, fontSize + 8, c, fontSize, false);
                        return;
                    case "kps":
                        _d2d.Text(text, cw * 0.030f, ch * 0.300f, 400, fontSize + 8, c, fontSize, false);
                        return;
                    case "notes":
                        _d2d.Text(text, cw * 0.030f, ch * 0.350f, 400, fontSize + 8, c, fontSize, false);
                        return;
                }
            }

            if (_chart != null && _chart.Mode == GameMode.OsuStandard && !EditLayoutMode)
            {
                float cw = ClientSize.Width, ch = ClientSize.Height;
                switch (key)
                {
                    case "combo":
                        if (_combo >= 2)
                            _d2d.Text(text, cw * 0.020f, ch * 0.870f, 560, fontSize + 10, c, fontSize, false);
                        return;
                    case "score":
                        _d2d.Text(text, cw * 0.020f, ch * 0.125f, 560, fontSize + 8, c, fontSize, false);
                        return; // t61b：0.075H→0.125H——徽章带 y=34..56 恒在（含回放/录制两行），Score 26pt 盒 73..107 完全让出（实测重叠 25..83 vs 34..56）
                    case "acc":
                        _d2d.Text(text, cw * 0.020f, ch * 0.175f, 400, fontSize + 8, c, fontSize, false);
                        return;
                }
            }

            float x = (float)(ClientSize.Width * p.X), y = (float)(ClientSize.Height * p.Y);
            if (!EditLayoutMode && p.Y > 0.64)
            {
                double vis = 1.0;
                try
                {
                    uint dpi = GetDpiForWindow(Handle);
                    if (dpi > 0)
                        vis = Math.Min(1.0, 96.0 / dpi);
                }
                catch
                {
                }

                y = (float)(ClientSize.Height * Math.Min(p.Y, vis * 0.94));
                if (key == "judge")
                    y += 50;
                else if (key == "dev")
                    y += 100;
            }

            if (key == "dev")
                y += 24;
            if (key == "combo" && _chart != null && _chart.Mode == GameMode.Maimai && _lastJudgePos.X > -500 && MonoMs() - _lastJudgeMono < 700)
                y -= 62; // t61b P2：maimai 中心 combo 的 PERFECT 判字同现提升连击 62px（判字在 hit 点正上，两者 0.72H/0.54H 叠区）
            float w = center ? 600 : 320;
            _d2d.Text(text, center ? x : x + 12, y, w, fontSize + 8, c, fontSize, center);
        }


        int DrawBadge(int x, int y, string text, Color fg, Color bg, Color border)
        {
            float tw = _d2d.MeasureText(text, 9f);
            float w = tw + 22;
            _d2d.FillRect(x, y, w, 22, bg);
            _d2d.DrawRect(x, y, w, 22, border, 1f);
            _d2d.Text(text, x + w / 2f, y + 11, w - 8, 16, fg, 9f, true);
            return x + (int)w + 8;
        }


        Color DevColor(double adev)
        {
            for (int i = 0; i < JudgeSettings.Levels.Count; i++)
                if (adev <= JudgeSettings.Levels[i].Window)
                    return JudgeColors[i % JudgeColors.Length];
            return Color.FromArgb(255, 90, 90);
        }


        void DrawKpsGraph(int x, int w, int y, int h)
        {
            _d2d.FillRect(x, y, w, h, Color.FromArgb(60, Skin.BgColor));
            _d2d.DrawRect(x, y, w, h, Color.DimGray, 1f);
            var hist = _kpsHistory;
            if (hist.Count == 0)
                return;
            double maxV = 10;
            foreach (var e in hist)
                maxV = Math.Max(maxV, e.v);
            int n = hist.Count;
            double bw = (double)w / n;
            Color bar = Color.FromArgb(210, 64, 180, 255);
            for (int i = 0; i < n; i++)
            {
                double hh = hist[i].v / maxV * (h - 4);
                _d2d.FillRect((float)(x + i * bw), (float)(y + h - hh), (float)Math.Max(1, bw - 1), (float)hh, bar);
            }
        }


        void DrawJudgeTicks(double max, int count, Action<int, double> line)
        {
            for (int i = 0; i < count; i++)
                for (int s = 1; s >= -1; s -= 2)
                    line(i, s * JudgeSettings.Levels[i].Window / max);
        }


        void DrawDevGraph(int x, int w, int y, int h)
        {
            _d2d.FillRect(x, y, w, h, Color.FromArgb(60, Skin.BgColor));
            _d2d.DrawRect(x, y, w, h, Color.DimGray, 1f);
            double cy = y + h / 2.0;
            _d2d.DrawLine(x, (float)cy, x + w, (float)cy, Color.White, 1f);
            double max = Math.Max(60, JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window + 20);
            DrawJudgeTicks(max, JudgeSettings.Levels.Count, (i, f) =>
            {
                _d2d.DrawLine(x, (float)(cy - f * (h / 2.0)), x + w, (float)(cy - f * (h / 2.0)), Color.FromArgb(70, 255, 255, 255), 1f);
            });
            var list = _allDev;
            if (list.Count == 0)
                return;
            double step = (double)w / 120;
            int start = Math.Max(0, list.Count - 120);
            for (int i = start; i < list.Count; i++)
            {
                double d = list[i];
                double yy = cy - d / max * (h / 2.0);
                _d2d.FillRect((float)(x + (i - start) * step), (float)(yy - 1.5f), 3, 3, DevColor(Math.Abs(d)));
            }
        }


        void DrawJudgeBar(float cx, float cy, float w)
        {
            double max = Math.Max(60, JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window + 20);
            _d2d.FillRect(cx - w / 2, cy - 3, w, 6, Skin.BgColor);
            DrawJudgeTicks(max, JudgeSettings.Levels.Count, (i, f) =>
            {
                _d2d.DrawLine(cx + (float)(f * w / 2), cy - 5, cx + (float)(f * w / 2), cy + 5, JudgeColors[i % JudgeColors.Length], 1f);
            });
            var list = _allDev;
            for (int i = Math.Max(0, list.Count - 24); i < list.Count; i++)
            {
                float xx = cx + (float)(list[i] / max * w / 2);
                _d2d.FillRect(xx - 1.5f, cy - 7, 3, 14, DevColor(Math.Abs(list[i])));
            }
        }


        void DrawRightPanel(int rx, int rw, int H)
        {
            _d2d.FillRect(rx, 0, rw, H, Color.FromArgb(210, 8, 12, 20));
            _d2d.Text("实时 KPS", rx + 12, 73, rw - 24, 18, Color.White, 10f);
            _d2d.Text(_kps.ToString(), rx + 12, 99, rw - 24, 34, Color.White, 26f);
            if (GraphicsQuality.RightPanelGraphs)
            {
                DrawKpsGraph(rx + 12, rw - 24, 128, 66);
                _d2d.Text("实时打击偏差 (ms)", rx + 12, 241, rw - 24, 18, Color.White, 10f);
                DrawDevGraph(rx + 12, rw - 24, 250, 90);
                _d2d.Text("判定窗口", rx + 12, 379, rw - 24, 18, Color.White, 10f);
                DrawJudgeBar(rx + rw / 2f, 396, rw - 40);
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            if (_d2d == null)
            {
                base.OnPaint(e);
                return;
            }

            if (_renderThreadRunning)
            {
                base.OnPaint(e);
                return;
            }

            var psw = Stopwatch.StartNew();
            try
            {
                lock (_renderLock)
                {
                    PaintCore();
                }
            }
            catch (Exception ex)
            {
                if (_paintErrOnce++ == 0)
                    Logger.Error("渲染帧失败（已跳过，不影响游戏）", ex);
                try
                {
                    _d2d.End();
                }
                catch
                {
                }
            }

            psw.Stop();
            PaintMs = psw.Elapsed.TotalMilliseconds;
        }


        int _paintErrOnce;

        int _phigLineCount;

        void PaintCore()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (IsDisposed || _d2d == null)
                return;
            _d2d.Resize(W, H);
            if (!_d2d.Begin())
                return;
            _d2d.Clear(Skin.BgColor);
            if (_lastTextAA != GraphicsQuality.TextAA)
            {
                _lastTextAA = GraphicsQuality.TextAA;
                _d2d.SetTextAA(_lastTextAA);
            }

            _fpsFrames++;
            double mono = MonoMs();
            if (_fpsStart < 0)
                _fpsStart = mono;
            if (mono - _fpsStart >= 500)
            {
                double el = mono - _fpsStart;
                _fps = (int)(_fpsFrames * 1000.0 / el);
                _frameMs = el / _fpsFrames;
                _fpsFrames = 0;
                _fpsStart = mono;
                D2DRenderer.ReportFps(_fps);
            }

            bool hasChart = _chart != null;
            if (!hasChart && !EditLayoutMode)
            {
                if (GameSettings.ShowFps)
                    _d2d.Text("FPS " + _fps + " · " + _frameMs.ToString("0.0") + "ms", W - 150, 19, 150, 18, Color.White, 9f);
                _d2d.Text("Milestone · 在主菜单选择「开始游戏」进入选歌", 24, 36, W - 40, 24, Color.White, 13f);
                _d2d.End();
                return;
            }

            double now = RawMs() + GameSettings.Offset;
            var L = ComputeLayout();
            int kc = hasChart ? _kc : 4;
            double ox = 0, oy = 0;
            if (_shakeAmt > 0.01 && GraphicsQuality.ScreenShake && Skin.ScreenShake)
            {
                ox = (_rnd.NextDouble() * 2 - 1) * 7 * _shakeAmt;
                oy = (_rnd.NextDouble() * 2 - 1) * 7 * _shakeAmt;
            }

            double topY = L.TopY + oy;
            double hitY = L.HitY + oy;
            double playX = L.PlayX + ox;
            double playW = L.PlayW;
            if (!EditLayoutMode && _bgD2D != null && GraphicsQuality.ShowBackgroundArt && Skin.ShowBackground)
            {
                float bw = _bgD2D.Width, bh = _bgD2D.Height;
                double sc = Math.Max((double)W / bw, (double)H / bh);
                float dw = (float)(bw * sc), dh = (float)(bh * sc);
                _d2d.DrawImage(_bgD2D, (float)((W - dw) / 2), (float)((H - dh) / 2), dw, dh, (float)(0.15 + 0.75 * Skin.BgDim));
                _d2d.FillRect(0, 0, W, H, Color.FromArgb((int)(30 + 150 * (1 - Skin.BgDim)), 10, 14, 22));
            }

            if (EditLayoutMode || !hasChart)
            {
                DrawManiaPreview(W, H, kc, topY, hitY, playX, playW);
                _d2d.End();
                return;
            }

            if (!ModeSystem.IsAvailable(_chart.Mode))
            {
                DrawUnsupported(W, H);
                _d2d.End();
                return;
            }

            if (_multi)
            {
                PaintMultiStages(W, H, now);
                _d2d.End();
                AutoShotTick();
                return;
            }

            switch (_chart.Mode)
            {
                case GameMode.Phigros:
                    DrawPhigros(W, H, now, L, topY, kc);
                    break;
                case GameMode.Arcaea:
                    DrawArcaea(W, H, now, L, topY, hitY, kc);
                    break;
                case GameMode.Cytus:
                    DrawCytus(W, H, now, L, topY);
                    break;
                case GameMode.OsuStandard:
                    DrawOsuStandard(W, H, now, L);
                    break;
                case GameMode.Adofai:
                    DrawAdofai(W, H, now, L);
                    break;
                case GameMode.AdofaiReal:
                    DrawAdofaiReal(W, H, now, L);
                    break;
                case GameMode.Iidx:
                    DrawIidx(W, H, now, L, topY, hitY);
                    break;
                case GameMode.Maimai:
                    DrawMaimai(W, H, now, L);
                    break;
                case GameMode.LoopComposer:
                    DrawLoopComposer(W, H, now, L);
                    break;
                default:
                    DrawMania(W, H, now, L, topY, hitY, playX, playW, kc);
                    break;
            }

            {
                var pa = PlayAreaRect();
                _d2d.DrawRect(pa.X, pa.Y, pa.Width, pa.Height, Color.FromArgb(72, 150, 190, 235), 1.4f);
            }

            if (GameSettings.ShowHitFx && Skin.BurstEffects && GraphicsQuality.BurstEffects)
                DrawHitFx();
            DrawHpBar(W);
            DrawHud("title", _chart.Title + " · " + _kc + "K", ColTitle, 13f, true);
            if (Skin.ShowScore)
                DrawHud("score", "Score " + (int)_score, ColScore, 26f, false);
            if (Skin.ShowAcc)
                DrawHud("acc", "ACC " + _acc.ToString("0.00") + "%", ColAcc, 13f, false);
            DrawHud("bpm", "BPM " + (_chart.Bpm > 0 ? _chart.Bpm.ToString("0") : "--"), ColBpm, 10f, false);
            DrawHud("kps", "KPS " + _kps, ColKps, 10f, false);
            DrawHud("notes", "音符 " + SumHits() + " / " + _notes.Count, ColNotes, 10f, false);
            if (Skin.ShowCombo && _combo >= 2)
                DrawHud("combo", _combo.ToString(), ColTitle, 44f * (1f + 0.28f * _comboPop), true);
            if (_lastJudge.Length > 0)
            {
                var jc = JudgeColor(hasChart ? _chart.Mode : GameMode.Mania, _lastJudge);
                long age = (long)MonoMs() - _lastJudgeMono;
                if (age >= 0 && age < 700 && _lastJudgePos.X > -500)
                {
                    float rise = (float)((age / 1000.0) * 46);
                    int a = (int)(255 * (1 - age / 700.0));
                    float judgeMinX = (_chart != null && (_chart.Mode == GameMode.Iidx || _chart.Mode == GameMode.Cytus || _chart.Mode == GameMode.Phigros)) ? 280f :
                        120f; // t61：无轨类判定字让出 x<160 左侧 HUD 带（Text center:true，盒宽 240→左缘=cx-120；280 保证墨迹全在 x≥160）
                    // t61b2 P2：maimai 判定字上移 —— 命中点在环上按钮时判字默认 -60-rise 会与中央 A~F 圈/连击排布拥挤，maimai 再抬 46px（0.06H）
                    float jLift = (hasChart && _chart.Mode == GameMode.Maimai) ? 46f : 0f;
                    _d2d.Text(_lastJudge, Math.Max(judgeMinX, Math.Min((float)W - 120f, _lastJudgePos.X - 120f)), _lastJudgePos.Y - 60 - rise - jLift, 240, 52, Color.FromArgb(Math.Max(0, a), jc),
                        Math.Max(12, Math.Min(60, Skin.JudgeFont * (1f + 0.35f * _judgePop))), true);
                }
                else
                    DrawHud("judge", _lastJudge, jc, Math.Max(12, Math.Min(60, Skin.JudgeFont * (1f + 0.35f * _judgePop))), true);
            }

            DrawHud("dev", _lastDev, ColDev, 10f, true);
            if (GameSettings.ShowFps)
                _d2d.Text("FPS " + _fps + " · " + _frameMs.ToString("0.0") + "ms", W - 150, 19, 150, 18, Color.White, 9f);
            if (_canSkip && !_skipped && _playing && !_paused && now < _firstNoteTime - 1000)
            {
                double skipLeft = Math.Max(0, (_firstNoteTime - 1500 - now) / 1000.0);
                double pulse = 0.5 + 0.5 * Math.Sin(mono / 170.0);
                float sbw = 520, sbh = 68;
                float sbx = W / 2f - sbw / 2, sby = H * 0.30f;
                _d2d.FillRect(sbx - 10, sby - 10, sbw + 20, sbh + 20, Color.FromArgb((int)(120 + 70 * pulse), 10, 8, 4));
                _d2d.FillRect(sbx, sby, sbw, sbh, Color.FromArgb((int)(180 + 60 * pulse), 26, 20, 8));
                _d2d.DrawRect(sbx, sby, sbw, sbh, Color.FromArgb(255, 255, 210, 63), 2f);
                _d2d.Text("⏭ 按 S 跳过开头空白（约 " + skipLeft.ToString("0") + " 秒）", W / 2f, sby + 22, sbw - 24, 22, Color.FromArgb(255, 255, 210, 63), 16f, true);
                _d2d.Text("▶ 直接进入第一个音符", W / 2f, sby + 46, sbw - 24, 16, Color.White, 11f, true);
            }

            if (mono < _qualityNoticeUntil && _qualityNotice.Length > 0)
                _d2d.Text(_qualityNotice, W / 2f, H - 26, 500, 20, Color.FromArgb(255, 210, 63), 11f, true);
            if (mono < _toastUntil && _toast.Length > 0)
            {
                float ta = (float)Math.Min(1, (_toastUntil - mono) / 300.0);
                _d2d.Text(_toast, W / 2f, H - 62, 460, 24, Color.FromArgb((int)(ta * 255), 255, 210, 63), 14f, true);
            }

            int bx = 10;
            bx = DrawBadge(bx, 8, "🔒 本地离线", Color.FromArgb(126, 232, 162), Color.FromArgb(12, 37, 24), Color.FromArgb(30, 92, 58));
            if (DemoAi != null)
                bx = DrawBadge(bx, 8, "🤖 AI 演示中 · ESC 退出", Color.FromArgb(126, 232, 162), Color.FromArgb(12, 37, 24), Color.FromArgb(30, 92, 58));
            else if (GameSettings.Autoplay)
                bx = DrawBadge(bx, 8, "AUTO 自动游玩", Color.FromArgb(126, 232, 162), Color.FromArgb(12, 37, 24), Color.FromArgb(30, 92, 58));
            int bx2 = 10;
            if (Replaying)
                bx2 = DrawBadge(bx2, 34, "🎬 回放中", Color.FromArgb(255, 210, 63), Color.FromArgb(26, 20, 8), Color.FromArgb(61, 58, 28));
            else if (Recording && !Replaying)
                bx2 = DrawBadge(bx2, 34, "● 录制中", Color.FromArgb(255, 122, 122), Color.FromArgb(42, 16, 16), Color.FromArgb(92, 42, 42));
            if (DemoAi != null)
            {
                string tag = "😨 " + DemoAi.Tension.ToString("0") + "% · UR " + DemoAi.Ur.ToString("0");
                if (DemoAi.GodMode)
                    tag += " · ⚡超神";
                else if (DemoAi.StreamMode)
                    tag += " · 🎵对拍";
                DrawBadge(bx2, 34, tag, Color.FromArgb(255, 210, 63), Color.FromArgb(26, 20, 8), Color.FromArgb(61, 58, 28));
            }

            if (_paused)
                _d2d.Text("已暂停  [空格/P 继续]  [R 重开]  [ESC 返回]", W / 2f, H / 2f, 440, 22, Color.FromArgb(255, 210, 63), 13f, true);
            if (ShowRightPanel && L.RpW > 0)
                DrawRightPanel((int)L.RpX, (int)L.RpW, H);
            if (_chart != null && _chart.Mode == GameMode.Cytus)
            {
                double tp = CytusTp();
                _d2d.Text("TP " + tp.ToString("0.00") + "%", W - 460, 42, 140, 22, Color.FromArgb(255, 160, 200, 255), 14f);
            }

            if (MpActive)
            {
                _d2d.FillRect(W - 330, 56, 314, (float)(26 + 22 * Math.Min(MpScores.Count, 8)), Color.FromArgb(210, 10, 14, 22));
                _d2d.Text("🌐 联机实时", W - 320, 69, 300, 18, Color.FromArgb(127, 208, 160), 10f);
                int li = 0;
                foreach (var p in MpScores.Values.OrderByDescending(x => x.Score))
                {
                    string mark = p.Name == MpSelfName ? "（我）" : "";
                    _d2d.Text(p.Name + mark + "　" + p.Score + "　" + p.Acc.ToString("0.00") + "%" + (p.Finished ? "　✓" : ""), W - 320, 89 + li * 20, 300, 18, Color.White, 10f);
                    if (++li >= 8)
                        break;
                }
            }

            if (_ais.Count > 0)
            {
                float top = 66;
                if (MpActive)
                    top = 56 + 26 + 22 * Math.Min(MpScores.Count, 8) + 12;
                _d2d.FillRect(W - 300, top, 284, (float)(24 + 22 * (Math.Min(_ais.Count, 6) + 1)), Color.FromArgb(200, 20, 30, 52));
                _d2d.Text("🤖 陪玩", W - 292, top + 13, 280, 18, Color.FromArgb(127, 208, 160), 10f);
                for (int i = 0; i < _ais.Count && i < 6; i++)
                {
                    var a = _ais[i];
                    int vs = (int)_score - (int)a.Score;
                    if (i == 0)
                        _d2d.Text("vs 我 " + (vs >= 0 ? "+" : "") + vs + " 分", W - 292, top + 31, 280, 18, vs >= 0 ? Color.FromArgb(127, 208, 160) : Color.FromArgb(255, 122, 122), 10f);
                    string s = a.Name +
                        "  " + (int)a.Score +
                        "  " + a.Acc.ToString("0.00") +
                        "%  ♥" + (int)a.Stamina +
                        "  😨" + a.Tension.ToString("0") +
                        "%" + (a.GodMode ? "  ⚡" : (a.StreamMode ? "  🎵" : ""));
                    _d2d.Text(s, W - 292, top + 51 + i * 20, 280, 18, Color.White, 10f);
                }
            }

            double ratio = _chart.EndTime > 0 ? Math.Max(0, Math.Min(1, now / _chart.EndTime)) : 0;
            _d2d.FillRect(0, H - 6, W, 3, Color.WhiteSmoke);
            _d2d.FillRect(0, H - 6, (float)(W * ratio), 3, Color.DodgerBlue);
            if (_resultPhase && _result != null)
                DrawResultScreen(W, H, mono);
            _d2d.End();
            AutoShotTick();
        }


        public static string AutoShotDir;

        int _autoShotCounter, _autoShotCount;

        void AutoShotTick()
        {
            if (string.IsNullOrEmpty(AutoShotDir))
                return;
            bool inPlay = _playing && !_paused;
            if (!inPlay && _autoShotCount >= 2)
                return;
            if (++_autoShotCounter < 30)
                return;
            _autoShotCounter = 0;
            try
            {
                if (_autoShotCount++ >= 40)
                    return;
                string path = Path.Combine(AutoShotDir, $"auto_{DateTime.Now:HHmmssfff}.png");
                using (var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = false;
                        try
                        {
                            ok = PrintWindow(Handle, hdc, 2);
                        }
                        catch
                        {
                        }

                        g.ReleaseHdc(hdc);
                        if (ok)
                            bmp.Save(path, ImageFormat.Png);
                    }
                }
            }
            catch
            {
            }
        }


        /* ===================== Milestone v4.0 布局 / 渲染辅助 ===================== */
        /// <summary>段位 HP 系统是否生效（仅真实游玩段位谱）。</summary>
        bool DanActive => _chart != null && _chart.IsDan && !GameSettings.Autoplay && DemoAi == null && !Replaying && !MpActive;

        Dictionary<string, ModeElem> _lcCache;

        /// <summary>刷新当前模式的覆盖集缓存（进编辑/切谱/换模式/Skin 修改时调用）。</summary>
        void LCRebuild()
        {
            _lcMode = null;
            _lcCache = null;
        }


        Dictionary<string, ModeElem> LC_Map()
        {
            if (_lcCache == null)
            {
                _lcCache = new Dictionary<string, ModeElem>();
                var id = ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania);
                _lcMode = id;
                if (Skin.ModeLayout != null && Skin.ModeLayout.TryGetValue(id, out var ov) && ov != null)
                    foreach (var kv in ov)
                        _lcCache[kv.Key] = kv.Value;
            }

            return _lcCache;
        }


        /// <summary>继承链：覆盖 → 模式默认表 → 全局兜底（每帧调用；缓存命中零分配）。</summary>
        ModeElem LC_Get(string key)
        {
            var id = ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania);
            var map = LC_Map();
            ModeElem v = null;
            if (map.TryGetValue(key, out var o))
                v = o;
            if (v == null)
                v = LayoutCustomDefaults.ModeDefault(id, key);
            return v ?? new ModeElem();
        }


        double LC_P(string key, double fallback)
        {
            var e = LC_Get(key);
            return double.IsNaN(e.P) ? fallback : e.P;
        }


        double LC_Y(string key, double fallback)
        {
            var e = LC_Get(key);
            return double.IsNaN(e.Y) ? fallback : e.Y;
        }


        bool LC_Bool(string key, bool fallback)
        {
            var e = LC_Get(key);
            return e.Show;
        }


        /* ================= maimai（环形 8 分区 + 中央 touch）================= */
        /// <summary>maimai 按钮位置：Col 1~8 外圈（官方编号顺时针：8=正上 0°、1=右上 45°、2=右、3=右下、4=下、5=左下、6=左、7=左上），Col 9~14 中央 A~F（9=A 中心，10=B 上 … 14=F，简化布局待实机校准）。</summary>
        void MaimaiButtonPos(Note n, PlayLayout L, out double x, out double y)
        {
            // t63 B2：ring.center 偏移（归一化 0..1 → 游玩区像素；默认 0.5=不偏移=像素不变）
            var rce = LC_Get("ring.center");
            double cx = L.CenterX + (rce.X - 0.5) * L.AreaW;
            double cy = L.CenterY + (rce.Y - 0.5) * L.AreaH;
            double R = MaimaiRingR(L);
            int col = n.Col;
            // t61：方位映射——统一引擎 RingNote（StartAngle=c×45°，c=0=正上）与 simai 键（1~8，8=正上）；
            //   c=0 或 8 → 扇区 8（正上）；c=1..7 → 扇区 1..7；越界（<0 或 >14）→ 最近扇区 + 告警。
            if (col < 0 || col > 14)
            {
                if (_maimaiColWarn == 0)
                {
                    _maimaiColWarn = 1;
                    Logger.Error("maimai 方位越界 Col=" + col + " 按最近扇区归一（t61）");
                }
            }

            // 中央 A~F（引擎使用者定案：设计如此的特殊键圈，非映射缺失）：A 在圆心，B~F 五角环绕（B 上，顺时针 72°）
            if (col >= 9)
            {
                int idx = col - 9;
                if (idx == 0)
                {
                    x = cx;
                    y = cy;
                    return;
                }

                double a = (-90 + (idx - 1) * 72) * Math.PI / 180.0;
                double r = R * 0.30 * LC_P("mid.radius", 1.0); // t63 B2：中央五角半径倍率
                x = cx + Math.Cos(a) * r;
                y = cy + Math.Sin(a) * r;
                return;
            }

            int k = ((col % 8) + 8) % 8;
            if (k == 0)
                k = 8;
            double ang = (k * 45 % 360) * Math.PI / 180.0; // 8=上 0°，1=右上 45°，顺时针
            x = cx + Math.Sin(ang) * R;
            y = cy - Math.Cos(ang) * R;
        }

    }
}
