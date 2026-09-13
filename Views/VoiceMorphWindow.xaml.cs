using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Configure the live mic morph engine and build/save morph voices with a mixer.</summary>
    public partial class VoiceMorphWindow : Window
    {
        private const string NoneLabel = "🔇 None — don't play back to me";
        // Empty OutputDevice = play back to the Windows default render endpoint. This is only
        // what the streamer hears; OBS Application Audio Capture grabs the app regardless.
        private const string DefaultLabel = "🎧 System default (recommended)";

        private readonly ObservableCollection<Row> _rows = new();
        private bool _ready;

        /// <summary>Set when the user clicks "OBS help" — the owner opens the How-To section on close.</summary>
        public bool OpenObsHelpRequested { get; private set; }

        // Refreshes the LIVE/off status while the window is open so watchdog recovery shows live.
        private System.Windows.Threading.DispatcherTimer? _statusTimer;

        // Mixer faders, keyed by effect (VoiceMorphService.MixBars).
        private readonly Dictionary<string, Slider> _mixSliders = new();
        private readonly Dictionary<string, TextBlock> _mixVals = new();

        // A TTS engine used only for the "Test with TTS" button (hear the mix without a mic).
        private readonly TtsService _ttsTest = new();

        public VoiceMorphWindow()
        {
            InitializeComponent();

            var s = SettingsService.LoadMorph();
            EnabledCb.IsChecked = s.Enabled;

            InputBox.ItemsSource = VoiceMorphService.InputDevices();
            var outputs = new System.Collections.Generic.List<string> { DefaultLabel, NoneLabel };
            outputs.AddRange(VoiceMorphService.OutputDevices());
            OutputBox.ItemsSource = outputs;
            InputBox.SelectedItem = InputBox.Items.OfType<string>().FirstOrDefault(d => d == s.InputDevice)
                                    ?? InputBox.Items.OfType<string>().FirstOrDefault();
            OutputBox.SelectedItem = s.OutputDevice == VoiceMorphService.NoneOutput
                ? NoneLabel
                : string.IsNullOrEmpty(s.OutputDevice)
                    ? DefaultLabel
                    : outputs.FirstOrDefault(d => d == s.OutputDevice) ?? DefaultLabel;
            UpdateOutputWarning();

            BuildMixer();
            EnsureStarterVoices();
            PresetList.ItemsSource = _rows;
            RefreshList();
            UpdateEngineStatus();
            _ready = true;

            _statusTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (_, _) => UpdateEngineStatus();
            _statusTimer.Start();
            Closed += (_, _) => { _statusTimer?.Stop(); try { _ttsTest.Dispose(); } catch { } };
        }

        private void RefreshEmpty() =>
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateEngineStatus()
        {
            string text; string hex;
            if (VoiceMorphService.IsRunning)
            {
                bool morph = VoiceMorphService.ActiveMorph.Length > 0;
                text = morph
                    ? $"🟢 LIVE — morph active: {VoiceMorphService.ActiveMorph}"
                    : "🟢 LIVE — the app is your audio source (normal voice).";
                hex = "#7fd47f";
            }
            else if (!string.IsNullOrEmpty(VoiceMorphService.LastError))
            {
                text = "⚠ Audio problem: " + VoiceMorphService.LastError + " — retrying…";
                hex = "#e0a030";
            }
            else
            {
                text = "⚪ Off — tick the box above to go live as an audio source.";
                hex = "#7a9070";
            }
            EngineStatus.Text = text;
            EngineStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }

        // ---- engine config ----

        private void SaveEngineSettings()
        {
            var s = SettingsService.LoadMorph();
            s.Enabled = EnabledCb.IsChecked == true;
            s.InputDevice = InputBox.SelectedItem as string ?? string.Empty;
            var output = OutputBox.SelectedItem as string ?? string.Empty;
            s.OutputDevice = output == NoneLabel ? VoiceMorphService.NoneOutput
                           : output == DefaultLabel ? string.Empty   // empty = Windows default render endpoint
                           : output;
            SettingsService.SaveMorph(s);
        }

        // Warn only when 🔇 None is chosen: with no playback there's no audio session, so
        // OBS Application Audio Capture has nothing to grab. Any real device (default or
        // specific) is fine for OBS — App Audio Capture hears the app regardless of device.
        private void UpdateOutputWarning()
        {
            var output = OutputBox.SelectedItem as string ?? string.Empty;
            if (OutputWarn != null)
                OutputWarn.Visibility = output == NoneLabel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Enabled_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            SaveEngineSettings();
            if (EnabledCb.IsChecked == true) VoiceMorphService.Start();
            else VoiceMorphService.Stop();
            UpdateEngineStatus();
        }

        private void Device_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            SaveEngineSettings();
            UpdateOutputWarning();
            if (EnabledCb.IsChecked == true) { VoiceMorphService.Start(); UpdateEngineStatus(); }
        }

        // ---- builder / mixer ----

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        // Build one fader per effect. Centre = neutral; drag right to add. Bidirectional
        // faders (e.g. Tone) also do the opposite effect when dragged left.
        private void BuildMixer()
        {
            foreach (var bar in VoiceMorphService.MixBars)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                var lbl = new TextBlock
                {
                    Text = bar.Label, Foreground = Brush("#a8c488"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center, Width = 120,
                };
                var slider = new Slider
                {
                    Width = 330, Minimum = -100, Maximum = 100, Value = 0,
                    VerticalAlignment = VerticalAlignment.Center,
                    TickFrequency = 10, IsSnapToTickEnabled = false,
                    ToolTip = bar.Bidir
                        ? $"Right = {bar.Right}, left = {bar.Left}. Centre = neutral."
                        : "Right = more of this effect. Centre/left = off.",
                };
                slider.SetResourceReference(Control.ForegroundProperty, "ThemeAccent");
                var val = new TextBlock
                {
                    Text = FormatBar(bar, 0), Foreground = Brush("#e8e0c4"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 70,
                };
                var b = bar;
                slider.ValueChanged += (_, _) => val.Text = FormatBar(b, (int)Math.Round(slider.Value));
                _mixSliders[bar.Key] = slider;
                _mixVals[bar.Key] = val;
                row.Children.Add(lbl);
                row.Children.Add(slider);
                row.Children.Add(val);
                MixHost.Children.Add(row);
            }
        }

        private static string FormatBar(VoiceMorphService.MixBar bar, int v)
        {
            if (bar.Bidir)
                return v > 0 ? $"{bar.Right} {v}%" : v < 0 ? $"{bar.Left} {-v}%" : "—";
            return v > 0 ? $"{v}%" : "off";
        }

        private void Pitch_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchVal != null) PitchVal.Text = (int)PitchSlider.Value + " st";
        }

        // Push the bars to match a saved MorphPreset (pitch + mix).
        private void ApplyToMixer(MorphPreset p)
        {
            foreach (var sl in _mixSliders.Values) sl.Value = 0;
            PitchSlider.Value = Math.Clamp(p.PitchSemitones, -12, 12);
            if (p.Mix != null)
                foreach (var kv in p.Mix)
                    if (_mixSliders.TryGetValue(kv.Key, out var sl))
                        sl.Value = Math.Clamp(kv.Value, -100, 100);
        }

        // Seed the built-in starter voices into the single voice list, once. Deleting one
        // afterwards makes it stay gone (we don't re-seed).
        private static void EnsureStarterVoices()
        {
            var s = SettingsService.LoadMorph();
            if (s.StartersSeeded) return;
            void Add(string name, int pitch, params (string k, int v)[] mix)
            {
                if (s.Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
                var d = new Dictionary<string, int>();
                foreach (var m in mix) d[m.k] = m.v;
                s.Presets.Add(new MorphPreset { Name = name, PitchSemitones = pitch, Effect = "none", Mix = d, TimerSeconds = 60 });
            }
            Add("YHWH", -2, ("tone", -30), ("wobble", 4), ("chorus", 19), ("reverb", 10), ("echo", 10));
            Add("Cathedral", -4, ("reverb", 85), ("echo", 45));
            Add("Angelic", 3, ("chorus", 60), ("reverb", 55));
            Add("Skeletor", 6, ("tone", 76), ("robot", 1), ("wobble", 69), ("reverb", 2), ("echo", 4));
            s.StartersSeeded = true;
            SettingsService.SaveMorph(s);
        }

        private void RefreshList()
        {
            _rows.Clear();
            foreach (var p in SettingsService.LoadMorph().Presets) _rows.Add(Row.From(p));
            RefreshEmpty();
        }

        // Load a saved voice into the bars to edit it (Save then overwrites it by name).
        private void Load_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Row r) return;
            var p = SettingsService.LoadMorph().Presets.FirstOrDefault(x => x.Name == r.Name);
            if (p == null) return;
            ApplyToMixer(p);
            NameBox.Text = p.Name;
            TimerBox.Text = p.TimerSeconds.ToString();
            Status.Text = $"Loaded “{p.Name}” — tweak the bars, then Save to overwrite it.";
        }

        private MorphPreset BuildFromUi(string name)
        {
            var mix = new Dictionary<string, int>();
            foreach (var bar in VoiceMorphService.MixBars)
            {
                if (!_mixSliders.TryGetValue(bar.Key, out var sl)) continue;
                int amt = (int)Math.Round(sl.Value);
                // Right adds the effect; left is kept only for bidirectional faders (the opposite).
                if (amt > 0 || (bar.Bidir && amt < 0)) mix[bar.Key] = amt;
            }
            return new MorphPreset
            {
                Name = name,
                PitchSemitones = (int)PitchSlider.Value,
                Effect = "none",
                Mix = mix,
                TimerSeconds = int.TryParse(TimerBox.Text.Trim(), out var t) ? Math.Clamp(t, 5, 3600) : 60,
            };
        }

        private void Try_Click(object sender, RoutedEventArgs e)
        {
            if (EnabledCb.IsChecked != true) { Status.Text = "Enable the mic morph first."; return; }
            var p = BuildFromUi("(preview)");
            p.TimerSeconds = 30;
            if (VoiceMorphService.Activate(p)) Status.Text = "Live for 30s — speak into your mic.";
            else Status.Text = "Couldn't start: " + VoiceMorphService.LastError;
            UpdateEngineStatus();
        }

        // Play a spoken test line through the current mixer bars — no microphone needed.
        private void TtsTest_Click(object sender, RoutedEventArgs e)
        {
            var preset = BuildFromUi("(tts-test)");
            var s = SettingsService.LoadMorph();
            _ttsTest.OutputDevice = string.IsNullOrEmpty(s.OutputDevice) || s.OutputDevice == VoiceMorphService.NoneOutput
                ? string.Empty : s.OutputDevice;
            _ttsTest.StopAll();
            var voice = TtsService.InstalledVoices()
                            .FirstOrDefault(v => v.Contains("David", StringComparison.OrdinalIgnoreCase))
                        ?? TtsService.InstalledVoices().FirstOrDefault();
            _ttsTest.SpeakMorphTest("Well well well, look who it is. This is how your voice sounds.", voice, preset);
            Status.Text = "Playing a TTS test through your current bars…";
        }

        private void Revert_Click(object sender, RoutedEventArgs e)
        {
            VoiceMorphService.ClearMorph();
            Status.Text = "Back to normal voice.";
            UpdateEngineStatus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { Status.Text = "Type a name above first, then Save."; return; }

            var s = SettingsService.LoadMorph();
            bool existed = s.Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            s.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            s.Presets.Add(BuildFromUi(name));
            SettingsService.SaveMorph(s);

            RefreshList();
            Status.Text = existed
                ? $"Updated “{name}”."
                : $"Saved “{name}” — attach it to a redeem in 🪙 Points & Redeems.";
        }

        // ---- saved list ----

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Row r) return;
            if (EnabledCb.IsChecked != true) { Status.Text = "Enable the mic morph first."; return; }
            var s = SettingsService.LoadMorph();
            var p = s.Presets.FirstOrDefault(x => x.Name == r.Name);
            if (p != null && VoiceMorphService.Activate(p))
                Status.Text = $"\"{p.Name}\" on for {p.TimerSeconds}s.";
            else Status.Text = "Couldn't start: " + VoiceMorphService.LastError;
            UpdateEngineStatus();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Row r) return;
            var s = SettingsService.LoadMorph();
            s.Presets.RemoveAll(p => p.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
            SettingsService.SaveMorph(s);
            _rows.Remove(r);
            RefreshEmpty();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            // Close this modal and let the owner Control Center jump to the OBS guide.
            OpenObsHelpRequested = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public sealed class Row
        {
            public string Name { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
            public static Row From(MorphPreset p)
            {
                string fx = p.Effect;
                if (p.Mix != null && p.Mix.Any(kv => kv.Value != 0))
                {
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var bar in VoiceMorphService.MixBars)
                    {
                        if (!p.Mix.TryGetValue(bar.Key, out var v) || v == 0) continue;
                        parts.Add(bar.Bidir
                            ? (v > 0 ? $"{bar.Right} {v}%" : $"{bar.Left} {-v}%")
                            : $"{bar.Key} {v}%");
                    }
                    if (parts.Count > 0) fx = string.Join(", ", parts);
                }
                return new Row
                {
                    Name = p.Name,
                    Detail = $"pitch {(p.PitchSemitones >= 0 ? "+" : "")}{p.PitchSemitones} st · {fx} · {p.TimerSeconds}s timer",
                };
            }
        }
    }
}
