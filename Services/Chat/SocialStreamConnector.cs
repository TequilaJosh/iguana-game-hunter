using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GameTracker.Models;

namespace GameTracker.Services.Chat
{
    /// <summary>
    /// Reads merged chat from Social Stream Ninja via its WebSocket relay (channel 4).
    /// No login — just the session ID from your dock URL. Auto-reconnects on timeout.
    /// </summary>
    public sealed class SocialStreamConnector : IChatConnector
    {
        public string Name => "Social Stream Ninja";
        public bool IsConnected { get; private set; }

        public event Action<ChatMessage>? MessageReceived;
        public event Action<string>? StatusChanged;

        private CancellationTokenSource? _cts;
        private string _session = string.Empty;
        private volatile bool _want;

        // Outgoing messages primarily use SSN's stateless HTTP endpoint (routes to the
        // extension no matter how channels are wired). A WebSocket on the extension's
        // command channel (3) is kept as a fallback if HTTP transport is unavailable.
        private ClientWebSocket? _sendWs;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        public async Task ConnectAsync(string input)
        {
            await DisconnectAsync();
            _session = (input ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(_session)) { StatusChanged?.Invoke("Enter your session ID"); return; }
            _want = true;
            _cts = new CancellationTokenSource();
            _ = RunLoop(_cts.Token);
        }

        private async Task RunLoop(CancellationToken ct)
        {
            while (_want && !ct.IsCancellationRequested)
            {
                ClientWebSocket? ws = null;
                try
                {
                    ws = new ClientWebSocket();
                    StatusChanged?.Invoke("Connecting…");
                    // Documented "read chat" method: join the session listening on channel 4.
                    // The URL alone subscribes us; no extra join frame is needed (and sending
                    // one without an "in" channel can drop the channel-4 subscription).
                    await ws.ConnectAsync(
                        new Uri($"wss://io.socialstream.ninja/join/{_session}/4"), ct);

                    IsConnected = true;
                    StatusChanged?.Invoke("Connected — waiting for chat");

                    var buffer = new byte[16384];
                    var sb = new StringBuilder();
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (result.EndOfMessage)
                        {
                            HandlePayload(sb.ToString());
                            sb.Clear();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { StatusChanged?.Invoke("Reconnecting… " + ex.Message); }
                finally
                {
                    IsConnected = false;
                    try { ws?.Dispose(); } catch { }
                }

                if (_want && !ct.IsCancellationRequested)
                {
                    StatusChanged?.Invoke("Reconnecting…");
                    try { await Task.Delay(1500, ct); } catch { }
                }
            }
            StatusChanged?.Invoke("Disconnected");
        }

        private void HandlePayload(string text)
        {
            try
            {
                var token = JToken.Parse(text);
                if (token is JArray arr)
                    foreach (var t in arr) Emit(t as JObject);
                else
                    Emit(token as JObject);
            }
            catch { /* ignore non-JSON keepalives */ }
        }

        private void Emit(JObject? o)
        {
            if (o == null) return;
            // The chat object is usually top-level, but may be wrapped.
            var msg = o["chatmessage"] != null ? o
                    : (o["contents"] as JObject) ?? (o["message"] as JObject) ?? o;

            var name = ChatText.Plain((string?)msg?["chatname"]);
            var segments = ChatText.Parse((string?)msg?["chatmessage"]);
            if (string.IsNullOrWhiteSpace(name) && segments.Count == 0) return;

            MessageReceived?.Invoke(new ChatMessage
            {
                Platform = ((string?)msg?["type"] ?? "social").ToLowerInvariant(),
                User = name,
                Segments = segments,
                UserColor = (string?)msg?["nameColor"] ?? string.Empty,
                AvatarUrl = (string?)msg?["chatimg"] ?? string.Empty,   // SSN forwards the avatar
                Badges = ParseBadges(msg?["chatbadges"]),
            });
        }

        // SSN "chatbadges": an array of image URLs (strings) or objects with a url field.
        private static List<ChatBadge> ParseBadges(JToken? badges)
        {
            var list = new List<ChatBadge>();
            if (badges is not JArray arr) return list;
            foreach (var b in arr)
            {
                string url = b.Type == JTokenType.String
                    ? (string?)b ?? string.Empty
                    : (string?)b["url"] ?? (string?)b["src"] ?? string.Empty;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    list.Add(new ChatBadge { Url = url });
                if (list.Count >= 3) break;
            }
            return list;
        }

        /// <summary>
        /// Send a chat message out through Social Stream Ninja: the SSN extension posts it
        /// into the connected platforms' chat boxes using the streamer's own logins.
        /// <paramref name="target"/> limits it to one platform (e.g. "twitch"); null/empty = all.
        /// Returns false if it couldn't be sent.
        /// </summary>
        public async Task<bool> SendChatAsync(string message, string? target = null)
        {
            message = (message ?? string.Empty).Trim();
            if (message.Length == 0 || string.IsNullOrEmpty(_session)) return false;

            // "null" is SSN's placeholder for "all platforms".
            var tgt = string.IsNullOrWhiteSpace(target) || target == "*"
                ? "null" : target.Trim().ToLowerInvariant();

            // Primary: the documented stateless HTTP endpoint
            //   https://io.socialstream.ninja/SESSION/sendEncodedChat/TARGET/URLENCODED
            // The relay hands this straight to the extension, so it doesn't depend on
            // which WebSocket channel the extension happens to be listening on. The
            // response body reports delivery: "failed" means the relay had no listener
            // (extension not running) — report that honestly rather than a false success.
            try
            {
                var url = $"https://io.socialstream.ninja/{Uri.EscapeDataString(_session)}" +
                          $"/sendEncodedChat/{tgt}/{Uri.EscapeDataString(message)}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var resp = await _http.GetAsync(url, cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    var body = (await resp.Content.ReadAsStringAsync()).Trim();
                    return !(body.StartsWith("failed", StringComparison.OrdinalIgnoreCase)
                          || body.StartsWith("error", StringComparison.OrdinalIgnoreCase));
                }
                // Non-2xx: fall through to the WebSocket path.
            }
            catch { /* HTTP transport unavailable — fall through to the WebSocket path. */ }

            // Fallback: WebSocket command on channel 3 (where the extension picks up
            // commands). The old code joined the default channel, which the extension
            // does not read sendChat from — that's why sends silently did nothing.
            await _sendLock.WaitAsync();
            try
            {
                if (_sendWs is not { State: WebSocketState.Open })
                {
                    try { _sendWs?.Dispose(); } catch { }
                    _sendWs = new ClientWebSocket();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    // /join/SESSION/IN/OUT — send OUT on channel 3.
                    await _sendWs.ConnectAsync(
                        new Uri($"wss://io.socialstream.ninja/join/{_session}/1/3"), cts.Token);
                }

                var payload = tgt == "null"
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(
                          new { action = "sendChat", value = message })
                    : Newtonsoft.Json.JsonConvert.SerializeObject(
                          new { action = "sendChat", value = message, target = tgt });
                using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await _sendWs.SendAsync(Encoding.UTF8.GetBytes(payload),
                    WebSocketMessageType.Text, true, sendCts.Token);
                return true;
            }
            catch
            {
                try { _sendWs?.Dispose(); } catch { }
                _sendWs = null;
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public Task DisconnectAsync()
        {
            _want = false;
            try { _cts?.Cancel(); } catch { }
            try { _sendWs?.Dispose(); } catch { }
            _sendWs = null;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() => _ = DisconnectAsync();
    }
}
