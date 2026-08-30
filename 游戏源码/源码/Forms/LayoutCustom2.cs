using System;
using System.Collections.Generic;

namespace ChartPlayer
{
    /// <summary>ModeDefaults 常量表：每模式默认值（= 当前各 Draw* 硬编码值；默认不落盘）。</summary>
    public static class LayoutDefaults
    {
        static ModeElem E(double x = 0.5, double y = 0.5, double p = double.NaN, double p2 = double.NaN, bool show = true)
            => new ModeElem { X = x, Y = y, P = p, P2 = p2, Show = show };

        public static ModeElem Get(string mode, string key)
        {
            var t = Table(mode);
            return t != null && t.TryGetValue(key, out var v) ? v : null;
        }

        static Dictionary<string, ModeElem> Table(string mode)
        {
            switch (mode)
            {
                case "mania": return new() {
                    ["field.hit"] = E(0.5, 0.82),
                    ["field.w"] = E(p: 1.0),
                    ["field.speed"] = E(p: 1.0),
                    ["lane.slant"] = E(p: double.NaN),
                    ["note.cam3d"] = E(show: false),
                    ["note.size"] = E(p: 1.0),
                    ["bg.dim"] = E(p: double.NaN),
                };
                case "phigros": return new() {
                    ["line.core.y"] = E(0.5, 0.5),
                    ["line.core.x"] = E(0.5, 0.5),
                    ["line.core.rot"] = E(p: 0),
                    ["field.speed"] = E(p: 1.0),
                    ["note.size"] = E(p: 1.0),
                    ["note.alpha"] = E(p: 1.0),
                };
                case "arcaea": return new() {
                    ["field.sky"] = E(0.5, 0.55),
                    ["field.ground"] = E(0.5, 0.82),
                    ["arc.tilt"] = E(p: double.NaN),
                    ["arc.width"] = E(p: 1.0),
                    ["arc.alpha"] = E(p: 1.0),
                    ["note.size"] = E(p: 1.0),
                };
                case "cytus": return new() {
                    ["ring.center"] = E(0.5, 0.5),
                    ["ring.radius"] = E(p: 1.0),
                    ["ring.thick"] = E(p: 1.0),
                    ["scan.alt"] = E(show: true),
                };
                case "osustd": return new() {
                    ["field.center"] = E(0.5, 0.5),
                    ["field.scale"] = E(p: 1.0),
                    ["field.aspect"] = E(show: true),
                    ["cursor.size"] = E(p: 1.0),
                };
                case "maimai": return new() {
                    ["ring.radius"] = E(p: 1.0),
                    ["ring.center"] = E(0.5, 0.5),
                    ["mid.radius"] = E(p: 1.0),
                    ["note.approach"] = E(p: 1.0),
                };
                case "iidx": return new() {
                    ["bucket.y"] = E(0.5, 0.82),
                    ["field.w"] = E(p: 1.0),
                    ["turntable.w"] = E(p: 1.0),
                    ["field.speed"] = E(p: 1.0),
                };
                case "adofai": return new() {
                    ["route.scale"] = E(p: 1.0),
                    ["route.rot"] = E(p: 0),
                    ["route.center"] = E(0.5, 0.5),
                };
                default: return MinimalTable();
            }
        }

        static Dictionary<string, ModeElem> MinimalTable() => new() {
            ["field.hit"] = E(0.5, 0.82),
            ["field.speed"] = E(p: 1.0),
            ["note.size"] = E(p: 1.0),
            ["note.alpha"] = E(p: 1.0),
        };
    }

    /// <summary>SkinSettings 所需：模式默认值查找（委托 LayoutDefaults）。</summary>
    public static class LayoutCustomDefaults
    {
        public static ModeElem ModeDefault(string mode, string key) => LayoutDefaults.Get(mode, key);
    }
}