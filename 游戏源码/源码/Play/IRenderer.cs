using System.Drawing;

namespace ChartPlayer
{
    /// <summary>
    /// 渲染后端接口（引擎层第 3 步）：GamePanel / 转场只依赖此接口，可替换为任意后端实现。
    /// 当前唯一实现为 D2DRenderer（SharpDX Direct2D 硬件加速）；未来可加 OpenGL/GDI+ 后端。
    /// 位图参数暂用 D2DBitmap（数据载体），替换后端时可引入通用 IBitmap。
    /// </summary>
    public interface IRenderer : System.IDisposable
    {
        void Resize(int width, int height);
        bool Begin();
        bool End();
        void Clear(Color c);
        void FillRect(float x, float y, float w, float h, Color c);
        void DrawRect(float x, float y, float w, float h, Color c, float thickness = 1f);
        void FillVerticalGradient(float x, float y, float w, float h, Color top, Color bottom);
        void FillRadialGradient(float cx, float cy, float r, Color inner, Color outer);
        void SetTextAA(bool on);
        void DrawLine(float x1, float y1, float x2, float y2, Color c, float thickness = 1f, int style = 0);
        void Text(string s, float cx, float cy, float w, float h, Color c, float size, bool center = false, string fontFamily = null);
        /// <summary>测量文本物理宽度（fontFamily=null 用默认 UI 字体；非空覆盖字体族——emoji run 走 Segoe UI Emoji）。</summary>
        float MeasureText(string s, float size, string fontFamily = null);
        void FillQuad(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, Color c);
        /// <summary>批量填充模式：Begin 后 FillQuad/FillPolygon 并入同色批次，Flush 时按颜色合并成少数 PathGeometry（每帧只三角化几次，消除音符级几何创建开销）。</summary>
        void BeginFillBatch();
        void FlushFillBatch();
        /// <summary>t16：描边批量（Begin 后 DrawPolyline 按 (颜色,线宽) 并入单几何，Flush 每色 1 几何）。</summary>
        void BeginStrokeBatch();
        void FlushStrokeBatch();
        void FillRoundedRect(float x, float y, float w, float h, float radius, Color c);
        void DrawRoundedRect(float x, float y, float w, float h, float radius, Color c, float thickness = 1f);
        void FillEllipse(float cx, float cy, float rx, float ry, Color c);
        void DrawEllipse(float cx, float cy, float rx, float ry, Color c, float thickness = 1f);
        void FillPolygon(PointF[] pts, Color c);
        void DrawPolyline(PointF[] pts, Color c, float thickness = 1f, bool closed = false);
        /// <summary>折线描边（count 重载：复用预分配缓冲，渲染热路径零分配调用）。</summary>
        void DrawPolyline(PointF[] pts, int count, Color c, float thickness = 1f, bool closed = false);
        void DrawArcSegments(float cx, float cy, float r, float startRad, float sweepRad, Color c, float thickness = 1f, int segs = 24);
        void PushQuadLayer(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
        void PopLayer();
        D2DBitmap CreateBitmap(System.Drawing.Bitmap bmp);
        void DrawImage(D2DBitmap img, float dx, float dy, float dw, float dh, float opacity);
        /// <summary>客户端坐标 → 虚拟布局坐标（内容缩放适配后的逆变换；命中检测/拖拽用）。</summary>
        PointF ClientToVirtual(float x, float y);
    }
}
