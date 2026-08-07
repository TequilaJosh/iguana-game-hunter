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
            _profiles = TtsService.BuildProfiles();
            TtsVoice.ItemsSource = _profiles;
            var match = _profiles.FirstOrDefault(p => p.Voice == tts.Voice && p.Pitch == tts.Pitch)
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
            NavAppearance.Tag = NavChat.Tag = NavOverlay.Tag = null;
            PanelAppearance.Visibility = PanelChat.Visibility = PanelOverlay.Visibility = Visibility.Collapsed;

            if (sender == NavChat) { NavChat.Tag = "active"; PanelChat.Visibility = Visibility.Visible; }
            else if (sender == NavOverlay) { NavOverlay.Tag = "active"; PanelOverlay.Visibility = Visibility.Visible; }
            else { NavAppearance.Tag = "active"; PanelAppearance.Visibility = Visibility.Visible; }
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
                Pitch = profile?.Pitch ?? 0,
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

        private void TtsTest_Click(object sender, RoutedEventArgs e)
        {
            // Test the currently-selected single voice (random-per-chatter picks live in chat).
            var profile = TtsVoice.SelectedItem as VoiceProfile;
            _ttsTest.StopAll();
            _ttsTest.Speak("This is a text to speech test. Your chat will sound like this.",
                profile?.Voice, profile?.Pitch ?? 0, (int)TtsRate.Value, (int)TtsVolume.Value);
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
