using System; using System.Drawing; using System.Drawing.Imaging; using System.IO; using System.Runtime.InteropServices; namespace ChartPlayer { public static class ScreenCapture { const string DirName = "屏幕采集"; static string _dir; static string CaptureDir { get { if (_dir == null) {   // t62：游戏内容根=BaseDirectory（用户指令）——截图固定落 exe 旁 屏幕采集\（移除了向父目录找 Milestone.csproj 的旧开发路径探测，迁移后 csproj 在 游戏源码\ 不再适用）。
    string baseDir = AppDomain.CurrentDomain.BaseDirectory; _dir = Path.Combine(baseDir, DirName); try { Directory.CreateDirectory(_dir); } catch { } } return _dir; } }    public static string SaveWindow(IntPtr hwnd) { try { if (!GetWindowRect(hwnd, out var r) || r.Rt <= r.L || r.B <= r.T) return null; int w = r.Rt - r.L, h = r.B - r.T; if (w <= 0 || h <= 0) return null; string name = $"窗口_{DateTime.Now:yyyyMMdd-HHmmss}.png";
                string path = Path.Combine(CaptureDir, name);
                using (var bmp = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = false;
                        try { ok = PrintWindow(hwnd, hdc, 2); }   // PW_RENDERFULLCONTENT：含 D2D 内容
                        catch { }
                        g.ReleaseHdc(hdc);
                        if (!ok)
                        {
                            // 回退：屏幕拷贝（需窗口可见）
                            try { g.CopyFromScreen(r.L, r.T, 0, 0, bmp.Size); }
                            catch { return null; }
                        }
                    }
                    bmp.Save(path, ImageFormat.Png);
                    Logger.Info("屏幕采集已保存：" + path);
                    return path;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("屏幕采集失败：" + ex.Message);
                return null;
            }
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, Rt, B; }
    }
}