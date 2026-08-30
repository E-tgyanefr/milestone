using System; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Linq; using System.Text.Json; using System.Text.RegularExpressions; namespace ChartPlayer { public static partial class ChartParser { public static readonly string[] ChartExts = { ".osu", ".mc", ".sm", ".ssc", ".qua", ".mil", ".aff", ".txt", ".json" };  public static readonly string[] ChartZipExts = { ".mcz", ".osz", ".zip" }; public static Chart ParseFile(string path) { var text = File.ReadAllText(path); return ParseText(text, path); }  public static Chart ParseText(string text, string sourcePath) { var ext = Path.GetExtension(sourcePath).ToLowerInvariant();  switch (ext) { case ".mil": return ParseMil(text, sourcePath); case ".mc": return ManiaChart(ParseMc(text, sourcePath), sourcePath); case ".osu": return ManiaChart(ParseOsu(text, sourcePath), sourcePath); case ".sm": case ".ssc": return ManiaChart(ParseSmText(text, sourcePath, ext), sourcePath); case ".qua": return ManiaChart(ParseQua(text, sourcePath), sourcePath); case ".ma2": case ".mai": case ".sus": case ".c2s": case ".ksh": case ".rtchart": case ".dy": case ".tone": throw new InvalidDataException("已移除玩法的谱面格式：" + ext); case ".aff": return ParseArcaea(text, sourcePath); case ".txt": return ParseTxtSniff(text, sourcePath); case ".json": return ParseJsonSniff(text, sourcePath); case ".adofai": return ParseAdofai(text, sourcePath); }  var t = text.TrimStart(); if (t.StartsWith("{")) { if (t.Contains("\"HitObjects\"") && t.Contains("\"TimingPoints\"")) return ManiaChart(ParseQua(text, sourcePath), sourcePath); if (t.Contains("\"judgeLineList\"") || t.Contains("\"judgeLineGroup\"")) return ParsePhigros(text, sourcePath); if (IsMilestone(text)) return ParseMil(text, sourcePath); return ManiaChart(ParseMc(text, sourcePath), sourcePath); } if (t.Contains("VERSION 2")) return ParseCytus(text, sourcePath); if (Regex.IsMatch(t, @"#TITLE\s*:")) return ManiaChart(ParseSmText(text, sourcePath, ""), sourcePath);
            if (t.Contains("AudioOffset") || t.Contains("timing(")) return ParseArcaea(text, sourcePath);
            return ManiaChart(ParseOsu(text, sourcePath), sourcePath);
        }

        /* ---------- osu! ---------- */
        public static Chart ParseOsu(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, ModeName = "osu!mania" };
            var sections = ParseOsuSections(text);
            var gen = ParseKv(sections, "General");
            var diff = ParseKv(sections, "Difficulty");
            var meta = ParseKv(sections, "Metadata");
            int mode = I(gen.GetValueOrDefault("Mode"), 0);
            // osu! 模式分发：0=standard 走专用解析器；1=taiko / 2=catch 为已移除玩法 → 轻桩（只取头部元数据，不解析音符，
            // 否则旧 taiko/catch .osu 掉进 mania 解析被当成谱面入库）。ModeName 保留原玩法标识供曲库过滤。
            if (mode == 0) return ParseOsuStandard(text, sourcePath);
            if (mode == 1 || mode == 2) return ParseOsuRemoved(text, sourcePath, mode);
            chart.Title = meta.GetValueOrDefault("TitleUnicode") ?? meta.GetValueOrDefault("Title") ?? Path.GetFileNameWithoutExtension(sourcePath);
            chart.Artist = meta.GetValueOrDefault("ArtistUnicode") ?? meta.GetValueOrDefault("Artist") ?? "";
            chart.Version = meta.GetValueOrDefault("Version") ?? "";
            chart.AudioFile = gen.GetValueOrDefault("AudioFilename") ?? "";
            chart.KeyCount = mode == 3 ? Math.Max(1, (int)Math.Round(D(diff.GetValueOrDefault("CircleSize"), 4))) : 4;
            if (chart.KeyCount < 1) chart.KeyCount = 4;
            chart.Od = D(diff.GetValueOrDefault("OverallDifficulty"), 8);   // osu! OD（判定窗口）
            chart.Dr = D(diff.GetValueOrDefault("HPDrainRate"), 8);          // osu! HP 扣血率
            chart.Ar = D(diff.GetValueOrDefault("ApproachRate"), 5);          // osu!standard AR（圆圈收缩速度）
            chart.SliderTickRate = D(diff.GetValueOrDefault("SliderTickRate"), 1);  // osu!standard 滑条 tick 率（D2e 修正：[Difficulty] 实际位置，General 无此键）

            var tps = ParseOsuTimingPoints(sections);
            chart.Bpm = diff.ContainsKey("BPM") ? D(diff["BPM"], 0) : (tps.Count > 0 ? 60000.0 / tps[0].beat : 0);

            if (sections.TryGetValue("HitObjects", out var ho))
                foreach (var l in ho)
                {
                    var p = l.Split(',');
                    if (p.Length < 4) continue;
                    double x = D(p[0], 0), time = D(p[2], 0);
                    int type = (int)D(p[3], 0);
                    int col; double end = time; string ntype = "tap";
                    if (mode == 3)
                    {
                        col = Math.Max(0, Math.Min(chart.KeyCount - 1, (int)Math.Floor(x * chart.KeyCount / 512.0)));
                        if ((type & 128) != 0 || (type & 8) != 0) { end = D(p.Length > 5 ? p[5] : "0", time); if (end < time) end = time; ntype = "hold"; }
                    }
                    else
                    {
                        col = Math.Max(0, Math.Min(chart.KeyCount - 1, (int)Math.Floor(x / 512.0 * chart.KeyCount)));
                        if ((type & 1) != 0) ntype = "tap";
                        else if ((type & 128) != 0 || (type & 8) != 0) { end = D(p.Length > 5 ? p[5] : "0", time); if (end < time) end = time; ntype = "hold"; }
                        else if ((type & 2) != 0)
                        {
                            var par = (p.Length > 5 ? p[5] : "").Split('|');
                            int slides = (int)(p.Length > 6 ? D(p[6], 1) : 1); if (slides < 1) slides = 1;
                            double length = par.Length > 0 ? D(par[par.Length - 1], 0) : 0;
                            double sm = D(diff.GetValueOrDefault("SliderMultiplier"), 1.4);
                            double dur = length > 0 ? (length / (sm * 100.0)) * BeatLengthAt(tps, time) * slides : 0;
                            end = time + dur; ntype = dur > 20 ? "hold" : "tap";
                        }
                        else continue;
                    }
                    chart.Notes.Add(new Note { Time = time, End = end, Col = col, Type = ntype });
                }
            return Sanitize(chart);
        }

        /* ---------- Malody ---------- */
        public static Chart ParseMc(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, ModeName = "Malody" };
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var meta = root.TryGetProperty("meta", out var m) ? m : default;
            var song = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("song", out var so) ? so : default;
            chart.Title = song.ValueKind == JsonValueKind.Object && song.TryGetProperty("title", out var t1) ? t1.GetString()
                        : (meta.TryGetProperty("title", out var t2) ? t2.GetString() : Path.GetFileNameWithoutExtension(sourcePath));
            chart.Artist = song.ValueKind == JsonValueKind.Object && song.TryGetProperty("artist", out var a1) ? a1.GetString() : "";
            chart.Version = meta.TryGetProperty("version", out var v) ? v.GetString() : "";
            chart.AudioFile = song.ValueKind == JsonValueKind.Object && song.TryGetProperty("file", out var af) ? af.GetString() : "";
            int kcFromMeta = 0;
            if (meta.TryGetProperty("mode_ext", out var me))
            {
                // mode_ext 兼容两种形式：数字（老版）与对象 { "column": N } / { "key": N }（新版 Malody）
                if (me.ValueKind == JsonValueKind.Number) kcFromMeta = me.GetInt32();
                else if (me.ValueKind == JsonValueKind.Object)
                {
                    if (me.TryGetProperty("column", out var col) && col.ValueKind == JsonValueKind.Number) kcFromMeta = col.GetInt32();
                    else if (me.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Number) kcFromMeta = key.GetInt32();
                }
                kcFromMeta = Math.Max(1, Math.Min(16, kcFromMeta));
            }
            chart.KeyCount = kcFromMeta > 0 ? kcFromMeta : 16;   // 无 mode_ext 时先用 16 上限，解析后按实际列数修正
            double offset = meta.TryGetProperty("offset", out var os) ? os.GetDouble() : 0;

            double beatOf(JsonElement b) { if (b.ValueKind != JsonValueKind.Array || b.GetArrayLength() < 3) return 0; return b[0].GetDouble() + b[1].GetDouble() / b[2].GetDouble(); }
            var bpmList = new List<(double beat, double bpm)>();
            if (root.TryGetProperty("time", out var timeArr))
                foreach (var tp in timeArr.EnumerateArray())
                    if (tp.TryGetProperty("bpm", out var bp) && bp.GetDouble() > 0)
                        bpmList.Add((beatOf(tp.TryGetProperty("beat", out var bb) ? bb : default), bp.GetDouble()));
            bpmList.Sort((x, y) => x.beat.CompareTo(y.beat));
            double ct = 0, cb = 0, cbpm = bpmList.Count > 0 ? bpmList[0].bpm : 120;
            var segs = new List<(double beat, double time, double bpm)>();
            foreach (var s in bpmList) { if (s.beat < cb - 1e-9) continue; ct += (s.beat - cb) * (60000.0 / cbpm); cb = s.beat; cbpm = s.bpm; segs.Add((s.beat, ct, cbpm)); }
            double timeAt(double db) { double t = 0, bb = 0, bp = segs.Count > 0 ? segs[0].bpm : 120; foreach (var s in segs) { if (db >= s.beat) { t = s.time; bb = s.beat; bp = s.bpm; } } return t + (db - bb) * (60000.0 / bp); }

            if (root.TryGetProperty("note", out var noteArr))
                foreach (var n in noteArr.EnumerateArray())
                {
                    double time = timeAt(beatOf(n.TryGetProperty("beat", out var nb) ? nb : default)) + offset;
                    int col = n.TryGetProperty("column", out var c) ? c.GetInt32() : 0;
                    col = Math.Max(0, Math.Min(chart.KeyCount - 1, col));
                    double end = time; string ntype = "tap";
                    if (n.TryGetProperty("endbeat", out var eb)) { end = timeAt(beatOf(eb)) + offset; if (end < time) end = time; ntype = "hold"; }
                    chart.Notes.Add(new Note { Time = time, End = end, Col = col, Type = ntype });
                }
            // 老版本 .mc 没有 mode_ext：按实际列数修正
            if (kcFromMeta <= 0)
            {
                int maxCol = 0;
                foreach (var n in chart.Notes) maxCol = Math.Max(maxCol, n.Col);
                chart.KeyCount = Math.Max(1, maxCol + 1);
            }
            chart.Bpm = bpmList.Count > 0 ? bpmList[0].bpm : 0;
            return Sanitize(chart);
        }

        /* ---------- SM / Etterna ---------- */
        public static Chart ParseSmText(string text, string sourcePath, string ext)
        {
            var chart = new Chart { SourcePath = sourcePath, ModeName = ext == ".ssc" ? "Etterna" : "SM" };
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var notesData = new List<string>();
            var re = new Regex(@"#([A-Z0-9]+)\s*:\s*([^;]*);", RegexOptions.IgnoreCase);
            foreach (Match m in re.Matches(text))
            {
                var key = m.Groups[1].Value.ToUpperInvariant();
                var val = m.Groups[2].Value;
                if (key == "NOTES") notesData.Add(val);
                else if (!meta.ContainsKey(key)) meta[key] = val;
            }
            chart.Title = meta.GetValueOrDefault("TITLE") ?? Path.GetFileNameWithoutExtension(sourcePath);
            chart.Artist = meta.GetValueOrDefault("ARTIST") ?? "";
            chart.AudioFile = meta.GetValueOrDefault("MUSIC") ?? "";

            var bpmMap = new List<(double beat, double bpm)>();
            foreach (var seg in (meta.GetValueOrDefault("BPMS") ?? "").Split(','))
            {
                var p = seg.Split('=');
                if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
                    && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    bpmMap.Add((b, v));
            }
            bpmMap.Sort((a, b2) => a.beat.CompareTo(b2.beat));
            double offset = double.TryParse(meta.GetValueOrDefault("OFFSET") ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var of) ? of : 0;
            double btm(double beat)
            {
                double t = 0, bb = 0, bp = bpmMap.Count > 0 ? bpmMap[0].bpm : 120;
                foreach (var s in bpmMap)
                {
                    if (beat >= s.beat) { t += (s.beat - bb) * (60000.0 / bp); bb = s.beat; bp = s.bpm; }
                    else break;
                }
                return t + (beat - bb) * (60000.0 / bp);
            }

            var notes = new List<Note>();
            int keyCount = 4;
            if (notesData.Count > 0)
            {
                var parts = notesData[0].Split(':');
                chart.Version = parts.Length > 4 ? parts[4] : "Normal";
                var data = string.Join(":", parts.Skip(5));
                var measures = data.Split(',').Where(s => s.Trim().Length > 0).ToList();
                int mi = 0;
                double?[] holdStart = new double?[16];
                foreach (var ms in measures)
                {
                    // 允许 0-4 与 M（地雷，忽略）：'2'=hold 头 '3'=roll 头 '4'=hold/roll 尾
                    var rows = ms.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length >= 2 && l.All(c => c == '0' || c == '1' || c == '2' || c == '3' || c == '4' || c == 'M'))
                        .ToList();
                    if (rows.Count == 0) { mi++; continue; }
                    keyCount = Math.Min(10, Math.Max(1, rows[0].Length));
                    int rc = rows.Count;
                    for (int ri = 0; ri < rc; ri++)
                    {
                        double beat = mi * 4 + ri * (4.0 / rc);
                        double t = btm(beat) + offset;
                        var row = rows[ri];
                        for (int c = 0; c < row.Length && c < 10; c++)
                        {
                            char ch = row[c];
                            if (ch == '1')
                                notes.Add(new Note { Time = t, End = t, Col = c, Type = "tap" });
                            else if (ch == '2' || ch == '3')
                                holdStart[c] = t;
                            else if (ch == '4' && holdStart[c].HasValue)
                            {
                                double hs = holdStart[c].Value;
                                notes.Add(new Note { Time = hs, End = Math.Max(t, hs + 1), Col = c, Type = "hold" });
                                holdStart[c] = null;
                            }
                        }
                    }
                    // 小节末尾未闭合的 hold 头：以小节末为尾
                    for (int c = 0; c < keyCount; c++)
                        if (holdStart[c].HasValue)
                        {
                            double hs = holdStart[c].Value;
                            double tail = btm((mi + 1) * 4) + offset;
                            notes.Add(new Note { Time = hs, End = Math.Max(tail, hs + 1), Col = c, Type = "hold" });
                            holdStart[c] = null;
                        }
                    mi++;
                }
            }
            notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            chart.KeyCount = keyCount;
            chart.Notes = notes;
            chart.Bpm = bpmMap.Count > 0 ? bpmMap[0].bpm : 0;
            return Sanitize(chart);
        }

        /* ---------- Quaver ---------- */
        public static Chart ParseQua(string text, string sourcePath)
        {
            var chart = new Chart { SourcePath = sourcePath, ModeName = "Quaver" };
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            chart.Title = root.TryGetProperty("Title", out var t) ? t.GetString() : Path.GetFileNameWithoutExtension(sourcePath);
            chart.Artist = root.TryGetProperty("Artist", out var a) ? a.GetString() : "";
            chart.Version = root.TryGetProperty("DifficultyName", out var d) ? d.GetString() : "";

            var notes = new List<Note>();
            if (root.TryGetProperty("HitObjects", out var ho))
                foreach (var h in ho.EnumerateArray())
                {
                    double time = h.TryGetProperty("StartTime", out var st) ? st.GetDouble() : 0;
                    double end = time;
                    if (h.TryGetProperty("EndTime", out var et) && et.ValueKind != JsonValueKind.Null)
                    {
                        double e2 = et.GetDouble();
                        if (e2 > time) end = e2;
                    }
                    int lane = h.TryGetProperty("Lane", out var ln) ? ln.GetInt32() : 1;
                    int col = Math.Max(0, Math.Min(31, lane - 1));
                    notes.Add(new Note { Time = time, End = end, Col = col, Type = (end > time ? "hold" : "tap") });
                }
            int kc = 4;
            foreach (var n in notes) kc = Math.Max(kc, n.Col + 1);
            chart.KeyCount = Math.Min(31, kc);
            chart.Notes = notes;

            if (root.TryGetProperty("TimingPoints", out var tps))
            {
                double bp = 0;
                foreach (var tp in tps.EnumerateArray())
                    if (tp.TryGetProperty("Bpm", out var b) && b.GetDouble() > 0) { bp = b.GetDouble(); break; }
                chart.Bpm = bp;
            }
            return Sanitize(chart);
        }

        static Dictionary<string, string> ParseKv(Dictionary<string, List<string>> sections, string name)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sections.TryGetValue(name, out var lines))
                foreach (var l in lines) { var i = l.IndexOf(':'); if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim(); }
            return d;
        }

        /// <summary>
        /// 谱面数据消毒（对畸形文件加固）：
        /// 丢弃 NaN/∞ 音符、负时间归零、长条尾修正、类型白名单（非 hold/arc 归零尾）、
        /// 位置钳制、按时间排序、键数钳制（mania ≤10，其余 ≤31）、列号钳制到有效范围。
        /// 保证任何文件都不会打崩游戏。
        /// </summary>
        static Chart Sanitize(Chart c)
        {
            if (c.Notes == null) c.Notes = new List<Note>();
            if (c.Events == null) c.Events = new List<ChartEvent>();
            c.Notes.RemoveAll(n => n == null || double.IsNaN(n.Time) || double.IsInfinity(n.Time) ||
                                   double.IsNaN(n.End) || double.IsInfinity(n.End));
            foreach (var n in c.Notes)
            {
                if (n.Time < 0) n.Time = 0;
                if (n.End < n.Time) n.End = n.Time;
                if (n.Type == null || Array.IndexOf(ValidTypes, n.Type) < 0) n.Type = "tap";
                if (n.Type != "hold" && n.Type != "arc" && n.Type != "spin" && n.Type != "slide") { n.End = n.Time; }
                n.X = Clamp01(n.X);
                n.Y = Clamp01(n.Y);
                n.EndX = Clamp01(n.EndX);
                n.EndY = Clamp01(n.EndY);
            }
            c.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            int maxKc = MaxKeyCount(c.Mode);
            c.KeyCount = Math.Max(1, Math.Min(maxKc, c.KeyCount));
            foreach (var n in c.Notes)
            {
                // maimai：Col 1~8 外圈 + 9~14 中央 touch，不按 KeyCount 钳制（与 GamePanel.ResetState 例外一致）
                if (c.Mode != GameMode.Maimai)
                    n.Col = Math.Max(0, Math.Min(c.KeyCount - 1, n.Col));
            }
            // 事件仅过滤 NaN/∞ 时间；保留任意非空 Type（bpm/twirl/rotate 等各音游特色事件），空 Type 默认 "moveY"。
            c.Events.RemoveAll(ev => ev == null || double.IsNaN(ev.Time) || double.IsInfinity(ev.Time));
            foreach (var ev in c.Events) if (string.IsNullOrEmpty(ev.Type)) ev.Type = "moveY";
            c.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
            return c;
        }

        /// <summary>各模式轨道数上限（消毒钳制用；未知模式保守取 31）。</summary>
        static int MaxKeyCount(GameMode m) => m switch
        {
            GameMode.Mania => 10,
            GameMode.Adofai => 1,
            GameMode.AdofaiReal => 1,
            GameMode.Maimai => 8,
            GameMode.Arcaea => 6,
            GameMode.Iidx => 8,
            GameMode.Taiko => 2,
            GameMode.Catch => 8,
            _ => 31
        };

        static readonly string[] ValidTypes = { "tap", "hold", "arc", "slide", "flick", "drag", "spin" };
        static double Clamp01(double v) { if (double.IsNaN(v) || v < 0) return 0; return v > 1 ? 1 : v; }
        static int I(string s, int def) => int.TryParse(s, out var v) ? v : def;
        static double D(string s, double def) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        /// <summary>快速提取谱面元信息（标题/艺术家/难度），用于选歌列表。</summary>
        public static (string Title, string Artist, string Diff, string Mode) QuickInfo(string path)
        {
            try
            {
                var chart = ParseFile(path);
                return (chart.Title, chart.Artist, chart.Version, chart.ModeName);
            }
            catch { return (System.IO.Path.GetFileNameWithoutExtension(path), "", "", ""); }
        }
    }
}