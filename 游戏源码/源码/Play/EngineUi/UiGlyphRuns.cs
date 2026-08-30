using System;
using System.Collections.Generic;
using System.Text;

namespace ChartPlayer
{
    /* ================= UiGlyphRuns（t56）：引擎 UI 文本 run-split（emoji 真实字形） =================
     * 问题：引擎 UI 图标准确文本（UiText 原文，如 ✏🏆🌐🎬📂⚙🎯🤖📐👤📊📋🎨ℹ✖🎵🎮✨♪）用 Microsoft YaHei UI
     * 渲染成 □（该字体无 emoji 字形）；legacy（GDI+ WinForms）同一文本渲染为真实图标（字体回退链不同）。
     * 方案：按 Unicode 范围把字符串拆为 run——常规文本→Microsoft YaHei UI；emoji（U+1F000-U+1FAFF 四字节、
     * U+2600-U+27BF、U+2B00-U+2BFF，及变体选择符 FE0F / ZWJ 200D 并入 emoji run）→Segoe UI Emoji；
     * 逐 run 同字号/同基线绘制（monochrome 字形，前景色不变；不做彩色位图）。
     * 只改字体回退选择；不改 UiText 文本、不改布局坐标。MeasureText 按同样 run 拆分测量（宽度一致）。
     */

    /// <summary>文本 run：连续同类字符段。</summary>
    public sealed class GlyphRun
    {
        /// <summary>该 run 的原文子串。</summary>
        public string Text = "";
        /// <summary>true=emoji/符号 run（用 Segoe UI Emoji）；false=常规（Microsoft YaHei UI）。</summary>
        public bool Emoji;
        public override string ToString() => (Emoji ? "[emoji] " : "[text] ") + Text;
    }

    public static class UiGlyphRuns
    {
        /// <summary>默认 UI 字体族（与既有渲染一致）。</summary>
        public const string DefaultFont = "Microsoft YaHei UI";
        /// <summary>emoji/符号字体族（Segoe UI Emoji，monochrome 字形由 GDI+/DirectWrite 以前景色绘制）。</summary>
        public const string EmojiFont = "Segoe UI Emoji";

        static readonly List<GlyphRun> _empty = new List<GlyphRun>(0);

        // t9：emoji 文本拆分结果缓存（有界）。引擎壳菜单/按钮标签每帧重复测量/绘制同一文本，
        // 缓存后稳态零分配；动态文本（计数/时间）偶尔换键，超限整表清空保持有界。
        static readonly object _splitLock = new object();
        static readonly Dictionary<string, List<GlyphRun>> _splitCache = new Dictionary<string, List<GlyphRun>>(StringComparer.Ordinal);
        const int SplitCacheLimit = 256;

        static List<GlyphRun> CachedSplit(string s)
        {
            lock (_splitLock)
            {
                if (_splitCache.TryGetValue(s, out var cached)) return cached;
                if (_splitCache.Count >= SplitCacheLimit) _splitCache.Clear();
                var built = BuildSplit(s);
                _splitCache[s] = built;
                return built;
            }
        }

        /// <summary>字符串是否含 emoji/符号码点（t9：MeasureText/Text 每帧热路径快速判断——
        /// 纯文本直接单字体绘制/测量，跳过 Split 的 List+GlyphRun 分配）。</summary>
        public static bool ContainsEmoji(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length;)
            {
                int cp = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    i += 2;
                }
                else i += 1;
                if (IsEmojiCodePoint(cp)) return true;
            }
            return false;
        }

        /// <summary>是否是 emoji/符号码点（含代理对组合后的完整码点）。</summary>
        public static bool IsEmojiCodePoint(int cp)
            => (cp >= 0x1F000 && cp <= 0x1FAFF)      // 四字节 emoji（🎮1F3AE 🎬1F3AC 🎨1F3A8 🏆1F3C6 📂1F4C2 📋1F4CB 🎵1F3B5 🌐1F310 🤖1F916 等）
            || (cp >= 0x2600 && cp <= 0x27BF)        // 杂项符号/装饰（✏270F ✖2716 ✨2728 ⚙2699 ♪266A ⚡26A1 🎁1F381?-否；本区含音符/星座等）
            || (cp >= 0x2B00 && cp <= 0x2BFF)        // 箭头/杂项符号扩展
            || (cp >= 0x1F1E6 && cp <= 0x1F1FF)      // 地区指示符（国旗）
            || cp == 0xFE0F                          // 变体选择符（并入 emoji run，避免拆散序列）
            || cp == 0x200D;                         // ZWJ 零宽连接符（多编码 emoji 序列）

        /// <summary>
        /// 拆分字符串为 run 序列（相邻同类合并；FE0F/200D 并入前一 emoji run，否则自成 emoji run）。
        /// 代理对按完整码点分类；非法/残代理按普通字符处理。
        /// </summary>
        /// <summary>拆分（含缓存：重复文本零分配）。调用方只读使用返回的 run 列表（GlyphRun 为共享对象，勿修改）。</summary>
        public static List<GlyphRun> Split(string s)
        {
            if (string.IsNullOrEmpty(s)) return _empty;

            // t9：纯文本快速路径（共享空列表，零分配；绘制/测量方用 ContainsEmoji 判断后直接整段单字体渲染）
            if (!ContainsEmoji(s)) return _empty;

            return CachedSplit(s);
        }

        static List<GlyphRun> BuildSplit(string s)
        {
            var list = new List<GlyphRun>(2);
            var sb = new StringBuilder();          // t9：run 文本用 StringBuilder 累积（原逐码点字符串拼接为 O(n²) 分配）
            for (int i = 0; i < s.Length;)
            {
                int cp = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    i += 2;
                }
                else i += 1;

                bool emoji = IsEmojiCodePoint(cp);
                // FE0F/200D 已计入 IsEmojiCodePoint：跟在 emoji 后自然并入其 run（不拆散序列）；单独出现自成 emoji run
                var run = list.Count > 0 && list[list.Count - 1].Emoji == emoji
                    ? list[list.Count - 1] : null;
                if (run == null)
                {
                    if (list.Count > 0 && sb.Length > 0) { list[list.Count - 1].Text = sb.ToString(); sb.Clear(); }
                    run = new GlyphRun { Emoji = emoji };
                    list.Add(run);
                }
                sb.Append(char.ConvertFromUtf32(cp));
            }
            if (sb.Length > 0) list[list.Count - 1].Text = sb.ToString();
            return list;
        }

        /// <summary>清空拆分缓存（主题/字体策略变化或诊断时调用；缓存有界通常无需手动清）。</summary>
        public static void ClearCache()
        {
            lock (_splitLock) _splitCache.Clear();
        }

        /// <summary>t34：emoji run 推进额外间距（宿主按当前 letterbox scale 设置：scale&lt;1 时 =2，否则 0）。
        /// 800 极窄档（scale 0.625）下图标-文字 gap 实测 4-5px 低于 6px 下限 → 窄档 +2px 兜底；
        /// 测量（MeasureText）与推进（Text）同读此值，三端口径不变。</summary>
        public static double EmojiGapBoost;

        /// <summary>单段文本的字体族（run-split 绘制/测量用）。</summary>
        public static string FontFor(bool emoji) => emoji ? EmojiFont : DefaultFont;

        /// <summary>码点数（代理对按 1 计；emoji 估宽用）。</summary>
        public static int CodePointCount(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length;)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i += 2;
                else i += 1;
                n++;
            }
            return n;
        }

        /// <summary>run 序列测量统一口径：全部委托后端 d.MeasureText——后端（Gdi/D2DDrawAdapter，t31）
        /// 按 run 字体测真实 advance（emoji=Segoe UI Emoji 实测 ≈2.33em）+ 6px 保底间距；
        /// 测量=推进=绘制三端一致（居中/Ellipsis/AutoShrink 数学不变）。</summary>
        public static double MeasureRuns(IUiDraw d, IReadOnlyList<GlyphRun> runs, double size)
        {
            double w = 0;
            double sz = Math.Max(1, size);
            foreach (var run in runs)
                w += d != null ? d.MeasureText(run.Text, sz) : 0;
            return w;
        }
    }
}
