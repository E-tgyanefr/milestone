using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;

namespace ChartPlayer
{
    /// <summary>
    /// 渲染后端探测（引擎化痕迹）：枚举 DXGI 硬件适配器获取 GPU 名称；
    /// 无硬件加速时回退 WARP（软件光栅化）。
    /// </summary>
    public static class RenderBackend
    {
        static string _gpu;
        static bool _hardware;
        public static bool IsHardware => _hardware || !GpuName.Contains("WARP");
        public static string GpuName
        {
            get
            {
                if (_gpu != null) return _gpu;
                try
                {
                    using var factory = new SharpDX.DXGI.Factory1();
                    for (int i = 0; i < factory.GetAdapterCount(); i++)
                    {
                        using var adapter = factory.GetAdapter(i);
                        string name = adapter.Description.Description.Trim();
                        if (name.Length > 0 && !name.Contains("Basic Render"))   // 跳过 Microsoft 基础渲染/软件
                        {
                            _gpu = name;
                            _hardware = true;
                            return _gpu;
                        }
                    }
                    _gpu = "WARP（软件光栅化）";
                    _hardware = false;
                }
                catch
                {
                    _gpu = "WARP（软件光栅化）";
                    _hardware = false;
                }
                return _gpu;
            }
        }
        public static string Describe => "Direct2D1 · " + (IsHardware ? "GPU: " + GpuName : "软件光栅化（WARP）");
    }

    /// <summary>D2D GPU 位图（由 GDI+ Image 转换，预乘 Alpha）。</summary>
    public sealed class D2DBitmap : IDisposable
    {
        public SharpDX.Direct2D1.Bitmap Bmp;
        public int Width, Height;
        public void Dispose() { Bmp?.Dispose(); Bmp = null; }
    }

    /// <summary>
    /// Direct2D 硬件加速渲染器（GPU）——渲染后端默认实现（IRenderer）。
    ///  - 文字按窗口 DPI 缩放：与 WinForms 字号一致，高 DPI 下不再偏小/错位
    ///  - 画刷缓存：避免每帧反复创建对象，帧率稳定
    ///  - 支持居中文本、文本测量、斜四边形（斜轨）、虚线/点线、位图绘制
    /// </summary>
    public sealed class D2DRenderer : IDisposable, IRenderer
    {
        readonly SharpDX.Direct2D1.Factory _d2dFactory;
        readonly SharpDX.DirectWrite.Factory _dwFactory;
        WindowRenderTarget _rtWindow;        // 窗口渲染目标（最终呈现）
        RenderTarget _rt;                    // 当前绘制目标（窗口 或 超采样离屏）
        readonly Dictionary<int, SolidColorBrush> _brushes = new Dictionary<int, SolidColorBrush>();
        readonly List<int> _brushOrder = new List<int>();
        const int MaxBrushes = 64;
        readonly float _dpiScale;
        int _w, _h;
        bool _disposed;
        IntPtr _hwnd;
        bool _recreatePending;   // D2DERR_RECREATE_TARGET：需要重建渲染目标

        /// <summary>是否应用高 DPI 全画布缩放适配（默认 true）。编辑器画布关闭此功能以保持 1:1 坐标（其命中检测未做逆变换）。</summary>
        public bool FitEnabled = true;

        /// <summary>超采样因子（>1 时渲染到窗口×SS 离屏再放大——GPU 满载 + 抗锯齿；1=直绘）。</summary>
        public float SuperSample = 1f;

        /// <summary>乒乓放大级数（超采样时每级 = 一次全屏 GPU 填充；级数越高 GPU 占用越高）。</summary>
        public int SuperSampleStages = 2;

        TextFormat _tfSmall, _tfFont, _tfBig, _tfCombo;
        TextFormat _tfSmallC, _tfFontC, _tfBigC, _tfComboC;
        readonly Dictionary<string, TextFormat> _tfFamilyCache = new Dictionary<string, TextFormat>();   // t56：非默认字体族（Segoe UI Emoji）按 族|档|居中 缓存
        StrokeStyle _solid, _dash, _dot;

        const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public D2DRenderer(IntPtr hwnd, int width, int height)
        {
            // 多线程工厂：允许后台线程并发创建 D2D 资源（位图解码等），渲染本身由 Direct2D 硬件加速（GPU）
            _d2dFactory = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded);
            _dwFactory = new SharpDX.DirectWrite.Factory();

            float s = 1f;
            try
            {
                uint d = GetDpiForWindow(hwnd);
                if (d >= 72 && d <= 480) s = d / 96f;
            }
            catch { }
            _dpiScale = s;

            _tfSmall = Make(10f, false); _tfFont = Make(13f, false);
            _tfBig = Make(26f, false); _tfCombo = Make(44f, false);
            _tfSmallC = Make(10f, true); _tfFontC = Make(13f, true);
            _tfBigC = Make(26f, true); _tfComboC = Make(44f, true);

            _solid = MakeStyle(DashStyle.Solid);
            _dash = MakeStyle(DashStyle.Dash);
            _dot = MakeStyle(DashStyle.Dot);

            _w = Math.Max(1, width);
            _h = Math.Max(1, height);
            CreateTarget(hwnd, _w, _h);
        }

        // SharpDX 4.2 的 StrokeStyle 构造对 null 虚线数组存在空引用缺陷：
        // 传入空数组并加防御，失败时返回 null（绘制降级为普通实线）。
        StrokeStyle MakeStyle(DashStyle dash)
        {
            try
            {
                return new StrokeStyle(_d2dFactory, new StrokeStyleProperties
                {
                    DashStyle = dash,
                    DashCap = CapStyle.Round,
                    StartCap = CapStyle.Round,
                    EndCap = CapStyle.Round
                }, new float[0]);
            }
            catch { return null; }
        }

        TextFormat Make(float size, bool center)
        {
            var tf = new TextFormat(_dwFactory, "Microsoft YaHei UI", size * _dpiScale);
            tf.ParagraphAlignment = ParagraphAlignment.Center;
            if (center) tf.TextAlignment = TextAlignment.Center;
            tf.WordWrapping = WordWrapping.NoWrap;
            return tf;
        }

        void CreateTarget(IntPtr hwnd, int width, int height)
        {
            ClearGradCache();   // 渐变 brush 绑定旧渲染目标，重建前必须释放
            _offBitmap?.Dispose(); _offBitmap = null;
            _offScreen?.Dispose(); _offScreen = null;
            _offBitmap2?.Dispose(); _offBitmap2 = null;
            _offScreen2?.Dispose(); _offScreen2 = null;
            _rtWindow?.Dispose();
            _hwnd = hwnd;
            // 显式硬件加速：先尝试 RenderTargetType.Hardware（GPU 渲染）；仅当硬件不可用（远程桌面/虚拟机/驱动异常）
            // 才回退 Default（D2D 自动含 WARP 软件光栅化）——避免静默降级到软件渲染。
            // GPU 保护：ForceWarp=true（检测到其他程序占满显存/AI 服务吞 GPU）时跳过硬件尝试——
            // 硬件目标创建本身可能在驱动 OOM 边缘挂起（TDR），直接软件渲染以避免冻结系统。
            if (!GameSettings.ForceWarp)
            try
            {
                _rtWindow = new WindowRenderTarget(_d2dFactory,
                    new RenderTargetProperties
                    {
                        Type = RenderTargetType.Hardware,
                        PixelFormat = new SharpDX.Direct2D1.PixelFormat(Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Ignore),
                        DpiX = 96, DpiY = 96
                    },
                    new HwndRenderTargetProperties
                    {
                        Hwnd = hwnd,
                        PixelSize = new SharpDX.Size2(width, height),
                        // RetainContents 防闪烁 + Immediately 立即呈现(禁用 vsync)——帧率不被显示器刷新率锁死,可破千
                        PresentOptions = PresentOptions.RetainContents | PresentOptions.Immediately
                    });
            }
            catch (Exception ex)
            {
                Logger.Error("D2D 硬件渲染目标创建失败，回退 WARP：" + ex.Message);
            }
            if (_rtWindow == null)
            {
                // 硬件不可用或被 GPU 保护跳过 → 回退 Default（软件光栅化 WARP），保证功能可用且不冻结系统
                _rtWindow = new WindowRenderTarget(_d2dFactory,
                    new RenderTargetProperties
                    {
                        PixelFormat = new SharpDX.Direct2D1.PixelFormat(Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Ignore),
                        DpiX = 96, DpiY = 96
                    },
                    new HwndRenderTargetProperties
                    {
                        Hwnd = hwnd,
                        PixelSize = new SharpDX.Size2(width, height),
                        PresentOptions = PresentOptions.RetainContents | PresentOptions.Immediately
                    });
                Logger.Info("D2D 渲染目标已回退创建完成（软件渲染 WARP）");
            }
            _rt = _rtWindow;
            _rt.AntialiasMode = AntialiasMode.PerPrimitive;
            _rt.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Grayscale;
            _recreatePending = false;
        }

        public void Resize(int width, int height)
        {
            if (_rt == null) return;
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (_recreatePending)
            {
                try { CreateTarget(_hwnd, width, height); } catch { return; }
                _w = width; _h = height;
                return;
            }
            if (_w == width && _h == height) return;
            _w = width;
            _h = height;
            try { _rtWindow.Resize(new SharpDX.Size2(width, height)); } catch { }
            if (SuperSample > 1f)   // 窗口尺寸变化：离屏需重建
            {
                _offScreen?.Dispose(); _offScreen = null;
                _offBitmap?.Dispose(); _offBitmap = null;
                _offScreen2?.Dispose(); _offScreen2 = null;
                _offBitmap2?.Dispose(); _offBitmap2 = null;
            }
        }

        /// <summary>开始绘制；渲染目标失效（设备丢失）时自动重建。返回 false 表示本帧跳过。
        /// 超采样模式(SuperSample>1)：绘制到 窗口×SS 的离屏 BitmapRenderTarget（GPU 负载 ×SS²），提交时放大到窗口。</summary>
        /// <summary>GPU 保护会话切换（ForceWarp 变化）后强制重建渲染目标（硬件↔WARP）。</summary>
        public void MarkRecreate() { _recreatePending = true; }

        public bool Begin()
        {
            if (_rt == null) return false;
            if (_recreatePending)
            {
                try { CreateTarget(_hwnd, _w, _h); }
                catch { return false; }
            }
            try
            {
                SyncFpsEma();
                float effSs = EffSs();
                if (effSs > 1f)
                {
                    EnsureOffscreen();
                    if (_offScreen == null) return false;
                    _rt = _offScreen;                       // 绘制目标切到离屏（超采样）
                    _offScreen.BeginDraw();
                    _offScreen.Transform = SharpDX.Matrix3x2.Scaling(effSs);
                    return true;
                }
                _rtWindow.BeginDraw();
                _rt = _rtWindow;
                ApplyFitTransform();
                return true;
            }
            catch { return false; }
        }

        /// <summary>结束绘制并提交：超采样时把离屏位图放大绘制到窗口渲染目标。
        /// 乒乓放大链：内容画到离屏A（SS 倍），然后 A→B→A→B… 逐级全屏放大（每级 = 一次 GPU 全屏填充，
        /// SuperSampleStages 级联把 GPU 占满），最后缩小到窗口。
        /// t6 自适应级数：链长按实测耗时自动收敛（目标 ≤0.35ms/帧）——快机器自动降到命中 1000 FPS，
        /// 慢机器保持满链 GPU 饱和；CHART_SS_STAGES 环境变量仍可强制（≥0 时用固定值）。</summary>
        int _adaptiveStages = -1;   // -1=未初始化（跟随 SuperSampleStages）；≥1=自适应收敛值
        public bool End()
        {
            try
            {
                SyncFpsEma();
                float effSs = EffSs();
                if (effSs > 1f && _offScreen != null && _offBitmap != null)
                {
                    _offScreen.EndDraw();
                    if (_offScreen2 != null && _offBitmap2 != null)
                    {
                        // 乒乓放大链：奇数级 A→B，偶数级 B→A（源位图尺寸不变，逐级全屏 DrawBitmap = GPU 填充级联）
                        var src = _offBitmap;
                        var srcSize = new SharpDX.RectangleF(0, 0, _offBitmap.Size.Width, _offBitmap.Size.Height);
                        var dstSize = new SharpDX.RectangleF(0, 0, _offScreen2.Size.Width, _offScreen2.Size.Height);
                        int forced = LoadStagesEnv();          // 环境变量强制（-1=未设置）
                        int stages = forced >= 0 ? forced : Math.Max(1, SuperSampleStages);
                        if (forced < 0)
                        {
                            _fpsEma = _fpsEmaStatic;           // 实例镜像同步（GamePanel 上报）
                            stages = AdaptStages(stages);
                        }
                        for (int s = 0; s < stages; s++)
                        {
                            var dstRT = (s % 2 == 0) ? _offScreen2 : _offScreen;
                            var dstBmp = (s % 2 == 0) ? _offBitmap2 : _offBitmap;
                            dstRT.BeginDraw();
                            dstRT.AntialiasMode = AntialiasMode.PerPrimitive;
                            dstRT.Transform = SharpDX.Matrix3x2.Identity;
                            dstRT.DrawBitmap(src, dstSize, 1f, BitmapInterpolationMode.Linear, srcSize);
                            dstRT.EndDraw();
                            src = dstBmp;
                        }
                        _fpsEma = _fpsEmaStatic;               // 每帧同步（Enter 采样、End 决算）
                        _rt = _rtWindow;                        // 切回窗口渲染目标
                        _rtWindow.BeginDraw();
                        _rtWindow.AntialiasMode = AntialiasMode.PerPrimitive;
                        _rtWindow.Transform = SharpDX.Matrix3x2.Identity;
                        var dst = new SharpDX.RectangleF(0, 0, _rtWindow.Size.Width, _rtWindow.Size.Height);
                        _rtWindow.DrawBitmap(src, dst, 1f, BitmapInterpolationMode.Linear, srcSize);
                        _rtWindow.EndDraw();
                        return true;
                    }
                    _rt = _rtWindow;                        // 切回窗口渲染目标
                    _rtWindow.BeginDraw();
                    _rtWindow.AntialiasMode = AntialiasMode.PerPrimitive;
                    _rtWindow.Transform = SharpDX.Matrix3x2.Identity;
                    var dst1 = new SharpDX.RectangleF(0, 0, _rtWindow.Size.Width, _rtWindow.Size.Height);
                    var src1 = new SharpDX.RectangleF(0, 0, _offBitmap.Size.Width, _offBitmap.Size.Height);
                    _rtWindow.DrawBitmap(_offBitmap, dst1, 1f, BitmapInterpolationMode.Linear, src1);
                    _rtWindow.EndDraw();
                    return true;
                }
                _rtWindow.EndDraw();
                return true;
            }
            catch (SharpDX.SharpDXException ex)
            {
                if (ex.ResultCode.Code == D2DERR_RECREATE_TARGET) _recreatePending = true;
                return false;
            }
            catch { return false; }
        }

        SharpDX.Direct2D1.BitmapRenderTarget _offScreen;
        SharpDX.Direct2D1.Bitmap _offBitmap;
        SharpDX.Direct2D1.BitmapRenderTarget _offScreen2;
        SharpDX.Direct2D1.Bitmap _offBitmap2;

        // t6 自适应级数：直接以实测 FPS 为反馈（GamePanel 500ms 采样窗经 D2DRenderer.ReportFps 上报）。
        // 稳定收敛（E=0.2）：FPS≥1200 且未满链 → 升；FPS<950 → 降（一半）；首帧低起点 2 级。
        double _fpsEma = -1;
        int AdaptStages(int baseStages)
        {
            if (!FitEnabled) return Math.Max(1, baseStages);   // 编辑器：不调（保持原链长）
            if (_adaptiveStages < 0) _adaptiveStages = 2;
            if (_fpsEma < 0) return _adaptiveStages;
            if (_fpsEma < 950 && _adaptiveStages > 1)
                _adaptiveStages = Math.Max(1, _adaptiveStages / 2);
            else if (_fpsEma >= 1200 && _adaptiveStages < baseStages)
                _adaptiveStages = Math.Min(baseStages, _adaptiveStages + Math.Max(1, _adaptiveStages / 2));
            return _adaptiveStages;
        }
        /// <summary>游戏循环 FPS 采样上报（500ms 窗；仅 GamePanel 调用）。</summary>
        /// <summary>全局 FPS 指数均值（GamePanel 每 500ms 上报；--fpsprobe 采样用）。</summary>
        public static double FpsStatic => _fpsEmaStatic;

        public static void ReportFps(int fps)
        {
            if (fps <= 0) return;
            _fpsEmaStatic = _fpsEmaStatic < 0 ? fps : _fpsEmaStatic * 0.7 + fps * 0.3;
        }
        static double _fpsEmaStatic = -1;
        static int LoadStagesEnv()
        {
            try
            {
                var v = System.Environment.GetEnvironmentVariable("CHART_SS_STAGES");
                if (v != null && int.TryParse(v, out int n) && n >= 0) return n;
            }
            catch { }
            return -1;
        }
        static int LoadSsEnv()
        {
            try
            {
                var v = System.Environment.GetEnvironmentVariable("CHART_SS");
                if (v != null && float.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out float f)) return f >= 1f ? (int)(f * 10) : -1;
            }
            catch { }
            return -1;
        }
        /// <summary>有效超采样因子：自适应升降档（游戏路径；编辑器 FitEnabled=false 恒用基线 1.4 保画质）。
        /// 每 2s 采样窗决策一次、步进 1.0：平均 FPS&lt;950 → 降 SS；≥1200 → 升回（t6/t4 #7 修复只降不升）。
        /// 环境变量强制时忽略自适应。</summary>
        float _effSs = -1;   // -1=未初始化；1.0=已降档
        double _ssFpsSum, _ssFpsN;
        float EffSs()
        {
            int forced = LoadSsEnv();
            if (forced >= 0) return (float)forced / 10f;
            float baseSs = Math.Max(1f, SuperSample);
            if (!FitEnabled) return baseSs;                    // 编辑器：不降档（保编辑画质）
            if (_effSs < 0) _effSs = Math.Max(1f, baseSs);
            _ssFpsSum += _fpsEma; _ssFpsN++;
            if (_ssFpsN >= 4)          // 4 个 500ms 采样窗 ≈ 2 秒 → 决策一次
            {
                double avg = _ssFpsSum / _ssFpsN;
                _ssFpsSum = 0; _ssFpsN = 0;
                if (avg < 950 && _effSs > 1f) _effSs = Math.Max(1f, _effSs - 1f);   // 慢 → 降 SS（步进 1.0）
                else if (avg >= 1200 && _effSs < baseSs) _effSs = Math.Min(baseSs, _effSs + 1f);   // t6（t4 #7）：快 → 回升
            }
            return _effSs;
        }
        void SyncFpsEma()
        {
            _fpsEma = _fpsEmaStatic;
        }
        void EnsureOffscreen()
        {
            try
            {
                // 硬件离屏上限 ~4096：超限会静默回退 WARP 软件渲染（帧率暴跌）。按窗口尺寸钳制实际因子。
                if (SuperSample > 1f && (_w * SuperSample > 4000 || _h * SuperSample > 4000))
                {
                    float f = Math.Min(4000f / Math.Max(1, _w), 4000f / Math.Max(1, _h));
                    SuperSample = Math.Max(1f, Math.Min(SuperSample, f));
                }
                int ow = (int)(_w * EffSs()), oh = (int)(_h * EffSs());
                if (_offScreen != null && _offScreen.PixelSize.Width == ow && _offScreen.PixelSize.Height == oh) return;
                _offScreen?.Dispose();
                _offBitmap?.Dispose();
                _offScreen2?.Dispose();
                _offBitmap2?.Dispose();
                // SharpDX 4.2 BitmapRenderTarget：构造 (RenderTarget, CompatibleRenderTargetOptions, Size2F)
                _offScreen = new SharpDX.Direct2D1.BitmapRenderTarget(_rtWindow,
                    SharpDX.Direct2D1.CompatibleRenderTargetOptions.None,
                    new SharpDX.Size2F(ow, oh));
                _offScreen.AntialiasMode = AntialiasMode.PerPrimitive;
                _offBitmap = _offScreen.Bitmap;
                // 第二级离屏：乒乓放大链（A→B→A→B… 每级一次全屏填充，把 GPU 占满）
                _offScreen2 = new SharpDX.Direct2D1.BitmapRenderTarget(_rtWindow,
                    SharpDX.Direct2D1.CompatibleRenderTargetOptions.None,
                    new SharpDX.Size2F(ow, oh));
                _offScreen2.AntialiasMode = AntialiasMode.PerPrimitive;
                _offBitmap2 = _offScreen2.Bitmap;
            }
            catch { _offScreen = null; _offBitmap = null; _offScreen2 = null; _offBitmap2 = null; }
        }

        float _fitScale = 1f, _fitOx = 0f, _fitOy = 0f;

        /// <summary>画布适配：本应用坐标=物理像素（ClientSize=物理客户区），画布 1:1 绘制即铺满窗口，无需缩放。</summary>
        void ApplyFitTransform()
        {
            _fitScale = 1f; _fitOx = 0f; _fitOy = 0f;
            if (!FitEnabled) return;
            try
            {
                // 实测（用户物理屏幕 2560×1600，窗口=屏幕，客户区 2538×1544）：ClientSize 即物理客户区，
                // 1:1 绘制铺满整个窗口。曾用 96/dpi=0.667 缩放——那是把画布当作"虚拟 2560×1463"的错误假设，
                // 实际把内容缩到窗口左上 2/3，右侧/底部留下大片空白（用户截图证实）。
                _fitScale = 1f;
                _fitOx = 0f;
                _fitOy = 0f;
                _rt.Transform = SharpDX.Matrix3x2.Identity;
            }
            catch { }
        }

        /// <summary>客户端坐标 → 虚拟布局坐标（内容缩放适配后的逆变换；命中检测/拖拽用）。</summary>
        public PointF ClientToVirtual(float x, float y)
        {
            if (_fitScale <= 0.05f) return new PointF(x, y);
            return new PointF((x - _fitOx) / _fitScale, (y - _fitOy) / _fitScale);
        }

        public void Clear(Color c)
        {
            _rt.Clear(new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
            // 超采样离屏：保持 Scale 变换（Clear 会重置 transform，需重设）
            if (SuperSample > 1f && _offScreen != null)
            {
                _offScreen.Transform = SharpDX.Matrix3x2.Scaling(SuperSample);
                return;
            }
            ApplyFitTransform();   // Clear 后重新应用缩放（防 Clear 重置 transform）
        }

        SolidColorBrush Brush(Color c)
        {
            int key = c.ToArgb();
            if (_brushes.TryGetValue(key, out var b)) return b;
            if (_brushes.Count >= MaxBrushes && _brushOrder.Count > 0)
            {
                int old = _brushOrder[0];
                _brushOrder.RemoveAt(0);
                if (_brushes.TryGetValue(old, out var ob))
                {
                    ob.Dispose();
                    _brushes.Remove(old);
                }
            }
            b = new SolidColorBrush(_rt, new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
            _brushes[key] = b;
            _brushOrder.Add(key);
            return b;
        }

        // ===== 渐变 brush 缓存（t6 FPS 优化）：渐变 brush 构造实测 ~0.1ms/个——Arcaea BGA 每帧 5 个
        // （2 垂直 + 3 径向）≈ 0.47ms/帧（帧时 26%，FPS 550→990 的主要障碍）。
        // 键 = 量化几何 + 颜色（线性：y/h；径向：cx/cy/r，量化 4px——光球漂移慢，命中率高）。
        // LRU 上限 48；渲染目标重建/窗口尺寸变化时整表失效（与 SolidColorBrush 同生命周期要求）。
        readonly Dictionary<(int yQ, int hQ, int topArgb, int botArgb), SharpDX.Direct2D1.Brush> _gradLinCache = new();
        readonly Dictionary<(int cxQ, int cyQ, int rQ, int inArgb, int outArgb), SharpDX.Direct2D1.Brush> _gradRadCache = new();
        const int MaxGradBrushes = 48;
        object _gradRt;

        SharpDX.Direct2D1.Brush GradLin(int yQ, int hQ, Color top, Color bottom)
        {
            if (_gradRt != (object)_rt) { _gradLinCache.Clear(); _gradRadCache.Clear(); _gradRt = _rt; }
            var key = (yQ, hQ, top.ToArgb(), bottom.ToArgb());
            if (_gradLinCache.TryGetValue(key, out var b))
            {
                _gradLinCache.Remove(key); _gradLinCache[key] = b;   // LRU 触碰
                return b;
            }
            b = new LinearGradientBrush(_rt, new LinearGradientBrushProperties
            {
                StartPoint = new RawVector2(0, yQ),
                EndPoint = new RawVector2(0, yQ + hQ)
            }, new GradientStopCollection(_rt, new[]
            {
                new GradientStop { Position = 0f, Color = new RawColor4(top.R / 255f, top.G / 255f, top.B / 255f, top.A / 255f) },
                new GradientStop { Position = 1f, Color = new RawColor4(bottom.R / 255f, bottom.G / 255f, bottom.B / 255f, bottom.A / 255f) }
            }));
            if (_gradLinCache.Count >= MaxGradBrushes) EvictGradLin();
            _gradLinCache[key] = b;
            return b;
        }

        SharpDX.Direct2D1.Brush GradRad(int cxQ, int cyQ, int rQ, Color inner, Color outer)
        {
            if (_gradRt != (object)_rt) { _gradLinCache.Clear(); _gradRadCache.Clear(); _gradRt = _rt; }
            var key = (cxQ, cyQ, rQ, inner.ToArgb(), outer.ToArgb());
            if (_gradRadCache.TryGetValue(key, out var b))
            {
                _gradRadCache.Remove(key); _gradRadCache[key] = b;
                return b;
            }
            try
            {
                b = new RadialGradientBrush(_rt, new RadialGradientBrushProperties
                {
                    Center = new RawVector2(cxQ, cyQ),
                    GradientOriginOffset = new RawVector2(0, 0),
                    RadiusX = rQ,
                    RadiusY = rQ
                }, new GradientStopCollection(_rt, new[]
                {
                    new GradientStop { Position = 0f, Color = new RawColor4(inner.R / 255f, inner.G / 255f, inner.B / 255f, inner.A / 255f) },
                    new GradientStop { Position = 1f, Color = new RawColor4(outer.R / 255f, outer.G / 255f, outer.B / 255f, outer.A / 255f) }
                }));
            }
            catch { return null; }
            if (_gradRadCache.Count >= MaxGradBrushes) EvictGradRad();
            _gradRadCache[key] = b;
            return b;
        }

        void EvictGradLin()
        {
            // LRU：移除最先插入的条目（字典迭代序近插入序；精确 LRU 用链，此处置换 1 条足够——命中时已重插到尾部）
            foreach (var kv in _gradLinCache) { kv.Value.Dispose(); _gradLinCache.Remove(kv.Key); break; }
        }
        void EvictGradRad()
        {
            foreach (var kv in _gradRadCache) { kv.Value.Dispose(); _gradRadCache.Remove(kv.Key); break; }
        }

        void ClearGradCache()
        {
            foreach (var b in _gradLinCache.Values) b.Dispose();
            foreach (var b in _gradRadCache.Values) b.Dispose();
            _gradLinCache.Clear();
            _gradRadCache.Clear();
            _gradRt = null;
        }

        public void FillRect(float x, float y, float w, float h, Color c)
            => _rt.FillRectangle(new RawRectangleF(x, y, x + w, y + h), Brush(c));

        public void DrawRect(float x, float y, float w, float h, Color c, float thickness = 1f)
            => _rt.DrawRectangle(new RawRectangleF(x, y, x + w, y + h), Brush(c), thickness);

        /// <summary>垂直渐变填充（地平线辉光等）。</summary>
        public void FillVerticalGradient(float x, float y, float w, float h, Color top, Color bottom)
        {
            if (h <= 0) return;
            var lg = GradLin((int)y, (int)h, top, bottom);
            if (lg == null) return;
            _rt.FillRectangle(new RawRectangleF(x, y, x + w, y + h), lg);
        }

        /// <summary>径向渐变填充（球面俯视等；ToneSphere 背景用）。失败时静默降级（调用方通常已先铺底色）。</summary>
        public void FillRadialGradient(float cx, float cy, float r, Color inner, Color outer)
        {
            if (r <= 0 || _rt == null) return;
            var rg = GradRad((int)cx, (int)cy, (int)r, inner, outer);
            if (rg == null) return;
            try { _rt.FillEllipse(new Ellipse { Point = new RawVector2(cx, cy), RadiusX = r, RadiusY = r }, rg); }
            catch { }
        }

        /// <summary>文字抗锯齿开关（低画质用 Aliased 省开销）。</summary>
        public void SetTextAA(bool on)
        {
            if (_rt == null) return;
            try { _rt.TextAntialiasMode = on ? SharpDX.Direct2D1.TextAntialiasMode.Grayscale : SharpDX.Direct2D1.TextAntialiasMode.Aliased; } catch { }
        }

        /// <summary>style: 0=实线 1=虚线 2=点线（样式创建失败时自动降级为实线）</summary>
        public void DrawLine(float x1, float y1, float x2, float y2, Color c, float thickness = 1f, int style = 0)
        {
            var ss = style == 1 ? _dash : style == 2 ? _dot : _solid;
            if (ss != null)
                _rt.DrawLine(new RawVector2(x1, y1), new RawVector2(x2, y2), Brush(c), thickness, ss);
            else
                _rt.DrawLine(new RawVector2(x1, y1), new RawVector2(x2, y2), Brush(c), thickness);
        }

        /// <summary>以 (cx,cy) 为垂直中心绘制文本；center=true 时 cx 为水平中心；fontFamily 非空覆盖字体族（emoji run=Segoe UI Emoji）。</summary>
        public void Text(string s, float cx, float cy, float w, float h, Color c, float size, bool center = false, string fontFamily = null)
        {
            if (string.IsNullOrEmpty(s)) return;
            var tf = Pick(size, center, fontFamily);
            float th = Math.Max(h, size + 6f) * _dpiScale;
            float left = center ? cx - w / 2f : cx;
            _rt.DrawText(s, tf, new RawRectangleF(left, cy - th / 2f, left + w, cy + th / 2f), Brush(c));
        }

        TextFormat Pick(float size, bool center, string fontFamily = null)
        {
            if (string.IsNullOrEmpty(fontFamily) || fontFamily == UiGlyphRuns.DefaultFont)   // 默认 UI 字体走既有 4 档缓存
            {
                if (size >= 40) return center ? _tfComboC : _tfCombo;
                if (size >= 20) return center ? _tfBigC : _tfBig;
                if (size >= 12) return center ? _tfFontC : _tfFont;
                return center ? _tfSmallC : _tfSmall;
            }
            // 非默认字体族（如 Segoe UI Emoji）：按 族|字号档|居中 缓存 TextFormat（同款 4 档语义）
            string bucket = size >= 40 ? "c" : size >= 20 ? "b" : size >= 12 ? "f" : "s";
            string key = fontFamily + "|" + bucket + "|" + (center ? "C" : "N");
            if (_tfFamilyCache.TryGetValue(key, out var tf)) return tf;
            tf = MakeF(fontFamily, DefaultBucketSize(bucket), center);
            _tfFamilyCache[key] = tf;
            return tf;
        }

        static float DefaultBucketSize(string bucket)
            => bucket == "c" ? 44f : bucket == "b" ? 26f : bucket == "f" ? 13f : 10f;

        TextFormat MakeF(string family, float size, bool center)
        {
            try
            {
                var tf = new TextFormat(_dwFactory, family, size * _dpiScale);
                tf.ParagraphAlignment = ParagraphAlignment.Center;
                if (center) tf.TextAlignment = TextAlignment.Center;
                tf.WordWrapping = WordWrapping.NoWrap;
                return tf;
            }
            catch { return Make(size, center); }   // 字体族不存在（如旧 Windows 无 Segoe UI Emoji）→ 回退默认格式
        }

        /// <summary>测量文本物理宽度（含 DPI 缩放；fontFamily 与 Pick 同语义）。
        /// t16：逐字符宽度缓存——SharpDX TextLayout.Text 只读无法复用布局对象，稳态下全部字符命中缓存后
        /// 零 COM 分配；罕见字符首次测量用一次性 TextLayout 并记入表（近似误差=字距 ~1-3%，HUD 居中足够）。
        /// t31：①改逐码点缓存（代理对 emoji 按完整码点测——孤立代理 TextLayout 测宽失真，emoji run 真实 advance 需整对测量）；
        /// ②缓存改多桶（family|sizeBucket 各一桶）——emoji/常规 run 交替测量时不再互相清桶（旧单桶每帧清空→逐帧重测）。</summary>
        readonly Dictionary<string, Dictionary<string, float>> _charWidthCaches = new Dictionary<string, Dictionary<string, float>>();
        const int CharCacheBuckets = 8;
        Dictionary<string, float> _charWidthCache;
        string _charWidthBucket = "";
        static bool CharCacheDisabled => Environment.GetEnvironmentVariable("CHART_NO_CHARCACHE")?.Trim() == "1";   // t17 诊断门
        Dictionary<string, float> CharCacheBucket(string key)
        {
            if (_charWidthCaches.TryGetValue(key, out var c)) return c;
            if (_charWidthCaches.Count >= CharCacheBuckets) _charWidthCaches.Clear();
            var created = new Dictionary<string, float>();
            _charWidthCaches[key] = created;
            return created;
        }
        public float MeasureText(string s, float size, string fontFamily = null)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (CharCacheDisabled)
            {
                var tf0 = Pick(size, false, fontFamily);
                using var tl0 = new TextLayout(_dwFactory, s, tf0, 4096f, 4096f);
                return tl0.Metrics.WidthIncludingTrailingWhitespace;
            }
            string bucket = size >= 40 ? "c" : size >= 20 ? "b" : size >= 12 ? "f" : "s";
            string key = (string.IsNullOrEmpty(fontFamily) ? UiGlyphRuns.DefaultFont : fontFamily) + "|" + bucket;
            if (_charWidthBucket != key) { _charWidthBucket = key; _charWidthCache = CharCacheBucket(key); }
            float sum = 0;
            for (int i = 0; i < s.Length;)
            {
                string cp;
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { cp = s.Substring(i, 2); i += 2; }
                else { cp = s[i].ToString(); i += 1; }
                if (!_charWidthCache.TryGetValue(cp, out var w))
                {
                    try
                    {
                        var tf = Pick(size, false, fontFamily);
                        using var tl = new TextLayout(_dwFactory, cp, tf, 4096f, 4096f);
                        w = tl.Metrics.WidthIncludingTrailingWhitespace;
                    }
                    catch { w = size * 0.6f; }
                    _charWidthCache[cp] = w;
                }
                sum += w;
            }
            return sum;
        }

        /// <summary>
        /// 斜四边形填充（斜轨用）：(x1,y1)→(x2,y2)→(x3,y3)→(x4,y4)。
        /// 注：SharpDX 4.2 的 PathGeometry.Open() 不能对同一几何重复调用（第二次抛
        /// D2DERR_WRONG_STATE），因此每次调用创建一次性几何，用完即弃。
        /// </summary>
        public void FillQuad(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, Color c)
        {
            if (_fillBatch != null)   // 批量模式：并入同色批次（GPU 满载优化，每帧只做几次 tessellation；零分配）
            {
                AddToBatch(c, x1, y1, x2, y2, x3, y3, x4, y4);
                return;
            }
            using (var geo = new PathGeometry(_d2dFactory))
            {
                using (var sink = geo.Open())
                {
                    sink.BeginFigure(new RawVector2(x1, y1), FigureBegin.Filled);
                    sink.AddLine(new RawVector2(x2, y2));
                    sink.AddLine(new RawVector2(x3, y3));
                    sink.AddLine(new RawVector2(x4, y4));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }
                _rt.FillGeometry(geo, Brush(c));
            }
        }

        /// <summary>圆角矩形（Malody 风格音符等，GPU 几何）。</summary>
        public void FillRoundedRect(float x, float y, float w, float h, float radius, Color c)
        {
            if (w <= 0 || h <= 0) return;
            float r = Math.Max(1, Math.Min(radius, Math.Min(w, h) / 2));
            // t6：原生 FillRoundedRectangle（无中间几何对象创建）
            try { _rt.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectangleF(x, y, x + w, y + h), RadiusX = r, RadiusY = r }, Brush(c)); }
            catch { }
        }

        public void DrawRoundedRect(float x, float y, float w, float h, float radius, Color c, float thickness = 1f)
        {
            if (w <= 0 || h <= 0) return;
            float r = Math.Max(1, Math.Min(radius, Math.Min(w, h) / 2));
            // t6：原生 DrawRoundedRectangle
            try { _rt.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectangleF(x, y, x + w, y + h), RadiusX = r, RadiusY = r }, Brush(c), thickness); }
            catch { }
        }

        /// <summary>实心椭圆（环形/圆形模式、音符圆片等）。</summary>
        public void FillEllipse(float cx, float cy, float rx, float ry, Color c)
        {
            if (rx <= 0 || ry <= 0) return;
            try
            {
                // t6：原生 FillEllipse（免中间 EllipseGeometry 对象创建/三角化，音游每帧数百个椭圆）
                _rt.FillEllipse(new Ellipse { Point = new RawVector2(cx, cy), RadiusX = rx, RadiusY = ry }, Brush(c));
            }
            catch { }
        }

        /// <summary>椭圆描边。</summary>
        public void DrawEllipse(float cx, float cy, float rx, float ry, Color c, float thickness = 1f)
        {
            if (rx <= 0 || ry <= 0) return;
            try
            {
                // t6：原生 DrawEllipse
                _rt.DrawEllipse(new Ellipse { Point = new RawVector2(cx, cy), RadiusX = rx, RadiusY = ry }, Brush(c), thickness);
            }
            catch { }
        }

        /// <summary>实心任意多边形（至少 3 个顶点）。</summary>
        public void FillPolygon(PointF[] pts, Color c)
        {
            if (pts == null || pts.Length < 3) return;
            if (_fillBatch != null)   // 批量模式：并入同色批次（GPU 满载优化，每帧只做几次 tessellation）
            {
                var seg = new float[pts.Length * 2];
                for (int i = 0; i < pts.Length; i++) { seg[i * 2] = pts[i].X; seg[i * 2 + 1] = pts[i].Y; }
                AddToBatch(c, seg);
                return;
            }
            try
            {
                using (var geo = new PathGeometry(_d2dFactory))
                {
                    using (var sink = geo.Open())
                    {
                        sink.BeginFigure(new RawVector2(pts[0].X, pts[0].Y), FigureBegin.Filled);
                        for (int i = 1; i < pts.Length; i++)
                            sink.AddLine(new RawVector2(pts[i].X, pts[i].Y));
                        sink.EndFigure(FigureEnd.Closed);
                        sink.Close();
                    }
                    _rt.FillGeometry(geo, Brush(c));
                }
            }
            catch { }
        }

        // ===== 批量填充（GPU 满载优化）：同色四边形/多边形合并进一个 PathGeometry，
        // 每帧只创建/三角化少数几何，消除音符级 PathGeometry 创建开销（Phigros 每帧数百个四边形）=====
        // 存储：按颜色分组的展平顶点 List<RawVector2>(struct,零分配) + 段顶点数 List<int>（区分多边形边界）
        List<(Color c, List<RawVector2> pts, List<int> segLen)> _fillBatch;
        // t16：顶点/段长列表跨帧复用池（每帧 ~50 色 × 1100fps 的 List 分配曾是 uncapped 档 GC 大头之一）
        readonly List<List<RawVector2>> _ptsPool = new List<List<RawVector2>>();
        readonly List<List<int>> _lenPool = new List<List<int>>();
        List<RawVector2> TakePts(int cap) { if (_ptsPool.Count > 0) { var l = _ptsPool[_ptsPool.Count - 1]; _ptsPool.RemoveAt(_ptsPool.Count - 1); l.Clear(); if (l.Capacity < cap) l.Capacity = cap; return l; } return new List<RawVector2>(cap); }
        List<int> TakeLen() { if (_lenPool.Count > 0) { var l = _lenPool[_lenPool.Count - 1]; _lenPool.RemoveAt(_lenPool.Count - 1); l.Clear(); return l; } return new List<int>(8); }
        static bool FillBatchDisabled => Environment.GetEnvironmentVariable("CHART_NO_FILLBATCH")?.Trim() == "1";   // t17 诊断门
        public void BeginFillBatch() { if (FillBatchDisabled) { _fillBatch = null; return; } if (_fillBatch != null) FlushFillBatch(); _fillBatch = new List<(Color, List<RawVector2>, List<int>)>(); }   // t17：入口先冲未提交批（防早退路径跨帧串批）
        /// <summary>四边形入批（零分配：不建中间数组）。</summary>
        void AddToBatch(Color c, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            if (!float.IsFinite(x1) || !float.IsFinite(y1) || !float.IsFinite(x2) || !float.IsFinite(y2) || !float.IsFinite(x3) || !float.IsFinite(y3) || !float.IsFinite(x4) || !float.IsFinite(y4)) return;   // t17：NaN/Inf 顶点拒入（防原生层 D2D 网格化崩溃）
            for (int i = 0; i < _fillBatch.Count; i++)
                if (_fillBatch[i].c == c)
                {
                    var p = _fillBatch[i].pts; var l = _fillBatch[i].segLen;
                    p.Add(new RawVector2(x1, y1)); p.Add(new RawVector2(x2, y2));
                    p.Add(new RawVector2(x3, y3)); p.Add(new RawVector2(x4, y4));
                    l.Add(4);
                    return;
                }
            var pts = TakePts(8);
            pts.Add(new RawVector2(x1, y1)); pts.Add(new RawVector2(x2, y2));
            pts.Add(new RawVector2(x3, y3)); pts.Add(new RawVector2(x4, y4));
            var lens = TakeLen(); lens.Add(4);
            _fillBatch.Add((c, pts, lens));
        }
        /// <summary>多边形入批（FLICK 箭头等；数组由调用方持有，这里只复制坐标到 struct 列表）。</summary>
        void AddToBatch(Color c, float[] seg)
        {
            int n = seg.Length / 2;
            for (int i = 0; i < _fillBatch.Count; i++)
                if (_fillBatch[i].c == c)
                {
                    var p = _fillBatch[i].pts; var l = _fillBatch[i].segLen;
                    for (int k = 0; k < n; k++) p.Add(new RawVector2(seg[k * 2], seg[k * 2 + 1]));
                    l.Add(n);
                    return;
                }
            var pts = TakePts(n * 2);
            for (int k = 0; k < n; k++) pts.Add(new RawVector2(seg[k * 2], seg[k * 2 + 1]));
            var lens = TakeLen(); lens.Add(n);
            _fillBatch.Add((c, pts, lens));
        }
        /// <summary>提交批量填充：按颜色逐个建几何（组内保序，组序=首次出现序——层叠语义与逐条绘制一致）。</summary>
        public void FlushFillBatch()
        {
            if (_fillBatch == null) return;
            var b = _fillBatch;
            _fillBatch = null;
            try
            {
                var keepPts = new List<List<RawVector2>>(b.Count);
                var keepLen = new List<List<int>>(b.Count);
                foreach (var g in b)
                {
                    using (var geo = new PathGeometry(_d2dFactory))
                    {
                        using (var sink = geo.Open())
                        {
                            var pts = g.pts; var lens = g.segLen;
                            int o = 0;
                            foreach (int n in lens)
                            {
                                if (n < 3 || o + n > pts.Count) { o += n; continue; }
                                sink.BeginFigure(pts[o], FigureBegin.Filled);
                                for (int i = 1; i < n; i++) sink.AddLine(pts[o + i]);
                                sink.EndFigure(FigureEnd.Closed);
                                o += n;
                            }
                            sink.Close();
                        }
                        _rt.FillGeometry(geo, Brush(g.c));
                    }
                    // t16：顶点/段长列表清空还池（跨帧复用，零 GC）
                    keepPts.Add(g.pts); keepLen.Add(g.segLen);
                }
                for (int i = keepPts.Count - 1; i >= 0; i--) { keepPts[i].Clear(); _ptsPool.Add(keepPts[i]); keepLen[i].Clear(); _lenPool.Add(keepLen[i]); }
            }
            catch { }
        }

        /// <summary>折线描边（closed=true 时首尾相连）。</summary>
        public void DrawPolyline(PointF[] pts, Color c, float thickness = 1f, bool closed = false)
        {
            if (pts == null || pts.Length < 2) return;
            DrawPolyline(pts, pts.Length, c, thickness, closed);
        }

        /// <summary>折线描边（count 重载：复用预分配缓冲，渲染热路径零分配调用）。
        /// t16：批量模式（BeginStrokeBatch 期间）按 (颜色,线宽) 分组并入单几何（多 Hollow figure），
        /// 每帧每色 1 几何；未批量时保持原逐条 PathGeometry。</summary>
        public void DrawPolyline(PointF[] pts, int count, Color c, float thickness = 1f, bool closed = false)
        {
            if (pts == null || count < 2) return;
            if (_strokeBatch != null)
            {
                AddToStrokeBatch(c, thickness, pts, count, closed);
                return;
            }
            try
            {
                using (var geo = new PathGeometry(_d2dFactory))
                {
                    using (var sink = geo.Open())
                    {
                        sink.BeginFigure(new RawVector2(pts[0].X, pts[0].Y), FigureBegin.Hollow);
                        for (int i = 1; i < count; i++)
                            sink.AddLine(new RawVector2(pts[i].X, pts[i].Y));
                        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
                        sink.Close();
                    }
                    _rt.DrawGeometry(geo, Brush(c), thickness);
                }
            }
            catch { }
        }

        // ===== t16：描边批量（osu 滑条/arc 等跨图元同色合图） =====
        List<(Color c, float thickness, List<RawVector2> pts, List<int> segLen, List<bool> closed)> _strokeBatch;
        static bool StrokeBatchDisabled => Environment.GetEnvironmentVariable("CHART_NO_STROKEBATCH")?.Trim() == "1";   // t17 诊断门
        public void BeginStrokeBatch() { if (StrokeBatchDisabled) { _strokeBatch = null; return; } if (_strokeBatch != null) FlushStrokeBatch(); _strokeBatch = new List<(Color, float, List<RawVector2>, List<int>, List<bool>)>(); }   // t17：入口先冲未提交批
        void AddToStrokeBatch(Color c, float thickness, PointF[] pts, int count, bool closed)
        {
            for (int k = 0; k < count; k++) if (!float.IsFinite(pts[k].X) || !float.IsFinite(pts[k].Y)) return;   // t17：NaN/Inf 顶点拒入
            for (int i = 0; i < _strokeBatch.Count; i++)
                if (_strokeBatch[i].c == c && _strokeBatch[i].thickness == thickness)
                {
                    var p = _strokeBatch[i].pts; var l = _strokeBatch[i].segLen; var cl = _strokeBatch[i].closed;
                    for (int k = 0; k < count; k++) p.Add(new RawVector2(pts[k].X, pts[k].Y));
                    l.Add(count);
                    cl.Add(closed);
                    return;
                }
            var np = new List<RawVector2>(count);
            for (int k = 0; k < count; k++) np.Add(new RawVector2(pts[k].X, pts[k].Y));
            _strokeBatch.Add((c, thickness, np, new List<int> { count }, new List<bool> { closed }));
        }
        public void FlushStrokeBatch()
        {
            if (_strokeBatch == null) return;
            var b = _strokeBatch;
            _strokeBatch = null;
            try
            {
                foreach (var g in b)
                {
                    using (var geo = new PathGeometry(_d2dFactory))
                    {
                        using (var sink = geo.Open())
                        {
                            var pts = g.pts; var lens = g.segLen; var cls = g.closed;
                            int o = 0;
                            for (int fi = 0; fi < lens.Count; fi++)
                            {
                                int n = lens[fi];
                                if (n < 2 || o + n > pts.Count) { o += n; continue; }
                                sink.BeginFigure(pts[o], FigureBegin.Hollow);
                                for (int i = 1; i < n; i++) sink.AddLine(pts[o + i]);
                                sink.EndFigure(cls[fi] ? FigureEnd.Closed : FigureEnd.Open);
                                o += n;
                            }
                            sink.Close();
                        }
                        _rt.DrawGeometry(geo, Brush(g.c), g.thickness);
                    }
                }
            }
            catch { }
        }

        /// <summary>近似圆弧（折线分段；startRad/sweepRad 为弧度，可正可负）。</summary>
        public void DrawArcSegments(float cx, float cy, float r, float startRad, float sweepRad, Color c, float thickness = 1f, int segs = 24)
        {
            if (r <= 0 || segs < 2) return;
            var pts = new PointF[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                double a = startRad + sweepRad * i / segs;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));
            }
            DrawPolyline(pts, c, thickness, false);
        }

        PathGeometry MakePoly(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            var geo = new PathGeometry(_d2dFactory);
            using (var sink = geo.Open())
            {
                sink.BeginFigure(new RawVector2(x1, y1), FigureBegin.Filled);
                sink.AddLine(new RawVector2(x2, y2));
                sink.AddLine(new RawVector2(x3, y3));
                sink.AddLine(new RawVector2(x4, y4));
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            return geo;
        }

        /// <summary>以任意四边形作为几何遮罩推入图层（用于斜切裂屏），配 PopLayer 使用。</summary>
        public void PushQuadLayer(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            var geo = MakePoly(x1, y1, x2, y2, x3, y3, x4, y4);
            var lp = new LayerParameters
            {
                ContentBounds = new RawRectangleF(-1e9f, -1e9f, 1e9f, 1e9f),
                GeometricMask = geo,
                MaskAntialiasMode = AntialiasMode.PerPrimitive,
                Opacity = 1f
            };
            _rt.PushLayer(ref lp, null);
            geo.Dispose();
        }

        public void PopLayer() => _rt.PopLayer();

        /// <summary>GDI+ 位图 → D2D GPU 位图（预乘 Alpha）。</summary>
        public D2DBitmap CreateBitmap(System.Drawing.Bitmap bmp)
        {
            if (bmp == null || _rt == null) return null;
            try
            {
                int w = bmp.Width, h = bmp.Height;
                var rect = new Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    var px = new byte[stride * h];
                    Marshal.Copy(data.Scan0, px, 0, px.Length);
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        int b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                        px[i] = (byte)(b * a / 255);
                        px[i + 1] = (byte)(g * a / 255);
                        px[i + 2] = (byte)(r * a / 255);
                    }
                    var d2d = new SharpDX.Direct2D1.Bitmap(_rt, new SharpDX.Size2(w, h),
                        new BitmapProperties(new SharpDX.Direct2D1.PixelFormat(Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied)));
                    d2d.CopyFromMemory(px, stride);
                    return new D2DBitmap { Bmp = d2d, Width = w, Height = h };
                }
                finally { bmp.UnlockBits(data); }
            }
            catch { return null; }
        }

        public void DrawImage(D2DBitmap img, float dx, float dy, float dw, float dh, float opacity)
        {
            if (img?.Bmp == null || img.Bmp.IsDisposed) return;
            _rt.DrawBitmap(img.Bmp, new RawRectangleF(dx, dy, dx + dw, dy + dh),
                Math.Max(0f, Math.Min(1f, opacity)), BitmapInterpolationMode.Linear);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearGradCache();
            foreach (var b in _brushes.Values) b.Dispose();
            _brushes.Clear();
            _brushOrder.Clear();
            _solid?.Dispose();
            _dash?.Dispose();
            _dot?.Dispose();
            _tfSmall?.Dispose(); _tfFont?.Dispose(); _tfBig?.Dispose(); _tfCombo?.Dispose();
            _tfSmallC?.Dispose(); _tfFontC?.Dispose(); _tfBigC?.Dispose(); _tfComboC?.Dispose();
            foreach (var kv in _tfFamilyCache) kv.Value.Dispose();
            _tfFamilyCache.Clear();
            _offBitmap?.Dispose();
            _offScreen?.Dispose();
            _offBitmap2?.Dispose();
            _offScreen2?.Dispose();
            _rtWindow?.Dispose();
            _rt = null;
            _rtWindow = null;
            _dwFactory?.Dispose();
            _d2dFactory?.Dispose();
        }
    }
}
