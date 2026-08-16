using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Configure tiered gift alerts (sound + effect by coin value). Persisted on Save.</summary>
    public partial class GiftAlertsWindow : Window
    {
        private readonly ObservableCollection<TierItem> _items = new();
        private readonly SoundService _tester = new();
        private readonly ChatFeatureSettings _features;

        public GiftAlertsWindow()
        {
            InitializeComponent();
            _features = SettingsService.LoadChatFeatures();
            EnabledChk.IsChecked = _features.GiftAlertsEnabled;
            FeedChk.IsChecked = _features.EventFeedEnabled;
            foreach (var t in _features.CoinTiers.OrderBy(t => t.MinCoins))
                _items.Add(new TierItem
                {
                    Name = t.Name, MinCoins = t.MinCoins, SoundPath = t.SoundPath,
                    Volume = t.Volume, Effect = t.Effect ?? string.Empty,
                });
            TierList.ItemsSource = _items;
        }

        private void Add_Click(object sender, RoutedEventArgs e) =>
            _items.Add(new TierItem { Name = "New tier", MinCoins = 1, Volume = 1.0 });

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TierItem item)
                _items.Remove(item);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not TierItem item) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose a sound file",
                Filter =
                    "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.alac;*.aif;*.aiff;*.mp2;*.mpa;*.adts;*.ac3;*.amr;*.3gp;*.opus;*.ogg" +
                    "|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true)
                item.SoundPath = dlg.FileName;
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not TierItem item) return;
            if (string.IsNullOrWhiteSpace(item.SoundPath))
            {
                TestStatus.Text = "Pick a sound file first (Browse).";
                return;
            }
            TestStatus.Text = "▶ Playing " + System.IO.Path.GetFileName(item.SoundPath);
            _tester.Play(item.SoundPath, item.Volume, msg => Dispatcher.Invoke(() => TestStatus.Text = msg));
        }

        // ── Test buttons: fire the real alert path with the tiers currently in the editor ──
        private void TestGift_Click(object sender, RoutedEventArgs e)
        {
            int coins = int.TryParse((TestCoins.Text ?? "").Trim(), out var c) ? c : 1;
            if (coins < 1) coins = 1;
            var tier = _items.Where(i => i.MinCoins <= coins).OrderByDescending(i => i.MinCoins).FirstOrDefault();

            if (EnabledChk.IsChecked == true && tier != null && !string.IsNullOrWhiteSpace(tier.SoundPath))
                _tester.Play(tier.SoundPath, tier.Volume, msg => Dispatcher.Invoke(() => TestStatus.Text = msg));
            if (tier != null && !string.IsNullOrWhiteSpace(tier.Effect))
                OverlayServer.TriggerEffect(tier.Effect);

            var text = $"TestGifter sent a Rose ({coins} 🪙)";
            OverlayServer.PushActivity("gift", "TestGifter", text, "tiktok", coins);
            OverlayServer.Toast(text, coins >= 100);
            SetTestStatus($"gift {coins} → tier \"{tier?.Name ?? "none"}\"");
        }

        private void TestFollow_Click(object sender, RoutedEventArgs e)
        {
            OverlayServer.PushActivity("follow", "NewFan", "NewFan followed 💚", "tiktok");
            SetTestStatus("follow");
        }

        private void TestSub_Click(object sender, RoutedEventArgs e)
        {
            OverlayServer.PushActivity("sub", "NewSub", "NewSub subscribed ⭐", "tiktok");
            OverlayServer.Toast("NewSub subscribed ⭐");
            SetTestStatus("sub");
        }

        private void SetTestStatus(string what)
        {
            TestStatus.Text = OverlayServer.ActiveClients > 0
                ? $"Sent test {what} → check your overlay!"
                : $"Sent test {what}. Open the overlay in OBS/browser to see the feed.";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _features.GiftAlertsEnabled = EnabledChk.IsChecked == true;
            _features.EventFeedEnabled = FeedChk.IsChecked == true;
            _features.CoinTiers = _items
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new CoinAlertTier
                {
                    Name = i.Name.Trim(),
                    MinCoins = i.MinCoins < 1 ? 1 : i.MinCoins,
                    SoundPath = (i.SoundPath ?? string.Empty).Trim(),
                    Volume = i.Volume,
                    Effect = (i.Effect ?? string.Empty).Trim(),
                })
                .OrderBy(t => t.MinCoins)
                .ToList();
            SettingsService.SaveChatFeatures(_features);
            ChatWindow.Current?.ReloadFeatures();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        public class TierItem : INotifyPropertyChanged
        {
            private string _name = string.Empty;
            private int _minCoins = 1;
            private string _soundPath = string.Empty;
            private double _volume = 1.0;
            private string _effect = string.Empty;

            public string Name { get => _name; set { _name = value; OnChanged(nameof(Name)); } }
            public int MinCoins { get => _minCoins; set { _minCoins = value; OnChanged(nameof(MinCoins)); } }
            public string SoundPath { get => _soundPath; set { _soundPath = value; OnChanged(nameof(SoundPath)); } }
            public double Volume { get => _volume; set { _volume = value; OnChanged(nameof(Volume)); } }
            public string Effect { get => _effect; set { _effect = value; OnChanged(nameof(Effect)); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
