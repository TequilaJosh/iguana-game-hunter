using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Live editor for per-game on-stream counters. Changes save + push to OBS instantly.</summary>
    public partial class CountersWindow : Window
    {
        public const string AllGames = "All games";

        /// <summary>The open instance, so global hotkeys can update the displayed values live.</summary>
        public static CountersWindow? Current { get; private set; }

        private readonly ObservableCollection<CounterVm> _counters = new();
        private readonly DispatcherTimer _push;
        private bool _loading = true;

        public ObservableCollection<string> Games { get; } = new();

        public CountersWindow()
        {
            InitializeComponent();
            Current = this;

            RefreshGamesList();
            _push = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _push.Tick += (_, _) => { _push.Stop(); SaveAndPush(); };

            LoadFrom(SettingsService.LoadCounters());
            CounterList.ItemsSource = _counters;
            _loading = false;

            // Pick up hotkey/value changes made elsewhere when the window regains focus (no pending edits).
            Activated += (_, _) => { if (!_push.IsEnabled) { _loading = true; LoadFrom(SettingsService.LoadCounters()); _loading = false; } };
            Closed += (_, _) => { if (Current == this) Current = null; };
        }

        private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

        private void RefreshGamesList()
        {
            Games.Clear();
            Games.Add(AllGames);
            foreach (var t in Main?.AllGameTitles() ?? Enumerable.Empty<string>()) Games.Add(t);
        }

        private void LoadFrom(System.Collections.Generic.List<GameCounter> list)
        {
            _counters.Clear();
            foreach (var c in list) _counters.Add(CounterVm.From(c, OnChanged));
        }

        private void OnChanged()
        {
            if (_loading) return;
            _push.Stop();
            _push.Start();
        }

        private void SaveAndPush()
        {
            var list = _counters.Select(v => v.ToModel()).ToList();
            SettingsService.SaveCounters(list);
            OverlayServer.PushCounters(list);
        }

        /// <summary>Called by a global +/- hotkey (via MainWindow) so the open editor stays in sync.</summary>
        public void BumpByIndex(int index, int delta)
        {
            if (index >= 0 && index < _counters.Count) _counters[index].Value += delta;  // VM setter → save + push + UI
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_counters.Count >= 12) return;
            var game = Main?.CurrentGameTitle() ?? string.Empty;   // default to the game you're on
            _counters.Add(CounterVm.From(new GameCounter { Name = "New counter", Game = game }, OnChanged));
            OnChanged();
            Main?.RefreshHotkeys();   // list size changed → hotkey ids shift
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v)
            {
                _counters.Remove(v); OnChanged(); Main?.RefreshHotkeys();
            }
        }

        private void Plus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) v.Value++;
        }

        private void Minus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) v.Value--;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) v.Value = 0;
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v)
            {
                var hex = ColorPickerWindow.Pick(this, v.Color);
                if (hex != null) v.Color = hex;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            EndHotkeyCapture(cancel: true);
            _push.Stop();
            SaveAndPush();
            Close();
        }

        // ── inline +/- hotkey capture ───────────────────────────────────────────
        private Action<HotkeyBinding>? _hotkeyCapture;

        private void IncHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v)
                StartHotkeyCapture($"+1 for “{v.Name}”", hk => { v.IncHotkey = hk; Main?.RefreshHotkeys(); });
        }

        private void DecHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v)
                StartHotkeyCapture($"−1 for “{v.Name}”", hk => { v.DecHotkey = hk; Main?.RefreshHotkeys(); });
        }

        private void ClearIncHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) { v.IncHotkey = null; Main?.RefreshHotkeys(); }
        }

        private void ClearDecHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) { v.DecHotkey = null; Main?.RefreshHotkeys(); }
        }

        private void StartHotkeyCapture(string what, Action<HotkeyBinding> apply)
        {
            EndHotkeyCapture(cancel: true);
            Main?.PauseHotkeys();              // release globals so the combo reaches us
            _hotkeyCapture = apply;
            CaptureStatus.Text = $"Press a key combo for {what}…  (Esc cancels)";
            PreviewKeyDown += HotkeyCapture_KeyDown;
        }

        private void EndHotkeyCapture(bool cancel)
        {
            if (_hotkeyCapture == null) return;
            PreviewKeyDown -= HotkeyCapture_KeyDown;
            _hotkeyCapture = null;
            CaptureStatus.Text = "";
            Main?.RefreshHotkeys();            // re-arm the global hotkeys
        }

        private void HotkeyCapture_KeyDown(object sender, KeyEventArgs e)
        {
            if (_hotkeyCapture == null) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                    or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;   // ignore bare modifiers
            e.Handled = true;
            if (key == Key.Escape) { EndHotkeyCapture(cancel: true); return; }
            var apply = _hotkeyCapture;
            _hotkeyCapture = null;
            PreviewKeyDown -= HotkeyCapture_KeyDown;
            CaptureStatus.Text = "";
            apply(new HotkeyBinding(Keyboard.Modifiers, key));
        }

        public sealed class CounterVm : INotifyPropertyChanged
        {
            private Action _changed = () => { };
            private string _name = "", _color = "#7cc44a", _game = "";
            private int _value;
            private bool _show = true;
            private HotkeyBinding? _inc, _dec;

            public HotkeyBinding? IncHotkey
            {
                get => _inc;
                set { _inc = value; Raise(nameof(IncHotkey)); Raise(nameof(IncHotkeyDisplay)); _changed(); }
            }
            public HotkeyBinding? DecHotkey
            {
                get => _dec;
                set { _dec = value; Raise(nameof(DecHotkey)); Raise(nameof(DecHotkeyDisplay)); _changed(); }
            }
            public string IncHotkeyDisplay => _inc?.Display ?? "set +1 key…";
            public string DecHotkeyDisplay => _dec?.Display ?? "set −1 key…";

            public string Name { get => _name; set { _name = value; Bump(nameof(Name)); } }
            public int Value { get => _value; set { _value = value; Bump(nameof(Value)); } }
            public bool Show { get => _show; set { _show = value; Bump(nameof(Show)); } }

            // "" (all games) <-> the "All games" label used in the combo box.
            public string GameLabel
            {
                get => string.IsNullOrEmpty(_game) ? AllGames : _game;
                set { _game = (value == AllGames || string.IsNullOrEmpty(value)) ? "" : value; Bump(nameof(GameLabel)); }
            }

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

            public static CounterVm From(GameCounter c, Action changed) => new()
            {
                _name = c.Name, _value = c.Value,
                _color = string.IsNullOrWhiteSpace(c.Color) ? "#7cc44a" : c.Color,
                _show = c.Show, _game = c.Game ?? "",
                _inc = c.IncHotkey, _dec = c.DecHotkey,   // set backing fields (don't trigger a save on load)
                _changed = changed,
            };

            public GameCounter ToModel() => new()
            {
                Name = Name, Value = Value, Color = Color, Show = Show, Game = _game,
                IncHotkey = IncHotkey, DecHotkey = DecHotkey,
            };

            private void Bump(string n) { Raise(n); _changed(); }
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
