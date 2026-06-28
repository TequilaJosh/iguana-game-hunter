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
    /// Reads Restream's unified chat over its WebSocket. Needs an OAuth access token
    /// (from a registered Restream app). Read-only.
    /// </summary>
    public sealed class RestreamConnector : IChatConnector
    {
        public string Name => "Restream";
        public bool IsConnected { get; private set; }

        public event Action<ChatMessage>? MessageReceived;
        public event Action<string>? StatusChanged;

        private CancellationTokenSource? _cts;
        private string _token = string.Empty;
        private volatile bool _want;

        public async Task ConnectAsync(string input)
        {
            await DisconnectAsync();
            _token = (input ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(_token)) { StatusChanged?.Invoke("Paste your access token"); return; }
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
                    await ws.ConnectAsync(
                        new Uri($"wss://chat.api.restream.io/ws?accessToken={Uri.EscapeDataString(_token)}"), ct);

                    IsConnected = true;
                    StatusChanged?.Invoke("Connected");

                    var buffer = new byte[16384];
                    var sb = new StringBuilder();
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (result.EndOfMessage)
                        {
                            Handle(sb.ToString());
                            sb.Clear();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { StatusChanged?.Invoke("Error: " + ex.Message); }
                finally
                {
                    IsConnected = false;
                    try { ws?.Dispose(); } catch { }
                }

                if (_want && !ct.IsCancellationRequested)
                {
                    StatusChanged?.Invoke("Reconnecting…");
                    try { await Task.Delay(2000, ct); } catch { }
                }
            }
            StatusChanged?.Invoke("Disconnected");
        }

        private void Handle(string text)
        {
            try
            {
                var o = JObject.Parse(text);
                if ((string?)o["action"] != "event") return; // skip heartbeats/connection info

                var ep = o["payload"]?["eventPayload"];
                var author = ep?["author"];
                string user = (string?)author?["displayName"]
                              ?? (string?)author?["name"]
                              ?? string.Empty;
                string msg = (string?)ep?["text"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(msg)) return;

                int sourceId = (int?)o["payload"]?["eventSourceId"] ?? 0;
                MessageReceived?.Invoke(new ChatMessage
                {
                    Platform = MapSource(sourceId),
                    User = user,
                    Text = msg,
                });
            }
            catch { }
        }

        // Best-effort Restream event-source IDs → platform name (falls back to "restream").
        private static string MapSource(int id) => id switch
        {
            2 => "twitch",
            5 => "facebook",
            13 => "youtube",
            28 => "tiktok",
            _ => "restream",
        };

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
