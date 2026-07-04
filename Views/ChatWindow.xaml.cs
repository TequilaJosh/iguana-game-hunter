using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameTracker.Models;
using GameTracker.Services;
using GameTracker.Services.Chat;

namespace GameTracker.Views
{
    /// <summary>Merged live-chat window (read-only): Twitch, Social Stream Ninja, Restream.</summary>
    public partial class ChatWindow : Window
    {
        private const int MaxMessages = 400;
        private int _overlayLines = 20;   // how many recent messages feed the overlay (user-set)

        private readonly TwitchChatConnector _twitch = new();
        private readonly SocialStreamConnector _ssn = new();
        private readonly RestreamConnector _restream = new();
        private readonly ObservableCollection<ChatRow> _rows = new();
        private readonly List<ChatMessage> _recent = new();
        private readonly DispatcherTimer _overlayTimer;
        private bool _pinned;
        private bool _collapsed;
        private bool _chatDirty;
        private bool _ready;

        private readonly SoundService _sound = new();

        private const string AllChats = "All chats";
        private readonly ObservableCollection<string> _sendTargets = new() { AllChats };

        private ChatFeatureSettings _features = new();
        private readonly ChattersService _chattersSvc = new();
        private DispatcherTimer? _chattersTimer;
        private ChattersWindow? _chattersWindow;
        private bool _chattersDirty;
        private int _boxColorIdx;

        /// <summary>Raised when a viewer runs "!request &lt;game&gt;" — (game title, requester, platform).</summary>
        public Action<string, string, string>? OnGameRequested;

        private static readonly Brush DefaultUser = Frozen("#a8c488");

        public ChatWindow()
        {
            InitializeComponent();
            MessageList.ItemsSource = _rows;

            Wire(_twitch, s => TwitchStatus.Text = s);
            Wire(_ssn, s => SsnStatus.Text = s);
            Wire(_restream, s => RestreamStatus.Text = s);

            // Discover send targets from the platforms flowing through SSN.
            _ssn.MessageReceived += m => Dispatcher.Invoke(() => AddSendTarget(m.Platform));

            // Restore saved connection details (the SSN session ID is persistent).
            var saved = SettingsService.LoadChat();
            TwitchBox.Text = saved.TwitchChannel;
            SsnBox.Text = saved.SsnSession;
            RestreamBox.Text = saved.RestreamToken;
            AutoConnectCheck.IsChecked = saved.AutoConnect;

            var op = saved.Opacity is >= 0.25 and <= 1.0 ? saved.Opacity : 1.0;
            OpacitySlider.Value = op;
            Opacity = op;

            // Send-target picker: "All chats" + platforms seen via SSN. Always starts on
            // All chats so a leftover platform pick can't surprise the streamer later.
            SendTargetCombo.ItemsSource = _sendTargets;
            SendTargetCombo.SelectedItem = AllChats;

            _sound.SetAlerts(SettingsService.LoadSoundAlerts());
            UpdateMuteButton();

            // Chat features: counts, chatters list, points, style, redeems.
            _features = SettingsService.LoadChatFeatures();
            ApplyChatTemplate();
            UpdateChattersUi();
            _chattersTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _chattersTimer.Tick += (_, _) =>
            {
                var (changed, awards) = _chattersSvc.Tick(3, _features);
                if (changed || awards > 0 || _chattersDirty)
                {
                    _chattersDirty = false;
                    UpdateChattersUi();
                }
            };
            _chattersTimer.Start();

            // OBS overlay server: show the current port + URL + chat-line count.
            PortBox.Text = SettingsService.LoadOverlayPort().ToString();
            _overlayLines = SettingsService.LoadOverlayChatLines();
            ChatLinesBox.Text = _overlayLines.ToString();
            UpdateOverlayStatus();

            _ready = true;

            // Refresh the OBS chat.html a few times a second when there's new chat.
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _overlayTimer.Tick += (_, _) =>
            {
                if (!_chatDirty) return;
                _chatDirty = false;
                OverlayService.WriteChatHtml(_recent.TakeLast(_overlayLines).ToList());
            };
            _overlayTimer.Start();
        }

        private void Wire(IChatConnector c, Action<string> status)
        {
            c.MessageReceived += OnMessage;
            c.StatusChanged += s => Dispatcher.Invoke(() => status(s));
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private void OnMessage(ChatMessage m)
        {
            Dispatcher.Invoke(() =>
            {
                bool atBottom = MsgScroll.VerticalOffset >= MsgScroll.ScrollableHeight - 4;

                _rows.Add(ToRow(m));
                while (_rows.Count > MaxMessages) _rows.RemoveAt(0);

                _recent.Add(m);
                while (_recent.Count > _overlayLines) _recent.RemoveAt(0);
                _chatDirty = true;

                _chattersSvc.OnMessage(m);
                _chattersDirty = true;

                HandleCommands(m);

                if (atBottom) MsgScroll.ScrollToEnd();
            });
        }

        private void HandleCommands(ChatMessage m)
        {
            var text = m.Text ?? string.Empty;

            // Sound alerts: play if the first word matches a configured command.
            _sound.CheckAndPlay(text);

            var parts = text.TrimStart().Split(new[] { ' ' }, 2);
            var cmd = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            if (cmd.Length == 0) return;

            // "!request <game>" -> hand off to the board (deduped there).
            if (parts.Length == 2 && string.Equals(cmd, "!request", StringComparison.OrdinalIgnoreCase))
            {
                var game = parts[1].Trim();
                if (game.Length > 0) OnGameRequested?.Invoke(game, m.User, m.Platform ?? string.Empty);
                return;
            }

            // "!points" (or "!<points name>") -> balance toast + optional chat reply.
            if (_features.PointsEnabled && IsBalanceCommand(cmd))
            {
                var bal = PointsService.Get(m.Platform, m.User);
                OverlayServer.Toast($"{m.User} has {bal} {_features.PointsName}");
                SendChatReply($"@{m.User} you have {bal} {_features.PointsName}", m.Platform);
                return;
            }

            // Point redeems -> spend points, fire the effect.
            var idx = _features.Redeems.FindIndex(r =>
                string.Equals(r.Command.Trim(), cmd, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var r = _features.Redeems[idx];
                if (!_features.PointsEnabled || r.Cost <= 0 ||
                    PointsService.TrySpend(m.Platform, m.User, r.Cost))
                {
                    PointsService.Save();
                    if (!_sound.Muted && !string.IsNullOrWhiteSpace(r.SoundPath))
                        _sound.Play(r.SoundPath, r.Volume);
                    OverlayServer.TriggerEffect(r.Effect,
                        r.Effect == "custom" && !string.IsNullOrWhiteSpace(r.ImagePath) ? "/fx/" + idx : null);
                    OverlayServer.Toast($"{m.User} redeemed {r.Command.TrimStart('!')}!", confetti: false);
                    SendChatReply($"@{m.User} redeemed {r.Command.TrimStart('!')}!", m.Platform);
                }
                else
                {
                    var missing = r.Cost - PointsService.Get(m.Platform, m.User);
                    OverlayServer.Toast($"{m.User} needs {missing} more {_features.PointsName} for {r.Command}");
                    SendChatReply($"@{m.User} you need {missing} more {_features.PointsName} for {r.Command}", m.Platform);
                }
            }
        }

        private bool IsBalanceCommand(string cmd)
        {
            if (string.Equals(cmd, "!points", StringComparison.OrdinalIgnoreCase)) return true;
            var custom = "!" + new string(_features.PointsName.Where(char.IsLetterOrDigit).ToArray());
            return custom.Length > 1 && string.Equals(cmd, custom, StringComparison.OrdinalIgnoreCase);
        }

        private void SoundAlerts_Click(object sender, RoutedEventArgs e)
        {
            var win = new SoundAlertsWindow { Owner = this };
            if (win.ShowDialog() == true)
                _sound.SetAlerts(SettingsService.LoadSoundAlerts());
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _sound.Muted = !_sound.Muted;
            if (_sound.Muted) _sound.StopAll();   // panic: kill anything currently playing
            UpdateMuteButton();
        }

        private void UpdateMuteButton()
        {
            MuteButton.Content = _sound.Muted ? "\U0001F507" : "\U0001F50A";   // 🔇 / 🔊
            MuteButton.Foreground = _sound.Muted
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0x5a, 0x37))         // red when muted
                : new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));        // gray when active
            MuteButton.ToolTip = _sound.Muted
                ? "Sound alerts muted — click to unmute"
                : "Stop sound alerts (panic): kill the current alert and mute incoming";
        }

        // ---- OBS overlay server controls ----

        private string OverlayUrl => $"http://localhost:{OverlayServer.Port}/";

        private void ApplyPort_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortBox.Text.Trim(), out int port) || port is < 1 or > 65535)
            {
                OverlayStatus.Text = "Enter a port between 1 and 65535 (default 3620).";
                OverlayStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xa4, 0x37));
                return;
            }

            SettingsService.SaveOverlayPort(port);
            OverlayServer.Restart();
            PortBox.Text = OverlayServer.Port.ToString();
            UpdateOverlayStatus();
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(OverlayUrl); OverlayStatus.Text = "Copied: " + OverlayUrl; }
            catch { /* clipboard can be momentarily locked by another app */ }
        }

        private void EditLayout_Click(object sender, RoutedEventArgs e)
        {
            if (!OverlayServer.IsRunning)
            {
                OverlayStatus.Text = "Overlay server isn't running — can't open the editor.";
                OverlayStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0x5a, 0x37));
                return;
            }
            var url = OverlayUrl + "?edit=1";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                OverlayStatus.Text = "Opened the layout editor in your browser. Drag/resize blocks, set fonts — it saves automatically.";
                OverlayStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
            }
            catch { OverlayStatus.Text = "Couldn't open a browser. Go to " + url + " manually."; }
        }

        private void ChatLines_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            if (!int.TryParse(ChatLinesBox.Text.Trim(), out int n)) return;
            n = Math.Clamp(n, 5, 100);
            _overlayLines = n;
            SettingsService.SaveOverlayChatLines(n);
            OverlayStatus.Text = $"Chat set to {n} lines — refresh the OBS source to apply.";
            OverlayStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
        }

        private void UpdateOverlayStatus()
        {
            if (OverlayServer.IsRunning)
            {
                OverlayStatus.Text = $"Serving at {OverlayUrl} — use this as the OBS Browser source URL.";
                OverlayStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
            }
            else
            {
                OverlayStatus.Text = "Overlay server is not running" +
                    (string.IsNullOrEmpty(OverlayServer.LastError) ? "." : $": {OverlayServer.LastError}. Try another port.");
                OverlayStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0x5a, 0x37));
            }
        }

        private int _zebra;
        private static readonly Brush ZebraNone = Brushes.Transparent;
        private static readonly Brush ZebraDark = Frozen("#22000000");

        private ChatRow ToRow(ChatMessage m)
        {
            Brush userBrush = DefaultUser;
            if (!string.IsNullOrWhiteSpace(m.UserColor))
            {
                try { userBrush = Frozen(m.UserColor); } catch { /* keep default */ }
            }

            Brush symbolBrush = DefaultUser;
            try { symbolBrush = Frozen(OverlayService.ChatColorHex(m.Platform)); } catch { }

            // Avatar: photo if provided, otherwise a colored circle with the user's initial.
            Brush avatarBrush = symbolBrush;
            string initial = string.Empty;
            bool hasPhoto = false;
            if (IsHttp(m.AvatarUrl))
            {
                try { avatarBrush = AvatarImage(m.AvatarUrl); hasPhoto = true; } catch { }
            }
            if (!hasPhoto) initial = FirstLetter(m.User);

            Brush boxBrush = DefaultUser;
            if (_features.BoxColors.Count > 0)
            {
                try { boxBrush = Frozen(_features.BoxColors[_boxColorIdx++ % _features.BoxColors.Count]); }
                catch { }
            }

            return new ChatRow
            {
                Symbol = OverlayService.ChatSymbol(m.Platform),
                SymbolBrush = symbolBrush,
                User = m.User,
                Segments = m.Segments,
                Badges = m.Badges ?? new List<ChatBadge>(),
                UserBrush = userBrush,
                AvatarBrush = avatarBrush,
                Initial = initial,
                RowBrush = (_zebra++ % 2 == 0) ? ZebraNone : ZebraDark,
                BoxBrush = boxBrush,
            };
        }

        // ---- chat features (counts / chatters / points / style) ----

        private void ApplyChatTemplate()
        {
            var key = _features.ChatStyle == "boxes" ? "BoxRow" : "LogRow";
            if (MessageList.Resources[key] is DataTemplate t)
                MessageList.ItemTemplate = t;
        }

        private void UpdateChattersUi()
        {
            // In-app count bar.
            CountBar.Visibility = _features.ShowCount ? Visibility.Visible : Visibility.Collapsed;
            if (_features.ShowCount)
            {
                CountText.Inlines.Clear();
                CountText.Inlines.Add(new System.Windows.Documents.Run("\U0001F465 ")
                { Foreground = Frozen("#7a9070") });
                if (_features.CountPerSource)
                {
                    bool first = true;
                    foreach (var (platform, n) in _chattersSvc.PerSource().OrderBy(kv => kv.Key))
                    {
                        if (!first) CountText.Inlines.Add(new System.Windows.Documents.Run("   "));
                        first = false;
                        Brush b = DefaultUser;
                        try { b = Frozen(OverlayService.ChatColorHex(platform)); } catch { }
                        CountText.Inlines.Add(new System.Windows.Documents.Run(
                            OverlayService.ChatSymbol(platform) + " " + n)
                        { Foreground = b, FontWeight = FontWeights.Bold });
                    }
                    if (first) CountText.Inlines.Add(new System.Windows.Documents.Run("0 chatting")
                    { Foreground = Frozen("#7a9070") });
                }
                else
                {
                    CountText.Inlines.Add(new System.Windows.Documents.Run(
                        _chattersSvc.Count + " chatting")
                    { Foreground = Frozen("#a8c488"), FontWeight = FontWeights.Bold });
                }
            }

            // In-app chatters window.
            _chattersWindow?.Refresh(_chattersSvc.Snapshot(), _features);

            // Overlay.
            OverlayServer.SetChatters(BuildChattersPayload());
        }

        private object BuildChattersPayload() => new
        {
            showCount = _features.ShowCount,
            onOverlay = _features.CountOnOverlay,
            perSourceMode = _features.CountPerSource,
            total = _chattersSvc.Count,
            perSource = _chattersSvc.PerSource().OrderBy(kv => kv.Key).Select(kv => new
            {
                platform = kv.Key,
                symbol = OverlayService.ChatSymbol(kv.Key),
                symbolColor = OverlayService.ChatColorHex(kv.Key),
                count = kv.Value,
            }).ToArray(),
            showList = _features.ShowChattersOnOverlay,
            list = _chattersSvc.Snapshot().Select(c => new
            {
                u = c.User,
                s = c.State == ChatterState.Lurking ? "l" : "a",
                symbol = OverlayService.ChatSymbol(c.Platform),
                symbolColor = OverlayService.ChatColorHex(c.Platform),
            }).ToArray(),
        };

        private void Features_Click(object sender, RoutedEventArgs e)
        {
            var win = new ChatFeaturesWindow { Owner = this };
            if (win.ShowDialog() != true) return;

            _features = SettingsService.LoadChatFeatures();
            ApplyChatTemplate();
            OverlayServer.SetStyle(_features.ChatStyle, _features.BoxColors);
            UpdateChattersUi();
        }

        private void Chatters_Click(object sender, RoutedEventArgs e)
        {
            if (_chattersWindow != null) { _chattersWindow.Activate(); return; }
            _chattersWindow = new ChattersWindow { Owner = this };
            _chattersWindow.Closed += (_, _) => _chattersWindow = null;
            _chattersWindow.Refresh(_chattersSvc.Snapshot(), _features);
            _chattersWindow.Show();
        }

        private static bool IsHttp(string? s) =>
            !string.IsNullOrWhiteSpace(s) &&
            (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        private static string FirstLetter(string? user) =>
            string.IsNullOrWhiteSpace(user) ? "?" : user.Trim()[0].ToString().ToUpperInvariant();

        private static ImageBrush AvatarImage(string url)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.DecodePixelWidth = 44;
            bmp.EndInit();
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }

        private void SaveChat() => SettingsService.SaveChat(new ChatSettings
        {
            TwitchChannel = TwitchBox.Text.Trim(),
            SsnSession = SsnBox.Text.Trim(),
            RestreamToken = RestreamBox.Text.Trim(),
            AutoConnect = AutoConnectCheck.IsChecked == true,
            Opacity = OpacitySlider.Value,
        });

        private void AutoConnect_Changed(object sender, RoutedEventArgs e) => SaveChat();

        private void SsnGuide_Click(object sender, RoutedEventArgs e) =>
            new SsnGuideWindow { Owner = this }.ShowDialog();

        // ---- two-way chat (outgoing via Social Stream Ninja) ----

        /// <summary>
        /// Send an @mention reply through SSN if "Reply in chat" is enabled and SSN is
        /// connected. When <paramref name="platform"/> is a real platform name, the reply
        /// goes only to that chat so the others aren't spammed. Fire-and-forget.
        /// </summary>
        public void SendChatReply(string text, string? platform = null)
        {
            if (!_features.ReplyInChat || !_ssn.IsConnected || string.IsNullOrWhiteSpace(text)) return;
            _ = _ssn.SendChatAsync(text, ReplyTarget(platform));
        }

        // Map a message's platform to an SSN send target. Unknown/aggregate sources
        // ("social", "restream", empty) have no reliable SSN label -> send to all.
        private static string? ReplyTarget(string? platform)
        {
            var p = (platform ?? string.Empty).Trim().ToLowerInvariant();
            return p.Length == 0 || p == "social" || p == "restream" ? null : p;
        }

        private void AddSendTarget(string? platform)
        {
            platform = (platform ?? string.Empty).Trim().ToLowerInvariant();
            if (platform.Length == 0 || platform == "social") return;
            if (!_sendTargets.Contains(platform)) _sendTargets.Add(platform);
        }

        private string? SelectedSendTarget()
        {
            var t = SendTargetCombo.SelectedItem as string;
            return string.IsNullOrWhiteSpace(t) || t == AllChats ? null : t;
        }

        private void SendTarget_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Selection is intentionally session-only (always defaults back to All chats).
        }

        private void SendBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { _ = SendMessageAsync(); e.Handled = true; }
        }

        private void Send_Click(object sender, RoutedEventArgs e) => _ = SendMessageAsync();

        private async System.Threading.Tasks.Task SendMessageAsync()
        {
            var text = SendBox.Text.Trim();
            if (text.Length == 0) return;

            if (!_ssn.IsConnected)
            {
                SsnStatus.Text = "Connect Social Stream Ninja to send messages.";
                return;
            }

            SendBtn.IsEnabled = false;
            try
            {
                var target = SelectedSendTarget();
                bool ok = await _ssn.SendChatAsync(text, target);
                if (ok)
                {
                    SendBox.Clear();   // the message echoes back through SSN's chat feed
                    SsnStatus.Text = target == null ? "Sent to all chats." : $"Sent to {target}.";
                }
                else
                {
                    SsnStatus.Text = "Couldn't send — check SSN is running with remote API control on.";
                }
            }
            finally { SendBtn.IsEnabled = true; }
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Opacity = e.NewValue;
            if (_ready) SaveChat();
        }

        /// <summary>Connect every source that has a saved value and isn't already connected.</summary>
        public void ConnectSaved()
        {
            if (!string.IsNullOrWhiteSpace(TwitchBox.Text) && !_twitch.IsConnected)
                Twitch_Click(this, new RoutedEventArgs());
            if (!string.IsNullOrWhiteSpace(SsnBox.Text) && !_ssn.IsConnected)
                Ssn_Click(this, new RoutedEventArgs());
            if (!string.IsNullOrWhiteSpace(RestreamBox.Text) && !_restream.IsConnected)
                Restream_Click(this, new RoutedEventArgs());
        }

        private async void Twitch_Click(object sender, RoutedEventArgs e)
        {
            if (_twitch.IsConnected) { await _twitch.DisconnectAsync(); TwitchBtn.Content = "Connect"; }
            else { SaveChat(); TwitchBtn.Content = "Disconnect"; await _twitch.ConnectAsync(TwitchBox.Text); }
        }

        private async void Ssn_Click(object sender, RoutedEventArgs e)
        {
            if (_ssn.IsConnected) { await _ssn.DisconnectAsync(); SsnBtn.Content = "Connect"; }
            else { SaveChat(); SsnBtn.Content = "Disconnect"; await _ssn.ConnectAsync(SsnBox.Text); }
        }

        private async void Restream_Click(object sender, RoutedEventArgs e)
        {
            if (_restream.IsConnected) { await _restream.DisconnectAsync(); RestreamBtn.Content = "Connect"; }
            else { SaveChat(); RestreamBtn.Content = "Disconnect"; await _restream.ConnectAsync(RestreamBox.Text); }
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            PinButton.Foreground = _pinned
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0xa4, 0x37))
                : new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
            PinButton.ToolTip = _pinned ? "Unpin" : "Pin on top";
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            _collapsed = !_collapsed;
            ConnectPanel.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapseButton.Content = _collapsed ? "▼" : "▲"; // ▼ / ▲
            CollapseButton.ToolTip = _collapsed ? "Show the connect bar" : "Hide the connect bar (clean chat box)";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            SaveChat();
            _chattersTimer?.Stop();
            PointsService.Save();
            _overlayTimer.Stop();
            OverlayService.ClearChatHtml();
            _twitch.Dispose();
            _ssn.Dispose();
            _restream.Dispose();
            base.OnClosed(e);
        }

        public class ChatRow
        {
            public string Symbol { get; set; } = string.Empty;
            public Brush SymbolBrush { get; set; } = Brushes.Gray;
            public string User { get; set; } = string.Empty;
            public List<ChatSegment> Segments { get; set; } = new();
            public List<ChatBadge> Badges { get; set; } = new();
            public Brush UserBrush { get; set; } = Brushes.White;
            public Brush AvatarBrush { get; set; } = Brushes.Gray;
            public string Initial { get; set; } = string.Empty;
            public Brush RowBrush { get; set; } = Brushes.Transparent;
            public Brush BoxBrush { get; set; } = Brushes.DarkOliveGreen;
        }
    }
}
