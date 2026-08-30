using System;
using System.Collections.Generic;
using System.Linq;

namespace ChartPlayer
{
    /// <summary>
    /// 模式注册表（引擎层第一步）：全部 20 种玩法的元数据单一事实来源。
    /// 显示名、模式字符串、键数、有轨/无轨、默认键位、判定预设键均在此定义，
    /// 供 GamePanel / ChartEditorPanel / ChartParser 共同使用。
    /// </summary>
    public static class ModeSystem
    {
        public class ModeInfo
        {
            public GameMode Mode;
            public string Id;          // .mil 模式字符串
            public string Display;     // 显示名
            public int Keys;           // 默认键数（列式输入模式）
            public bool Tracked;       // true=有轨（列式编辑）；false=无轨（位置场编辑）
            public bool Adjustable;    // 键数是否可调
            public string KeyHint;     // 键位提示（如 "S D F 空格 J K L"）
            public bool Removed;       // true=玩法已移除（保留注册表接口，供开发者将来重新接入）
            public bool Editable = true;   // false=不出现在编辑器模式列表（如回环作曲——编辑器无对应编辑分支）
        }

        /// <summary>当前可玩模式列表（已移除玩法不出现；玩法全开放——无开发者模式门控）。</summary>
        public static IEnumerable<ModeInfo> Available => All.Where(m => !m.Removed);

        /// <summary>玩法可用性（仅"已移除玩法"过滤，无 TestModes 门控）。</summary>
        public static bool IsAvailable(GameMode mode)
            => _byMode.TryGetValue(mode, out var m) && !m.Removed;

        public static readonly ModeInfo[] All =
        {
            new ModeInfo { Mode = GameMode.Mania,       Id = "mania",    Display = "Mania",          Keys = 4,  Tracked = true,  Adjustable = true,  KeyHint = "D F J K" },
            new ModeInfo { Mode = GameMode.Maimai,      Id = "maimai",   Display = "maimai",         Keys = 8,  Tracked = false, Adjustable = false, KeyHint = "8 分区" },
            new ModeInfo { Mode = GameMode.Wacca,       Id = "wacca",    Display = "WACCA",          Keys = 8,  Tracked = false, Adjustable = false, KeyHint = "8 分区", Removed = true },
            new ModeInfo { Mode = GameMode.Phigros,     Id = "phigros",  Display = "Phigros",        Keys = 4,  Tracked = false, Adjustable = false, KeyHint = "D F J K（判定线上自由位置）" },
            new ModeInfo { Mode = GameMode.Arcaea,      Id = "arcaea",   Display = "Arcaea",         Keys = 6,  Tracked = true,  Adjustable = false, KeyHint = "6 轨（天2+地4）" },
            new ModeInfo { Mode = GameMode.Cytus,       Id = "cytus",    Display = "Cytus",          Keys = 4,  Tracked = false, Adjustable = false, KeyHint = "4 分位" },
            new ModeInfo { Mode = GameMode.Deemo,       Id = "deemo",    Display = "Deemo",          Keys = 2,  Tracked = false, Adjustable = false, KeyHint = "左右半区", Removed = true },
            new ModeInfo { Mode = GameMode.OsuStandard, Id = "osustd",   Display = "osu!standard",   Keys = 1,  Tracked = false, Adjustable = false, KeyHint = "鼠标/Z X 空格" },
            new ModeInfo { Mode = GameMode.Taiko,       Id = "taiko",    Display = "osu!taiko",      Keys = 2,  Tracked = true,  Adjustable = false, KeyHint = "Z X C V（KDDK：X/C=ドン · Z/V=カッ）", Removed = true },
            new ModeInfo { Mode = GameMode.Catch,       Id = "catch",    Display = "osu!catch",      Keys = 1,  Tracked = false, Adjustable = false, KeyHint = "← → 移动（Z/X）· Shift=dash", Removed = true },
            new ModeInfo { Mode = GameMode.Sus,         Id = "sus",      Display = "偶像音游 (SUS)", Keys = 7,  Tracked = true,  Adjustable = false, KeyHint = "7 轨（SUS）", Removed = true },
            new ModeInfo { Mode = GameMode.Sdvx,        Id = "sdvx",     Display = "SDVX",           Keys = 6,  Tracked = true,  Adjustable = false, KeyHint = "D F J K=BT · C M=FX", Removed = true },
            new ModeInfo { Mode = GameMode.MuseDash,    Id = "musedash", Display = "Muse Dash",      Keys = 2,  Tracked = false, Adjustable = false, KeyHint = "D F=上轨 · J K=下轨", Removed = true },
            new ModeInfo { Mode = GameMode.Adofai,      Id = "adofai",   Display = "Routlock",       Keys = 1,  Tracked = true,  Adjustable = false, KeyHint = "空格/D 单键" },
            new ModeInfo { Mode = GameMode.AdofaiReal,  Id = "adofai2",  Display = "ADOFAI",          Keys = 1,  Tracked = true,  Adjustable = false, KeyHint = "空格/D 单键（每拍输入）" },
            new ModeInfo { Mode = GameMode.Rotaeno,     Id = "rotaeno",  Display = "Rotaeno",        Keys = 4,  Tracked = false, Adjustable = false, KeyHint = "D F J K（4 扇形）", Removed = true },
            new ModeInfo { Mode = GameMode.Dynamix,     Id = "dynamix",  Display = "Dynamix",        Keys = 3,  Tracked = true,  Adjustable = false, KeyHint = "F=左 D=下 J=右", Removed = true },
            new ModeInfo { Mode = GameMode.Lanota,      Id = "lanota",   Display = "Lanota",         Keys = 4,  Tracked = false, Adjustable = false, KeyHint = "D F J K（4 扇形）", Removed = true },
            new ModeInfo { Mode = GameMode.ToneSphere,  Id = "tone",     Display = "Tone Sphere",    Keys = 4,  Tracked = false, Adjustable = false, KeyHint = "D F J K（4 分位）", Removed = true },
            new ModeInfo { Mode = GameMode.Iidx,        Id = "iidx",     Display = "IIDX",           Keys = 8,  Tracked = true,  Adjustable = false, KeyHint = "S=转盘 · D F 空格 J K L ;" },
            new ModeInfo { Mode = GameMode.Pump,        Id = "pump",     Display = "Pump It Up",     Keys = 5,  Tracked = true,  Adjustable = false, KeyHint = "5 面板（↙↖中↘↗）", Removed = true },
            new ModeInfo { Mode = GameMode.LoopComposer, Id = "loopcompose", Display = "回环作曲",  Keys = 4,  Tracked = false, Adjustable = false, KeyHint = "D F J K（环上 4 分区）· 鼠标点环任意 16 分格", Editable = false },
        };

        static readonly Dictionary<GameMode, ModeInfo> _byMode = Build();
        static readonly Dictionary<string, ModeInfo> _byId = BuildId();

        static Dictionary<GameMode, ModeInfo> Build()
        {
            var d = new Dictionary<GameMode, ModeInfo>();
            foreach (var m in All) d[m.Mode] = m;
            return d;
        }
        static Dictionary<string, ModeInfo> BuildId()
        {
            var d = new Dictionary<string, ModeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in All) d[m.Id] = m;
            return d;
        }

        public static ModeInfo Of(GameMode mode)
            => _byMode.TryGetValue(mode, out var m) ? m : All[0];

        public static ModeInfo FromId(string id)
            => string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out var m) ? All[0] : m;

        public static string DisplayName(GameMode mode) => Of(mode).Display;
        public static string ModeId(GameMode mode) => Of(mode).Id;
        public static int KeyCount(GameMode mode) => Of(mode).Keys;
        public static bool IsTracked(GameMode mode) => Of(mode).Tracked;
        public static bool IsTrackless(GameMode mode) => !Of(mode).Tracked;
    }
}
