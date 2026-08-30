using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ChartPlayer
{
    /* ================= AiChartReview：制谱助检结果与规则回退（本地 AI 服务已禁用，t12） =================
     * 用户决策：程序【不调用本地 AI】—— 不对 Ollama / llama-server / LM Studio 等本地服务发起任何
     * 请求（HTTP / 启动探测 / 后台轮询）。
     * 本类保留全部公开签名（ReviewFileAsync / ReviewMetaAsync / ReviewChartTextAsync 及其同步包装），
     * 但 LocalAiEnabled=false：所有入口不再发起任何网络调用，直接返回
     * 「本地 AI 未启用（规则检查仍可用）」+ 规则回退文本（BuildDisabledText）。
     * 规则检查（ChartValidator）由 ChartAiAssistant.RunAiCheck 提供，完全不受影响。
     * 加速器标签（AiAccelerator.Preferred：NPU>GPU>CPU）仅为硬件探测结果展示，不涉及任何服务调用。
     */

    /// <summary>制谱助检结果（本地 AI 禁用后：Ok=false / Offline=true / Text=提示+规则文本）。</summary>
    public class AiReviewResult
    {
        /// <summary>是否拿到模型的有效建议文本（本地 AI 禁用后恒为 false）。</summary>
        public bool Ok;
        /// <summary>是否回退（本地 AI 禁用后恒为 true）。</summary>
        public bool Offline;
        /// <summary>展示文本（本地 AI 禁用后 = 提示文案 + 规则回退文本）。</summary>
        public string Text = "";
        /// <summary>按行拆出的建议列表（供 UI 逐条显示）。</summary>
        public List<string> Suggestions = new List<string>();
        /// <summary>回退原因（Offline=true 时有效）。</summary>
        public string Error = "";
        /// <summary>本次调用耗时（ms；禁用路径恒为 0）。</summary>
        public long ElapsedMs;
        /// <summary>调试/记录用 prompt（禁用路径不使用）。</summary>
        public string Prompt = "";
        /// <summary>加速器标签（NPU/GPU/CPU，硬件探测）。</summary>
        public string Accelerator = "CPU";
    }

    public static class AiChartReview
    {
        /// <summary>本地 AI 服务是否启用。用户决策（t12）：禁用 —— 程序不调用 Ollama / llama-server /
        /// LM Studio 等本地服务（不发起 HTTP 请求、不探测端口、不后台轮询）。规则检查仍可用。</summary>
        public const bool LocalAiEnabled = false;

        /// <summary>禁用时向 UI 展示的统一提示文案。</summary>
        public const string DisabledNotice = "本地 AI 未启用（规则检查仍可用）";

        /// <summary>超时参数（历史签名兼容保留；禁用路径不发起调用，恒不超时）。</summary>
        public const int DefaultTimeoutMs = 120000;

        /// <summary>直读谱面文本截断长度（~4000 字符；供 BuildPromptFromChartText 使用）。</summary>
        public const int MaxPromptChars = 4000;

        /* ================= Prompt 组装（纯字符串函数，不发起任何请求） ================= */

        /// <summary>直读谱面：谱面文件文本 → 中文 prompt（截断 ~4000 字符）。纯字符串函数，保留供外部调试。</summary>
        public static string BuildPromptFromChartText(string chartText)
        {
            string src = chartText ?? "";
            if (src.Length > MaxPromptChars)
                src = src.Substring(0, MaxPromptChars) + "\n……（谱面较长，已截断至 " + MaxPromptChars + " 字符）";
            return "你是资深音游制谱审查助手。下面是某个谱面文件的原始文本（可能已截断）。" +
                   "请站在制谱者角度审查，给出 3~5 条具体、可执行的中文制谱建议（对音/结构/难度均可），" +
                   "每条一行，以「建议：」开头。不要客套，不要重复谱面内容，不要输出其他内容。\n\n" +
                   "——— 谱面文本 ———\n" + src;
        }

        /// <summary>元数据 + 校验问题列表 → 中文 prompt。纯字符串函数，保留供外部调试。</summary>
        public static string BuildPromptFromMeta(string title, string mode, string version,
            double bpm, int noteCount, IReadOnlyList<string> issues)
        {
            var sb = new StringBuilder();
            sb.Append("你是资深音游制谱审查助手。请基于以下谱面信息与程序校验结果，给出 3~5 条具体、可执行的中文制谱建议" +
                      "（对音/结构/难度均可），每条一行，以「建议：」开头。不要客套，不要重复信息。\n\n");
            sb.Append("【谱面信息】标题:").Append(title ?? "").Append("；模式:").Append(mode ?? "")
              .Append("；难度:").Append(version ?? "").Append("；BPM:").Append(bpm.ToString("0.##"))
              .Append("；音符数:").Append(noteCount).Append("。\n");
            if (issues != null && issues.Count > 0)
            {
                sb.Append("【校验问题】共 ").Append(issues.Count).Append(" 项：\n");
                int shown = 0;
                foreach (var it in issues)
                {
                    if (string.IsNullOrEmpty(it)) continue;
                    if (shown >= 30) { sb.Append("……（余下 " + (issues.Count - shown) + " 项省略）\n"); break; }
                    sb.Append("· ").Append(it).Append('\n');
                    shown++;
                }
            }
            else
            {
                sb.Append("【校验问题】无（或未运行规则校验）。\n");
            }
            return sb.ToString();
        }

        /* ================= 主入口（本地 AI 禁用：全部直接回退，零网络调用） ================= */

        /// <summary>直读谱面文件审查：本地 AI 已禁用 —— 不读取文件/不发起任何请求，直接返回提示 + 规则说明。</summary>
        public static async Task<AiReviewResult> ReviewFileAsync(string path, int timeoutMs = DefaultTimeoutMs)
        {
            await Task.CompletedTask;   // 保持异步签名（参数为兼容保留）；禁用路径无任何 I/O / 网络
            return BuildDisabled(null);
        }

        /// <summary>同步包装（禁用路径：零网络调用）。</summary>
        public static AiReviewResult ReviewFile(string path, int timeoutMs = DefaultTimeoutMs)
            => Task.Run(() => ReviewFileAsync(path, timeoutMs)).GetAwaiter().GetResult();

        /// <summary>谱面元数据 + 校验问题列表审查：本地 AI 已禁用 —— 直接返回提示 + 规则问题文本。</summary>
        public static async Task<AiReviewResult> ReviewMetaAsync(string title, string mode, string version,
            double bpm, int noteCount, IReadOnlyList<string> issues, int timeoutMs = DefaultTimeoutMs)
        {
            await Task.CompletedTask;   // 禁用路径：零网络调用（title/mode/... 参数兼容保留）
            return BuildDisabled(issues);
        }

        /// <summary>同步包装（禁用路径：零网络调用）。</summary>
        public static AiReviewResult ReviewMeta(string title, string mode, string version,
            double bpm, int noteCount, IReadOnlyList<string> issues, int timeoutMs = DefaultTimeoutMs)
            => Task.Run(() => ReviewMetaAsync(title, mode, version, bpm, noteCount, issues, timeoutMs)).GetAwaiter().GetResult();

        /// <summary>直接给定谱面文本审查：本地 AI 已禁用 —— 直接返回提示 + 规则说明。</summary>
        public static async Task<AiReviewResult> ReviewChartTextAsync(string chartText, int timeoutMs = DefaultTimeoutMs)
        {
            await Task.CompletedTask;   // 禁用路径：零网络调用
            return BuildDisabled(null);
        }

        /* ================= 内部：禁用回退（零网络） ================= */

        /// <summary>本地 AI 禁用回退：提示文案 + 规则问题文本（不发起任何请求）。</summary>
        static AiReviewResult BuildDisabled(IReadOnlyList<string> issues)
        {
            var r = new AiReviewResult
            {
                Ok = false,
                Offline = true,
                Error = DisabledNotice,
                Accelerator = SafeAccelerator(),
                ElapsedMs = 0,
                Text = BuildDisabledText(issues),
            };
            foreach (var s in SplitSuggestions(r.Text)) r.Suggestions.Add(s);
            return r;
        }

        /// <summary>禁用提示 + 规则文本（供 UI 直接展示/落盘）。</summary>
        public static string BuildDisabledText(IReadOnlyList<string> issues)
        {
            var sb = new StringBuilder();
            sb.Append(DisabledNotice).Append('\n');
            sb.Append("程序已禁用全部本地 AI 服务调用（Ollama / llama-server / LM Studio 等），不会发起任何网络请求。\n");
            if (issues == null || issues.Count == 0)
            {
                sb.Append("未运行规则校验。可先手动检查：音符时间是否乱序/同轨重叠、长条尾是否大于头、" +
                          "键位是否越界（列 > 键数）、BPM 是否在 30~400、Phigros 事件 alpha 是否在 [0,1]、speed 是否 > 0。");
            }
            else
            {
                sb.Append("规则检查问题（逐项处理）：\n");
                foreach (var it in issues)
                    if (!string.IsNullOrEmpty(it)) sb.Append("· ").Append(it).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>按行拆分建议（取非空行，最多 5 条）。</summary>
        static List<string> SplitSuggestions(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var line in text.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("\u0060\u0060\u0060")) continue;
                list.Add(t);
                if (list.Count >= 5) break;
            }
            return list;
        }

        /// <summary>规则回退文本（保留：纯字符串函数，历史调用方仍可用）。</summary>
        public static string BuildFallbackText(string reason, IReadOnlyList<string> issues)
        {
            var sb = new StringBuilder();
            sb.Append("【AI 离线】").Append(reason ?? "").Append("\n");
            if (issues == null || issues.Count == 0)
            {
                sb.Append("未运行规则校验。可先手动检查：音符时间是否乱序/同轨重叠、长条尾是否大于头、" +
                          "键位是否越界（列 > 键数）、BPM 是否在 30~400、Phigros 事件 alpha 是否在 [0,1]、speed 是否 > 0。");
            }
            else
            {
                sb.Append("规则检查问题（逐项处理）：\n");
                foreach (var it in issues)
                    if (!string.IsNullOrEmpty(it)) sb.Append("· ").Append(it).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>加速器标签（AiAccelerator：NPU>GPU>CPU，仅硬件探测；任何失败回退 "CPU"）。</summary>
        static string SafeAccelerator()
        {
            try { return AiAccelerator.Name(AiAccelerator.Preferred()); }
            catch { return "CPU"; }
        }
    }
}
