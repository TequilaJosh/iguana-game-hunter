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

        private readonly List<string> _items;
        private readonly List<string> _results;
        private readonly bool _editable;
        private readonly bool _recordResults;
        private readonly Action<List<string>>? _onItemsChanged;
        private readonly Action<List<string>>? _onResultsChanged;
        private readonly Action<string>? _onRolled;
        private readonly Action<int>? _onChosen;
        private readonly Random _rng = new();

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
                           Action<string>? onRolled = null)
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

            if (editable)
            {
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

            // Record each spin into the running challenges group.
            if (_recordResults)
            {
                _results.Add(winner);
                RefreshResultsList();
                _onResultsChanged?.Invoke(_results);
                _onRolled?.Invoke(winner);
            }
        }

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            if (_winnerIndex < 0) return;
            _onChosen?.Invoke(_winnerIndex);
            Close();
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
            _items.Add(text);
            AddItemBox.Clear();
            ItemsChanged();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string item)
            {
                _items.Remove(item);
                ItemsChanged();
            }
        }

        private void ItemsChanged()
        {
            RefreshItemsList();
            BuildWheel();
            _winnerIndex = -1;
            _onItemsChanged?.Invoke(_items);
        }

        private void RefreshItemsList()
        {
            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = _items.ToList();
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
        }

        private void RemoveResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int idx && idx >= 0 && idx < _results.Count)
            {
                _results.RemoveAt(idx);
                RefreshResultsList();
                _onResultsChanged?.Invoke(_results);
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
}
