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

        /// <summary>是否为按住类音符（hold / arc / slide，统一走 hold 判定与渲染）。</summary>
        static bool IsHoldNote(Note n) => n != null && (n.Type == "hold" || n.Type == "arc" || n.Type == "slide");

        /// <summary>播放区域几何布局（各模式共用的尺寸常量）。</summary>
        struct PlayLayout
        {
            public int H;
            public double TopY, HitY, RpX, RpW, PlayW, PlayX, LaneW;
            public double CenterX, CenterY; // 环模式圆心（右侧面板存在时按 rpX 中心）
            public double Ppm; // 像素/毫秒（判定线处流速）
            public double ScrollDir; // 下落方向：+1 从上往下（默认）、-1 从下往上（scroll 事件）
            public double AreaX, AreaY, AreaW, AreaH; // 框定游玩区域（游戏内容渲染范围，尽可能大）
        }


        /// <summary>游玩区域：游戏内容渲染的框定矩形。开启 osu!std 框定时按 osu!standard 的
        /// 4:3（512×384）等比缩放居中，剩余空间留边；关闭时全窗口扣除右侧面板（Mania/IIDX）与
        /// 最小边距，尽可能大。</summary>
        RectangleF PlayAreaRect()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            double rpW = EditLayoutMode ? EditPanelW : (ShowRightPanel ? 300 : 0);
            double rpX = W - rpW;
            const double m = 6; // 最小边距（框线可见、内容不贴边）
            double availW = Math.Max(120, rpX - m * 2);
            double availH = Math.Max(120, H - m * 2);
            if (GameSettings.OsuStdPlayfield)
            {
                // osu!standard 框定范围：512×384（4:3）等比缩放居中，超出部分留边
                double w = availW, h = w * 384.0 / 512.0;
                if (h > availH)
                {
                    h = availH;
                    w = h * 512.0 / 384.0;
                }

                return new RectangleF((float)(m + (availW - w) / 2), (float)(m + (availH - h) / 2), (float)w, (float)h);
            }

            return new RectangleF((float)m, (float)m, (float)availW, (float)availH);
        }


        /// <summary>谱面事件求值：在 [Time, End] 内按缓动曲线插值 Value→EndValue；事件结束后保持 EndValue；
        /// 无匹配事件返回 fallback。全局事件（Line&lt;0）作用于所有判定线；同一时刻专属线事件优先。
        /// 索引化：ResetState 时按 (Type, Line) 分组（组内保序=按时间），组内二分 O(logN)，避免每帧线性扫描全部事件。</summary>
        readonly Dictionary<(string, int), List<(ChartEvent ev, int gidx)>> _evIndex = new Dictionary<(string, int), List<(ChartEvent ev, int gidx)>>();

        void RebuildEventIndex()
        {
            _evIndex.Clear();
            if (_chart == null || _chart.Events == null)
                return;
            var evs = _chart.Events;
            for (int i = 0; i < evs.Count; i++)
            {
                var ev = evs[i];
                int k = ev.Line >= 0 ? ev.Line : -1; // -1 = 全局组
                if (!_evIndex.TryGetValue((ev.Type, k), out var lst))
                {
                    lst = new List<(ChartEvent, int)>();
                    _evIndex[(ev.Type, k)] = lst;
                }

                lst.Add((ev, i));
            }
        }


        /// <summary>组内最后一个 Time≤t 的事件（同 Time 组取最早=索引最小）。</summary>
        static (ChartEvent ev, int gidx) LastBeforeGroup(List<(ChartEvent ev, int gidx)> lst, double t)
        {
            int lo = 0, hi = lst.Count - 1, idx = -1;
            while (lo <= hi)
            {
                int m = (lo + hi) >> 1;
                if (lst[m].ev.Time <= t)
                {
                    idx = m;
                    lo = m + 1;
                }
                else
                    hi = m - 1;
            }

            if (idx < 0)
                return (null, -1);
            var best = lst[idx];
            for (int i = idx - 1; i >= 0 && lst[i].ev.Time == best.ev.Time; i--)
                best = lst[i];
            return best;
        }


        /// <summary>组内第一个 Time&gt;t 的事件（Next 查询用）。</summary>
        static (ChartEvent ev, int gidx) FirstAfterGroup(List<(ChartEvent ev, int gidx)> lst, double t)
        {
            int lo = 0, hi = lst.Count - 1, idx = lst.Count;
            while (lo <= hi)
            {
                int m = (lo + hi) >> 1;
                if (lst[m].ev.Time > t)
                {
                    idx = m;
                    hi = m - 1;
                }
                else
                    lo = m + 1;
            }

            if (idx >= lst.Count)
                return (null, -1);
            return lst[idx];
        }


        double EvalEvents(string type, double now, double fallback, int line = -1)
        {
            if (_chart == null || _chart.Events == null || _chart.Events.Count == 0)
                return fallback;
            // 候选组：全局组(-1) + 目标组（line≥0 用该线；line<0 用 0 线，与线性版语义一致）
            _evIndex.TryGetValue((type, -1), out var gList);
            _evIndex.TryGetValue((type, line >= 0 ? line : 0), out var lList);
            var ca = gList != null ? LastBeforeGroup(gList, now) : (null, -1);
            var cb = lList != null ? LastBeforeGroup(lList, now) : (null, -1);
            ChartEvent active;
            bool activeHeld;
            if (ca.ev == null && cb.ev == null)
                return fallback;
            if (ca.ev == null)
            {
                active = cb.ev;
                activeHeld = false;
            }
            else if (cb.ev == null)
            {
                active = ca.ev;
                activeHeld = false;
            }
            else if (ca.ev.Time > cb.ev.Time)
            {
                active = ca.ev;
                activeHeld = false;
            }
            else if (cb.ev.Time > ca.ev.Time)
            {
                active = cb.ev;
                activeHeld = false;
            }
            else
            {
                // 同 Time：专属线优先；同级别取全局索引更早（与线性遍历一致）
                bool specA = line >= 0 && ca.ev.Line == line;
                bool specB = line >= 0 && cb.ev.Line == line;
                if (specA && !specB)
                    active = ca.ev;
                else if (specB && !specA)
                    active = cb.ev;
                else
                    active = ca.gidx <= cb.gidx ? ca.ev : cb.ev;
                activeHeld = false;
            }

            activeHeld = !double.IsNaN(active.End) && active.End > active.Time && now > active.End;
            if (activeHeld)
            {
                // RPE next 语义：true=结束后衔接下一事件起点（曲线连续）；false=保持自身结束值
                if (!active.Next)
                    return active.EndValue;
                ChartEvent nxt = null;
                if (gList != null)
                {
                    var n = FirstAfterGroup(gList, active.Time);
                    if (n.ev != null)
                        nxt = n.ev;
                }

                if (lList != null)
                {
                    var n = FirstAfterGroup(lList, active.Time);
                    if (n.ev != null && (nxt == null || n.ev.Time < nxt.Time))
                        nxt = n.ev;
                }

                return nxt != null ? nxt.Value : active.EndValue;
            }

            if (double.IsNaN(active.End) || active.End <= active.Time)
                return active.Value;
            double t = Math.Max(0, Math.Min(1, (now - active.Time) / (active.End - active.Time)));
            t = active.Bezier != null ? BezierEval(active.Bezier, t) : ApplyEase(active.Ease, t);
            return active.Value + (active.EndValue - active.Value) * t;
        }


        /// <summary>RPE beziers 缓动：求 u 使 Bx(u)=t（二分，精度 1e-6，上限 64 次；x 非单调 → 回退线性），返回 By(u)。
        /// pts = [x0,y0,x1,y1,x2,y2,x3,y3] 三次贝塞尔 4 控制点。</summary>
        static double BezierEval(double[] pts, double t)
        {
            double x0 = pts[0], y0 = pts[1], x1 = pts[2], y1 = pts[3], x2 = pts[4], y2 = pts[5], x3 = pts[6], y3 = pts[7];
            if (!(x0 <= x1 + 1e-9 && x1 <= x2 + 1e-9 && x2 <= x3 + 1e-9))
                return t; // x 非单调 → 回退线性
            if (t <= x0)
                return y0;
            if (t >= x3)
                return y3;
            double lo = 0, hi = 1;
            for (int i = 0; i < 64; i++)
            {
                double u = (lo + hi) / 2;
                double bu = CubicB(x0, x1, x2, x3, u);
                if (bu < t)
                    lo = u;
                else
                    hi = u;
                if (hi - lo < 1e-9)
                    break;
            }

            double uu = (lo + hi) / 2;
            return CubicB(y0, y1, y2, y3, uu);
        }


        /// <summary>三次贝塞尔点：B(u)=(1-u)³a + 3(1-u)²u·b + 3(1-u)u²·c + u³·d。</summary>
        static double CubicB(double a, double b, double c, double d, double u)
        {
            double v = 1 - u;
            return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d;
        }


        /// <summary>构建指定判定线的 speed 场关键帧（t,v），覆盖 [t0,t1]：事件内按缓动采样、事件间线性衔接。
        /// 空表 = 该线无 speed 事件（位移恒等于时间差）。谱面加载时构建一次并缓存（事件只读，帧间不变）。</summary>
        List<(double t, double v)> BuildSpeedKeyframes(int line, double t0, double t1)
        {
            var ks = new List<(double t, double v)>();
            if (_chart == null || _chart.Events == null || _chart.Events.Count == 0)
                return ks;
            var evs = new List<ChartEvent>();
            foreach (var ev in _chart.Events)
            {
                if (ev.Type != "speed")
                    continue;
                if (line >= 0 && ev.Line >= 0 && ev.Line != line)
                    continue;
                if (line < 0 && ev.Line > 0)
                    continue;
                evs.Add(ev);
            }

            if (evs.Count == 0)
                return ks;
            evs.Sort((a, b) => a.Time.CompareTo(b.Time));
            // 起始基线值：t0 前最后一个事件在 t0 处的求值（无事件=1.0）
            double curV = 1.0;
            ChartEvent last = null;
            foreach (var ev in evs)
            {
                if (ev.Time > t0)
                    break;
                last = ev;
            }

            if (last != null)
                curV = EventValueAt(last, t0);
            ks.Add((t0, curV));
            // 各事件区间采样（事件内按缓动曲线 SEG 段）
            const int SEG = 4;
            foreach (var ev in evs)
            {
                if (double.IsNaN(ev.End) || ev.End <= ev.Time)
                    continue;
                if (ev.End <= t0 || ev.Time >= t1)
                    continue;
                double s = Math.Max(ev.Time, t0), e = Math.Min(ev.End, t1);
                if (e <= s)
                    continue;
                for (int i = 0; i <= SEG; i++)
                {
                    double u = (double)i / SEG;
                    double tt = s + (e - s) * u;
                    ks.Add((tt, EventValueAt(ev, tt)));
                }
            }

            // 尾部：最后事件结束后保持 EndValue 至 t1
            var le = evs[evs.Count - 1];
            if (!double.IsNaN(le.End) && le.End > t0 && le.End < t1)
                ks.Add((le.End, le.EndValue));
            ks.Add((t1, le.EndValue));
            ks.Sort((a, b) => a.t.CompareTo(b.t));
            // 同 t 去重（保留后值）；排序后间隙天然线性衔接
            var uniq = new List<(double t, double v)>();
            foreach (var k in ks)
                if (uniq.Count == 0 || k.t > uniq[uniq.Count - 1].t + 1e-6)
                    uniq.Add(k);
            return uniq;
        }


        /// <summary>事件在时刻 t 的求值（t 在事件区间内按缓动插值；t≥End 返回 EndValue；瞬间事件返回 Value）。</summary>
        double EventValueAt(ChartEvent ev, double t)
        {
            if (double.IsNaN(ev.End) || ev.End <= ev.Time)
                return ev.Value;
            if (t >= ev.End)
                return ev.EndValue;
            if (t <= ev.Time)
                return ev.Value;
            double p = Math.Max(0, Math.Min(1, (t - ev.Time) / (ev.End - ev.Time)));
            double c = ev.Bezier != null ? BezierEval(ev.Bezier, p) : ApplyEase(ev.Ease, p);
            return ev.Value + (ev.EndValue - ev.Value) * c;
        }


        /// <summary>缓动曲线（非线性动画，RPE/Phira 命名）：把线性进度 t∈[0,1] 映射为曲线进度。
        /// 覆盖 RPE_TWEEN_MAP 全部 30 项（Sine/Quad/Cubic/Quart/Quint/Expo/Circ/Back/Elastic/Bounce × In/Out/InOut）。</summary>
        static double ApplyEase(string ease, double t)
        {
            if (string.IsNullOrEmpty(ease) || ease == "Linear")
                return t;
            switch (ease)
            {
                // ---- RPE 命名（首字母大写 In/Out/InOut + 类型）----
                case "EaseInSine":
                    return 1 - Math.Cos(t * Math.PI / 2);
                case "EaseOutSine":
                    return Math.Sin(t * Math.PI / 2);
                case "EaseInOutSine":
                    return -(Math.Cos(Math.PI * t) - 1) / 2;
                case "EaseInQuad":
                    return t * t;
                case "EaseOutQuad":
                {
                    double u = 1 - t;
                    return 1 - u * u;
                }

                case "EaseInOutQuad":
                    return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                case "EaseInCubic":
                    return t * t * t;
                case "EaseOutCubic":
                {
                    double u = 1 - t;
                    return 1 - u * u * u;
                }

                case "EaseInOutCubic":
                    return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                case "EaseInQuart":
                    return t * t * t * t;
                case "EaseOutQuart":
                {
                    double u = 1 - t;
                    return 1 - u * u * u * u;
                }

                case "EaseInOutQuart":
                    return t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;
                case "EaseInQuint":
                    return t * t * t * t * t;
                case "EaseOutQuint":
                {
                    double u = 1 - t;
                    return 1 - u * u * u * u * u;
                }

                case "EaseInOutQuint":
                    return t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;
                case "EaseInExpo":
                    return t == 0 ? 0 : Math.Pow(2, 10 * t - 10);
                case "EaseOutExpo":
                    return t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
                case "EaseInOutExpo":
                    return t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;
                case "EaseInCirc":
                    return 1 - Math.Sqrt(1 - t * t);
                case "EaseOutCirc":
                {
                    double u = 1 - t;
                    return Math.Sqrt(1 - u * u);
                }

                case "EaseInOutCirc":
                    return t < 0.5 ? (1 - Math.Sqrt(1 - (2 * t) * (2 * t))) / 2 : (Math.Sqrt(1 - (-2 * t + 2) * (-2 * t + 2)) + 1) / 2;
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

                case "EaseInOutBack":
                {
                    const double c1 = 1.70158, c2 = c1 * 1.525;
                    double u;
                    if (t < 0.5)
                    {
                        u = 2 * t;
                        return (u * u * ((c2 + 1) * u - c2)) / 2;
                    }

                    u = 2 * t - 2;
                    return (u * u * ((c2 + 1) * u + c2) + 2) / 2;
                }

                case "EaseInElastic":
                    return t == 0 ? 0 : t == 1 ? 1 : -(Math.Pow(2, 10 * t - 10)) * Math.Sin((t * 10 - 10.75) * 2 * Math.PI / 3);
                case "EaseOutElastic":
                    return t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * 2 * Math.PI / 3) + 1;
                case "EaseInOutElastic":
                    return t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? -(Math.Pow(2, 20 * t - 10)) * Math.Sin((20 * t - 11.125) * 2 * Math.PI / 4.5) / 2 : (Math.Pow(2,
                        -20 * t + 10)) * Math.Sin((20 * t - 11.125) * 2 * Math.PI / 4.5) / 2 + 1;
                case "EaseInBounce":
                    return 1 - ApplyEase("EaseOutBounce", 1 - t);
                case "EaseOutBounce":
                case "bounce":
                {
                    const double n1 = 7.5625, d1 = 2.75;
                    if (t < 1 / d1)
                        return n1 * t * t;
                    if (t < 2 / d1)
                    {
                        t -= 1.5 / d1;
                        return n1 * t * t + 0.75;
                    }

                    if (t < 2.5 / d1)
                    {
                        t -= 2.25 / d1;
                        return n1 * t * t + 0.9375;
                    }

                    t -= 2.625 / d1;
                    return n1 * t * t + 0.984375;
                }

                case "EaseInOutBounce":
                    return t < 0.5 ? (1 - ApplyEase("EaseOutBounce", 1 - 2 * t)) / 2 : (1 + ApplyEase("EaseOutBounce", 2 * t - 1)) / 2;
                // ---- 兼容旧名（三次/二次）----
                case "EaseIn":
                case "EaseInCubicLegacy":
                    return t * t * t;
                case "EaseOut":
                case "EaseOutCubicLegacy":
                {
                    double u = 1 - t;
                    return 1 - u * u * u;
                }

                case "EaseInOut":
                case "EaseInOutCubicLegacy":
                    return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                case "EaseInOutBackLegacy":
                {
                    const double c1 = 1.70158, c2 = c1 * 1.525;
                    double u;
                    if (t < 0.5)
                    {
                        u = 2 * t;
                        return (u * u * ((c2 + 1) * u - c2)) / 2;
                    }

                    u = 2 * t - 2;
                    return (u * u * ((c2 + 1) * u + c2) + 2) / 2;
                }

                // 兼容旧值（二次）
                case "in":
                    return t * t;
                case "out":
                    return t * (2 - t);
                case "inout":
                    return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                case "back":
                {
                    double c = 1.70158, u = t - 1;
                    return u * u * ((c + 1) * u + c) + 1;
                }

                default:
                    return t;
            }
        }


        /* ================= 多场同屏（t27）================= */
        /// <summary>单场运行时（含其独立判定引擎 / 键位表 / 独立自动/漏判游标）。</summary>
        sealed class StageRT
        {
            public Stage Spec; // 舞台规格（null = 旧谱单场兼容）
            public GameMode StageMode; // 该场玩法
            public int Kc; // 该场键数
            public List<Note> Notes = new List<Note>(); // 该场音符
            public JudgementEngine Eng; // 该场独立判定引擎（stage0 = _eng）
            public Dictionary<Keys, int> KeyCol = new Dictionary<Keys, int>(); // 该场键位块
            public int AutoIdx; // 自动游玩游标
            public int SweepIdx; // 漏判扫除游标
            public List<Note> Holds = new List<Note>(); // 该场按下中的长条
        }

    }
}
