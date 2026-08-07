using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Consolidated settings hub: Appearance (theme), Chat, and Overlay.</summary>
    public partial class SettingsWindow : Window
    {
        private ThemeSettings _theme;
        private bool _ready;
        private TextPanelsWindow? _textPanelsWindow;
        private readonly TtsService _ttsTest = new();
        private List<VoiceProfile> _profiles = new();
        private readonly List<(string label, Func<ThemeSettings, string> get, Action<ThemeSettings, string> set)> _slots;

        public SettingsWindow()
        {
            InitializeComponent();
            _theme = Clone(ThemeService.Current);

            _slots = new()
            {
                ("Accent",           t => t.Accent,     (t, v) => t.Accent = v),
                ("Accent (deep)",    t => t.AccentDeep, (t, v) => t.AccentDeep = v),
                ("Accent 2 (amber)", t => t.Accent2,    (t, v) => t.Accent2 = v),
                ("Background",       t => t.BgBase,     (t, v) => t.BgBase = v),
                ("Background lines", t => t.BgTile,     (t, v) => t.BgTile = v),
            };

            PresetList.ItemsSource = BuildPresetVms();
            BuildCustomRows();

            // Chat
            var chat = SettingsService.LoadChat();
            AutoConnectCheck.IsChecked = chat.AutoConnect;
            OpacitySlider.Value = chat.Opacity is >= 0.25 and <= 1.0 ? chat.Opacity : 1.0;

            // Text to speech
            var tts = SettingsService.LoadTts();
            _profiles = TtsService.AllProfiles(tts.Custom);
            TtsVoice.ItemsSource = _profiles;
            var match = _profiles.FirstOrDefault(p => p.Voice == tts.Voice && p.Effect == tts.Effect)
                        ?? _profiles.FirstOrDefault();
            TtsVoice.SelectedItem = match;
            TtsEnabled.IsChecked = tts.Enabled;
            TtsPerChatter.IsChecked = tts.PerChatterVoices;
            TtsRate.Value = tts.Rate;
            TtsVolume.Value = tts.Volume;
            TtsReadName.IsChecked = tts.ReadName;
            TtsSkipCommands.IsChecked = tts.SkipCommands;
            UpdateVoicePickerState();

            // Overlay
            PortBox.Text = OverlayServer.Port.ToString();
            ChatLinesBox.Text = SettingsService.LoadOverlayChatLines().ToString();
            UpdateOverlayStatus();

            _ready = true;
        }

        // ---- left nav ----

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            NavAppearance.Tag = NavChat.Tag = NavOverlay.Tag = NavHelp.Tag = null;
            PanelAppearance.Visibility = PanelChat.Visibility =
                PanelOverlay.Visibility = PanelHelp.Visibility = Visibility.Collapsed;

            if (sender == NavChat) { NavChat.Tag = "active"; PanelChat.Visibility = Visibility.Visible; }
            else if (sender == NavOverlay) { NavOverlay.Tag = "active"; PanelOverlay.Visibility = Visibility.Visible; }
            else if (sender == NavHelp)
            {
                NavHelp.Tag = "active"; PanelHelp.Visibility = Visibility.Visible;
                BuildHelp();
            }
            else { NavAppearance.Tag = "active"; PanelAppearance.Visibility = Visibility.Visible; }
        }

        // ---- how-to guides ----

        private bool _helpBuilt;

        private void BuildHelp()
        {
            if (_helpBuilt) return;
            _helpBuilt = true;

            void Section(string title, string body)
            {
                HelpContent.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = (System.Windows.Media.Brush)FindResource("ThemeAccent"),
                    FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 4),
                    TextWrapping = TextWrapping.Wrap,
                });
                HelpContent.Children.Add(new TextBlock
                {
                    Text = body,
                    Foreground = Brush("#c4d4a8"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17,
                });
            }

            HelpContent.Children.Add(new TextBlock
            {
                Text = "How to use LazerGuanas Game Hunter",
                Foreground = Brush("#e8e0c4"), FontSize = 16, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2),
            });
            HelpContent.Children.Add(new TextBlock
            {
                Text = "A quick guide to every part of the app. Hover most controls for a tooltip too.",
                Foreground = Brush("#7a9070"), FontSize = 11, TextWrapping = TextWrapping.Wrap,
            });

            Section("The board (Dormant / Hunting / Devoured)",
                "Your games live in three columns: Dormant (backlog, not started), Hunting (actively playing), and Devoured (finished). " +
                "Drag a card between columns, or right-click a card for options. Click a card to open its details, rating, and notes.");

            Section("Adding games",
                "Use + Add Game (top right) to add a title, platform, who suggested it, and a suggestion type. " +
                "Viewers can also add games from chat with \"!request <game>\" — those land in Dormant automatically.");

            Section("Sessions & timer",
                "Press Start on a Hunting game to begin a play session; the timer tracks your playtime and shows on the overlay. " +
                "Press Stop to end it. Total time is remembered per game.");

            Section("The challenge wheel",
                "Right-click a game → Wheel to spin for random challenges. Set how many items the wheel holds, add your own entries, " +
                "and spin — landed challenges appear on the overlay in real time. Rolled challenges show even before a session starts.");

            Section("Recap",
                "The Recap button builds a shareable summary of your progress — games played, beaten, and time spent.");

            Section("Chat — connecting",
                "Open Chat (top bar). Connect any of: Twitch (just your channel name, no login), Social Stream Ninja (one session ID pulls in " +
                "every platform), or Restream (an access token). The SSN session ID is saved between streams. " +
                "\"Auto-connect\" (Settings → Chat) reconnects your saved sources whenever you start a game.");

            Section("Chat — Social Stream Ninja setup",
                "SSN brings all platforms through one connection. In SSN's Global settings → Mechanics, turn ON \"Enable remote API control\" " +
                "and \"Send chat messages to API server\", then paste your session ID (the part after session= in the dock URL). " +
                "The \"SSN not showing chat? Setup guide\" link in the chat window walks through it.");

            Section("Chat — two-way & replies",
                "The send bar at the bottom of the chat window posts a message out through SSN to your chats; pick a target (all chats or one platform). " +
                "Bot replies for !request / !points / redeems reply only in the chat the viewer used, so other platforms aren't spammed.");

            Section("Chatters list & points",
                "The Chatters button shows who's active now: green = active, yellow = lurking (idle past your set time), then removed. " +
                "In Settings → Chat → Features you can award points to everyone on the list on an interval; viewers check their balance with !points. " +
                "Set the points name, interval, and amount there.");

            Section("Features — counts & chat style",
                "Settings → Chat → Features also controls: the chatter count header (total or per-source, on overlay or not), and the chat look " +
                "(classic log or colored boxes with your own palette of up to 10 colors).");

            Section("Sound alerts",
                "Settings → Chat → Sound Alerts maps chat commands to your own sound files (mp3/wav/ogg and more), each with its own volume and a Test button. " +
                "The 🔇 panic button in the chat window instantly stops and mutes all alerts (and TTS).");

            Section("Point redeems & stream effects",
                "In Features → Point redeems, map a command (e.g. !confetti) to an on-stream effect: Confetti, Fireworks, Screen shake, a Custom image, " +
                "or a Video. Set a point cost; viewers spend points to trigger it. Each has a Test button and a volume slider. Videos play on the overlay (mp4/webm work best).");

            Section("Text to speech (TTS)",
                "Settings → Chat → Text to Speech reads incoming chat aloud using offline Windows voices. Choose a voice, speed, and volume, " +
                "and whether to say the sender's name or skip ! commands. Turn on \"Give each chatter their own random voice\" so everyone gets a " +
                "consistent voice saved across streams. Busy chats auto-skip extras so speech never lags.");

            Section("Voice Lab (custom voices)",
                "Settings → Chat → Voice Lab lets you build your own voices: pick a base voice + a funny effect (chipmunk, deep, robot, ghost, alien, demon), " +
                "type a phrase, Test it, name it, and Save. Saved voices appear in the voice list and the per-chatter pool.");

            Section("Appearance (themes)",
                "Settings → Appearance re-colors the whole app. Pick a preset (Reptile, Amber, Ocean, Royal, Crimson, Mono) or set custom colors " +
                "for accent, deep accent, amber, background, and tiles. Changes apply instantly and are saved.");

            Section("OBS overlay",
                "Settings → Overlay serves a live browser overlay for OBS. Copy the URL into an OBS Browser source (set it to 1280×720). " +
                "Set the port and how many chat lines show. \"Edit overlay layout\" opens the browser editor where you drag/resize blocks, set fonts, " +
                "toggle which elements appear, adjust per-element background opacity, and save presets.");

            Section("Text overlays",
                "Settings → Overlay → Text overlays creates up to 5 custom OBS text panels, each with a styled header, divider, and lines " +
                "(font, size, color, scrolling). Add bordered left/right side blocks with text or images, horizontal or vertical, with their own scroll. " +
                "Each panel has its own URL to add as a Browser source, and updates live as you type.");

            Section("Updates",
                "The app checks for updates on launch and updates itself silently; your open windows reopen afterward. " +
                "The ↻ button (top bar) checks manually.");
        }

        // ---- theme ----

        private static ThemeSettings Clone(ThemeSettings t) => new()
        {
            PresetName = t.PresetName, Accent = t.Accent, AccentDeep = t.AccentDeep,
            Accent2 = t.Accent2, BgBase = t.BgBase, BgTile = t.BgTile,
        };

        private static List<PresetVm> BuildPresetVms()
        {
            var list = new List<PresetVm>();
            foreach (var p in ThemeSettings.Presets) list.Add(new PresetVm(p));
            return list;
        }

        private void BuildCustomRows()
        {
            CustomColors.Children.Clear();
            foreach (var slot in _slots)
                CustomColors.Children.Add(BuildColorRow(slot.label, slot.get(_theme), hex =>
                {
                    slot.set(_theme, hex);
                    _theme.PresetName = "Custom";
                    ApplyLive();
                    BuildCustomRows();
                }));
        }

        private FrameworkElement BuildColorRow(string label, string hex, Action<string> onPick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            row.Children.Add(new TextBlock
            {
                Text = label, Foreground = Brush("#a8c488"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Width = 128,
            });
            var swatch = new Border
            {
                Width = 26, Height = 22, CornerRadius = new CornerRadius(3),
                BorderBrush = Brush("#4a7c3a"), BorderThickness = new Thickness(1),
                Background = Brush(hex), Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Pick a color",
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                var picked = ColorPickerWindow.Pick(this, hex);
                if (picked != null) onPick(picked);
            };
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock
            {
                Text = hex, Foreground = Brush("#e8e0c4"), FontFamily = new FontFamily("Consolas"),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            });
            return row;
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PresetVm vm)
            {
                _theme = Clone(vm.Source);
                ApplyLive();
                BuildCustomRows();
            }
        }

        private void ResetTheme_Click(object sender, RoutedEventArgs e)
        {
            _theme = Clone(ThemeSettings.Presets[0]);
            ApplyLive();
            BuildCustomRows();
        }

        private void ApplyLive() => ThemeService.Apply(_theme);

        private static SolidColorBrush Brush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Gray); }
        }

        // ---- chat ----

        private void AutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var s = SettingsService.LoadChat();
            s.AutoConnect = AutoConnectCheck.IsChecked == true;
            SettingsService.SaveChat(s);
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_ready) return;
            var s = SettingsService.LoadChat();
            s.Opacity = OpacitySlider.Value;
            SettingsService.SaveChat(s);
            ChatWindow.Current?.ApplyChatOpacity(OpacitySlider.Value);
        }

        // ---- text to speech ----

        private ChatTtsSettings ReadTtsUi()
        {
            var profile = TtsVoice.SelectedItem as VoiceProfile;
            return new ChatTtsSettings
            {
                Enabled = TtsEnabled.IsChecked == true,
                PerChatterVoices = TtsPerChatter.IsChecked == true,
                Voice = profile?.Voice ?? string.Empty,
                Effect = profile?.Effect ?? "normal",
                Rate = (int)TtsRate.Value,
                Volume = (int)TtsVolume.Value,
                ReadName = TtsReadName.IsChecked == true,
                SkipCommands = TtsSkipCommands.IsChecked == true,
            };
        }

        private void UpdateVoicePickerState()
        {
            bool perChatter = TtsPerChatter.IsChecked == true;
            TtsVoice.IsEnabled = !perChatter;
            TtsVoiceLbl.Foreground = new SolidColorBrush(
                perChatter ? Color.FromRgb(0x50, 0x60, 0x50) : Color.FromRgb(0xa8, 0xc4, 0x88));
        }

        private void SaveTts()
        {
            if (!_ready) return;
            SettingsService.SaveTts(ReadTtsUi());
            ChatWindow.Current?.ReloadTts();
        }

        private void Tts_Changed(object sender, RoutedEventArgs e) => SaveTts();
        private void Tts_Slider(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveTts();

        private void TtsPerChatter_Changed(object sender, RoutedEventArgs e)
        {
            UpdateVoicePickerState();
            SaveTts();
        }

        private void VoiceLab_Click(object sender, RoutedEventArgs e)
        {
            new VoiceLabWindow { Owner = this }.ShowDialog();
            // Refresh the voice list — custom voices may have been added/removed.
            var tts = SettingsService.LoadTts();
            var selected = TtsVoice.SelectedItem as VoiceProfile;
            _profiles = TtsService.AllProfiles(tts.Custom);
            TtsVoice.ItemsSource = _profiles;
            TtsVoice.SelectedItem = _profiles.FirstOrDefault(p =>
                selected != null && p.Voice == selected.Voice && p.Effect == selected.Effect)
                ?? _profiles.FirstOrDefault();
        }

        private void TtsTest_Click(object sender, RoutedEventArgs e)
        {
            // Test the currently-selected single voice (random-per-chatter picks live in chat).
            var profile = TtsVoice.SelectedItem as VoiceProfile;
            _ttsTest.StopAll();
            _ttsTest.Speak("This is a text to speech test. Your chat will sound like this.",
                profile?.Voice, profile?.Effect ?? "normal", (int)TtsRate.Value, (int)TtsVolume.Value);
        }

        private void Features_Click(object sender, RoutedEventArgs e)
        {
            var win = new ChatFeaturesWindow { Owner = this };
            if (win.ShowDialog() == true) ChatWindow.Current?.ReloadFeatures();
        }

        private void SoundAlerts_Click(object sender, RoutedEventArgs e)
        {
            var win = new SoundAlertsWindow { Owner = this };
            if (win.ShowDialog() == true) ChatWindow.Current?.ReloadSoundAlerts();
        }

        // ---- overlay ----

        private string OverlayUrl => $"http://localhost:{OverlayServer.Port}/";

        private void ApplyPort_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortBox.Text.Trim(), out int port) || port is < 1 or > 65535)
            {
                OverlayStatus.Text = "Enter a port between 1 and 65535 (default 3620).";
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
            catch { /* clipboard can be momentarily locked */ }
        }

        private void ChatLines_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready || !int.TryParse(ChatLinesBox.Text.Trim(), out int n)) return;
            n = Math.Clamp(n, 5, 100);
            ChatLinesBox.Text = n.ToString();
            SettingsService.SaveOverlayChatLines(n);
            ChatWindow.Current?.ReloadOverlayLines();
            OverlayStatus.Text = $"Chat set to {n} lines — refresh the OBS source to apply.";
        }

        private void EditLayout_Click(object sender, RoutedEventArgs e)
        {
            if (!OverlayServer.IsRunning)
            {
                OverlayStatus.Text = "Overlay server isn't running — can't open the editor.";
                return;
            }
            var url = OverlayUrl + "?edit=1";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                OverlayStatus.Text = "Opened the layout editor in your browser — it saves automatically.";
            }
            catch { OverlayStatus.Text = "Couldn't open a browser. Go to " + url + " manually."; }
        }

        private void TextPanels_Click(object sender, RoutedEventArgs e)
        {
            if (_textPanelsWindow != null) { _textPanelsWindow.Activate(); return; }
            _textPanelsWindow = new TextPanelsWindow { Owner = this };
            _textPanelsWindow.Closed += (_, _) => _textPanelsWindow = null;
            _textPanelsWindow.Show();
        }

        private void UpdateOverlayStatus() =>
            OverlayStatus.Text = OverlayServer.IsRunning
                ? $"Serving at {OverlayUrl} — use this as the OBS Browser source URL."
                : "Overlay server is not running" +
                  (string.IsNullOrEmpty(OverlayServer.LastError) ? "." : $": {OverlayServer.LastError}. Try another port.");

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _ttsTest.Dispose();
            base.OnClosed(e);
        }

        public class PresetVm
        {
            public ThemeSettings Source { get; }
            public string PresetName => Source.PresetName;
            public Color AccentColor => Parse(Source.Accent);
            public Color Accent2Color => Parse(Source.Accent2);
            public Color BgColor => Parse(Source.BgBase);
            public PresetVm(ThemeSettings s) => Source = s;
            private static Color Parse(string hex)
            {
                try { return (Color)ColorConverter.ConvertFromString(hex); }
                catch { return Colors.Gray; }
            }
        }
    }
}
