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

        /// <summary>按谱面 stages 构建场运行时（旧谱无 stages → 单场兼容，_eng=场 0 引擎）。</summary>
        void BuildStageRuntime(Chart chart)
        {
            _stagesRT = new List<StageRT>();
            _multiDowns.Clear();
            if (chart == null || chart.Stages == null || chart.Stages.Count == 0 || chart.Parts == null || chart.Parts.Count == 0)
            {
                _stagesRT.Add(new StageRT { Spec = null, StageMode = chart != null ? chart.Mode : GameMode.Mania, Kc = _kc, Notes = _notes, Eng = _eng });
                _multi = false;
                return;
            }

            _multi = true;
            for (int i = 0; i < chart.Stages.Count; i++)
            {
                var sp = chart.Stages[i];
                if (sp == null)
                    continue;
                var part = i < chart.Parts.Count ? chart.Parts[i] : null;
                var objs = part != null ? part.Notes : (i == 0 ? chart.Notes : new List<Note>());
                int kc = part != null && part.KeyCount > 0 ? part.KeyCount : sp.KeyCount;
                if (kc <= 0)
                    kc = _kc;
                var eng = i == 0 ? _eng : new JudgementEngine(true);
                eng.MissCountsInAcc = true;
                eng.TotalNotes = objs.Count;
                var rt = new StageRT
                {
                    Spec = sp,
                    StageMode = sp.Mode,
                    Kc = kc,
                    Notes = objs,
                    Eng = eng
                };
                foreach (var nn in objs)
                    if (nn != null)
                        nn.Field = i; // 运行期统一场归属（手动构造谱也正确路由）
                if (kc > 0 && sp.Mode != GameMode.OsuStandard && sp.Mode != GameMode.LoopComposer)
                {
                    var keys = sp.KeyMap != null && sp.KeyMap.Length > 0 ? sp.KeyMap : GameSettings.GetKeys(kc);
                    for (int k = 0; k < keys.Length; k++)
                        rt.KeyCol[keys[k]] = k;
                    if (sp.Mode == GameMode.Adofai || sp.Mode == GameMode.AdofaiReal)
                    {
                        rt.KeyCol.Clear();
                        rt.KeyCol[Keys.Space] = 0;
                        rt.KeyCol[Keys.D] = 0;
                    }
                }

                _stagesRT.Add(rt);
            }

            if (_stagesRT.Count == 0)
            {
                _stagesRT.Add(new StageRT { Spec = null, StageMode = chart.Mode, Kc = _kc, Notes = _notes, Eng = _eng });
                _multi = false;
            }
        }


        /// <summary>重开：重置场 1..n 的引擎/游标/音符状态（场 0 由 ResetState 处理）。</summary>
        void ResetMultiStates()
        {
            for (int i = 1; i < _stagesRT.Count; i++)
            {
                var st = _stagesRT[i];
                st.Eng.Reset();
                st.Eng.MissCountsInAcc = true;
                st.Eng.TotalNotes = st.Notes.Count;
                st.AutoIdx = 0;
                st.SweepIdx = 0;
                st.Holds.Clear();
                foreach (var n in st.Notes)
                {
                    n.Judged = false;
                    n.Held = false;
                    n.Completed = false;
                    n.Judgment = null;
                    n.Dev = 0;
                }
            }
        }


        /// <summary>合并各场判定命中计数（结算/总评 HUD）。</summary>
        Dictionary<string, int> MultiMergedHits()
        {
            var m = new Dictionary<string, int>();
            foreach (var st in _stagesRT)
                foreach (var kv in st.Eng.Hits)
                    m[kv.Key] = m.TryGetValue(kv.Key, out var v) ? v + kv.Value : kv.Value;
            return m;
        }


        /// <summary>音符所属场引擎（多场路由；单场= _eng）。</summary>
        JudgementEngine EngFor(Note n)
        {
            if (!_multi || _stagesRT.Count == 0)
                return _eng;
            int s = Math.Max(0, Math.Min(_stagesRT.Count - 1, n.Field));
            return _stagesRT[s].Eng;
        }


        /// <summary>舞台屏幕矩形（归一化 → 像素；钳制最小尺寸与出界）。</summary>
        RectangleF StageRect(Stage st)
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            double x = Math.Clamp(st.X, 0, 0.95) * W;
            double y = Math.Clamp(st.Y, 0, 0.95) * H;
            double w = Math.Clamp(st.W, 0.05, 1) * W;
            double h = Math.Clamp(st.H, 0.05, 1) * H;
            if (x + w > W)
                w = W - x;
            if (y + h > H)
                h = H - y;
            return new RectangleF((float)x, (float)y, (float)Math.Max(2, w), (float)Math.Max(2, h));
        }


        /// <summary>场局部布局（与 ComputeLayout 同算法；base=场矩形，无右侧面板，小场合并顶部余量）。</summary>
        PlayLayout ComputeLayoutFor(RectangleF area, int kc, bool useRightPanel)
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            var l = new PlayLayout
            {
                H = H
            };
            l.AreaX = area.X;
            l.AreaY = area.Y;
            l.AreaW = area.Width;
            l.AreaH = area.Height;
            l.TopY = area.Height < 220 ? l.AreaY + 24 : Math.Max(l.AreaY, 70);
            double hitFrac = Math.Min(HitLineY, 0.94);
            // t63 B2：field.hit/field.w 仅覆盖时生效（默认=维持全局 HitLineY/PlayScale——像素不变）
            string _lcid = ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania);
            bool _hitOv = Skin.ModeLayout != null && Skin.ModeLayout.TryGetValue(_lcid, out var _hd) && _hd != null && _hd.ContainsKey("field.hit");
            l.HitY = Math.Max(l.TopY + 20, Math.Min(l.AreaY + l.AreaH - 14, l.AreaY + l.AreaH * (_hitOv ? Math.Max(0.05, Math.Min(0.95, LC_Y("field.hit", hitFrac))) : hitFrac)));
            l.RpW = 0;
            l.RpX = W;
            l.PlayW = Math.Max(60, Math.Min(area.Width, area.Width * PlayScale));
            bool _wOv = Skin.ModeLayout != null && Skin.ModeLayout.TryGetValue(_lcid, out var _wd) && _wd != null && _wd.ContainsKey("field.w");
            if (_wOv)
                l.PlayW = Math.Max(60, Math.Min(area.Width, l.PlayW * Math.Max(0.5, Math.Min(1.5, LC_P("field.w", 1.0)))));
            double zoom = EvalEvents("zoom", RawMs() + GameSettings.Offset, 1.0);
            l.PlayW = Math.Max(50, Math.Min(area.Width, l.PlayW * Math.Max(0.5, Math.Min(1.5, zoom))));
            l.PlayX = area.X + (area.Width - l.PlayW) / 2;
            l.LaneW = l.PlayW / Math.Max(1, kc);
            l.CenterX = area.X + area.Width / 2.0;
            l.CenterY = area.Y + area.Height / 2.0;
            double ns = EvalEvents("noteSpeed", RawMs() + GameSettings.Offset, 1.0);
            l.Ppm = (l.HitY - l.TopY) * GameSettings.Speed / 1000.0 * Math.Max(0.3, Math.Min(3.0, ns));
            l.ScrollDir = EvalEvents("scroll", RawMs() + GameSettings.Offset, 1.0) < 0 ? -1 : 1;
            return l;
        }


        /// <summary>场音符近窗二分（与 LowerBound 同算法，作用于场音符表）。</summary>
        int StageLowerBound(StageRT st, double t)
        {
            int lo = 0, hi = st.Notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (st.Notes[mid].Time < t)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }


        /// <summary>场近窗候选（列匹配；Phigros/Cytus 门控在单场路径，多场触点走 TapAt）。</summary>
        Note FindStageCandidate(StageRT st, int col, double now, double lastW)
        {
            int i = StageLowerBound(st, now - lastW);
            double earlyLimit = now + lastW + 100;
            for (; i < st.Notes.Count; i++)
            {
                var n = st.Notes[i];
                if (n.Time > earlyLimit)
                    break;
                if (n.Judged || n.Type == "spinner")
                    continue;
                if (n.Col != col)
                    continue;
                return n;
            }

            return null;
        }


        /// <summary>多场按键 Down：按键 → 各场键位块候选 → 冲突决议（|dev| 最小；±0.5ms 平局取小场号）。</summary>
        void MultiKeyDown(Keys key, double now)
        {
            double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
            int bestS = -1;
            Note bestN = null;
            double bestDev = 0;
            for (int s = 0; s < _stagesRT.Count; s++)
            {
                var st = _stagesRT[s];
                if (st.KeyCol == null || !st.KeyCol.TryGetValue(key, out var col))
                    continue;
                var n = FindStageCandidate(st, col, now, lastW);
                if (n == null)
                    continue;
                double dev = now - n.Time;
                if (bestN == null || Math.Abs(dev) < Math.Abs(bestDev) - 0.5)
                {
                    bestS = s;
                    bestN = n;
                    bestDev = dev;
                }
            }

            if (bestN == null)
                return;
            _multiDowns[key] = bestS;
            int bi = Math.Max(0, Math.Min(15, bestN.Col));
            _pressFlash[bi] = Math.Max(_pressFlash[bi], 1f);
            Hit(bestN, now);
        }


        /// <summary>多场按键 Up：按 Down 决议的场释放（HOLD 提前松开 v1 简化：直接完成）。</summary>
        void MultiKeyUp(Keys key, double now)
        {
            if (!_multiDowns.TryGetValue(key, out int s))
                return;
            _multiDowns.Remove(key);
            if (s < 0 || s >= _stagesRT.Count)
                return;
            var st = _stagesRT[s];
            for (int i = st.Holds.Count - 1; i >= 0; i--)
            {
                var n = st.Holds[i];
                st.Holds.RemoveAt(i);
                n.Held = false;
                n.Completed = true;
            }
        }


        /// <summary>场局部音符屏幕位置（触点几何 v1：Phigros 静态线/缺省事件、Cytus 归一化、其余投影）。</summary>
        PointF StageNoteScreenPos(StageRT st, Note n, PlayLayout L, double now)
        {
            switch (st.StageMode)
            {
                case GameMode.Phigros:
                {
                    double lineY = L.TopY + (L.H - L.TopY) * 0.5;
                    double lineX = L.PlayX + 0.5 * L.PlayW;
                    double kc = Math.Max(1, st.Kc);
                    double laneW = L.PlayW / kc;
                    double xc = n.Col >= 0 ? lineX + (n.Col - kc / 2.0 + 0.5) * laneW : lineX + (n.X - 0.5) * L.PlayW;
                    double y = lineY - (n.Time - now) * (lineY - L.TopY) * GameSettings.Speed / 1000.0;
                    return new PointF((float)xc, (float)y);
                }

                case GameMode.Cytus:
                {
                    double playW = L.AreaW, playX = L.AreaX;
                    double fieldY = L.AreaY + 6, fieldH = L.AreaH - 12;
                    return new PointF((float)(playX + n.X * playW), (float)(fieldY + n.Y * fieldH));
                }

                default:
                {
                    double laneW = L.PlayW / Math.Max(1, st.Kc);
                    double x = L.PlayX + (n.Col >= 0 ? (n.Col + 0.5) * laneW : n.X * L.PlayW);
                    double y = L.HitY - (n.Time - now) * L.Ppm;
                    return new PointF((float)x, (float)y);
                }
            }
        }


        /// <summary>多场触点：按场 rect 命中（数组逆序=后场在上层）→ 转场局部 → 最近音符（半径内）。</summary>
        void MultiTapAt(PointF pt, double now)
        {
            for (int s = _stagesRT.Count - 1; s >= 0; s--)
            {
                var st = _stagesRT[s];
                if (st.Spec == null)
                    continue;
                var r = StageRect(st.Spec);
                if (!r.Contains(pt))
                    continue;
                var mode = st.StageMode;
                if (!IsTouchMode(mode))
                    continue;
                var Ls = ComputeLayoutFor(r, st.Kc, false);
                double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
                double radius = mode == GameMode.Phigros ? 110 : 96;
                var local = new PointF(pt.X - r.X, pt.Y - r.Y);
                int i = StageLowerBound(st, now - lastW);
                double earlyLimit = now + lastW + 100;
                double bestD = double.MaxValue;
                Note best = null;
                for (; i < st.Notes.Count; i++)
                {
                    var n = st.Notes[i];
                    if (n.Time > earlyLimit)
                        break;
                    if (n.Judged)
                        continue;
                    if (n.Time < now - lastW - 100)
                        continue;
                    var p = StageNoteScreenPos(st, n, Ls, now);
                    double d = Math.Sqrt((p.X - local.X) * (p.X - local.X) + (p.Y - local.Y) * (p.Y - local.Y));
                    if (d < bestD)
                    {
                        bestD = d;
                        best = n;
                    }
                }

                if (best != null && bestD <= radius)
                {
                    int bi = Math.Max(0, Math.Min(15, best.Col));
                    _pressFlash[bi] = Math.Max(_pressFlash[bi], 1f);
                    Hit(best, now);
                }

                return;
            }
        }


        /// <summary>多场每帧推进：场 1..n 的漏判扫除 / 自动游玩 / 长条完成（场 0 由既有 Step 处理）。</summary>
        void MultiStageStep(double now)
        {
            if (!_multi || _stagesRT.Count <= 1)
                return;
            double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
            for (int s = 1; s < _stagesRT.Count; s++)
            {
                var st = _stagesRT[s];
                if (st.SweepIdx < st.Notes.Count)
                {
                    int i = st.SweepIdx;
                    while (i < st.Notes.Count && st.Notes[i].Time < now - lastW - 100)
                    {
                        var n = st.Notes[i];
                        if (!n.Judged)
                        {
                            st.Eng.Miss();
                            n.Judged = true;
                            n.Judgment = "MISS";
                        }

                        st.SweepIdx = ++i;
                    }
                }

                if (GameSettings.Autoplay && _playing && !_paused)
                {
                    while (st.AutoIdx < st.Notes.Count && st.Notes[st.AutoIdx].Time <= now)
                    {
                        var n = st.Notes[st.AutoIdx];
                        if (!n.Judged)
                            Hit(n, n.Time);
                        st.AutoIdx++;
                    }
                }

                for (int i = st.Holds.Count - 1; i >= 0; i--)
                {
                    var hn = st.Holds[i];
                    if (now >= hn.End)
                    {
                        st.Holds.RemoveAt(i);
                        hn.Held = false;
                        hn.Completed = true;
                    }
                }
            }
        }


        /// <summary>多场渲染主入口（PaintCore 多场分支）：背景一次 → 逐场裁剪绘制 → 边框/场名 → 合并 HUD。</summary>
        void PaintMultiStages(int W, int H, double now)
        {
            if (!EditLayoutMode && _bgD2D != null && GraphicsQuality.ShowBackgroundArt && Skin.ShowBackground)
            {
                float bw = _bgD2D.Width, bh = _bgD2D.Height;
                double sc = Math.Max((double)W / bw, (double)H / bh);
                float dw = (float)(bw * sc), dh = (float)(bh * sc);
                _d2d.DrawImage(_bgD2D, (float)((W - dw) / 2), (float)((H - dh) / 2), dw, dh, (float)(0.15 + 0.75 * Skin.BgDim));
                _d2d.FillRect(0, 0, W, H, Color.FromArgb((int)(30 + 150 * (1 - Skin.BgDim)), 10, 14, 22));
            }

            for (int i = 0; i < _stagesRT.Count; i++)
            {
                var st = _stagesRT[i];
                if (st.Spec == null)
                    continue;
                var r = StageRect(st.Spec);
                if (r.Width < 2 || r.Height < 2)
                    continue;
                _d2d.PushQuadLayer(r.X, r.Y, r.Right, r.Y, r.Right, r.Bottom, r.X, r.Bottom);
                try
                {
                    var prevNotes = _notes;
                    int prevKc = _kc;
                    _notes = st.Notes;
                    _kc = st.Kc;
                    try
                    {
                        DrawStageContents(st, (int)r.Width, (int)r.Height, now);
                    }
                    finally
                    {
                        _notes = prevNotes;
                        _kc = prevKc;
                    }
                }
                finally
                {
                    _d2d.PopLayer();
                }

                _d2d.DrawRect(r.X, r.Y, r.Width, r.Height, Color.FromArgb(90, 150, 190, 235), 1.2f);
                if (!string.IsNullOrEmpty(st.Spec.Name))
                    _d2d.Text("场 " + (i + 1) + " · " + st.Spec.Name, r.X + 8, r.Y + 4, r.Width - 16, 20, Color.FromArgb(200, 160, 200, 255), 11f);
            }

            _d2d.Text("Score " + (int)_score + " · ACC " + _acc.ToString("0.00") + "% · 多场同屏", W / 2f, 8, 600, 24, Color.White, 15f, true);
        }


        /// <summary>单场内容绘制（复用既有 Draw*；事件/锁定几何沿用主场上下文，v1 限制见契约）。</summary>
        void DrawStageContents(StageRT st, int w, int h, double now)
        {
            var Ls = ComputeLayoutFor(new RectangleF(0, 0, w, h), st.Kc, false);
            switch (st.StageMode)
            {
                case GameMode.Phigros:
                    DrawPhigros(w, h, now, Ls, Ls.TopY, st.Kc);
                    break;
                case GameMode.Arcaea:
                    DrawArcaea(w, h, now, Ls, Ls.TopY, Ls.HitY, st.Kc);
                    break;
                case GameMode.Cytus:
                    DrawCytus(w, h, now, Ls, Ls.TopY);
                    break;
                case GameMode.OsuStandard:
                    DrawOsuStandard(w, h, now, Ls);
                    break;
                case GameMode.Adofai:
                    DrawAdofai(w, h, now, Ls);
                    break;
                case GameMode.AdofaiReal:
                    DrawAdofaiReal(w, h, now, Ls);
                    break;
                case GameMode.Iidx:
                    DrawIidx(w, h, now, Ls, Ls.TopY, Ls.HitY);
                    break;
                case GameMode.Maimai:
                    DrawMaimai(w, h, now, Ls);
                    break;
                default:
                    DrawMania(w, h, now, Ls, Ls.TopY, Ls.HitY, Ls.PlayX, Ls.PlayW, st.Kc);
                    break;
            }
        }


        PlayLayout ComputeLayout()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            var area = PlayAreaRect();
            var l = new PlayLayout
            {
                H = H
            };
            l.AreaX = area.X;
            l.AreaY = area.Y;
            l.AreaW = area.Width;
            l.AreaH = area.Height;
            l.TopY = Math.Max(l.AreaY, 70); // 音符生成区顶部（HUD 徽标区之下；osu 框定时不越出框定区域）
            // 判定线位置：按皮肤 HitLineY（默认 0.82）——s=1 物理像素后不再需要高 DPI 可见区钳制
            double hitFrac = Math.Min(HitLineY, 0.94);
            l.HitY = Math.Max(l.TopY + 24, Math.Min(l.AreaY + l.AreaH - 24, l.AreaY + l.AreaH * hitFrac));
            l.RpW = EditLayoutMode ? EditPanelW : (ShowRightPanel ? 300 : 0);
            l.RpX = W - l.RpW;
            // 游玩区宽度：在框定区域内尽可能大（PlayScale=1.0 铺满区域宽；上限=区域宽，不再有 1200px 硬顶）
            l.PlayW = Math.Max(180, Math.Min(area.Width, l.RpX * PlayScale));
            // 谱面事件：zoom 缩放场地、noteSpeed 变速、scroll 反转下落方向（mania 系模式）
            double zoom = EvalEvents("zoom", RawMs() + GameSettings.Offset, 1.0);
            l.PlayW = Math.Max(120, Math.Min(area.Width, l.PlayW * Math.Max(0.5, Math.Min(1.5, zoom))));
            l.PlayX = area.X + (area.Width - l.PlayW) / 2;
            l.LaneW = l.PlayW / Math.Max(1, _kc);
            l.CenterX = area.X + area.Width / 2.0;
            l.CenterY = area.Y + area.Height / 2.0;
            double ns = EvalEvents("noteSpeed", RawMs() + GameSettings.Offset, 1.0);
            l.Ppm = (l.HitY - l.TopY) * GameSettings.Speed / 1000.0 * Math.Max(0.3, Math.Min(3.0, ns));
            l.ScrollDir = EvalEvents("scroll", RawMs() + GameSettings.Offset, 1.0) < 0 ? -1 : 1;
            return l;
        }


        /// <summary>音符判定点的屏幕位置（打击特效爆发点）。</summary>
        PointF HitPointFor(Note n, PlayLayout L)
        {
            switch (_chart?.Mode ?? GameMode.Mania)
            {
                case GameMode.Arcaea:
                {
                    bool sky = n.Col < 2;
                    double skyMove = EvalEvent("moveY", RawMs() + GameSettings.Offset, 0.18);
                    double skyY = L.TopY + 8 + Math.Clamp(skyMove, 0.0, 1.0) * (L.H * 0.40 - 8);
                    double lineY = sky ? skyY : L.HitY;
                    return new PointF((float)(L.PlayX + (n.Col + 0.5) * L.LaneW), (float)lineY);
                }

                case GameMode.Cytus:
                {
                    double playW = L.AreaW;
                    double playX = L.AreaX;
                    double fieldY = L.AreaY + 6;
                    double fieldH = L.AreaH - 12;
                    return new PointF((float)(playX + n.X * playW), (float)(fieldY + n.Y * fieldH));
                }

                case GameMode.Phigros:
                {
                    int ln = Math.Max(0, n.Line);
                    double moveY = EvalEventLine("moveY", RawMs() + GameSettings.Offset, 0.5, ln);
                    double moveX = EvalEventLine("moveX", RawMs() + GameSettings.Offset, 0.5, ln);
                    double rot = EvalEventLine("rotate", RawMs() + GameSettings.Offset, 0, ln) * Math.PI / 180.0;
                    double lineY = L.TopY + (L.H - L.TopY) * Math.Clamp(moveY, 0.02, 0.98);
                    double lineX = L.PlayX + moveX * L.PlayW;
                    double kc = Math.Max(1, _kc);
                    double laneW = L.PlayW / kc;
                    double xc = n.Col >= 0 ? lineX + (n.Col - kc / 2.0 + 0.5) * laneW : lineX + (n.X - 0.5) * L.PlayW;
                    double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
                    double dx = xc - lineX, dy = -8;
                    return new PointF((float)(lineX + dx * cosR - dy * sinR), (float)(lineY + dx * sinR + dy * cosR));
                }

                case GameMode.OsuStandard:
                {
                    var f = OsuField(L);
                    return new PointF(f.X + (float)(n.X * f.Width), f.Y + (float)(n.Y * f.Height));
                }

                case GameMode.Adofai:
                {
                    double ar = Math.Min(L.PlayW, L.H) * 0.34;
                    return new PointF((float)L.CenterX, (float)(L.CenterY + ar));
                }

                case GameMode.AdofaiReal:
                    return new PointF((float)(L.CenterX + Math.Min(L.PlayW, L.H) * 0.10), (float)L.CenterY);
                case GameMode.Iidx:
                    return new PointF((float)(L.PlayX + (n.Col + 0.5) * L.LaneW), (float)L.HitY);
                case GameMode.Maimai:
                {
                    MaimaiButtonPos(n, L, out var mx, out var my);
                    return new PointF((float)mx, (float)my);
                }

                default:
                    return new PointF((float)(L.PlayX + (n.Col + 0.5) * L.LaneW), (float)L.HitY);
            }
        }


        /// <summary>触屏类模式（原版为触摸屏玩法，不做定轨简化）：鼠标点击音符所在位置判定。</summary>
        bool IsTouchMode(GameMode m) => m == GameMode.Phigros || m == GameMode.Cytus || m == GameMode.Arcaea || m == GameMode.Maimai;

        /// <summary>音符当前屏幕位置（与对应 DrawXxx 完全同几何，供位置判定 / 打击特效 / 模拟点击）。</summary>
        PointF NoteScreenPos(Note n, PlayLayout L, double now)
        {
            switch (_chart?.Mode ?? GameMode.Mania)
            {
                case GameMode.Phigros:
                {
                    // 多判定线：按音符 Line 求值各自事件
                    int ln = Math.Max(0, n.Line);
                    double moveY = EvalEventLine("moveY", now, 0.5, ln);
                    double moveX = EvalEventLine("moveX", now, 0.5, ln);
                    double rot = EvalEventLine("rotate", now, 0, ln) * Math.PI / 180.0;
                    double lineY = L.TopY + (L.H - L.TopY) * Math.Clamp(moveY, 0.02, 0.98);
                    double lineX = L.PlayX + moveX * L.PlayW;
                    double kc = Math.Max(1, _kc);
                    double laneW = L.PlayW / kc;
                    double xc = n.Col >= 0 ? lineX + (n.Col - kc / 2.0 + 0.5) * laneW : lineX + (n.X - 0.5) * L.PlayW;
                    double y = lineY - (n.Time - now) * (lineY - L.TopY) * GameSettings.Speed / 1000.0;
                    double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
                    double dx = xc - lineX, dy = y - lineY;
                    return new PointF((float)(lineX + dx * cosR - dy * sinR), (float)(lineY + dx * sinR + dy * cosR));
                }

                case GameMode.Cytus:
                {
                    double playW = L.AreaW;
                    double playX = L.AreaX;
                    double fieldY = L.AreaY + 6;
                    double fieldH = L.AreaH - 12;
                    return new PointF((float)(playX + n.X * playW), (float)(fieldY + n.Y * fieldH));
                }

                case GameMode.Arcaea:
                {
                    // 3D 统一透视（与 DrawArcaea 同映射）：深度 z（0=消失点，zSky=天线/轨道远端，1=判定线，>1=更近）
                    ArcaeaSetup(L, now, out double cx, out double skyY, out double gndY, out double ppms);
                    double skyH = Math.Max(1, gndY - skyY);
                    double DepthOfRemain(double remain) => _arcPt0 / (_arcPt0 + Math.Max(-_arcPt0 * 0.98, remain));
                    double ProjX3(double x3d, double z) => _arcCx + (x3d - _arcCx) * z;
                    double ProjY3(double h, double z) => _arcPvpY + (gndY - _arcPvpY) * z - h * z;
                    double glaneW = ClientSize.Width * 0.62 / 4.0;
                    double X3dOf(double laneIdx) => _arcCx + (laneIdx - 2.0) * glaneW;
                    double X3dFree(double nx) => _arcCx + (nx - 0.5) * (4.0 * glaneW);
                    double ahead = _arcPt0 / _arcPk + 400;
                    if (n.Type == "arc")
                    {
                        double span = Math.Max(1e-6, n.End - n.Time);
                        double kk = Math.Max(0, Math.Min(1, (now - n.Time) / span));
                        int e0 = Math.Max(0, Math.Min(3, n.Col - 2));
                        int e1 = Math.Max(0, Math.Min(3, (n.EndCol >= 0 ? n.EndCol : n.Col) - 2));
                        double t = n.Time + (n.End - n.Time) * kk;
                        double z = DepthOfRemain(t - now);
                        double h = skyH * ArcaeaHeightAt(n, kk);
                        double x3d = ArcaeaXAt(n, kk, X3dOf(e0), X3dOf(e1), glaneW);
                        return new PointF((float)ProjX3(x3d, z), (float)ProjY3(h, z));
                    }

                    double x3d2 = X3dFree(n.X);
                    if (n.Col < 2)
                    {
                        // 天键 skytap/hold：空中，从更深处（zAntenna²、天空高处）下落到天线判定
                        double antennaY2 = _arcSkyY; // D12：随 moveY 浮动
                        double zAntenna2 = (antennaY2 - _arcPvpY) / (gndY - _arcPvpY);
                        double remain = n.Time - now;
                        double z = zAntenna2 * DepthOfRemain(remain);
                        double h = skyH * ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, remain / ahead));
                        return new PointF((float)ProjX3(x3d2, z), (float)ProjY3(h, z));
                    }

                    // 地键：地面平面（h=0，深度=时间映射）——与 DrawArcaea 地面斜轨一致
                    double tt = Math.Max(-_arcPt0 * 0.98, n.Time - now);
                    double z2 = _arcPt0 / (_arcPt0 + tt);
                    double lane = Math.Max(0, Math.Min(3, n.Col - 2));
                    return new PointF((float)ProjX3(X3dOf(lane + 0.5), z2), (float)ProjY3(0, z2));
                }

                default:
                    return HitPointFor(n, L);
            }
        }


        /// <summary>位置点击判定：窗口内距点击点最近的未判定音符（按当前绘制位置），命中；长条则按住。</summary>
        void TapAt(PointF pt, double now)
        {
            if (Replaying || DemoAi != null || GameSettings.Autoplay || !_playing || _paused)
                return;
            if (_chart == null)
                return;
            var mode = _chart.Mode;
            if (!IsTouchMode(mode))
                return;
            if (_multi)
            {
                MultiTapAt(pt, now);
                return;
            }

            var L = ComputeLayout();
            double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
            double radius = 96;
            if (mode == GameMode.Phigros)
                radius = 110;
            // Cytus 扫描线门控（实机：早触无效）；Phigros DRAG 按到判定线距离判定
            bool cytusGate = mode == GameMode.Cytus;
            double cytScanY = 0, cytGate = 0;
            if (cytusGate)
            {
                cytScanY = CytusScanY(now, L);
                cytGate = CytusGate(L, now);
            }

            bool phigrosDrag = mode == GameMode.Phigros;
            int phDragLine = -1;
            PointF phA = default, phB = default;
            int i = LowerBound(now - lastW);
            double earlyLimit = now + lastW + 100;
            double bestD = double.MaxValue;
            Note best = null;
            for (; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time > earlyLimit)
                    break;
                if (n.Judged)
                    continue;
                if (n.Time < now - lastW - 100)
                    continue;
                if (cytusGate && Math.Abs(cytScanY - CytusNoteY(n, L)) > cytGate)
                    continue;
                double d;
                if (phigrosDrag && n.Type == "drag")
                {
                    // 实机：DRAG 触到所属判定线即可（多判定线各算各的）
                    int nl = Math.Max(0, n.Line);
                    if (nl != phDragLine)
                    {
                        PhigrosLineSegment(now, L, nl, out phA, out phB);
                        phDragLine = nl;
                    }

                    d = DistToSegment(pt, phA, phB);
                }
                else
                {
                    var p = NoteScreenPos(n, L, now);
                    d = Math.Sqrt((p.X - pt.X) * (p.X - pt.X) + (p.Y - pt.Y) * (p.Y - pt.Y));
                }

                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }

            if (best == null || bestD > radius)
            {
                if (mode == GameMode.Phigros && _humanTest)
                    Logger.Info(string.Format("TapAtMiss: now={0:0} pt=({1:0},{2:0}) bestD={3:0} best={4} bestTime={5:0}", now, pt.X, pt.Y, bestD, best != null ? best.Col : -99,
                        best != null ? best.Time : -1));
                return;
            }

            int bi = Math.Max(0, Math.Min(15, best.Col));
            _pressFlash[bi] = Math.Max(_pressFlash[bi], 1f);
            Hit(best, now);
            if (best.Held && !_mouseHeld.Contains(best))
                _mouseHeld.Add(best); // 长条：按住鼠标跟随
        }


        /// <summary>松开鼠标：释放距松开点最近的鼠标按住长条（模拟器按位置释放）。</summary>
        void MouseReleaseHeld(PointF pt, double now)
        {
            if (_mouseHeld.Count == 0)
                return;
            var L = ComputeLayout();
            double bestD = 1e9;
            Note best = null;
            foreach (var n in _mouseHeld)
            {
                var p = NoteScreenPos(n, L, now);
                double d = Math.Sqrt((p.X - pt.X) * (p.X - pt.X) + (p.Y - pt.Y) * (p.Y - pt.Y));
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }

            if (best == null || bestD > 160)
                return;
            _mouseHeld.Remove(best);
            ReleaseHeld(best, now);
        }


        /// <summary>Cytus 页面时长(ms)：时刻 t 生效的页长——最近一次 speed 事件起生效（E-3 规格：事件生效时刻之后才改变线速/线位）。
        /// 无 speed 事件 = 默认 4 拍。事件值语义：每拍时长系数（×60000/BPM=1 拍）。</summary>
        double CytusPageMsAt(double t)
        {
            double bpm = _chart != null && _chart.Bpm > 0 ? _chart.Bpm : 120;
            double ms = 4 * 60000.0 / bpm;
            if (_chart == null || _chart.Events == null)
                return ms;
            foreach (var e in _chart.Events)
                if (e.Type == "speed" && e.Value > 0 && e.Time <= t)
                    ms = e.Value * 60000.0 / bpm;
            return ms;
        }


        /// <summary>Cytus 页定位：时刻 t 所在页索引与页内相位（页边界随 speed 事件累计漂移；页内相位 = 到达时刻的扫描进度）。
        /// 与 CytusScanY/音符扫过时刻同一几何（禁止两套公式）。</summary>
        void CytusPageAt(double t, out int pageIdx, out double phase)
        {
            double bpm = _chart != null && _chart.Bpm > 0 ? _chart.Bpm : 120;
            double ms = 4 * 60000.0 / bpm;
            double acc = 0;
            int idx = 0, guard = 0;
            while (acc + ms <= t && guard++ < 100000)
            {
                acc += ms;
                idx++;
                ms = CytusPageMsAt(acc); // 页边界处生效新页长（事件恰好落在边界）
            }

            // 事件落在页中间：该页长在事件时刻切换（页起点之后的事件立即生效）
            if (_chart != null && _chart.Events != null)
            {
                foreach (var e in _chart.Events)
                {
                    if (e.Type != "speed" || e.Value <= 0)
                        continue;
                    if (e.Time > acc && e.Time < acc + ms && e.Time <= t)
                    {
                        // 页内变速：相位 = 事件前进度 + 事件后进度（同一页内两段）
                        double before = (e.Time - acc) / ms;
                        double after = (t - e.Time) / (e.Value * 60000.0 / bpm);
                        phase = before + after;
                        pageIdx = idx;
                        return;
                    }
                }
            }

            pageIdx = idx;
            phase = (t - acc) / ms;
        }


        /// <summary>Cytus 页起点时刻：累计页长（speed 事件分段生效），返回第 pageIdx 页的起始时刻。</summary>
        double CytusPageStartMs(int pageIdx)
        {
            double bpm = _chart != null && _chart.Bpm > 0 ? _chart.Bpm : 120;
            double ms = 4 * 60000.0 / bpm;
            double acc = 0;
            for (int i = 0; i < pageIdx && i < 100000; i++)
            {
                acc += ms;
                ms = CytusPageMsAt(acc);
            }

            return acc;
        }


        /// <summary>Cytus 扫描线 Y（页拍速事件驱动：偶数页向下扫、奇数页向上扫，实机玩法核心）。</summary>
        double CytusScanY(double now, PlayLayout L)
        {
            double fieldY = L.AreaY + 6;
            double fieldH = L.AreaH - 12;
            CytusPageAt(now, out int pageIdx, out double phase);
            bool down = (pageIdx % 2) == 0;
            return down ? fieldY + phase * fieldH : fieldY + (1 - phase) * fieldH;
        }


        /// <summary>Cytus 判定门限：仅当扫描线到达音符附近才可命中（实机：早触无效，音符只在扫描线扫过时可打）。</summary>
        double CytusGate(PlayLayout L, double now)
        {
            double fieldH = L.AreaH - 12;
            double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
            double pageMs = CytusPageMsAt(now);
            return (lastW + 80) * (fieldH / pageMs) + 26; // 判定窗 × 线速 + 音符半径
        }


        /// <summary>Cytus 音符屏幕 Y（与 DrawCytus 同几何）。</summary>
        double CytusNoteY(Note n, PlayLayout L) => L.AreaY + 6 + n.Y * (L.AreaH - 12);

        /// <summary>Phigros 判定线当前线段（随 moveX/moveY/rotate 事件），供 DRAG 触碰判定。</summary>
        void PhigrosLineSegment(double now, PlayLayout L, int line, out PointF a, out PointF b)
        {
            double moveY = EvalEventLine("moveY", now, 0.5, line);
            double moveX = EvalEventLine("moveX", now, 0.5, line);
            double rot = EvalEventLine("rotate", now, 0, line) * Math.PI / 180.0;
            double lineY = L.TopY + (L.H - L.TopY) * Math.Clamp(moveY, 0.02, 0.98);
            double lineX = L.PlayX + moveX * L.PlayW;
            double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
            PointF Pt(double colX, double y)
            {
                double dx = colX - lineX, dy = y - lineY;
                return new PointF((float)(lineX + dx * cosR - dy * sinR), (float)(lineY + dx * sinR + dy * cosR));
            }

            a = Pt(L.PlayX - 20, lineY);
            b = Pt(L.PlayX + L.PlayW + 20, lineY);
        }


        static double DistToSegment(PointF p, PointF a, PointF b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2)) : 0;
            double dx = p.X - (a.X + abx * t), dy = p.Y - (a.Y + aby * t);
            return Math.Sqrt(dx * dx + dy * dy);
        }


        public void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = MonoMs() + 1200;
        } // t13 P2-3（captain 放行）：大谱 toast 宿主入口


        static Color Darken(Color c, float f)
        {
            float m = Math.Max(0, Math.Min(1, 1f - f));
            return Color.FromArgb(c.A, (int)(c.R * m), (int)(c.G * m), (int)(c.B * m));
        }


        static Color GradeColor(string g)
        {
            switch (g)
            {
                case "SSS":
                case "SS":
                    return Color.FromArgb(255, 215, 80);
                case "S":
                    return Color.FromArgb(220, 220, 230);
                case "A":
                    return Color.FromArgb(90, 160, 255);
                case "B":
                    return Color.FromArgb(90, 220, 140);
                case "C":
                    return Color.FromArgb(255, 210, 90);
                default:
                    return Color.FromArgb(150, 150, 150);
            }
        }


        /// <summary>判定爆字颜色（按各玩法实机配色还原；MISS 系全模式红色）。</summary>
        static Color JudgeColor(GameMode mode, string g)
        {
            switch (g)
            {
                // 全模式 MISS 系：红（osu!mania 默认素材实测 #CA3429；osu!standard 纯红 #FF0000）
                case "MISS":
                    if (mode == GameMode.OsuStandard)
                        return Color.FromArgb(255, 255, 0, 0);
                    return Color.FromArgb(255, 202, 52, 41);
                // Arcaea：PURE 青 #06e7f9 / FAR 金 / LOST 红 #f1004e
                case "PURE+":
                case "PURE":
                    return Color.FromArgb(255, 6, 231, 249);
                case "FAR":
                    return Color.FromArgb(255, 255, 213, 77);
                case "LOST":
                    return Color.FromArgb(255, 241, 0, 78);
                // Cytus：C.PERFECT/PERFECT 金、GOOD 蓝
                case "C.PERFECT":
                    return Color.FromArgb(255, 255, 213, 77);
                // Phigros：Perfect 金 / Good 蓝 / Bad 紫
                case "Bad":
                    return Color.FromArgb(255, 199, 125, 255);
                // ADOFAI：PURE 金 / PERFECT 青 / COUNTED 灰
                case "COUNTED":
                    return Color.FromArgb(255, 150, 150, 150);
                // IIDX：PGREAT/GREAT 金、GOOD 蓝、BAD 绿
                case "PGREAT":
                case "GREAT":
                    return Color.FromArgb(255, 255, 196, 0);
                case "BAD":
                    if (mode == GameMode.Iidx)
                        return Color.FromArgb(255, 102, 187, 106);
                    return Color.FromArgb(255, 109, 120, 134); // osu!mania BAD 灰蓝 #6D7886
                // osu!standard：300 白字+青 #37CFFF / 100 白字+绿 #5AE516 / 50 白字+金 #DDB045
                case "300":
                    return Color.FromArgb(255, 55, 207, 255);
                case "100":
                    return Color.FromArgb(255, 90, 229, 22);
                case "50":
                    return Color.FromArgb(255, 221, 176, 69);
                // Malody：Best 金 / Cool 青 / Good 绿
                case "BEST":
                    return Color.FromArgb(255, 255, 213, 77);
                case "COOL":
                    return Color.FromArgb(255, 77, 208, 225);
                // 通用（osu!mania 默认皮肤素材实测）：Marvelous/Perfect 金 #FFC000、Great 绿 #66C305、
                // Good 蓝 #1E67C4（Malody 谱面 Good 绿）、Ok/Meh 灰
                case "Marvelous":
                case "PERFECT":
                case "Perfect":
                    return Color.FromArgb(255, 255, 192, 0);
                case "Great":
                    return Color.FromArgb(255, 102, 195, 5);
                case "Good":
                    if (JudgeSettings.PresetKey.StartsWith("mal", StringComparison.OrdinalIgnoreCase))
                        return Color.FromArgb(255, 129, 199, 132);
                    return Color.FromArgb(255, 30, 103, 196);
                case "Ok":
                case "Meh":
                    return Color.FromArgb(255, 160, 160, 160);
                default:
                    return ColJudge;
            }
        }


        void SpawnConfetti(int W, int H)
        {
            var cols = new[]
            {
                Color.FromArgb(255, 215, 80),
                Color.FromArgb(110, 242, 160),
                Color.FromArgb(65, 166, 255),
                Color.FromArgb(255, 111, 216),
                Color.FromArgb(255, 179, 64)
            };
            for (int i = 0; i < 60 && _particles.Count < 400; i++)
            {
                float life = 4000 + (float)_rnd.NextDouble() * 1500;
                _particles.Add(new Particle { X = (float)(_rnd.NextDouble() * W), Y = (float)(-20 - _rnd.NextDouble() * 80), VX = (float)((_rnd.NextDouble() - 0.5) * 80),
                    VY = (float)(40 + _rnd.NextDouble() * 120), Life = life, MaxLife = life, Gravity = 70f, Color = cols[_rnd.Next(cols.Length)] });
            }
        }


        RectangleF ResultButtonRect(int W, int H, int idx)
        {
            float panelW = (float)Math.Min(W * 0.72, 900);
            float panelH = (float)Math.Min(H * 0.64, 580);
            float cx = W / 2f;
            float py = H / 2f - panelH / 2;
            float byBtn = py + panelH - 104;
            float bw = Math.Min(220, panelW * 0.4f);
            float bh = 44;
            float totalB = bw * 2 + 24;
            float startX = cx - totalB / 2;
            if (idx == 0)
                return new RectangleF(startX, byBtn, bw, bh);
            return new RectangleF(startX + bw + 24, byBtn, bw, bh);
        }


        void DrawHpBar(int W)
        {
            if (!DanActive || !JudgeSettings.DanHpEnabled || _resultPhase)
                return;
            float x = 12, y = 62, w = 280, h = 14;
            _d2d.FillRect(x, y, w, h, Color.FromArgb(200, 10, 14, 22));
            float frac = (float)Math.Max(0, Math.Min(1, _hp / JudgeSettings.HpMax));
            Color c = frac > 0.5 ? Color.FromArgb(255, 90, 220, 140) : frac > 0.2 ? Color.FromArgb(255, 255, 210, 90) : Color.FromArgb(255, 255, 90, 90);
            _d2d.FillRect(x + 1, y + 1, (w - 2) * frac, h - 2, c);
            _d2d.DrawRect(x, y, w, h, Color.FromArgb(120, 255, 255, 255), 1f);
            if (frac < 0.2 && (int)(MonoMs() / 200) % 2 == 0)
                _d2d.DrawRect(x - 2, y - 2, w + 4, h + 4, Color.FromArgb(255, 255, 90, 90), 2f);
            _d2d.Text("HP " + _hp.ToString("0"), x + w + 8, y + 7, 80, 16, c, 11f);
        }


        void DrawHitFx()
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                var r = _rings[i];
                float k = r.MaxLife > 0 ? 1f - r.Life / r.MaxLife : 1f;
                float rad = r.R0 + (r.R1 - r.R0) * k;
                float a = 1f - k;
                _d2d.DrawEllipse(r.X, r.Y, rad, rad, Color.FromArgb((int)(a * 210), r.Color.R, r.Color.G, r.Color.B), 2.6f - 1.4f * k);
            }

            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                float a = p.MaxLife > 0 ? Math.Max(0, Math.Min(1, p.Life / p.MaxLife)) : 0;
                float sz = 2.2f + 2.2f * (1 - a);
                _d2d.FillRect(p.X - sz / 2, p.Y - sz / 2, sz, sz, Color.FromArgb((int)(a * 255), p.Color.R, p.Color.G, p.Color.B));
            }
        }


        /// <summary>按线求值（Phigros 多判定线）：line≥0 取该线事件，line&lt;0 取全局事件。</summary>
        double EvalEventLine(string type, double t, double def, int line) => EvalEvents(type, t, def, line);

        double EvalEvent(string type, double t, double def) => EvalEvents(type, t, def, -1);

        /* ===================== 各模式渲染 ===================== */
        void DrawManiaPreview(int W, int H, int kc, double topY, double hitY, double playX, double playW)
        {
            double laneW = playW / Math.Max(1, kc);
            Color laneBg = Color.FromArgb(14, 20, 33);
            for (int i = 0; i < kc; i++)
                _d2d.FillQuad((float)(playX + i * laneW), (float)topY, (float)(playX + (i + 1) * laneW), (float)topY, (float)(playX + (i + 1) * laneW), (float)H, (float)(playX + i * laneW), (float)H,
                    laneBg);
            for (int i = 1; i < kc; i++)
            {
                double x = playX + i * laneW;
                _d2d.DrawLine((float)x, (float)topY, (float)x, (float)H, Color.FromArgb(45, 60, 90), 1f);
            }

            _d2d.DrawLine((float)playX, (float)hitY, (float)(playX + playW), (float)hitY, Skin.HitLineColor, Skin.HitLineThickness, Skin.HitLineStyle);
            if (EditLayoutMode)
            {
                Color gold = Color.FromArgb(255, 210, 63);
                _d2d.Text("📐 布局编辑器：拖动或右侧微调 · ESC 保存并退出", 16, 21, W - 40, 22, gold, 12f);
                foreach (var k in HudKeys)
                {
                    var box = HudBox(k);
                    _d2d.DrawRect(box.X, box.Y, box.Width, box.Height, gold, 2f);
                    _d2d.Text(k, box.X + 12, box.Y + 16, box.Width - 16, 20, Color.White, 12f);
                }

                _d2d.DrawLine((float)playX, (float)hitY, (float)(playX + playW), (float)hitY, gold, 4f);
                _d2d.Text("判定线（可上下拖动）", (float)playX, (float)(hitY + 17), 200, 18, gold, 10f);
            }
        }


        /// <summary>已移除玩法占位画面（保留注册表接口，供开发者将来重新接入）。</summary>
        void DrawUnsupported(int W, int H)
        {
            _d2d.FillRect(0, 0, W, H, Color.FromArgb(12, 14, 22));
            string modeName = _chart != null ? ModeSystem.DisplayName(_chart.Mode) : "";
            _d2d.Text("「" + modeName + "」玩法已移除", W / 2f - 300, H / 2f - 70, 600, 40, Color.FromArgb(255, 210, 63), 26f, true);
            _d2d.Text("注册表接口（ModeSystem）已保留，开发者可在未来重新接入该玩法", W / 2f - 340, H / 2f - 14, 680, 22, Color.FromArgb(190, 200, 215), 13f, true);
            _d2d.Text("按 ESC / 退格 返回", W / 2f - 200, H / 2f + 16, 400, 18, Color.FromArgb(140, 155, 175), 12f, true);
        }


        /* ===== NoteStyle 接入（t25：配色/描边/发光读 NoteStyleBook.Get(mode)，形状与几何不变） ===== */
        /// <summary>当前谱面模式的音符主色（NoteStyleBook）。</summary>
        Color NoteColCurrent() => ToGameColor(NoteStyleBook.Get(ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania)).NoteColor);

        /// <summary>当前谱面模式的强调色（hold/slide/次类型）。</summary>
        Color AccentColCurrent() => ToGameColor(NoteStyleBook.Get(ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania)).AccentColor);

        /// <summary>当前谱面模式的发光/接近色。</summary>
        Color GlowColCurrent() => ToGameColor(NoteStyleBook.Get(ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania)).GlowColor);

        static Color ToGameColor(RgbaColor c) => Color.FromArgb(255, Math.Max(0, Math.Min(255, (int)c.R)), Math.Max(0, Math.Min(255, (int)c.G)), Math.Max(0, Math.Min(255, (int)c.B)));

        void DrawMania(int W, int H, double now, PlayLayout L, double topY, double hitY, double playX, double playW, int kc)
        {
            bool use3d = GameSettings.Camera3D && !EditLayoutMode;
            if (use3d)
            {
                DrawMania3D(W, H, now, L, kc);
                return;
            }

            // osu!mania stable 默认皮肤 4K 配色 [1,2,2,1]：外列灰白 note1、内列粉 note2、
            // 转盘轨金色 noteS；receptor key1 浅紫灰 / key2 粉。斜轨（Malody 风格）保留白音符。
            Color OsuNoteCol(int c)
            {
                if (kc == 8 && c == 0)
                    return Color.FromArgb(255, 224, 192, 0); // noteS 金（IIDX 转盘不用此函数）
                bool outer = c == 0 || c == kc - 1 || (kc == 5 && c == 2);
                if (kc >= 5 && c == kc / 2)
                    return Color.FromArgb(255, 224, 192, 0); // 中轨金色（5K/7K 特殊轨）
                return outer ? Color.FromArgb(255, 183, 183, 183) : Color.FromArgb(255, 211, 138, 172); // note1 灰白 / note2 粉
            }

            Color OsuKeyCol(int c)
            {
                if (kc >= 5 && c == kc / 2)
                    return Color.FromArgb(255, 224, 192, 0); // keyS 金
                bool outer = c == 0 || c == kc - 1;
                return outer ? Color.FromArgb(255, 192, 176, 192) : Color.FromArgb(255, 192, 96, 144); // key1 浅紫灰 / key2 粉
            }

            double laneW = playW / kc;
            double ppms = (hitY - topY) * GameSettings.Speed / 1000.0;
            double dir = L.ScrollDir; // scroll 事件：-1 反转下落方向（音符从下往上）
            double slant = Math.Max(0, Math.Min(GraphicsQuality.SlantCap, Skin.Slant));
            if (!GameSettings.SlantEnabled)
                slant = 0; // 斜轨开关
            double pk = slant * 4.0;
            double pt0 = slant <= 0.01 ? 0 : (1 + pk) * 1000.0 / Math.Max(0.1, GameSettings.Speed);
            double pvpY = hitY - (hitY - topY) * (1 + pk);
            double pcenterX = playX + playW / 2.0;
            double YAt(double tRemain)
            {
                if (slant <= 0.01)
                    return hitY - dir * tRemain * ppms;
                double tt = Math.Max(-pt0 * 0.98, tRemain);
                return pvpY + (hitY - pvpY) * (pt0 / (pt0 + tt));
            }

            double DepthAt(double y)
            {
                if (slant <= 0.01)
                    return 1.0;
                double d = (y - pvpY) / (hitY - pvpY);
                return Math.Max(0.06, Math.Min(1.8, d));
            }

            double LaneL(int c, double y) => pcenterX + (c - kc / 2.0) * laneW * DepthAt(y);
            double NoteX(int c, double f, double y) => pcenterX + (c + f - kc / 2.0) * laneW * DepthAt(y);
            if (slant > 0.01)
            {
                _d2d.FillVerticalGradient((float)(playX - 60), (float)(pvpY - 56), (float)(playW + 120), 56f, Color.FromArgb(0, 255, 210, 120), Color.FromArgb(46, 255, 200, 90));
                _d2d.DrawLine((float)(playX - 40), (float)pvpY, (float)(playX + playW + 40), (float)pvpY, Color.FromArgb(60, 255, 210, 130), 2f);
            }

            // 轨道背景（实机亮度参照：FrameStat 实测实机 Mania 帧 61/40/30 暖调 vs 程序 16/20/31 过暗——
            // 提亮 2 倍并偏暖；实机 stable 皮肤轨道为浅灰蓝底+细分隔线）
            Color laneBg = Color.FromArgb(38, 44, 62);
            for (int i = 0; i < kc; i++)
            {
                _d2d.FillQuad((float)LaneL(i, topY), (float)topY, (float)LaneL(i + 1, topY), (float)topY, (float)LaneL(i + 1, H), (float)H, (float)LaneL(i, H), (float)H, laneBg);
            }

            for (int i = 1; i < kc; i++)
            {
                _d2d.DrawLine((float)LaneL(i, topY), (float)topY, (float)LaneL(i, hitY), (float)hitY, Color.FromArgb(90, 110, 150), 1f);
                _d2d.DrawLine((float)LaneL(i, hitY), (float)hitY, (float)LaneL(i, H), (float)H, Color.FromArgb(90, 110, 150), 1f);
            }

            // 按键 / 命中闪光（渐变竖条）
            if (GraphicsQuality.BurstEffects && Skin.BurstEffects)
            {
                for (int c = 0; c < kc; c++)
                {
                    float flash = _pressFlash[c], burst = _burst[c];
                    if (flash <= 0.01f && burst <= 0.01f)
                        continue;
                    float a = Math.Min(0.9f, 0.28f * flash + 0.5f * burst);
                    _d2d.FillQuad((float)LaneL(c, topY), (float)topY, (float)LaneL(c + 1, topY), (float)topY, (float)LaneL(c + 1, hitY), (float)hitY, (float)LaneL(c, hitY), (float)hitY,
                        Color.FromArgb((int)(a * 255), 255, 255, 255));
                }
            }

            double ahead, below;
            if (slant > 0.01)
            {
                ahead = pt0 / pk + 400;
                below = pt0 * (1 - (hitY - pvpY) / Math.Max(1, H - pvpY)) + 500;
            }
            else
            {
                below = (H - topY + NoteThickness * 2) / (ppms > 0.01 ? ppms : 0.5);
                ahead = (hitY - topY) / (ppms > 0.01 ? ppms : 0.5) + 300;
            }

            // hold 长条
            double holdCut = now - below - 300;
            while (_holdIdx < _holdNotes.Count && _holdNotes[_holdIdx].End < holdCut)
                _holdIdx++;
            for (int hi = _holdIdx; hi < _holdNotes.Count; hi++)
            {
                var n = _holdNotes[hi];
                if (n.Time - now > ahead)
                    break;
                if (n.Judged && n.Judgment == "MISS" && now > n.End)
                    continue;
                double yHead = YAt(n.Time - now);
                double yTail = YAt(n.End - now);
                double y1 = Math.Max(yTail, pvpY + 6), y2 = Math.Min(yHead, H);
                if (y2 <= y1)
                    continue;
                var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                int baseA = (int)(120 * Math.Max(0.1, Math.Min(1, Skin.HoldAlpha)));
                double xl1 = NoteX(n.Col, 0.3, y1), xr1 = NoteX(n.Col, 0.7, y1);
                double xl2 = NoteX(n.Col, 0.3, y2), xr2 = NoteX(n.Col, 0.7, y2);
                // hold 长条：白色半透明（osu!mania 经典），轨道色描边
                if (Skin.HoldStyle == 1 && GraphicsQuality.HoldGlowStyle)
                    _d2d.FillQuad((float)NoteX(n.Col, 0.24, y1), (float)y1, (float)NoteX(n.Col, 0.76, y1), (float)y1, (float)NoteX(n.Col, 0.76, y2), (float)y2, (float)NoteX(n.Col, 0.24, y2),
                        (float)y2, Color.FromArgb(baseA / 2, 255, 255, 255));
                if (Skin.HoldStyle == 2)
                {
                    var lc = Color.FromArgb(Math.Min(255, baseA + 60), 255, 255, 255);
                    _d2d.DrawLine((float)xl1, (float)y1, (float)xl2, (float)y2, lc, 2f);
                    _d2d.DrawLine((float)xr1, (float)y1, (float)xr2, (float)y2, lc, 2f);
                    if (Math.Abs(yHead - y1) < 4)
                        _d2d.DrawLine((float)xl1, (float)y1, (float)xr1, (float)y1, lc, 2f);
                    if (Math.Abs(yTail - y2) < 4)
                        _d2d.DrawLine((float)xl2, (float)y2, (float)xr2, (float)y2, lc, 2f);
                }
                else
                    _d2d.FillQuad((float)xl1, (float)y1, (float)xr1, (float)y1, (float)xr2, (float)y2, (float)xl2, (float)y2, Color.FromArgb(baseA, 255, 255, 255));
                // hold 头部白色方块（与 tap 同款）
                double hTop = y2 - NoteThickness * 0.9;
                _d2d.FillRoundedRect((float)NoteX(n.Col, 0.06, hTop), (float)hTop, (float)(NoteX(n.Col, 0.94, hTop) - NoteX(n.Col, 0.06, hTop)), (float)(NoteThickness * 0.9), 3,
                    AccentColCurrent()); // t25：hold 头强调色
                _d2d.DrawRoundedRect((float)NoteX(n.Col, 0.06, hTop), (float)hTop, (float)(NoteX(n.Col, 0.94, hTop) - NoteX(n.Col, 0.06, hTop)), (float)(NoteThickness * 0.9), 3, Color.FromArgb(140,
                    200, 205, 215), 1.5f);
            }

            // tap 音符
            int t0 = LowerBound(now - below);
            for (int i = t0; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time - now > ahead)
                    break;
                if (IsHoldNote(n) || n.Judged)
                    continue;
                double yHead = YAt(n.Time - now);
                double th = NoteThickness * (slant > 0.01 ? (0.45 + 0.55 * Math.Min(1, DepthAt(yHead))) : 1.0);
                if (yHead < pvpY - th || yHead > H + th)
                    continue;
                var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                double judgeFrac = GameSettings.JudgeBase switch
                {
                    0 => 1.0,
                    2 => 0.0,
                    _ => 0.5
                };
                double yTop = yHead - th * judgeFrac;
                double yBot = yTop + th;
                // 音符：白色圆角竖条（osu!mania 经典白音符；斜轨时保留轨道色梯形便于透视）
                double xTL = NoteX(n.Col, 0.06, yTop), xTR = NoteX(n.Col, 0.94, yTop);
                double xBL = NoteX(n.Col, 0.06, yBot), xBR = NoteX(n.Col, 0.94, yBot);
                if (slant <= 0.01)
                {
                    // osu!mania 默认皮肤：音符 = 列色渐变条（外灰白/内粉）+ 底边 1-2px 白色亮条
                    var body = OsuNoteCol(n.Col);
                    _d2d.FillRoundedRect((float)xTL, (float)yTop, (float)(xTR - xTL), (float)(yBot - yTop), 3, body);
                    _d2d.DrawRoundedRect((float)xTL, (float)yTop, (float)(xTR - xTL), (float)(yBot - yTop), 3, Color.FromArgb(140, 255, 255, 255), 1.2f);
                    _d2d.FillRoundedRect((float)xTL, (float)(yBot - 3), (float)(xTR - xTL), 3, 1.5f, Color.White);
                }
                else
                {
                    // 斜轨整体吸附：音符上下边缘都按该深度处的轨道宽度取梯形（Malody 风格）
                    _d2d.FillQuad((float)xTL, (float)yTop, (float)xTR, (float)yTop, (float)xBR, (float)yBot, (float)xBL, (float)yBot, col);
                }
            }

            // 判定线
            if (GraphicsQuality.HitLineGlow && Skin.HitLineGlow > 0)
            {
                int ga = Math.Min(200, 30 + Skin.HitLineGlow * 5);
                _d2d.DrawLine((float)playX, (float)hitY, (float)(playX + playW), (float)hitY, Color.FromArgb(ga, Skin.HitLineColor.R, Skin.HitLineColor.G, Skin.HitLineColor.B),
                    Skin.HitLineThickness + Skin.HitLineGlow / 2f + 4, Skin.HitLineStyle);
            }

            _d2d.DrawLine((float)playX, (float)hitY, (float)(playX + playW), (float)hitY, Skin.HitLineColor, Skin.HitLineThickness, Skin.HitLineStyle);
            // 判定线下彩色键位（osu!mania 默认 receptor：key1 浅紫灰 / key2 粉 / keyS 金）
            for (int c = 0; c < kc; c++)
            {
                var kc2 = OsuKeyCol(c);
                double kx = NoteX(c, 0.12, hitY + 10), kx2 = NoteX(c, 0.88, hitY + 10);
                double flash = _pressFlash[c];
                _d2d.FillRoundedRect((float)kx, (float)(hitY + 4), (float)(kx2 - kx), 16, 4, Color.FromArgb(200, kc2.R, kc2.G, kc2.B));
                _d2d.DrawRoundedRect((float)kx, (float)(hitY + 4), (float)(kx2 - kx), 16, 4, Color.FromArgb(flash > 0.1f ? (int)(255 * flash) : 90, 255, 255, 255), 2f);
            }
        }


        void DrawMania3D(int W, int H, double now, PlayLayout L, int kc)
        {
            double cx = L.PlayX + L.PlayW / 2.0;
            _cam.CenterX = cx;
            _cam.CenterY = L.HitY;
            _cam.Pitch = GameSettings.CameraPitch;
            _cam.Yaw = GameSettings.CameraYaw;
            _cam.Depth = GameSettings.CameraDepth;
            _cam.Fov = 60;
            double ppms = L.Ppm;
            double topWorldY = L.TopY - L.HitY;
            double botWorldY = H - L.HitY;
            Color laneBg = Color.FromArgb(16, 22, 36);
            for (int i = 0; i < kc; i++)
            {
                double x0 = L.PlayX + i * L.LaneW - cx;
                double x1 = L.PlayX + (i + 1) * L.LaneW - cx;
                var p0 = P(_cam.Project(x0, topWorldY, 0));
                var p1 = P(_cam.Project(x1, topWorldY, 0));
                var p2 = P(_cam.Project(x1, botWorldY, 0));
                var p3 = P(_cam.Project(x0, botWorldY, 0));
                _d2d.FillQuad(p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, laneBg);
            }

            for (int i = 1; i < kc; i++)
            {
                double x = L.PlayX + i * L.LaneW - cx;
                var a = P(_cam.Project(x, topWorldY, 0));
                var b = P(_cam.Project(x, 0, 0));
                var c2 = P(_cam.Project(x, botWorldY, 0));
                _d2d.DrawLine(a.X, a.Y, b.X, b.Y, Color.FromArgb(45, 60, 90), 1f);
                _d2d.DrawLine(b.X, b.Y, c2.X, c2.Y, Color.FromArgb(45, 60, 90), 1f);
            }

            double ahead = (L.HitY - L.TopY) / (ppms > 0.01 ? ppms : 0.5) + 300;
            double below = (H - L.TopY + NoteThickness * 2) / (ppms > 0.01 ? ppms : 0.5);
            // ===== 远→近深度排序绘制（远处先画，判定线最后画在最上层）=====
            _mania3DQuads.Clear();
            // hold：沿时间细分 8 段，保证透视弯曲；每段按平均深度入列
            double holdCut = now - below - 300;
            while (_holdIdx < _holdNotes.Count && _holdNotes[_holdIdx].End < holdCut)
                _holdIdx++;
            for (int hi = _holdIdx; hi < _holdNotes.Count; hi++)
            {
                var n = _holdNotes[hi];
                if (n.Time - now > ahead)
                    break;
                if (n.Judged && n.Judgment == "MISS" && now > n.End)
                    continue;
                var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                int baseA = (int)(120 * Math.Max(0.1, Math.Min(1, Skin.HoldAlpha)));
                double wx = L.PlayX + (n.Col + 0.5) * L.LaneW - cx;
                double hw = L.LaneW * 0.34; // 与 2D 长条宽度一致（0.32~0.68 轨道宽）
                int SEG = 8;
                for (int s = 0; s < SEG; s++)
                {
                    double t1 = n.Time + (n.End - n.Time) * s / SEG;
                    double t2 = n.Time + (n.End - n.Time) * (s + 1) / SEG;
                    double tr1 = t1 - now, tr2 = t2 - now;
                    double wy1 = -tr1 * ppms, wz1 = tr1 * ppms;
                    double wy2 = -tr2 * ppms, wz2 = tr2 * ppms;
                    if (wy2 > H - L.HitY + 200 || wy1 < L.TopY - L.HitY - 200)
                        continue;
                    float fog = (float)(Math.Round(_cam.Fog(Math.Max(0, (wz1 + wz2) / 2)) * 32.0) / 32.0); // t16：32 级雾化量化（批量分组，视觉近似无损）
                    var pL1 = P(_cam.Project(wx - hw, wy1, wz1));
                    var pR1 = P(_cam.Project(wx + hw, wy1, wz1));
                    var pR2 = P(_cam.Project(wx + hw, wy2, wz2));
                    var pL2 = P(_cam.Project(wx - hw, wy2, wz2));
                    var c1 = Darken(col, fog);
                    double z = (wz1 + wz2) / 2;
                    _mania3DQuads.Add(new M3Quad { x1 = pL1.X, y1 = pL1.Y, x2 = pR1.X, y2 = pR1.Y, x3 = pR2.X, y3 = pR2.Y, x4 = pL2.X, y4 = pL2.Y, argb = Color.FromArgb(baseA, c1.R, c1.G,
                        c1.B).ToArgb(), z = z });
                }
            }

            // tap：四角真投影（整块贴合 3D 轨道方向），按深度入列
            int t0 = LowerBound(now - below);
            for (int i = t0; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time - now > ahead)
                    break;
                if (IsHoldNote(n) || n.Judged)
                    continue;
                double tr = n.Time - now;
                double wy = -tr * ppms, wz = tr * ppms;
                if (wy > H - L.HitY + 200 || wy < L.TopY - L.HitY - 200)
                    continue;
                double wx = L.PlayX + (n.Col + 0.5) * L.LaneW - cx;
                double hw = L.LaneW * 0.44;
                double hh = NoteThickness * 0.5;
                var pTL = P(_cam.Project(wx - hw, wy - hh, wz));
                var pTR = P(_cam.Project(wx + hw, wy - hh, wz));
                var pBR = P(_cam.Project(wx + hw, wy + hh, wz));
                var pBL = P(_cam.Project(wx - hw, wy + hh, wz));
                var fogT = (float)(Math.Round(_cam.Fog(Math.Max(0, wz)) * 32.0) / 32.0); // t16：32 级雾化量化
                var col = Darken(NoteColCurrent(), fogT); // t25：主色读 NoteStyleBook
                _mania3DQuads.Add(new M3Quad { x1 = pTL.X, y1 = pTL.Y, x2 = pTR.X, y2 = pTR.Y, x3 = pBR.X, y3 = pBR.Y, x4 = pBL.X, y4 = pBL.Y, argb = col.ToArgb(), z = wz });
            }

            // 远 → 近
            _mania3DQuads.Sort((a, b) => b.z.CompareTo(a.z));
            if (!_multi)
                _d2d.BeginFillBatch(); // t17：多场/图层嵌套路径回退旧直绘（批量仅热路径生效）
            foreach (var q in _mania3DQuads)
                _d2d.FillQuad(q.x1, q.y1, q.x2, q.y2, q.x3, q.y3, q.x4, q.y4, Color.FromArgb(q.argb));
            if (!_multi)
                _d2d.FlushFillBatch();
            // 判定线（z=0, y=0）最后绘制，始终在音符上层
            var hl0 = P(_cam.Project(L.PlayX - cx, 0, 0));
            var hl1 = P(_cam.Project(L.PlayX + L.PlayW - cx, 0, 0));
            if (GraphicsQuality.HitLineGlow && Skin.HitLineGlow > 0)
            {
                int ga = Math.Min(200, 30 + Skin.HitLineGlow * 5);
                _d2d.DrawLine(hl0.X, hl0.Y, hl1.X, hl1.Y, Color.FromArgb(ga, Skin.HitLineColor.R, Skin.HitLineColor.G, Skin.HitLineColor.B), Skin.HitLineThickness + Skin.HitLineGlow / 2f + 4,
                    Skin.HitLineStyle);
            }

            _d2d.DrawLine(hl0.X, hl0.Y, hl1.X, hl1.Y, Skin.HitLineColor, Skin.HitLineThickness, Skin.HitLineStyle);
        }


        void DrawPhigros(int W, int H, double now, PlayLayout L, double topY, int kc)
        {
            if (!_multi)
                _d2d.BeginFillBatch(); // GPU 满载优化：同色四边形合并进一个 PathGeometry（每帧只三角化几次；t17：多场路径回退直绘）
            // ===== 多判定线（PhiMaker 式）：各线拥有独立 moveX/moveY/rotate/alpha 事件，音符按 Line 归属；
            // 实机所有判定线同色（白），数量无上限（性能内 64 条封顶） =====
            int lineCount = _phigLineCount;
            if (lineCount < 1)
            {
                lineCount = 1;
                foreach (var n in _notes)
                    if (n.Line >= lineCount)
                        lineCount = n.Line + 1;
                if (_chart != null && _chart.Events != null)
                    foreach (var ev in _chart.Events)
                        if (ev.Line >= lineCount)
                            lineCount = ev.Line + 1;
                lineCount = Math.Max(1, Math.Min(64, lineCount));
                _phigLineCount = lineCount;
            }

            for (int line = 0; line < lineCount; line++)
            {
                // 实机（RPE/phi 逻辑坐标系 1920x1080）：判定线默认 y=810/1080=0.75（距顶 75%）
                double moveY = EvalEventLine("moveY", now, 0.75, line);
                double moveX = EvalEventLine("moveX", now, 0.5, line);
                double rot = EvalEventLine("rotate", now, 0, line) * Math.PI / 180.0;
                // alpha：RPE/官方谱事件值为 0-255 整数（如 240），归一为 0-1（>1 视为 0-255；实机默认 1）
                double alphaRaw = EvalEventLine("alpha", now, 1.0, line);
                double alpha = Math.Max(0.0, Math.Min(1.0, alphaRaw > 1 ? alphaRaw / 255.0 : alphaRaw));
                alpha = Math.Max(0.05, alpha);
                double lineY = topY + (H - topY) * Math.Clamp(moveY, 0.02, 0.98);
                double lineX = L.PlayX + moveX * L.PlayW;
                double laneW = L.PlayW / kc;
                double cosR = Math.Cos(rot), sinR = Math.Sin(rot);
                // 判定线（纯白，带柔和辉光；随 rotate 事件绕线心旋转，Phigros 实机动态线）
                // 实机：白色细线（约屏高 0.2–0.3%）+ 柔和外辉光，无描边、无渐变
                var glow = Color.FromArgb((int)(60 * alpha), 255, 255, 255);
                var lnA = PhigrosPt(L.PlayX - 20, lineY);
                var lnB = PhigrosPt(L.PlayX + L.PlayW + 20, lineY);
                _d2d.DrawLine(lnA.X, lnA.Y, lnB.X, lnB.Y, glow, 7f);
                _d2d.DrawLine(lnA.X, lnA.Y, lnB.X, lnB.Y, Color.FromArgb((int)(255 * alpha), 255, 255, 255), 2.2f);
                double ppms = (lineY - topY) * GameSettings.Speed / 1000.0;
                double ahead = (lineY - topY) / (ppms > 0.01 ? ppms : 0.5) + 300;
                double below = (H - topY + NoteThickness * 2) / (ppms > 0.01 ? ppms : 0.5);
                // speed 事件场：音符视觉位置 = 对 speed 场积分（判定线不动、音符变速的实机语义）。
                // 关键帧在 ResetState 时构建一次（_spdFieldCache），这里二分前缀积分 O(logN) 查询。
                double Disp(double t0, double t1) // speed 场积分（虚拟时间差，ms×倍率）
                {
                    return SpdDispCached(line, t0, t1);
                }

                PointF PhigrosPt(double colX, double y)
                {
                    double dx = colX - lineX, dy = y - lineY;
                    return new PointF((float)(lineX + dx * cosR - dy * sinR), (float)(lineY + dx * sinR + dy * cosR));
                }

                // 背景滚动网格（仅主线：网格随节拍向判定线流动，营造下落速度感）
                if (line == 0)
                {
                    double bpm = _chart != null && _chart.Bpm > 0 ? _chart.Bpm : 120;
                    double gridMs = 60000.0 / bpm / 4.0; // 16 分音符一行
                    double spacing = ppms * gridMs;
                    if (spacing > 5)
                    {
                        double off = (now % gridMs) / gridMs * spacing;
                        for (double gy = lineY - off; gy > topY - spacing; gy -= spacing)
                        {
                            var a = PhigrosPt(L.PlayX - 20, gy);
                            var b = PhigrosPt(L.PlayX + L.PlayW + 20, gy);
                            _d2d.DrawLine(a.X, a.Y, b.X, b.Y, Color.FromArgb((int)(30 * alpha), 140, 170, 220), 1f);
                        }
                    }
                }

                // 音符沿判定线的位置：Col>=0 按轨道列；Col=-1 按自由位置 X（编辑器无轨放置）
                double NoteXc(Note n) => n.Col >= 0 ? lineX + (n.Col - kc / 2.0 + 0.5) * laneW : lineX + (n.X - 0.5) * L.PlayW;
                // 音符尺寸（用户标准：横置——长轴平行判定线，宽:高≈4.2:1；实机 ≈0.08W 屏宽，用户"太细"已加粗）
                double tapW = W * 0.075, tapH = tapW * 0.24;
                double dragW = tapW * 0.72, dragH = tapH * 0.55;
                double flickW = tapW * 1.7, flickH = tapH;
                // 线局部坐标四边形（u=沿线方向，v=沿线法线，v 减小=远离判定线；随线旋转）
                bool axisAligned = cosR > 0.9999 && Math.Abs(sinR) < 0.0001; // 无旋转：轴对齐矩形（D2D FillRect 快速路径）
                void LineQuad(double u0, double v0, double u1, double v1, Color c)
                {
                    if (axisAligned)
                    {
                        // rot=0：屏幕轴对齐矩形（Phigros 多数谱面无 rotate 事件）——GPU 原生矩形，免 tessellation
                        double x0 = lineX + u0, x1 = lineX + u1;
                        double y0 = lineY + v0, y1 = lineY + v1;
                        _d2d.FillRect((float)Math.Min(x0, x1), (float)Math.Min(y0, y1), (float)Math.Abs(x1 - x0), (float)Math.Abs(y1 - y0), c);
                        return;
                    }

                    var a = PhigrosPt(lineX + u0, lineY + v0);
                    var b = PhigrosPt(lineX + u1, lineY + v0);
                    var d = PhigrosPt(lineX + u1, lineY + v1);
                    var e = PhigrosPt(lineX + u0, lineY + v1);
                    _d2d.FillQuad(a.X, a.Y, b.X, b.Y, d.X, d.Y, e.X, e.Y, c);
                }

                // 屏幕可见性：v 落在 [-(ahead)*ppms, (H-lineY)] 附近才画
                bool Visible(double v0, double v1)
                {
                    var p0 = PhigrosPt(lineX, lineY + v0);
                    var p1 = PhigrosPt(lineX, lineY + v1);
                    return !(p0.Y < topY - tapH * 2 && p1.Y < topY - tapH * 2) && !(p0.Y > H + tapH * 2 && p1.Y > H + tapH * 2);
                }

                // hold（用户标准：横置——头 TAP 同款白色圆角块压线 + 身横向延伸，长轴平行判定线）
                double holdCut = now - below - 300;
                while (_holdIdx < _holdNotes.Count && _holdNotes[_holdIdx].End < holdCut)
                    _holdIdx++;
                for (int hi = _holdIdx; hi < _holdNotes.Count; hi++)
                {
                    var n = _holdNotes[hi];
                    if (n.Line != line)
                        continue;
                    if (n.Time - now > ahead)
                        break;
                    if (n.Judged && n.Judgment == "MISS" && now > n.End)
                        continue;
                    double xc = NoteXc(n);
                    double vHead = Math.Min(0, -Disp(now, n.Time) * ppms); // 按住期间头停在线处（speed 场积分定位）
                    double vTail = Math.Min(0, -Disp(now, n.End) * ppms); // 尾在最远处（speed 场积分定位）
                    double bodyTop = vHead - tapH * 0.9; // 头（TAP 块）顶端
                    double bodyLen = bodyTop - vTail;
                    if (bodyLen <= 0)
                        continue;
                    double wu = tapW * 0.5;
                    if (!Visible(bodyTop, vTail))
                        continue;
                    // 横置：身=横向宽扁条（高≈tapH，沿 X 由头向远端延伸——长轴平行判定线，用户标准"横着才对"）
                    double bodyH = tapH;
                    double bodyX = xc - wu; // 头左缘
                    // 头：TAP 同款白色圆角横块（压线端）
                    LineQuad(xc - wu, vHead, xc + wu, vHead - tapH * 0.9, Color.FromArgb(255, 255, 255, 255));
                    // 身：横向延伸（带宽=bodyH 高、横向长度随 bodyLen/ppms 时间方向近端渐隐）
                    double ext = Math.Min(bodyLen, tapW * 1.2);
                    LineQuad(xc - wu - ext, vHead, xc - wu, vHead - bodyH, Color.FromArgb(88, 255, 255, 255));
                    // 尾：平切端盖（远端）
                    LineQuad(xc - wu - ext - tapH * 0.16, vHead, xc - wu - ext, vHead - bodyH, Color.FromArgb(110, 255, 255, 255));
                }

                // tap / drag / flick（实机：底边压线判定时刻 = 底缘触线）
                int t0 = LowerBound(now - below);
                for (int i = t0; i < _notes.Count; i++)
                {
                    var n = _notes[i];
                    if (n.Line != line)
                        continue;
                    if (n.Time - now > ahead)
                        break;
                    if (IsHoldNote(n) || n.Judged)
                        continue;
                    double xc = NoteXc(n);
                    double vBot = -Disp(now, n.Time) * ppms; // 底边位置（触线时刻 vBot=0；speed 场积分定位）
                    double vTop = vBot - tapH;
                    if (n.Type == "drag")
                    {
                        // DRAG：小型白色半透明圆角横条（无需精确点击，实机 ~20–30% 不透明）
                        if (!Visible(vBot, vBot - dragH))
                            continue;
                        LineQuad(xc - dragW / 2, vBot, xc + dragW / 2, vBot - dragH, Color.FromArgb(64, 255, 255, 255));
                        continue;
                    }

                    if (n.Type == "flick")
                    {
                        // FLICK：红色宽条 + 白色箭头（指向滑动方向 = 远离判定线）
                        if (!Visible(vBot, vBot - flickH))
                            continue;
                        var red = AccentColCurrent(); // t25：强调色读 NoteStyleBook（原 #FF3B30 实机亮红）
                        LineQuad(xc - flickW / 2, vBot, xc + flickW / 2, vBot - flickH, red);
                        // 白箭头（三角，尖端朝远离线方向）
                        var tip = PhigrosPt(lineX + xc, lineY + vBot - flickH * 0.82);
                        var bl = PhigrosPt(lineX + xc - flickW * 0.30, lineY + vBot - flickH * 0.20);
                        var br = PhigrosPt(lineX + xc + flickW * 0.30, lineY + vBot - flickH * 0.20);
                        _d2d.FillPolygon(new PointF[] { tip, bl, br }, Color.White);
                        continue;
                    }

                    // TAP：白色圆角竖条，底边压线（实机白胶囊：顶圆底平）
                    if (!Visible(vBot, vTop))
                        continue;
                    LineQuad(xc - tapW / 2, vBot, xc + tapW / 2, vTop, NoteColCurrent()); // t25：tap 主色
                }
            }

            if (!_multi)
                _d2d.FlushFillBatch(); // t17：多场路径回退直绘（与 Begin 门配对）
        }


        double _arcGndY, _arcSkyY, _arcCx, _arcTilt; // Arcaea 2D 平面透视参数（当前帧）

        double _arcPk, _arcPt0, _arcPvpY; // mania 斜轨参数（地面部分：地线=近处 1.0 深度，消失点在天线上方）

        /// <summary>Arcaea 天键高度比（n.Y 0..1：0=地面、1=天空顶/天空线；NaN/缺失回退 1.0）。
        /// 天空键自由放置——渲染/命中统一按此比例（与编辑器 ArcHeightFromXY 同语义）。</summary>
        static double ArcaeaSkyHeightRatio(Note n) => n == null || double.IsNaN(n.Y) ? 1.0 : Math.Max(0, Math.Min(1, n.Y));

        /// <summary>Arcaea 场地：2D 平面透视（用户要求非 3D 相机）——地线=底部 4K 透视轨道，天线在上。</summary>
        void ArcaeaSetup(PlayLayout L, double now, out double cx, out double skyY, out double gndY, out double ppms)
        {
            gndY = L.HitY; // 地线（底部判定线）
            // t63 B2：field.ground 仅覆盖时生效（否则沿用 L.HitY=hitline 全局——避免破坏自定义 HitLineY）；field.sky 天线位置覆盖（默认 0.55 与现状一致）
            var fgE = LC_Get("field.ground");
            bool fgOv = Skin.ModeLayout != null && Skin.ModeLayout.TryGetValue(ModeSystem.ModeId(_chart != null ? _chart.Mode : GameMode.Mania),
                out var fgd) && fgd != null && fgd.ContainsKey("field.ground");
            if (fgOv)
                gndY = L.AreaY + L.AreaH * Math.Max(0.5, Math.Min(0.95, fgE.Y));
            double skyMove = EvalEvent("moveY", now, LC_Y("field.sky", 0.55)); // 天线高度（默认 55% 天-地间距）
            skyY = gndY - Math.Clamp(skyMove, 0.12, 0.88) * (gndY - L.TopY);
            ppms = Math.Max(0.05, gndY - skyY) * GameSettings.Speed / 1000.0;
            cx = L.PlayX + L.PlayW / 2.0;
            _arcGndY = gndY;
            _arcSkyY = skyY;
            _arcCx = cx;
            _arcTilt = 0;
            // mania 斜轨参数（地面部分直接复用 mania 斜轨：消失点在天线上方、地线=判定处 1.0 深度）
            double slant = Math.Max(0.3, Math.Min(GraphicsQuality.SlantCap, Skin.Slant));
            _arcPk = slant * 4.0;
            _arcPt0 = (1 + _arcPk) * 1000.0 / Math.Max(0.1, GameSettings.Speed);
            _arcPvpY = gndY - (gndY - skyY) * (1 + _arcPk);
        }


        /// <summary>Arcaea arc 在时间比例 k 处的高度（0=地面线, 1=天空线）：起点 Y → 3D 控制点 Z → 终点 EndY 分段线性。</summary>
        double ArcaeaHeightAt(Note n, double k)
        {
            double y0 = n.Y, y1 = n.EndY;
            if (n.Arc3 == null || n.Arc3.Count == 0)
                return y0 + (y1 - y0) * k;
            var pts = new List<(double t, double h)>
            {
                (0, y0)
            };
            foreach (var p in n.Arc3)
                pts.Add((Math.Max(0, Math.Min(1, p.Y)), Math.Max(0, Math.Min(1, p.Z))));
            pts.Add((1, y1));
            pts.Sort((a, b) => a.t.CompareTo(b.t));
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (k >= pts[i].t && k <= pts[i + 1].t)
                {
                    double span = Math.Max(1e-6, pts[i + 1].t - pts[i].t);
                    double kk = (k - pts[i].t) / span;
                    return pts[i].h + (pts[i + 1].h - pts[i].h) * kk;
                }
            }

            return y1;
        }


        /// <summary>Arcaea arc 在时间比例 k 处的横向 x3d：起点轨 → Arc3 控制点归一 X → 终点轨 分段线性。
        /// 让控制点的横向位置真正参与路径（编辑器拖动控制点即可改变弧走向）。</summary>
        double ArcaeaXAt(Note n, double k, double x0, double x1, double glaneW)
        {
            if (n.Arc3 == null || n.Arc3.Count == 0)
                return x0 + (x1 - x0) * k;
            var pts = new List<(double t, double x)>
            {
                (0, x0)
            };
            foreach (var p in n.Arc3)
            {
                double nx = Math.Max(0, Math.Min(1, p.X));
                pts.Add((Math.Max(0, Math.Min(1, p.Y)), _arcCx + (nx - 0.5) * (4.0 * glaneW)));
            }

            pts.Add((1, x1));
            pts.Sort((a, b) => a.t.CompareTo(b.t));
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (k >= pts[i].t && k <= pts[i + 1].t)
                {
                    double span = Math.Max(1e-6, pts[i + 1].t - pts[i].t);
                    double kk = (k - pts[i].t) / span;
                    return pts[i].x + (pts[i + 1].x - pts[i].x) * kk;
                }
            }

            return x1;
        }


        void DrawArcaea(int W, int H, double now, PlayLayout L, double topY, double hitY, int kc)
        {
            // ===== 用户要求还原：2D 平面视角（非 3D 相机）=====
            // 地线（底部）= 4K 透视轨道（样式类似 mania 斜轨，向天线收敛），有 tap 与 hold；
            // 天线（上部）= 天空判定线；skytap 可在地线到天线间任意放置（只在 arc 上），
            // arc 为天地之间连续的、可变化的弧线，有「可接触」（粗亮）与「装饰」（细暗）两态；
            // 接触 arc 时轨道向 arc 一侧倾斜；skytap 与 tap 同竖直面时以细线相连（skytap 之间不连）。
            double laneW = L.PlayW / 4; // 地线平面 4K 轨道
            ArcaeaSetup(L, now, out double cx, out double skyY, out double gndY, out double ppms);
            // ===== 实机 BGA 氛围背景（参照 bilibili 横屏实机视频：暗紫渐变 + 漂浮品红/蓝/白光效）=====
            _d2d.FillVerticalGradient(0, 0, W, H, Color.FromArgb(255, 26, 18, 40), Color.FromArgb(255, 8, 7, 14));
            double tt = now / 1000.0;
            double beatPulse = _chart != null && _chart.Bpm > 0 ? Math.Max(0, 1 - Math.Abs((now % (60000.0 / _chart.Bpm)) / (60000.0 / _chart.Bpm) - 0.5) * 2) : 0.5;
            // 品红光球（右下，随节拍脉动——Xterfusion BGA 的品红主光）
            double g1x = W * (0.66 + 0.07 * Math.Sin(tt * 0.21));
            double g1y = H * (0.58 + 0.09 * Math.Cos(tt * 0.17));
            _d2d.FillRadialGradient((float)g1x, (float)g1y, (float)(H * (0.30 + 0.05 * beatPulse)), Color.FromArgb(150, 255, 90, 165), Color.FromArgb(0, 255, 90, 165));
            // 蓝光球（左上）
            double g2x = W * (0.26 + 0.08 * Math.Sin(tt * 0.15 + 2.1));
            double g2y = H * (0.36 + 0.07 * Math.Cos(tt * 0.23 + 0.8));
            _d2d.FillRadialGradient((float)g2x, (float)g2y, (float)(H * 0.26), Color.FromArgb(110, 80, 160, 255), Color.FromArgb(0, 80, 160, 255));
            // 白色光球（中上，缓慢漂移）
            double g3x = W * (0.50 + 0.10 * Math.Sin(tt * 0.11 + 4.2));
            double g3y = H * (0.28 + 0.05 * Math.Cos(tt * 0.19 + 2.6));
            _d2d.FillRadialGradient((float)g3x, (float)g3y, (float)(H * 0.18), Color.FromArgb(80, 255, 235, 255), Color.FromArgb(0, 255, 235, 255));
            // 地线下方微光（实机：轨道底部通常有背景反光）
            _d2d.FillVerticalGradient(0, (float)(H * 0.86), W, (float)(H * 0.14), Color.FromArgb(40, 150, 90, 180), Color.FromArgb(0, 150, 90, 180));
            bool Sky(int c) => c < 2; // 天键（Col 0-1）/ 地键（Col 2-5）
            Color NoteColFor(int c) => Sky(c) ? Color.FromArgb(255, 238, 248, 255) : Color.White;
            // 地键轨道索引（Col 2-5 → 4K 轨 0-3）
            int LaneOf(int c) => Math.Max(0, Math.Min(3, c - 2));
            double LaneX(double lane) => L.PlayX + (lane + 0.5) * laneW;
            // 轨道倾斜：按住 arc（可接触态）时轨道向 arc 一侧倾斜（天线处偏移最大）；装饰弧不可接触、不倾斜
            _arcTilt = 0;
            foreach (var hn in _heldNotes)
                if (hn.Type == "arc" && hn.Held && !hn.Decor)
                {
                    int e0 = LaneOf(hn.Col), e1 = LaneOf(hn.EndCol >= 0 ? hn.EndCol : hn.Col);
                    double acx = LaneX((e0 + e1) / 2.0);
                    _arcTilt = Math.Sign(acx - cx) * laneW * 0.9;
                    break;
                }

            // 左侧回忆收集率竖条（Recollection Rate，Arcaea 招牌，2D HUD）
            {
                double barX = 14, barW = 7;
                double barTop = topY + 30;
                double barBot = Math.Min(H - 60, H * 0.92);
                if (barBot < barTop + 40)
                    barBot = barTop + 40;
                double fill = _chart != null && _chart.Mode == GameMode.Arcaea ? _arcRecollection / 100.0 : _acc / 100.0; // D11：Arcaea 用独立回忆率
                _d2d.FillRect((float)barX, (float)barTop, (float)barW, (float)(barBot - barTop), Color.FromArgb(60, 40, 50, 80));
                _d2d.DrawRect((float)barX, (float)barTop, (float)barW, (float)(barBot - barTop), Color.FromArgb(140, 160, 190, 235), 1f);
                double fy = barBot - fill * (barBot - barTop);
                // 实机回忆率配色（Normal）：≥70% 蓝、<70% 变红（<30% 自动衰减）；Easy 青 / Hard 红
                Color fc = fill >= 0.7 ? Color.FromArgb(255, 90, 160, 255) : Color.FromArgb(255, 255, 110, 120);
                _d2d.FillRect((float)barX, (float)fy, (float)barW, (float)(barBot - fy), fc);
                var tp = new PointF[4]
                {
                    new PointF((float)(barX + barW / 2), (float)Math.Max(barTop - 12, fy - 14)),
                    new PointF((float)(barX + barW + 5), (float)Math.Max(barTop - 6, fy - 8)),
                    new PointF((float)(barX + barW / 2), (float)Math.Max(barTop, fy - 2)),
                    new PointF((float)(barX - 5), (float)Math.Max(barTop - 6, fy - 8))
                };
                _d2d.FillPolygon(tp, fc);
            }

            // ===== 地面部分：4K 斜轨（直接复用 mania 斜轨代码；地线=轨道底部判定线）=====
            double skyH = Math.Max(1, gndY - skyY);
            // 斜轨轨道宽：占画面宽 62%，以**虚拟画面中心**为基准对称缩放——等腰梯形（mania 原版 LaneL 写法）。
            // 画面完整缩放显示（D2D fit 到物理窗口）后，画面中心 = 屏幕中心，轨道随之居中。
            double gPlayW = ClientSize.Width * 0.62;
            double glaneW = gPlayW / 4.0;
            double gCx = ClientSize.Width / 2.0; // 虚拟画面中心（完整缩放显示后即屏幕中心）
            // ===== 3D 统一透视（天空+地面同一摄像机）=====
            // 场景：一块地面平面向远处延伸——近端=判定线（深度 1.0），远端=消失点（深度→0）；
            // 天线（天空线）= 用户指定的参考线位置，天键判定深度 = 天线深度 zAntenna。
            // 空中物体 (x3d, h, z)：h=离地高度，z=深度。
            // 投影：屏幕 x = gCx + (x3d-gCx)·z（近大远小）；屏幕 y = 地面投影 y − h·z（高度近大远小）。
            // 自洽性：h=0 即地面点（与 GLaneX/YAtG 完全一致）；h=skyH 的点在判定时刻(remain=0)恰好投影到天空线。
            double DepthOfRemain(double remain) => _arcPt0 / (_arcPt0 + Math.Max(-_arcPt0 * 0.98, remain)); // 时间→深度（判定=1）
            double ProjX3(double x3d, double z) => gCx + (x3d - gCx) * z;
            double ProjY3(double h, double z) => _arcPvpY + (gndY - _arcPvpY) * z - h * z;
            double X3dOf(double laneIdx) => gCx + (laneIdx - 2.0) * glaneW; // 轨道列(0-4) → 平面 x
            double X3dFree(double nx) => gCx + (nx - 0.5) * (4.0 * glaneW); // skytap 自由 x（0..1 → 全轨宽）
            // 天线（天空线）位置 = ArcaeaSetup 求值（D12：随 moveY 事件浮动，默认 55% 天-地间距；原硬编码 774 已弃）
            // 天键（skytap）判定深度 = 天线所在深度（判定时刻正好落在天线上）
            double antennaY = skyY;
            double zAntenna = (antennaY - _arcPvpY) / (gndY - _arcPvpY);
            double YAtG(double tRemain)
            {
                double tt = Math.Max(-_arcPt0 * 0.98, tRemain);
                return _arcPvpY + (gndY - _arcPvpY) * (_arcPt0 / (_arcPt0 + tt));
            }

            double DepthAtG(double y)
            {
                double d = (y - _arcPvpY) / (gndY - _arcPvpY);
                return Math.Max(0.06, Math.Min(1.8, d));
            }

            // 等腰梯形：左右边界以中心为基准对称收敛（与 mania 斜轨 LaneL/NoteX 完全一致）
            double GLaneX(double lane, double y) => gCx + (lane - 2.0) * glaneW * DepthAtG(y); // 列边界（lane 0-4）
            double GNoteX(double lane, double f, double y) => gCx + (lane + f - 2.0) * glaneW * DepthAtG(y);
            // 轨道底色（4 列斜轨梯形，mania 风格暗蓝）——轨道从消失点(pvpY)延伸到画面底部；
            // 天线只是轨道上方的参考线（skytap 判定线），不是轨道边界
            Color laneBg = Color.FromArgb(26, 36, 58);
            double trackTop = _arcPvpY; // 轨道远端 = 消失点附近（深度→0，可能高于屏幕顶部，D2D 裁剪）
            for (int i = 0; i < 4; i++)
                _d2d.FillQuad((float)GLaneX(i, trackTop), (float)trackTop, (float)GLaneX(i + 1, trackTop), (float)trackTop, (float)GLaneX(i + 1, H), (float)H, (float)GLaneX(i, H), (float)H, laneBg);
            for (int i = 1; i < 4; i++)
                _d2d.DrawLine((float)GLaneX(i, trackTop), (float)trackTop, (float)GLaneX(i, H), (float)H, Color.FromArgb(45, 60, 90), 1f);
            // 地线（白色，轨道底部判定线）
            _d2d.DrawLine((float)GLaneX(0, gndY), (float)gndY, (float)GLaneX(4, gndY), (float)gndY, Color.White, 2.4f, Skin.HitLineStyle);
            // 天线（天空线）绘制在 DrawArcaea 末尾（最后画，不被弧/音符盖住）——位置 = 用户指定（屏幕 y≈820 → 画布 774）
            // mania 斜轨的前瞻/下探（地面音符从消失点附近出现）
            double ahead = _arcPt0 / _arcPk + 400;
            double below = _arcPt0 * (1 - (gndY - _arcPvpY) / Math.Max(1, H - _arcPvpY)) + 500;
            // arc：空中 3D 曲线 (x3d, h, z)——h=弧高（0..skyH，可上下起伏），z=弧点深度（同地面时间映射，判定时刻=1）。
            // 贴地(h≈0)的弧段沿地面平面滑向判定线；天线高(h=skyH)的弧段在判定时刻恰好投影到天线——天地统一透视。
            // 渲染为 3D 透视丝带：逐段四边形，**宽度随深度 z 缩放**（近大远小——真正的 3D 视角，而非恒定宽度的 2D 折线）。
            for (int i = 0; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Type != "arc")
                    continue;
                if (n.End - now < -600 || n.Time - now > ahead + 400)
                    continue;
                int e0 = LaneOf(n.Col), e1 = LaneOf(n.EndCol >= 0 ? n.EndCol : n.Col);
                bool leftSide = (e0 + e1) / 2.0 < 2.0;
                var col = leftSide ? AccentColCurrent() : NoteColCurrent(); // t25：蓝=强调色 / 红=主色
                if (n.Judged && n.Judgment == "MISS")
                    col = Color.FromArgb(255, 255, 60, 60); // 错判鲜红
                else if (n.Judged)
                    col = Color.FromArgb(255, Math.Min(255, col.R + 60), Math.Min(255, col.G + 60), Math.Min(255, col.B + 60)); // 点亮
                int SEG = 14;
                double hw = n.Decor ? glaneW * 0.03 : glaneW * 0.21; // 装饰态变细；可接触弧粗带（实机视频参照）
                var path = _arcPath; // 逐帧复用缓冲（零 GC）
                var zs = _arcZs;
                for (int s = 0; s <= SEG; s++)
                {
                    double kk = s / (double)SEG;
                    double t = n.Time + (n.End - n.Time) * kk;
                    double z = Math.Min(1.3, DepthOfRemain(t - now)); // 钳制：已滑过部分(z>1.3)不参与中轴计算，避免中点跑偏
                    double h = skyH * ArcaeaHeightAt(n, kk);
                    double x3d = ArcaeaXAt(n, kk, X3dOf(e0), X3dOf(e1), glaneW);
                    path[s] = new PointF((float)ProjX3(x3d, z), (float)ProjY3(h, z));
                    zs[s] = z;
                }

                // 3D 丝带段：用段方向作法向，宽度 = hw×系数×深度^1.3（远处收得更细——立体纵深更强）
                void ArcBand(PointF[] p, double wFactor, Color c)
                {
                    for (int s = 0; s < SEG; s++)
                    {
                        double w1 = hw * wFactor * Math.Max(0.05, Math.Pow(zs[s], 1.3));
                        double w2 = hw * wFactor * Math.Max(0.05, Math.Pow(zs[s + 1], 1.3));
                        double dx = p[s + 1].X - p[s].X, dy = p[s + 1].Y - p[s].Y;
                        double len = Math.Max(1e-4, Math.Sqrt(dx * dx + dy * dy));
                        double nx = -dy / len, ny = dx / len;
                        _d2d.FillQuad((float)(p[s].X - nx * w1), (float)(p[s].Y - ny * w1), (float)(p[s].X + nx * w1), (float)(p[s].Y + ny * w1), (float)(p[s + 1].X + nx * w2),
                            (float)(p[s + 1].Y + ny * w2), (float)(p[s + 1].X - nx * w2), (float)(p[s + 1].Y - ny * w2), c);
                    }
                }

                if (n.Decor)
                {
                    // 装饰弧（音轨）：细四边形带
                    ArcBand(path, 1.0, Color.FromArgb(120, col.R, col.G, col.B));
                }
                else
                {
                    // 可接触弧：**实心棱柱带**——弧是立体柱体：
                    // 上半面受光(亮)、下半面背光(暗) → 圆柱/棱柱质感；带内 X 对角线表示横截面为 X 型；
                    // 上棱线亮描边、下棱线暗描边、中心线高光 → 立体透视（近粗远细）。
                    var edge = leftSide ? Color.FromArgb(255, 18, 56, 130) : Color.FromArgb(255, 150, 30, 30);
                    var fillHi = Color.FromArgb(235, Math.Min(255, col.R + 48), Math.Min(255, col.G + 48), Math.Min(255, col.B + 48)); // 上半(受光, 亮)
                    var fillLo = Color.FromArgb(235, (int)(col.R * 0.5), (int)(col.G * 0.5), (int)(col.B * 0.5)); // 下半(背光, 暗)
                    var xLine = Color.FromArgb(150, edge.R, edge.G, edge.B); // 带内 X 剖面线(深色)
                    var hiEdge = Color.FromArgb(255, Math.Min(255, edge.R + 90), Math.Min(255, edge.G + 90), Math.Min(255, edge.B + 90)); // 上棱(亮描边)
                    var loEdge = Color.FromArgb(255, (int)(edge.R * 0.45), (int)(edge.G * 0.45), (int)(edge.B * 0.45)); // 下棱(暗描边)
                    var Lp2 = _arcLp;
                    var Rp2 = _arcRp; // 逐帧复用缓冲（零 GC）
                    for (int s = 0; s <= SEG; s++)
                    {
                        double w = hw * 0.9 * Math.Max(0.05, Math.Pow(zs[s], 1.3));
                        double dx = (s < SEG ? path[s + 1].X - path[s].X : path[s].X - path[s - 1].X);
                        double dy = (s < SEG ? path[s + 1].Y - path[s].Y : path[s].Y - path[s - 1].Y);
                        double len = Math.Max(1e-4, Math.Sqrt(dx * dx + dy * dy));
                        double nx = -dy / len, ny = dx / len;
                        Lp2[s] = new PointF((float)(path[s].X - nx * w), (float)(path[s].Y - ny * w));
                        Rp2[s] = new PointF((float)(path[s].X + nx * w), (float)(path[s].Y + ny * w));
                    }

                    // 实心棱柱：上半面(亮) + 下半面(暗)，以中心线为分界
                    for (int s = 0; s < SEG; s++)
                    {
                        _d2d.FillQuad(Lp2[s].X, Lp2[s].Y, path[s].X, path[s].Y, path[s + 1].X, path[s + 1].Y, Lp2[s + 1].X, Lp2[s + 1].Y, fillHi);
                        _d2d.FillQuad(path[s].X, path[s].Y, Rp2[s].X, Rp2[s].Y, Rp2[s + 1].X, Rp2[s + 1].Y, path[s + 1].X, path[s + 1].Y, fillLo);
                    }

                    // 带内 X 剖面线：每段两条交叉对角线(左端↔右端互连) → 横截面 X 型
                    for (int s = 0; s < SEG; s++)
                    {
                        _d2d.DrawLine(Lp2[s].X, Lp2[s].Y, Rp2[s + 1].X, Rp2[s + 1].Y, xLine, 1.6f);
                        _d2d.DrawLine(Rp2[s].X, Rp2[s].Y, Lp2[s + 1].X, Lp2[s + 1].Y, xLine, 1.6f);
                    }

                    // 棱线：上棱(亮) / 下棱(暗) + 首尾端面
                    for (int s = 0; s < SEG; s++)
                    {
                        _d2d.DrawLine(Lp2[s].X, Lp2[s].Y, Lp2[s + 1].X, Lp2[s + 1].Y, hiEdge, 2.2f);
                        _d2d.DrawLine(Rp2[s].X, Rp2[s].Y, Rp2[s + 1].X, Rp2[s + 1].Y, loEdge, 2.2f);
                    }

                    _d2d.DrawLine(Lp2[0].X, Lp2[0].Y, Rp2[0].X, Rp2[0].Y, loEdge, 2.0f);
                    _d2d.DrawLine(Lp2[SEG].X, Lp2[SEG].Y, Rp2[SEG].X, Rp2[SEG].Y, hiEdge, 2.0f);
                    // 中心高光线(立体感)
                    for (int s = 0; s < SEG; s++)
                        _d2d.DrawLine(path[s].X, path[s].Y, path[s + 1].X, path[s + 1].Y, Color.FromArgb(90, 255, 255, 255), 1.0f);
                }
            }

            // hold（地键：mania 斜轨白色长条 + 白色圆角头，底缘压地线；天键 hold 属天空部分暂保持原样）
            double holdCut = now - below - 300;
            while (_holdIdx < _holdNotes.Count && _holdNotes[_holdIdx].End < holdCut)
                _holdIdx++;
            for (int hi = _holdIdx; hi < _holdNotes.Count; hi++)
            {
                var n = _holdNotes[hi];
                if (n.Type == "arc")
                    continue;
                if (n.Time - now > ahead)
                    break;
                if (n.Judged && n.Judgment == "MISS" && now > n.End)
                    continue;
                if (Sky(n.Col))
                {
                    // —— 天空 hold：空中长条（同 skytap 深度映射：头在天线判定，尾在结束时刻），宽度随深度缩放 ——
                    var col = NoteColFor(n.Col);
                    double x3d = X3dFree(n.X);
                    double zHead = zAntenna * DepthOfRemain(n.Time - now);
                    double hHead = skyH * ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, (n.Time - now) / ahead));
                    double zTail = zAntenna * DepthOfRemain(n.End - now);
                    double hTail = skyH * ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, (n.End - now) / ahead));
                    var pHead = new PointF((float)ProjX3(x3d, zHead), (float)ProjY3(hHead, zHead));
                    var pTail = new PointF((float)ProjX3(x3d, zTail), (float)ProjY3(hTail, zTail));
                    double wHead = glaneW * 0.18 * Math.Max(0.25, zHead);
                    double wTail = glaneW * 0.18 * Math.Max(0.25, zTail);
                    _d2d.FillQuad((float)(pHead.X - wHead), pHead.Y, (float)(pHead.X + wHead), pHead.Y, (float)(pTail.X + wTail), pTail.Y, (float)(pTail.X - wTail), pTail.Y, Color.FromArgb(140,
                        col.R, col.G, col.B));
                    double ds = wHead * 0.9;
                    var pt2 = new PointF(pHead.X, (float)(pHead.Y - ds));
                    var pr2 = new PointF((float)(pHead.X + ds), pHead.Y);
                    var pb2 = new PointF(pHead.X, (float)(pHead.Y + ds));
                    var pl2 = new PointF((float)(pHead.X - ds), pHead.Y);
                    _d2d.FillPolygon(new[] { pt2, pr2, pb2, pl2 }, Color.FromArgb(235, col.R, col.G, col.B));
                    continue;
                }

                // 地键 hold：mania 斜轨白色半透明长条 + 白色圆角头（osu!mania 经典）
                double lane = LaneOf(n.Col);
                double yHead = YAtG(n.Time - now);
                double yTail = YAtG(n.End - now);
                double y1 = Math.Max(yTail, _arcPvpY + 6), y2 = Math.Min(yHead, H);
                if (y2 <= y1)
                    continue;
                double xl1 = GNoteX(lane, 0.3, y1), xr1 = GNoteX(lane, 0.7, y1);
                double xl2 = GNoteX(lane, 0.3, y2), xr2 = GNoteX(lane, 0.7, y2);
                _d2d.FillQuad((float)xl1, (float)y1, (float)xr1, (float)y1, (float)xr2, (float)y2, (float)xl2, (float)y2, Color.FromArgb(120, 255, 255, 255));
                double hTop = y2 - NoteThickness * 0.9;
                _d2d.FillRoundedRect((float)GNoteX(lane, 0.06, hTop), (float)hTop, (float)(GNoteX(lane, 0.94, hTop) - GNoteX(lane, 0.06, hTop)), (float)(NoteThickness * 0.9), 3, Color.White);
                _d2d.DrawRoundedRect((float)GNoteX(lane, 0.06, hTop), (float)hTop, (float)(GNoteX(lane, 0.94, hTop) - GNoteX(lane, 0.06, hTop)), (float)(NoteThickness * 0.9), 3, Color.FromArgb(140,
                    200, 205, 215), 1.5f);
            }

            // tap（地键 4K 轨上；天键=skytap 菱形，空中小菱形从更远处下落到天线判定）
            int t0 = LowerBound(now - below);
            for (int i = t0; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time - now > ahead)
                    break;
                if (IsHoldNote(n) || n.Judged)
                    continue;
                var col = NoteColFor(n.Col);
                if (Sky(n.Col))
                {
                    // skytap：空中小菱形——从更远处（深度 zAntenna²、天空高处 0.45×skyH）沿空间下落到天线判定。
                    // 判定时刻 remain=0 → z=zAntenna、h=0 → 屏幕位置恰好在天线（774）上。
                    double remain = n.Time - now;
                    double z = zAntenna * DepthOfRemain(remain);
                    double h = skyH * ArcaeaSkyHeightRatio(n) * Math.Max(0, Math.Min(1, remain / ahead));
                    double x3d = X3dFree(n.X);
                    var pc = new PointF((float)ProjX3(x3d, z), (float)ProjY3(h, z));
                    double ds = glaneW * 0.16 * Math.Max(0.3, z); // 近大远小
                    _d2d.FillEllipse((float)pc.X, (float)pc.Y, (float)(ds * 2.1), (float)(ds * 2.1), Color.FromArgb(16, 255, 255, 255));
                    var pt = new PointF((float)pc.X, (float)(pc.Y - ds));
                    var pr = new PointF((float)(pc.X + ds), (float)pc.Y);
                    var pb = new PointF((float)pc.X, (float)(pc.Y + ds));
                    var pl = new PointF((float)(pc.X - ds), (float)pc.Y);
                    _d2d.FillPolygon(new[] { pt, pr, pb, pl }, Color.FromArgb(235, col.R, col.G, col.B));
                    _d2d.DrawLine(pt.X, pt.Y, pr.X, pr.Y, Color.FromArgb(150, 255, 255, 255), 1.2f);
                    _d2d.DrawLine(pr.X, pr.Y, pb.X, pb.Y, Color.FromArgb(150, 255, 255, 255), 1.2f);
                    _d2d.DrawLine(pb.X, pb.Y, pl.X, pl.Y, Color.FromArgb(150, 255, 255, 255), 1.2f);
                    _d2d.DrawLine(pl.X, pl.Y, pt.X, pt.Y, Color.FromArgb(150, 255, 255, 255), 1.2f);
                    continue;
                }

                // 地键：mania 斜轨梯形音符（Malody 风格白音符；底缘触地线判定）
                double lane = LaneOf(n.Col);
                double yHead = YAtG(n.Time - now);
                double th = NoteThickness * (0.45 + 0.55 * Math.Min(1, DepthAtG(yHead)));
                if (yHead < _arcPvpY - th || yHead > H + th)
                    continue;
                double yTop = yHead - th, yBot = yHead; // 底缘压线（判定时刻底缘触地线）
                double xTL = GNoteX(lane, 0.06, yTop), xTR = GNoteX(lane, 0.94, yTop);
                double xBL = GNoteX(lane, 0.06, yBot), xBR = GNoteX(lane, 0.94, yBot);
                _d2d.FillQuad((float)xTL, (float)yTop, (float)xTR, (float)yTop, (float)xBR, (float)yBot, (float)xBL, (float)yBot, Color.White);
            }

            // skytap 与 tap 同竖直面（同一时刻、同一 x 平面）→ 细线相连（实机特征；skytap 之间不连）
            double linkWindow = 120; // 同拍容差
            for (int i = t0; i < _notes.Count; i++)
            {
                var sn = _notes[i];
                if (sn.Time - now > ahead)
                    break;
                if (!Sky(sn.Col) || IsHoldNote(sn) || sn.Judged)
                    continue;
                double sRemain = sn.Time - now;
                double sx3d = X3dFree(sn.X); // skytap 的平面 x
                double sz = zAntenna * DepthOfRemain(sRemain);
                double sh = skyH * ArcaeaSkyHeightRatio(sn) * Math.Max(0, Math.Min(1, sRemain / ahead));
                for (int j = t0; j < _notes.Count; j++)
                {
                    var fn = _notes[j];
                    if (fn.Time > sn.Time + linkWindow)
                        break;
                    if (fn.Time < sn.Time - linkWindow)
                        continue;
                    if (Sky(fn.Col) || IsHoldNote(fn) || fn.Judged)
                        continue;
                    double fx3d = X3dOf(LaneOf(fn.Col));
                    if (Math.Abs(sx3d - fx3d) > glaneW * 0.55)
                        continue; // 非同一竖直面
                    double fz = DepthOfRemain(fn.Time - now);
                    var pa = new PointF((float)ProjX3(sx3d, sz), (float)ProjY3(sh, sz));
                    var pb = new PointF((float)ProjX3(fx3d, fz), (float)ProjY3(0, fz));
                    _d2d.DrawLine(pa.X, pa.Y, pb.X, pb.Y, Color.FromArgb(110, 255, 255, 255), 1f);
                    break;
                }
            }

            // 天线（天空线）：最后绘制（不被弧/音符盖住）——位置 = 用户指定（v8 截图红线处，屏幕 y≈820 → 画布 774）；
            // 加长：两端各延长轨道宽的 25%；加粗：4px
            {
                double aW = GLaneX(4, antennaY) - GLaneX(0, antennaY);
                double aL = GLaneX(0, antennaY) - aW * 0.25;
                double aR = GLaneX(4, antennaY) + aW * 0.25;
                _d2d.DrawLine((float)aL, (float)antennaY, (float)aR, (float)antennaY, Color.FromArgb(170, 215, 238, 255), 4.0f);
            }
        }


        void DrawCytus(int W, int H, double now, PlayLayout L, double topY)
        {
            // 场 = 框定游玩区域：尽可能大（横贯全区域，不再缩到 70% 高）
            double playW = L.AreaW;
            double playX = L.AreaX;
            double fieldY = L.AreaY + 6;
            double fieldH = L.AreaH - 12;
            bool use3d = GameSettings.Camera3D && !EditLayoutMode;
            // 3D：场平面放在 z=0，相机中心 = 场中心，轻微俯仰 + 偏航
            if (use3d)
            {
                _cam.CenterX = playX + playW / 2.0;
                _cam.CenterY = fieldY + fieldH / 2.0;
                _cam.Pitch = GameSettings.CameraPitch * 0.35;
                _cam.Yaw = GameSettings.CameraYaw;
                _cam.Depth = GameSettings.CameraDepth;
                _cam.Fov = 60;
            }

            // 场背景（3D 投影四边形 / 2D 矩形）
            if (use3d)
            {
                var tl = P(_cam.Project(playX - _cam.CenterX, fieldY - _cam.CenterY, 0));
                var tr = P(_cam.Project(playX + playW - _cam.CenterX, fieldY - _cam.CenterY, 0));
                var br = P(_cam.Project(playX + playW - _cam.CenterX, fieldY + fieldH - _cam.CenterY, 0));
                var bl = P(_cam.Project(playX - _cam.CenterX, fieldY + fieldH - _cam.CenterY, 0));
                _d2d.FillQuad(tl.X, tl.Y, tr.X, tr.Y, br.X, br.Y, bl.X, bl.Y, Color.FromArgb(200, 12, 16, 26));
                var gridCol = Color.FromArgb(24, 90, 110, 150);
                for (int i = 1; i < 8; i++)
                {
                    double y = fieldY + fieldH * i / 8.0;
                    var a = P(_cam.Project(playX - _cam.CenterX, y - _cam.CenterY, 0));
                    var b = P(_cam.Project(playX + playW - _cam.CenterX, y - _cam.CenterY, 0));
                    _d2d.DrawLine(a.X, a.Y, b.X, b.Y, gridCol, 1f);
                }
            }
            else
            {
                _d2d.FillVerticalGradient((float)playX, (float)fieldY, (float)playW, (float)fieldH, Color.FromArgb(200, 16, 26, 48), Color.FromArgb(200, 8, 10, 18));
                _d2d.DrawRect((float)playX, (float)fieldY, (float)playW, (float)fieldH, Color.FromArgb(80, 90, 110, 150), 1.5f);
                for (int i = 1; i < 8; i++)
                {
                    double y = fieldY + fieldH * i / 8.0;
                    _d2d.DrawLine((float)playX, (float)y, (float)(playX + playW), (float)y, Color.FromArgb(24, 90, 110, 150), 1f);
                }
            }

            // 扫描线（亮色横线，两端光点；与判定门控同几何）
            double scanY = CytusScanY(now, L);
            CytusPageAt(now, out int scanPage, out _);
            bool down = (scanPage % 2) == 0;
            var scanCol = Color.FromArgb(255, 120, 220, 255);
            if (use3d)
            {
                var a = P(_cam.Project(playX - _cam.CenterX, scanY - _cam.CenterY, 0));
                var b = P(_cam.Project(playX + playW - _cam.CenterX, scanY - _cam.CenterY, 0));
                _d2d.DrawLine(a.X, a.Y, b.X, b.Y, scanCol, 2f);
                _d2d.FillEllipse(a.X, a.Y, 7, 7, scanCol);
                _d2d.FillEllipse(b.X, b.Y, 7, 7, scanCol);
            }
            else
            {
                // 扫描线带动画尾迹（渐隐短线，沿运动反方向）
                for (int t = 1; t <= 5; t++)
                {
                    double trailY = down ? scanY - t * 9 : scanY + t * 9;
                    int a = Math.Max(20, 150 - t * 26);
                    _d2d.DrawLine((float)playX, (float)trailY, (float)(playX + playW), (float)trailY, Color.FromArgb(a, 120, 220, 255), 1.5f);
                }

                _d2d.DrawLine((float)playX, (float)scanY, (float)(playX + playW), (float)scanY, scanCol, 2f);
                _d2d.FillEllipse((float)playX, (float)scanY, 7, 7, scanCol);
                _d2d.FillEllipse((float)(playX + playW), (float)scanY, 7, 7, scanCol);
            }

            // 音符（圆形；Y 已折进 Time，扫描线扫过即判定时间）
            double bpm2 = _chart != null && _chart.Bpm > 0 ? _chart.Bpm : 120;
            double pageMs = CytusPageMsAt(now);
            double ahead = pageMs;
            for (int i = LowerBound(now - pageMs); i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time - now > ahead)
                    break;
                if (n.Judged)
                    continue;
                double nx = playX + n.X * playW;
                double ny = fieldY + n.Y * fieldH;
                double dist = Math.Abs(scanY - ny);
                double pulse = Math.Max(0, 1 - dist / (fieldH * 0.15));
                double r = (6 + 8 * pulse);
                // 初代音符单色双状态（实机）：未激活=淡灰圆圈，激活（扫描线接近）=亮白圈
                var col = pulse > 0.35 ? NoteColCurrent() : AccentColCurrent();
                if (IsHoldNote(n))
                {
                    if (use3d)
                    {
                        var a = P(_cam.Project(nx - _cam.CenterX, fieldY - _cam.CenterY, 0));
                        var b = P(_cam.Project(nx - _cam.CenterX, fieldY + fieldH - _cam.CenterY, 0));
                        _d2d.DrawLine(a.X, a.Y, b.X, b.Y, Color.FromArgb(120, col.R, col.G, col.B), 6f);
                        var ctr = P(_cam.Project(nx - _cam.CenterX, ny - _cam.CenterY, 0));
                        var rx = P(_cam.Project(nx + r + 4 - _cam.CenterX, ny - _cam.CenterY, 0));
                        var ry = P(_cam.Project(nx - _cam.CenterX, ny + r + 4 - _cam.CenterY, 0));
                        _d2d.FillEllipse(ctr.X, ctr.Y, Math.Abs(rx.X - ctr.X) + 1, Math.Abs(ry.Y - ctr.Y) + 1, col);
                    }
                    else
                    {
                        _d2d.FillRect((float)(nx - 3), (float)fieldY, 6, (float)fieldH, Color.FromArgb(120, col.R, col.G, col.B));
                        _d2d.FillEllipse((float)nx, (float)ny, (float)r + 4, (float)r + 4, col);
                    }
                }
                else if (use3d)
                {
                    var ctr = P(_cam.Project(nx - _cam.CenterX, ny - _cam.CenterY, 0));
                    var rx = P(_cam.Project(nx + r - _cam.CenterX, ny - _cam.CenterY, 0));
                    var ry = P(_cam.Project(nx - _cam.CenterX, ny + r - _cam.CenterY, 0));
                    float erx = Math.Abs(rx.X - ctr.X) + 1, ery = Math.Abs(ry.Y - ctr.Y) + 1;
                    _d2d.FillEllipse(ctr.X, ctr.Y, erx, ery, col);
                    _d2d.FillEllipse(ctr.X, ctr.Y, erx * 0.4f, ery * 0.4f, Color.White);
                }
                else
                {
                    // Cytus Click（实机初代）：单色圆环 + 白核；未激活淡灰、激活亮白 + 内环收缩
                    _d2d.DrawEllipse((float)nx, (float)ny, (float)r, (float)r, Color.FromArgb(230, col.R, col.G, col.B), 3f);
                    _d2d.FillEllipse((float)nx, (float)ny, (float)(r * 0.45), (float)(r * 0.45), Color.White);
                    double shrink = r * (0.9 - pulse * 0.55);
                    if (shrink > 1)
                        _d2d.DrawEllipse((float)nx, (float)ny, (float)shrink, (float)shrink, Color.FromArgb((int)(120 + 100 * pulse), col.R, col.G, col.B), 1.6f);
                    if (n.Type == "drag")
                    {
                        // DRAG：沿扫描方向的一串小圆点
                        for (int k = -1; k <= 1; k++)
                            _d2d.FillEllipse((float)nx, (float)(ny + k * r * 1.6), (float)(r * 0.28), (float)(r * 0.28), Color.FromArgb(180, col.R, col.G, col.B));
                    }
                }
            }
        }


        void DrawOsuStandard(int W, int H, double now, PlayLayout L)
        {
            var f = OsuField(L);
            double arv = _chart != null && _chart.Ar > 0 ? Math.Max(0, Math.Min(10, _chart.Ar)) : 5;
            double approach = arv >= 5 ? 1950 - 150 * arv : 1800 - 120 * arv; // D6: 实机 AR 接近时间（AR<5: 1800−120AR；AR≥5: 1950−150AR）
            // D14: 实机圆半径 = (54.4 − 4.48×CS) × 1.00041 单位（512×384 场）→ 像素
            double csOsu = _chart != null && _chart.Cs > 0 ? Math.Max(0, Math.Min(10, _chart.Cs)) : 4;
            double circleR = (54.4 - 4.48 * csOsu) * 1.00041 / 384.0 * f.Height;
            bool useSkin = GameSettings.OsuDefaultSkin && OsuSkinReady();
            _d2d.DrawRect(f.X, f.Y, f.Width, f.Height, Color.FromArgb(70, 150, 170, 210), 1.5f);
            // 连击序号：未判定圆圈/滑条头按时间顺序编号（复用字典，零 GC）
            var comboNum = _osuComboNum;
            comboNum.Clear();
            int next = _combo + 1;
            foreach (var n in _notes)
            {
                if (n.Judged || IsOsuSpinner(n))
                    continue;
                comboNum[n] = next++;
            }

            // ①a 滑条描边（t16：批量合图——跨滑条同色聚为每帧 1 几何；层序=全部描边在下）
            _d2d.BeginStrokeBatch();
            foreach (var n in _notes)
            {
                if (n.Time - now > approach + 200)
                    break;
                if (!IsOsuSlider(n))
                    continue;
                if (n.Judged && n.Judgment == "MISS" && now > n.End)
                    continue;
                if (n.End - now < -600)
                    continue;
                var pts = SampleOsuSlider(n, 48);
                var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                int pxN = pts.Count;
                if (_osuPx == null || _osuPx.Length < pxN)
                    _osuPx = new PointF[pxN];
                for (int i = 0; i < pxN; i++)
                    _osuPx[i] = OsuPt(f, pts[i].X, pts[i].Y);
                _d2d.DrawPolyline(_osuPx, pxN, Color.FromArgb(90, 255, 255, 255), (float)(circleR * 0.9), false); // 外白边（发光）
                _d2d.DrawPolyline(_osuPx, pxN, Color.FromArgb(160, 255, 255, 255), (float)(circleR * 0.72), false); // 主体白边
                if (GraphicsQuality.Effective >= 1)
                    _d2d.DrawPolyline(_osuPx, pxN, Color.FromArgb(220, col.R, col.G, col.B), (float)(circleR * 0.30), false); // 中心色带
            }

            _d2d.FlushStrokeBatch();
            // ①b 滑条（路径 + 头尾 + 跟随球）
            foreach (var n in _notes)
            {
                if (n.Time - now > approach + 200)
                    break;
                if (!IsOsuSlider(n))
                    continue;
                if (n.Judged && n.Judgment == "MISS" && now > n.End)
                    continue;
                if (n.End - now < -600)
                    continue;
                var pts = SampleOsuSlider(n, 48);
                var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                int pxN = pts.Count;
                if (_osuPx == null || _osuPx.Length < pxN)
                    _osuPx = new PointF[pxN];
                for (int i = 0; i < pxN; i++)
                    _osuPx[i] = OsuPt(f, pts[i].X, pts[i].Y);
                // D13：滑条 tick 圆点（实机沿路径的小圆）
                if (!useSkin)
                {
                    var tickTimes = OsuSliderTicks(n);
                    foreach (var tt in tickTimes)
                    {
                        if (tt <= n.Time + 1 || tt >= n.End - 1)
                            continue;
                        var tp = OsuPt(f, SliderPointAt(pts, (tt - n.Time) / Math.Max(1, n.End - n.Time)).X, SliderPointAt(pts, (tt - n.Time) / Math.Max(1, n.End - n.Time)).Y);
                        _d2d.FillEllipse(tp.X, tp.Y, (float)(circleR * 0.22), (float)(circleR * 0.22), Color.White);
                        _d2d.DrawEllipse(tp.X, tp.Y, (float)(circleR * 0.22), (float)(circleR * 0.22), Color.FromArgb(120, 0, 0, 0), 1f);
                    }
                }

                var head = OsuPt(f, n.X, n.Y);
                if (useSkin && OsuSkinImg(1, out var slHc, out _))
                {
                    _d2d.DrawImage(slHc, head.X - (float)circleR, head.Y - (float)circleR, (float)(circleR * 2), (float)(circleR * 2), 1f);
                    string hn2 = comboNum.TryGetValue(n, out var hv2) ? hv2.ToString() : "";
                    if (hn2.Length > 0)
                        _d2d.Text(hn2, head.X, head.Y, (float)(circleR * 2), (float)(circleR * 2), Color.Black, (float)(circleR * 0.95), true);
                }
                else
                {
                    // D13：实心色圆 + 白描边（实机打击圈外观）
                    _d2d.FillEllipse(head.X, head.Y, (float)circleR, (float)circleR, col);
                    _d2d.DrawEllipse(head.X, head.Y, (float)circleR, (float)circleR, Color.White, (float)(circleR * 0.16));
                    string hnum = comboNum.TryGetValue(n, out var hv) ? hv.ToString() : "";
                    if (hnum.Length > 0)
                        _d2d.Text(hnum, head.X, head.Y, (float)(circleR * 2), (float)(circleR * 2), Color.White, (float)(circleR * 0.95), true);
                }

                var tail = _osuPx[pxN - 1];
                _d2d.DrawEllipse(tail.X, tail.Y, (float)(circleR * 0.5), (float)(circleR * 0.5), Color.FromArgb(220, 255, 255, 255), 2f);
                if (n.Repeats > 1)
                    _d2d.Text("↺×" + n.Repeats, tail.X, tail.Y - (float)(circleR * 0.9), 60, 16, Color.FromArgb(200, 255, 255, 255), 11f, true);
                // 跟随球（实机：滑条球白色 + 橙色 #FFC606 跟随圈）
                double dur = Math.Max(1, n.End - n.Time);
                double t = SliderBallT((now - n.Time) / dur, n.Repeats);
                var bp = SliderPointAt(pts, t);
                var ball = OsuPt(f, bp.X, bp.Y);
                if (useSkin && OsuSkinImg(4, out var followImg, out _))
                {
                    // 官方 sliderfollowcircle 贴图（跟随圈）+ 白色球心
                    double fr = circleR * 1.5;
                    _d2d.DrawImage(followImg, (float)(ball.X - fr), (float)(ball.Y - fr), (float)(fr * 2), (float)(fr * 2), 1f);
                    _d2d.FillEllipse(ball.X, ball.Y, (float)(circleR * 0.55), (float)(circleR * 0.55), Color.White);
                }
                else
                {
                    _d2d.DrawEllipse(ball.X, ball.Y, (float)(circleR * 1.3), (float)(circleR * 1.3), AccentColCurrent(), 3f);
                    _d2d.FillEllipse(ball.X, ball.Y, (float)(circleR * 0.55), (float)(circleR * 0.55), Color.White);
                }

                _d2d.DrawEllipse(ball.X, ball.Y, (float)(circleR * 0.55), (float)(circleR * 0.55), Color.FromArgb(120, 0, 0, 0), 1.5f);
            }

            // ② 圆圈 + approach circle
            foreach (var n in _notes)
            {
                if (n.Time - now > approach + 100)
                    break;
                if (n.Type != "tap")
                    continue;
                double remain = n.Time - now;
                if (remain < -400)
                    continue;
                var p = OsuPt(f, n.X, n.Y);
                if (n.Judged)
                {
                    // 命中爆开
                    double burst = now - n.Time;
                    if (burst < 260 && n.Judgment != "MISS")
                    {
                        double k = burst / 260.0;
                        _d2d.DrawEllipse(p.X, p.Y, (float)(circleR * (1 + k * 1.6)), (float)(circleR * (1 + k * 1.6)), Color.FromArgb((int)(180 * (1 - k)), 255, 255, 255),
                            (float)(circleR * 0.16 * (1 - k) + 1));
                    }

                    continue;
                }

                double frac = Math.Max(0, Math.Min(1, remain / approach));
                // D17：物件 400ms 淡入（出现即渐显，实机规则）
                double fadeIn = Math.Max(0, Math.Min(1, (approach - remain) / 400.0));
                // approach circle（从外圈缩小到圈本身）+ 辉光多层描边（osu! 默认皮肤白粉调）
                double appR = circleR + circleR * 3.0 * frac;
                if (appR > circleR + 0.5)
                {
                    if (useSkin && OsuSkinImg(2, out var appImg, out var appSz))
                    {
                        // 官方 approachcircle 贴图：随接近缩小，透明度渐强（叠加淡入）
                        double a = (0.25 + 0.75 * (1 - frac)) * fadeIn;
                        _d2d.DrawImage(appImg, (float)(p.X - appR), (float)(p.Y - appR), (float)(appR * 2), (float)(appR * 2), (float)a);
                    }
                    else
                    {
                        if (GraphicsQuality.Effective >= 1)
                        {
                            _d2d.DrawEllipse(p.X, p.Y, (float)(appR + 6), (float)(appR + 6), Color.FromArgb((int)((28 + 40 * (1 - frac)) * fadeIn), 255, 220, 235), 5f);
                            _d2d.DrawEllipse(p.X, p.Y, (float)(appR + 2), (float)(appR + 2), Color.FromArgb((int)((40 + 80 * (1 - frac)) * fadeIn), 255, 220, 235), 3f);
                        }

                        _d2d.DrawEllipse(p.X, p.Y, (float)appR, (float)appR, Color.FromArgb((int)((40 + 120 * (1 - frac)) * fadeIn), 255, 224, 238), 2f);
                    }
                }

                if (useSkin && OsuSkinImg(1, out var hcImg, out var hcSz))
                {
                    // 官方 hitcircle 贴图 + 连击数字覆盖层（叠加淡入）
                    _d2d.DrawImage(hcImg, (float)(p.X - circleR), (float)(p.Y - circleR), (float)(circleR * 2), (float)(circleR * 2), (float)fadeIn);
                    string num2 = comboNum.TryGetValue(n, out var v2) ? v2.ToString() : "";
                    if (num2.Length > 0)
                        _d2d.Text(num2, p.X, p.Y, (float)(circleR * 2), (float)(circleR * 2), Color.FromArgb((int)(255 * fadeIn), 0, 0, 0), (float)(circleR * 0.95), true);
                }
                else
                {
                    // D13：实心色圆 + 白描边（叠加淡入；实机打击圈外观）
                    var cc = NoteColCurrent(); // t25：主色读 NoteStyleBook
                    _d2d.FillEllipse(p.X, p.Y, (float)circleR, (float)circleR, Color.FromArgb((int)(255 * fadeIn), cc.R, cc.G, cc.B));
                    _d2d.DrawEllipse(p.X, p.Y, (float)circleR, (float)circleR, Color.FromArgb((int)(255 * fadeIn), 255, 255, 255), (float)(1.5 + 7.0 * frac));
                    string num = comboNum.TryGetValue(n, out var v) ? v.ToString() : "";
                    _d2d.Text(num, p.X, p.Y, (float)(circleR * 2), (float)(circleR * 2), Color.FromArgb((int)(255 * fadeIn), 255, 255, 255), (float)(circleR * 0.95), true);
                }
            }

            // ③ 转盘
            foreach (var n in _notes)
            {
                if (n.Time - now > approach + 100)
                    break;
                if (!IsOsuSpinner(n))
                    continue;
                if (n.End - now < -400)
                    continue;
                double cx = f.X + f.Width / 2.0, cy = f.Y + f.Height / 2.0;
                double r = f.Height * 0.42;
                var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                double phase = now / 120.0;
                // 螺旋色带旋转（3 圈，半径随角度增大）（缓冲复用）
                var sp = _spinnerSp;
                for (int i = 0; i < 64; i++)
                {
                    double a = phase + i * (3 * Math.PI * 2 / 63.0);
                    double rr = r * (0.16 + 0.78 * i / 63.0);
                    sp[i] = new PointF((float)(cx + Math.Cos(a) * rr), (float)(cy + Math.Sin(a) * rr));
                }

                _d2d.DrawPolyline(sp, Color.FromArgb(200, col.R, col.G, col.B), 4f, false);
                // 外圈刻度弧
                for (int s = 0; s < 6; s++)
                {
                    double a0 = phase + s * Math.PI / 3.0;
                    _d2d.DrawArcSegments((float)cx, (float)cy, (float)(r * 0.86), (float)a0, 0.9f, Color.FromArgb(120, col.R, col.G, col.B), 3f, 8);
                }

                _d2d.DrawEllipse((float)cx, (float)cy, (float)r, (float)r, Color.FromArgb(160, 255, 255, 255), 3f);
                if (!n.Judged)
                    _d2d.Text("SPIN", (float)cx, (float)(cy - 20), 160, 30, Color.White, 20f, true);
                // D9：进度条按实机公式折算（转数/所需转数）
                double odS = _chart != null && _chart.Od > 0 ? Math.Max(0, Math.Min(10, _chart.Od)) : 5;
                double minRpmS = odS < 5 ? 1.5 + 0.2 * odS : 1.25 + 0.25 * odS;
                double reqS = Math.Max(1, (n.End - n.Time) / 1000.0 * minRpmS + 0.5);
                double prog = Math.Max(0, Math.Min(1, _osuSpinProgress / reqS));
                _d2d.FillRect((float)(cx - 90), (float)(cy + r + 12), 180, 10, Color.FromArgb(120, 20, 26, 40));
                _d2d.FillRect((float)(cx - 90), (float)(cy + r + 12), (float)(180 * prog), 10, col);
            }

            // ④ 光标（实机默认皮肤：白色圆环 + 顶部粉色 #FFB2CD 三角，点击放大）
            if (_osuCursorX > 0 || _osuCursorY > 0)
            {
                var cc = Color.FromArgb(230, 255, 255, 255);
                double cr = circleR * 1.1 * (_osuMouseDown ? 1.25 : 1.0);
                _d2d.DrawEllipse(_osuCursorX, _osuCursorY, (float)cr, (float)cr, cc, 1.5f);
                _d2d.DrawLine(_osuCursorX - (float)(cr * 0.5), _osuCursorY, _osuCursorX + (float)(cr * 0.5), _osuCursorY, cc, 1.2f);
                _d2d.DrawLine(_osuCursorX, _osuCursorY - (float)(cr * 0.5), _osuCursorX, _osuCursorY + (float)(cr * 0.5), cc, 1.2f);
                // 顶部粉色三角（默认皮肤 cursor.png 特征）
                float tr = (float)(cr * 0.30);
                _d2d.FillPolygon(new PointF[] { new PointF(_osuCursorX, (float)(_osuCursorY - cr - tr * 0.6)), new PointF((float)(_osuCursorX - tr), (float)(_osuCursorY - cr + tr * 0.4)),
                    new PointF((float)(_osuCursorX + tr), (float)(_osuCursorY - cr + tr * 0.4)) }, Color.FromArgb(255, 255, 178, 205));
            }
        }


        // ===== osu!standard 几何辅助（与编辑器同几何，滑条按 SliderType 采样路径） =====
        /// <summary>osu 4:3 游玩场（以框定游玩区域为可用空间，等比 512:384）。</summary>
         // —— 渲染热路径零分配复用缓冲（仅渲染线程 RenderThreadLoop 使用；容量不足时仅首帧扩容） ——
        readonly List<(double X, double Y)> _osuPts = new List<(double X, double Y)>();

        readonly List<(double X, double Y)> _osuCtrl = new List<(double X, double Y)>();

        (double X, double Y)[] _osuBez = new (double X, double Y)[8];

        PointF[] _osuPx;

        readonly Dictionary<Note, int> _osuComboNum = new Dictionary<Note, int>();

        readonly PointF[] _spinnerSp = new PointF[64];

        double[] _afCum;

        PointF[] _afPos;

        readonly List<double> _twirlBuf = new List<double>();

        readonly PointF[] _quadPts = new PointF[4];

        readonly PointF[] _arcPath = new PointF[15]; // Arcaea 3D 弧带（SEG=14 → 15 点，逐帧复用）

        readonly double[] _arcZs = new double[15];

        readonly PointF[] _arcLp = new PointF[15];

        readonly PointF[] _arcRp = new PointF[15];

        RectangleF OsuField(PlayLayout L)
        {
            double availW = Math.Max(200, L.AreaW);
            double availH = Math.Max(160, L.AreaH - 6);
            double h = availH;
            double w = h * 512.0 / 384.0;
            if (w > availW)
            {
                w = availW;
                h = w * 384.0 / 512.0;
            }

            // t63 B2：field.scale 倍率 + field.center 偏移（默认 1.0/0.5=像素不变；aspect 锁 4:3 保持）
            var fe = LC_Get("field.center");
            double cxF = L.AreaX + (availW - w) / 2 + (fe.X - 0.5) * availW;
            double cyF = L.AreaY + 6 + (availH - h) / 2 + (fe.Y - 0.5) * availH;
            double scF = LC_P("field.scale", 1.0);
            w *= scF;
            h *= scF;
            if (LC_Bool("field.aspect", true))
            {
                if (w > availW)
                {
                    w = availW;
                    h = w * 384.0 / 512.0;
                }

                if (h > availH)
                {
                    h = availH;
                    w = h * 512.0 / 384.0;
                }
            }

            return new RectangleF((float)cxF, (float)cyF, (float)w, (float)h);
        }


        static PointF OsuPt(RectangleF f, double x, double y) => new PointF((float)(f.X + x * f.Width), (float)(f.Y + y * f.Height));

        static double Clamp01(double v)
        {
            if (double.IsNaN(v) || v < 0)
                return 0;
            return v > 1 ? 1 : v;
        }


        /// <summary>滑条路径采样（归一化 0..1）：L 折线 / B 贝塞尔 / C Catmull-Rom / P 三点圆。
        /// 渲染热路径：复用实例缓冲（_osuPts/_osuCtrl/_osuBez），结果一次性消费，零逐帧分配。</summary>
        List<(double X, double Y)> SampleOsuSlider(Note n, int segs)
        {
            var pts = _osuPts;
            pts.Clear();
            try
            {
                var ctrl = _osuCtrl;
                ctrl.Clear();
                if (n.Curve != null && n.Curve.Count >= 2)
                    foreach (var p in n.Curve)
                        ctrl.Add((Clamp01(p.X), Clamp01(p.Y)));
                else
                {
                    ctrl.Add((Clamp01(n.X), Clamp01(n.Y)));
                    ctrl.Add((Clamp01(n.EndX), Clamp01(n.EndY)));
                }

                if (ctrl.Count >= 2)
                {
                    ctrl[0] = (Clamp01(n.X), Clamp01(n.Y));
                    ctrl[ctrl.Count - 1] = (Clamp01(n.EndX), Clamp01(n.EndY));
                }

                if (ctrl.Count < 2)
                    return pts;
                char st = n.SliderType;
                if (st == 'B')
                {
                    int bc = Math.Min(4, ctrl.Count);
                    if (_osuBez.Length < bc)
                        _osuBez = new (double X, double Y)[bc];
                    for (int s = 0; s <= segs; s++)
                        pts.Add(BezierPointNorm(ctrl, s / (double)segs, bc, _osuBez));
                }
                else if (st == 'P')
                {
                    PerfectCircleFill(ctrl, segs, pts);
                    if (pts.Count < 2)
                        foreach (var p in ctrl)
                            pts.Add(p);
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
                    foreach (var p in ctrl)
                        pts.Add(p);
                }
            }
            catch
            {
            }

            if (pts.Count < 2)
            {
                pts.Clear();
                pts.Add((Clamp01(n.X), Clamp01(n.Y)));
                pts.Add((Clamp01(n.EndX), Clamp01(n.EndY)));
            }

            return pts;
        }


        /// <summary>De Casteljau 就地细分（复用 scratch 数组，零分配；count 为控制点数）。</summary>
        static (double X, double Y) BezierPointNorm(List<(double X, double Y)> p, double t, int count, (double X, double Y)[] scratch)
        {
            int n = count;
            for (int i = 0; i < n; i++)
                scratch[i] = p[i];
            while (n > 1)
            {
                for (int i = 0; i < n - 1; i++)
                    scratch[i] = (scratch[i].X + (scratch[i + 1].X - scratch[i].X) * t, scratch[i].Y + (scratch[i + 1].Y - scratch[i].Y) * t);
                n--;
            }

            return scratch[0];
        }


        static (double X, double Y) CatmullPointNorm(List<(double X, double Y)> p, double t, int i)
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


        static double NormalizeAngle(double a)
        {
            a = a % (2 * Math.PI);
            if (a > Math.PI)
                a -= 2 * Math.PI;
            if (a < -Math.PI)
                a += 2 * Math.PI;
            return a;
        }


        static bool CircumCircleD(double x1, double y1, double x2, double y2, double x3, double y3, out double cx, out double cy, out double r)
        {
            cx = cy = r = 0;
            double d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
            if (Math.Abs(d) < 1e-9)
                return false;
            double a = x1 * x1 + y1 * y1, b = x2 * x2 + y2 * y2, c = x3 * x3 + y3 * y3;
            cx = (a * (y2 - y3) + b * (y3 - y1) + c * (y1 - y2)) / d;
            cy = (a * (x3 - x2) + b * (x1 - x3) + c * (x2 - x1)) / d;
            r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
            return r > 1e-6;
        }


        /// <summary>三点定圆弧采样（P 完美圆）：填充到调用方缓存的 pts（零分配）。</summary>
        static void PerfectCircleFill(List<(double X, double Y)> ctrl, int segs, List<(double X, double Y)> pts)
        {
            pts.Clear();
            try
            {
                if (ctrl.Count < 3)
                    return;
                var A = ctrl[0];
                var B = ctrl[1];
                var C = ctrl[2];
                if (!CircumCircleD(A.X, A.Y, B.X, B.Y, C.X, C.Y, out var cx, out var cy, out var r))
                    return;
                double aA = Math.Atan2(A.Y - cy, A.X - cx);
                double aB = Math.Atan2(B.Y - cy, B.X - cx);
                double aC = Math.Atan2(C.Y - cy, C.X - cx);
                double dB = NormalizeAngle(aB - aA);
                double dC = NormalizeAngle(aC - aA);
                double sweep;
                if (Math.Abs(dC) < 1e-9 && Math.Abs(dB) > 1e-9)
                    sweep = dB > 0 ? 2 * Math.PI : -2 * Math.PI;
                else
                {
                    sweep = dC;
                    bool bIn = sweep >= 0 ? (dB >= -1e-9 && dB <= sweep + 1e-9) : (dB <= 1e-9 && dB >= sweep - 1e-9);
                    if (!bIn && Math.Abs(dB) > 1e-9)
                        sweep = dC - Math.Sign(dC) * 2 * Math.PI;
                }

                for (int s = 0; s <= segs; s++)
                {
                    double a = aA + sweep * (s / (double)segs);
                    pts.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
                }
            }
            catch
            {
            }
        }


        /// <summary>滑条往返映射：frac∈[0,1] → 路径进度 t∈[0,1]（折返时反向）。</summary>
        static double SliderBallT(double frac, int repeats)
        {
            if (repeats < 1)
                repeats = 1;
            double t = frac * repeats;
            int leg = (int)Math.Floor(t);
            double local = t - leg;
            return (leg % 2 == 0) ? local : 1 - local;
        }


        /// <summary>沿采样折线按弧长取点 t∈[0,1]（零分配：两遍线性扫描替代累计弧长数组）。</summary>
        static (double X, double Y) SliderPointAt(List<(double X, double Y)> pts, double t)
        {
            if (pts == null || pts.Count == 0)
                return (0, 0);
            if (pts.Count == 1)
                return pts[0];
            int n = pts.Count - 1;
            double total = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }

            if (total <= 1e-9)
                return pts[0];
            double target = Math.Max(0, Math.Min(1, t)) * total;
            double acc = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y;
                double seg = Math.Sqrt(dx * dx + dy * dy);
                if (acc + seg >= target || i == n - 1)
                {
                    double k = seg > 1e-9 ? (target - acc) / seg : 0;
                    k = Math.Max(0, Math.Min(1, k));
                    return (pts[i].X + (pts[i + 1].X - pts[i].X) * k, pts[i].Y + (pts[i + 1].Y - pts[i].Y) * k);
                }

                acc += seg;
            }

            return pts[pts.Count - 1];
        }


        /* ===================== ADOFAI（冰与火之舞：旋转路径单键） ===================== */
        void DrawAdofai(int W, int H, double now, PlayLayout L)
        {
            try
            {
                double cx = L.CenterX, cy = L.CenterY;
                double R = Math.Min(L.PlayW, H) * 0.34;
                if (R < 70)
                    R = 70;
                double bpm = _chart != null && _chart.Bpm > 0 ? _chart.Bpm : 120;
                double beatMs = 60000.0 / bpm;
                int cnt = _notes.Count;
                if (cnt == 0)
                {
                    _d2d.DrawEllipse((float)cx, (float)cy, (float)R, (float)R, Color.FromArgb(80, 255, 255, 255), 2f);
                    return;
                }

                double t0 = Math.Max(0, _notes[0].Time);
                double wheelDeg = 180.0 * (now - t0) / beatMs; // 每拍转 180°，随时间平滑旋转
                // 累计转角（度）：Kind 存 angleData 转角；全部为 0 时回退为按拍差线性，保证 tile 正点到底
                bool hasAngle = false;
                for (int i = 0; i < cnt; i++)
                    if (_notes[i].Kind != 0)
                    {
                        hasAngle = true;
                        break;
                    }

                if (_afCum == null || _afCum.Length < cnt)
                    _afCum = new double[cnt];
                var cum = _afCum;
                double acc = 0;
                for (int i = 0; i < cnt; i++)
                {
                    acc += hasAngle ? _notes[i].Kind : 0;
                    cum[i] = acc;
                }

                if (!hasAngle)
                    for (int i = 0; i < cnt; i++)
                        cum[i] = 90.0 + 180.0 * (_notes[i].Time - t0) / beatMs;
                // tile i 的屏幕方位角（90° = 正下方）
                double ScreenDeg(int i) => cum[i] - wheelDeg;
                // 以轮缘某点为中心画菱形（sx=切向半宽, sy=径向半宽）
                void DiamondAt(double angDeg, double sx, double sy, Color c)
                {
                    double a = (angDeg - 90.0) * Math.PI / 180.0;
                    double rx = Math.Cos(a), ry = Math.Sin(a);
                    double tx = -ry, ty = rx;
                    double px = cx + rx * R, py = cy + ry * R;
                    var pts = _quadPts; // 复用缓冲（零 GC）
                    pts[0] = new PointF((float)(px + tx * sx), (float)(py + ty * sx));
                    pts[1] = new PointF((float)(px + rx * sy), (float)(py + ry * sy));
                    pts[2] = new PointF((float)(px - tx * sx), (float)(py - ty * sx));
                    pts[3] = new PointF((float)(px - rx * sy), (float)(py - ry * sy));
                    _d2d.FillPolygon(pts, c);
                    _d2d.DrawPolyline(pts, Color.FromArgb(220, 255, 255, 255), 1.6f, true);
                }

                // 深色转盘 + 霓虹描边 + 径向刻度
                _d2d.FillEllipse((float)cx, (float)cy, (float)R, (float)R, Color.FromArgb(210, 8, 12, 22));
                _d2d.DrawEllipse((float)cx, (float)cy, (float)R, (float)R, Color.FromArgb(230, 90, 220, 255), 3f);
                _d2d.DrawEllipse((float)cx, (float)cy, (float)(R * 0.9), (float)(R * 0.9), Color.FromArgb(70, 140, 200, 255), 1.5f);
                for (int seg = 0; seg < 8; seg++)
                {
                    double a = seg / 8.0 * Math.PI * 2;
                    _d2d.DrawLine((float)(cx + Math.Cos(a) * R * 0.9), (float)(cy + Math.Sin(a) * R * 0.9), (float)(cx + Math.Cos(a) * R), (float)(cy + Math.Sin(a) * R), Color.FromArgb(50, 120, 180,
                        255), 1f);
                }

                // 正下方判定标记
                _d2d.DrawEllipse((float)cx, (float)(cy + R), 14, 14, Color.FromArgb(255, 90, 220, 255), 2.5f);
                // 当前 tile（窗口内最近未判定）
                double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
                int curIdx = -1;
                double best = double.MaxValue;
                for (int i = 0; i < cnt; i++)
                {
                    var n = _notes[i];
                    if (n.Judged)
                        continue;
                    double d = Math.Abs(n.Time - now);
                    if (d > lastW + 100)
                        continue;
                    if (d < best)
                    {
                        best = d;
                        curIdx = i;
                    }
                }

                // 路径折线：霓虹发光（宽暗线 + 细亮线）；twirl 长间隔段用发光点线
                for (int i = 0; i < cnt - 1; i++)
                {
                    double a0 = (ScreenDeg(i) - 90.0) * Math.PI / 180.0;
                    double a1 = (ScreenDeg(i + 1) - 90.0) * Math.PI / 180.0;
                    double x0 = cx + Math.Cos(a0) * R, y0 = cy + Math.Sin(a0) * R;
                    double x1 = cx + Math.Cos(a1) * R, y1 = cy + Math.Sin(a1) * R;
                    bool twirl = _notes[i + 1].Time - _notes[i].Time > beatMs * 1.5;
                    int style = twirl ? 2 : 0; // twirl=发光点线
                    _d2d.DrawLine((float)x0, (float)y0, (float)x1, (float)y1, Color.FromArgb(70, 90, 160, 255), 8f, style); // 宽暗发光
                    _d2d.DrawLine((float)x0, (float)y0, (float)x1, (float)y1, Color.FromArgb(230, 140, 220, 255), 2.5f, style); // 细亮线
                }

                // tile 菱形（hold 画成拉长的菱形链）
                double s = Math.Max(7, R * 0.11);
                for (int i = 0; i < cnt; i++)
                {
                    var n = _notes[i];
                    if (n.Judged && n.Judgment == "MISS" && now > n.End)
                        continue;
                    double sd = ScreenDeg(i);
                    bool isCur = i == curIdx;
                    var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                    if (IsHoldNote(n))
                    {
                        DiamondAt(sd, s * 2.2, s, col); // 主菱形（切向拉长）
                        DiamondAt(sd - 15, s * 0.7, s * 0.7, Color.FromArgb(150, col.R, col.G, col.B));
                        DiamondAt(sd + 15, s * 0.7, s * 0.7, Color.FromArgb(150, col.R, col.G, col.B));
                    }
                    else
                    {
                        DiamondAt(sd, s * (isCur ? 1.5 : 1.0), s * (isCur ? 1.5 : 1.0), col);
                    }

                    if (isCur)
                    {
                        // 当前 tile 外圈脉冲环
                        double ca = (sd - 90.0) * Math.PI / 180.0;
                        double pr = s * (2.0 + 0.6 * Math.Sin(now / 110.0));
                        _d2d.DrawEllipse((float)(cx + Math.Cos(ca) * R), (float)(cy + Math.Sin(ca) * R), (float)pr, (float)pr, Color.FromArgb(230, 255, 255, 255), 2f);
                        _d2d.DrawEllipse((float)(cx + Math.Cos(ca) * R), (float)(cy + Math.Sin(ca) * R), (float)(pr + 5), (float)(pr + 5), Color.FromArgb(90, 140, 220, 255), 3f);
                    }
                }

                // ===== 红蓝双轨道球（ADOFAI 招牌：火红 / 冰蓝两球对称绕路径旋转） =====
                if (curIdx >= 0)
                {
                    double ca = (ScreenDeg(curIdx) - 90.0) * Math.PI / 180.0;
                    double tx = -Math.Sin(ca), ty = Math.Cos(ca); // 切向
                    double bx = cx + Math.Cos(ca) * R, by = cy + Math.Sin(ca) * R;
                    float orbR = (float)Math.Max(5, R * 0.055);
                    float off = (float)(R * 0.075);
                    // 红球（火）：实机橙红渐变 + 中心近白高光 + 外圈辉光
                    float rx = (float)(bx + tx * off), ry = (float)(by + ty * off);
                    _d2d.FillEllipse(rx + orbR * 0.15f, ry + orbR * 0.15f, orbR * 1.15f, orbR * 1.15f, Color.FromArgb(70, 255, 122, 61)); // 橙色辉光 #FF7A3D
                    _d2d.FillEllipse(rx, ry, orbR, orbR, Color.FromArgb(255, 255, 75, 75));
                    _d2d.DrawEllipse(rx, ry, orbR, orbR, Color.White, 2f);
                    _d2d.FillEllipse(rx - orbR * 0.3f, ry - orbR * 0.3f, orbR * 0.28f, orbR * 0.28f, Color.FromArgb(255, 255, 240, 232));
                    // 蓝球（冰）：实机亮蓝渐变 + 中心近白高光 + 外圈辉光
                    float bx2 = (float)(bx - tx * off), by2 = (float)(by - ty * off);
                    _d2d.FillEllipse(bx2 + orbR * 0.15f, by2 + orbR * 0.15f, orbR * 1.15f, orbR * 1.15f, Color.FromArgb(70, 102, 194, 255)); // 亮蓝辉光 #66C2FF
                    _d2d.FillEllipse(bx2, by2, orbR, orbR, Color.FromArgb(255, 75, 160, 255));
                    _d2d.DrawEllipse(bx2, by2, orbR, orbR, Color.White, 2f);
                    _d2d.FillEllipse(bx2 - orbR * 0.3f, by2 - orbR * 0.3f, orbR * 0.28f, orbR * 0.28f, Color.FromArgb(255, 234, 246, 255));
                }
            }
            catch
            {
            }
        }


        /// <summary>真实 ADOFAI（冰与火之舞）渲染：轨道式视图——路径从中心向右延伸，每 tile 一段、
        /// 方向=累计角度（Kind 存 angleData），双球沿轨道（火球=当前拍、冰球=下一拍预览），
        /// 每拍按键输入（单键通道），角度判定+掉轨重开（Routlock=轮盘式保留）。</summary>
        void DrawAdofaiReal(int W, int H, double now, PlayLayout L)
        {
            try
            {
                double cx = L.CenterX, cy = L.CenterY;
                double segLen = Math.Min(L.PlayW, H) * 0.10; // 每 tile 段长
                int cnt = _notes.Count;
                if (cnt == 0)
                    return;
                // 当前拍索引（最近未判定）
                double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
                int curIdx = -1;
                double best = double.MaxValue;
                for (int i = 0; i < cnt; i++)
                {
                    var n = _notes[i];
                    if (n.Judged)
                        continue;
                    double d = Math.Abs(n.Time - now);
                    if (d > lastW + 200)
                        continue;
                    if (d < best)
                    {
                        best = d;
                        curIdx = i;
                    }
                }

                if (curIdx < 0)
                    return;
                // 累计角度（度）：路径方向 = 起始 0°（向右）+ 各 tile Kind 转角累计
                if (_afCum == null || _afCum.Length < cnt)
                    _afCum = new double[cnt];
                var cum = _afCum;
                double acc = 0;
                bool hasAngle = false;
                for (int i = 0; i < cnt; i++)
                    if (_notes[i].Kind != 0)
                    {
                        hasAngle = true;
                        break;
                    }

                for (int i = 0; i < cnt; i++)
                {
                    acc += hasAngle ? _notes[i].Kind : 0;
                    cum[i] = acc;
                }

                // 背景：深空渐变（实机亮度参照——FrameStat 实测实机游玩段 34/28/45~29/27/38、
                // 暗部 60-77%；一轮提亮后程序游玩区 20/24/36 仍偏暗，二轮再提亮）
                _d2d.FillVerticalGradient((float)L.AreaX, (float)L.AreaY, (float)L.AreaW, (float)L.AreaH, Color.FromArgb(255, 40, 34, 78), Color.FromArgb(255, 18, 16, 44));
                // 星空点缀（实机背景有星星/光点，增加亮度层次）
                for (int si = 0; si < 34; si++)
                {
                    double sx = L.AreaX + (Math.Sin(si * 12.9898) * 43758.5453 % 1 + 1) * 0.5 * L.AreaW;
                    double sy = L.AreaY + (Math.Cos(si * 78.233) * 12543.123 % 1 + 1) * 0.5 * L.AreaH;
                    double tw = 0.4 + 0.6 * Math.Sin(now / 900.0 + si);
                    _d2d.FillEllipse((float)sx, (float)sy, (float)(1.4 + tw * 1.3), (float)(1.4 + tw * 1.3), Color.FromArgb((int)(50 + 70 * tw), 200, 210, 255));
                }

                // 单路径（策划规格 0b 修正：双球机制 = 一球固定于当前 tile、另一球绕其旋转，每拍按键跳转+角色互换）
                // 路径：从中心向右延伸（每 tile 段，方向=累计角），画当前 tile 及其后 10 段 + 前 3 段
                // MoveTrack 消费：最近 movetrack/positiontrack 事件 → 轨道偏移(px)/旋转(°)/缩放
                double trackDx = 0, trackDy = 0, trackRot = 0, trackScale = 1;
                if (_chart != null && _chart.Events != null)
                    foreach (var ev in _chart.Events)
                    {
                        if (ev == null || (ev.Type != "movetrack" && ev.Type != "positiontrack"))
                            continue;
                        if (ev.Time > now)
                            break;
                        trackDx = ev.Value;
                        trackDy = 0;
                        trackRot = ev.EndValue;
                        trackScale = ev.Line > 0 ? ev.Line : 1;
                    }

                double cosR = Math.Cos(trackRot * Math.PI / 180.0), sinR = Math.Sin(trackRot * Math.PI / 180.0);
                PointF TilePos(int i)
                {
                    double a = cum[i] * Math.PI / 180.0;
                    int rel = i - curIdx;
                    double x = cx + rel * segLen * Math.Cos(a) * trackScale;
                    double y = cy + rel * segLen * Math.Sin(a) * 0.55 * trackScale;
                    // 旋转 + 位移
                    double rx = x - cx, ry = y - cy;
                    x = cx + rx * cosR - ry * sinR + trackDx;
                    y = cy + rx * sinR + ry * cosR + trackDy;
                    return new PointF((float)x, (float)y);
                }

                if (_afPos == null || _afPos.Length < cnt)
                    _afPos = new PointF[cnt];
                var pos = _afPos;
                for (int i = 0; i < cnt; i++)
                    pos[i] = TilePos(i);
                // 路径线段（霓虹发光；Twirl 区间用粉紫虚线——策划规格：Twirl 反转旋转方向提示）
                bool InTwirl(double t)
                {
                    if (_chart == null || _chart.Events == null)
                        return false;
                    var ts = _twirlBuf; // 复用缓冲（零 GC）
                    ts.Clear();
                    foreach (var ev in _chart.Events)
                        if (ev != null && ev.Type == "twirl")
                            ts.Add(ev.Time);
                    ts.Sort();
                    for (int k = 0; k + 1 < ts.Count; k += 2)
                        if (t > ts[k] && t < ts[k + 1])
                            return true;
                    return ts.Count % 2 == 1 && t > ts[ts.Count - 1];
                }

                for (int i = Math.Max(0, curIdx - 3); i < Math.Min(cnt - 1, curIdx + 10); i++)
                {
                    bool twirl = InTwirl(_notes[i].Time) || InTwirl(_notes[i + 1].Time);
                    int style = twirl ? 2 : 0;
                    var p0 = pos[i];
                    var p1 = pos[i + 1];
                    // 实机霓虹轨道：外层宽光晕 + 内层亮芯（三轮提亮：ZoneDiff 实测实机轨道区亮度 80-94 vs 程序 18-30，
                    // 轨道亮度仍差 3 倍——光晕 26f、内芯 7f 且更高 alpha）
                    _d2d.DrawLine(p0.X, p0.Y, p1.X, p1.Y, twirl ? Color.FromArgb(170, 235, 110, 185) : Color.FromArgb(170, 150, 235, 255), 26f, style);
                    _d2d.DrawLine(p0.X, p0.Y, p1.X, p1.Y, twirl ? Color.FromArgb(255, 255, 175, 225) : Color.FromArgb(255, 215, 250, 255), 7f, style);
                    _d2d.DrawLine(p0.X, p0.Y, p1.X, p1.Y, Color.FromArgb(120, 255, 255, 255), 12f, style); // 白芯补充
                }

                // tile 菱形（单路径）
                double s = Math.Max(7, segLen * 0.45);
                for (int i = Math.Max(0, curIdx - 3); i < Math.Min(cnt, curIdx + 10); i++)
                {
                    var n = _notes[i];
                    if (n.Judged && n.Judgment == "MISS" && now > n.End)
                        continue;
                    var p = pos[i];
                    double a = cum[i] * Math.PI / 180.0;
                    // 实机 tile：白亮发光方形（FrameStat：实机亮部约 1.1% 且为高亮白/蓝白，程序砖块需提亮）
                    var col = NoteColCurrent(); // t25：主色读 NoteStyleBook
                    col = Color.FromArgb(255, (int)Math.Min(255, col.R * 1.5 + 80), (int)Math.Min(255, col.G * 1.5 + 80), (int)Math.Min(255, col.B * 1.5 + 80));
                    bool isCur = i == curIdx;
                    double sc = isCur ? 1.5 : 1.0;
                    // 实机 tile = 发光方形砖块（策划规格 §3）：正方形沿路径方向摆放（面朝路径），白亮描边
                    double ca = Math.Cos(a), sa = Math.Sin(a) * 0.55;
                    double nx2 = -sa, ny2 = ca; // 法向
                    double hx = (ca * 0.7071 + nx2 * 0.7071) * s * sc;
                    double hy = (sa * 0.7071 + ny2 * 0.7071) * s * sc;
                    double hx2 = (ca * 0.7071 - nx2 * 0.7071) * s * sc;
                    double hy2 = (sa * 0.7071 - ny2 * 0.7071) * s * sc;
                    var pts = _quadPts; // 复用缓冲（零 GC）
                    pts[0] = new PointF((float)(p.X + hx), (float)(p.Y + hy));
                    pts[1] = new PointF((float)(p.X + hx2), (float)(p.Y + hy2));
                    pts[2] = new PointF((float)(p.X - hx), (float)(p.Y - hy));
                    pts[3] = new PointF((float)(p.X - hx2), (float)(p.Y - hy2));
                    _d2d.FillPolygon(pts, col);
                    _d2d.DrawPolyline(pts, Color.FromArgb(240, 255, 255, 255), isCur ? 2.5f : 1.6f, true);
                    if (isCur)
                    {
                        double pr = s * (2.0 + 0.6 * Math.Sin(now / 110.0));
                        _d2d.DrawEllipse(p.X, p.Y, (float)pr, (float)pr, Color.FromArgb(230, 255, 255, 255), 2f);
                    }
                }

                // 双球（策划规格 0b 修正：一球固定于当前 tile，另一球绕其旋转；每拍按键跳转+角色互换 SwitchChosen）
                var curN = _notes[curIdx];
                var prevN = curIdx > 0 ? _notes[curIdx - 1] : null;
                double prog = Math.Max(0, Math.Min(1, (now - (prevN != null ? prevN.Time : curN.Time - 500)) / Math.Max(1, curN.Time - (prevN != null ? prevN.Time : curN.Time - 500))));
                float orbR = (float)Math.Max(6, segLen * 0.26);
                var fixedP = pos[curIdx];
                // 绕旋球：绕固定球旋转（角度 = 进度×360 + 路径方向偏移；实机绕旋球沿弧线滑向下一 tile）
                double curA = cum[curIdx] * Math.PI / 180.0;
                double orbitA = curA + prog * 2.0 * Math.PI; // 每拍绕一圈
                float orbitR = (float)(segLen * 0.85);
                double ox = fixedP.X + Math.Cos(orbitA) * orbitR;
                double oy = fixedP.Y + Math.Sin(orbitA) * orbitR * 0.55;
                // 火球（当前=固定球），冰球（绕旋球）——实机每拍后互换，此处视觉上绕旋球落向下一 tile
                // 实机双球鲜艳发光（火红/冰蓝），加大光晕提亮
                _d2d.FillEllipse(fixedP.X + orbR * 0.2f, fixedP.Y + orbR * 0.2f, orbR * 2.0f, orbR * 2.0f, Color.FromArgb(70, 255, 90, 30));
                _d2d.FillEllipse(fixedP.X, fixedP.Y, orbR, orbR, Color.FromArgb(255, 255, 82, 60));
                _d2d.DrawEllipse(fixedP.X, fixedP.Y, orbR, orbR, Color.White, 2.4f);
                _d2d.FillEllipse((float)ox + orbR * 0.2f, (float)oy + orbR * 0.2f, orbR * 2.0f, orbR * 2.0f, Color.FromArgb(70, 80, 190, 255));
                _d2d.FillEllipse((float)ox, (float)oy, orbR, orbR, Color.FromArgb(255, 90, 170, 255));
                _d2d.DrawEllipse((float)ox, (float)oy, orbR, orbR, Color.White, 2.4f);
            }
            catch
            {
            }
        }


        void DrawIidx(int W, int H, double now, PlayLayout L, double topY, double hitY)
        {
            try
            {
                const int kc = 8;
                double laneW = L.PlayW / kc;
                double ppms = (hitY - topY) * GameSettings.Speed / 1000.0;
                // 轨道背景：转盘轨（Col 0）蓝色底 + 斜纹，白键轨淡灰（IIDX 1P 蓝转盘）
                // 实机亮度参照：FrameStat 实测 84/98/97、亮部 17.9%、饱和 27%——提亮轨道底色
                for (int i = 0; i < kc; i++)
                {
                    double x = L.PlayX + i * laneW;
                    if (i == 0)
                    {
                        _d2d.FillRect((float)x, (float)topY, (float)laneW, (float)(H - topY), Color.FromArgb(120, 40, 70, 120));
                        for (double yy = topY - laneW; yy < H + laneW; yy += 24)
                            _d2d.DrawLine((float)x, (float)yy, (float)(x + laneW), (float)(yy - laneW), Color.FromArgb(100, 140, 210, 255), 1.5f);
                    }
                    else
                        _d2d.FillRect((float)x, (float)topY, (float)laneW, (float)(H - topY), i % 2 == 0 ? Color.FromArgb(70, 216, 222, 234) : Color.FromArgb(58, 206, 213, 226));
                }

                // 转盘轨与白键轨之间加粗分隔线
                double sepX = L.PlayX + laneW;
                _d2d.DrawLine((float)sepX, (float)topY, (float)sepX, (float)H, Color.FromArgb(150, 90, 160, 255), 2f);
                for (int i = 2; i < kc; i++)
                {
                    double x = L.PlayX + i * laneW;
                    _d2d.DrawLine((float)x, (float)topY, (float)x, (float)H, Color.FromArgb(40, 90, 110, 150), 1f);
                }

                // 判定线 + 底部转盘旋钮（蓝色 1P；旋转辐条随按住/时间转动，IIDX 实机转盘感）
                _d2d.DrawLine((float)L.PlayX, (float)hitY, (float)(L.PlayX + L.PlayW), (float)hitY, Skin.HitLineColor, Skin.HitLineThickness + 2, Skin.HitLineStyle);
                double scratchCX = L.PlayX + laneW / 2, scratchCY = hitY + 26;
                double spin = now / 90.0 + (_pressFlash[0] > 0.05f ? MonoMs() / 40.0 : 0);
                _d2d.FillEllipse((float)scratchCX, (float)scratchCY, (float)(laneW * 0.36), (float)(laneW * 0.36), Color.FromArgb(255, 90, 160, 255));
                _d2d.DrawEllipse((float)scratchCX, (float)scratchCY, (float)(laneW * 0.36), (float)(laneW * 0.36), Color.White, 2f);
                for (int s = 0; s < 3; s++)
                {
                    double a = spin + s * Math.PI * 2 / 3.0;
                    _d2d.DrawLine((float)scratchCX, (float)scratchCY, (float)(scratchCX + Math.Cos(a) * laneW * 0.30), (float)(scratchCY + Math.Sin(a) * laneW * 0.30), Color.FromArgb(220, 255, 255,
                        255), 2.5f);
                }

                double ahead = (hitY - topY) / (ppms > 0.01 ? ppms : 0.5) + 300;
                double below = (H - topY + NoteThickness * 2) / (ppms > 0.01 ? ppms : 0.5);
                // hold 长条
                double holdCut = now - below - 300;
                while (_holdIdx < _holdNotes.Count && _holdNotes[_holdIdx].End < holdCut)
                    _holdIdx++;
                for (int hi = _holdIdx; hi < _holdNotes.Count; hi++)
                {
                    var n = _holdNotes[hi];
                    if (n.Time - now > ahead)
                        break;
                    if (n.Judged && n.Judgment == "MISS" && now > n.End)
                        continue;
                    double yHead = hitY - (n.Time - now) * ppms;
                    double yTail = hitY - (n.End - now) * ppms;
                    double y1 = Math.Max(yTail, topY), y2 = Math.Min(yHead, H);
                    if (y2 <= y1)
                        continue;
                    double xc = L.PlayX + (n.Col + 0.5) * laneW;
                    if (n.Col == 0)
                        _d2d.FillRoundedRect((float)(xc - laneW * 0.30), (float)y1, (float)(laneW * 0.60), (float)(y2 - y1), 6, Color.FromArgb(160, 63, 127, 219));
                    else
                        // CN 长条：头+半透明浅蓝白条体（实机 #DFF0FF 半透明）
                        _d2d.FillRoundedRect((float)(xc - laneW * 0.30), (float)y1, (float)(laneW * 0.60), (float)(y2 - y1), 6, Color.FromArgb(150, 223, 240, 255));
                }

                // tap：白键近白 #F8F8F8 圆角横条（实机宽高比约 3:1）/ 转盘蓝 #3F7FDB 横条
                int t0 = LowerBound(now - below);
                for (int i = t0; i < _notes.Count; i++)
                {
                    var n = _notes[i];
                    if (n.Time - now > ahead)
                        break;
                    if (IsHoldNote(n) || n.Judged)
                        continue;
                    if (n.Col < 0 || n.Col > 7)
                        continue;
                    double yHead = hitY - (n.Time - now) * ppms;
                    if (yHead < topY - 30 || yHead > H + 30)
                        continue;
                    double xc = L.PlayX + (n.Col + 0.5) * laneW;
                    double nw = laneW * 0.64, nh = Math.Max(8, laneW * 0.21); // 3:1 圆角横条
                    if (n.Col == 0)
                    {
                        // 转盘音符：与白键同形横条，蓝色 #3F7FDB（实机经典纯蓝条）
                        _d2d.FillRoundedRect((float)(xc - nw / 2), (float)(yHead - nh / 2), (float)nw, (float)nh, 5, NoteColCurrent()); // t25：IIDX 主色读 NoteStyleBook
                        _d2d.DrawRoundedRect((float)(xc - nw / 2), (float)(yHead - nh / 2), (float)nw, (float)nh, 5, Color.FromArgb(190, 255, 255, 255), 1.5f);
                    }
                    else
                    {
                        // 白键音符：近白 #F8F8F8 + 细深色描边 + 顶部高光
                        _d2d.FillRoundedRect((float)(xc - nw / 2), (float)(yHead - nh / 2), (float)nw, (float)nh, 5, Color.FromArgb(255, 248, 248, 248));
                        _d2d.DrawRoundedRect((float)(xc - nw / 2), (float)(yHead - nh / 2), (float)nw, (float)nh, 5, Color.FromArgb(150, 110, 110, 110), 1.2f);
                        _d2d.FillRoundedRect((float)(xc - nw / 2 + 2), (float)(yHead - nh / 2 + 1), (float)(nw - 4), (float)Math.Max(2, nh * 0.18f), 2, Color.FromArgb(120, 255, 255, 255));
                    }
                }
            }
            catch
            {
            }
        }


        /* ===================== 结算画面 ===================== */
        void DrawResultScreen(int W, int H, double mono)
        {
            var res = _result;
            double t = (mono - _resultStart) / 1000.0;
            // 背景暗化渐变
            double dim = Math.Max(0, Math.Min(1, t / 0.4));
            _d2d.FillRect(0, 0, W, H, Color.FromArgb((int)(dim * 170), 5, 8, 14));
            float panelW = (float)Math.Min(W * 0.72, 900);
            float panelH = (float)Math.Min(H * 0.64, 580);
            float px = W / 2f - panelW / 2;
            float py = H / 2f - panelH / 2;
            double slide = Math.Max(0, Math.Min(1, t / 0.4));
            double eb = ApplyEase("EaseOutBack", slide);
            float py2 = (float)(H + 40 + (py - (H + 40)) * eb);
            if (slide <= 0)
                return; // 面板出现前不画内容
            // 面板卡片
            _d2d.FillRoundedRect(px, py2, panelW, panelH, 18, Color.FromArgb(240, 16, 22, 36));
            _d2d.DrawRoundedRect(px, py2, panelW, panelH, 18, Color.FromArgb(120, 120, 150, 200), 1.5f);
            float cx = px + panelW / 2;
            float titleY = py2 + 36;
            float gradeY = py2 + 122;
            // 标题 / 艺术家 / 模式
            _d2d.Text(res.Title, cx, titleY, panelW - 40, 30, Color.White, 22f, true);
            string sub = res.Artist + (string.IsNullOrEmpty(res.ModeName) ? "" : " · " + res.ModeName);
            _d2d.Text(sub, cx, titleY + 30, panelW - 40, 20, Color.FromArgb(200, 150, 165, 200), 12f, true);
            // 评级字母（放大弹出 + 外圈辉光）
            if (t >= 0.5)
            {
                double gp = Math.Max(0, Math.Min(1, (t - 0.5) / 0.5));
                double gs = ApplyEase("EaseOutBack", gp);
                var gc = GradeColor(res.Grade);
                float gsSize = 84f * (float)gs;
                _d2d.DrawEllipse(cx, gradeY, gsSize * 0.9f, gsSize * 0.9f, Color.FromArgb((int)(140 * gp), gc.R, gc.G, gc.B), 5f);
                _d2d.Text(res.Grade, cx, gradeY, 200, gsSize, gc, gsSize, true);
            }

            float statY = py2 + 192;
            // 分数滚动计数
            if (t >= 0.8)
            {
                double sp = Math.Max(0, Math.Min(1, (t - 0.8) / 1.0));
                double e = 1 - Math.Pow(1 - sp, 3);
                int shown = (int)(res.Score * e);
                _d2d.Text(shown.ToString("N0"), cx, statY, panelW - 80, 34, Color.White, 28f, true);
            }

            // ACC / 最大连击
            if (t >= 1.0)
            {
                double ap = Math.Max(0, Math.Min(1, (t - 1.0) / 0.6));
                string accLine;
                if (_result != null && _result.ModeName == "ADOFAI")
                    accLine = "完成度 " + _result.MaxCombo + "% · 精准度 " + _result.Acc.ToString("0.00") + "%";
                else
                    accLine = "ACC " + res.Acc.ToString("0.00") + "% · 最大连击 " + res.MaxCombo;
                _d2d.Text(accLine, cx, statY + 34, panelW - 80, 20, Color.FromArgb((int)(ap * 255), 127, 208, 160), 14f, true);
            }

            // 判定统计条
            float jy = statY + 70;
            int total = res.TotalNotes > 0 ? res.TotalNotes : SumHits();
            var order = new List<string>();
            foreach (var lv in JudgeSettings.Levels)
                order.Add(lv.Name);
            order.Add("MISS");
            int rowH = 22;
            float barW = panelW * 0.5f;
            float nameW = 120;
            float countW = 70;
            for (int i = 0; i < order.Count; i++)
            {
                string name = order[i];
                int cnt = res.Hits != null && res.Hits.TryGetValue(name, out var v) ? v : 0;
                double bp = Math.Max(0, Math.Min(1, (t - 1.2 - i * 0.08) / 0.4));
                if (bp <= 0)
                    continue;
                float rowY = jy + i * rowH;
                var jc = name == "MISS" ? Color.FromArgb(255, 90, 90) : JudgeColors[Math.Min(i, JudgeColors.Length - 1)];
                _d2d.Text(name, px + 30, rowY + 11, nameW, 18, jc, 11f);
                _d2d.Text(cnt.ToString(), px + 30 + nameW, rowY + 11, countW, 18, Color.White, 11f);
                float bx = px + 30 + nameW + countW;
                float frac = total > 0 ? (float)cnt / total : 0;
                _d2d.FillRect(bx, rowY + 4, barW, 12, Color.FromArgb(120, 30, 38, 54));
                _d2d.FillRect(bx, rowY + 4, barW * frac * (float)bp, 12, jc);
            }

            // 段位徽章
            if (res.IsDan)
            {
                float danY = jy + order.Count * rowH + 24;
                bool pass = res.DanPass;
                var dc = pass ? Color.FromArgb(255, 90, 220, 140) : Color.FromArgb(255, 255, 90, 90);
                string danText = pass ? "PASS" : "FAIL";
                _d2d.FillRoundedRect(cx - 70, danY, 140, 40, 10, Color.FromArgb(180, dc.R / 4, dc.G / 4, dc.B / 4));
                _d2d.DrawRoundedRect(cx - 70, danY, 140, 40, 10, dc, 2f);
                _d2d.Text(danText, cx, danY + 20, 120, 24, dc, 18f, true);
                if (pass)
                    _d2d.Text("HP 剩余 " + res.HpEnd.ToString("0") + " / " + JudgeSettings.HpMax.ToString("0"), cx, danY + 48, 200, 18, Color.FromArgb(220, 200, 210, 230), 11f, true);
            }

            // 新纪录横幅
            if (res.NewBest && t >= 1.4)
            {
                double np = Math.Max(0, Math.Min(1, (t - 1.4) / 0.3));
                _d2d.FillRect(px, py2 - 14, panelW, 34, Color.FromArgb((int)(np * 200), 60, 40, 8));
                _d2d.Text("🏆 新纪录！", cx, py2 + 3, panelW, 24, Color.FromArgb((int)(np * 255), 255, 210, 63), 16f, true);
            }

            // 底部按钮 + 提示
            float byBtn = py2 + panelH - 104;
            float bw = Math.Min(220, panelW * 0.4f);
            float bh = 44;
            float totalB = bw * 2 + 24;
            float startX = cx - totalB / 2;
            _d2d.FillRoundedRect(startX, byBtn, bw, bh, 10, Color.FromArgb(220, 30, 60, 40));
            _d2d.DrawRoundedRect(startX, byBtn, bw, bh, 10, Color.FromArgb(255, 90, 220, 140), 1.5f);
            _d2d.Text("重新开始 (R)", startX + bw / 2, byBtn + 22, bw - 20, 20, Color.White, 13f, true);
            _d2d.FillRoundedRect(startX + bw + 24, byBtn, bw, bh, 10, Color.FromArgb(220, 60, 30, 40));
            _d2d.DrawRoundedRect(startX + bw + 24, byBtn, bw, bh, 10, Color.FromArgb(255, 255, 120, 120), 1.5f);
            _d2d.Text("返回主菜单 (ESC)", startX + bw + 24 + bw / 2, byBtn + 22, bw - 20, 20, Color.White, 13f, true);
            _d2d.Text("[R/回车] 重新开始 · [退格] 返回选歌 · [ESC] 主菜单", cx, py2 + panelH - 48, panelW - 40, 20, Color.FromArgb(220, 150, 165, 200), 10.5f, true);
        }


        /// <summary>maimai 外圈半径（大环，占游玩区短边一半）。</summary>
        int _maimaiColWarn; // t61：方位越界一次性告警

        double MaimaiRingR(PlayLayout L)
        {
            double side = Math.Min(L.AreaW, L.AreaH);
            // t63 B2：maimai ring.radius 倍率（默认 1.0=像素不变）
            return Math.Max(120, side * 0.36) * LC_P("ring.radius", 1.0);
        }


        /// <summary>maimai 音符扩散进度：从圆心向外圈按钮飞行，判定时刻到达按钮。</summary>
        double MaimaiApproachMs(PlayLayout L)
        {
            // 实机约 1 拍接近时间；用速度换算（Ppm 像素/毫秒）
            double ppms = L.Ppm;
            if (ppms <= 0.05)
                return 900;
            return Math.Max(400, Math.Min(1400, MaimaiRingR(L) / ppms)) * LC_P("note.approach", 1.0); // t63 B2：接近时间倍率
        }


        /// <summary>maimai 接近进度 0..1（用于缩放/渐显；音符恒在按钮方位上，不再落环心）。</summary>
        double MaimaiApproachK(Note n, PlayLayout L, double now)
        {
            double approach = MaimaiApproachMs(L);
            double t0 = n.Time - approach;
            return Math.Max(0, Math.Min(1, (now - t0) / approach));
        }


        /// <summary>maimai 音符屏幕位置（t61）：恒在按钮方位上（环上，不落环心）；接近进度见 MaimaiApproachK。</summary>
        PointF MaimaiNotePos(Note n, PlayLayout L, double now)
        {
            MaimaiButtonPos(n, L, out var bx, out var by);
            return new PointF((float)bx, (float)by);
        }


        /// <summary>maimai 渲染：外圈 8 按钮环 + 中央 A~F touch，音符从圆心扩散到按钮。</summary>
        void DrawMaimai(int W, int H, double now, PlayLayout L)
        {
            try
            {
                double cx = L.CenterX, cy = L.CenterY;
                double R = MaimaiRingR(L);
                double approach = MaimaiApproachMs(L);
                // 场背景：径向渐变（深蓝→黑）
                _d2d.FillVerticalGradient((float)L.AreaX, (float)L.AreaY, (float)L.AreaW, (float)L.AreaH, Color.FromArgb(210, 14, 22, 44), Color.FromArgb(210, 6, 8, 16));
                _d2d.DrawRect((float)L.AreaX, (float)L.AreaY, (float)L.AreaW, (float)L.AreaH, Color.FromArgb(70, 110, 140, 190), 1.5f);
                // 外圈轨道环
                var ringCol = Color.FromArgb(110, 120, 160, 210);
                _d2d.DrawEllipse((float)cx, (float)cy, (float)R, (float)R, ringCol, 2.5f);
                _d2d.DrawEllipse((float)cx, (float)cy, (float)(R * 0.94), (float)(R * 0.94), Color.FromArgb(40, 90, 120, 170), 1.2f);
                // 外圈 8 按钮 + 中央 A~F
                for (int k = 1; k <= 8; k++)
                {
                    MaimaiButtonPos(new Note { Col = k }, L, out var bxD, out var byD);
                    float bx = (float)bxD, by = (float)byD;
                    float br = (float)(R * 0.16);
                    int col = k;
                    var bc = MaimaiButtonColor(col, false);
                    _d2d.FillEllipse(bx, by, br, br, bc);
                    _d2d.DrawEllipse(bx, by, br, br, Color.FromArgb(150, 200, 220, 255), 2f);
                    // 按键闪光
                    if (_pressFlash[col] > 0.05f)
                        _d2d.DrawEllipse(bx, by, br + _pressFlash[col] * 26, br + _pressFlash[col] * 26, Color.FromArgb((int)(200 * _pressFlash[col]), 255, 255, 255), 2f);
                }

                // 中央 A~F（引擎使用者定案：设计特殊键圈，非映射缺失）——五角环绕绘制保留
                for (int idx = 0; idx < 6; idx++)
                {
                    double a = (-90 + (idx - 1) * 72) * Math.PI / 180.0;
                    double tx = cx, ty = cy;
                    if (idx > 0)
                    {
                        tx = cx + Math.Cos(a) * R * 0.30;
                        ty = cy + Math.Sin(a) * R * 0.30;
                    }

                    float tr = (float)(R * 0.10);
                    int col = 9 + idx;
                    var tc = MaimaiButtonColor(col, true);
                    _d2d.FillEllipse((float)tx, (float)ty, tr, tr, tc);
                    _d2d.DrawEllipse((float)tx, (float)ty, tr, tr, Color.FromArgb(140, 255, 214, 170), 1.8f);
                    if (_pressFlash[col] > 0.05f)
                        _d2d.DrawEllipse((float)tx, (float)ty, tr + _pressFlash[col] * 20, tr + _pressFlash[col] * 20, Color.FromArgb((int)(180 * _pressFlash[col]), 255, 230, 180), 2f);
                }

                // 音符：恒在所属方位上（环上缩放接近，不再落环心）；hold/slide 到达按钮后驻留显示
                double lastW = JudgeSettings.Levels[JudgeSettings.Levels.Count - 1].Window;
                double early = now - lastW - 200, late = now + lastW + approach + 300;
                for (int i = 0; i < _notes.Count; i++)
                {
                    var n = _notes[i];
                    if (n == null || n.Judged && !n.Held)
                        continue;
                    if (n.Time < early || n.Time > late)
                        continue;
                    MaimaiButtonPos(n, L, out var bx, out var by);
                    float px = (float)bx, py = (float)by;
                    bool isTouch = n.Col >= 9;
                    bool isBreak = n.Type == "break";
                    bool isHold = IsHoldNote(n);
                    float nr = isTouch ? (float)(R * 0.09) : isBreak ? (float)(R * 0.14) : (float)(R * 0.11);
                    // slide：画起点→终点弧线 + 移动圆沿环弧角向插值（走最短弧，含 360° 回绕）
                    if (n.Type == "slide")
                    {
                        int k1 = ((n.Col % 8) + 8) % 8;
                        if (k1 == 0)
                            k1 = 8;
                        int k2 = ((Math.Max(1, n.EndCol) % 8) + 8) % 8;
                        if (k2 == 0)
                            k2 = 8;
                        double a1 = (k1 * 45 % 360) * Math.PI / 180.0;
                        double a2 = (k2 * 45 % 360) * Math.PI / 180.0;
                        double d = a2 - a1;
                        while (d > Math.PI)
                            d -= Math.PI * 2;
                        while (d < -Math.PI)
                            d += Math.PI * 2;
                        double sp = Math.Max(0, Math.Min(1, (now - n.Time) / Math.Max(1, n.End - n.Time)));
                        double aq = a1 + d * sp;
                        px = (float)(cx + Math.Sin(aq) * R);
                        py = (float)(cy - Math.Cos(aq) * R);
                        MaimaiButtonPos(new Note { Col = k2 }, L, out var ex, out var ey);
                        _d2d.DrawLine((float)bx, (float)by, (float)ex, (float)ey, Color.FromArgb(150, 90, 200, 255), 3f);
                    }

                    // hold 到达按钮后驻留（t61：位置=按钮方位，天然驻留）
                    var col2 = NoteColCurrent(); // t25：主色读 NoteStyleBook
                    if (isBreak)
                        col2 = GlowColCurrent(); // t25：break 发光色
                    else if (isTouch)
                        col2 = NoteColCurrent(); // t25：touch 主色
                    else if (n.Type == "slide")
                        col2 = AccentColCurrent(); // t25：slide 强调色
                    else if (n.Type == "hold")
                        col2 = AccentColCurrent(); // t25：hold 强调色
                    // t61：接近进度只做缩放+渐显（方位恒定，验收判据=音符在环上）
                    double ak = MaimaiApproachK(n, L, now);
                    float ksc = (float)(0.35 + 0.65 * ak);
                    int kalpha = (int)(90 + 165 * Math.Max(0.5, ak));
                    col2 = Color.FromArgb(Math.Min(255, kalpha), col2.R, col2.G, col2.B);
                    _d2d.FillEllipse(px, py, nr * ksc, nr * ksc, col2);
                    _d2d.DrawEllipse(px, py, nr * ksc, nr * ksc, Color.FromArgb(220, 255, 255, 255), 1.6f);
                    // hold 按住进度条
                    if (isHold && n.Held)
                    {
                        double hk = Math.Max(0, Math.Min(1, (now - n.Time) / Math.Max(1, n.End - n.Time)));
                        _d2d.DrawLine((float)(bx - nr), (float)(by + nr + 6), (float)(bx + nr), (float)(by + nr + 6), Color.FromArgb(160, 255, 255, 255), 2f);
                        _d2d.DrawLine((float)(bx - nr), (float)(by + nr + 6), (float)(bx - nr + 2 * nr * hk), (float)(by + nr + 6), Color.FromArgb(255, 120, 230, 255), 3f);
                    }

                    // 命中爆发
                    int bi = Math.Max(0, Math.Min(15, n.Col));
                    if (_burst[bi] > 0.05f)
                        _d2d.DrawEllipse(px, py, nr + _burst[bi] * 30, nr + _burst[bi] * 30, Color.FromArgb((int)(200 * _burst[bi]), 255, 255, 255), 2.5f);
                }
            }
            catch
            {
            }
        }


        /// <summary>maimai 按钮底色：外圈蓝系、中央橙系，EX 更亮。</summary>
        Color MaimaiButtonColor(int col, bool touch)
        {
            if (touch)
                return Color.FromArgb(150, 200, 110, 60);
            if (col % 2 == 1)
                return Color.FromArgb(160, 40, 90, 190);
            return Color.FromArgb(160, 30, 70, 160);
        }


        /* ================= 回环作曲 LoopComposer（编玩一体化） ================= */
        /// <summary>Vec2（引擎库）→ PointF（宿主 GDI 坐标）：引擎零 UI 依赖，宿主负责换算。</summary>
        static PointF P(Vec2 v) => new PointF((float)v.X, (float)v.Y);

        /// <summary>奏判定钩子：爆字+闪光放环顶判定点（GamePanel 既有判定视觉路径）。</summary>
        void LoopComposerJudgeFx(string grade, double now)
        {
            _lastJudge = grade;
            _lastDev = "";
            try
            {
                var L = ComputeLayout();
                double R = Math.Max(120, Math.Min(L.AreaW, L.AreaH) * 0.36);
                _lastJudgePos = new PointF((float)L.CenterX, (float)(L.CenterY - R * 0.87));
            }
            catch
            {
                _lastJudgePos = new PointF(-999, -999);
            }

            _lastJudgeMono = (long)MonoMs();
            if (_judgePop < 0.1f)
                _judgePop = 0.5f;
        }


        /// <summary>重开/重载：清空 LoopComposer 录音状态（下次 Tick 重新开始录）。</summary>
        void ResetLoopComposer()
        {
            if (_loop != null)
                _loop.PhaseStart = -1;
        }


        /// <summary>回环作曲渲染：环=时间轮（16 分格线 + 播放头 + 轨色块 + 环顶判定点）。</summary>
        void DrawLoopComposer(int W, int H, double now, PlayLayout L)
        {
            if (_loop == null || !(_d2d is D2DRenderer d2))
                return;
            try
            {
                double cx = L.CenterX, cy = L.CenterY;
                double R = Math.Max(120, Math.Min(L.AreaW, L.AreaH) * 0.36);
                _loop.Draw(d2, W, H, now, cx, cy, R);
            }
            catch
            {
            }
        }

    }
}
