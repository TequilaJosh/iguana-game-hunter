using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameTracker.Models;

namespace GameTracker.Views
{
    public partial class AddEditGameWindow : Window
    {
        public Game? ResultGame { get; private set; }
        public bool Deleted { get; private set; } = false;

        private readonly Game _game;
        private readonly bool _isEdit;
        private readonly ObservableCollection<PlaySession> _sessions;

        private static readonly List<string> Platforms = new()
        {
            "PC",
            // Nintendo - home consoles
            "Nintendo Switch 2", "Nintendo Switch", "Wii U", "Wii", "GameCube",
            "Nintendo 64", "Super Nintendo", "Nintendo Entertainment System",
            // Nintendo - handhelds
            "Nintendo 3DS", "Nintendo DS", "Game Boy Advance", "Game Boy Color",
            "Game Boy", "Virtual Boy",
            // PlayStation
            "PlayStation 5", "PlayStation 4", "PlayStation 3", "PlayStation 2",
            "PlayStation", "PlayStation Vita", "PSP",
            // Xbox
            "Xbox Series X|S", "Xbox One", "Xbox 360", "Xbox",
            // Sega
            "Dreamcast", "Sega Saturn", "Sega Genesis", "Sega Master System",
            "Sega CD", "Sega 32X", "Game Gear",
            // Atari
            "Atari Jaguar", "Atari 7800", "Atari 5200", "Atari 2600", "Atari Lynx",
            // Other classic / niche
            "Neo Geo", "TurboGrafx-16", "3DO", "ColecoVision", "Intellivision",
            "Other"
        };

        private static readonly List<string> Genres = new()
        {
            "Action", "Adventure", "RPG", "Strategy", "Simulation", "Sports",
            "Racing", "Puzzle", "Horror", "Platformer", "Shooter", "Fighting",
            "Stealth", "Survival", "Metroidvania", "Visual Novel", "Other"
        };

        public AddEditGameWindow(Game? existing = null)
        {
            InitializeComponent();

            _isEdit = existing != null;
            _game = existing != null
                ? new Game
                {
                    Id = existing.Id,
                    Title = existing.Title,
                    Status = existing.Status,
                    Platform = existing.Platform,
                    Genre = existing.Genre,
                    Requester = existing.Requester,
                    Rating = existing.Rating,
                    Notes = existing.Notes,
                    DateAdded = existing.DateAdded,
                    PlaySessions = new List<DateTime>(existing.PlaySessions)
                }
                : new Game();

            // Working copy of sessions (newest first), cloned so edits only commit on Save.
            var source = existing?.Sessions ?? new List<PlaySession>();
            _sessions = new ObservableCollection<PlaySession>(
                source.OrderByDescending(s => s.Start)
                      .Select(s => new PlaySession { Start = s.Start, End = s.End, Note = s.Note }));

            SetupControls();
        }

        private void SetupControls()
        {
            WindowTitle.Text = _isEdit ? "Edit Game" : "Add Game";
            TitleBarText.Text = _isEdit ? "EDIT GAME" : "ADD GAME";
            Title = _isEdit ? "Edit Game" : "Add Game";

            PlatformCombo.ItemsSource = Platforms;
            GenreCombo.ItemsSource = Genres;

            StatusCombo.ItemsSource = new List<string> { "Not Started", "In Progress", "Beaten" };
            StatusCombo.SelectedIndex = (int)_game.Status;

            TitleBox.Text = _game.Title;
            PlatformCombo.Text = _game.Platform;
            GenreCombo.Text = _game.Genre;
            RequesterBox.Text = _game.Requester;
            RatingSlider.Value = _game.Rating;
            NotesBox.Text = _game.Notes;

            if (_isEdit)
                DeleteButton.Visibility = Visibility.Visible;

            SessionsItems.ItemsSource = _sessions;
            RefreshSessionsUi();
        }

        private void RefreshSessionsUi()
        {
            // Force the rows to re-render computed fields (duration/live) after a mutation.
            SessionsItems.Items.Refresh();

            NoSessionsText.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var total = TimeSpan.Zero;
            foreach (var s in _sessions) total += s.Duration;
            TotalTimeText.Text = PlaySession.FormatSpan(total);

            var active = _sessions.Any(s => s.IsActive);
            StartStopButton.Content = active ? "■ Stop Session" : "▶ Start Session";
            StartStopButton.Background = active
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x5a, 0x1a, 0x14))
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4a, 0x7c, 0x3a));
            StartStopButton.Foreground = active
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xe8, 0x90, 0x80))
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x0a, 0x14, 0x10));
        }

        private void RatingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RatingLabel != null)
            {
                int val = (int)e.NewValue;
                RatingLabel.Text = val == 0 ? "-" : val.ToString();
            }
        }

        private void ToggleSession_Click(object sender, RoutedEventArgs e)
        {
            var active = _sessions.FirstOrDefault(s => s.IsActive);
            if (active != null)
                active.End = DateTime.Now;          // stop the running session
            else
                _sessions.Insert(0, new PlaySession { Start = DateTime.Now });  // start a new one

            RefreshSessionsUi();
        }

        private void AddManual_Click(object sender, RoutedEventArgs e)
        {
            int.TryParse(ManualHoursBox.Text, out var h);
            int.TryParse(ManualMinutesBox.Text, out var m);
            var dur = new TimeSpan(Math.Max(0, h), Math.Max(0, m), 0);

            if (dur <= TimeSpan.Zero)
            {
                MessageBox.Show("Enter an hours and/or minutes amount greater than zero.",
                    "Add Session", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var end = DateTime.Now;
            _sessions.Insert(0, new PlaySession { Start = end - dur, End = end });

            ManualHoursBox.Clear();
            ManualMinutesBox.Clear();
            RefreshSessionsUi();
        }

        private void RemoveSession_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is PlaySession s)
            {
                _sessions.Remove(s);
                RefreshSessionsUi();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                MessageBox.Show("Please enter a game title.", "Missing Title",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TitleBox.Focus();
                return;
            }

            _game.Title = TitleBox.Text.Trim();
            _game.Platform = PlatformCombo.Text.Trim();
            _game.Genre = GenreCombo.Text.Trim();
            _game.Requester = RequesterBox.Text.Trim();
            _game.Status = (GameStatus)StatusCombo.SelectedIndex;
            _game.Rating = (int)RatingSlider.Value;
            _game.Notes = NotesBox.Text.Trim();
            _game.Sessions = _sessions.OrderByDescending(s => s.Start).ToList();

            ResultGame = _game;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete \"{_game.Title}\"?\nThis cannot be undone.",
                "Delete Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Deleted = true;
                DialogResult = true;
                Close();
            }
        }
    }
}
