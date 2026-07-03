using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GameTracker.Models;
using GameTracker.Services;
using GameTracker.Services.Chat;

namespace GameTracker.Views
{
    /// <summary>Live "who's in the room" list: green = active, yellow = lurking, plus points.</summary>
    public partial class ChattersWindow : Window
    {
        private readonly ObservableCollection<Row> _rows = new();
        private bool _pinned;

        private static readonly Brush Green = Make("#35d13a");
        private static readonly Brush Yellow = Make("#e0c341");

        public ChattersWindow()
        {
            InitializeComponent();
            List.ItemsSource = _rows;
        }

        private static SolidColorBrush Make(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public void Refresh(List<Chatter> chatters, ChatFeatureSettings features)
        {
            _rows.Clear();
            foreach (var c in chatters)
            {
                Brush symbolBrush = Brushes.Gray;
                try { symbolBrush = Make(OverlayService.ChatColorHex(c.Platform)); } catch { }
                _rows.Add(new Row
                {
                    User = c.User,
                    Symbol = OverlayService.ChatSymbol(c.Platform),
                    SymbolBrush = symbolBrush,
                    DotBrush = c.State == ChatterState.Active ? Green : Yellow,
                    Points = features.PointsEnabled
                        ? PointsService.Get(c.Platform, c.User).ToString()
                        : string.Empty,
                });
            }
            TitleText.Text = $"CHATTERS ({chatters.Count})";
            EmptyText.Visibility = chatters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
            PinButton.Foreground = _pinned
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0xa4, 0x37))
                : new SolidColorBrush(Color.FromRgb(0x7a, 0x90, 0x70));
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private sealed class Row
        {
            public string User { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public Brush SymbolBrush { get; set; } = Brushes.Gray;
            public Brush DotBrush { get; set; } = Brushes.Gray;
            public string Points { get; set; } = string.Empty;
        }
    }
}
