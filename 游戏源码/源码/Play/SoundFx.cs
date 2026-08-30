using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ChartPlayer
{
    /// <summary>
    /// Milestone 打击音效引擎：
    /// 程序内合成多风格打击音（经典 / 电子 / 木鱼），按判定等级区分音高与音色，
    /// 用 winmm PlaySound 异步播放——多个音效可互相叠加，不互相打断。
    /// </summary>
    public static class SoundFx
    {
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        static extern bool PlaySound(byte[] data, IntPtr hmod, uint flags);

        const uint SND_MEMORY = 0x0004;
        const uint SND_ASYNC = 0x0001;
        const uint SND_NODEFAULT = 0x0002;

        static readonly object _lock = new object();
        static readonly Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>();

        static bool _enabled = true;
        static int _volume = 70;
        static int _style = 0;

        public static bool Enabled { get => _enabled; set => _enabled = value; }
        public static int Volume { get => _volume; set => _volume = Math.Max(0, Math.Min(100, value)); }
        public static int Style
        {
            get => _style;
            set
            {
                int v = Math.Max(0, Math.Min(2, value));
                if (v == _style) return;
                _style = v;
                lock (_lock) _cache.Clear();
            }
        }

        /// <summary>获取样本（按需合成并缓存）。</summary>
        static byte[] Sample(string key, Func<double, double, double> osc, double f1, double f2, double durMs, double decay, double gainMul = 1.0)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var hit)) return hit;
                const int sr = 44100;
                int n = Math.Max(8, (int)(sr * durMs / 1000.0));
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                int dataLen = n * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataLen);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(sr);
                w.Write(sr * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataLen);
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / sr;
                    double env = Math.Exp(-t * decay);
                    double s = osc(t, i);
                    w.Write((short)(Math.Max(-1.0, Math.Min(1.0, s * env)) * short.MaxValue * 0.62 * gainMul));
                }
                w.Flush();
                var bytes = ms.ToArray();
                _cache[key] = bytes;
                ms.Dispose();
                return bytes;
            }
        }

        static double Sine(double t, double i) => Math.Sin(2 * Math.PI * 880 * t) * 0.8 + Math.Sin(2 * Math.PI * 1320 * t) * 0.2;
        static double Sine2(double t, double i) => Math.Sin(2 * Math.PI * 440 * t) * 0.7 + Math.Sin(2 * Math.PI * 660 * t) * 0.3;
        static double Square(double t, double i) => Math.Sign(Math.Sin(2 * Math.PI * 440 * t)) * 0.5 + Math.Sin(2 * Math.PI * 880 * t) * 0.3;
        static double Wood(double t, double i) => Math.Sin(2 * Math.PI * 1200 * Math.Exp(-t * 60) * t) * 0.9;

        /// <summary>
        /// 等级索引：0=最佳判定（PERFECT/Best/Marvelous），越大越差；-1 = MISS。
        /// 风格与音高随等级变化，让玩家"听得到"判定好坏。
        /// </summary>
        static byte[] SampleFor(int level)
        {
            string style = _style switch { 1 => "E", 2 => "W", _ => "C" };
            string key = style + ":" + level + ":" + _volume;
            double vol = 0.18 + 0.82 * _volume / 100.0;
            if (level < 0)
                return Sample("MISS" + style + ":" + _volume, Wood, 90, 60, 70, 42, 0.9 * vol);
            switch (_style)
            {
                case 1:   // 电子：方波 + 噪声，音高随判定
                    return Sample(key, Square, 300 + level * 160, 460 + level * 240, 34, 60, vol);
                case 2:   // 木鱼：短促敲击
                    return Sample(key, Wood, 900 + level * 260, 0, 16, 90, vol);
                default:  // 经典：正弦二重音
                    return Sample(key, level == 0 ? Sine : Sine2, 880, 1320, 26, 95, vol);
            }
        }

        /// <summary>判定名 → 等级索引（按当前判定表）。</summary>
        public static int LevelOf(string grade)
        {
            if (string.IsNullOrEmpty(grade) || grade == "MISS") return -1;
            for (int i = 0; i < JudgeSettings.Levels.Count; i++)
                if (JudgeSettings.Levels[i].Name == grade) return i;
            return 0;
        }

        /// <summary>播放一次打击音（grade 为判定名；null = 默认最佳判定音）。</summary>
        public static void Hit(string grade = null)
        {
            if (!_enabled) return;
            try
            {
                int lv = GameSettings.HitsoundPerJudge ? LevelOf(grade) : 0;
                var data = SampleFor(lv);
                if (data != null) PlaySound(data, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            }
            catch { }
        }

        /// <summary>播放 MISS 音。</summary>
        public static void PlayMiss() => Hit("MISS");

        /// <summary>试听当前风格/判定音效（设置界面用）。</summary>
        public static void Preview(int level = 0) => Hit(level < 0 ? "MISS" : JudgeSettings.Levels.Count > level && level >= 0 ? JudgeSettings.Levels[level].Name : null);
    }
}
