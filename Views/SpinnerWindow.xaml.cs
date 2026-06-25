using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>
    /// Slot-machine style picker that lands on a random game from the Dormant column.
    /// </summary>
    public partial class SpinnerWindow : Window
    {
        private readonly List<Game> _candidates;
        private readonly Action<Game> _onStart;
        private readonly DispatcherTimer _timer;
        private readonly Random _rng = new();

        private Game? _winner;
        private int _ticksLeft;

        public SpinnerWindow(IEnumerable<Game> dormantGames, Action<Game> onStart)
        {
            InitializeComponent();

            _candidates = dormantGames.ToList();
            _onStart = onStart;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _timer.Tick += Timer_Tick;

            if (_candidates.Count == 0)
            {
                ResultText.Text = "No dormant games to pick from";
                SubText.Text = "Add some games (or move a few to Dormant) and try again.";
                SpinButton.IsEnabled = false;
            }
            else
            {
                SubText.Text = $"{_candidates.Count} games waiting in Dormant";
            }
        }

        private void Spin_Click(object sender, RoutedEventArgs e)
        {
            if (_candidates.Count == 0) return;

            _winner = _candidates[_rng.Next(_candidates.Count)];
            _ticksLeft = 28 + _rng.Next(8);
            SpinButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            SubText.Text = "Spinning…";
            _timer.Interval = TimeSpan.FromMilliseconds(55);
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _ticksLeft--;

            if (_ticksLeft <= 0)
            {
                _timer.Stop();
                ResultText.Text = _winner!.Title;
                SubText.Text = string.IsNullOrWhiteSpace(_winner.Platform)
                    ? "Your next game!"
                    : $"Your next game! ({_winner.Platform})";
                SpinButton.IsEnabled = true;
                SpinButton.Content = "🎲 Spin again";
                StartButton.IsEnabled = true;
                return;
            }

            // Flash random titles, slowing down toward the end.
            ResultText.Text = _candidates[_rng.Next(_candidates.Count)].Title;
            if (_ticksLeft < 8)
                _timer.Interval = TimeSpan.FromMilliseconds(55 + (8 - _ticksLeft) * 45);
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_winner == null) return;
            _onStart(_winner);
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
