using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Live stats for this stream, with a Discord-ready copyable recap.</summary>
    public partial class StreamStatsWindow : Window
    {
        public StreamStatsWindow()
        {
            InitializeComponent();
            Render();
        }

        private void Render()
        {
            StatsContent.Children.Clear();
            var s = StreamStatsService.Get();

            void Head(string t) => StatsContent.Children.Add(new TextBlock
            {
                Text = t, Foreground = (Brush)FindResource("ThemeAccent"),
                FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 3),
            });
            void Line(string t, string? accent = null) => StatsContent.Children.Add(new TextBlock
            {
                Text = t, Foreground = Brush2(accent ?? "#e8e0c4"), FontSize = 13,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1),
            });

            Line($"⏱ Uptime: {(int)s.Uptime.TotalHours}h {s.Uptime.Minutes}m", "#c4d4a8");
            Head("CHAT");
            Line($"{s.TotalMessages} messages · {s.UniqueChatters} unique chatters");
            foreach (var (platform, count) in s.ByPlatform)
                Line($"  {OverlayService.ChatSymbol(platform)} {platform}: {count}", "#a8c488");
            if (s.TopChatters.Count > 0)
            {
                Head("TOP CHATTERS");
                int rank = 1;
                foreach (var (user, count) in s.TopChatters)
                    Line($"{rank++}. {user} — {count}");
            }
            if (s.Games.Count > 0)
            {
                Head("GAMES PLAYED");
                foreach (var g in s.Games) Line("🎮 " + g);
            }
            if (s.GiftCount > 0 || s.Follows > 0 || s.Subs > 0)
            {
                Head("COMMUNITY");
                if (s.GiftCount > 0) Line($"🎁 Gifts: {s.GiftCount}" + (s.GiftCoins > 0 ? $" · {s.GiftCoins} coins" : ""), "#f0c86a");
                if (s.Follows > 0) Line($"➕ New followers: {s.Follows}", "#a8c488");
                if (s.Subs > 0) Line($"⭐ New subs/members: {s.Subs}", "#c4a8e8");
                if (s.TopGifters.Count > 0 && s.GiftCoins > 0)
                {
                    int rank = 1;
                    foreach (var (user, coins) in s.TopGifters)
                        Line($"  💝 {rank++}. {user} — {coins}", "#d8b060");
                }
            }

            Head("ACTIVITY");
            Line($"✨ Redeems: {s.Redeems}   📥 Requests: {s.Requests}");
            if (s.Morphs > 0 || s.Videos > 0)
                Line($"🎙 Voice morphs: {s.Morphs}   🎬 Videos: {s.Videos}");
            Line($"🪙 Points handed out: {s.PointsGiven}");
        }

        private async void Share_Click(object sender, RoutedEventArgs e)
        {
            var recap = StreamStatsService.AsText();
            var f = SettingsService.LoadChatFeatures();
            ShareBtn.IsEnabled = false;
            ShareStatus.Text = "Posting to Discord…";
            try
            {
                bool ok = false;
                // Prefer the companion bot (posts a nice embed in the configured channel).
                if (!string.IsNullOrWhiteSpace(f.BotIngestUrl) && !string.IsNullOrWhiteSpace(f.BotIngestToken))
                    ok = await DiscordService.PostRecapToBotAsync(f.BotIngestUrl, f.BotIngestToken, recap);
                // Fall back to a plain webhook if configured.
                if (!ok && DiscordService.LooksLikeWebhook(f.DiscordWebhook))
                    ok = await DiscordService.PostAsync(f.DiscordWebhook, recap);

                ShareStatus.Text = ok
                    ? "✅ Recap posted to Discord."
                    : "Couldn't post — set the bot ingest (Settings → Discord) or a webhook first.";
            }
            catch { ShareStatus.Text = "Couldn't post the recap."; }
            finally { ShareBtn.IsEnabled = true; }
        }

        private static SolidColorBrush Brush2(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.White); }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Render();

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(StreamStatsService.AsText()); } catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
