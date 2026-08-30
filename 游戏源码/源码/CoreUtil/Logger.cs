using System;
using System.IO;
using System.Text;

namespace ChartPlayer
{
    /// <summary>
    /// 线程安全的日志记录器：每次程序启动都会新建一个日志文件
    /// （Log\chartplayer-yyyyMMdd-HHmmss.log），本次运行的全部日志写入该会话文件。
    /// </summary>
    public static class Logger
    {
        static readonly object _lock = new object();
        static readonly string _sessionFile;
        static bool _inited;

        static Logger()
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                // 会话文件名：chartplayer-20260816-153000.log（每次启动唯一）
                _sessionFile = Path.Combine(LogDir,
                    "chartplayer-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            }
            catch { _sessionFile = null; }
        }

        public static string LogDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");

        /// <summary>当前会话日志文件路径。</summary>
        public static string LogPath
        {
            get
            {
                if (_sessionFile != null) return _sessionFile;
                return Path.Combine(LogDir, "chartplayer.log");
            }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);
        public static void Error(string msg, Exception ex)
            => Write("ERROR", msg + " | " + (ex?.Message ?? "") + " | " + (ex?.StackTrace ?? ""));

        static void Write(string level, string msg)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                // 首次写入时建立会话日志文件（若同名已存在则覆盖，保证一次启动一份）
                lock (_lock)
                {
                    if (!_inited)
                    {
                        _inited = true;
                        if (File.Exists(LogPath)) File.Delete(LogPath);
                        File.WriteAllText(LogPath, "Milestone 会话日志 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n", Encoding.UTF8);
                    }
                    string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [" + level + "] " + msg;
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                    Console.WriteLine(line);
                }
            }
            catch { }
        }

        /// <summary>用系统默认程序打开日志文件（记事本）。</summary>
        public static void OpenLog()
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                var path = LogPath;
                if (!File.Exists(path)) File.WriteAllText(path, "Milestone 日志\n", Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>读取日志末尾若干行（用于在界面内展示）。</summary>
        public static string ReadTail(int maxLines = 200)
        {
            try
            {
                var path = LogPath;
                if (!File.Exists(path)) return "（日志为空）";
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - maxLines);
                return string.Join(Environment.NewLine, lines, start, lines.Length - start);
            }
            catch (Exception ex) { return "读取日志失败：" + ex.Message; }
        }
    }
}
