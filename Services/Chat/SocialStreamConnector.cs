using System;
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

            var name = (string?)msg?["chatname"];
            var body = (string?)msg?["chatmessage"];
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(body)) return;

            MessageReceived?.Invoke(new ChatMessage
            {
                Platform = ((string?)msg?["type"] ?? "social").ToLowerInvariant(),
                User = name ?? string.Empty,
                Text = body ?? string.Empty,
                UserColor = (string?)msg?["nameColor"] ?? string.Empty,
            });
        }

        public Task DisconnectAsync()
        {
            _want = false;
            try { _cts?.Cancel(); } catch { }
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() => _ = DisconnectAsync();
    }
}
