using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>
    /// Floating "Now Playing" window for the active session: live timer,
    /// pin-on-top toggle, and a notes box that saves into the running session.
    /// </summary>
    public partial class SessionWindow : Window
    {
        private readonly Game _game;
        private readonly PlaySession _session;
        private readonly Action _onChanged;
        private readonly DispatcherTimer _timer;

        private bool _pinned;
        private bool _dirty;
        private int _ticks;

        private static readonly SolidColorBrush PinnedBrush =
            new(Color.FromRgb(0xd4, 0xa4, 0x37));
        private static readonly SolidColorBrush UnpinnedBrush =
            new(Color.FromRgb(0x7a, 0x90, 0x70));

        public SessionWindow(Game game, Action onChanged)
        {
            InitializeComponent();

            _game = game;
            _onChanged = onChanged;

            // The caller only opens this for a game with a live session; guard anyway.
            _session = game.ActiveSession ?? new PlaySession { Start = DateTime.Now };
            if (game.ActiveSession == null)
                game.Sessions.Insert(0, _session);

            GameTitleText.Text = game.Title;
            NotesBox.Text = _session.Note;
            UpdateElapsed();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_session.IsActive)
            {
                SessionEndedExternally();
                return;
            }

            UpdateElapsed();

            // Autosave notes roughly every 20s so nothing is lost mid-stream.
            _ticks++;
            if (_dirty && _ticks % 20 == 0)
                Persist();
        }

        private void UpdateElapsed()
        {
            var t = (_session.End ?? DateTime.Now) - _session.Start;
            ElapsedText.Text = $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _session.Note = NotesBox.Text;
            _dirty = true;
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            PinButton.Foreground = _pinned ? PinnedBrush : UnpinnedBrush;
            PinButton.ToolTip = _pinned ? "Unpin" : "Pin on top";
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_session.IsActive)
                _session.End = DateTime.Now;
            _session.Note = NotesBox.Text;
            _timer.Stop();
            Persist();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void SessionEndedExternally()
        {
            // Session was stopped from the main board while this pop-out was open.
            _timer.Stop();
            UpdateElapsed();
            StopButton.IsEnabled = false;
            StopButton.Content = "Session ended";
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            _session.Note = NotesBox.Text;
            if (_dirty)
                Persist();
            base.OnClosed(e);
        }

        private void Persist()
        {
            _dirty = false;
            _onChanged?.Invoke();
        }
    }
}
