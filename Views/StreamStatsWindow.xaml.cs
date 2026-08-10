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
            Head("ACTIVITY");
            Line($"🎁 Redeems: {s.Redeems}   📥 Requests: {s.Requests}");
            if (s.Morphs > 0 || s.Videos > 0)
                Line($"🎙 Voice morphs: {s.Morphs}   🎬 Videos: {s.Videos}");
            Line($"🪙 Points handed out: {s.PointsGiven}");
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
