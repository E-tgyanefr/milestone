using System;
using System.Collections.Generic;
using System.Linq;

namespace ChartPlayer
{
    /// <summary>t63 布局自定义：模式专属布局元素（per-mode 覆盖句柄）。
    /// NaN=未覆盖（P/P2 用）；Show 用于 Toggle 类。X/Y=归一化位置（0..1）。</summary>
    public class ModeElem
    {
        public double X { get; set; } = 0.5;
        public double Y { get; set; } = 0.5;
        public bool Show { get; set; } = true;
        public double P { get; set; } = double.NaN;
        public double P2 { get; set; } = double.NaN;
    }

    /// <summary>元素语义定义（供编辑器 UI 动态渲染 + 取值范围约束）。</summary>
    public enum ElemKind { PosXY, PosX, PosY, ScalarP, ScalarP2, Toggle }

    public record ModeElemDef(string Key, string CN, string Unit, double Min, double Max, double Step, ElemKind Kind);

    /// <summary>t63 布局自定义：每模式元素注册表 + 内置默认常量表 + 继承链读取（含热路径缓存）。
    /// 设计：编辑指南/文档精选/协作/layout-custom-design.md（D1-D8；默认不落盘；零引擎改动）。</summary>
    public static class LayoutCustom
    {
        // ===== 每模式元素注册表（UI 用；HUD 组由 SkinSettings.DefaultLayout 提供，不在此注册） =====
        public static readonly Dictionary<string, List<ModeElemDef>> ModeElements = new()
        {
            ["mania"] = new() {
                new("field.hit", "判定线位置", "Y", 0.05, 0.95, 0.005, ElemKind.PosY),
                new("field.top", "生成区顶", "Y", 0.08, 0.30, 0.005, ElemKind.PosY),
                new("field.w", "轨道宽倍率", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
                new("field.speed", "流速倍率", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
                new("lane.slant", "斜轨强度", "", 0, 1, 0.01, ElemKind.ScalarP),
                new("note.cam3d", "3D 相机", "", 0, 1, 1, ElemKind.Toggle),
                new("note.size", "音符尺寸", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
                new("bg.dim", "背景淡化", "", 0, 1, 0.01, ElemKind.ScalarP),
            },
            ["phigros"] = new() {
                new("line.core.y", "判线基准 Y", "Y", 0.02, 0.98, 0.005, ElemKind.PosY),
                new("line.core.x", "判线基准 X", "X", 0.02, 0.98, 0.005, ElemKind.PosX),
                new("line.core.rot", "判线旋转", "°", -180, 180, 1, ElemKind.ScalarP),
                new("field.speed", "流速倍率", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
                new("note.size", "音符尺寸", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
                new("note.alpha", "音符透明度", "", 0.3, 1.0, 0.01, ElemKind.ScalarP),
            },
            ["arcaea"] = new() {
                new("field.sky", "天线位置", "Y", 0.12, 0.88, 0.005, ElemKind.PosY),
                new("field.ground", "地线位置", "Y", 0.5, 0.95, 0.005, ElemKind.PosY),
                new("arc.tilt", "斜轨强度", "", 0, 1, 0.01, ElemKind.ScalarP),
                new("arc.width", "arc 线宽", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
                new("arc.alpha", "arc 透明度", "", 0.3, 1.0, 0.01, ElemKind.ScalarP),
                new("note.size", "音符尺寸", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
            },
            ["cytus"] = new() {
                new("ring.center", "扫描场中心", "", 0.02, 0.98, 0.005, ElemKind.PosXY),
                new("ring.radius", "扫描环半径", "×", 0.3, 1.0, 0.01, ElemKind.ScalarP),
                new("ring.thick", "环厚", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
                new("scan.alt", "上下页交替", "", 0, 1, 1, ElemKind.Toggle),
            },
            ["osustd"] = new() {
                new("field.center", "场中心", "", 0.02, 0.98, 0.005, ElemKind.PosXY),
                new("field.scale", "场缩放", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
                new("field.aspect", "锁定 4:3", "", 0, 1, 1, ElemKind.Toggle),
                new("cursor.size", "光标倍率", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
            },
            ["maimai"] = new() {
                new("ring.radius", "外圈半径", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
                new("ring.center", "环心", "", 0.02, 0.98, 0.005, ElemKind.PosXY),
                new("mid.radius", "中央五角半径", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
                new("note.approach", "接近时间", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
            },
            ["iidx"] = new() {
                new("bucket.y", "判定桶位置", "Y", 0.5, 0.95, 0.005, ElemKind.PosY),
                new("field.top", "生成区顶", "Y", 0.08, 0.30, 0.005, ElemKind.PosY),
                new("field.w", "轨道宽倍率", "×", 0.6, 1.3, 0.01, ElemKind.ScalarP),
                new("turntable.w", "转盘宽倍率", "×", 0.7, 1.6, 0.01, ElemKind.ScalarP),
                new("field.speed", "流速倍率", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
            },
            ["adofai"] = new() {
                new("route.scale", "tile 段长", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
                new("route.rot", "全局旋转", "°", -180, 180, 1, ElemKind.ScalarP),
                new("route.center", "路线中心", "", 0.02, 0.98, 0.005, ElemKind.PosXY),
            },
        };

        public static List<ModeElemDef> For(string modeId)
            => ModeElements.TryGetValue(modeId, out var l) ? l : new List<ModeElemDef>();

        public static bool IsRegistered(string modeId) => ModeElements.ContainsKey(modeId);

        /// <summary>最小集（taiko/catch/loopcompose 等）：保证「所有玩法都可更改」底线。</summary>
        public static List<ModeElemDef> Minimal() => new() {
            new("field.hit", "判定线位置", "Y", 0.05, 0.95, 0.005, ElemKind.PosY),
            new("field.speed", "流速倍率", "×", 0.5, 2.0, 0.01, ElemKind.ScalarP),
            new("note.size", "音符尺寸", "×", 0.5, 1.5, 0.01, ElemKind.ScalarP),
            new("note.alpha", "音符透明度", "", 0.3, 1.0, 0.01, ElemKind.ScalarP),
        };
    }
}