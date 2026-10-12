using System;
using System.Linq;
using System.Windows;
using GameTracker.Models;
using Choice = GameTracker.Services.ReminderService.Choice;

namespace GameTracker.Views
{
    /// <summary>
    /// The reminder pop-up: bottom-right, always on top, and it never takes focus (so it can't
    /// knock the streamer out of a game). Reports the choice back through <see cref="Chosen"/>.
    /// </summary>
    public partial class ReminderPopup : Window
    {
        public Guid GameId { get; }

        /// <summary>Raised once when the streamer picks a button (closing via Alt+F4 counts as Got it).</summary>
        public event Action<ReminderPopup, Choice>? Chosen;

        private bool _reported;

        public ReminderPopup(Game game, GameReminder reminder, DateTime dueAt)
        {
            InitializeComponent();
            GameId = game.Id;

            GameTitle.Text = game.Title;
            NoteText.Text = reminder.Note;
            NoteText.Visibility = string.IsNullOrWhiteSpace(reminder.Note) ? Visibility.Collapsed : Visibility.Visible;
            WhenText.Text = reminder.RepeatMinutes > 0
                ? $"Scheduled {dueAt:ddd h:mm tt} · reminding you {reminder.RepeatDisplay} until you turn it off"
                : $"Scheduled {dueAt:ddd h:mm tt}";

            Loaded += (_, _) =>
            {
                PlaceBottomRight();
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { /* sound is a nicety */ }
            };
            Closed += (_, _) => Report(Choice.GotIt);
        }

        // Bottom-right of the work area. With several on screen, each new one sits just above the
        // highest one already showing — measured, since pop-ups differ in height (notes wrap).
        private void PlaceBottomRight()
        {
            var area = SystemParameters.WorkArea;
            const double gap = 12;
            double bottom = area.Bottom - gap;
            foreach (var other in Application.Current.Windows.OfType<ReminderPopup>())
                if (!ReferenceEquals(other, this) && other.IsLoaded && other.IsVisible)
                    bottom = Math.Min(bottom, other.Top - gap);
            Left = area.Right - ActualWidth - gap;
            Top = Math.Max(area.Top + gap, bottom - ActualHeight);
        }

        private void Report(Choice c)
        {
            if (_reported) return;
            _reported = true;
            Chosen?.Invoke(this, c);
        }

        private void GotIt_Click(object sender, RoutedEventArgs e) { Report(Choice.GotIt); Close(); }
        private void Snooze_Click(object sender, RoutedEventArgs e) { Report(Choice.Snooze); Close(); }
        private void TurnOff_Click(object sender, RoutedEventArgs e) { Report(Choice.TurnOff); Close(); }
    }
}
