using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ChartPlayer
{
    public class ReplayEvent
    {
        public double T { get; set; }
        public string Code { get; set; } = "";
        public bool Down { get; set; }
    }

    public class ReplayFile
    {
        public int Version { get; set; } = 1;
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int KeyCount { get; set; }
        public List<ReplayEvent> Events { get; set; } = new List<ReplayEvent>();
    }

    public static class ReplaySystem
    {
        static string ReplayDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replay");

        public static void EnsureDir() { try { Directory.CreateDirectory(ReplayDir); } catch { } }

        public static string[] List()
        {
            EnsureDir();
            try { return Directory.GetFiles(ReplayDir, "*.json"); } catch { return Array.Empty<string>(); }
        }

        public static bool Save(ReplayFile r)
        {
            try
            {
                EnsureDir();
                string name = "replay-" + Sanitize(r.Title) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.WriteAllText(Path.Combine(ReplayDir, name), JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch { return false; }
        }

        public static ReplayFile Load(string path)
        {
            try { return JsonSerializer.Deserialize<ReplayFile>(File.ReadAllText(path)); } catch { return null; }
        }

        public static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
