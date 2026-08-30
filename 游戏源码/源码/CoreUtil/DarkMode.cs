using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>为窗口启用深色模式，使原生滚动条/滑条变为深色，与深色背景协调。</summary>
    public static class DarkMode
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        static extern int AllowDarkModeForWindow(IntPtr hwnd, bool allow);

        public static void Enable(IntPtr hwnd)
        {
            try
            {
                // 允许整个进程使用深色模式（Win10 1903+）
                SetPreferredAppMode(2);
                // 该窗口启用深色（Win10 1809+）
                AllowDarkModeForWindow(hwnd, true);
                // 标题栏 / 滚动条深色（Win10 用 19，Win11 用 20）
                int v = 1;
                DwmSetWindowAttribute(hwnd, 19, ref v, 4);
                DwmSetWindowAttribute(hwnd, 20, ref v, 4);
            }
            catch { }
        }
    }
}
