using System;
using System.Collections.Generic;
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

        // Outgoing messages go over their own socket on the session's default channel
        // (the receive socket is pinned to channel 4, which is inbound chat only).
        private ClientWebSocket? _sendWs;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

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

            await _sendLock.WaitAsync();
            try
            {
                // (Re)open the send socket if needed.
                if (_sendWs is not { State: WebSocketState.Open })
                {
                    try { _sendWs?.Dispose(); } catch { }
                    _sendWs = new ClientWebSocket();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    await _sendWs.ConnectAsync(
                        new Uri($"wss://io.socialstream.ninja/join/{_session}"), cts.Token);
                }

                var payload = string.IsNullOrWhiteSpace(target) || target == "*"
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(
                          new { action = "sendChat", value = message })
                    : Newtonsoft.Json.JsonConvert.SerializeObject(
                          new { action = "sendChat", value = message, target });
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
