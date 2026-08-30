using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ChartPlayer
{
    /// <summary>
    /// 打包导出（用户指令）：把谱面文件打包成完整独立可运行程序（单文件 exe）。
    /// 原理：Milestone.exe --pack "<谱面>" → 解析谱面（全 10 模式）→ 音轨归一化为 4K 时间列 →
    /// 复制引擎播放器模板 pack-template.exe（MilestoneEngine 零依赖 Win32 壳）→ 尾部追加
    /// [payload(int64 len + magic MILSTPK1)] 内嵌 → 输出 <曲名>.exe：双击自动播放，ESC/播完自动关闭。
    /// </summary>
    public static class PackExporter
    {
        public const string Magic = "MILSTPK1";

        public static int RunPack(string chartPath, string outDir)
        {
            string log = "===== Milestone --pack =====\n";
            int code = 1;
            try
            {
                string full = Path.GetFullPath(chartPath);
                if (!File.Exists(full)) throw new FileNotFoundException("谱面不存在：" + full);
                var chart = ChartParser.ParseFile(full);
                if (chart == null) throw new InvalidOperationException("谱面解析失败（不支持格式或文件损坏）。");

                // 归一化：所有模式/所有部件 → 4K 时间列（键盘类取 Col%4；触摸类取 X 分桶；hold/arc/slide 也以击打时刻计）
                var notes = new List<(double t, int lane)>();
                foreach (var part in chart.EffectiveParts())
                {
                    int kc = part.KeyCount > 0 ? part.KeyCount : 4;
                    foreach (var n in part.Notes)
                    {
                        double t = n.Time + chart.Offset;
                        int lane;
                        if (n.Col >= 0 && kc > 0) lane = n.Col % 4;
                        else if (n.X >= 0 && n.X <= 1) lane = (int)(n.X * 4.0) % 4;
                        else lane = 0;
                        notes.Add((t, lane));
                    }
                }
                notes.Sort((a, b) => a.t.CompareTo(b.t));
                if (notes.Count > 8000) notes = notes.GetRange(0, 8000);

                // 序列化 payload
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(chart.Title.Length > 0 ? chart.Title : Path.GetFileNameWithoutExtension(full));
                    bw.Write(notes.Count);
                    foreach (var (t, lane) in notes) { bw.Write(t); bw.Write((byte)lane); }
                    bw.Flush();
                    byte[] payload = ms.ToArray();

                    // 模板
                    string template = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pack-template.exe");
                    if (!File.Exists(template)) throw new FileNotFoundException("缺少播放器模板 pack-template.exe（请先发布 PublishPlayer 并放置于程序目录）", template);
                    string name = Sanitize(chart.Title.Length > 0 ? chart.Title : Path.GetFileNameWithoutExtension(full));
                    string outPath = Path.Combine(string.IsNullOrEmpty(outDir) ? Path.GetDirectoryName(full) : outDir, name + ".exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                    using (var fs = File.Create(outPath))
                    {
                        var tmpl = File.ReadAllBytes(template);
                        fs.Write(tmpl, 0, tmpl.Length);
                        fs.Write(payload, 0, payload.Length);
                        byte[] len = BitConverter.GetBytes((long)payload.Length);
                        fs.Write(len, 0, 8);
                        var mg = Encoding.ASCII.GetBytes(Magic);
                        fs.Write(mg, 0, mg.Length);
                    }
                    long kb = new FileInfo(outPath).Length / 1024;
                    log += "✅ 打包成功：" + outPath + "（" + kb + " KB · 音符 " + notes.Count + " · 标题「" + (chart.Title.Length > 0 ? chart.Title : Path.GetFileNameWithoutExtension(full)) + "」）\n";
                    Console.WriteLine(log);
                    try { File.WriteAllText("pack.log", log, new System.Text.UTF8Encoding(true)); } catch { }
                    code = 0;
                }
            }
            catch (Exception ex)
            {
                log += "❌ 打包失败：" + ex.Message + "\n";
                Console.Error.WriteLine(log);
                try { File.WriteAllText("pack.log", log, new System.Text.UTF8Encoding(true)); } catch { }
                code = 1;
            }
            return code;
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "chart";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}