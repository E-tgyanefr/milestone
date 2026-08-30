using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ChartPlayer
{
    public class LeaderboardEntry
    {
        public string Name { get; set; } = "";
        public int Score { get; set; }
        public double Acc { get; set; }
        public int Combo { get; set; }
        public string Chart { get; set; } = "";
        public string Date { get; set; } = "";
    }

    /// <summary>联机排行榜本地持久化（无中心服务器，本地累计各端收到的成绩）。</summary>
    public static class MpLeaderboard
    {
        // t62：游戏内容根=程序当前目录（BaseDirectory，用户指令）——排行榜迁到 BaseDirectory\PlayerData\mp_leaderboard.json；
        // 旧数据迁移：首次 Load 时若新位置无文件而旧位置（%LocalAppData%\ChartPlayer\leaderboard.json）存在则复制（读仍兼容两种）。
        static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
        static string LegacyPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChartPlayer", "leaderboard.json");
        static string Path => System.IO.Path.Combine(BaseDir, "PlayerData", "mp_leaderboard.json");

        static void MigrateLegacy()
        {
            try
            {
                if (!File.Exists(Path) && File.Exists(LegacyPath))
                {
                    var dir = System.IO.Path.GetDirectoryName(Path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(LegacyPath, Path, true);
                }
            }
            catch { }
        }

        static readonly object _lock = new object();
        static List<LeaderboardEntry> _entries = new List<LeaderboardEntry>();

        public static IReadOnlyList<LeaderboardEntry> All
        {
            get { lock (_lock) return _entries.ToList(); }
        }

        public static void Load()
        {
            lock (_lock)
            {
                try
                {
                    MigrateLegacy();   // t62：旧位置 → BaseDirectory\PlayerData\mp_leaderboard.json
                    if (File.Exists(Path))
                        _entries = JsonSerializer.Deserialize<List<LeaderboardEntry>>(File.ReadAllText(Path)) ?? new List<LeaderboardEntry>();
                    else _entries = new List<LeaderboardEntry>();
                }
                catch { _entries = new List<LeaderboardEntry>(); }
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    _entries = _entries.OrderByDescending(e => e.Score).Take(200).ToList();
                    File.WriteAllText(Path, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }

        public static void Add(LeaderboardEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Name)) return;
            lock (_lock)
            {
                _entries.Add(e);
                _entries = _entries.OrderByDescending(x => x.Score).Take(200).ToList();
            }
            Save();
        }

        public static void RecordPlayer(string name, int score, double acc, int combo, string chart)
        {
            Add(new LeaderboardEntry
            {
                Name = name,
                Score = score,
                Acc = acc,
                Combo = combo,
                Chart = chart,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            });
        }
    }
}
