using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameTracker.Views
{
    /// <summary>
    /// Themed "What's New" dialog: shows the installed → available versions and the
    /// combined release notes for every version newer than the one the user is on.
    /// DialogResult == true means the user chose to update now.
    /// </summary>
    public partial class UpdateAvailableWindow : Window
    {
        public UpdateAvailableWindow(string currentVersion, string latestTag,
                                     IReadOnlyList<(string tag, string body)> entries)
        {
            InitializeComponent();
            FromVer.Text = "Installed  v" + currentVersion;
            ToVer.Text = "New  " + latestTag;
            BuildNotes(entries);
        }

        private void BuildNotes(IReadOnlyList<(string tag, string body)> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                Notes.Children.Add(Para("Bug fixes and improvements. See the full notes on GitHub.", "#c4d4a8"));
                return;
            }

            bool first = true;
            foreach (var (tag, body) in entries)
            {
                Notes.Children.Add(new TextBlock
                {
                    Text = tag,
                    Foreground = (Brush)FindResource("ThemeAccent"),
                    FontSize = 14, FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, first ? 4 : 16, 0, 5),
                });
                first = false;

                var lines = CleanLines(body);
                if (lines.Count == 0)
                    Notes.Children.Add(Para("Improvements and fixes.", "#c4d4a8"));
                else
                    foreach (var line in lines) Notes.Children.Add(BulletOrPara(line));
            }
        }

        // Split release-notes markdown into tidy display lines: drop blanks and headings,
        // strip list markers/emphasis so it reads cleanly in the app's own style.
        private static List<string> CleanLines(string body)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(body)) return result;
            foreach (var raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) line = line.TrimStart('#', ' ');       // heading → plain
                line = Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");               // **bold** → bold text
                line = Regex.Replace(line, @"`(.+?)`", "$1");                     // `code` → code text
                if (line.Length == 0) continue;
                result.Add(line);
            }
            return result;
        }

        private UIElement BulletOrPara(string line)
        {
            bool bullet = line.StartsWith("-") || line.StartsWith("*") || line.StartsWith("•");
            if (!bullet) return Para(line, "#e8e0c4");

            var text = line.TrimStart('-', '*', '•', ' ');
            var grid = new Grid { Margin = new Thickness(2, 1, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new TextBlock
            {
                Text = "•",
                Foreground = (Brush)FindResource("ThemeAccent"),
                FontSize = 13, FontWeight = FontWeights.Bold,
            };
            var body = new TextBlock
            {
                Text = text, Foreground = Brush("#e8e0c4"),
                FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 18,
            };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(body, 1);
            grid.Children.Add(dot);
            grid.Children.Add(body);
            return grid;
        }

        private TextBlock Para(string text, string hex) => new()
        {
            Text = text, Foreground = Brush(hex),
            FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 18,
            Margin = new Thickness(0, 1, 0, 3),
        };

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private void Update_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
        private void Later_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }
}
