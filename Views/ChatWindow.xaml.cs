using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
        private const int OverlayMessages = 20;

        private readonly TwitchChatConnector _twitch = new();
        private readonly SocialStreamConnector _ssn = new();
        private readonly RestreamConnector _restream = new();
        private readonly ObservableCollection<ChatRow> _rows = new();
        private readonly List<ChatMessage> _recent = new();
        private readonly DispatcherTimer _overlayTimer;
        private bool _pinned;
        private bool _collapsed;
        private bool _chatDirty;

        private readonly SoundService _sound = new();

        /// <summary>Raised when a viewer runs "!request &lt;game&gt;" — (game title, requester).</summary>
        public Action<string, string>? OnGameRequested;

        private static readonly Brush DefaultUser = Frozen("#a8c488");

        public ChatWindow()
        {
            InitializeComponent();
            MessageList.ItemsSource = _rows;

            Wire(_twitch, s => TwitchStatus.Text = s);
            Wire(_ssn, s => SsnStatus.Text = s);
            Wire(_restream, s => RestreamStatus.Text = s);

            // Restore saved connection details (the SSN session ID is persistent).
            var saved = SettingsService.LoadChat();
            TwitchBox.Text = saved.TwitchChannel;
            SsnBox.Text = saved.SsnSession;
            RestreamBox.Text = saved.RestreamToken;

            _sound.SetAlerts(SettingsService.LoadSoundAlerts());

            // Refresh the OBS chat.html a few times a second when there's new chat.
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _overlayTimer.Tick += (_, _) =>
            {
                if (!_chatDirty) return;
                _chatDirty = false;
                OverlayService.WriteChatHtml(_recent.TakeLast(OverlayMessages).ToList());
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
                while (_recent.Count > OverlayMessages) _recent.RemoveAt(0);
                _chatDirty = true;

                HandleCommands(m);

                if (atBottom) MsgScroll.ScrollToEnd();
            });
        }

        private void HandleCommands(ChatMessage m)
        {
            var text = m.Text ?? string.Empty;

            // Sound alerts: play if the first word matches a configured command.
            _sound.CheckAndPlay(text);

            // "!request <game>" -> hand off to the board (deduped there).
            var parts = text.TrimStart().Split(new[] { ' ' }, 2);
            if (parts.Length == 2 &&
                string.Equals(parts[0], "!request", StringComparison.OrdinalIgnoreCase))
            {
                var game = parts[1].Trim();
                if (game.Length > 0) OnGameRequested?.Invoke(game, m.User);
            }
        }

        private void SoundAlerts_Click(object sender, RoutedEventArgs e)
        {
            var win = new SoundAlertsWindow { Owner = this };
            if (win.ShowDialog() == true)
                _sound.SetAlerts(SettingsService.LoadSoundAlerts());
        }

        private ChatRow ToRow(ChatMessage m)
        {
            Brush userBrush = DefaultUser;
            if (!string.IsNullOrWhiteSpace(m.UserColor))
            {
                try { userBrush = Frozen(m.UserColor); } catch { /* keep default */ }
            }

            Brush symbolBrush = DefaultUser;
            try { symbolBrush = Frozen(OverlayService.ChatColorHex(m.Platform)); } catch { }

            return new ChatRow
            {
                Symbol = OverlayService.ChatSymbol(m.Platform),
                SymbolBrush = symbolBrush,
                User = m.User,
                Segments = m.Segments,
                UserBrush = userBrush,
            };
        }

        private void SaveChat() => SettingsService.SaveChat(new ChatSettings
        {
            TwitchChannel = TwitchBox.Text.Trim(),
            SsnSession = SsnBox.Text.Trim(),
            RestreamToken = RestreamBox.Text.Trim(),
        });

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
            public Brush UserBrush { get; set; } = Brushes.White;
        }
    }
}
