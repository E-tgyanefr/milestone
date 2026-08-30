using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace ChartPlayer
{
    public class HudPos
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool Show { get; set; } = true;
    }

    public class SkinSettings
    {
        public Color BgColor { get; set; } = Color.FromArgb(10, 14, 22);
        public Color HitLineColor { get; set; } = Color.White;
        public int HitLineThickness { get; set; } = 2;
        public bool UseLaneColorForNote { get; set; } = true;
        public bool ShowAcc { get; set; } = true;
        public bool ShowScore { get; set; } = true;
        public bool ShowCombo { get; set; } = true;

        // ===== 视觉增强（v2.9+） =====
        public double Slant { get; set; } = 0.0;            // 斜轨强度 0~1（0=垂直轨道，与原音游一致）
        public double BgDim { get; set; } = 0.5;            // 背景曲绘亮度
        public bool ShowBackground { get; set; } = true;    // 显示背景曲绘
        public bool BurstEffects { get; set; } = true;      // 打击特效
        public bool ScreenShake { get; set; } = false;      // 屏幕震动（MISS 时）
        public bool SoundEffects { get; set; } = true;      // 打击音效
        public int JudgeFont { get; set; } = 20;            // 判定文字大小
        public int JudgePosMode { get; set; } = 0;          // 0 轨道上方 1 判定线中央 2 顶部中央 3 隐藏
        public int DevPosMode { get; set; } = 0;            // 延迟数字位置（同上）
        public int HitLineStyle { get; set; } = 0;          // 0 实线 1 虚线 2 点线
        public int HitLineGlow { get; set; } = 0;           // 判定线辉光强度 0~30
        public double HoldAlpha { get; set; } = 0.8;        // 长条透明度
        public int HoldStyle { get; set; } = 0;             // 0 实心 1 辉光 2 描边

        public List<Color> LaneColors { get; set; } = DefaultLanes();
        public List<Color> NoteColors { get; set; } = null;

        public Dictionary<string, HudPos> Layout { get; set; } = DefaultLayout();

        // ===== t63 布局自定义：每模式元素覆盖集（key=模式 Id；value=覆盖元素；只存非默认项） =====
        public Dictionary<string, Dictionary<string, ModeElem>> ModeLayout { get; set; } = new();

        public static List<Color> DefaultLanes() => new List<Color>
        {
            Color.FromArgb(255,92,122), Color.FromArgb(255,179,64), Color.FromArgb(255,217,61),
            Color.FromArgb(61,220,151), Color.FromArgb(57,182,255), Color.FromArgb(123,107,255),
            Color.FromArgb(255,111,216), Color.FromArgb(255,158,88), Color.FromArgb(94,224,232), Color.FromArgb(168,255,96)
        };

        public static Dictionary<string, HudPos> DefaultLayout() => new Dictionary<string, HudPos>
        {
            ["title"] = new HudPos { X = 0.5,  Y = 0.03 },
            ["score"] = new HudPos { X = 0.02, Y = 0.12 },
            ["acc"]   = new HudPos { X = 0.02, Y = 0.20 },
            ["bpm"]   = new HudPos { X = 0.02, Y = 0.28 },
            ["kps"]   = new HudPos { X = 0.02, Y = 0.33 },
            ["notes"] = new HudPos { X = 0.02, Y = 0.38 },
            ["combo"] = new HudPos { X = 0.5,  Y = 0.72 },
            ["judge"] = new HudPos { X = 0.5,  Y = 0.78 },
            ["dev"]   = new HudPos { X = 0.5,  Y = 0.83 },
            // 判定线纵向位置（可在布局编辑器中上下拖动），默认 0.82 = 轨道下部 82% 处
            // （实机：osu!mania HitPosition=402/480≈84%，IIDX 判定线≈80–85%）
            ["hitline"] = new HudPos { X = 0.5, Y = 0.82 },
        };

        public Color LaneColor(int col)
        {
            if (LaneColors != null && LaneColors.Count > 0) return LaneColors[col % LaneColors.Count];
            return Color.White;
        }
        public Color NoteColor(int col)
        {
            if (NoteColors != null && NoteColors.Count > 0) return NoteColors[col % NoteColors.Count];
            if (UseLaneColorForNote) return LaneColor(col);
            return Color.White;
        }
        public void RandomizeLanes()
        {
            var rnd = new Random();
            var list = DefaultLanes();
            for (int i = list.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = list[i]; list[i] = list[j]; list[j] = t; }
            LaneColors = list;
            NoteColors = null;
        }

        static string SkinPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skin.json");
        static string PlayerSkinPath => Path.Combine(AppConfig.DefaultPlayerFolder, "skin.json");

        public SkinDto ToDto() => new SkinDto
        {
            Bg = ColorTranslator.ToHtml(BgColor),
            HitLine = ColorTranslator.ToHtml(HitLineColor),
            Th = HitLineThickness,
            UseLane = UseLaneColorForNote,
            ShowAcc = ShowAcc, ShowScore = ShowScore, ShowCombo = ShowCombo,
            Lanes = LaneColors.ConvertAll(c => ColorTranslator.ToHtml(c)),
            Layout = new Dictionary<string, HudPos>(Layout),
            ModeLayout = ModeLayout == null ? new() : new Dictionary<string, Dictionary<string, ModeElem>>(
                ModeLayout.ToDictionary(kv => kv.Key, kv => new Dictionary<string, ModeElem>(kv.Value))),
            Slant = Slant, BgDim = BgDim, ShowBg = ShowBackground,
            Burst = BurstEffects, Shake = ScreenShake, Sound = SoundEffects,
            JudgeFont = JudgeFont, JudgePos = JudgePosMode, DevPos = DevPosMode,
            HitLineStyle = HitLineStyle, HitLineGlow = HitLineGlow,
            HoldAlpha = HoldAlpha, HoldStyle = HoldStyle
        };

        public void ApplyDto(SkinDto dto)
        {
            if (dto == null) return;
            if (!string.IsNullOrEmpty(dto.Bg)) BgColor = ColorTranslator.FromHtml(dto.Bg);
            if (!string.IsNullOrEmpty(dto.HitLine)) HitLineColor = ColorTranslator.FromHtml(dto.HitLine);
            if (dto.Th > 0) HitLineThickness = dto.Th;
            UseLaneColorForNote = dto.UseLane;
            ShowAcc = dto.ShowAcc; ShowScore = dto.ShowScore; ShowCombo = dto.ShowCombo;
            if (dto.Lanes != null && dto.Lanes.Count > 0) LaneColors = dto.Lanes.ConvertAll(ColorTranslator.FromHtml);
            if (dto.Slant.HasValue) Slant = Math.Max(0, Math.Min(1, dto.Slant.Value));
            if (dto.BgDim.HasValue) BgDim = Math.Max(0, Math.Min(1, dto.BgDim.Value));
            if (dto.ShowBg.HasValue) ShowBackground = dto.ShowBg.Value;
            if (dto.Burst.HasValue) BurstEffects = dto.Burst.Value;
            if (dto.Shake.HasValue) ScreenShake = dto.Shake.Value;
            if (dto.Sound.HasValue) SoundEffects = dto.Sound.Value;
            if (dto.JudgeFont.HasValue) JudgeFont = Math.Max(12, Math.Min(44, dto.JudgeFont.Value));
            if (dto.JudgePos.HasValue) JudgePosMode = Math.Max(0, Math.Min(3, dto.JudgePos.Value));
            if (dto.DevPos.HasValue) DevPosMode = Math.Max(0, Math.Min(3, dto.DevPos.Value));
            if (dto.HitLineStyle.HasValue) HitLineStyle = Math.Max(0, Math.Min(2, dto.HitLineStyle.Value));
            if (dto.HitLineGlow.HasValue) HitLineGlow = Math.Max(0, Math.Min(30, dto.HitLineGlow.Value));
            if (dto.HoldAlpha.HasValue) HoldAlpha = Math.Max(0.1, Math.Min(1, dto.HoldAlpha.Value));
            if (dto.HoldStyle.HasValue) HoldStyle = Math.Max(0, Math.Min(2, dto.HoldStyle.Value));
            if (dto.Layout != null && dto.Layout.Count > 0)
            {
                Layout = dto.Layout;
                // 旧皮肤文件可能没有判定线位置，自动补上默认值（实机：mania 84% / IIDX 80-85%）
                if (!Layout.ContainsKey("hitline"))
                    Layout["hitline"] = new HudPos { X = 0.5, Y = 0.82 };
            }
            // t63：ModeLayout 增量字段（旧 skin.json 无此字段→null→空字典=全默认，零迁移）
            if (dto.ModeLayout != null)
                ModeLayout = new Dictionary<string, Dictionary<string, ModeElem>>(
                    dto.ModeLayout.ToDictionary(kv => kv.Key, kv => new Dictionary<string, ModeElem>(kv.Value)));
        }

        // ===== t63 继承链读取：① ModeLayout[mode][key] → ② ModeDefaults(mode)[key] → ③ Skin.Layout[key]（仅 HUD）→ ④ 代码兜底 =====
        public ModeElem Eff(string mode, string key)
        {
            if (ModeLayout != null && ModeLayout.TryGetValue(mode, out var d) && d != null && d.TryGetValue(key, out var v) && v != null)
                return v;
            var def = LayoutCustomDefaults.ModeDefault(mode, key);
            return def ?? new ModeElem();
        }

        /// <summary>当前模式的生效缓存（渲染每帧读；进入编辑/切谱/换模式时重建）。仅存覆盖项+默认项混合（读快）。</summary>
        Dictionary<string, ModeElem> _effCache;
        string _effCacheMode = null;
        public Dictionary<string, ModeElem> EffCache(string mode)
        {
            if (_effCache == null || _effCacheMode != mode)
            {
                var d = new Dictionary<string, ModeElem>();
                if (ModeLayout != null && ModeLayout.TryGetValue(mode, out var ov) && ov != null)
                    foreach (var kv in ov) d[kv.Key] = kv.Value;
                _effCache = d; _effCacheMode = mode;
            }
            return _effCache;
        }
        public void InvalidateEffCache() { _effCache = null; }

        public void Save() => ExportToFile(SkinPath);

        /// <summary>优先加载 Player 文件夹皮肤，其次程序目录皮肤。</summary>
        public static SkinSettings LoadWithPlayerFirst()
        {
            var s = new SkinSettings();
            try
            {
                if (File.Exists(PlayerSkinPath)) { s.ApplyDto(JsonSerializer.Deserialize<SkinDto>(File.ReadAllText(PlayerSkinPath))); return s; }
                if (File.Exists(SkinPath)) s.ApplyDto(JsonSerializer.Deserialize<SkinDto>(File.ReadAllText(SkinPath)));
            }
            catch { }
            return s;
        }

        public void ExportToFile(string path)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(ToDto(), new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }

        public static SkinSettings ImportFromFile(string path)
        {
            var s = new SkinSettings();
            s.ApplyDto(JsonSerializer.Deserialize<SkinDto>(File.ReadAllText(path)));
            return s;
        }
    }

    public class SkinDto
    {
        public string Bg { get; set; }
        public string HitLine { get; set; }
        public int Th { get; set; }
        public bool UseLane { get; set; }
        public bool ShowAcc { get; set; }
        public bool ShowScore { get; set; }
        public bool ShowCombo { get; set; }
        public List<string> Lanes { get; set; }
        public Dictionary<string, HudPos> Layout { get; set; }
        public Dictionary<string, Dictionary<string, ModeElem>> ModeLayout { get; set; }

        // 视觉增强（nullable：旧皮肤文件无这些字段时保持默认值）
        public double? Slant { get; set; }
        public double? BgDim { get; set; }
        public bool? ShowBg { get; set; }
        public bool? Burst { get; set; }
        public bool? Shake { get; set; }
        public bool? Sound { get; set; }
        public int? JudgeFont { get; set; }
        public int? JudgePos { get; set; }
        public int? DevPos { get; set; }
        public int? HitLineStyle { get; set; }
        public int? HitLineGlow { get; set; }
        public double? HoldAlpha { get; set; }
        public int? HoldStyle { get; set; }
    }
}
