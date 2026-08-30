using System;
using System.IO;
using System.Windows.Media;

namespace ChartPlayer
{
    public class AudioPlayer : IDisposable
    {
        MediaPlayer _mp = new MediaPlayer();
        double _volume = 0.8;   // 当前音量（0~1）
        public bool HasMedia { get; private set; }
        public event Action Ended;

        public AudioPlayer() { _mp.MediaEnded += (s, e) => Ended?.Invoke(); }

        public void Open(string path)
        {
            if (!File.Exists(path)) { HasMedia = false; return; }
            _mp.Close();
            _mp.Open(new Uri(path));
            HasMedia = true;
            _mp.Volume = _volume;
        }

        /// <summary>动态调整音量（0~1），立即作用到正在播放的音频。</summary>
        public void SetVolume(double v)
        {
            _volume = Math.Max(0, Math.Min(1, v));
            if (HasMedia) _mp.Volume = _volume;
        }

        public void Close()
        {
            try { _mp.Close(); } catch { }
            HasMedia = false;
        }

        public double PositionMs => HasMedia ? _mp.Position.TotalMilliseconds : 0;
        public void Play() { if (HasMedia) _mp.Play(); }
        public void Pause() { if (HasMedia) _mp.Pause(); }
        public void Stop() { if (HasMedia) _mp.Stop(); }
        public void Restart() { if (HasMedia) { _mp.Stop(); _mp.Play(); } }

        /// <summary>跳转到指定时间（跳过开头空白用）。</summary>
        public void Seek(double ms)
        {
            if (!HasMedia) return;
            try { _mp.Position = TimeSpan.FromMilliseconds(Math.Max(0, ms)); } catch { }
        }

        public void Dispose() => _mp.Close();
    }
}
