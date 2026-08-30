using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ChartPlayer
{
    /* ================= EngineHosting（t50）：引擎窗承载验证工具 =================
     * 顶层可见窗口枚举（EnumWindows + IsWindowVisible，跨进程）：
     * 用于验证「任意路径（设置/编辑/游玩/段位/联机）下可见窗口数=1（引擎窗）」——
     * 引擎窗内承载的 GamePanel/编辑器都是 WS_CHILD 子控件（非顶层窗口），不计入本枚举；
     * 运行时在关键路径打点（EngineMainShell.HostContent/UnhostContent / 外壳显示 / --wincount CLI）。
     * 注意：EnumWindows 只枚举顶层窗口——子控件（Win32 句柄）天然不计，因此"同窗承载"即"可见窗口=1"。
     */

    public static class EngineHosting
    {
        public class WindowInfo
        {
            public IntPtr Hwnd;
            public string Title = "";
            public string Class = "";
            public override string ToString() => "hwnd=0x" + Hwnd.ToString("X") + " · [" + Class + "] · " + Title;
        }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr hwnd, StringBuilder sb, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr hwnd, StringBuilder sb, int max);

        /// <summary>枚举全部可见顶层窗口（含其他进程；子控件窗口不计入）。</summary>
        public static List<WindowInfo> EnumVisibleTopLevel()
        {
            var list = new List<WindowInfo>();
            try
            {
                EnumWindows((h, l) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    var w = new WindowInfo { Hwnd = h };
                    var sb = new StringBuilder(256);
                    GetWindowTextW(h, sb, 256);
                    w.Title = sb.ToString();
                    sb.Clear();
                    GetClassNameW(h, sb, 256);
                    w.Class = sb.ToString();
                    list.Add(w);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        /// <summary>可见顶层窗口数（目标：任意路径下 =1，即引擎窗）。</summary>
        public static int CountVisibleTopLevelWindows() => EnumVisibleTopLevel().Count;

        /// <summary>人类可读描述（计数 + 逐条 hwnd/类名/标题）。</summary>
        public static string DescribeVisibleWindows()
        {
            var sb = new StringBuilder(512);
            var ws = EnumVisibleTopLevel();
            sb.Append("可见窗口 ").Append(ws.Count).Append(" 个");
            foreach (var w in ws)
            {
                sb.Append(Environment.NewLine).Append("  · ").Append(w);
            }
            return sb.ToString();
        }

        /// <summary>运行期打点：Logger.Info + Console.WriteLine；返回当前可见窗口数。</summary>
        public static int LogWindowState(string context)
        {
            string msg = "[窗口枚举] " + context + " → " + DescribeVisibleWindows();
            try { Logger.Info(msg); } catch { }
            try { Console.WriteLine(msg); } catch { }
            return CountVisibleTopLevelWindows();
        }
    }
}
