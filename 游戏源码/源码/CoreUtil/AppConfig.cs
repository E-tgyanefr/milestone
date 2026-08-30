using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace ChartPlayer
{
    public class AppConfig
    {
        public string ChartsFolder { get; set; } = "";
        public string DataFolder { get; set; } = "";
        public string PlayerFolder { get; set; } = "";
        public double Speed { get; set; } = 2.0;   // 默认流速 2.00
        public double Offset { get; set; } = 0;
        public int JudgeBase { get; set; } = 1;
        public bool ShowFps { get; set; } = false;
        public string JudgePreset { get; set; } = "std2";
        public bool Autoplay { get; set; } = false;
        public int QualityPreset { get; set; } = 3;   // 画质：0 低 1 中 2 高 3 自动（默认自动）
        public Dictionary<int, string[]> Keys { get; set; } = new Dictionary<int, string[]>();
        public string SkinFile { get; set; } = "";

        // 自定义判定持久化
        public List<JudgeLevel> CustomLevels { get; set; } = null;
        public double CustomMissWindow { get; set; } = 200;
        public int CustomComboBonusMax { get; set; } = 100;

        // ===== Milestone 新设置（nullable：旧配置无字段时用默认值） =====
        public bool? SlantEnabled { get; set; }
        public bool? Camera3D { get; set; }
        public double? CameraPitch { get; set; }
        public double? CameraYaw { get; set; }
        public double? CameraDepth { get; set; }
        public int? HitsoundStyle { get; set; }
        public bool? HitsoundPerJudge { get; set; }
        public int? HitsoundVolume { get; set; }
        public bool? ShowHitFx { get; set; }
        public bool? ResultScreenEnabled { get; set; }
        public int? UiTheme { get; set; }
        public int? TransitionStyle { get; set; }
        public int? RefreshRate { get; set; }   // 刷新率挡位（0=无限制 1000+）
        public bool? OsuStdPlayfield { get; set; }   // osu!std 4:3 框定游玩区（旧配置缺字段时默认开启）
        // ===== t5：编辑器三栏折叠档（0=开 1=左图标轨 2=收）与最近谱面列表 =====
        public int? PanelLeftMode { get; set; }
        public int? PanelRightMode { get; set; }
        public List<string> RecentCharts { get; set; } = null;

        // 一次性迁移标记：旧配置（无此字段）首次加载时把默认流速升级为 2.0
        public bool SpeedMigrated { get; set; } = false;

        // t62：迁移标记——用户指令「谱面文件夹默认在程序当前目录」；旧配置 ChartsFolder 指向别处时一次性重置为
        // BaseDirectory\Chart（仅当默认目录可创建；用户自选曲库路径保留不再强制覆盖）。
        public bool ChartsFolderMigrated { get; set; } = false;

        static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartPlayer", "config.json");

        public static string DefaultChartsFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chart");
        public static string DefaultDataFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlayerData");
        public static string DefaultPlayerFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Player");
        public static string DefaultReplayFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replay");
        public static string DefaultSkinPath => Path.Combine(DefaultPlayerFolder, "skin.json");

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            try
            {
                if (File.Exists(ConfigPath))
                    cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
            }
            catch { }

            // 迁移：旧配置（未标记 SpeedMigrated）→ 默认流速升级为 2.0 并立即保存
            if (!cfg.SpeedMigrated)
            {
                cfg.Speed = 2.0;
                cfg.SpeedMigrated = true;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                    File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }

            if (string.IsNullOrEmpty(cfg.ChartsFolder)) cfg.ChartsFolder = DefaultChartsFolder;
            // t62 ④：旧 config 的 ChartsFolder 如果指向非当前程序目录，一次性重置为程序目录 Chart（用户指令：曲库默认=程序当前目录）
            if (!cfg.ChartsFolderMigrated)
            {
                try
                {
                    string def = DefaultChartsFolder;
                    if (!string.IsNullOrEmpty(cfg.ChartsFolder) && !Path.GetFullPath(cfg.ChartsFolder).Equals(Path.GetFullPath(def), StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(def);
                        cfg.ChartsFolder = def;
                        cfg.ChartsFolderMigrated = true;
                    }
                    else if (string.IsNullOrEmpty(cfg.ChartsFolder))
                    {
                        cfg.ChartsFolder = def;
                        cfg.ChartsFolderMigrated = true;
                    }
                }
                catch { }
            }
            if (string.IsNullOrEmpty(cfg.DataFolder)) cfg.DataFolder = DefaultDataFolder;
            if (string.IsNullOrEmpty(cfg.PlayerFolder)) cfg.PlayerFolder = DefaultPlayerFolder;
            // 打包运行时自动创建默认目录（含当前目录 Chart）
            try { Directory.CreateDirectory(cfg.ChartsFolder); } catch { }
            try { Directory.CreateDirectory(cfg.DataFolder); } catch { }
            try { Directory.CreateDirectory(cfg.PlayerFolder); } catch { }
            try { Directory.CreateDirectory(DefaultReplayFolder); } catch { }
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public void Apply()
        {
            GameSettings.Speed = Speed > 0 ? Speed : 2.0;
            GameSettings.Offset = Offset;
            GameSettings.Autoplay = Autoplay;
            GameSettings.JudgeBase = JudgeBase;
            GameSettings.ShowFps = ShowFps;
            GameSettings.SlantEnabled = SlantEnabled ?? true;
            GameSettings.Camera3D = Camera3D ?? true;
            GameSettings.CameraPitch = Math.Max(0, Math.Min(60, CameraPitch ?? 20));
            GameSettings.CameraYaw = Math.Max(-30, Math.Min(30, CameraYaw ?? 4));
            GameSettings.CameraDepth = Math.Max(0.4, Math.Min(2.5, CameraDepth ?? 1.0));
            GameSettings.HitsoundStyle = Math.Max(0, Math.Min(2, HitsoundStyle ?? 0));
            GameSettings.HitsoundPerJudge = HitsoundPerJudge ?? true;
            GameSettings.HitsoundVolume = Math.Max(0, Math.Min(100, HitsoundVolume ?? 70));
            GameSettings.ShowHitFx = ShowHitFx ?? true;
            GameSettings.ResultScreenEnabled = ResultScreenEnabled ?? true;
            GameSettings.UiTheme = Math.Max(0, Math.Min(2, UiTheme ?? 0));
            GameSettings.TransitionStyle = Math.Max(-1, Math.Min(2, TransitionStyle ?? -1));
            GameSettings.OsuStdPlayfield = OsuStdPlayfield ?? true;
            SoundFx.Enabled = GameSettings.HitsoundVolume > 0;
            SoundFx.Volume = GameSettings.HitsoundVolume;
            SoundFx.Style = GameSettings.HitsoundStyle;
            GraphicsQuality.ApplyPreset(QualityPreset >= 0 && QualityPreset <= 3 ? QualityPreset : 2);
            try
            {
                if (JudgePreset == "custom" && CustomLevels != null && CustomLevels.Count >= 3)
                    JudgeSettings.ApplyCustom(CustomLevels, CustomMissWindow, CustomComboBonusMax);
                else
                    JudgeSettings.ApplyPreset(JudgePreset);
            }
            catch { }

            if (Keys != null)
                foreach (var kv in Keys)
                {
                    if (kv.Value == null || kv.Value.Length == 0) continue;
                    var arr = new Keys[kv.Value.Length];
                    bool ok = true;
                    for (int i = 0; i < kv.Value.Length; i++)
                    {
                        if (Enum.TryParse(kv.Value[i], true, out Keys k)) arr[i] = k;
                        else { ok = false; break; }
                    }
                    if (ok) GameSettings.KeyMaps[kv.Key] = arr;
                }
        }

        public void Capture()
        {
            // t5：编辑器三栏折叠档/最近谱面由 ChartEditorPanel 直接写盘 —— Capture 前从盘上取最新值，
            // 避免本实例（启动期加载）退出保存时用旧值覆盖会话中的变更。
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var fresh = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                    if (fresh != null)
                    {
                        PanelLeftMode = fresh.PanelLeftMode;
                        PanelRightMode = fresh.PanelRightMode;
                        RecentCharts = fresh.RecentCharts;
                    }
                }
            }
            catch { }
            Speed = GameSettings.Speed;
            Offset = GameSettings.Offset;
            Autoplay = GameSettings.Autoplay;
            JudgeBase = GameSettings.JudgeBase;
            ShowFps = GameSettings.ShowFps;
            QualityPreset = GraphicsQuality.Preset;
            JudgePreset = JudgeSettings.PresetKey;
            SlantEnabled = GameSettings.SlantEnabled;
            Camera3D = GameSettings.Camera3D;
            CameraPitch = GameSettings.CameraPitch;
            CameraYaw = GameSettings.CameraYaw;
            CameraDepth = GameSettings.CameraDepth;
            HitsoundStyle = GameSettings.HitsoundStyle;
            HitsoundPerJudge = GameSettings.HitsoundPerJudge;
            HitsoundVolume = GameSettings.HitsoundVolume;
            ShowHitFx = GameSettings.ShowHitFx;
            ResultScreenEnabled = GameSettings.ResultScreenEnabled;
            UiTheme = GameSettings.UiTheme;
            TransitionStyle = GameSettings.TransitionStyle;
            OsuStdPlayfield = GameSettings.OsuStdPlayfield;
            if (JudgePreset == "custom")
            {
                CustomLevels = new List<JudgeLevel>(JudgeSettings.Levels);
                CustomMissWindow = JudgeSettings.MissWindow;
                CustomComboBonusMax = JudgeSettings.ComboBonusMax;
            }
            else
            {
                CustomLevels = null;
            }
            Keys = new Dictionary<int, string[]>();
            foreach (var kv in GameSettings.KeyMaps)
                Keys[kv.Key] = Array.ConvertAll(kv.Value, k => k.ToString());
            SkinFile = DefaultSkinPath;
            SpeedMigrated = true;
        }
    }
}
