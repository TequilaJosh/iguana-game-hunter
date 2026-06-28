using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameTracker.Models;

namespace GameTracker.Services.Chat
{
    /// <summary>
    /// Reads Twitch chat anonymously over IRC-WebSocket (no login required).
    /// </summary>
    public sealed class TwitchChatConnector : IChatConnector
    {
        private const string Endpoint = "wss://irc-ws.chat.twitch.tv:443";

        public string Name => "Twitch";
        public bool IsConnected { get; private set; }

        public event Action<ChatMessage>? MessageReceived;
        public event Action<string>? StatusChanged;

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private string _channel = string.Empty;
        private readonly Random _rng = new();

        public async Task ConnectAsync(string channel)
        {
            await DisconnectAsync();

            _channel = (channel ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant();
            if (string.IsNullOrEmpty(_channel))
            {
                StatusChanged?.Invoke("Enter a channel");
                return;
            }

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            try
            {
                StatusChanged?.Invoke("Connecting…");
                await _ws.ConnectAsync(new Uri(Endpoint), _cts.Token);

                await SendAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");
                await SendAsync("PASS SCHMOOPIIE");
                await SendAsync($"NICK justinfan{_rng.Next(10000, 99999)}");
                await SendAsync($"JOIN #{_channel}");

                IsConnected = true;
                StatusChanged?.Invoke($"Connected — #{_channel}");
                _ = ReceiveLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusChanged?.Invoke("Error: " + ex.Message);
            }
        }

        private async Task SendAsync(string line)
        {
            if (_ws is null || _cts is null) return;
            var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            try
            {
                while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    var content = sb.ToString();
                    int nl;
                    while ((nl = content.IndexOf("\r\n", StringComparison.Ordinal)) >= 0)
                    {
                        await HandleLine(content.Substring(0, nl));
                        content = content[(nl + 2)..];
                    }
                    sb.Clear();
                    sb.Append(content);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusChanged?.Invoke("Disconnected: " + ex.Message); }

            IsConnected = false;
            StatusChanged?.Invoke("Disconnected");
        }

        private async Task HandleLine(string line)
        {
            if (line.StartsWith("PING", StringComparison.Ordinal))
            {
                await SendAsync("PONG :tmi.twitch.tv");
                return;
            }
            if (!line.Contains("PRIVMSG", StringComparison.Ordinal)) return;

            // @tags :nick!nick@nick.tmi.twitch.tv PRIVMSG #channel :message text
            string tags = string.Empty;
            string rest = line;
            if (line.StartsWith("@", StringComparison.Ordinal))
            {
                int sp = line.IndexOf(' ');
                if (sp < 0) return;
                tags = line.Substring(1, sp - 1);
                rest = line[(sp + 1)..];
            }

            int privIdx = rest.IndexOf("PRIVMSG", StringComparison.Ordinal);
            int msgIdx = rest.IndexOf(" :", privIdx, StringComparison.Ordinal);
            if (msgIdx < 0) return;
            string message = rest[(msgIdx + 2)..].TrimEnd();

            string nick = string.Empty;
            if (rest.StartsWith(":", StringComparison.Ordinal))
            {
                int bang = rest.IndexOf('!');
                if (bang > 1) nick = rest.Substring(1, bang - 1);
            }

            string display = nick, color = string.Empty;
            foreach (var kv in tags.Split(';'))
            {
                int eq = kv.IndexOf('=');
                if (eq < 0) continue;
                var k = kv[..eq];
                var v = kv[(eq + 1)..];
                if (k == "display-name" && v.Length > 0) display = v;
                else if (k == "color" && v.Length > 0) color = v;
            }

            if (message.Length == 0) return;
            MessageReceived?.Invoke(new ChatMessage
            {
                Platform = "twitch",
                User = display,
                Text = message,
                UserColor = color,
            });
        }

        public async Task DisconnectAsync()
        {
            try { _cts?.Cancel(); } catch { }
            try
            {
                if (_ws is { State: WebSocketState.Open })
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            catch { }
            _ws?.Dispose();
            _ws = null;
            IsConnected = false;
        }

        public void Dispose() => _ = DisconnectAsync();
    }
}
