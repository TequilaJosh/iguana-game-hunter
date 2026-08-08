using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Configure the live mic morph engine and build/save morph voices with timers.</summary>
    public partial class VoiceMorphWindow : Window
    {
        private static readonly string[] EffectKeys =
            { "none", "robot", "whisper", "echo", "distortion", "flanger", "vibrato", "tremolo", "autowah" };

        private const string NoneLabel = "🔇 None — don't play back to me";

        private readonly ObservableCollection<Row> _rows = new();
        private bool _ready;

        public VoiceMorphWindow()
        {
            InitializeComponent();

            var s = SettingsService.LoadMorph();
            EnabledCb.IsChecked = s.Enabled;

            InputBox.ItemsSource = VoiceMorphService.InputDevices();
            var outputs = new System.Collections.Generic.List<string> { NoneLabel };
            outputs.AddRange(VoiceMorphService.OutputDevices());
            OutputBox.ItemsSource = outputs;
            InputBox.SelectedItem = InputBox.Items.OfType<string>().FirstOrDefault(d => d == s.InputDevice)
                                    ?? InputBox.Items.OfType<string>().FirstOrDefault();
            OutputBox.SelectedItem = s.OutputDevice == VoiceMorphService.NoneOutput
                ? NoneLabel
                : outputs.FirstOrDefault(d => d == s.OutputDevice)
                  ?? outputs.Skip(1).FirstOrDefault() ?? NoneLabel;

            EffectBox.ItemsSource = EffectKeys;
            EffectBox.SelectedIndex = 0;

            foreach (var p in s.Presets) _rows.Add(Row.From(p));
            PresetList.ItemsSource = _rows;
            RefreshEmpty();
            UpdateEngineStatus();
            _ready = true;
        }

        private void RefreshEmpty() =>
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateEngineStatus() =>
            EngineStatus.Text = VoiceMorphService.IsRunning
                ? "Mic chain running" +
                  (VoiceMorphService.ActiveMorph.Length > 0 ? $" — morph active: {VoiceMorphService.ActiveMorph}" : " (normal voice)")
                : (string.IsNullOrEmpty(VoiceMorphService.LastError)
                    ? "Mic chain off."
                    : "Mic chain error: " + VoiceMorphService.LastError);

        // ---- engine config ----

        private void SaveEngineSettings()
        {
            var s = SettingsService.LoadMorph();
            s.Enabled = EnabledCb.IsChecked == true;
            s.InputDevice = InputBox.SelectedItem as string ?? string.Empty;
            var output = OutputBox.SelectedItem as string ?? string.Empty;
            s.OutputDevice = output == NoneLabel ? VoiceMorphService.NoneOutput : output;
            SettingsService.SaveMorph(s);
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
            if (EnabledCb.IsChecked == true) { VoiceMorphService.Start(); UpdateEngineStatus(); }
        }

        // ---- builder ----

        private void Pitch_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchVal != null) PitchVal.Text = (int)PitchSlider.Value + " st";
        }

        private MorphPreset BuildFromUi(string name) => new()
        {
            Name = name,
            PitchSemitones = (int)PitchSlider.Value,
            Effect = EffectBox.SelectedItem as string ?? "none",
            TimerSeconds = int.TryParse(TimerBox.Text.Trim(), out var t) ? Math.Clamp(t, 5, 3600) : 60,
        };

        private void Try_Click(object sender, RoutedEventArgs e)
        {
            if (EnabledCb.IsChecked != true) { Status.Text = "Enable the mic morph first."; return; }
            var p = BuildFromUi("(preview)");
            p.TimerSeconds = 30;
            if (VoiceMorphService.Activate(p)) Status.Text = "Live for 30s — speak into your mic.";
            else Status.Text = "Couldn't start: " + VoiceMorphService.LastError;
            UpdateEngineStatus();
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
            if (name.Length == 0) { Status.Text = "Give it a name first."; return; }

            var s = SettingsService.LoadMorph();
            s.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            s.Presets.Add(BuildFromUi(name));
            SettingsService.SaveMorph(s);

            _rows.Clear();
            foreach (var p in s.Presets) _rows.Add(Row.From(p));
            RefreshEmpty();
            NameBox.Clear();
            Status.Text = $"Saved \"{name}\" — usable in point redeems now.";
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

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public sealed class Row
        {
            public string Name { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
            public static Row From(MorphPreset p) => new()
            {
                Name = p.Name,
                Detail = $"pitch {(p.PitchSemitones >= 0 ? "+" : "")}{p.PitchSemitones} st · {p.Effect} · {p.TimerSeconds}s timer",
            };
        }
    }
}
