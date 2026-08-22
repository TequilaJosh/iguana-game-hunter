using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameTracker.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameTracker.Services
{
    /// <summary>
    /// A tiny, dependency-free loopback server that powers the OBS Browser-source
    /// overlay (overlay/LiveOverlay.html). It does two jobs:
    ///
    ///   GET /            -> serves the overlay HTML (so OBS can point at a URL).
    ///   GET /ws          -> upgrades to a WebSocket and PUSHES state + chat as JSON.
    ///
    /// It uses a raw <see cref="TcpListener"/> on 127.0.0.1 (no admin rights or
    /// netsh urlacl reservation needed, unlike HttpListener), then hands each
    /// upgraded socket to <see cref="WebSocket.CreateFromStream"/> for framing.
    ///
    /// Everything is best-effort and wrapped in try/catch: the overlay is a
    /// streaming convenience and must never be able to crash the app. A new client
    /// immediately receives a full {type:"snapshot"} so it can render without
    /// waiting for the next change.
    /// </summary>
    public static class OverlayServer
    {
        // ---- Configurable server-side values ----
        public const int DefaultPort = 3620;          // ws://localhost:3620/ws

        /// <summary>The port the server is currently bound to (read by the UI/README).</summary>
        public static int Port { get; private set; } = DefaultPort;

        /// <summary>True while the listener is up and accepting connections.</summary>
        public static bool IsRunning => _running;

        /// <summary>Last bind/start failure message (e.g. port already in use), or null.</summary>
        public static string? LastError { get; private set; }

        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private sealed class Client
        {
            public WebSocket Socket = null!;
            public readonly SemaphoreSlim SendLock = new(1, 1);
            public volatile bool IsEffects;   // set by a {"type":"hello","client":"effects"} message
        }

        private static readonly object Gate = new();
        private static readonly List<Client> Clients = new();
        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static volatile bool _running;

        // Last-known snapshot, sent verbatim to every newly connected client.
        private static object _state = new { live = false };
        private static object[] _chat = Array.Empty<object>();
        private static JToken? _layout;   // per-element layout (position/size/font), or null = default
        private static JToken? _presets;  // saved layout presets (editor-only)
        private static object? _style;    // chat style: { mode, colors[] }
        private static object? _chatters; // counts + chatters list state
        private static object? _panels;   // custom text-panel overlays (wire form)
        private static object? _goals;    // goal bars (wire form)
        private static object? _counters; // per-game counters (wire form)
        private static object? _poll;     // active poll (wire form; null = none)
        private static readonly List<object> _activity = new(); // live activity feed (gifts/follows/subs…)
        private static readonly Dictionary<string, object> _latestByKind = new(); // newest item per kind (ticker)
        private static JToken? _ticker;   // ticker banner config (edited live in /ticker?edit)
        private const int ActivityMax = 25;
        private static string? _panelHtml; // PanelOverlay.html template (index injected per request)

        // The overlay HTML (loaded once, from the embedded resource or disk).
        private static string _html = string.Empty;

        public static int ActiveClients { get { lock (Gate) return Clients.Count; } }

        /// <summary>True while a dedicated effects-overlay page is connected.</summary>
        public static bool HasEffectsClient
        {
            get { lock (Gate) return Clients.Any(c => c.IsEffects); }
        }

        // -----------------------------------------------------------------
        //  Lifecycle
        // -----------------------------------------------------------------

        public static bool Start()
        {
            if (_running) return true;
            try
            {
                Port = NormalizePort(SettingsService.LoadOverlayPort());
                _html = LoadOverlayHtml();          // injects the live port for file:// use
                _layout = ParseLayout(SettingsService.LoadOverlayLayout());
                _presets = ParseLayout(SettingsService.LoadOverlayPresets());
                _ticker = ParseLayout(SettingsService.LoadOverlayTicker());   // ticker banner config
                var f = SettingsService.LoadChatFeatures();
                _style = new { mode = f.ChatStyle, colors = f.BoxColors.ToArray() };
                _panels = PanelsToWire(SettingsService.LoadTextPanels());
                _goals = GoalsToWire(SettingsService.LoadGoals());
                _counters = CountersToWire(SettingsService.LoadCounters());
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _running = true;
                LastError = null;
                _ = AcceptLoop(_cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                LastError = ex.Message;             // e.g. "address already in use"
                return false;
            }
        }

        /// <summary>Stop and re-start on the currently configured port (after a port change).</summary>
        public static bool Restart()
        {
            Stop();
            return Start();
        }

        private static int NormalizePort(int p) => (p is >= 1 and <= 65535) ? p : DefaultPort;

        public static void Stop()
        {
            _running = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            lock (Gate)
            {
                foreach (var c in Clients)
                {
                    try { c.Socket.Abort(); } catch { }
                    try { c.Socket.Dispose(); } catch { }
                }
                Clients.Clear();
            }
        }

        private static async Task AcceptLoop(CancellationToken ct)
        {
            while (_running && !ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
                catch { break; } // listener stopped / cancelled
                _ = HandleClient(tcp, ct); // fire-and-forget per connection
            }
        }

        // -----------------------------------------------------------------
        //  Per-connection handling: minimal HTTP, optional WS upgrade
        // -----------------------------------------------------------------

        private static async Task HandleClient(TcpClient tcp, CancellationToken ct)
        {
            try
            {
                tcp.NoDelay = true;
                using var stream = tcp.GetStream();

                var (method, path, headers) = await ReadRequest(stream, ct);
                if (method == null)
                {
                    tcp.Close();
                    return;
                }

                bool wantsWs = headers.TryGetValue("upgrade", out var up) &&
                               up.Contains("websocket", StringComparison.OrdinalIgnoreCase);

                // Route on the path only; ignore any ?query (e.g. /?edit for the editor).
                var route = path.Contains('?') ? path[..path.IndexOf('?')] : path;

                if (wantsWs && (route == "/ws"))
                {
                    await Upgrade(stream, headers, ct);
                    // Upgrade() owns the socket until it closes; keep tcp alive.
                    return;
                }

                if (method == "GET" && (route == "/" || route == "/index.html" ||
                                        route == "/live" || route == "/overlay" ||
                                        route == "/LiveOverlay.html"))
                {
                    await ServeHtml(stream, ct);
                }
                else if (method == "GET" && route.StartsWith("/fx/", StringComparison.Ordinal))
                {
                    await ServeRedeemImage(stream, route, ct);
                }
                else if (method == "GET" && route == "/fonts")
                {
                    await ServeFonts(stream, ct);
                }
                else if (method == "GET" && route == "/font/saturn.ttf")
                {
                    await ServeBundledFont(stream, ct);
                }
                else if (method == "GET" && route.StartsWith("/panel/", StringComparison.Ordinal))
                {
                    await ServePanel(stream, route, ct);
                }
                else if (method == "GET" && route.StartsWith("/pimg/", StringComparison.Ordinal))
                {
                    await ServePanelImage(stream, route, ct);
                }
                else if (method == "GET" && route.StartsWith("/fxvideo/", StringComparison.Ordinal))
                {
                    await ServeVideo(stream, route, headers, ct);
                }
                else if (method == "GET" && (route == "/effects" || route == "/effects/"))
                {
                    await ServeEmbeddedHtml(stream, "EffectsOverlay.html", ct);
                }
                else if (method == "GET" && (route == "/ticker" || route == "/ticker/"))
                {
                    await ServeEmbeddedHtml(stream, "TickerOverlay.html", ct);
                }
                else
                {
                    await WriteSimple(stream, "404 Not Found", "text/plain", "Not found", ct);
                }
                tcp.Close();
            }
            catch
            {
                try { tcp.Close(); } catch { }
            }
        }

        private static async Task<(string? method, string path, Dictionary<string, string> headers)>
            ReadRequest(NetworkStream stream, CancellationToken ct)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buf = new byte[8192];
            var sb = new StringBuilder();
            int total = 0;

            // Read until the end of the header block (\r\n\r\n) or a sane cap.
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), ct);
                if (n <= 0) break;
                total += n;
                sb.Clear();
                sb.Append(Encoding.ASCII.GetString(buf, 0, total));
                if (sb.ToString().Contains("\r\n\r\n")) break;
            }

            var text = sb.ToString();
            var lines = text.Split("\r\n");
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
                return (null, "", headers);

            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return (null, "", headers);
            string method = parts[0];
            string path = parts[1];

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0) break;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                headers[line[..c].Trim()] = line[(c + 1)..].Trim();
            }
            return (method, path, headers);
        }

        private static async Task ServeHtml(NetworkStream stream, CancellationToken ct)
        {
            // Inject the user's "chat lines" choice fresh on every load, so changing it
            // just needs an OBS source refresh (no server restart).
            var html = _html.Replace("maxMessages: 20,",
                "maxMessages: " + SettingsService.LoadOverlayChatLines() + ",");
            var body = Encoding.UTF8.GetBytes(html);
            var head =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Cache-Control: no-store\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
            await stream.WriteAsync(body, ct);
            await stream.FlushAsync(ct);
        }

        // Serve a configured redeem image (/fx/<index>). Only files the streamer picked in
        // the redeems editor are reachable — the index maps into that saved list.
        private static async Task ServeRedeemImage(NetworkStream stream, string route, CancellationToken ct)
        {
            string? path = null;
            try
            {
                if (int.TryParse(route.Substring(4), out int idx))
                {
                    var redeems = SettingsService.LoadChatFeatures().Redeems;
                    if (idx >= 0 && idx < redeems.Count) path = redeems[idx].ImagePath;
                }
            }
            catch { }
            await ServeImageFile(stream, path, ct);
        }

        // Serve a text-panel side image (/pimg/<panel 1-5>/<left|right>). Only files the
        // streamer picked in the Text Overlays editor are reachable.
        private static async Task ServePanelImage(NetworkStream stream, string route, CancellationToken ct)
        {
            string? path = null;
            try
            {
                var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries); // pimg, N, side
                if (parts.Length >= 3 && int.TryParse(parts[1], out int n) && n is >= 1 and <= 5)
                {
                    var panels = SettingsService.LoadTextPanels();
                    if (n <= panels.Count)
                    {
                        var panel = panels[n - 1];
                        var key = parts[2].Split('?')[0];
                        if (key.Equals("left", StringComparison.OrdinalIgnoreCase))
                            path = panel.LeftImage;
                        else if (key.Equals("right", StringComparison.OrdinalIgnoreCase))
                            path = panel.RightImage;
                        else if (key.StartsWith("line", StringComparison.OrdinalIgnoreCase) &&
                                 int.TryParse(key.AsSpan(4), out int li) &&
                                 li >= 0 && li < panel.Lines.Count)
                            path = panel.Lines[li].Image;
                    }
                }
            }
            catch { }
            await ServeImageFile(stream, path, ct);
        }

        // Serve a redeem video (/fxvideo/<index>) with HTTP range support so <video> in OBS
        // can seek/stream it. Only files the streamer picked in the redeems editor are reachable.
        private static async Task ServeVideo(NetworkStream stream, string route,
            Dictionary<string, string> headers, CancellationToken ct)
        {
            string? path = null;
            try
            {
                var idStr = route.Substring("/fxvideo/".Length).Split('?')[0];
                if (int.TryParse(idStr, out int idx))
                {
                    var redeems = SettingsService.LoadChatFeatures().Redeems;
                    if (idx >= 0 && idx < redeems.Count) path = redeems[idx].VideoPath;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await WriteSimple(stream, "404 Not Found", "text/plain", "Not found", ct);
                return;
            }

            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".ogv" or ".ogg" => "video/ogg",
                ".mov" => "video/quicktime",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                _ => "application/octet-stream",
            };

            try
            {
                long len = new FileInfo(path).Length;
                long start = 0, end = len - 1;
                bool partial = false;
                if (headers.TryGetValue("range", out var range) &&
                    range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    var spec = range.Substring(6).Split('-');
                    if (long.TryParse(spec[0], out var s)) { start = s; partial = true; }
                    if (spec.Length > 1 && long.TryParse(spec[1], out var e2) && e2 >= start) end = e2;
                }
                if (start < 0 || start >= len) { start = 0; end = len - 1; partial = false; }
                long count = end - start + 1;

                var head =
                    (partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n") +
                    "Content-Type: " + mime + "\r\n" +
                    "Accept-Ranges: bytes\r\n" +
                    (partial ? $"Content-Range: bytes {start}-{end}/{len}\r\n" : "") +
                    "Content-Length: " + count + "\r\n" +
                    "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[64 * 1024];
                long remaining = count;
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int r = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
                    if (r <= 0) break;
                    await stream.WriteAsync(buffer.AsMemory(0, r), ct);
                    remaining -= r;
                }
                await stream.FlushAsync(ct);
            }
            catch { /* client aborted / seek */ }
        }

        private static async Task ServeImageFile(NetworkStream stream, string? path, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    await WriteSimple(stream, "404 Not Found", "text/plain", "Not found", ct);
                    return;
                }

                var mime = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    _ => "application/octet-stream",
                };
                var body = await File.ReadAllBytesAsync(path, ct);
                var head =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: " + mime + "\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "Content-Length: " + body.Length + "\r\n" +
                    "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
                await stream.WriteAsync(body, ct);
                await stream.FlushAsync(ct);
            }
            catch { /* drop */ }
        }

        /// <summary>Fonts bundled with the app (name shown in the picker; loaded via @font-face).</summary>
        public const string BundledFontName = "MR. Saturn";

        private static byte[]? _bundledFont;

        // Serve the bundled "MR. Saturn" font (embedded TTF) for the overlay's @font-face.
        private static async Task ServeBundledFont(NetworkStream stream, CancellationToken ct)
        {
            try
            {
                if (_bundledFont == null)
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var name = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("SaturnBoingNew.ttf", StringComparison.OrdinalIgnoreCase));
                    if (name != null)
                    {
                        using var s = asm.GetManifestResourceStream(name);
                        if (s != null)
                        {
                            using var ms = new MemoryStream();
                            await s.CopyToAsync(ms, ct);
                            _bundledFont = ms.ToArray();
                        }
                    }
                }
                if (_bundledFont == null)
                {
                    await WriteSimple(stream, "404 Not Found", "text/plain", "Not found", ct);
                    return;
                }
                var head =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: font/ttf\r\n" +
                    "Cache-Control: max-age=86400\r\n" +
                    "Content-Length: " + _bundledFont.Length + "\r\n" +
                    "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
                await stream.WriteAsync(_bundledFont, ct);
                await stream.FlushAsync(ct);
            }
            catch { /* drop */ }
        }

        // Serve a text-panel overlay page (/panel/1 .. /panel/5) with its index baked in.
        private static async Task ServePanel(NetworkStream stream, string route, CancellationToken ct)
        {
            if (!int.TryParse(route.Substring(7).TrimEnd('/'), out int idx) || idx is < 1 or > 5)
            {
                await WriteSimple(stream, "404 Not Found", "text/plain", "Not found", ct);
                return;
            }

            if (_panelHtml == null)
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var name = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("PanelOverlay.html", StringComparison.OrdinalIgnoreCase));
                    if (name != null)
                    {
                        using var s = asm.GetManifestResourceStream(name);
                        if (s != null)
                        {
                            using var r = new StreamReader(s, Encoding.UTF8);
                            _panelHtml = await r.ReadToEndAsync(ct);
                        }
                    }
                }
                catch { }
            }
            if (_panelHtml == null)
            {
                await WriteSimple(stream, "404 Not Found", "text/plain", "PanelOverlay.html missing", ct);
                return;
            }

            var html = _panelHtml.Replace("__PANEL_INDEX__", idx.ToString());
            await WriteSimple(stream, "200 OK", "text/html", html, ct);
        }

        // Small cache of embedded HTML pages served as-is (e.g. the effects overlay).
        private static readonly Dictionary<string, string> _embeddedHtml = new();

        private static async Task ServeEmbeddedHtml(NetworkStream stream, string resourceName, CancellationToken ct)
        {
            string? html;
            lock (Gate) _embeddedHtml.TryGetValue(resourceName, out html);
            if (html == null)
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var name = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
                    if (name != null)
                    {
                        using var s = asm.GetManifestResourceStream(name);
                        if (s != null)
                        {
                            using var r = new StreamReader(s, Encoding.UTF8);
                            html = await r.ReadToEndAsync(ct);
                            lock (Gate) _embeddedHtml[resourceName] = html;
                        }
                    }
                }
                catch { }
            }
            if (html == null)
            {
                await WriteSimple(stream, "404 Not Found", "text/plain", resourceName + " missing", ct);
                return;
            }
            await WriteSimple(stream, "200 OK", "text/html", html, ct);
        }

        private static string? _fontsJson; // enumerated once; installed fonts rarely change mid-run

        // The fonts installed on this PC, for the overlay editor's font picker. The OBS
        // browser renders on the same machine, so every listed font will work there too.
        private static async Task ServeFonts(NetworkStream stream, CancellationToken ct)
        {
            if (_fontsJson == null)
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var en = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
                    foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies)
                    {
                        try
                        {
                            var name = ff.FamilyNames.TryGetValue(en, out var n)
                                ? n
                                : ff.FamilyNames.Values.FirstOrDefault() ?? ff.Source;
                            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                        }
                        catch { /* skip odd families */ }
                    }
                }
                catch { /* fall through to whatever we collected */ }
                // Bundled bonus font first so it's easy to find.
                var all = new List<string> { BundledFontName };
                all.AddRange(names.Where(n => !string.Equals(n, BundledFontName, StringComparison.OrdinalIgnoreCase)));
                _fontsJson = JsonConvert.SerializeObject(all);
            }
            await WriteSimple(stream, "200 OK", "application/json", _fontsJson, ct);
        }

        private static async Task WriteSimple(NetworkStream stream, string status, string type,
            string body, CancellationToken ct)
        {
            var b = Encoding.UTF8.GetBytes(body);
            var head =
                "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + type + "; charset=utf-8\r\n" +
                "Content-Length: " + b.Length + "\r\n" +
                "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
            await stream.WriteAsync(b, ct);
            await stream.FlushAsync(ct);
        }

        // -----------------------------------------------------------------
        //  WebSocket upgrade + receive loop
        // -----------------------------------------------------------------

        private static async Task Upgrade(NetworkStream stream,
            Dictionary<string, string> headers, CancellationToken ct)
        {
            if (!headers.TryGetValue("Sec-WebSocket-Key", out var key) || string.IsNullOrEmpty(key))
                return;

            string accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid)));

            var resp =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), ct);
            await stream.FlushAsync(ct);

            var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null,
                keepAliveInterval: TimeSpan.FromSeconds(30));
            var client = new Client { Socket = socket };

            lock (Gate) Clients.Add(client);

            // Send the current snapshot immediately so the overlay renders at once.
            object snapshot;
            lock (Gate) snapshot = new
            {
                type = "snapshot", state = _state, chat = _chat,
                layout = _layout, presets = _presets, style = _style, chatters = _chatters,
                panels = _panels, morph = MorphSnapshot(), goals = _goals, counters = _counters, poll = _poll,
                activity = _activity.ToArray(),
                activityLatest = new Dictionary<string, object>(_latestByKind),
                ticker = _ticker,
            };
            await SendText(client, JsonConvert.SerializeObject(snapshot), ct);

            await ReceiveLoop(client, ct);

            lock (Gate) Clients.Remove(client);
            try { socket.Dispose(); } catch { }
        }

        private static async Task ReceiveLoop(Client client, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            try
            {
                while (client.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult r;
                    try { r = await client.Socket.ReceiveAsync(buffer, ct); }
                    catch { break; }

                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await client.Socket.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure, null, ct);
                        }
                        catch { }
                        break;
                    }
                    if (r.MessageType != WebSocketMessageType.Text) continue;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    if (!r.EndOfMessage) continue;

                    var msg = sb.ToString();
                    sb.Clear();
                    HandleClientMessage(client, msg, ct);
                }
            }
            catch { /* drop the client */ }
        }

        private static void HandleClientMessage(Client client, string msg, CancellationToken ct)
        {
            try
            {
                // Heartbeat.
                if (msg.Contains("\"ping\""))
                {
                    _ = SendText(client, "{\"type\":\"pong\"}", ct);
                    return;
                }

                // A client identifying itself (the effects overlay says hello so video/image
                // redeems can be routed to it instead of the main overlay).
                if (msg.Contains("\"hello\""))
                {
                    if (msg.Contains("\"effects\"")) client.IsEffects = true;
                    return;
                }

                // Layout editor saved a new arrangement -> persist and push to every client
                // (so the OBS source updates live while you edit in the browser).
                if (msg.Contains("\"saveLayout\""))
                {
                    var o = JObject.Parse(msg);
                    if ((string?)o["type"] == "saveLayout" && o["layout"] is JToken layout)
                    {
                        SettingsService.SaveOverlayLayout(layout.ToString(Formatting.None));
                        lock (Gate) _layout = layout;
                        Broadcast(new { type = "layout", layout });
                    }
                }

                // Ticker editor (/ticker?edit) saved a config -> persist and push live to every
                // ticker source so the OBS banner updates immediately.
                if (msg.Contains("\"saveTicker\""))
                {
                    var o = JObject.Parse(msg);
                    if ((string?)o["type"] == "saveTicker" && o["ticker"] is JToken tk)
                    {
                        SettingsService.SaveOverlayTicker(tk.ToString(Formatting.None));
                        lock (Gate) _ticker = tk;
                        Broadcast(new { type = "tickerConfig", ticker = tk });
                    }
                }

                // Editor saved its preset list (editor-only, not broadcast to viewers).
                if (msg.Contains("\"savePresets\""))
                {
                    var o = JObject.Parse(msg);
                    if ((string?)o["type"] == "savePresets" && o["presets"] is JToken presets)
                    {
                        SettingsService.SaveOverlayPresets(presets.ToString(Formatting.None));
                        lock (Gate) _presets = presets;
                    }
                }
            }
            catch { /* ignore malformed client messages */ }
        }

        private static JToken? ParseLayout(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JToken.Parse(json); }
            catch { return null; }
        }

        // -----------------------------------------------------------------
        //  Broadcasting
        // -----------------------------------------------------------------

        /// <summary>Push the current "now playing" + challenges state to all clients.</summary>
        public static void SetState(bool live, string title, string elapsed, string requester,
            IReadOnlyList<string> challenges)
        {
            if (!_running) return;
            var state = new
            {
                live,
                title = title ?? string.Empty,
                elapsed = elapsed ?? string.Empty,
                requester = requester ?? string.Empty,
                challenges = (challenges ?? Array.Empty<string>()).ToArray(),
            };
            lock (Gate) _state = state;
            Broadcast(new { type = "state", state });
        }

        /// <summary>Push the current recent chat list to all clients.</summary>
        public static void SetChat(IReadOnlyList<ChatMessage> messages)
        {
            if (!_running) return;
            var arr = (messages ?? Array.Empty<ChatMessage>()).Select(ToWire).ToArray();
            lock (Gate) _chat = arr;
            Broadcast(new { type = "chat", messages = arr });
        }

        public static void ClearChat()
        {
            if (!_running) return;
            lock (Gate) _chat = Array.Empty<object>();
            Broadcast(new { type = "chatClear" });
        }

        /// <summary>Push the chat style (log/boxes + palette) to all clients.</summary>
        public static void SetStyle(string mode, IEnumerable<string> colors)
        {
            if (!_running) return;
            var style = new { mode, colors = (colors ?? Array.Empty<string>()).ToArray() };
            lock (Gate) _style = style;
            Broadcast(new { type = "style", style });
        }

        /// <summary>Push chatter counts + the active chatters list to all clients.</summary>
        public static void SetChatters(object chatters)
        {
            if (!_running) return;
            lock (Gate) _chatters = chatters;
            Broadcast(new { type = "chatters", chatters });
        }

        private static object PanelsToWire(IEnumerable<TextPanel>? panels) =>
            (panels ?? Enumerable.Empty<TextPanel>()).Take(5).Select(p => new
            {
                header = new
                {
                    text = p.HeaderText ?? string.Empty,
                    font = p.HeaderFont ?? string.Empty,
                    size = p.HeaderSize,
                    color = p.HeaderColor ?? "#7cc44a",
                },
                left = new
                {
                    text = p.LeftText ?? string.Empty,
                    font = p.LeftFont ?? string.Empty,
                    size = p.LeftSize,
                    color = p.LeftColor ?? "#e8e0c4",
                    img = !string.IsNullOrWhiteSpace(p.LeftImage) && File.Exists(p.LeftImage),
                    imgw = Math.Clamp(p.LeftImageWidth, 20, 1600),
                    imgv = ImageVersion(p.LeftImage),
                    dir = p.LeftDir == "h" ? "h" : "v",
                    scroll = p.LeftScroll,
                    speed = p.LeftSpeed,
                },
                right = new
                {
                    text = p.RightText ?? string.Empty,
                    font = p.RightFont ?? string.Empty,
                    size = p.RightSize,
                    color = p.RightColor ?? "#e8e0c4",
                    img = !string.IsNullOrWhiteSpace(p.RightImage) && File.Exists(p.RightImage),
                    imgw = Math.Clamp(p.RightImageWidth, 20, 1600),
                    imgv = ImageVersion(p.RightImage),
                    dir = p.RightDir == "h" ? "h" : "v",
                    scroll = p.RightScroll,
                    speed = p.RightSpeed,
                },
                opacity = Math.Clamp(p.Opacity, 0, 100),
                lines = (p.Lines ?? new List<TextPanelLine>()).Take(10).Select(l => new
                {
                    text = l.Text ?? string.Empty,
                    font = l.Font ?? string.Empty,
                    size = l.Size,
                    color = l.Color ?? "#e8e0c4",
                    scroll = l.Scroll,
                    speed = l.Speed,
                    img = !string.IsNullOrWhiteSpace(l.Image) && File.Exists(l.Image),
                    imgw = Math.Clamp(l.ImageWidth, 20, 1600),
                    imgv = ImageVersion(l.Image),
                }).ToArray(),
            }).ToArray();

        // Cache-busting token so the browser refetches when the picked file changes.
        private static string ImageVersion(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "0";
                return ((uint)(path + File.GetLastWriteTimeUtc(path).Ticks).GetHashCode()).ToString("x");
            }
            catch { return "0"; }
        }

        private static object GoalsToWire(IEnumerable<StreamGoal>? goals) =>
            (goals ?? Enumerable.Empty<StreamGoal>())
                .Where(g => g.Show && !string.IsNullOrWhiteSpace(g.Name))
                .Take(8)
                .Select(g => new
                {
                    name = g.Name,
                    current = g.Current,
                    target = Math.Max(1, g.Target),
                    color = string.IsNullOrWhiteSpace(g.Color) ? "#7cc44a" : g.Color,
                }).ToArray();

        /// <summary>Push the current poll (or null to clear it) to all clients.</summary>
        public static void PushPoll(object? poll)
        {
            if (!_running) return;
            lock (Gate) _poll = poll;
            Broadcast(new { type = "poll", poll });
        }

        /// <summary>Push the streamer's goal bars to all clients (live as they're edited).</summary>
        public static void PushGoals(IEnumerable<StreamGoal> goals)
        {
            if (!_running) return;
            var wire = GoalsToWire(goals);
            lock (Gate) _goals = wire;
            Broadcast(new { type = "goals", goals = wire });
        }

        private static object CountersToWire(IEnumerable<GameCounter>? counters) =>
            (counters ?? Enumerable.Empty<GameCounter>())
                .Where(c => c.Show && !string.IsNullOrWhiteSpace(c.Name))
                .Take(12)
                .Select(c => new
                {
                    name = c.Name,
                    color = string.IsNullOrWhiteSpace(c.Color) ? "#7cc44a" : c.Color,
                    games = (c.Games ?? new List<string>()).ToArray(),  // empty = all games
                    values = c.Values ?? new Dictionary<string, int>(), // per-game value; overlay picks the current game
                }).ToArray();

        /// <summary>Push the streamer's counters to all clients (live as they change).</summary>
        public static void PushCounters(IEnumerable<GameCounter> counters)
        {
            if (!_running) return;
            var wire = CountersToWire(counters);
            lock (Gate) _counters = wire;
            Broadcast(new { type = "counters", counters = wire });
        }

        /// <summary>Push the streamer's text panels to all clients (live while typing).</summary>
        public static void PushTextPanels(IEnumerable<TextPanel> panels)
        {
            if (!_running) return;
            var wire = PanelsToWire(panels);
            lock (Gate) _panels = wire;
            Broadcast(new { type = "panels", panels = wire });
        }

        /// <summary>Show a big on-stream toast (optionally with a confetti puff).</summary>
        public static void Toast(string text, bool confetti = false)
        {
            if (!_running) return;
            Broadcast(new { type = "toast", text = text ?? string.Empty, confetti });
        }

        /// <summary>
        /// Push one item onto the live activity feed (new follower, sub, gift, alert…).
        /// <paramref name="kind"/> drives the icon/colour; the item is remembered so it
        /// survives overlay reconnects.
        /// </summary>
        public static void PushActivity(string kind, string user, string text,
            string platform = "", int amount = 0, string color = "")
        {
            if (!_running) return;
            var item = new
            {
                kind = kind ?? "info",
                user = user ?? string.Empty,
                text = text ?? string.Empty,
                platform = platform ?? string.Empty,
                amount,
                color = color ?? string.Empty,
                at = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            };
            lock (Gate)
            {
                _activity.Add(item);
                while (_activity.Count > ActivityMax) _activity.RemoveAt(0);
                _latestByKind[item.kind] = item;   // for the ticker's "latest of each" slots
            }
            Broadcast(new { type = "activity", item });
        }

        /// <summary>Fire a visual effect on the overlay (confetti / fireworks / shake / custom image).
        /// Custom images route to the dedicated effects overlay when one is connected.</summary>
        public static void TriggerEffect(string effect, string? imageUrl = null, int durationMs = 4000)
        {
            if (!_running) return;
            var target = effect == "custom" && HasEffectsClient ? "effects" : "main";
            Broadcast(new { type = "effect", effect, image = imageUrl ?? string.Empty, duration = durationMs, target });
        }

        /// <summary>Play a video clip (served from /fxvideo/&lt;index&gt;). Routes to the dedicated
        /// effects overlay when one is connected, else plays on the main overlay.</summary>
        public static void PlayVideo(string url, int volumePercent = 100)
        {
            if (!_running || string.IsNullOrEmpty(url)) return;
            var target = HasEffectsClient ? "effects" : "main";
            Broadcast(new { type = "video", url, volume = Math.Clamp(volumePercent, 0, 100), target });
        }

        // Active streamer voice-morph (for the overlay countdown pill).
        private static string? _morphName;
        private static DateTime _morphEnd;

        /// <summary>Show/clear the voice-morph countdown on the overlay (name + seconds; null clears).</summary>
        public static void SetMorph(string? name, int seconds)
        {
            _morphName = string.IsNullOrWhiteSpace(name) ? null : name;
            _morphEnd = DateTime.UtcNow.AddSeconds(seconds);
            if (!_running) return;
            Broadcast(new { type = "morph", name = _morphName, seconds = _morphName == null ? 0 : seconds });
        }

        private static object? MorphSnapshot()
        {
            if (_morphName == null) return null;
            var remaining = (int)(_morphEnd - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? new { name = _morphName, seconds = remaining } : null;
        }

        private static object ToWire(ChatMessage m)
        {
            string text = m.Text ?? string.Empty;
            // Stable id so the client can dedupe overlapping snapshots.
            string id = m.At.Ticks.ToString() + "-" +
                        (uint)(m.Platform + "|" + m.User + "|" + text).GetHashCode();

            var segs = (m.Segments ?? new List<ChatSegment>()).Select(s => new
            {
                t = s.Kind == ChatSegmentKind.Emote ? "emote" : "text",
                text = s.Text ?? string.Empty,
                url = s.Url ?? string.Empty,
            }).ToArray();

            string color = IsHex(m.UserColor) ? m.UserColor : string.Empty;

            var badges = (m.Badges ?? new List<ChatBadge>()).Select(b => new
            {
                label = b.Label ?? string.Empty,
                url = b.Url ?? string.Empty,
                color = b.Color ?? "#4a7c3a",
            }).ToArray();

            return new
            {
                id,
                platform = m.Platform ?? string.Empty,
                user = m.User ?? string.Empty,
                color,
                avatar = m.AvatarUrl ?? string.Empty,
                badges,
                symbol = OverlayService.ChatSymbol(m.Platform),
                symbolColor = OverlayService.ChatColorHex(m.Platform),
                at = m.At.Ticks,
                segments = segs,
            };
        }

        private static bool IsHex(string? s) =>
            !string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#' &&
            s.Skip(1).All(Uri.IsHexDigit);

        private static void Broadcast(object payload)
        {
            string json;
            try { json = JsonConvert.SerializeObject(payload); }
            catch { return; }

            Client[] snapshot;
            lock (Gate) snapshot = Clients.ToArray();
            foreach (var c in snapshot)
                _ = SendText(c, json, _cts?.Token ?? CancellationToken.None);
        }

        private static async Task SendText(Client client, string json, CancellationToken ct)
        {
            // SendAsync must not be called concurrently on one socket -> serialize.
            await client.SendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (client.Socket.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(json);
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Dead client: drop it so we stop trying to write.
                lock (Gate) Clients.Remove(client);
                try { client.Socket.Abort(); } catch { }
            }
            finally
            {
                try { client.SendLock.Release(); } catch { }
            }
        }

        // -----------------------------------------------------------------
        //  Overlay HTML loading (embedded resource, with disk fallback)
        // -----------------------------------------------------------------

        private static string LoadOverlayHtml()
        {
            // 1) Embedded resource (shipped inside the exe).
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("LiveOverlay.html", StringComparison.OrdinalIgnoreCase));
                if (name != null)
                {
                    using var s = asm.GetManifestResourceStream(name);
                    if (s != null)
                    {
                        using var r = new StreamReader(s, Encoding.UTF8);
                        var html = InjectPort(r.ReadToEnd());
                        // Also drop a copy next to the other overlay files so it can be
                        // opened directly (file://) if someone prefers that.
                        TryWriteCopy(html);
                        return html;
                    }
                }
            }
            catch { }

            // 2) Disk fallback (dev runs, or a user-edited copy).
            try
            {
                var path = Path.Combine(OverlayService.FolderPath, "LiveOverlay.html");
                if (File.Exists(path)) return InjectPort(File.ReadAllText(path));
            }
            catch { }

            return "<!DOCTYPE html><meta charset=\"utf-8\"><body style=\"background:transparent\">" +
                   "<!-- LiveOverlay.html resource missing --></body>";
        }

        // Bake the live port into the overlay's CONFIG.fallbackPort so that opening
        // the file directly (file://) still finds the server. When served over http://
        // the overlay derives the host from its own URL, so this is belt-and-braces.
        private static string InjectPort(string html)
        {
            html = html.Replace("fallbackPort: " + DefaultPort, "fallbackPort: " + Port);
            html = html.Replace("partyBase: \"\"", "partyBase: \"" + BotBaseUrl() + "\"");
            return html;
        }

        // The companion bot's base URL, derived from the Discord-bot connection the
        // streamer set up (BotIngestUrl, e.g. https://host/clip -> https://host). Empty
        // if not configured yet, so the Party overlay shows a "connect the bot" hint.
        private static string BotBaseUrl()
        {
            try
            {
                var ingest = SettingsService.LoadChatFeatures()?.BotIngestUrl;
                if (string.IsNullOrWhiteSpace(ingest)) return "";
                var u = new Uri(ingest, UriKind.Absolute);
                var baseUrl = u.GetLeftPart(UriPartial.Authority); // scheme://host[:port]
                return baseUrl.Replace("\"", "").Replace("\\", "");
            }
            catch { return ""; }
        }

        private static void TryWriteCopy(string html)
        {
            try
            {
                Directory.CreateDirectory(OverlayService.FolderPath);
                File.WriteAllText(Path.Combine(OverlayService.FolderPath, "LiveOverlay.html"), html);
            }
            catch { }
        }
    }
}
