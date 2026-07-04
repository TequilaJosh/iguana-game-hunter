using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Streamer settings for chatter counts, the chatters list, points, chat style and point redeems.</summary>
    public partial class ChatFeaturesWindow : Window
    {
        private readonly ObservableCollection<ColorItem> _colors = new();
        private readonly ObservableCollection<RedeemItem> _redeems = new();
        private readonly SoundService _tester = new();

        public ChatFeaturesWindow()
        {
            InitializeComponent();
            var f = SettingsService.LoadChatFeatures();

            ShowCountCb.IsChecked = f.ShowCount;
            PerSourceCb.IsChecked = f.CountPerSource;
            CountOverlayCb.IsChecked = f.CountOnOverlay;
            ChattersOverlayCb.IsChecked = f.ShowChattersOnOverlay;
            ReplyCb.IsChecked = f.ReplyInChat;
            LurkBox.Text = f.LurkMinutes.ToString();
            RemoveBox.Text = f.RemoveMinutes.ToString();
            PointsCb.IsChecked = f.PointsEnabled;
            PointsNameBox.Text = f.PointsName;
            PointsIntervalBox.Text = f.PointsIntervalMinutes.ToString();
            PointsAmountBox.Text = f.PointsPerInterval.ToString();
            StyleLogRb.IsChecked = f.ChatStyle != "boxes";
            StyleBoxRb.IsChecked = f.ChatStyle == "boxes";

            foreach (var c in f.BoxColors) _colors.Add(new ColorItem { Hex = c });
            ColorList.ItemsSource = _colors;

            foreach (var r in f.Redeems)
                _redeems.Add(new RedeemItem
                {
                    Command = r.Command, Effect = r.Effect, Cost = r.Cost,
                    SoundPath = r.SoundPath, ImagePath = r.ImagePath, Volume = r.Volume,
                });
            RedeemList.ItemsSource = _redeems;
        }

        // ---- colors ----

        private void AddColor_Click(object sender, RoutedEventArgs e)
        {
            if (_colors.Count >= 10) { StatusText.Text = "Max 10 colors."; return; }
            _colors.Add(new ColorItem { Hex = "#7cc44a" });
        }

        private void RemoveColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ColorItem c) _colors.Remove(c);
        }

        private void PickBoxColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ColorItem c)
            {
                var hex = ColorPickerWindow.Pick(this, c.Hex);
                if (hex != null) c.Hex = hex;
            }
        }

        // ---- redeems ----

        private void AddRedeem_Click(object sender, RoutedEventArgs e) =>
            _redeems.Add(new RedeemItem { Command = "!", Effect = "confetti", Cost = 100 });

        private void RemoveRedeem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is RedeemItem r) _redeems.Remove(r);
        }

        private void PickSound_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose a sound file",
                Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.ogg;*.aif;*.aiff|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true) r.SoundPath = dlg.FileName;
        }

        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose an image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true) r.ImagePath = dlg.FileName;
        }

        private void TestRedeem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RedeemItem r) return;

            // Persist the current redeem list quietly so /fx/<index> can serve the image.
            var saved = SettingsService.LoadChatFeatures();
            saved.Redeems = BuildRedeems();
            SettingsService.SaveChatFeatures(saved);

            if (!string.IsNullOrWhiteSpace(r.SoundPath))
                _tester.Play(r.SoundPath, r.Volume, msg => Dispatcher.Invoke(() => StatusText.Text = msg));

            if (OverlayServer.IsRunning)
            {
                // Index into the just-saved (filtered) list so /fx/<i> resolves correctly.
                int idx = saved.Redeems.FindIndex(x =>
                    x.Command == r.Command.Trim() && x.ImagePath == r.ImagePath);
                OverlayServer.TriggerEffect(r.Effect,
                    r.Effect == "custom" && idx >= 0 && !string.IsNullOrWhiteSpace(r.ImagePath)
                        ? "/fx/" + idx : null);
                StatusText.Text = "Fired " + r.Effect + " on the overlay.";
            }
            else StatusText.Text = "Overlay server isn't running — sound only.";
        }

        // ---- save ----

        private System.Collections.Generic.List<EffectRedeem> BuildRedeems() =>
            _redeems.Where(r => !string.IsNullOrWhiteSpace(r.Command))
                    .Select(r => new EffectRedeem
                    {
                        Command = r.Command.Trim(),
                        Effect = string.IsNullOrWhiteSpace(r.Effect) ? "confetti" : r.Effect,
                        Cost = Math.Max(0, r.Cost),
                        SoundPath = r.SoundPath, ImagePath = r.ImagePath,
                        Volume = Math.Clamp(r.Volume, 0, 1),
                    }).ToList();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var f = new ChatFeatureSettings
            {
                ShowCount = ShowCountCb.IsChecked == true,
                CountPerSource = PerSourceCb.IsChecked == true,
                CountOnOverlay = CountOverlayCb.IsChecked == true,
                ShowChattersOnOverlay = ChattersOverlayCb.IsChecked == true,
                ReplyInChat = ReplyCb.IsChecked == true,
                LurkMinutes = ParseInt(LurkBox.Text, 5, 1, 720),
                RemoveMinutes = ParseInt(RemoveBox.Text, 15, 1, 1440),
                PointsEnabled = PointsCb.IsChecked == true,
                PointsName = string.IsNullOrWhiteSpace(PointsNameBox.Text) ? "Points" : PointsNameBox.Text.Trim(),
                PointsIntervalMinutes = ParseInt(PointsIntervalBox.Text, 5, 1, 720),
                PointsPerInterval = ParseInt(PointsAmountBox.Text, 10, 1, 1000000),
                ChatStyle = StyleBoxRb.IsChecked == true ? "boxes" : "log",
                BoxColors = _colors.Select(c => c.Hex.Trim())
                                   .Where(IsHex).Take(10).ToList(),
                Redeems = BuildRedeems(),
            };
            if (f.BoxColors.Count == 0) f.BoxColors = new ChatFeatureSettings().BoxColors;

            SettingsService.SaveChatFeatures(f);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private static int ParseInt(string s, int fallback, int min, int max) =>
            int.TryParse((s ?? "").Trim(), out int v) ? Math.Clamp(v, min, max) : fallback;

        private static bool IsHex(string s) =>
            !string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#' &&
            s.Skip(1).All(Uri.IsHexDigit);

        public class ColorItem : INotifyPropertyChanged
        {
            private string _hex = "#7cc44a";
            public string Hex
            {
                get => _hex;
                set { _hex = value; Raise(nameof(Hex)); Raise(nameof(Brush)); }
            }
            public Brush Brush
            {
                get
                {
                    try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_hex)); }
                    catch { return Brushes.Transparent; }
                }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class RedeemItem : INotifyPropertyChanged
        {
            private string _command = string.Empty, _effect = "confetti", _sound = string.Empty, _image = string.Empty;
            private int _cost = 100;
            private double _volume = 1.0;

            public string Command { get => _command; set { _command = value; Raise(nameof(Command)); } }
            public string Effect { get => _effect; set { _effect = value; Raise(nameof(Effect)); } }
            public int Cost { get => _cost; set { _cost = value; Raise(nameof(Cost)); } }
            public double Volume { get => _volume; set { _volume = value; Raise(nameof(Volume)); } }
            public string SoundPath
            {
                get => _sound;
                set { _sound = value; Raise(nameof(SoundPath)); Raise(nameof(SoundName)); }
            }
            public string ImagePath
            {
                get => _image;
                set { _image = value; Raise(nameof(ImagePath)); Raise(nameof(ImageName)); }
            }
            public string SoundName => string.IsNullOrWhiteSpace(_sound) ? "(none)" : Path.GetFileName(_sound);
            public string ImageName => string.IsNullOrWhiteSpace(_image) ? "(none)" : Path.GetFileName(_image);

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
