using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace ChartPlayer
{
    public class HistoryEntry
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public double Acc { get; set; }
        public int Score { get; set; }
        public int MaxCombo { get; set; }
        public string Date { get; set; } = "";
    }

    public class PlayerStats
    {
        public int Plays { get; set; }
        public int NotesHit { get; set; }
        public double MaxAcc { get; set; }
        public int MaxScore { get; set; }
        public int BestCombo { get; set; }
    }

    public class PlayerProfile
    {
        public string Name { get; set; } = "玩家";
        public string AvatarBase64 { get; set; } = "";

        /// <summary>解码头像为 Image（无则 null）。</summary>
        public Image AvatarImage
        {
            get
            {
                if (string.IsNullOrEmpty(AvatarBase64)) return null;
                try
                {
                    using var ms = new MemoryStream(Convert.FromBase64String(AvatarBase64));
                    return Image.FromStream(ms);
                }
                catch { return null; }
            }
        }

        /// <summary>从图片文件裁剪为 128×128 居中方形并转 base64 保存。</summary>
        public void SetAvatarFromFile(string path)
        {
            try
            {
                using var src = Image.FromFile(path);
                int s = Math.Min(src.Width, src.Height);
                var bmp = new Bitmap(128, 128);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawImage(src,
                        new Rectangle(0, 0, 128, 128),
                        new Rectangle((src.Width - s) / 2, (src.Height - s) / 2, s, s),
                        GraphicsUnit.Pixel);
                }
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                AvatarBase64 = Convert.ToBase64String(ms.ToArray());
                bmp.Dispose();
            }
            catch { }
        }
    }

    public class PlayerData
    {
        public PlayerStats Stats { get; set; } = new PlayerStats();
        public List<HistoryEntry> History { get; set; } = new List<HistoryEntry>();

        static string DataPath => Path.Combine(AppConfig.DefaultDataFolder, "playerdata.json");

        public static PlayerData Load()
        {
            try { if (File.Exists(DataPath)) return JsonSerializer.Deserialize<PlayerData>(File.ReadAllText(DataPath)) ?? new PlayerData(); }
            catch { }
            return new PlayerData();
        }
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppConfig.DefaultDataFolder);
                if (History.Count > 50) History = History.GetRange(0, 50);
                File.WriteAllText(DataPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public void Record(GameResult r)
        {
            if (r == null || string.IsNullOrEmpty(r.Title)) return;
            Stats.Plays++;
            int hit = 0;
            foreach (var kv in r.Hits) if (kv.Key != "MISS" && kv.Key != "BAD") hit += kv.Value;
            Stats.NotesHit += hit;
            if (r.Acc > Stats.MaxAcc) Stats.MaxAcc = r.Acc;
            if (r.Score > Stats.MaxScore) Stats.MaxScore = r.Score;
            if (r.MaxCombo > Stats.BestCombo) Stats.BestCombo = r.MaxCombo;
            History.Insert(0, new HistoryEntry
            {
                Title = r.Title, Artist = r.Artist, Acc = r.Acc, Score = r.Score, MaxCombo = r.MaxCombo,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            });
            if (History.Count > 50) History.RemoveRange(50, History.Count - 50);
            Save();
        }

        /* ---------- 玩家信息（Player/player.json） ---------- */
        static string PlayerPath => Path.Combine(AppConfig.DefaultPlayerFolder, "player.json");

        public static PlayerProfile LoadProfile()
        {
            try { if (File.Exists(PlayerPath)) return JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(PlayerPath)) ?? new PlayerProfile(); }
            catch { }
            return new PlayerProfile();
        }
        public static void SaveProfile(PlayerProfile p)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.DefaultPlayerFolder);
                File.WriteAllText(PlayerPath, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
