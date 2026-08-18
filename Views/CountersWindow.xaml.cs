using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        public const string NoGame = "(no game)";

        public static CountersWindow? Current { get; private set; }

        private readonly ObservableCollection<CounterVm> _counters = new();
        private readonly DispatcherTimer _push;
        private bool _loading = true;
        private string _editingGame = "";   // which game's values the +/- and value fields edit

        public ObservableCollection<string> EditingGames { get; } = new();

        public CountersWindow()
        {
            InitializeComponent();
            Current = this;

            _push = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _push.Tick += (_, _) => { _push.Stop(); SaveAndPush(); };

            _editingGame = Main?.CurrentGameTitle() ?? "";
            RefreshEditingGames();
            LoadFrom(SettingsService.LoadCounters());
            CounterList.ItemsSource = _counters;
            _loading = false;

            Activated += (_, _) => { if (!_push.IsEnabled) { _loading = true; _editingGame = Main?.CurrentGameTitle() ?? _editingGame; RefreshEditingGames(); LoadFrom(SettingsService.LoadCounters()); _loading = false; } };
            Closed += (_, _) => { if (Current == this) Current = null; };
        }

        private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

        private void RefreshEditingGames()
        {
            EditingGames.Clear();
            EditingGames.Add(NoGame);
            foreach (var t in Main?.AllGameTitles() ?? Enumerable.Empty<string>()) EditingGames.Add(t);
            EditGameCombo.SelectedItem = string.IsNullOrEmpty(_editingGame) ? NoGame : _editingGame;
        }

        private void LoadFrom(List<GameCounter> list)
        {
            _counters.Clear();
            foreach (var c in list) _counters.Add(CounterVm.From(c, _editingGame, OnChanged));
        }

        private void EditGame_Changed(object sender, SelectionChangedEventArgs e)
        {
            var sel = EditGameCombo.SelectedItem as string ?? NoGame;
            _editingGame = sel == NoGame ? "" : sel;
            foreach (var v in _counters) v.EditingGame = _editingGame;
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

        // Called by a global +/- hotkey (via MainWindow) — always bumps the CURRENT game's value.
        public void BumpByIndex(int index, int delta)
        {
            if (index < 0 || index >= _counters.Count) return;
            _counters[index].BumpGame(Main?.CurrentGameTitle() ?? "", delta);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_counters.Count >= 12) return;
            _counters.Add(CounterVm.From(new GameCounter { Name = "New counter" }, _editingGame, OnChanged));
            OnChanged();
            Main?.RefreshHotkeys();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) { _counters.Remove(v); OnChanged(); Main?.RefreshHotkeys(); }
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

        // Multi-select games: a checkable menu ("All games" + one per game).
        private void Games_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not CounterVm v) return;
            var menu = new ContextMenu();
            var all = new MenuItem { Header = "All games", IsCheckable = true, IsChecked = v.Games.Count == 0, StaysOpenOnClick = true };
            all.Click += (_, _) => { if (all.IsChecked) v.SetGames(new List<string>()); OnChanged(); };
            menu.Items.Add(all);
            menu.Items.Add(new Separator());
            foreach (var g in Main?.AllGameTitles() ?? Enumerable.Empty<string>())
            {
                var gg = g;
                var mi = new MenuItem { Header = g, IsCheckable = true, StaysOpenOnClick = true, IsChecked = v.Games.Any(x => x.Equals(gg, StringComparison.OrdinalIgnoreCase)) };
                mi.Click += (_, _) =>
                {
                    var games = new List<string>(v.Games);
                    if (mi.IsChecked) { if (!games.Any(x => x.Equals(gg, StringComparison.OrdinalIgnoreCase))) games.Add(gg); }
                    else games.RemoveAll(x => x.Equals(gg, StringComparison.OrdinalIgnoreCase));
                    v.SetGames(games);
                    all.IsChecked = games.Count == 0;
                    OnChanged();
                };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = b;
            menu.IsOpen = true;
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
                StartHotkeyCapture($"+1 for “{v.Name}”", hk => { v.IncHotkey = hk; FlushAndRefreshHotkeys(); });
        }

        private void DecHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v)
                StartHotkeyCapture($"−1 for “{v.Name}”", hk => { v.DecHotkey = hk; FlushAndRefreshHotkeys(); });
        }

        private void ClearIncHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) { v.IncHotkey = null; FlushAndRefreshHotkeys(); }
        }

        private void ClearDecHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CounterVm v) { v.DecHotkey = null; FlushAndRefreshHotkeys(); }
        }

        // Save immediately so RegisterHotkeys reads the new binding (fixes the "last-set hotkey
        // didn't register" bug where the debounced save hadn't run yet).
        private void FlushAndRefreshHotkeys()
        {
            _push.Stop();
            SaveAndPush();
            Main?.RefreshHotkeys();
        }

        private void StartHotkeyCapture(string what, Action<HotkeyBinding> apply)
        {
            EndHotkeyCapture(cancel: true);
            Main?.PauseHotkeys();
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
            Main?.RefreshHotkeys();
        }

        private void HotkeyCapture_KeyDown(object sender, KeyEventArgs e)
        {
            if (_hotkeyCapture == null) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                    or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
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
            private string _name = "", _color = "#7cc44a", _editingGame = "";
            private bool _show = true;
            private HotkeyBinding? _inc, _dec;
            private List<string> _games = new();
            private Dictionary<string, int> _values = new();

            public string Name { get => _name; set { _name = value; Bump(nameof(Name)); } }
            public bool Show { get => _show; set { _show = value; Bump(nameof(Show)); } }

            // The value for the game currently selected in the editor.
            public int Value
            {
                get => _values.TryGetValue(_editingGame, out var v) ? v : 0;
                set { _values[_editingGame] = value; Bump(nameof(Value)); }
            }

            public string EditingGame
            {
                get => _editingGame;
                set { _editingGame = value ?? ""; Raise(nameof(Value)); }
            }

            public IReadOnlyList<string> Games => _games;
            public string GamesSummary => _games.Count == 0 ? "All games" : _games.Count == 1 ? _games[0] : $"{_games.Count} games";
            public void SetGames(List<string> games) { _games = games ?? new(); Raise(nameof(GamesSummary)); _changed(); }

            public void BumpGame(string game, int delta)
            {
                game ??= "";
                _values[game] = (_values.TryGetValue(game, out var v) ? v : 0) + delta;
                if (game == _editingGame) Raise(nameof(Value));
                _changed();
            }

            public HotkeyBinding? IncHotkey { get => _inc; set { _inc = value; Raise(nameof(IncHotkey)); Raise(nameof(IncHotkeyDisplay)); _changed(); } }
            public HotkeyBinding? DecHotkey { get => _dec; set { _dec = value; Raise(nameof(DecHotkey)); Raise(nameof(DecHotkeyDisplay)); _changed(); } }
            public string IncHotkeyDisplay => _inc?.Display ?? "set +1 key…";
            public string DecHotkeyDisplay => _dec?.Display ?? "set −1 key…";

            public string Color
            {
                get => _color;
                set { _color = value; Bump(nameof(Color)); Raise(nameof(ColorBrush)); }
            }
            public Brush ColorBrush
            {
                get { try { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(_color)); } catch { return Brushes.Gray; } }
            }

            public static CounterVm From(GameCounter c, string editingGame, Action changed) => new()
            {
                _name = c.Name,
                _color = string.IsNullOrWhiteSpace(c.Color) ? "#7cc44a" : c.Color,
                _show = c.Show,
                _games = new List<string>(c.Games ?? new()),
                _values = new Dictionary<string, int>(c.Values ?? new()),
                _inc = c.IncHotkey, _dec = c.DecHotkey,
                _editingGame = editingGame ?? "",
                _changed = changed,
            };

            public GameCounter ToModel() => new()
            {
                Name = Name, Color = Color, Show = Show,
                Games = new List<string>(_games),
                Values = new Dictionary<string, int>(_values),
                IncHotkey = _inc, DecHotkey = _dec,
            };

            private void Bump(string n) { Raise(n); _changed(); }
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
