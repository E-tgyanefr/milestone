using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ChartPlayer
{
    /// <summary>
    /// 谱面包导入：.osz / .mcz / .zip 本质是 zip 压缩包，直接内建解压（不再依赖外部 7z.exe）。
    /// 完整保留包内目录结构（osu! 曲包的音频、背景图路径依赖相对目录），
    /// 文件名编码自动兼容 UTF-8 / GBK（国内 Malody 包常见）。
    /// </summary>
    public static class ZipImport
    {
        /// <summary>解压谱面包到曲库目录，返回包内谱面文件数量。</summary>
        public static int Extract(string zipPath, string chartsFolder)
        {
            try
            {
                if (!File.Exists(zipPath)) return 0;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var enc = PickEncoding(zipPath);
                Directory.CreateDirectory(chartsFolder);
                var target = Path.Combine(chartsFolder, Path.GetFileNameWithoutExtension(zipPath));
                if (Directory.Exists(target))
                    target = Path.Combine(chartsFolder, Path.GetFileNameWithoutExtension(zipPath) + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

                long zipSize = new FileInfo(zipPath).Length;
                int chartCount = 0;
                long totalBytes = 0;
                int entries = 0;

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Read, enc))
                {
                    foreach (var e in zip.Entries)
                    {
                        entries++;
                        if (entries > 20000) break;
                        string name = SafeName(e.FullName);
                        if (string.IsNullOrEmpty(name) || e.Length == 0) continue;

                        totalBytes += e.Length;
                        if (totalBytes > 1024L * 1024 * 1024) break;                     // 总解压 1GB 上限
                        if (zipSize > 0 && totalBytes > zipSize * 100) break;            // 压缩比 >100：疑似 zip 炸弹

                        string dest = Path.Combine(target, name);
                        var dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        e.ExtractToFile(dest, true);

                        var ext = Path.GetExtension(dest).ToLowerInvariant();
                        if (Array.IndexOf(ChartParser.ChartExts, ext) >= 0) chartCount++;
                    }
                }
                Logger.Info("解压完成：" + zipPath + " → " + target + "（谱面 " + chartCount + " 个）");
                return chartCount;
            }
            catch (Exception ex)
            {
                Logger.Error("解压失败：" + zipPath, ex);
                return 0;
            }
        }

        /// <summary>先用 UTF-8 检查包内文件名是否出现乱码替换符，有则改用 GBK。</summary>
        static Encoding PickEncoding(string path)
        {
            try
            {
                using (var z = ZipFile.OpenRead(path))
                    foreach (var e in z.Entries)
                        if (e.FullName.IndexOf('\uFFFD') >= 0)
                            return Encoding.GetEncoding(936);
            }
            catch { }
            return new UTF8Encoding(false);
        }

        static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace('\\', '/');
            while (name.StartsWith("/", StringComparison.Ordinal)) name = name.Substring(1);
            foreach (var seg in name.Split('/'))
                if (seg == "..") return null;
            return name;
        }
    }
}
