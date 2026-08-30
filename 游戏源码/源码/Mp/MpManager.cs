using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ChartPlayer
{
    public enum MpRole { None, Host, Client }

    public class MpPlayerState
    {
        public string Name { get; set; } = "";
        public int Score { get; set; }
        public int Combo { get; set; }
        public double Acc { get; set; }
        public bool Finished { get; set; }
    }

    public static class MpManager
    {
        public static MpRole Role = MpRole.None;
        public static string PlayerName = "玩家";
        public static Chart CurrentChart;
        public const int Port = 27566;

        static readonly object _lock = new object();
        static TcpListener _listener;
        static readonly List<HostConn> _conns = new List<HostConn>();
        static TcpClient _client;
        static StreamReader _clientReader;
        static StreamWriter _clientWriter;
        static volatile bool _running;

        public static readonly Dictionary<string, MpPlayerState> Players = new Dictionary<string, MpPlayerState>();

        public static event Action<string> Log;
        public static event Action<string[]> OnRoster;
        public static event Action<string, string> OnChart;
        public static event Action OnStart;
        public static event Action<MpPlayerState> OnHit;
        public static event Action<MpPlayerState> OnFinish;
        public static event Action<string> OnError;

        class HostConn
        {
            public TcpClient Tcp; public StreamReader R; public StreamWriter W; public Thread Th; public string Name = "";
        }

        public static string LocalIps()
        {
            try
            {
                var list = new List<string>();
                foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork) list.Add(a.ToString());
                return list.Count > 0 ? string.Join(", ", list) : "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }

        static void Fire(Action a) { try { a?.Invoke(); } catch { } }

        /// <summary>修改玩家名：本地立即生效，房内自动广播并刷新成员名单。</summary>
        public static void Rename(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (newName == PlayerName) return;
            string old = PlayerName;
            lock (_lock)
            {
                if (Players.TryGetValue(old, out var st)) { Players.Remove(old); st.Name = newName; Players[newName] = st; }
            }
            PlayerName = newName;
            if (Role == MpRole.Host)
            {
                BroadcastRoster();
            }
            else if (Role == MpRole.Client)
            {
                SendToHost(new { type = "rename", name = newName });
            }
            Fire(() => Log?.Invoke("玩家名已改为：" + newName));
        }

        /* ---------- 主机 ---------- */
        public static void StartHost()
        {
            Stop();
            Role = MpRole.Host;
            _running = true;
            lock (_lock) Players.Clear();
            lock (_lock) Players[PlayerName] = new MpPlayerState { Name = PlayerName };
            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                Fire(() => Log?.Invoke("✅ 房间已创建（端口 " + Port + "） 本机 IP：" + LocalIps()));
                var th = new Thread(AcceptLoop) { IsBackground = true };
                th.Start();
                BroadcastRoster();   // 建房后立即把房主自己放进成员名单
            }
            catch (Exception ex)
            {
                Fire(() => Log?.Invoke("❌ 创建房间失败：" + ex.Message));
                Role = MpRole.None;
            }
        }

        static void AcceptLoop()
        {
            while (_running && Role == MpRole.Host)
            {
                try
                {
                    var tcp = _listener.AcceptTcpClient();
                    tcp.NoDelay = true;
                    var s = tcp.GetStream();
                    var conn = new HostConn
                    {
                        Tcp = tcp,
                        R = new StreamReader(s, Encoding.UTF8),
                        W = new StreamWriter(s, Encoding.UTF8) { AutoFlush = true }
                    };
                    lock (_lock) _conns.Add(conn);
                    conn.Th = new Thread(() => HostReadLoop(conn)) { IsBackground = true };
                    conn.Th.Start();
                }
                catch { }
            }
        }

        static void HostReadLoop(HostConn c)
        {
            try
            {
                string line;
                while ((line = c.R.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    HandleHostMessage(c, line);
                }
            }
            catch { }
            finally
            {
                lock (_lock)
                {
                    _conns.Remove(c);
                    if (c.Name.Length > 0) Players.Remove(c.Name);   // 断线立即从成员名单移除
                }
                try { c.R.Dispose(); c.W.Dispose(); c.Tcp.Close(); } catch { }
                Fire(() => Log?.Invoke("客户端断开：" + (c.Name.Length > 0 ? c.Name : "?") + "）"));
                BroadcastRoster();
            }
        }

        static void HandleHostMessage(HostConn c, string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.GetProperty("type").GetString();
                switch (type)
                {
                    case "join":
                        c.Name = root.GetProperty("name").GetString() ?? "玩家";
                        if (string.IsNullOrWhiteSpace(c.Name)) c.Name = "玩家";
                        lock (_lock) Players[c.Name] = new MpPlayerState { Name = c.Name };
                        Fire(() => Log?.Invoke("👤 玩家加入：" + c.Name));
                        BroadcastRoster();
                        break;
                    case "rename":
                        var newName = root.GetProperty("name").GetString();
                        if (string.IsNullOrWhiteSpace(newName)) newName = c.Name;
                        if (newName != c.Name)
                        {
                            lock (_lock)
                            {
                                if (Players.TryGetValue(c.Name, out var pr)) { Players.Remove(c.Name); pr.Name = newName; Players[newName] = pr; }
                                else Players[newName] = new MpPlayerState { Name = newName };
                            }
                            c.Name = newName;
                            Fire(() => Log?.Invoke("✏️ 玩家已改名：" + newName));
                            BroadcastRoster();
                        }
                        break;
                    case "hit":
                        var hit = ReadState(root);
                        lock (_lock) if (Players.TryGetValue(hit.Name, out var ph)) { ph.Score = hit.Score; ph.Combo = hit.Combo; ph.Acc = hit.Acc; }
                        BroadcastRaw(line, null);
                        break;
                    case "finish":
                        var fin = ReadState(root);
                        lock (_lock) if (Players.TryGetValue(fin.Name, out var pf)) { pf.Score = fin.Score; pf.Combo = fin.Combo; pf.Acc = fin.Acc; pf.Finished = true; }
                        BroadcastRaw(line, null);
                        break;
                    case "bye":
                        lock (_lock) { Players.Remove(c.Name); _conns.Remove(c); }
                        BroadcastRoster();
                        break;
                }
            }
            catch { }
        }

        static MpPlayerState ReadState(JsonElement root)
        {
            var st = new MpPlayerState();
            if (root.TryGetProperty("name", out var n)) st.Name = n.GetString() ?? "";
            if (root.TryGetProperty("score", out var sc)) st.Score = sc.GetInt32();
            if (root.TryGetProperty("combo", out var cb)) st.Combo = cb.GetInt32();
            if (root.TryGetProperty("acc", out var ac)) st.Acc = ac.GetDouble();
            if (root.TryGetProperty("finished", out var fd)) st.Finished = fd.GetBoolean();
            return st;
        }

        static void BroadcastRoster()
        {
            string[] names;
            lock (_lock) names = Players.Keys.ToArray();
            var json = JsonSerializer.Serialize(new { type = "roster", players = names });
            BroadcastRaw(json, null);
            Fire(() => OnRoster?.Invoke(names));
        }

        static void BroadcastRaw(string json, HostConn except)
        {
            lock (_lock)
                foreach (var c in _conns)
                    if (c != except)
                        try { c.W.WriteLine(json); } catch { }
        }

        /* ---------- 客户端 ---------- */
        public static void Join(string ip)
        {
            Stop();
            Role = MpRole.Client;
            _running = true;
            lock (_lock) Players.Clear();
            lock (_lock) Players[PlayerName] = new MpPlayerState { Name = PlayerName };
            try
            {
                _client = new TcpClient();
                _client.Connect(ip, Port);
                _client.NoDelay = true;
                var s = _client.GetStream();
                _clientReader = new StreamReader(s, Encoding.UTF8);
                _clientWriter = new StreamWriter(s, Encoding.UTF8) { AutoFlush = true };
                _clientWriter.WriteLine(JsonSerializer.Serialize(new { type = "join", name = PlayerName }));
                var th = new Thread(ClientReadLoop) { IsBackground = true };
                th.Start();
                Fire(() => Log?.Invoke("✅ 已连接主机：" + ip));
            }
            catch (Exception ex)
            {
                Fire(() => Log?.Invoke("❌ 加入失败：" + ex.Message));
                Fire(() => OnError?.Invoke("加入失败：" + ex.Message));
                Role = MpRole.None;
            }
        }

        static void ClientReadLoop()
        {
            try
            {
                string line;
                while ((line = _clientReader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        var type = root.GetProperty("type").GetString();
                        switch (type)
                        {
                            case "roster":
                                var list = new List<string>();
                                if (root.TryGetProperty("players", out var pl))
                                    foreach (var p in pl.EnumerateArray()) list.Add(p.GetString());
                                lock (_lock)
                                    foreach (var n in list)
                                        if (!Players.ContainsKey(n)) Players[n] = new MpPlayerState { Name = n };
                                Fire(() => OnRoster?.Invoke(list.ToArray()));
                                break;
                            case "chart":
                                Fire(() => OnChart?.Invoke(root.GetProperty("name").GetString(), root.GetProperty("text").GetString()));
                                break;
                            case "start":
                                Fire(() => OnStart?.Invoke());
                                break;
                            case "hit":
                                var hs = ReadState(root);
                                lock (_lock)
                                {
                                    if (!Players.ContainsKey(hs.Name)) Players[hs.Name] = new MpPlayerState { Name = hs.Name };
                                    var p = Players[hs.Name]; p.Score = hs.Score; p.Combo = hs.Combo; p.Acc = hs.Acc;
                                }
                                Fire(() => OnHit?.Invoke(hs));
                                break;
                            case "finish":
                                var fs = ReadState(root);
                                lock (_lock)
                                {
                                    if (!Players.ContainsKey(fs.Name)) Players[fs.Name] = new MpPlayerState { Name = fs.Name };
                                    var p = Players[fs.Name]; p.Score = fs.Score; p.Combo = fs.Combo; p.Acc = fs.Acc; p.Finished = true;
                                }
                                Fire(() => OnFinish?.Invoke(fs));
                                break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                if (_running && Role == MpRole.Client)
                    Fire(() => { Log?.Invoke("与主机断开连接"); OnError?.Invoke("与主机断开连接"); });
            }
        }

        /* ---------- 发送 ---------- */
        static void SendToHost(object o) { try { _clientWriter?.WriteLine(JsonSerializer.Serialize(o)); } catch { } }

        public static void SendChart(string name, string text)
        {
            var json = JsonSerializer.Serialize(new { type = "chart", name = name, text = text });
            if (Role == MpRole.Host) BroadcastRaw(json, null);
            else SendToHost(json);
        }

        public static void SendStart()
        {
            var json = JsonSerializer.Serialize(new { type = "start" });
            if (Role == MpRole.Host) BroadcastRaw(json, null);
            else SendToHost(json);
            Fire(() => OnStart?.Invoke());
        }

        public static void ReportHit(int score, int combo, double acc)
        {
            var json = JsonSerializer.Serialize(new { type = "hit", name = PlayerName, score = score, combo = combo, acc = acc });
            if (Role == MpRole.Host)
            {
                lock (_lock) if (Players.TryGetValue(PlayerName, out var p)) { p.Score = score; p.Combo = combo; p.Acc = acc; }
                BroadcastRaw(json, null);
            }
            else SendToHost(json);
        }

        public static void ReportFinish(int score, int combo, double acc)
        {
            var json = JsonSerializer.Serialize(new { type = "finish", name = PlayerName, score = score, combo = combo, acc = acc, finished = true });
            if (Role == MpRole.Host)
            {
                lock (_lock) if (Players.TryGetValue(PlayerName, out var p)) { p.Score = score; p.Combo = combo; p.Acc = acc; p.Finished = true; }
                BroadcastRaw(json, null);
            }
            else SendToHost(json);
        }

        public static void Leave()
        {
            if (Role == MpRole.Client) { try { SendToHost(new { type = "bye" }); } catch { } }
            Stop();
            Fire(() => Log?.Invoke("已离开联机"));
        }

        static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _client?.Close(); } catch { }
            try { _clientReader?.Dispose(); } catch { }
            try { _clientWriter?.Dispose(); } catch { }
            lock (_lock)
            {
                foreach (var c in _conns) { try { c.R.Dispose(); c.W.Dispose(); c.Tcp.Close(); } catch { } }
                _conns.Clear();
            }
            _listener = null; _client = null; _clientReader = null; _clientWriter = null;
            Role = MpRole.None;
        }
    }
}
