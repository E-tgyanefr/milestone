using System;

namespace ChartPlayer
{
    /* ================= AiDevice：AI 加速器探测与路由（Npu > Gpu > Cpu） =================
     * 供 AI 功能路由（训练/推理/演示按设备选后端）：
     *  Npu = HardwareProbe.IsNpuPresent（处理器名启发式，见 HardwareProbe）；
     *  Gpu = RenderBackend（D2DRenderer 现有 DXGI GPU 名接口）；
     *  Cpu = 兜底。
     */

    /// <summary>AI 加速设备类型（首选优先级：Npu &gt; Gpu &gt; Cpu）。</summary>
    public enum AiDeviceKind
    {
        /// <summary>CPU（兜底）。</summary>
        Cpu,
        /// <summary>GPU（Direct2D/DXGI 硬件适配器）。</summary>
        Gpu,
        /// <summary>NPU（神经处理单元，启发式探测）。</summary>
        Npu,
    }

    /// <summary>AI 加速器：Probe() 探测当前设备，Preferred() 给出首选路由（Npu&gt;Gpu&gt;Cpu）。纯静态零依赖。</summary>
    public static class AiAccelerator
    {
        /// <summary>探测当前可用的 AI 加速设备（最高优先：NPU → GPU → CPU）。</summary>
        public static AiDeviceKind Probe()
        {
            try
            {
                var hw = HardwareProbe.Probe();
                if (hw.IsNpuPresent) return AiDeviceKind.Npu;
            }
            catch { }
            try
            {
                if (RenderBackend.IsHardware) return AiDeviceKind.Gpu;
            }
            catch { }
            return AiDeviceKind.Cpu;
        }

        /// <summary>首选 AI 加速设备（与 Probe 相同；语义供路由方调用）。</summary>
        public static AiDeviceKind Preferred() => Probe();

        /// <summary>设备类型显示名。</summary>
        public static string Name(AiDeviceKind kind)
        {
            switch (kind)
            {
                case AiDeviceKind.Npu: return "NPU";
                case AiDeviceKind.Gpu: return "GPU";
                default: return "CPU";
            }
        }

        /// <summary>人类可读描述（CPU 品牌 + NPU/GPU 名称）。</summary>
        public static string Describe()
        {
            var hw = HardwareProbe.Probe();
            string gpuName;
            try { gpuName = RenderBackend.GpuName; } catch { gpuName = "(未知)"; }
            return "AI 加速器首选: " + Name(Preferred())
                + " | CPU: " + (hw.CpuBrand.Length > 0 ? hw.CpuBrand : "(未知)")
                + (hw.IsNpuPresent ? " | NPU: " + hw.NpuName : "")
                + " | GPU: " + gpuName;
        }
    }
}
