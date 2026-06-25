using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private int _shownMarkerCount = -1;

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
            RefreshMarkers();
            LoadGoal();

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
            UpdateGoal();

            // Pick up clips added elsewhere (e.g. the Ctrl+Alt+C hotkey).
            if (_session.Markers.Count != _shownMarkerCount)
                RefreshMarkers();

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

        private void LoadGoal()
        {
            if (_session.GoalMinutes > 0)
            {
                int h = (int)(_session.GoalMinutes / 60);
                int m = (int)(_session.GoalMinutes % 60);
                GoalHoursBox.Text = h > 0 ? h.ToString() : string.Empty;
                GoalMinutesBox.Text = m > 0 ? m.ToString() : string.Empty;
            }
            UpdateGoal();
        }

        private void SetGoal_Click(object sender, RoutedEventArgs e)
        {
            if ((GoalSetButton.Content as string) == "Clear")
            {
                _session.GoalMinutes = 0;
                GoalHoursBox.Clear();
                GoalMinutesBox.Clear();
            }
            else
            {
                int.TryParse(GoalHoursBox.Text, out var h);
                int.TryParse(GoalMinutesBox.Text, out var m);
                _session.GoalMinutes = Math.Max(0, h) * 60 + Math.Max(0, m);
            }

            UpdateGoal();
            Persist();
        }

        private void UpdateGoal()
        {
            if (_session.GoalMinutes <= 0)
            {
                GoalProgressRow.Visibility = Visibility.Collapsed;
                GoalSetButton.Content = "Set";
                return;
            }

            GoalSetButton.Content = "Clear";
            GoalProgressRow.Visibility = Visibility.Visible;

            var elapsed = (_session.End ?? DateTime.Now) - _session.Start;
            var goal = TimeSpan.FromMinutes(_session.GoalMinutes);
            var pct = Math.Min(100, elapsed.TotalSeconds / goal.TotalSeconds * 100);
            GoalBar.Value = pct;

            if (elapsed >= goal)
            {
                GoalBar.Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xa4, 0x37));
                GoalRemainingText.Text = "Goal reached!";
            }
            else
            {
                GoalBar.Foreground = new SolidColorBrush(Color.FromRgb(0x7c, 0xc4, 0x4a));
                var left = goal - elapsed;
                GoalRemainingText.Text = $"{(int)left.TotalHours}h {left.Minutes:00}m left";
            }
        }

        private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _session.Note = NotesBox.Text;
            _dirty = true;
        }

        private void Clip_Click(object sender, RoutedEventArgs e) => AddMarker();

        private void ClipBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddMarker();
        }

        private void AddMarker()
        {
            _session.Markers.Add(new SessionMarker
            {
                At = DateTime.Now,
                Text = ClipBox.Text.Trim()
            });
            ClipBox.Clear();
            RefreshMarkers();
            Persist();
        }

        private void RefreshMarkers()
        {
            var rows = _session.Markers
                .OrderBy(m => m.At)
                .Select(m => new MarkerRow
                {
                    Offset = FormatOffset(m.At - _session.Start),
                    Text = string.IsNullOrWhiteSpace(m.Text) ? "(clip)" : m.Text
                })
                .ToList();

            MarkersItems.ItemsSource = rows;
            NoMarkersText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _shownMarkerCount = _session.Markers.Count;
        }

        private static string FormatOffset(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        private sealed class MarkerRow
        {
            public string Offset { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
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
