using System;
using System.Drawing;

namespace ChartPlayer
{
    /// <summary>
    /// t78：固定 96 DPI 离屏位图工厂（引擎 UI 文本"显示不全/被裁/重叠"根治）。
    /// 背景：进程按 app.manifest 声明 PerMonitorV2 DPI 感知后，`new Bitmap(w,h)` 的默认分辨率=显示器 DPI
    /// （本机 150% 缩放→144），而 GDI+ 的 GraphicsUnit.Point 字号按 Graphics.DpiY 换算像素
    /// （13pt @144dpi = 26px，应 17.3px）→ 引擎壳文本比 1280×800 逻辑布局大 1.5×，被 96-DPI 语义的
    /// 标签裁剪矩形（UiLabel Clip）裁切（"功能"标题顶部/底部缺失、玩家统计两行压叠、底栏提示被裁等）。
    /// 修复：所有引擎壳 GDI 离屏位图显式 SetResolution(96,96)——pt→px 按 96 换算，文本以逻辑尺寸进入
    /// letterbox 变换（t27 设计：文字按字形轮廓在目标尺寸直接光栅化），任意 DPI 显示器下与设计一致。
    /// 注意：测量侧（UiMeasure）必须同步 96 DPI，否则 AutoSize/FitFont 与绘制口径不一致（字体缩得过小）。
    /// </summary>
    public static class DpiBitmap
    {
        /// <summary>创建 96 DPI 的离屏位图（PixelUnit=Display，GDI+ Point 字号按 96 换算像素）。</summary>
        public static Bitmap Create(int w, int h)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h));
            bmp.SetResolution(96f, 96f);
            return bmp;
        }
    }
}
