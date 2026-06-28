using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GameTracker.Views
{
    /// <summary>
    /// A spinning prize-wheel. Powers both the backlog picker (game titles, with a
    /// "Start playing" action) and a per-game custom wheel (editable items).
    /// </summary>
    public partial class WheelWindow : Window
    {
        private const double Cx = 180, Cy = 180, R = 170;

        private readonly List<string> _items;          // faces currently on the wheel (≤ MaxWheel)
        private readonly List<string> _pool = new();   // full master list (custom-wheel rotation)
        private readonly List<string> _queue = new();  // unused items waiting to rotate in
        private const int MaxWheel = 20;
        private bool _rotation;                          // custom challenge wheel: cap + cycle on land
        private readonly List<string> _results;
        private readonly bool _editable;
        private readonly bool _recordResults;
        private readonly Action<List<string>>? _onItemsChanged;
        private readonly Action<List<string>>? _onResultsChanged;
        private readonly Action<string>? _onRolled;
        private readonly Action<int>? _onChosen;
        private readonly Random _rng = new();

        // Backlog game-picker mode.
        private readonly bool _gameMode;
        private readonly List<WheelGame> _allGames = new();
        private readonly List<WheelGame> _selected = new();
        private readonly Action<Guid>? _onStartGame;
        private WheelGame? _winnerGame;
        private Point _dragStart;

        private bool _pinned;
        private bool _spinning;
        private double _currentAngle;
        private int _winnerIndex = -1;

        private static readonly Brush[] SliceBrushes =
        {
            Brush2("#2e4a30"), Brush2("#1a2e1c"), Brush2("#3a5a2a"), Brush2("#14241a"),
        };
        private static readonly Brush SliceRim = Brush2("#0a1410");
        private static readonly Brush LabelBrush = Brush2("#e8e0c4");

        public WheelWindow(string header, IEnumerable<string> items, bool editable,
                           Action<List<string>>? onItemsChanged, Action<int>? onChosen,
                           string? chooseButtonText,
                           IEnumerable<string>? initialResults = null,
                           Action<List<string>>? onResultsChanged = null,
                           Action<string>? onRolled = null,
                           List<WheelGame>? games = null,
                           Action<Guid>? onStartGame = null)
        {
            InitializeComponent();

            _items = items.ToList();
            _results = initialResults?.ToList() ?? new List<string>();
            _editable = editable;
            _onItemsChanged = onItemsChanged;
            _onResultsChanged = onResultsChanged;
            _onRolled = onRolled;
            _recordResults = onResultsChanged != null;
            _onChosen = onChosen;

            TitleBarText.Text = header.ToUpperInvariant();

            if (games != null)
            {
                _gameMode = true;
                _onStartGame = onStartGame;
                _allGames = games;
                _selected.AddRange(games.Where(g => g.IsDefault));
                SyncItemsFromSelected();

                CustomizeButton.Visibility = Visibility.Visible;
                FilterCombo.ItemsSource = new[] { "All" }
                    .Concat(Models.SuggestionTypes.All)
                    .Concat(new[] { "Dormant", "Hunting", "Devoured" })
                    .ToList();
                FilterCombo.SelectedIndex = 0;
            }

            if (editable)
            {
                // The editor edits the full master list (_pool); the wheel shows a rotating
                // window of up to MaxWheel items drawn from it.
                _rotation = true;
                _pool.AddRange(_items);
                _items.Clear();
                RebuildRotation();

                EditorPanel.Visibility = Visibility.Visible;
                RefreshItemsList();
                RefreshResultsList();
            }

            if (!string.IsNullOrEmpty(chooseButtonText))
            {
                ChooseButton.Visibility = Visibility.Visible;
                ChooseButton.Content = chooseButtonText;
            }

            Loaded += (_, _) => BuildWheel();
        }

        private static SolidColorBrush Brush2(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private void BuildWheel()
        {
            WheelCanvas.Children.Clear();

            bool any = _items.Count > 0;
            EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            SpinButton.IsEnabled = any && !_spinning;

            if (!any) return;

            if (_items.Count == 1)
            {
                WheelCanvas.Children.Add(new Ellipse
                {
                    Width = R * 2,
                    Height = R * 2,
                    Fill = SliceBrushes[0],
                    Stroke = SliceRim,
                    StrokeThickness = 1,
                });
                AddLabel(_items[0], 0);
                return;
            }

            double span = 360.0 / _items.Count;
            for (int i = 0; i < _items.Count; i++)
            {
                double start = i * span;
                double end = (i + 1) * span;

                var fig = new PathFigure { StartPoint = new Point(Cx, Cy), IsClosed = true };
                fig.Segments.Add(new LineSegment(PointAt(start), true));
                fig.Segments.Add(new ArcSegment(PointAt(end), new Size(R, R), 0,
                    span > 180, SweepDirection.Clockwise, true));

                WheelCanvas.Children.Add(new Path
                {
                    Data = new PathGeometry(new[] { fig }),
                    Fill = SliceBrushes[i % SliceBrushes.Length],
                    Stroke = SliceRim,
                    StrokeThickness = 1,
                });

                AddLabel(_items[i], start + span / 2);
            }
        }

        private static Point PointAt(double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(Cx + R * Math.Sin(rad), Cy - R * Math.Cos(rad));
        }

        private void AddLabel(string text, double midDeg)
        {
            double fontSize = _items.Count <= 6 ? 15 : _items.Count <= 10 ? 13 : _items.Count <= 16 ? 11 : 9;
            var label = text.Length > 22 ? text.Substring(0, 21) + "…" : text;

            var tb = new TextBlock
            {
                Text = label,
                Foreground = LabelBrush,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = tb.DesiredSize;

            double rad = midDeg * Math.PI / 180.0;
            double rLabel = _items.Count == 1 ? 0 : R * 0.55;
            double lx = Cx + rLabel * Math.Sin(rad);
            double ly = Cy - rLabel * Math.Cos(rad);

            // Run the text radially (along the slice length), flipped to stay upright.
            double rotation = 0;
            if (_items.Count > 1)
            {
                rotation = midDeg - 90;
                double n = ((rotation % 360) + 360) % 360;
                if (n > 90 && n < 270) rotation += 180;
            }

            Canvas.SetLeft(tb, lx - sz.Width / 2);
            Canvas.SetTop(tb, ly - sz.Height / 2);
            tb.RenderTransformOrigin = new Point(0.5, 0.5);
            tb.RenderTransform = new RotateTransform(rotation);
            WheelCanvas.Children.Add(tb);
        }

        private void Spin_Click(object sender, RoutedEventArgs e)
        {
            if (_spinning || _items.Count == 0) return;

            _spinning = true;
            SpinButton.IsEnabled = false;
            ChooseButton.IsEnabled = false;
            ResultText.Text = "Spinning…";

            _winnerIndex = _rng.Next(_items.Count);
            double span = 360.0 / _items.Count;
            double winnerCenter = _winnerIndex * span + span / 2;
            double jitter = (_rng.NextDouble() - 0.5) * span * 0.7; // land off-center, still on the slice

            // Reset to a small base angle so the numbers stay sane.
            WheelRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            double startAngle = ((_currentAngle % 360) + 360) % 360;
            WheelRotate.Angle = startAngle;

            double x = ((360 - winnerCenter) - startAngle + 360) % 360;
            double finalAngle = startAngle + 360 * 6 + x - jitter;

            var anim = new DoubleAnimation(startAngle, finalAngle,
                new Duration(TimeSpan.FromSeconds(4.5)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                _currentAngle = finalAngle;
                _spinning = false;
                OnLanded();
            };
            WheelRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void OnLanded()
        {
            if (_winnerIndex < 0 || _winnerIndex >= _items.Count) return;
            var winner = _items[_winnerIndex];
            ResultText.Text = "🎯 " + winner;
            SpinButton.IsEnabled = true;
            SpinButton.Content = "🎲 Spin again";
            if (ChooseButton.Visibility == Visibility.Visible)
                ChooseButton.IsEnabled = true;

            if (_gameMode)
                _winnerGame = _winnerIndex < _selected.Count ? _selected[_winnerIndex] : null;

            // Record each spin into the running challenges group.
            if (_recordResults)
            {
                _results.Add(winner);
                RefreshResultsList();
                _onResultsChanged?.Invoke(_results);
                _onRolled?.Invoke(winner);
            }

            // Take the landed challenge out of rotation and cycle in an unused one.
            if (_rotation)
            {
                _items.RemoveAt(_winnerIndex);
                if (_queue.Count > 0)
                {
                    _items.Insert(Math.Min(_winnerIndex, _items.Count), _queue[0]);
                    _queue.RemoveAt(0);
                }
                RefreshItemsList();
                BuildWheel();
                _winnerIndex = -1;
            }
        }

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            if (_gameMode)
            {
                if (_winnerGame != null) { _onStartGame?.Invoke(_winnerGame.Id); Close(); }
                return;
            }
            if (_winnerIndex < 0) return;
            _onChosen?.Invoke(_winnerIndex);
            Close();
        }

        // ----- backlog game picker -----

        private void SyncItemsFromSelected()
        {
            _items.Clear();
            _items.AddRange(_selected.Select(g => g.Title));
        }

        private void Customize_Click(object sender, RoutedEventArgs e)
        {
            bool show = CustomizePanel.Visibility != Visibility.Visible;
            CustomizePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            CustomizeButton.Content = show ? "⚙ Hide" : "⚙ Customize Wheel";
            if (show) RefreshGameList();
        }

        private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RefreshGameList();

        private void RefreshGameList()
        {
            if (!_gameMode) return;
            var filter = FilterCombo.SelectedItem as string ?? "All";
            bool isStatus = filter is "Dormant" or "Hunting" or "Devoured";
            var rows = _allGames
                .Where(g => filter == "All"
                            || (isStatus ? g.StatusLabel == filter : g.SuggestionType == filter))
                .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GameRow
                {
                    Game = g,
                    Title = g.Title,
                    Tag = g.SuggestionType,
                    OnWheel = _selected.Any(s => s.Id == g.Id),
                })
                .ToList();
            GameList.ItemsSource = rows;
        }

        private void ToggleGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is WheelGame g)
            {
                if (_selected.Any(s => s.Id == g.Id))
                    _selected.RemoveAll(s => s.Id == g.Id);
                else
                    _selected.Add(g);
                RebuildFromSelected();
            }
        }

        private void GameRow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
        }

        private void GameRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (sender is Button b && b.Tag is WheelGame g)
                DragDrop.DoDragDrop(b, g.Id.ToString(), DragDropEffects.Copy);
        }

        private void WheelArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void WheelArea_Drop(object sender, DragEventArgs e)
        {
            if (!_gameMode) return;
            if (e.Data.GetData(DataFormats.StringFormat) is string idStr &&
                Guid.TryParse(idStr, out var id))
            {
                var g = _allGames.FirstOrDefault(x => x.Id == id);
                if (g != null && _selected.All(s => s.Id != id))
                {
                    _selected.Add(g);
                    RebuildFromSelected();
                }
            }
        }

        private void RebuildFromSelected()
        {
            SyncItemsFromSelected();
            BuildWheel();
            RefreshGameList();
            _winnerGame = null;
            _winnerIndex = -1;
            ResultText.Text = "Press Spin";
            SpinButton.Content = "🎲 Spin";
            ChooseButton.IsEnabled = false;
        }

        public class GameRow
        {
            public WheelGame Game { get; set; } = null!;
            public string Title { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
            public bool OnWheel { get; set; }
            public string Mark => OnWheel ? "✓" : "+";
        }

        // ----- editable items -----

        private void AddItem_Click(object sender, RoutedEventArgs e) => AddItem();

        private void AddItemBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddItem();
        }

        private void AddItem()
        {
            var text = AddItemBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _pool.Add(text);
            AddItemBox.Clear();
            PoolChanged();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string item)
            {
                _pool.Remove(item);
                PoolChanged();
            }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            if (_pool.Count < 2) return;
            var spread = SpreadShuffle(_pool);
            _pool.Clear();
            _pool.AddRange(spread);
            PoolChanged();
        }

        // Build the wheel's active rotation from the master pool, skipping items already
        // rolled (kept out of rotation): the first MaxWheel land on the wheel, the rest queue.
        private void RebuildRotation()
        {
            var available = new List<string>(_pool);
            foreach (var r in _results) available.Remove(r); // one instance each (handles duplicates)

            _items.Clear();
            _queue.Clear();
            foreach (var it in available)
                (_items.Count < MaxWheel ? _items : _queue).Add(it);
        }

        // Randomize order while keeping related items apart: group by leading word so
        // "No …" restrictions, "Any …" classes, and exact duplicates (e.g. 3× VETO) get
        // spaced evenly around the wheel instead of landing in one big arc.
        private List<string> SpreadShuffle(IEnumerable<string> source)
        {
            var groups = source
                .GroupBy(KeyOf)
                .Select(g => { var list = g.ToList(); ShuffleInPlace(list); return list; })
                .ToList();

            int total = groups.Sum(g => g.Count);
            var result = new List<string>(total);
            string? lastKey = null;

            while (result.Count < total)
            {
                // Take from the largest remaining group whose key differs from the last
                // placed item (the classic max-spacing greedy), random tie-breaks.
                var ordered = groups.Where(g => g.Count > 0)
                                    .OrderByDescending(g => g.Count)
                                    .ThenBy(_ => _rng.Next())
                                    .ToList();
                var pick = ordered.FirstOrDefault(g => KeyOf(g[0]) != lastKey) ?? ordered[0];
                result.Add(pick[0]);
                lastKey = KeyOf(pick[0]);
                pick.RemoveAt(0);
            }
            return result;
        }

        private static string KeyOf(string item)
        {
            var s = (item ?? string.Empty).Trim().ToLowerInvariant();
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        private void ShuffleInPlace(IList<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void PoolChanged()
        {
            RebuildRotation();
            RefreshItemsList();
            BuildWheel();
            _winnerIndex = -1;
            _onItemsChanged?.Invoke(_pool);
        }

        private void RefreshItemsList()
        {
            var list = _rotation ? _pool : _items;
            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = list.ToList();
            if (_rotation)
                ItemsHeader.Text = $"WHEEL ITEMS  ({_items.Count}/{_pool.Count})";
        }

        private void RefreshResultsList()
        {
            var rows = _results
                .Select((r, i) => new ResultRow { Num = $"{i + 1}.", Text = r, Index = i })
                .ToList();
            ResultsList.ItemsSource = rows;
            NoResultsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearResults_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0) return;
            _results.Clear();
            RefreshResultsList();
            _onResultsChanged?.Invoke(_results);
            if (_rotation) { RebuildRotation(); RefreshItemsList(); BuildWheel(); _winnerIndex = -1; }
        }

        private void RemoveResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int idx && idx >= 0 && idx < _results.Count)
            {
                _results.RemoveAt(idx);
                RefreshResultsList();
                _onResultsChanged?.Invoke(_results);
                if (_rotation) { RebuildRotation(); RefreshItemsList(); BuildWheel(); }
            }
        }

        private sealed class ResultRow
        {
            public string Num { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public int Index { get; set; }
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            PinButton.Foreground = _pinned
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0xa4, 0x37))
                : new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
            PinButton.ToolTip = _pinned ? "Unpin" : "Pin on top";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    /// <summary>A game offered to the backlog picker wheel.</summary>
    public class WheelGame
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SuggestionType { get; set; } = "Suggested";
        public string StatusLabel { get; set; } = string.Empty; // Dormant / Hunting / Devoured
        public bool IsDefault { get; set; } // on the wheel by default (Dormant games)
    }
}
