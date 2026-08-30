using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ChartPlayer
{
    /// <summary>
    /// ChartParser 的扩展部分：全音游模式解析（Arcaea/Cytus/Phigros/Milestone 等）、
    /// 段位检测、Milestone 原生 .mil 序列化。
    /// 与 ChartParser.cs 通过 partial class 合并，共用 Sanitize / I / D 等私有助手。
    /// </summary>
    public static partial class ChartParser
    {
        /* ================= 模式名 / 分发辅助 ================= */

        /// <summary>把 .mil 里的 mode 字符串映射为 GameMode（统一由引擎 ModeSystem 处理）。</summary>
        static GameMode ModeFromString(string s) => ModeSystem.FromId(s).Mode;

        static string ModeToString(GameMode m) => ModeSystem.ModeId(m);

        /// <summary>模式人类可读名（ModeName / QuickInfo 展示用）。</summary>
        static string ModeDisplayName(GameMode m)
            => m == GameMode.Mania ? "Milestone" : ModeSystem.DisplayName(m);

        /// <summary>给 Mania 谱面统一打 Mode 标记并做段位检测（osu! 其它模式已由专用解析器设置 Mode，跳过）。</summary>
        static Chart ManiaChart(Chart c, string path)
        {
            if (c.Mode == GameMode.Mania) DanDetect(c, path);
            return c;
        }

        /* ================= 目录扫描 / 多谱面解析（并行热点：EngineJobs 门，带取消） ================= */

        /// <summary>
        /// 并行解析单个谱面文件（失败返回 null；支持取消）。
        /// </summary>
        public static Chart ParseFileOrNull(string path, System.Threading.CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            try { return ParseFile(path); }
            catch { return null; }
        }

        /// <summary>
        /// 多谱面并行解析（EngineJobs.ParallelMap，带取消）：文件列表 → 逐文件独立解析。
        /// 输出与输入同序（失败位置为 null）；EngineJobs.Paused 时自动降级串行。
        /// </summary>
        public static Chart[] ParseFilesParallel(IReadOnlyList<string> files, int? degree = null,
            System.Threading.CancellationToken token = default)
        {
            if (files == null || files.Count == 0) return Array.Empty<Chart>();
            return EngineJobs.ParallelMap(files, f => ParseFileOrNull(f, token), degree, token);
        }

        /// <summary>
        /// 目录扫描 + 多谱面并行解析（EngineJobs.ParallelMap，带取消）：
        /// 递归扫描 folder 下全部受支持扩展名谱面文件 → 并行解析，
        /// 返回解析成功的谱面列表（解析失败的文件跳过并回调 onFailed；结果顺序 = 文件枚举顺序）。
        /// </summary>
        public static List<Chart> ParseFolderParallel(string folder,
            System.Threading.CancellationToken token = default, Action<string> onFailed = null)
        {
            var result = new List<Chart>();
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;
                var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(f => Array.IndexOf(ChartExts, Path.GetExtension(f).ToLowerInvariant()) >= 0)
                    .ToArray();
                if (files.Length == 0) return result;
                var charts = ParseFilesParallel(files, null, token);
                for (int i = 0; i < files.Length; i++)
                {
                    if (charts[i] != null) result.Add(charts[i]);
                    else onFailed?.Invoke(files[i]);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            return result;
        }

        /// <summary>.txt 内容嗅探：simai(maidata)→Maimai，VERSION 2→Cytus，都不含再试 Cytus。</summary>
        static Chart ParseTxtSniff(string text, string path)
        {
            var t = text.TrimStart();
            if (t.Contains("&inote_") && (t.Contains("&bpm") || t.Contains("&first"))) return ParseMaimai(text, path);
            if (t.Contains("VERSION 2")) return ParseCytus(text, path);
            try
            {
                var c = ParseCytus(text, path);
                if (c.Notes.Count > 0) return c;
            }
            catch { }
            throw new InvalidDataException("无法识别的 .txt 谱面格式（既不是 Cytus VERSION 2）");
        }

        /// <summary>
        /// .json 内容嗅探：ADOFAI 优先，再按 Phigros / Milestone /（Malody 兼容）顺序，
        /// 否则 Phigros 兜底。
        /// </summary>
        static Chart ParseJsonSniff(string text, string path)
        {
            var t = text.TrimStart();
            if (t.Contains("\"angleData\"")) return ParseAdofai(text, path);
            if (t.Contains("\"judgeLineList\"") || t.Contains("\"judgeLineGroup\"")) return ParsePhigros(text, path);
            if (IsMilestone(text)) return ParseMil(text, path);
            // 兼容被改名为 .json 的 Malody 谱面（含 meta + note）
            if (t.Contains("\"meta\"") && t.Contains("\"note\"")) return ManiaChart(ParseMc(text, path), path);
            return ParsePhigros(text, path);
        }

        /// <summary>真实 ADOFAI 模式：同一 .adofai 格式，解析核心复用，Mode 标为 AdofaiReal（Routlock=Adofai 保留）。</summary>
        public static Chart ParseAdofaiReal(string text, string sourcePath)
        {
            var c = ParseAdofai(text, sourcePath);
            c.Mode = GameMode.AdofaiReal;
            c.ModeName = "ADOFAI";
            return c;
        }

        static bool IsMilestone(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                return root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("format", out var f)
                    && f.ValueKind == JsonValueKind.String
                    && f.GetString() == "milestone-1";
            }
            catch { return false; }
        }

        /* ================= JSON 取值助手 ================= */

        static JsonElement ObjAt(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

        static string StrAt(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static double? NumAt(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;

        /// <summary>数组元素数值：兼容数字与字符串两种序列化（.mil 曲线点历史为字符串）。</summary>
        static double NumAtEl(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return 0;
        }

        /// <summary>对象属性坐标（{x,y} 点对象，值兼容数字/字符串）。</summary>
        static double PtCoord(JsonElement pt, string name)
            => pt.ValueKind == JsonValueKind.Object && pt.TryGetProperty(name, out var v) ? NumAtEl(v) : 0;

        static string[] SplitWs(string s) => s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        /* ================= Arcaea (.aff 文本) ================= */

        /// <summary>拆 .aff 语句参数字段（逗号分隔 + 去空白/引号）。</summary>
        static string[] AffArgs(string args)
        {
            var list = new List<string>();
            foreach (var a in args.Split(','))
            {
                var t = a.Trim().Trim('"');
                if (t.Length > 0) list.Add(t);
            }
            return list.ToArray();
        }

        /// <summary>四舍五入（避免 .NET 银行家舍入）。</summary>
        static double AffRound(double v) => Math.Floor(v + 0.5);

        /// <summary>官方 Arc 坐标系 x∈[-0.5,1.5] → 归一化 X∈[0,1]（(x+0.5)/2；wiki 与整数轨道 (lane-0.5)/4 对齐）。</summary>
        static double AffArcX(double x) => Clamp01((x + 0.5) / 2.0);
        /// <summary>归一化 X∈[0,1] → 官方 Arc 坐标（导出反向：2X-0.5）。</summary>
        static double AffArcXInv(double nx) => nx * 2.0 - 0.5;
        /// <summary>官方轨道坐标（整数 1..4 地面 / 浮点=天空自由 x）→ 地面 Col 2..5（非整数时回退天空语义由调用方处理）。</summary>
        static int AffLane2Col(double lane)
        {
            if (Math.Abs(lane % 1.0) > 0.001) return 0;                     // 浮点轨道（自定义水平坐标）→ 天空
            return Math.Max(0, Math.Min(5, (int)AffRound(lane) + 1));       // lane 1..4 → Col 2..5；0/5 边界钳制
        }
        /// <summary>Arc 连续轨道坐标（0.5..4.5）→ 地面 Col 2..5（永不回退天空；弧端点跨满 4 轨）。</summary>
        static int AffArcLane2Col(double laneF) => Math.Max(2, Math.Min(5, (int)AffRound(laneF) + 1));
        /// <summary>字段文本是否表示浮点轨道（官方写整数轨道用 N0、自由坐标用小数——同 ArcCreate HasDecimal 判定）。</summary>
        static bool AffFloatLane(string s) => s.Trim().Contains('.');
        /// <summary>官方浮点轨道 x（天空自由坐标）→ 归一化 X：内部约定 X = (x-0.5)/4（wiki：整数轨道 (lane-0.5)/4）。</summary>
        static double AffSkyX(double x) => Clamp01((x - 0.5) / 4.0);
        /// <summary>归一化 X → 官方浮点轨道坐标（导出反向：X*4+0.5）。</summary>
        static double AffSkyXInv(double nx) => nx * 4.0 + 0.5;
        /// <summary>位置坐标 x（0..5 域，地面轨 1..4）→ 地面 Col 2..5。</summary>
        static int AffX2Col(double x) => Math.Max(2, Math.Min(5, (int)AffRound(x) + 1));
        /// <summary>位置坐标 x → 归一化 X（(x-0.5)/4，与天空自由坐标同一 4 轨宽度域）。</summary>
        static double AffXNorm(double x) => Clamp01((x - 0.5) / 4.0);
        /// <summary>.aff 布尔字段（false/true/designant/0/1）。</summary>
        static bool AffBool2(string s)
        {
            var t = s.Trim().ToLowerInvariant();
            return t == "true" || t == "1" || t == "designant";
        }
        /// <summary>毫秒取整（导出）。</summary>
        static string AffMs(double v) => ((long)AffRound(v)).ToString(CultureInfo.InvariantCulture);

        /// <summary>「弧上天空音符」：在弧的 k 时间比例处插值位置/高度 → 天空 tap（Col 0，X/Y 归一化）。</summary>
        static Note MakeArctap(double t, double t1, double t2, double x1, double y1, double x2, double y2)
        {
            double k = t2 > t1 ? Clamp01((t - t1) / (t2 - t1)) : 0;
            return new Note
            {
                Time = t, End = t, Col = 0,                      // 天空键（Col 0-1）
                X = Clamp01(x1 + (x2 - x1) * k),
                Y = Clamp01(y1 + (y2 - y1) * k),
                Type = "tap"
            };
        }

        /// <summary>老内部方言（arcaea_test.aff：(value,type,lane) 三元组 + hold(lane,endOrDuration) 两参）——保持原行为。</summary>
        static Chart ParseArcaeaOld(Chart chart, List<string> lines, double bpm, bool usesBeats)
        {
            var holds = new Dictionary<int, double>();                              // hold(lane, endOrDuration)
            var rawNotes = new List<(double value, int type, int lane)>();
            foreach (var s in lines)
            {
                // timing(组, bpm, 每小节拍数)
                var tm = Regex.Match(s, @"^timing\s*\(([^)]*)\)");
                if (tm.Success)
                {
                    var p = AffArgs(tm.Groups[1].Value);
                    if (p.Length >= 3 && (int)D(p[0], 0) == 0)
                    {
                        double cand = D(p[1], 0);
                        if (cand > 0) bpm = cand;
                    }
                    continue;
                }
                // hold(lane, endOrDuration)
                var hm = Regex.Match(s, @"^hold\s*\((\d+)\s*,\s*(-?\d+(?:\.\d+)?)\)");
                if (hm.Success) { holds[I(hm.Groups[1].Value, 0)] = D(hm.Groups[2].Value, 0); continue; }
                // 音符行 (value,type,lane)：type 0=tap 1=hold 2=arc
                var nm = Regex.Match(s, @"^\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
                if (nm.Success)
                    rawNotes.Add((D(nm.Groups[1].Value, 0), I(nm.Groups[2].Value, 0), I(nm.Groups[3].Value, 0)));
            }
            double BeatToTime(double beat) => beat * 60000.0 / bpm;

            for (int i = 0; i < rawNotes.Count; i++)
            {
                var (value, type, lane) = rawNotes[i];
                double time = usesBeats ? BeatToTime(value) : value;
                int col = Math.Max(0, Math.Min(5, lane - 1));      // lane 1..6 → Col 0..5，1-2 天空轨 3-6 地面轨
                switch (type)
                {
                    case 1: // hold：hold(lane,x) 或下一条同 lane 音符；否则保守 800ms
                        double end = time + 800;
                        if (holds.TryGetValue(lane, out var hv)) end = hv > time ? hv : time + hv;
                        else
                        {
                            for (int j = i + 1; j < rawNotes.Count; j++)
                                if (rawNotes[j].lane == lane)
                                {
                                    end = usesBeats ? BeatToTime(rawNotes[j].value) : rawNotes[j].value;
                                    break;
                                }
                            if (end <= time) end = time + 800;
                        }
                        chart.Notes.Add(new Note { Time = time, End = end, Col = col, Type = "hold" });
                        break;
                    case 2: // arc：老方言无弧坐标 → 保守终点=起点、时长 800ms
                        chart.Notes.Add(new Note { Time = time, End = time + 800, Col = col, EndCol = col, Type = "arc" });
                        break;
                    default: // 0 tap 及其它
                        chart.Notes.Add(new Note { Time = time, End = time, Col = col, Type = "tap" });
                        break;
                }
            }
            chart.Bpm = bpm;
            return chart;
        }

        /// <summary>
        /// 官方 ArcCreate AffChartReader.Line 语义（T60/D2k 重建）：tap=(time,lane)；hold=(time,endTime,lane)；
        /// arc=arc([start],[end],[x1],[x2],[easing(s/b/si/so…)],[y1],[y2],[color],[hitsound],[arctype],[smoothness])
        /// + [arctap([timing]),...]；arctype=false 实体 / true·designant 黑线音轨（内部 Decor）。
        /// 兼容 reviewer「realspec」方言：元组 (x,time,type)（1=地面tap 2=地面hold 3=天空tap 4=天空hold）、
        /// hold(x,time,end)、arc(x1,x2,time,timeEnd,color[,radius][,fx][,arctype]...)。
        /// </summary>
        public static Chart ParseArcaea(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, Mode = GameMode.Arcaea, ModeName = "Arcaea", KeyCount = 6 };
            double bpm = 120;

            // ---- 预扫描：`-` 分隔符、注释剔除；方言判定 ----
            var lines = new List<string>();
            foreach (var rawLine in text.Split('\n'))
            {
                var s = rawLine.Trim().TrimEnd(';').Trim();
                if (s.Length == 0 || s == "-" || s.StartsWith("//")) continue;
                lines.Add(s);
            }
            bool usesBeats = text.Contains("TimingPointDensityFactor");

            // 方言 A（老内部）：hold(lane,endOrDuration) 两参 —— 保持 arcaea_test.aff 既有行为
            bool oldDialect = lines.Any(l => Regex.IsMatch(l, @"^hold\s*\(\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?\s*\)"));

            // 方言 B（realspec）：元组 (x,time,type) —— 全部三元组第二字段为时间（≥50ms）
            var tupleLines = lines.Where(l => Regex.IsMatch(l, @"^\(\s*[^,]+,[^,]+,[^,]+\)")).ToList();
            bool realSpec = !oldDialect && tupleLines.Count > 0 &&
                tupleLines.All(l => AffArgs(l.Substring(1, l.Length - 2)).Length >= 3 && D(AffArgs(l.Substring(1, l.Length - 2))[1], 0) >= 50);

            if (oldDialect)
                return Sanitize(ParseArcaeaOld(chart, lines, bpm, usesBeats));

            foreach (var s in lines)
            {
                // 头部键值（AudioOffset / AudioFilename / TimingPointDensityFactor 等）
                var kv = Regex.Match(s, @"^([A-Za-z0-9_]+)\s*:\s*(.*)$");
                if (kv.Success)
                {
                    var key = kv.Groups[1].Value;
                    var val = kv.Groups[2].Value.Trim();
                    if (key.Equals("AudioOffset", StringComparison.OrdinalIgnoreCase)) chart.Offset = D(val, 0);
                    else if (key.Equals("AudioFilename", StringComparison.OrdinalIgnoreCase)) chart.AudioFile = val;
                    continue;
                }

                // timing(组, bpm, 每小节拍数)
                var tm = Regex.Match(s, @"^timing\s*\(([^)]*)\)");
                if (tm.Success)
                {
                    var p = AffArgs(tm.Groups[1].Value);
                    if (p.Length >= 2)
                    {
                        double grp = p.Length >= 3 ? D(p[0], 0) : 0;
                        double cand = p.Length >= 3
                            ? D(p[1], 0)
                            : (D(p[1], 0) > 10 ? D(p[1], 0) : D(p[0], 0));
                        if ((int)grp == 0 && cand > 0) bpm = cand;
                    }
                    continue;
                }

                // arc(…)[arctap(t),…]：官方 10 字段（第 5 字段=线型非数字或字段数 ≥10）/ realspec 8 字段自动分派
                var am = Regex.Match(s, @"^arc\s*\(([^)]*)\)\s*(?:\[([^\]]*)\])?");
                if (am.Success)
                {
                    var f = AffArgs(am.Groups[1].Value);
                    string arctaps = am.Groups[2].Value ?? "";
                    bool official = f.Length >= 10 ||
                                    (f.Length >= 5 && !double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out _));

                    Note arcNote;
                    if (official)
                    {
                        // arc(t1,t2,x1,x2,线型(s/b/si/so),y1,y2,color,hitsound,arctype[,smoothness])
                        double t1 = D(f[0], 0), t2 = D(f[1], 0);
                        double nx1 = AffArcX(D(f[2], 0)), nx2 = AffArcX(D(f[3], 0));
                        double y1 = Clamp01(D(f[5], 0)), y2 = Clamp01(D(f[6], 0));
                        int col1 = AffArcLane2Col(nx1 * 4.0 + 0.5);
                        int col2 = AffArcLane2Col(nx2 * 4.0 + 0.5);
                        bool trace = f.Length >= 10 && AffBool2(f[9]);
                        arcNote = new Note
                        {
                            Time = t1, End = t2,
                            Col = col1, EndCol = col2 == col1 ? -1 : col2,
                            X = nx1, EndX = nx2, Y = y1, EndY = y2,
                            Kind = f.Length >= 8 ? I(f[7], 0) : 0,
                            SliderType = f.Length >= 5 && f[4].Length > 0 ? f[4][0] : 's',
                            Decor = trace,
                            Type = "arc"
                        };
                        foreach (Match at in Regex.Matches(arctaps, @"(?:arctap|at)\s*\(\s*(-?\d+(?:\.\d+)?)(?:\s*,\s*[^)]*)?\)"))
                        {
                            double tt = D(at.Groups[1].Value, t1);
                            if (tt < t1 || tt > t2) tt = Math.Max(t1, Math.Min(t2, tt));
                            chart.Notes.Add(MakeArctap(tt, t1, t2, nx1, y1, nx2, y2));
                        }
                    }
                    else
                    {
                        // realspec：arc(x1,x2,time,timeEnd,color[,radius][,fx][,arctype]...)
                        double x1 = D(f[0], 0), x2 = D(f[1], 0);
                        double t1 = D(f[2], 0), t2 = D(f[3], 0);
                        int c1 = AffX2Col(x1), c2 = AffX2Col(x2);
                        bool arctype = f.Length >= 8 && AffBool2(f[7]);
                        arcNote = new Note
                        {
                            Time = t1, End = t2,
                            Col = c1, EndCol = c2 == c1 ? -1 : c2,
                            X = AffXNorm(x1), EndX = AffXNorm(x2), Y = 0, EndY = 0,
                            Kind = f.Length >= 5 ? I(f[4], 0) : 0,
                            Width = f.Length >= 6 ? (D(f[5], 0) <= 0 ? 1.0 : D(f[5], 0)) : 1.0,   // radius（0=默认 1.0）
                            Side = f.Length >= 7 ? I(f[6], 0) : 0,                                // fx（0/1）
                            SliderType = 's',
                            Decor = arctype,
                            Type = "arc"
                        };
                        foreach (Match at in Regex.Matches(arctaps, @"(?:arctap|at)\s*\(\s*(-?\d+(?:\.\d+)?)(?:\s*,\s*[^)]*)?\)"))
                        {
                            double tt = D(at.Groups[1].Value, t1);
                            if (tt < t1 || tt > t2) tt = Math.Max(t1, Math.Min(t2, tt));
                            chart.Notes.Add(MakeArctap(tt, t1, t2, arcNote.X, 0, arcNote.EndX, 0));
                        }
                    }
                    chart.Notes.Add(arcNote);
                    continue;
                }

                // hold(…)
                var hm = Regex.Match(s, @"^hold\s*\(([^)]*)\)");
                if (hm.Success)
                {
                    var f = AffArgs(hm.Groups[1].Value);
                    if (f.Length >= 3)
                    {
                        if (realSpec)
                        {
                            // hold(x, time, end)：地面长条显式终点
                            double x = D(f[0], 1), t = D(f[1], 0), end = D(f[2], t);
                            chart.Notes.Add(new Note { Time = t, End = Math.Max(t, end), Col = AffX2Col(x), Type = "hold" });
                        }
                        else
                        {
                            // hold(time, endTime, lane)
                            double t1 = D(f[0], 0), t2 = D(f[1], t1), lane = D(f[2], 1);
                            if (AffFloatLane(f[2]))
                                chart.Notes.Add(new Note { Time = t1, End = t2, Col = 0, X = AffSkyX(lane), Y = 1.0, Type = "hold" });   // 天空 hold：Y 缺省=天空线（自由高度由 .mil 保留）
                            else
                                chart.Notes.Add(new Note { Time = t1, End = t2, Col = AffLane2Col(lane), Type = "hold" });
                        }
                    }
                    // 两参 hold 已由老方言分支处理；此处跳过
                    continue;
                }

                // 音符元组 (…)
                var nm = Regex.Match(s, @"^\(\s*([^)]*)\)");
                if (nm.Success)
                {
                    var f = AffArgs(nm.Groups[1].Value);
                    if (f.Length == 2)
                    {
                        // 官方 tap：(time, lane)；lane 浮点=天空自由坐标（按文本含小数点判定，同 ArcCreate HasDecimal）
                        double t = D(f[0], 0), lane = D(f[1], 1);
                        if (AffFloatLane(f[1]))
                            chart.Notes.Add(new Note { Time = t, End = t, Col = 0, X = AffSkyX(lane), Y = 1.0, Type = "tap" });   // 天空 tap：Y 缺省=天空线
                        else
                            chart.Notes.Add(new Note { Time = t, End = t, Col = AffLane2Col(lane), Type = "tap" });
                    }
                    else if (f.Length >= 3)
                    {
                        if (realSpec)
                        {
                            // (x, time, type)：1=地面tap 2=地面hold 3=天空tap 4=天空hold
                            double x = D(f[0], 1), t = D(f[1], 0);
                            int type = I(f[2], 1);
                            switch (type)
                            {
                                case 2: chart.Notes.Add(new Note { Time = t, End = t + 800, Col = AffX2Col(x), Type = "hold" }); break;
                                case 3: chart.Notes.Add(new Note { Time = t, End = t, Col = 0, X = AffXNorm(x), Y = 1.0, Type = "tap" }); break;   // 天空 tap：Y 缺省=天空线
                                case 4: chart.Notes.Add(new Note { Time = t, End = t + 800, Col = 0, X = AffXNorm(x), Y = 1.0, Type = "hold" }); break;
                                default: chart.Notes.Add(new Note { Time = t, End = t, Col = AffX2Col(x), Type = "tap" }); break;
                            }
                        }
                        else
                        {
                            // 官方三参元组 (time, lane, type)：0/1=地面tap 2=地面hold 3/4=天空tap 5=天空hold
                            double t = D(f[0], 0), lane = D(f[1], 1);
                            int type = I(f[2], 1);
                            switch (type)
                            {
                                case 2: chart.Notes.Add(new Note { Time = t, End = t + 800, Col = AffLane2Col(lane), Type = "hold" }); break;
                                case 3:
                                case 4: chart.Notes.Add(new Note { Time = t, End = t, Col = 0, X = AffSkyX(lane), Y = 1.0, Type = "tap" }); break;   // 天空 tap：Y 缺省=天空线
                                case 5: chart.Notes.Add(new Note { Time = t, End = t + 800, Col = 0, X = AffSkyX(lane), Y = 1.0, Type = "hold" }); break;
                                default: chart.Notes.Add(new Note { Time = t, End = t, Col = AffLane2Col(lane), Type = "tap" }); break;
                            }
                        }
                    }
                    continue;
                }
                // 其余（camera/scenecontrol/timinggroup/include 等）忽略
            }

            chart.Bpm = bpm;
            return Sanitize(chart);
        }

        /// <summary>
        /// SerializeArcaea：官方 ArcCreate 兼容 .aff 导出（AudioOffset/timing/tap/hold/arc/arctap）。
        /// 地面 Col 2..5 ↔ 官方轨道 1..4；天空音符 X∈[0,1] ↔ 官方浮点轨道 X*4+0.5；
        /// 弧 X∈[0,1] ↔ 官方弧坐标 2X-0.5；弧线型 SliderType('s'/'b')、颜色 Kind、黑线 Decor。
        /// </summary>
        public static string SerializeArcaea(Chart c)
        {
            var sb = new StringBuilder();
            sb.Append("AudioOffset:").Append(c.Offset.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("-\n");
            double bpm = c.Bpm > 0 ? c.Bpm : 120;
            sb.Append("timing(0,").Append(bpm.ToString("0.00", CultureInfo.InvariantCulture)).Append(",4.00);\n");
            foreach (var n in c.Notes)
            {
                if (n == null) continue;
                if (n.Type == "arc")
                {
                    double x1 = AffArcXInv(Clamp01(n.X)), x2 = AffArcXInv(Clamp01(n.EndX));
                    char lt = n.SliderType == 'b' ? 'b' : 's';
                    sb.Append("arc(").Append(AffMs(n.Time)).Append(',')
                      .Append(AffMs(n.End)).Append(',')
                      .Append(x1.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(x2.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(lt).Append(',')
                      .Append(Clamp01(n.Y).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(Clamp01(n.EndY).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(n.Kind).Append(",none,").Append(n.Decor ? "true" : "false").Append(");\n");
                    continue;
                }
                if (n.Col < 2)   // 天空音符 → 官方浮点轨道（X*4+0.5）
                {
                    double lane = AffSkyXInv(Clamp01(n.X));
                    if (n.Type == "hold")
                        sb.Append("hold(").Append(AffMs(n.Time)).Append(',').Append(AffMs(n.End)).Append(',')
                          .Append(lane.ToString("0.00", CultureInfo.InvariantCulture)).Append(");\n");
                    else
                        sb.Append('(').Append(AffMs(n.Time)).Append(',')
                          .Append(lane.ToString("0.00", CultureInfo.InvariantCulture)).Append(");\n");
                    continue;
                }
                int laneI = Math.Max(1, Math.Min(4, n.Col - 1));   // Col 2..5 → 官方 1..4
                if (n.Type == "hold")
                    sb.Append("hold(").Append(AffMs(n.Time)).Append(',').Append(AffMs(n.End)).Append(',')
                      .Append(laneI).Append(");\n");
                else
                    sb.Append('(').Append(AffMs(n.Time)).Append(',').Append(laneI).Append(");\n");
            }
            return sb.ToString();
        }

        /* ================= Cytus (.txt VERSION 2) ================= */

        public static Chart ParseCytus(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, Mode = GameMode.Cytus, ModeName = "Cytus", KeyCount = 4 };
            double bpm = 120;
            double pageSize = 4;        // 页时长（拍），默认 1 页 = 4 拍
            int pageShift = 0;

            foreach (var rawLine in text.Split('\n'))
            {
                var s = rawLine.Trim();
                if (s.StartsWith("BPM", StringComparison.OrdinalIgnoreCase))
                {
                    var p = SplitWs(s);
                    if (p.Length >= 2) bpm = D(p[1], bpm);
                }
                else if (s.StartsWith("PAGE_SIZE", StringComparison.OrdinalIgnoreCase))
                {
                    var p = SplitWs(s);
                    if (p.Length >= 2) pageSize = D(p[1], pageSize);
                }
                else if (s.StartsWith("PAGE_SHIFT", StringComparison.OrdinalIgnoreCase))
                {
                    var p = SplitWs(s);
                    if (p.Length >= 2) pageShift = I(p[1], 0);
                }
            }
            double pageMs = pageSize * 60000.0 / bpm;

            // NOTE	page	id	type	x	y  → Time = (page + y) * pageMs
            var tuples = new List<(double time, int id, int type, double x, double y)>();
            foreach (var rawLine in text.Split('\n'))
            {
                var s = rawLine.Trim();
                if (!s.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)) continue;
                var p = SplitWs(s);
                if (p.Length < 6) continue;
                int page = I(p[1], 0) - pageShift;
                int id = I(p[2], 0);
                int type = I(p[3], 0);
                double x = D(p[4], 0.5);
                double y = D(p[5], 0.5);
                tuples.Add(((page + y) * pageMs, id, type, x, y));
            }
            tuples.Sort((a, b) => a.time.CompareTo(b.time));

            foreach (var (time, id, type, x, y) in tuples)
            {
                int col = x < 0.25 ? 0 : x < 0.5 ? 1 : x < 0.75 ? 2 : 3;
                double end = time;
                string ntype;
                if (type == 2) // hold：下一页同 id 音符时间，否则 +1 页时长
                {
                    ntype = "hold";
                    end = time + pageMs;
                    foreach (var t2 in tuples)
                        if (t2.id == id && t2.time > time + 1e-6) { end = t2.time; break; }
                }
                else if (type == 1) ntype = "drag";     // drag 按 tap 判定（Sanitize 会归零 End）
                else ntype = "tap";                      // 0 tap；flick 等也按 tap
                chart.Notes.Add(new Note { Time = time, End = end, Col = col, X = x, Y = y, Type = ntype });
            }
            chart.Bpm = bpm;
            return Sanitize(chart);
        }

        /* ================= Phigros (.json) ================= */

        public static Chart ParsePhigros(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, Mode = GameMode.Phigros, ModeName = "Phigros", KeyCount = 4 };
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            JsonElement lines = default;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("judgeLineList", out var jll) && jll.ValueKind == JsonValueKind.Array)
                lines = jll;
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("judgeLineGroup", out var jlg) && jlg.ValueKind == JsonValueKind.Object
                     && jlg.TryGetProperty("judgeLineList", out var jll2) && jll2.ValueKind == JsonValueKind.Array)
                lines = jll2;

            // RPE v1.4+ 谱面（Phira/Re:PhiEdit）：根含 BPMList，时间用 [小节,拍,tick] 三元组，事件含 easingType/bezierPoints
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("BPMList", out var bpmList))
                return ParsePhigrosRpe(root, bpmList, lines, sourcePath);

            double unitScale = DetectPhigrosScale(root);

            if (lines.ValueKind == JsonValueKind.Array)
            {
                int lineIdx = 0;
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object) { lineIdx++; continue; }
                    if (lineIdx == 0) chart.Bpm = NumAt(line, "bpm") ?? 0;
                    AddPhigrosEvents(chart, line, lineIdx, unitScale);
                    AddPhigrosNotes(chart, line, "notesAbove", lineIdx, unitScale);
                    AddPhigrosNotes(chart, line, "notesBelow", lineIdx, unitScale);
                    lineIdx++;
                }
            }
            else
            {
                // 结构无法识别（如 PhiEdit 导出）：尽力提取所有含 time/positionX 的对象
                var objs = FindNoteObjects(root);
                if (objs.Count == 0)
                    throw new InvalidDataException("Phigros 谱面结构无法识别：未找到 judgeLineList，也没有含 time/positionX 的音符对象");
                foreach (var o in objs) chart.Notes.Add(PhigrosNote(o, 0, unitScale));
            }

            if (chart.Bpm <= 0) chart.Bpm = 120;
            // 标题：根字段 title 优先，其次同目录 meta.json（官方谱面元数据），最后回退文件名
            chart.Title = StrAt(root, "title") ?? ReadPhigrosMetaTitle(sourcePath)
                          ?? Path.GetFileNameWithoutExtension(sourcePath);
            return Sanitize(chart);
        }

        // ================= RPE v1.4+（Phira/Re:PhiEdit 谱面格式）=================

        /// <summary>RPE 缓动类型数字 → 名称（对应 Phira RPE_TWEEN_MAP 30 项；0/1=Linear）。</summary>
        static readonly string[] RpeEaseNames =
        {
            "Linear", "Linear", "EaseOutSine", "EaseInSine", "EaseOutQuad", "EaseInQuad",
            "EaseInOutSine", "EaseInOutQuad", "EaseOutCubic", "EaseInCubic",
            "EaseOutQuart", "EaseInQuart", "EaseInOutCubic", "EaseInOutQuart",
            "EaseOutQuint", "EaseInQuint", "EaseOutExpo", "EaseInExpo",
            "EaseOutCirc", "EaseInCirc", "EaseOutBack", "EaseInBack",
            "EaseInOutCirc", "EaseInOutBack", "EaseOutElastic", "EaseInElastic",
            "EaseOutBounce", "EaseInBounce", "EaseInOutBounce", "EaseInOutElastic"
        };

        /// <summary>RPE 谱面解析：时间三元组 [小节,拍,tick]（拍 = i + n/d，BPMList 分段积分秒），事件 easingType/bezierPoints 对齐 RPE 标准。</summary>
        static Chart ParsePhigrosRpe(JsonElement root, JsonElement bpmList, JsonElement lines, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, Mode = GameMode.Phigros, ModeName = "Phigros", KeyCount = 4 };
            // BPM 表：(拍, bpm)，按拍分段积分 → 毫秒
            var bpmKfs = new List<(double beat, double bpm)>();
            double firstBpm = 120;
            if (bpmList.ValueKind == JsonValueKind.Array)
                foreach (var it in bpmList.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    double beat = RpeBeatAt(it, "startTime");
                    double bpm = NumAt(it, "bpm") ?? 120;
                    if (bpmKfs.Count == 0) firstBpm = bpm;
                    bpmKfs.Add((beat, bpm));
                }
            if (bpmKfs.Count == 0) bpmKfs.Add((0, firstBpm));
            bpmKfs.Sort((a, b) => a.beat.CompareTo(b.beat));
            chart.Bpm = firstBpm;

            // 拍 → 毫秒（分段线性：每段 [beat_k, beat_{k+1}) 内 bpm 恒定）
            double BeatMs(double beat)
            {
                if (bpmKfs.Count == 1) return beat * 60000.0 / bpmKfs[0].bpm;
                int k = 0;
                while (k < bpmKfs.Count - 2 && bpmKfs[k + 1].beat <= beat) k++;
                double t = 0;
                for (int i = 0; i < k; i++)
                    t += (bpmKfs[i + 1].beat - bpmKfs[i].beat) * 60000.0 / bpmKfs[i].bpm;
                t += (beat - bpmKfs[k].beat) * 60000.0 / bpmKfs[k].bpm;
                return t;
            }

            int lineIdx = 0;
            if (lines.ValueKind == JsonValueKind.Array)
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object) { lineIdx++; continue; }
                    AddRpeNotes(chart, line, lineIdx, BeatMs);
                    AddRpeEvents(chart, line, lineIdx, BeatMs);
                    lineIdx++;
                }
            // META：标题/作者/音频/偏移
            if (root.TryGetProperty("META", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                chart.Title = StrAt(meta, "name") ?? StrAt(meta, "title") ?? Path.GetFileNameWithoutExtension(sourcePath);
                chart.Artist = StrAt(meta, "composer") ?? "";
                // 音频：META.song 是 zip 内文件名，优先映射为同目录同名音频；找不到则回退 zip 内名字
                string song = StrAt(meta, "song") ?? "";
                if (!string.IsNullOrEmpty(song))
                {
                    var dir = Path.GetDirectoryName(sourcePath);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) chart.AudioFile = song;
                    else
                    {
                        string ext = Path.GetExtension(song);
                        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                        string[] cand = { Path.Combine(dir, baseName + ext), Path.Combine(dir, song) };
                        chart.AudioFile = File.Exists(cand[0]) ? cand[0] : (File.Exists(cand[1]) ? cand[1] : song);
                    }
                }
                double? off = NumAt(meta, "offset");
                if (off.HasValue) chart.Offset = off.Value;
                chart.Version = StrAt(meta, "charter") ?? "";
            }
            if (string.IsNullOrEmpty(chart.Title)) chart.Title = Path.GetFileNameWithoutExtension(sourcePath);
            return Sanitize(chart);
        }

        /// <summary>RPE 时间三元组 [i,n,d] = i + n/d 拍；无三元组回退 0。</summary>
        static double RpeBeatAt(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 2)
            {
                double i = v[0].GetDouble(), n = v[1].GetDouble(), d = 1;
                if (v.GetArrayLength() >= 3 && v[2].ValueKind == JsonValueKind.Number && v[2].GetDouble() != 0)
                    d = v[2].GetDouble();
                return i + n / d;
            }
            return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        }

        static void AddRpeNotes(Chart c, JsonElement line, int lineIdx, Func<double, double> beatMs)
        {
            if (!line.TryGetProperty("notes", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var n in arr.EnumerateArray())
            {
                if (n.ValueKind != JsonValueKind.Object) continue;
                double st = beatMs(RpeBeatAt(n, "startTime"));
                double en = beatMs(RpeBeatAt(n, "endTime"));
                int type = (int)Math.Round(NumAt(n, "type") ?? 1);   // RPE: 1=tap 2=drag 3=hold 4=flick
                double px = NumAt(n, "positionX") ?? NumAt(n, "x") ?? 0.5;
                double xNorm = Math.Abs(px) > 1 ? (px + 675.0) / 1350.0 : px;   // RPE 坐标 -675..675 → 0..1
                xNorm = Clamp01(xNorm);
                double yOff = NumAt(n, "yOffset") ?? 0;
                double yNorm = Clamp01(0.75 + yOff / 900.0);
                string ntype = type == 3 ? "hold" : type == 2 ? "drag" : type == 4 ? "flick" : "tap";
                if (en < st) en = st;
                c.Notes.Add(new Note { Time = st, End = en, Col = -1, X = xNorm, Y = yNorm, Line = lineIdx, Type = ntype });
            }
        }

        static void AddRpeEvents(Chart c, JsonElement line, int lineIdx, Func<double, double> beatMs)
        {
            if (!line.TryGetProperty("eventLayers", out var layers) || layers.ValueKind != JsonValueKind.Array) return;
            foreach (var layer in layers.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object) continue;
                foreach (var t in EventTypes)
                {
                    if (layer.TryGetProperty(t + "Events", out var arr)) AddRpeEventArray(c, arr, t, lineIdx, beatMs);
                }
            }
        }

        static void AddRpeEventArray(Chart c, JsonElement arr, string type, int lineIdx, Func<double, double> beatMs)
        {
            if (arr.ValueKind != JsonValueKind.Array) return;
            foreach (var ev in arr.EnumerateArray())
            {
                if (ev.ValueKind != JsonValueKind.Object) continue;
                double st = beatMs(RpeBeatAt(ev, "startTime"));
                double en = beatMs(RpeBeatAt(ev, "endTime"));
                double v = EventVal(type, NumAt(ev, "start") ?? 0);
                double evv = EventVal(type, NumAt(ev, "end") ?? 0);
                var ce = new ChartEvent { Time = st, End = en, Type = type, Value = v, EndValue = evv, Line = lineIdx };
                // easingType 数字 → 名称（RPE_TWEEN_MAP）；bezier 标志 → 4 控制点数组（RPE bezierPoints 是 [x1,y1,x2,y2] 两点制，端点隐含 (0,0)→(1,1)）
                int? et = (int?)NumAt(ev, "easingType");
                if (et.HasValue && et.Value >= 0 && et.Value < RpeEaseNames.Length)
                    ce.Ease = RpeEaseNames[et.Value];
                int bezier = (int)Math.Round(NumAt(ev, "bezier") ?? 0);
                if (bezier != 0 && ev.TryGetProperty("bezierPoints", out var bp) && bp.ValueKind == JsonValueKind.Array && bp.GetArrayLength() >= 4)
                {
                    double x1 = bp[0].GetDouble(), y1 = bp[1].GetDouble(), x2 = bp[2].GetDouble(), y2 = bp[3].GetDouble();
                    ce.Bezier = new[] { 0.0, 0.0, x1, y1, x2, y2, 1.0, 1.0 };   // 转换为主项目 4 控制点格式
                }
                // 绑定组（T59 D2j）：RPE "group" 数字 或 "bindGroup":{"value":N}
                if (ce.Group == 0)
                {
                    double? grp = NumAt(ev, "group");
                    if (!grp.HasValue && ev.TryGetProperty("bindGroup", out var bg) && bg.ValueKind == JsonValueKind.Object)
                        grp = NumAt(bg, "value");
                    if (grp.HasValue) ce.Group = (int)Math.Round(grp.Value);
                }
                c.Events.Add(ce);
            }
        }

        /// <summary>读取 Phigros 官方谱面同目录 meta 文件的歌曲名（meta.json / {同名}_meta.json / name/title/songName 字段兼容）。
        /// 只匹配与谱面文件同名的 meta（如 Undertale_EZ.json ↔ Undertale_meta.json），避免同目录多谱面串标题。</summary>
        static string ReadPhigrosMetaTitle(string sourcePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(sourcePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);   // 如 Undertale_EZ
                // 优先：去掉难度后缀后的同名 meta（Undertale_EZ → Undertale）
                string stem = baseName;
                foreach (var suf in new[] { "_EZ", "_HD", "_IN", "_AT", "_Legacy" })
                    if (baseName.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { stem = baseName.Substring(0, baseName.Length - suf.Length); break; }
                string[] cands = Directory.GetFiles(dir, stem + "*meta*.json")
                    .Concat(Directory.GetFiles(dir, "meta.json"))
                    .Distinct().ToArray();
                foreach (var f in cands)
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f));
                    var r = doc.RootElement;
                    string t = StrAt(r, "title") ?? StrAt(r, "name") ?? StrAt(r, "songName") ?? null;
                    if (!string.IsNullOrEmpty(t)) return t;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>Phigros 时间单位：&gt;1e5 视为微秒(÷1000)，&lt;1e4 视为秒(×1000)，否则毫秒（按全局最大值判断，避免文件内混判）。
        /// 修正（人类试玩 AI 实测发现）：Re:PhiEdit v3 导出用 endTime=1000000000 作"事件延续到曲末"的哨兵值，
        /// 会把单位误判成微秒导致全部音符时间塌缩到 0ms 附近；统计时忽略 ≥1e8 的哨兵值。</summary>
        static double DetectPhigrosScale(JsonElement root)
        {
            double max = 0;
            CollectMaxTime(root, ref max, 1e8);   // 忽略 ≥1e8 的哨兵值（Re:PhiEdit "持续到结束"标记）
            if (max > 1e5) return 1.0 / 1000.0;
            if (max < 1e4) return 1000.0;
            return 1.0;
        }

        static void CollectMaxTime(JsonElement e, ref double max, double sentinelIgnore = 1e8)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var key in new[] { "time", "startTime", "endTime", "holdTime" })
                        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                        {
                            double val = v.GetDouble();
                            if (val < sentinelIgnore) max = Math.Max(max, val);
                        }
                    foreach (var p in e.EnumerateObject()) CollectMaxTime(p.Value, ref max, sentinelIgnore);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) CollectMaxTime(item, ref max, sentinelIgnore);
                    break;
            }
        }

        static void AddPhigrosNotes(Chart c, JsonElement line, string field, int lineIdx, double unitScale)
        {
            if (!line.TryGetProperty(field, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var n in arr.EnumerateArray())
                if (n.ValueKind == JsonValueKind.Object)
                    c.Notes.Add(PhigrosNote(n, lineIdx, unitScale));
        }

        static Note PhigrosNote(JsonElement n, int line, double unitScale)
        {
            double time = (NumAt(n, "time") ?? 0) * unitScale;
            int type = (int)Math.Round(NumAt(n, "type") ?? 1);           // 1 tap 2 drag 3 hold 4 flick
            double posX = NumAt(n, "positionX") ?? NumAt(n, "x") ?? 0.5;
            double xNorm = posX > 1 ? posX / 4.0 : posX;                 // positionX 已是 0..3 整数则归一
            xNorm = Clamp01(xNorm);
            int col = Math.Max(0, Math.Min(3, (int)Math.Floor(xNorm * 4)));
            double holdTime = (NumAt(n, "holdTime") ?? 0) * unitScale;
            double yRaw = NumAt(n, "floorPosition") ?? NumAt(n, "y") ?? 0;
            double yNorm = Clamp01(yRaw > 1 ? yRaw / 8.0 : yRaw);        // floorPosition 0..8 → 0..1
            string ntype = type == 3 ? "hold" : type == 2 ? "drag" : type == 4 ? "flick" : "tap";   // t6/t7：旧格式补全 drag/flick（与 AddRpeNotes:435 同构；原为 type==3?"hold":"tap" 折叠遗漏）
            double end = ntype == "hold" ? time + holdTime : time;
            if (end < time) end = time;
            // ===== RPE 音符编辑面板字段（D2r 复刻）：side/width/alpha/visibleTime（对 time 作 RPE 秒→ms 换算外不另缩放）=====
            int side = (int)Math.Round(NumAt(n, "side") ?? 0);
            double width = NumAt(n, "width") ?? 1.0;
            double alphaRaw = NumAt(n, "alpha") ?? 1.0;
            double alpha = alphaRaw > 1 && alphaRaw <= 255 ? alphaRaw / 255.0 : Clamp01(alphaRaw);   // 255 制容错
            double vis = (NumAt(n, "visibleTime") ?? 999999);
            return new Note
            {
                Time = time, End = end, Col = col, X = xNorm, Y = yNorm, Line = line, Type = ntype,
                Side = side, Width = Math.Max(0.1, width), Alpha = alpha, VisMs = vis
            };
        }

        static readonly string[] EventTypes = { "moveX", "moveY", "rotate", "alpha", "speed" };

        static void AddPhigrosEvents(Chart c, JsonElement line, int lineIdx, double unitScale)
        {
            // eventLayers：每层含 moveX/moveY/rotate/alpha/speed 事件数组（兼容 Re:PhiEdit 的 xxxEvents 命名）
            if (line.TryGetProperty("eventLayers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            {
                foreach (var layer in layers.EnumerateArray())
                {
                    if (layer.ValueKind != JsonValueKind.Object) continue;
                    foreach (var t in EventTypes)
                    {
                        if (layer.TryGetProperty(t, out var arr)) AddEventArray(c, arr, t, lineIdx, unitScale);
                        if (layer.TryGetProperty(t + "Events", out var arr2)) AddEventArray(c, arr2, t, lineIdx, unitScale);
                    }
                }
            }
            // line 顶层 moveX/moveY/rotate/alpha/speed 字段（同样兼容 xxxEvents 命名）
            foreach (var t in EventTypes)
            {
                if (line.TryGetProperty(t, out var arr)) AddEventArray(c, arr, t, lineIdx, unitScale);
                if (line.TryGetProperty(t + "Events", out var arr2)) AddEventArray(c, arr2, t, lineIdx, unitScale);
            }
        }

        static void AddEventArray(Chart c, JsonElement arr, string type, int lineIdx, double unitScale)
        {
            if (arr.ValueKind != JsonValueKind.Array) return;
            foreach (var ev in arr.EnumerateArray())
            {
                if (ev.ValueKind != JsonValueKind.Object) continue;
                double st = (NumAt(ev, "startTime") ?? NumAt(ev, "t") ?? 0) * unitScale;
                double? enRaw = NumAt(ev, "endTime") ?? NumAt(ev, "e");
                double en = enRaw.HasValue ? enRaw.Value * unitScale : double.NaN;
                double v = EventVal(type, NumAt(ev, "start") ?? NumAt(ev, "v") ?? 0);
                double evv = EventVal(type, NumAt(ev, "end") ?? NumAt(ev, "ev") ?? 0);
                var ce = new ChartEvent { Time = st, End = en, Type = type, Value = v, EndValue = evv, Line = lineIdx };
                // RPE 扩展：easing 缓动名 / next 衔接(布尔或数字) / beziers 控制点（4×2，x 单调不减）
                ce.Ease = StrAt(ev, "easing") ?? StrAt(ev, "easeType") ?? StrAt(ev, "ease");
                if (ev.TryGetProperty("next", out var nv))
                {
                    if (nv.ValueKind == JsonValueKind.True) ce.Next = true;
                    else if (nv.ValueKind == JsonValueKind.False) ce.Next = false;
                    else if (nv.ValueKind == JsonValueKind.Number) ce.Next = nv.GetDouble() != 0;
                }
                if (ev.TryGetProperty("bezierPoints", out var bp) && bp.ValueKind == JsonValueKind.Array)
                {
                    var pts = new List<double>();
                    foreach (var p in bp.EnumerateArray())
                    {
                        if (p.ValueKind == JsonValueKind.Array)
                        {
                            if (p.GetArrayLength() >= 2) { pts.Add(p[0].GetDouble()); pts.Add(p[1].GetDouble()); }
                        }
                        else if (p.ValueKind == JsonValueKind.Object)
                        {
                            var x = NumAt(p, "x"); var y = NumAt(p, "y");
                            if (x.HasValue && y.HasValue) { pts.Add(x.Value); pts.Add(y.Value); }
                        }
                        else if (p.ValueKind == JsonValueKind.Number)
                        {
                            // 平铺数值数组 [x0,y0,x1,y1,...]
                            pts.Add(p.GetDouble());
                        }
                    }
                    if (pts.Count >= 8) ce.Bezier = pts.ToArray();   // 取前 4 点；不足 8 个数字则放弃（回退线性）
                }
                // 绑定组（T59 D2j）：group 或 bindGroup.value
                if (ce.Group == 0)
                {
                    double? grp = NumAt(ev, "group");
                    if (!grp.HasValue && ev.TryGetProperty("bindGroup", out var bg) && bg.ValueKind == JsonValueKind.Object)
                        grp = NumAt(bg, "value");
                    if (grp.HasValue) ce.Group = (int)Math.Round(grp.Value);
                }
                c.Events.Add(ce);
            }
        }

        /// <summary>事件值：rotate/alpha 保留原值；moveX/moveY/speed 若绝对值明显大于 1 视为 ×1000 整数归一。</summary>
        static double EventVal(string type, double v)
        {
            if (type == "rotate" || type == "alpha") return v;
            return Math.Abs(v) > 2 ? v / 1000.0 : v;
        }

        static List<JsonElement> FindNoteObjects(JsonElement e)
        {
            var list = new List<JsonElement>();
            if (e.ValueKind == JsonValueKind.Object)
            {
                bool hasTime = e.TryGetProperty("time", out _) || e.TryGetProperty("startTime", out _) || e.TryGetProperty("t", out _);
                bool hasPos = e.TryGetProperty("positionX", out _) || e.TryGetProperty("x", out _) || e.TryGetProperty("pos", out _) || e.TryGetProperty("lane", out _);
                if (hasTime && hasPos) list.Add(e);
                foreach (var p in e.EnumerateObject()) list.AddRange(FindNoteObjects(p.Value));
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray()) list.AddRange(FindNoteObjects(item));
            }
            return list;
        }

        /* ================= Milestone 原生 .mil (JSON) ================= */

        public static Chart ParseMil(string text, string sourcePath)
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var chart = new Chart { SourcePath = sourcePath };

            chart.Mode = ModeFromString(StrAt(root, "mode") ?? "mania");
            chart.ModeName = ModeDisplayName(chart.Mode);
            chart.Title = StrAt(root, "title") ?? Path.GetFileNameWithoutExtension(sourcePath);
            chart.Artist = StrAt(root, "artist") ?? "";
            chart.Version = StrAt(root, "version") ?? "";
            chart.Bpm = NumAt(root, "bpm") ?? 0;
            chart.Offset = NumAt(root, "offset") ?? 0;
            chart.Od = NumAt(root, "od") ?? 0;
            chart.Dr = NumAt(root, "dr") ?? 0;
            chart.Ar = NumAt(root, "ar") ?? 5;
            chart.AudioFile = StrAt(root, "audio") ?? "";
            chart.SliderTickRate = NumAt(root, "tickRate") ?? 1;   // 诊断②：.mil 往返读 tickRate（osu tick 率落盘约定）
            chart.KeyCount = (int)Math.Round(NumAt(root, "keys") ?? 4);
            chart.DanName = StrAt(root, "danName") ?? "";
            chart.DanSet = StrAt(root, "danSet") ?? "";

            AddMilNotes(chart, root);
            AddMilEvents(chart, root);
            AddMilLineParents(chart, root);
            AddMilLineMeta(chart, root);
            if (root.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                // 单一谱面多模式：解析各部件；根字段镜像第 0 部件（保持既有消费方兼容）
                chart.Parts = new List<ChartPart>();
                foreach (var p in parts.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var part = new ChartPart
                    {
                        Name = StrAt(p, "name") ?? "",
                        Mode = ModeFromString(StrAt(p, "mode") ?? "mania"),
                        KeyCount = (int)Math.Round(NumAt(p, "keys") ?? 4)
                    };
                    var sub = new Chart { Mode = part.Mode, KeyCount = part.KeyCount };
                    AddMilNotes(sub, p);
                    AddMilEvents(sub, p);
                    AddMilLineParents(sub, p);
                    AddMilLineMeta(sub, p);
                    Sanitize(sub);   // 各部件独立消毒（排序/钳制/类型白名单）
                    part.Notes = sub.Notes;
                    part.Events = sub.Events;
                    part.LineParents = sub.LineParents;
                    part.LineMeta = sub.LineMeta;
                    if (part.Notes.Count == 0 && part.Events.Count == 0 && chart.Parts.Count > 0) continue; // 空部件跳过
                    chart.Parts.Add(part);
                }
                if (chart.Parts.Count > 0)
                {
                    var first = chart.Parts[0];
                    chart.Mode = first.Mode;
                    chart.ModeName = ModeDisplayName(first.Mode);
                    chart.KeyCount = first.KeyCount;
                    chart.Notes = first.Notes;
                    chart.Events = first.Events;
                    chart.LineParents = first.LineParents;
                    chart.LineMeta = first.LineMeta;
                }
            }

            // ===== 多场同屏（t27）：stages 读取 + 部件/音符 Field 赋值 + 平铺 f 分派 =====
            if (root.TryGetProperty("stages", out var stg) && stg.ValueKind == JsonValueKind.Array)
            {
                var stagesList = new List<Stage>();
                int si = 0;
                foreach (var s in stg.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) { si++; continue; }
                    double sx = Math.Clamp(NumAt(s, "x") ?? 0, 0, 1);
                    double sy = Math.Clamp(NumAt(s, "y") ?? 0, 0, 1);
                    double sw = Math.Clamp(NumAt(s, "w") ?? 1, 0.05, 1);
                    double sh = Math.Clamp(NumAt(s, "h") ?? 1, 0.05, 1);
                    if (sx + sw > 1.0001) sw = 1 - sx;
                    if (sy + sh > 1.0001) sh = 1 - sy;
                    var stage = new Stage
                    {
                        Id = si,
                        Name = StrAt(s, "name") ?? "",
                        Mode = chart.Mode,
                        KeyCount = chart.KeyCount,
                        X = sx, Y = sy, W = sw, H = sh
                    };
                    if (s.TryGetProperty("keyMap", out var kma) && kma.ValueKind == JsonValueKind.Array)
                    {
                        var kl = new List<Keys>();
                        foreach (var kv in kma.EnumerateArray())
                            if (kv.ValueKind == JsonValueKind.String
                                && Enum.TryParse<Keys>(kv.GetString(), true, out var kk)) kl.Add(kk);
                        if (kl.Count > 0) stage.KeyMap = kl.ToArray();
                    }
                    stagesList.Add(stage);
                    si++;
                }
                chart.Stages = stagesList;
            }
            if (chart.Stages != null && chart.Stages.Count > 0)
            {
                // 部件容器在场归属（stage i ⇔ parts[i]）
                if (chart.Parts != null)
                {
                    for (int pi = 0; pi < chart.Parts.Count; pi++)
                    {
                        var pp = chart.Parts[pi];
                        if (pp == null) continue;
                        foreach (var nn in pp.Notes) if (nn != null) nn.Field = pi;
                        foreach (var ee in pp.Events) if (ee != null) ee.Field = pi;
                        if (pi < chart.Stages.Count)
                        {
                            chart.Stages[pi].Mode = pp.Mode;
                            chart.Stages[pi].KeyCount = pp.KeyCount;
                        }
                    }
                }
                // 平铺兼容：根 notes/events 带 f（无 parts 或 parts 为空 → 展开为 parts）
                if (chart.Parts == null || chart.Parts.Count == 0)
                {
                    bool flat = chart.Notes.Any(n => n != null && n.Field != 0)
                             || chart.Events.Any(ev => ev != null && ev.Field != 0);
                    if (flat)
                    {
                        chart.Parts = new List<ChartPart>();
                        for (int i = 0; i < chart.Stages.Count; i++)
                            chart.Parts.Add(new ChartPart
                            {
                                Name = chart.Stages[i].Name,
                                Mode = i == 0 ? chart.Mode : chart.Stages[i].Mode,
                                KeyCount = i == 0 ? chart.KeyCount : chart.Stages[i].KeyCount
                            });
                        foreach (var nn in chart.Notes)
                        {
                            if (nn == null) continue;
                            int f = Math.Max(0, Math.Min(chart.Stages.Count - 1, nn.Field));
                            chart.Parts[f].Notes.Add(nn); nn.Field = f;
                        }
                        foreach (var ee in chart.Events)
                        {
                            if (ee == null) continue;
                            int f = Math.Max(0, Math.Min(chart.Stages.Count - 1, ee.Field));
                            chart.Parts[f].Events.Add(ee); ee.Field = f;
                        }
                        chart.Notes = chart.Parts[0].Notes;
                        chart.Events = chart.Parts[0].Events;
                    }
                }
            }
            return Sanitize(chart);
        }

        /// <summary>读取 milestone-1 的 lineParents 数组（Phigros 父子线，phimakor；根与 parts 共用；元素=-1 或父线索引）。</summary>
        static void AddMilLineParents(Chart chart, JsonElement container)
        {
            if (container.ValueKind != JsonValueKind.Object) return;
            chart.LineParents = new List<int>();
            if (!container.TryGetProperty("lineParents", out var lp) || lp.ValueKind != JsonValueKind.Array) return;
            foreach (var v in lp.EnumerateArray())
                chart.LineParents.Add((int)Math.Round(NumAtEl(v)));
        }

        /// <summary>读取 milestone-1 的 lineMeta 数组（D2o：判定线元数据 名称/分组/Z/Cover；根与 parts 共用）。</summary>
        static void AddMilLineMeta(Chart chart, JsonElement container)
        {
            if (container.ValueKind != JsonValueKind.Object) return;
            chart.LineMeta = new List<PhigrosLineMeta>();
            if (!container.TryGetProperty("lineMeta", out var lm) || lm.ValueKind != JsonValueKind.Array) return;
            foreach (var v in lm.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                chart.LineMeta.Add(new PhigrosLineMeta
                {
                    Name = StrAt(v, "name") ?? "",
                    Group = (int)Math.Round(NumAt(v, "group") ?? 0),
                    Z = (int)Math.Round(NumAt(v, "z") ?? 0),
                    Cover = v.TryGetProperty("cover", out var cv) && cv.ValueKind == JsonValueKind.True
                });
            }
        }

        /// <summary>从 JSON 容器对象读取 notes 数组并写入目标谱面（根与 parts 共用）。</summary>
        static void AddMilNotes(Chart chart, JsonElement container)
        {
            if (container.ValueKind != JsonValueKind.Object) return;
            if (!container.TryGetProperty("notes", out var notesArr) || notesArr.ValueKind != JsonValueKind.Array) return;
            foreach (var n in notesArr.EnumerateArray())
            {
                if (n.ValueKind != JsonValueKind.Object) continue;
                double t = NumAt(n, "t") ?? 0;
                double e = NumAt(n, "e") ?? t;
                int c = (int)Math.Round(NumAt(n, "c") ?? 0);
                double x = Clamp01(NumAt(n, "x") ?? 0.5);
                // Arcaea 天键高度：缺省 = 1.0（天空线）；其余模式沿用 0.5
                double yDef = chart.Mode == GameMode.Arcaea ? 1.0 : 0.5;
                double y = Clamp01(n.TryGetProperty("y", out _) ? (NumAt(n, "y") ?? yDef) : yDef);
                double ex = NumAt(n, "ex") ?? x;
                double ey = NumAt(n, "ey") ?? y;
                int ec = (int)Math.Round(NumAt(n, "ec") ?? -1);
                string type = StrAt(n, "type") ?? "tap";
                int kind = (int)Math.Round(NumAt(n, "kind") ?? 0);
                int line = (int)Math.Round(NumAt(n, "line") ?? 0);
                bool decor = n.TryGetProperty("dec", out var decEl) && decEl.ValueKind == JsonValueKind.True;
                char slt = (StrAt(n, "st") ?? "") is string s2 && s2.Length > 0 ? s2[0] : '\0';
                int reps = (int)Math.Round(NumAt(n, "rp") ?? 1);
                List<(double, double)> curve = null;
                if (n.TryGetProperty("cv", out var cv) && cv.ValueKind == JsonValueKind.Array)
                {
                    // 曲线点数组：元素为对象 {x,y}
                    curve = new List<(double, double)>();
                    foreach (var pt in cv.EnumerateArray())
                    {
                        if (pt.ValueKind != JsonValueKind.Object) continue;
                        curve.Add((Clamp01(PtCoord(pt, "x")), Clamp01(PtCoord(pt, "y"))));
                    }
                }
                List<(double, double, double)> arc3 = null;
                if (n.TryGetProperty("a3", out var a3) && a3.ValueKind == JsonValueKind.Array)
                {
                    // Arcaea arc 3D 中间控制点：{x=轨, y=时间比例, z=天地间高度}
                    arc3 = new List<(double, double, double)>();
                    foreach (var pt in a3.EnumerateArray())
                    {
                        if (pt.ValueKind != JsonValueKind.Object) continue;
                        arc3.Add((Clamp01(PtCoord(pt, "x")), Clamp01(PtCoord(pt, "y")), Clamp01(PtCoord(pt, "z"))));
                    }
                }
                chart.Notes.Add(new Note
                {
                    Time = t, End = e, Col = c, X = x, Y = y,
                    EndX = ex, EndY = ey, EndCol = ec,
                    Type = type, Kind = kind, Line = line, Decor = decor,
                    SliderType = slt, Repeats = reps, Curve = curve, Arc3 = arc3,
                    // RPE 音符编辑字段（mil 往返：side/width/alpha/vis）
                    Side = (int)Math.Round(NumAt(n, "side") ?? 0),
                    Width = Math.Max(0.1, NumAt(n, "width") ?? 1.0),
                    Alpha = Clamp01(NumAt(n, "alpha") ?? 1.0),
                    VisMs = NumAt(n, "vis") ?? 999999,
                    Field = (int)Math.Round(NumAt(n, "f") ?? 0)
                });
            }
        }

        /// <summary>从 JSON 容器对象读取 events 数组并写入目标谱面（根与 parts 共用）。</summary>
        static void AddMilEvents(Chart chart, JsonElement container)
        {
            if (container.ValueKind != JsonValueKind.Object) return;
            if (!container.TryGetProperty("events", out var evArr) || evArr.ValueKind != JsonValueKind.Array) return;
            foreach (var ev in evArr.EnumerateArray())
            {
                if (ev.ValueKind != JsonValueKind.Object) continue;
                double t = NumAt(ev, "t") ?? 0;
                double? eRaw = NumAt(ev, "e");
                double e = eRaw.HasValue ? eRaw.Value : double.NaN;   // 缺 e = 瞬间事件
                string type = StrAt(ev, "type") ?? "moveY";
                double v = NumAt(ev, "v") ?? 0;
                double evv = NumAt(ev, "ev") ?? 0;
                int eline = (int)Math.Round(NumAt(ev, "line") ?? -1);
                string ease = StrAt(ev, "ease");
                int group = (int)Math.Round(NumAt(ev, "group") ?? 0);
                var ce2 = new ChartEvent { Time = t, End = e, Type = type, Value = v, EndValue = evv, Line = eline, Ease = ease, Group = group, Field = (int)Math.Round(NumAt(ev, "f") ?? 0) };
                // P0 数据保真：next + bezier 读回（mil 往返；与 AddEventArray 的 RPE 读取同语义）
                if (ev.TryGetProperty("next", out var nv))
                {
                    if (nv.ValueKind == JsonValueKind.True) ce2.Next = true;
                    else if (nv.ValueKind == JsonValueKind.False) ce2.Next = false;
                    else if (nv.ValueKind == JsonValueKind.Number) ce2.Next = nv.GetDouble() != 0;
                }
                if (ev.TryGetProperty("bezier", out var bz) && bz.ValueKind == JsonValueKind.Array)
                {
                    var pts = new List<double>();
                    foreach (var p in bz.EnumerateArray())
                    {
                        if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                        { pts.Add(p[0].GetDouble()); pts.Add(p[1].GetDouble()); }
                        else if (p.ValueKind == JsonValueKind.Number) pts.Add(p.GetDouble());
                        else if (p.ValueKind == JsonValueKind.Object)
                        {
                            // 数值可以是数字或字符串（0.###### 精度写法）
                            if (p.TryGetProperty("x", out var px) && p.TryGetProperty("y", out var py))
                            { pts.Add(NumAtEl(px)); pts.Add(NumAtEl(py)); }
                        }
                    }
                    if (pts.Count >= 8) ce2.Bezier = pts.ToArray();
                }
                chart.Events.Add(ce2);
            }
        }

        static double J(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

        /// <summary>把谱面序列化为 milestone-1 格式 JSON（默认值字段省略）。</summary>
        public static string SerializeMil(Chart c)
        {
            var root = new JsonObject
            {
                ["format"] = "milestone-1",
                ["mode"] = ModeToString(c.Mode)
            };
            if (!string.IsNullOrEmpty(c.Title)) root["title"] = c.Title;
            if (!string.IsNullOrEmpty(c.Artist)) root["artist"] = c.Artist;
            if (!string.IsNullOrEmpty(c.Version)) root["version"] = c.Version;
            if (c.Bpm != 0) root["bpm"] = J(c.Bpm);
            if (c.Offset != 0) root["offset"] = J(c.Offset);
            if (c.Od != 0) root["od"] = J(c.Od);
            if (c.Dr != 0) root["dr"] = J(c.Dr);
            if (c.Ar != 5 && c.Ar != 0) root["ar"] = J(c.Ar);
            if (Math.Abs(c.SliderTickRate - 1) > 1e-9 && c.SliderTickRate > 0) root["tickRate"] = J(c.SliderTickRate);   // 诊断②：.mil 往返写 tickRate（osu tick 率）
            if (!string.IsNullOrEmpty(c.AudioFile)) root["audio"] = c.AudioFile;
            if (c.KeyCount != 0) root["keys"] = c.KeyCount;
            if (!string.IsNullOrEmpty(c.DanName)) root["danName"] = c.DanName;
            if (!string.IsNullOrEmpty(c.DanSet)) root["danSet"] = c.DanSet;

            root["notes"] = SerializeNotes(c.Notes);
            root["events"] = SerializeEvents(c.Events);
            // Phigros 父子线（phimakor）：每线父线索引（-1=无）
            if (c.LineParents != null && c.LineParents.Count > 0 && c.LineParents.Any(p => p >= 0))
            {
                var lpArr = new JsonArray();
                foreach (var p in c.LineParents) lpArr.Add(J(p));
                root["lineParents"] = lpArr;
            }
            // D2o：判定线元数据（名称/分组/Z/Cover）——非空时落盘
            if (c.LineMeta != null && c.LineMeta.Count > 0 && c.LineMeta.Any(m => m != null && (!string.IsNullOrEmpty(m.Name) || m.Group != 0 || m.Z != 0 || m.Cover)))
                root["lineMeta"] = SerializeLineMeta(c.LineMeta);

            // 单一谱面多模式：>0 时写出 parts 数组（根字段 = 第 0 部件）
            if (c.Parts != null && c.Parts.Count > 0)
            {
                var partsArr = new JsonArray();
                foreach (var p in c.Parts)
                {
                    if (p == null) continue;
                    var po = new JsonObject
                    {
                        ["name"] = string.IsNullOrEmpty(p.Name) ? ModeDisplayName(p.Mode) : p.Name,
                        ["mode"] = ModeToString(p.Mode)
                    };
                    if (p.KeyCount != 0) po["keys"] = p.KeyCount;
                    po["notes"] = SerializeNotes(p.Notes);
                    po["events"] = SerializeEvents(p.Events);
                    if (p.LineParents != null && p.LineParents.Count > 0 && p.LineParents.Any(x => x >= 0))
                    {
                        var lpArr2 = new JsonArray();
                        foreach (var x in p.LineParents) lpArr2.Add(J(x));
                        po["lineParents"] = lpArr2;
                    }
                    if (p.LineMeta != null && p.LineMeta.Count > 0 && p.LineMeta.Any(m => m != null && (!string.IsNullOrEmpty(m.Name) || m.Group != 0 || m.Z != 0 || m.Cover)))
                        po["lineMeta"] = SerializeLineMeta(p.LineMeta);
                    partsArr.Add(po);
                }
                root["parts"] = partsArr;
            }

            // 多场同屏（t27）：stages 数组（与 parts 同索引；无 stages=旧谱单主场，不写）
            if (c.Stages != null && c.Stages.Count > 0 && c.Parts != null && c.Parts.Count > 0)
            {
                var stagesArr = new JsonArray();
                for (int i = 0; i < Math.Min(c.Stages.Count, c.Parts.Count); i++)
                {
                    var s = c.Stages[i];
                    if (s == null) continue;
                    var so = new JsonObject
                    {
                        ["id"] = s.Id,
                        ["name"] = string.IsNullOrEmpty(s.Name) ? "" : s.Name,
                        ["x"] = J(s.X), ["y"] = J(s.Y), ["w"] = J(s.W), ["h"] = J(s.H)
                    };
                    if (s.KeyMap != null && s.KeyMap.Length > 0)
                    {
                        var km = new JsonArray();
                        foreach (var k in s.KeyMap) km.Add(k.ToString());
                        so["keyMap"] = km;
                    }
                    stagesArr.Add(so);
                }
                root["stages"] = stagesArr;
            }

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>把音符列表序列化为 milestone-1 notes 数组（根与 parts 共用）。</summary>
        static JsonArray SerializeNotes(List<Note> notes)
        {
            var arr = new JsonArray();
            foreach (var n in notes)
            {
                if (n == null) continue;
                var o = new JsonObject
                {
                    ["t"] = J(n.Time),
                    ["type"] = string.IsNullOrEmpty(n.Type) ? "tap" : n.Type
                };
                if (n.Col != 0) o["c"] = n.Col;
                if (n.X != 0.5) o["x"] = J(n.X);
                if (n.Y != 0.5) o["y"] = J(n.Y);
                if (n.End != n.Time) o["e"] = J(n.End);
                if (n.EndX != n.X) o["ex"] = J(n.EndX);
                if (n.EndY != n.Y) o["ey"] = J(n.EndY);
                if (n.EndCol != -1) o["ec"] = n.EndCol;
                if (n.Kind != 0) o["kind"] = n.Kind;
                if (n.Line != 0) o["line"] = n.Line;
                if (n.Decor) o["dec"] = true;
                if (n.SliderType != '\0') o["st"] = n.SliderType.ToString();
                if (n.Repeats != 1) o["rp"] = n.Repeats;
                // RPE 音符编辑字段（D2r 复刻；除非默认值否则落盘）
                if (n.Side != 0) o["side"] = n.Side;
                if (Math.Abs(n.Width - 1.0) > 1e-9) o["width"] = n.Width;
                if (Math.Abs(n.Alpha - 1.0) > 1e-9) o["alpha"] = n.Alpha;
                if (Math.Abs(n.VisMs - 999999) > 1e-9 && n.VisMs > 0) o["vis"] = n.VisMs;
                if (n.Curve != null && n.Curve.Count > 0)
                {
                    // 曲线点数组（元素为对象 {x,y}；规避 .NET8 JsonArray 标量元素的 ToJsonString bug）
                    o["cv"] = PtArray(n.Curve, p => (J(p.X), J(p.Y), double.NaN));
                }
                if (n.Arc3 != null && n.Arc3.Count > 0)
                {
                    // Arcaea arc 3D 中间控制点：{x=轨, y=时间比例, z=天地间高度}
                    o["a3"] = PtArray(n.Arc3, p => (J(p.X), J(p.Y), J(p.Z)));
                }
                arr.Add(o);
            }
            return arr;
        }

        /// <summary>点对象序列化公共助手：元素为 {x,y[,z]}，坐标统一 "0.######" 精度；z=NaN 时省略。</summary>
        static JsonArray PtArray<T>(List<T> pts, Func<T, (double x, double y, double z)> sel)
        {
            var arr = new JsonArray();
            foreach (var p in pts)
            {
                var (x, y, z) = sel(p);
                var o = new JsonObject { ["x"] = x.ToString("0.######", CultureInfo.InvariantCulture), ["y"] = y.ToString("0.######", CultureInfo.InvariantCulture) };
                if (!double.IsNaN(z)) o["z"] = z.ToString("0.######", CultureInfo.InvariantCulture);
                arr.Add(o);
            }
            return arr;
        }

        /// <summary>把事件列表序列化为 milestone-1 events 数组（根与 parts 共用）。</summary>
        static JsonArray SerializeEvents(List<ChartEvent> events)
        {
            var arr = new JsonArray();
            foreach (var ev in events)
            {
                if (ev == null) continue;
                var o = new JsonObject
                {
                    ["t"] = J(ev.Time),
                    ["type"] = string.IsNullOrEmpty(ev.Type) ? "moveY" : ev.Type,
                    ["v"] = J(ev.Value)
                };
                if (!double.IsNaN(ev.End)) o["e"] = J(ev.End);         // 缺 e = 瞬间事件
                if (ev.EndValue != 0 && !double.IsNaN(ev.EndValue)) o["ev"] = J(ev.EndValue);
                // QA-4：Line>=0 才写（Phigros 线 1=0 不能当缺省；缺省/全局 = -1）
                if (ev.Line >= 0) o["line"] = ev.Line;
                if (!string.IsNullOrEmpty(ev.Ease)) o["ease"] = ev.Ease; // 缓动曲线（非线性动画）
                // 绑定组（T59 D2j）：非 0 时落盘
                if (ev.Group != 0) o["group"] = ev.Group;
                // P0 数据保真：Next + Bezier 落盘（此前 Edit→Save→Reload 丢贝塞尔/next）
                if (ev.Next) o["next"] = true;
                if (ev.Bezier != null && ev.Bezier.Length >= 8)
                {
                    // 控制点对象数组（规避 .NET8 JsonArray 标量元素的 ToJsonString bug，与 PtArray 同法）
                    var bz = new JsonArray();
                    for (int i = 0; i < ev.Bezier.Length; i += 2)
                        bz.Add(new JsonObject
                        {
                            ["x"] = ev.Bezier[i].ToString("0.######", CultureInfo.InvariantCulture),
                            ["y"] = ev.Bezier[i + 1].ToString("0.######", CultureInfo.InvariantCulture)
                        });
                    o["bezier"] = bz;
                }
                arr.Add(o);
            }
            return arr;
        }

        /// <summary>D2o：判定线元数据数组序列化（name/group/z/cover；根与 parts 共用）。</summary>
        static JsonArray SerializeLineMeta(List<PhigrosLineMeta> metas)
        {
            var arr = new JsonArray();
            foreach (var m in metas)
            {
                if (m == null) continue;
                var o = new JsonObject
                {
                    ["name"] = m.Name ?? "",
                    ["group"] = m.Group,
                    ["z"] = m.Z
                };
                if (m.Cover) o["cover"] = true;
                arr.Add(o);
            }
            return arr;
        }

        /* ================= osu! 专用模式解析（standard / taiko / catch） ================= */

        /// <summary>拆 osu! 文本为 [段] → 行列表（与 ParseOsu 一致）。</summary>
        static Dictionary<string, List<string>> ParseOsuSections(string text)
        {
            var sections = new Dictionary<string, List<string>>();
            string sec = "";
            foreach (var raw in text.Split('\n'))
            {
                var s = raw.Trim();
                if (s.Length == 0 || s.StartsWith("//")) continue;
                if (s.StartsWith("[") && s.EndsWith("]")) { sec = s.Substring(1, s.Length - 2); sections[sec] = new List<string>(); continue; }
                if (sec.Length > 0) sections[sec].Add(s);
            }
            return sections;
        }

        /// <summary>解析 TimingPoints → (time, beatLength) 列表（按时间升序）。</summary>
        static List<(double time, double beat)> ParseOsuTimingPoints(Dictionary<string, List<string>> sections)
        {
            var tps = new List<(double time, double beat)>();
            if (sections.TryGetValue("TimingPoints", out var tpl))
                foreach (var l in tpl)
                {
                    var p = l.Split(',');
                    if (p.Length >= 2) tps.Add((D(p[0], 0), D(p[1], 500)));
                }
            tps.Sort((a, b) => a.time.CompareTo(b.time));
            return tps;
        }

        /// <summary>t 时刻的节拍长度（毫秒/拍）：取 t 之前最近的非继承 timing point，默认 500。</summary>
        static double BeatLengthAt(List<(double time, double beat)> tps, double t)
        {
            double bl = 500;
            foreach (var tp in tps) if (tp.time <= t && tp.beat > 0) bl = tp.beat;
            return bl;
        }

        /// <summary>
        /// 解析滑条字段（与真实 .osu 一致）：p[5]="曲线类型|控制点"（竖线分隔），p[6]=slides，p[7]=length。
        /// 返回 (曲线类型, 往返次数, 长度, 控制点列表)。
        /// </summary>
        static (char CurveType, int Slides, double Length, string[] Points) ParseOsuSlider(string[] p)
        {
            var cp = (p.Length > 5 ? p[5] : "").Split('|');
            char curveType = cp.Length > 0 && cp[0].Length > 0 ? char.ToUpperInvariant(cp[0][0]) : 'L';
            int slides = p.Length > 6 ? I(p[6], 1) : 1;
            if (slides < 1) slides = 1;
            double length = p.Length > 7 ? D(p[7], 0) : 0;
            var points = cp.Length > 1 ? cp.Skip(1).ToArray() : new string[0];
            return (curveType, slides, length, points);
        }

        /// <summary>滑条持续时间：length / (SliderMultiplier×100) × beatLength(time) × slides。</summary>
        static double OsuSliderDuration(string[] p, double sliderMult, List<(double time, double beat)> tps, double time)
        {
            var (_, slides, length, _) = ParseOsuSlider(p);
            if (length <= 0) return 0;
            return (length / (sliderMult * 100.0)) * BeatLengthAt(tps, time) * slides;
        }

        /// <summary>滑条控制点（含起点，全部归一化 /512,/384）。终点为最后一个控制点。</summary>
        static List<(double X, double Y)> OsuSliderCurve(string[] p, double x, double y)
        {
            var (_, _, _, points) = ParseOsuSlider(p);
            var curve = new List<(double X, double Y)> { (x / 512.0, y / 384.0) };
            foreach (var ptStr in points)
            {
                var pt = ptStr.Split(':');
                double px = pt.Length >= 1 ? D(pt[0], x) / 512.0 : x / 512.0;
                double py = pt.Length >= 2 ? D(pt[1], y) / 384.0 : y / 384.0;
                curve.Add((px, py));
            }
            return curve;
        }

        /// <summary>解析 osu! 公共头部（元数据 + Ar/Od/Dr/Bpm + TimingPoints + 滑条倍率），返回解析上下文。</summary>
        static (Chart Chart, List<(double time, double beat)> Tps, double SliderMult, List<string> HitObjects)
            ParseOsuBase(string text, string sourcePath, GameMode mode, string modeName, int keyCount)
        {
            var chart = new Chart { SourcePath = sourcePath, Mode = mode, ModeName = modeName, KeyCount = keyCount };
            var sections = ParseOsuSections(text);
            var gen = ParseKv(sections, "General");
            var diff = ParseKv(sections, "Difficulty");
            var meta = ParseKv(sections, "Metadata");
            chart.Title = meta.GetValueOrDefault("TitleUnicode") ?? meta.GetValueOrDefault("Title") ?? Path.GetFileNameWithoutExtension(sourcePath);
            chart.Artist = meta.GetValueOrDefault("ArtistUnicode") ?? meta.GetValueOrDefault("Artist") ?? "";
            chart.Version = meta.GetValueOrDefault("Version") ?? "";
            chart.AudioFile = gen.GetValueOrDefault("AudioFilename") ?? "";
            chart.Od = D(diff.GetValueOrDefault("OverallDifficulty"), 8);
            chart.Dr = D(diff.GetValueOrDefault("HPDrainRate"), 8);
            chart.Ar = D(diff.GetValueOrDefault("ApproachRate"), 5);
            chart.Cs = D(diff.GetValueOrDefault("CircleSize"), 4);
            chart.SliderTickRate = D(diff.GetValueOrDefault("SliderTickRate"), 1);   // D2e 修正：[Difficulty] 实际位置；tick 率 0.5~8
            var tps = ParseOsuTimingPoints(sections);
            chart.Bpm = diff.ContainsKey("BPM") ? D(diff["BPM"], 0) : (tps.Count > 0 && tps[0].beat > 0 ? 60000.0 / tps[0].beat : 0);
            double sliderMult = D(diff.GetValueOrDefault("SliderMultiplier"), 1.4);
            var hitObjects = sections.TryGetValue("HitObjects", out var ho) ? ho : new List<string>();
            return (chart, tps, sliderMult, hitObjects);
        }

        /// <summary>osu!standard（模式 0）：圆圈/滑条/转盘 → tap/hold/spin，KeyCount=1。</summary>
        public static Chart ParseOsuStandard(string text, string sourcePath)
        {
            var (chart, tps, sliderMult, hitObjects) = ParseOsuBase(text, sourcePath, GameMode.OsuStandard, "osu!standard", 1);
            foreach (var l in hitObjects)
            {
                var p = l.Split(',');
                if (p.Length < 4) continue;
                double x = D(p[0], 0), y = D(p[1], 0), time = D(p[2], 0);
                int type = (int)D(p[3], 0);
                if ((type & 1) != 0)   // 圆圈
                {
                    chart.Notes.Add(new Note { Time = time, Col = 0, X = x / 512.0, Y = y / 384.0, Type = "tap" });
                }
                else if ((type & 2) != 0)   // 滑条
                {
                    var (curveType, slides, _, _) = ParseOsuSlider(p);
                    double dur = OsuSliderDuration(p, sliderMult, tps, time);
                    var curve = OsuSliderCurve(p, x, y);
                    double endX = curve[curve.Count - 1].X, endY = curve[curve.Count - 1].Y;
                    chart.Notes.Add(new Note
                    {
                        Time = time, End = time + dur,
                        Col = 0, X = x / 512.0, Y = y / 384.0,
                        EndX = endX, EndY = endY,
                        Type = "hold", SliderType = curveType, Repeats = slides, Curve = curve
                    });
                }
                else if ((type & 8) != 0)   // 转盘
                {
                    double end = D(p.Length > 5 ? p[5] : "0", time);
                    if (end < time) end = time;
                    chart.Notes.Add(new Note { Time = time, End = end, Col = 0, X = 0.5, Y = 0.5, Type = "spin" });
                }
            }
            return Sanitize(chart);
        }

        /// <summary>已移除玩法（osu!taiko / osu!catch）轻桩：只取头部元数据与 Mode（不解析音符）。
        /// 玩法 Removed=true，谱面不会进入游玩/编辑器；保留 Mode/ModeName 供曲库按
        /// ModeSystem.FromId(...).Removed 过滤（否则会掉进 mania 解析被当成谱面入库）。</summary>
        public static Chart ParseOsuRemoved(string text, string sourcePath, int mode)
        {
            var (chart, tps, sliderMult, hitObjects) = ParseOsuBase(
                text, sourcePath,
                mode == 1 ? GameMode.Taiko : GameMode.Catch,
                mode == 1 ? "osu!taiko" : "osu!catch",
                mode == 1 ? 2 : 1);
            return Sanitize(chart);
        }

        /* ================= Routlock (.adofai JSON，原 ADOFAI 单轨模式更名保留) ================= */

        /// <summary>解析 Routlock(.adofai JSON) 谱面：单轨 tile，按角度/倍率推时间。（真实 ADOFAI 双轨见 ParseAdofaiReal）</summary>
        public static Chart ParseAdofai(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, Mode = GameMode.Adofai, ModeName = "Routlock", KeyCount = 1 };
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var settings = ObjAt(root, "settings");
                chart.Title = StrAt(settings, "title") ?? StrAt(root, "title") ?? Path.GetFileNameWithoutExtension(sourcePath);
                chart.Artist = StrAt(settings, "artist") ?? StrAt(root, "artist") ?? "";
                chart.Offset = NumAt(settings, "offset") ?? 0;

                int n = 0;
                var angles = new List<double>();
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("angleData", out var ad) && ad.ValueKind == JsonValueKind.Array)
                {
                    n = ad.GetArrayLength();
                    foreach (var v in ad.EnumerateArray())   // D23：angleData 每 tile 转角（度），写 Kind 供轮转
                        angles.Add(v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0);
                }

                // pathData：每 tile 一个节拍倍率（tile i → i+1 段），缺省 1
                var path = new double[n];
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("pathData", out var pd) && pd.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    foreach (var v in pd.EnumerateArray())
                    {
                        if (i >= n) break;
                        path[i] = v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 1;
                        if (path[i] <= 0) path[i] = 1;
                        i++;
                    }
                }
                for (int i = 0; i < n; i++) if (path[i] <= 0) path[i] = 1;

                // actions：SetSpeed(Bpm/Multiplier/Beats) / Twirl / Hold / Checkpoint
                var speeds = new List<(int floor, double bpmOrMult, int isMult, double angleOffset)>();
                var beatsSpeeds = new List<(int floor, double beats)>();   // D2c/T51：SetSpeed Beats 型（每 tile 拍数）——此前被静默丢弃
                var twirls = new List<int>();
                var holds = new HashSet<int>();
                var checkpoints = new List<int>();   // Checkpoint 事件（掉轨重开点，策划规格 §5）
                var angleOffsets = new List<(int floor, double deg)>();   // angleOffset 角度延迟（SetSpeed 携带）
                var hitsounds = new List<(int floor, string name, double vol)>();   // SetHitsound 事件
                var trackMoves = new List<(int floor, string type, double pos, double rot, double scale, string ease)>();   // MoveTrack/PositionTrack 轨道变换
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array)
                    foreach (var a in acts.EnumerateArray())
                    {
                        if (a.ValueKind != JsonValueKind.Object) continue;
                        string ev = StrAt(a, "eventType") ?? "";
                        int floor = (int)Math.Round(NumAt(a, "floor") ?? 0);
                        // SetSpeed：Bpm 型（绝对值）/ Multiplier 型（倍率，作用于当前 BPM）/ Beats 型（每 tile 拍数）；angleOffset 角度延迟
                        if (ev == "SetSpeed")
                        {
                            string st = StrAt(a, "speedType") ?? "Bpm";
                            double bpmVal = NumAt(a, "beatsPerMinute") ?? 0;
                            double mult = NumAt(a, "bpmMultiplier") ?? 1;
                            double beatVal = NumAt(a, "beats") ?? 0;
                            double ao = NumAt(a, "angleOffset") ?? 0;
                            if (string.Equals(st, "Bpm", StringComparison.OrdinalIgnoreCase) && bpmVal > 0)
                                speeds.Add((floor, bpmVal, 0, ao));   // 绝对值
                            else if (string.Equals(st, "Beats", StringComparison.OrdinalIgnoreCase) && beatVal > 0)
                                beatsSpeeds.Add((floor, beatVal));     // Beats 型：每 tile 拍数（BeatsAt 作用于段长，不改 Bpm）
                            else if (mult > 0)
                                speeds.Add((floor, mult, 1, ao));      // 倍率（暂存，用时乘）
                            if (ao != 0) angleOffsets.Add((floor, ao));
                        }
                        else if (ev == "Twirl") twirls.Add(floor);
                        else if (ev == "Hold") holds.Add(floor);
                        else if (ev == "Checkpoint") checkpoints.Add(floor);
                        else if (ev == "SetHitsound")
                        {
                            // 音效事件：存 Chart.Events（type=hitsound, value=音效名索引），供游玩端音效映射
                            string hs = StrAt(a, "hitsound") ?? "";
                            double vol = NumAt(a, "volume") ?? 1;
                            if (hs.Length > 0) hitsounds.Add((floor, hs, vol));
                        }
                        else if (ev == "MoveTrack" || ev == "PositionTrack")
                        {
                            // 轨道变换事件：存 Chart.Events（type=movetrack/positiontrack, value=positionOffset, ease）
                            double pos = NumAt(a, "positionOffset") ?? 0;
                            double rot = NumAt(a, "rotation") ?? 0;
                            double sc = NumAt(a, "scale") ?? 1;
                            string ease = StrAt(a, "easing") ?? "Linear";
                            trackMoves.Add((floor, ev == "MoveTrack" ? "movetrack" : "positiontrack", pos, rot, sc, ease));
                        }
                        // MultiPlanet 等其它事件忽略（按 tap 处理该 tile）
                    }
                speeds.Sort((x, y) => x.floor.CompareTo(y.floor));
                beatsSpeeds.Sort((x, y) => x.floor.CompareTo(y.floor));
                twirls.Sort();

                // 初始 BPM：settings.bpm 优先，其次第一个 SetSpeed，缺省 120
                double initBpm = NumAt(settings, "bpm") ?? 120;
                if (initBpm <= 0) initBpm = 120;
                chart.Bpm = initBpm;

                // BpmAt：绝对值直取；Multiplier 倍率相对初始 BPM 累乘（实机：倍率作用于当前 BPM，简化按初始 BPM 连续乘）
                double BpmAt(int floor)
                {
                    double bpm = initBpm;
                    foreach (var s in speeds)
                    {
                        if (s.floor > floor) break;
                        if (s.isMult == 1) bpm = initBpm * s.bpmOrMult;
                        else if (s.bpmOrMult > 0) bpm = s.bpmOrMult;
                    }
                    return bpm > 0 ? bpm : 120;
                }

                // BeatsAt：SetSpeed Beats 型（每 tile 拍数）；无 Beats 事件=1（T51：Beats 型仅作用于 tile 段长，不改 Bpm）
                double BeatsAt(int floor)
                {
                    double b = 1;
                    foreach (var s in beatsSpeeds)
                    {
                        if (s.floor > floor) break;
                        b = s.beats;
                    }
                    return b > 0 ? b : 1;
                }

                // 各 tile 时间：tile0=0ms；tile i→i+1 时长 = 60000/bpm(i) × path[i] × Beats(i)
                var times = new double[n];
                for (int i = 0; i + 1 < n; i++)
                    times[i + 1] = times[i] + 60000.0 / BpmAt(i) * path[i] * BeatsAt(i);

                // Twirl 之间的 tile 自动（不生成 Note）
                var auto = new bool[n];
                for (int k = 0; k + 1 < twirls.Count; k++)
                    for (int f = twirls[k] + 1; f < twirls[k + 1] && f < n; f++)
                        auto[f] = true;

                for (int i = 0; i < n; i++)
                {
                    if (auto[i]) continue;
                    int kind = i < angles.Count ? (int)Math.Round(angles[i]) : 0;   // D23：角度写 Kind
                    // angleOffset：SetSpeed 携带的角度延迟叠加到该 tile 的转角（实机语义：到达延迟表现为角度偏移）
                    foreach (var ao in angleOffsets) if (ao.floor == i) kind += (int)Math.Round(ao.deg);
                    // 音符时刻当前 BPM（变速段判定换算用，策划复核 P0-2）
                    double noteBpm = BpmAt(i);
                    if (holds.Contains(i))
                    {
                        double end = i + 1 < n ? times[i + 1] : times[i] + 600;
                        chart.Notes.Add(new Note { Time = times[i], End = Math.Max(end, times[i] + 1), Col = 0, Type = "hold", Kind = kind, Bpm = noteBpm });
                    }
                    else
                    {
                        chart.Notes.Add(new Note { Time = times[i], End = times[i], Col = 0, Type = "tap", Kind = kind, Bpm = noteBpm });
                    }
                }

                // Checkpoint 事件 → Chart.Events（掉轨重开点；floor → 时刻）
                foreach (var cp in checkpoints)
                    if (cp >= 0 && cp < times.Length)
                        chart.Events.Add(new ChartEvent { Time = times[cp], Type = "checkpoint", Value = 0 });

                // Twirl 事件 → Chart.Events（渲染 Twirl 区间提示色）
                foreach (var tw in twirls)
                    if (tw >= 0 && tw < times.Length)
                        chart.Events.Add(new ChartEvent { Time = times[tw], Type = "twirl", Value = 0 });

                // SetHitsound / MoveTrack / PositionTrack → Chart.Events（游玩端按需消费）
                foreach (var hs in hitsounds)
                    if (hs.floor >= 0 && hs.floor < times.Length)
                        chart.Events.Add(new ChartEvent { Time = times[hs.floor], Type = "hitsound", Value = hs.vol, End = hs.name.GetHashCode() % 1000 });
                foreach (var tm in trackMoves)
                    if (tm.floor >= 0 && tm.floor < times.Length)
                        chart.Events.Add(new ChartEvent
                        {
                            Time = times[tm.floor],
                            Type = tm.type,
                            Value = tm.pos,
                            EndValue = tm.rot,
                            Line = (int)tm.scale,
                            Ease = tm.ease
                        });

                return Sanitize(chart);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Routlock(原 ADOFAI) 谱面解析失败：" + ex.Message, ex);
            }
        }

        /* ================= 段位检测 ================= */

        /// <summary>
        /// 段位（Dan）检测，仅对 Mania 谱面生效：
        /// Version 含 "Dan" 或匹配 Malody 段位命名（Regular-N / Extra-N vN / exN）→ DanName=Version；
        /// 否则父目录名（或再上一层目录名）含 "Dan" → DanName=Version(或文件名)。
        /// DanSet 优先取歌名（含 Dan 字样），其次取目录名。
        /// </summary>
        static void DanDetect(Chart c, string sourcePath)
        {
            if (c.Mode != GameMode.Mania) return;
            var ver = c.Version ?? "";
            var title = c.Title ?? "";
            bool verDan = ver.IndexOf("Dan", StringComparison.OrdinalIgnoreCase) >= 0
                          || Regex.IsMatch(ver, @"^(Regular|Extra)\s*[-_]?\s*\d", RegexOptions.IgnoreCase)
                          || Regex.IsMatch(ver, @"^ex\s*\d", RegexOptions.IgnoreCase);
            bool titleDan = title.IndexOf("Dan", StringComparison.OrdinalIgnoreCase) >= 0;

            string DanSetOf() => titleDan && title.Length > 0 ? title : ParentDirName(sourcePath);

            if (verDan && ver.Length > 0)
            {
                c.DanName = ver;
                c.DanSet = DanSetOf();
                return;
            }
            var p1 = ParentDirName(sourcePath);
            if (p1.IndexOf("Dan", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                c.DanName = string.IsNullOrEmpty(ver) ? Path.GetFileNameWithoutExtension(sourcePath) : ver;
                c.DanSet = titleDan && title.Length > 0 ? title : p1;
                return;
            }
            var p2 = ParentDirName(Path.GetDirectoryName(sourcePath));
            if (p2.IndexOf("Dan", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                c.DanName = string.IsNullOrEmpty(ver) ? Path.GetFileNameWithoutExtension(sourcePath) : ver;
                c.DanSet = titleDan && title.Length > 0 ? title : p2;
            }
        }

        static string ParentDirName(string path)
        {
            var dir = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(dir) ? "" : Path.GetFileName(dir);
        }

        /* ================= maimai（simai / maidata.txt）================= */

        /// <summary>
        /// simai 文本谱（maidata.txt）解析：支持 BPM/measure 控制 token、
        /// 1~8 外圈 tap/hold/slide、A~F 中央 touch、b break、x EX、f 焰火、
        /// 连符 %、和弦 /、时长 [n:m]/[#秒]/[wait##slide]。
        /// 参考 research_tmp\MaiLib（MaiLib SimaiParser 语义：每小节 4 拍、tick 精度 384、
        /// 每个逗号 token 推进 1/quaver 小节，BPM/measure 控制 token 不占位）。
        /// </summary>
        public static Chart ParseMaimai(string text, string sourcePath)
        {
            var chart = new Chart { Mode = GameMode.Maimai, KeyCount = 8, ModeName = "maimai" };
            chart.Title = Path.GetFileNameWithoutExtension(sourcePath);
            double firstMs = 0;
            string chartBody = null;
            int bestLv = -1;
            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("&title=")) chart.Title = line.Substring(7).Trim();
                else if (line.StartsWith("&artist=")) chart.Artist = line.Substring(8).Trim();
                else if (line.StartsWith("&bpm=")) { if (double.TryParse(line.Substring(5).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) chart.Bpm = b; }
                else if (line.StartsWith("&first=")) { if (double.TryParse(line.Substring(7).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) firstMs = f; }
                else if (line.StartsWith("&inote_"))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 7 && int.TryParse(line.Substring(7, eq - 7), out var n))
                    {
                        var body = line.Substring(eq + 1).Trim();
                        if (body.Length > 0 && n > bestLv) { bestLv = n; chartBody = body; }
                    }
                }
            }
            if (chart.Bpm <= 0) chart.Bpm = 120;
            if (string.IsNullOrEmpty(chartBody))
                throw new InvalidDataException("maimai 谱面缺少 &inote_N= 音符数据行");
            chart.Version = bestLv >= 0 ? "LV." + bestLv : "";
            if (chart.Title.Length == 0 && bestLv >= 0) chart.Title = Path.GetFileNameWithoutExtension(sourcePath);

            double currentBpm = chart.Bpm;
            double timeInBar = 0;                // 小节数（浮点）
            double timeStep = 1.0 / 4;           // 默认 4/4 → 每 token 1/4 小节
            var notes = new List<Note>();
            var pendingBpmChanges = new List<(double ms, double bpm)>();   // 变速点（供后续精确换算）

            // 先切 token，遇控制 token 即时更新 bpm/quaver；音符 token 按当前状态算绝对时间。
            var tokens = chartBody.Split(',');
            foreach (var tokRaw in tokens)
            {
                var tok = tokRaw.Trim();
                if (tok.Length == 0) { timeInBar += timeStep; continue; }

                // 控制 token：(bpm) 与 {quaver} 可混在音符 token 前（如 "(120){4}1"）
                var segments = SplitControlAndNotes(tok, out double tokenBpmDelta, out int tokenQuaverDelta);
                if (tokenBpmDelta > 0) { currentBpm = tokenBpmDelta; pendingBpmChanges.Add((BarToMs(timeInBar, chart.Bpm, pendingBpmChanges), currentBpm)); }
                if (tokenQuaverDelta > 0) timeStep = 1.0 / tokenQuaverDelta;
                if (segments.Count == 0) { timeInBar += timeStep; continue; }

                double noteMs = BarToMs(timeInBar, chart.Bpm, pendingBpmChanges) + firstMs;
                foreach (var seg in segments)
                {
                    // 和弦：同一 tick 多个音符用 / 分隔
                    foreach (var single in seg.Split('/'))
                        ParseMaimaiNote(single.Trim(), noteMs, currentBpm, chart, notes);
                }
                timeInBar += timeStep;
            }
            chart.Notes = notes;
            return chart;
        }

        /// <summary>把 token 拆成 [音符段...]，同时提取其中 (bpm) 与 {quaver} 控制。</summary>
        static List<string> SplitControlAndNotes(string tok, out double bpmDelta, out int quaverDelta)
        {
            bpmDelta = 0; quaverDelta = 0;
            var segs = new List<string>();
            int i = 0, n = tok.Length;
            var cur = new System.Text.StringBuilder();
            while (i < n)
            {
                char c = tok[i];
                if (c == '(')
                {
                    int j = tok.IndexOf(')', i);
                    if (j > i && double.TryParse(tok.Substring(i + 1, j - i - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) bpmDelta = b;
                    i = j + 1;
                }
                else if (c == '{')
                {
                    int j = tok.IndexOf('}', i);
                    if (j > i && int.TryParse(tok.Substring(i + 1, j - i - 1), out var q)) quaverDelta = q;
                    i = j + 1;
                }
                else { cur.Append(c); i++; }
            }
            if (cur.Length > 0) segs.Add(cur.ToString());
            return segs;
        }

        /// <summary>当前 BPM（含变速点）下，把"小节数"换算为毫秒（分段累计，精确）。</summary>
        static double BarToMs(double bar, double baseBpm, List<(double ms, double bpm)> changes)
        {
            double bpm = baseBpm;
            double accMs = 0, lastBar = 0;
            foreach (var (ms, b) in changes)
            {
                double barAt = lastBar + (ms - accMs) * bpm / 240000.0;   // 该变速点所在小节
                if (bar <= barAt) break;
                accMs += (barAt - lastBar) * 240000.0 / bpm;
                lastBar = barAt; bpm = b;
            }
            return accMs + (bar - lastBar) * 240000.0 / bpm;   // 每小节 4 拍
        }

        /// <summary>解析单个 simai 音符段（可含 slide 连写 / hold / break / ex / touch）。</summary>
        static void ParseMaimaiNote(string token, double tMs, double bpm, Chart chart, List<Note> notes)
        {
            if (token.Length == 0) return;
            // 时长后缀 [..]（slide 用 [wait##slide] 或 [n:m]/[#秒]，hold 同）
            double durMs = 0, waitMs = 0;
            int br = token.IndexOf('[');
            string body = token;
            if (br >= 0)
            {
                int cl = token.IndexOf(']', br);
                if (cl > br)
                {
                    var (w, l) = ParseMaimaiDuration2(token.Substring(br + 1, cl - br - 1), bpm);
                    durMs = l; waitMs = w;
                    body = token.Substring(0, br) + token.Substring(cl + 1);
                }
            }
            // % 连符：标记（渲染可紧贴前一音符；时间上已按 token 推进）
            body = body.Replace("%", "");

            // 中央 touch：A~F（含 Ah 长条 touch、Ab break touch；C/C1/C2 等价）
            if (body.Length >= 1 && body[0] >= 'A' && body[0] <= 'F')
            {
                bool isHold = body.IndexOf('h') >= 0;
                bool isBreak = body.IndexOf('b') >= 0;
                int col = 9 + (body[0] - 'A');
                notes.Add(new Note { Time = tMs, End = isHold ? tMs + durMs : tMs, Col = col, Type = isBreak ? "break" : "touch", Kind = isHold ? 2 : 0 });
                return;
            }

            // 外圈音符：先剥 break/ex/焰火 修饰（顺序任意）
            bool brk = body.IndexOf('b') >= 0;
            bool ex = body.IndexOf('x') >= 0;
            body = body.Replace("b", "").Replace("x", "").Replace("f", "");

            // slide 检测：含 - > < ^ v p q s z V w 且结构为 键+类型+键(+键…)
            if (TryParseMaimaiSlide(body, out var slideStart, out var slideEnd, out var midKeys, out var slideType))
            {
                double endMs = tMs + waitMs + durMs;
                var n = new Note { Time = tMs, End = endMs > tMs + 50 ? endMs : tMs + 500, Col = slideStart, EndCol = slideEnd, Type = brk ? "break" : "slide", Kind = ex ? 1 : 0 };
                if (midKeys != null && midKeys.Count > 0)
                    n.Curve = midKeys.Select(k => ((double)k, 0.0)).ToList();
                notes.Add(n);
                return;
            }

            // tap / hold：扫描式切分（增强 MaiLib——真实谱面大量使用带修饰符的同位多音符）：
            //   "123" → 3 个 tap；"1h[2:1]2h[2:1]" → 2 个 hold（时长共享）；"1b2b" → 2 个 break。
            //   切分规则：遇到数字 1~8 且当前候选非空时开新段；方括号时长已在前面剥离，此处不会误切。
            var singles = new List<string>();
            var cur = new System.Text.StringBuilder();
            foreach (char c in body)
            {
                if ((c >= '1' && c <= '8') && cur.Length > 0) { singles.Add(cur.ToString()); cur.Clear(); }
                cur.Append(c);
            }
            if (cur.Length > 0) singles.Add(cur.ToString());
            foreach (var single in singles)
            {
                if (single.Length == 0) continue;
                char first = single[0];
                if (first < '1' || first > '8') continue;   // 无法识别则跳过
                int key = first - '0';
                bool hold = single.IndexOf('h') >= 0;
                // hold 缺时长 = 伪 tap（官方隐含 [1280:1]，MaiLib 用 [384:0]=0 时长）
                double end = hold ? (durMs > 0 ? tMs + durMs : tMs) : tMs;
                notes.Add(new Note { Time = tMs, End = end, Col = key, Type = brk ? "break" : (hold ? "hold" : "tap"), Kind = ex ? 1 : 0 });
            }
        }

        /// <summary>解析 simai 时长串（官方语义）：[n:m]=m 个 n 分音符（tick=(384/n)*m）；[#秒]=绝对秒；
        /// [w##l]=等待 w 秒+滑行 l 秒；[BPM#n:m]=指定 BPM 下的小节制；无 ## 的 slide 默认等待一拍（四分音符）。
        /// 返回 (waitMs, lastMs)。hold 的 wait 恒为 0。</summary>
        static (double waitMs, double lastMs) ParseMaimaiDuration2(string s, double bpm)
        {
            s = s.Trim();
            if (s.Length == 0) return (0, 0);
            // [w##l] 或 [w##BPM#n:m]
            int hashIdx = s.IndexOf("##");
            if (hashIdx >= 0)
            {
                string w = s.Substring(0, hashIdx), l = s.Substring(hashIdx + 2);
                double waitMs = ParseMaimaiSec(w, bpm);
                double lastMs;
                int bHash = l.IndexOf('#');
                if (bHash >= 0 && bHash == l.IndexOf('#'))
                {
                    // BPM#n:m 或 BPM#s
                    string bp = l.Substring(0, bHash), rest = l.Substring(bHash + 1);
                    double bpm2 = double.TryParse(bp, NumberStyles.Float, CultureInfo.InvariantCulture, out var bb) && bb > 0 ? bb : bpm;
                    lastMs = rest.Contains(":") ? ParseMaimaiTicks(rest, bpm2) : ParseMaimaiSec(rest, bpm2);
                }
                else lastMs = ParseMaimaiSec(l, bpm);
                return (waitMs, lastMs);
            }
            // 带 BPM 前缀：[BPM#n:m] / [BPM#s]（官方 [160#2]）
            int bIdx = s.IndexOf('#');
            if (bIdx > 0 && bIdx < s.Length - 1)
            {
                string bp = s.Substring(0, bIdx), rest = s.Substring(bIdx + 1);
                double bpm2 = double.TryParse(bp, NumberStyles.Float, CultureInfo.InvariantCulture, out var bb) && bb > 0 ? bb : bpm;
                if (rest.Contains(":")) return (60000.0 / bpm2, ParseMaimaiTicks(rest, bpm2));
                return (60000.0 / bpm2, ParseMaimaiSec(rest, bpm2));   // [BPM#s]：等待=该 BPM 一拍，滑行=s 秒
            }
            if (s.StartsWith("#"))
                return (60000.0 / bpm, ParseMaimaiSec(s.Substring(1), bpm));   // [#秒]：等待一拍+滑行秒
            if (s.Contains(":"))
                return (60000.0 / bpm, ParseMaimaiTicks(s, bpm));   // [n:m]：等待一拍+滑行 n:m
            return (60000.0 / bpm, ParseMaimaiTicks(s, bpm));       // 纯数字=小节数（兼容）
        }

        /// <summary>小节制 [n:m] → 毫秒（tick=(384/n)*m，每 tick=60/bpm/96 秒）。</summary>
        static double ParseMaimaiTicks(string s, double bpm)
        {
            int colon = s.IndexOf(':');
            double n, m;
            if (colon > 0 && double.TryParse(s.Substring(0, colon).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n)
                && double.TryParse(s.Substring(colon + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out m) && n > 0)
                return (384.0 / n) * m * 60000.0 / bpm / 96.0;   // tick → ms
            return 0;
        }

        /// <summary>秒/纯数字 → 毫秒（纯数字按"小节数"兼容处理）。</summary>
        static double ParseMaimaiSec(string s, double bpm)
        {
            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v * 1000.0;
            return 0;
        }

        /// <summary>slide 解析：键 + 类型(+ 键)*。类型含 - > < ^ v p q s z pp qq V w。</summary>
        static bool TryParseMaimaiSlide(string body, out int start, out int end, out List<int> midKeys, out string slideType)
        {
            start = end = -1; midKeys = null; slideType = "";
            if (body.Length < 3) return false;
            char c0 = body[0];
            if (c0 < '1' || c0 > '8') return false;
            start = c0 - '0';
            // 找 slide 类型符
            int ti = -1;
            string[] types = { "pp", "qq", "-", ">", "<", "^", "v", "p", "q", "s", "z", "V", "w" };
            foreach (var ty in types)
            {
                int idx = body.IndexOf(ty, 1);
                if (idx >= 0) { ti = idx; slideType = ty; break; }
            }
            if (ti < 0) return false;
            // 起点键之后是类型符，之后跟终点键序列（可连写 1-2-3-4）
            string rest = body.Substring(ti + slideType.Length);
            if (rest.Length == 0) return false;
            var keys = new List<int>();
            foreach (var ch in rest)
                if (ch >= '1' && ch <= '8') keys.Add(ch - '0');
            if (keys.Count == 0) return false;
            end = keys[keys.Count - 1];
            if (keys.Count > 1) midKeys = keys.Take(keys.Count - 1).ToList();
            return true;
        }
    }
}
