using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Live editor for overlay goal bars. Every change saves + pushes to OBS instantly.</summary>
    public partial class GoalsWindow : Window
    {
        private readonly ObservableCollection<GoalVm> _goals = new();
        private readonly DispatcherTimer _push;
        private bool _loading = true;

        public GoalsWindow()
        {
            InitializeComponent();
            _push = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _push.Tick += (_, _) => { _push.Stop(); SaveAndPush(); };

            foreach (var g in SettingsService.LoadGoals())
                _goals.Add(GoalVm.From(g, OnChanged));
            GoalList.ItemsSource = _goals;
            _loading = false;
        }

        private void OnChanged()
        {
            if (_loading) return;
            _push.Stop();
            _push.Start();
        }

        private void SaveAndPush()
        {
            var list = _goals.Select(v => v.ToModel()).ToList();
            SettingsService.SaveGoals(list);
            OverlayServer.PushGoals(list);
        }

        private void AddGoal_Click(object sender, RoutedEventArgs e)
        {
            if (_goals.Count >= 8) return;
            _goals.Add(GoalVm.From(new StreamGoal { Name = "New goal", Target = 100 }, OnChanged));
            OnChanged();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GoalVm v) { _goals.Remove(v); OnChanged(); }
        }

        private void Plus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GoalVm v) v.Current++;
        }

        private void Minus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GoalVm v && v.Current > 0) v.Current--;
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is GoalVm v)
            {
                var hex = ColorPickerWindow.Pick(this, v.Color);
                if (hex != null) v.Color = hex;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _push.Stop();
            SaveAndPush();
            Close();
        }

        public sealed class GoalVm : INotifyPropertyChanged
        {
            private Action _changed = () => { };
            private string _name = "", _color = "#7cc44a";
            private int _current, _target = 100;
            private bool _show = true;

            public string Name { get => _name; set { _name = value; Bump(nameof(Name)); } }
            public int Current { get => _current; set { _current = Math.Max(0, value); Bump(nameof(Current)); } }
            public int Target { get => _target; set { _target = Math.Max(1, value); Bump(nameof(Target)); } }
            public bool Show { get => _show; set { _show = value; Bump(nameof(Show)); } }
            public string Color
            {
                get => _color;
                set { _color = value; Bump(nameof(Color)); Raise(nameof(ColorBrush)); }
            }
            public Brush ColorBrush
            {
                get
                {
                    try { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(_color)); }
                    catch { return Brushes.Gray; }
                }
            }

            public static GoalVm From(StreamGoal g, Action changed) => new()
            {
                _name = g.Name, _current = g.Current, _target = Math.Max(1, g.Target),
                _color = string.IsNullOrWhiteSpace(g.Color) ? "#7cc44a" : g.Color,
                _show = g.Show, _changed = changed,
            };

            public StreamGoal ToModel() => new()
            { Name = Name, Current = Current, Target = Target, Color = Color, Show = Show };

            private void Bump(string n) { Raise(n); _changed(); }
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
