using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 启动 GPU 保护（防"打开程序电脑就卡死"）：
    /// 本地 AI 服务（llama-server / Ollama / LM Studio 等）常把大模型整块塞进显存（-ngl 99 → 10.9GB@12GB VRAM），
    /// 此时游戏再创建 Direct2D 硬件目标会把驱动推到 OOM 边缘 → NVIDIA 报错 / TDR / 全机冻结。
    /// 检测到此类进程正在占用大量内存时：自动置 GameSettings.ForceWarp（游戏走 WARP 软件渲染，稳定可玩），
    /// 并弹窗提示用户关闭 AI 服务可恢复硬件加速。
    /// </summary>
    public static class GpuGuard
    {
        public static bool Warned;

        // 危险名单：整卡（-ngl 99）占满显存的服务才是“打开程序冻结”元凶（llama.cpp / LM Studio 默认装填全体层）；
        // Ollama 走官方管理（模型按需加载、自动卸载，不会整卡常驻）——**放行**（t12 起程序已禁用本地 AI 服务调用；用户自行运行的本地 AI 服务不应触发 WARP）。
        static readonly string[] DangerousServers =
        {
            "llama-server", "lmstudio", "lm-studio", "koboldcpp", "text-generation-ui", "textgen", "sillytavern", "open-webui"
        };

        /// <summary>是否存在正在占用大量内存/显存的**危险**本地 AI 服务进程（Ollama 放行）。</summary>
        public static bool AIServerHogging()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    string n = p.ProcessName ?? "";
                    bool hit = false;
                    foreach (var nm in DangerousServers)
                        if (n.IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    if (!hit) continue;
                    try { if (p.WorkingSet64 > 1_500_000_000L) return true; } catch { }
                }
            }
            catch { }
            return false;
        }

        /// <summary>进入 AI 演示/AI 陪玩会话：检测到本地 AI 服务占显存 → 本会话游戏临时软件渲染（GPU 让给 AI 服务，防驱动 OOM 冻结）；未占用则常态硬件渲染不动。用户决策 08-26：常态也调用 GPU，仅会话内避让。</summary>
        public static bool BeginAiSession()
        {
            Warned = true;
            if (!AIServerHogging()) return false;
            GameSettings.ForceWarp = true;
            try
            {
                Logger.Warn("GPU 保护：AI 演示/陪玩会话中检测到本地 AI 服务占用大量显存（如 llama-server -ngl 99 整卡）——本会话游戏临时切换软件渲染（WARP），GPU 让给 AI 服务，防驱动 OOM 冻结。");
            }
            catch { }
            return true;
        }

        /// <summary>离开 AI 演示/陪玩会话：恢复常态硬件 GPU 渲染（调用方随后应请求渲染目标重建）。</summary>
        public static void EndAiSession()
        {
            GameSettings.ForceWarp = false;
            try { Logger.Info("GPU 保护会话结束：恢复硬件渲染。"); } catch { }
        }
    }
}
