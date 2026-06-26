using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>Read-only details view for a game: info on the left, sessions on the right.</summary>
    public partial class GameInfoWindow : Window
    {
        private readonly Game _game;

        /// <summary>Set when the user clicks Edit, so the caller can open the edit dialog.</summary>
        public bool EditRequested { get; private set; }

        public GameInfoWindow(Game game)
        {
            InitializeComponent();
            _game = game;
            Populate();
        }

        private void Populate()
        {
            TitleText.Text = _game.Title;
            Title = _game.Title;

            // Status badge
            (string label, string bg, string fg) = _game.Status switch
            {
                GameStatus.NotStarted => ("DORMANT", "#1a2620", "#7a9a8a"),
                GameStatus.InProgress => ("HUNTING", "#1e3a1e", "#7cc44a"),
                GameStatus.Beaten => ("DEVOURED", "#3a2a10", "#d4a437"),
                _ => ("—", "#1c2a1e", "#c4d4a8"),
            };
            StatusText.Text = label;
            StatusText.Foreground = Brush(fg);
            StatusBadge.Background = Brush(bg);
            StatusBadge.BorderBrush = Brush(fg);
            StatusBadge.BorderThickness = new Thickness(1);

            // Chips
            ShowChip(PlatformChip, PlatformText, _game.Platform);
            ShowChip(GenreChip, GenreText, _game.Genre);
            ShowChip(RequesterChip, RequesterText,
                string.IsNullOrWhiteSpace(_game.Requester) ? "" : "@" + _game.Requester);

            // Rating
            RatingText.Text = _game.Rating > 0
                ? new string('★', _game.Rating) + new string('☆', 10 - _game.Rating) + $"   {_game.Rating}/10"
                : "Not rated";

            // Time
            TotalText.Text = _game.HasPlayTime ? _game.TotalPlayTimeDisplay + " played" : "Not played yet";
            LastPlayedText.Text = "Last played: " + _game.LastPlayedDisplay;
            DateAddedText.Text = "Added " + _game.DateAdded.ToString("MMM d, yyyy");

            // Notes
            NotesText.Text = string.IsNullOrWhiteSpace(_game.Notes) ? "No notes." : _game.Notes;

            // Sessions (newest first)
            var sessions = _game.Sessions.OrderByDescending(s => s.Start).ToList();
            SessionsItems.ItemsSource = sessions;
            NoSessionsText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TotalTimeBadge.Text = _game.TotalPlayTimeDisplay;
        }

        private static void ShowChip(UIElement chip, System.Windows.Controls.TextBlock text, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                chip.Visibility = Visibility.Collapsed;
            }
            else
            {
                chip.Visibility = Visibility.Visible;
                text.Text = value;
            }
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            EditRequested = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
