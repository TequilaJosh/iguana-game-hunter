using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>
    /// Summarizes play sessions and clip markers over a time range — handy to paste
    /// into a Discord/social "stream recap" post.
    /// </summary>
    public partial class RecapWindow : Window
    {
        private readonly List<Game> _games;

        public RecapWindow(IEnumerable<Game> games)
        {
            InitializeComponent();
            _games = games.ToList();
            Build("Today", DateTime.Today);
        }

        private void Today_Click(object sender, RoutedEventArgs e) => Build("Today", DateTime.Today);
        private void Week_Click(object sender, RoutedEventArgs e) => Build("Last 7 days", DateTime.Now.AddDays(-7));
        private void All_Click(object sender, RoutedEventArgs e) => Build("All time", DateTime.MinValue);

        private void Build(string label, DateTime from)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🦎 Stream Recap — {label}");
            sb.AppendLine(new string('=', 40));

            // Gather each game's sessions/markers within the range.
            var rows = new List<(Game game, TimeSpan time, int sessions,
                                 List<(TimeSpan offset, DateTime at, string text)> marks)>();
            var totalTime = TimeSpan.Zero;
            int totalSessions = 0, totalClips = 0;

            foreach (var g in _games)
            {
                var inRange = g.Sessions.Where(s => s.Start >= from).ToList();
                if (inRange.Count == 0) continue;

                var time = TimeSpan.Zero;
                foreach (var s in inRange) time += s.Duration;

                var marks = inRange
                    .SelectMany(s => s.Markers
                        .Where(m => m.At >= from)
                        .Select(m => (offset: m.At - s.Start, at: m.At, text: m.TextOrClip)))
                    .OrderBy(m => m.at)
                    .ToList();

                rows.Add((g, time, inRange.Count, marks));
                totalTime += time;
                totalSessions += inRange.Count;
                totalClips += marks.Count;
            }

            if (rows.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("No play sessions in this range yet.");
                RecapText.Text = sb.ToString();
                return;
            }

            sb.AppendLine($"Total: {Fmt(totalTime)} across {totalSessions} session(s), " +
                          $"{rows.Count} game(s), {totalClips} clip(s)");
            sb.AppendLine();

            foreach (var r in rows.OrderByDescending(r => r.time))
            {
                sb.AppendLine($"▶ {r.game.Title} — {Fmt(r.time)}" +
                              (string.IsNullOrWhiteSpace(r.game.Platform) ? "" : $"  [{r.game.Platform}]"));
                if (r.marks.Count == 0)
                {
                    sb.AppendLine("    (no clips)");
                }
                else
                {
                    foreach (var m in r.marks)
                        sb.AppendLine($"    {Offset(m.offset)}  ({m.at:h:mm tt})  {m.text}");
                }
                sb.AppendLine();
            }

            RecapText.Text = sb.ToString().TrimEnd();
        }

        private static string Fmt(TimeSpan t)
        {
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }

        private static string Offset(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RecapText.Text);
                StatusFlash();
            }
            catch { /* clipboard can occasionally be locked by another app */ }
        }

        private void StatusFlash()
        {
            Title = "Copied!";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
