using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Build, test and save custom voices (base voice + funny effect), with a test phrase.</summary>
    public partial class VoiceLabWindow : Window
    {
        private readonly TtsService _tts = new();
        private readonly ObservableCollection<Row> _rows = new();

        public VoiceLabWindow()
        {
            InitializeComponent();

            VoiceBox.ItemsSource = TtsService.InstalledVoices();
            if (VoiceBox.Items.Count > 0) VoiceBox.SelectedIndex = 0;
            EffectBox.ItemsSource = TtsService.Effects;
            EffectBox.SelectedIndex = 0;

            foreach (var c in SettingsService.LoadTts().Custom)
                _rows.Add(Row.From(c));
            CustomList.ItemsSource = _rows;
            RefreshEmpty();
        }

        private void RefreshEmpty() =>
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private string SelectedVoice => VoiceBox.SelectedItem as string ?? string.Empty;
        private string SelectedEffect => (EffectBox.SelectedItem as TtsService.Effect)?.Key ?? "normal";
        private string Phrase => string.IsNullOrWhiteSpace(PhraseBox.Text) ? "Hello there!" : PhraseBox.Text.Trim();

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            _tts.StopAll();
            _tts.Speak(Phrase, SelectedVoice, SelectedEffect, 0, 100);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0) { Status.Text = "Give it a name first."; return; }

            var tts = SettingsService.LoadTts();
            tts.Custom.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            tts.Custom.Add(new CustomVoice { Name = name, Voice = SelectedVoice, Effect = SelectedEffect });
            SettingsService.SaveTts(tts);

            _rows.Clear();
            foreach (var c in tts.Custom) _rows.Add(Row.From(c));
            RefreshEmpty();
            NameBox.Clear();
            Status.Text = $"Saved \"{name}\".";
            ChatWindow.Current?.ReloadTts();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Row r)
            {
                _tts.StopAll();
                _tts.Speak(Phrase, r.Voice, r.Effect, 0, 100);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Row r) return;
            var tts = SettingsService.LoadTts();
            tts.Custom.RemoveAll(c => c.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
            SettingsService.SaveTts(tts);
            _rows.Remove(r);
            RefreshEmpty();
            ChatWindow.Current?.ReloadTts();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _tts.Dispose();
            base.OnClosed(e);
        }

        public sealed class Row
        {
            public string Name { get; set; } = string.Empty;
            public string Voice { get; set; } = string.Empty;
            public string Effect { get; set; } = "normal";
            public string Detail => $"{Voice.Replace("Microsoft ", "")} · {Effect}";
            public static Row From(CustomVoice c) => new() { Name = c.Name, Voice = c.Voice, Effect = c.Effect };
        }
    }
}
