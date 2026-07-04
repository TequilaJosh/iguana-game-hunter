using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameTracker.Views
{
    /// <summary>
    /// Themed color picker: preview + hex, RGB sliders, and a palette of app/theme colors.
    /// Use <see cref="Pick"/> to show it; returns the chosen "#RRGGBB" or null on cancel.
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        private bool _sync;   // guards slider<->hex feedback loops
        private string _hex = "#e8e0c4";

        private static readonly string[] Swatches =
        {
            // App theme
            "#7cc44a", "#4a7c3a", "#a8c488", "#e8e0c4", "#d4a437", "#2e4a30", "#0a1410",
            // Platform / accent
            "#9146FF", "#1f6feb", "#25F4EE", "#53FC18", "#FF69B4", "#c44a7c", "#4aa3c4",
            // Standards
            "#FF0000", "#FF6B4A", "#FFA500", "#FFD700", "#FFFF00", "#00FF7F", "#00FFFF",
            "#1E90FF", "#4B0082", "#FF00FF", "#DC143C", "#8B4513", "#20B2AA", "#F5DEB3",
            "#FFFFFF", "#C0C0C0", "#808080", "#000000",
        };

        public string? SelectedHex { get; private set; }

        public ColorPickerWindow(string? initialHex)
        {
            InitializeComponent();
            Palette.ItemsSource = Swatches;
            SetHex(IsHex(initialHex) ? initialHex! : "#e8e0c4");
        }

        /// <summary>Show the picker; returns "#RRGGBB" or null if cancelled.</summary>
        public static string? Pick(Window owner, string? currentHex)
        {
            var win = new ColorPickerWindow(currentHex) { Owner = owner };
            return win.ShowDialog() == true ? win.SelectedHex : null;
        }

        private static bool IsHex(string? s) =>
            !string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#' &&
            ulong.TryParse(s.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);

        private void SetHex(string hex)
        {
            _hex = hex;
            _sync = true;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                HexBox.Text = hex;
                RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                RVal.Text = c.R.ToString(); GVal.Text = c.G.ToString(); BVal.Text = c.B.ToString();
                Preview.Background = new SolidColorBrush(c);
            }
            catch { }
            finally { _sync = false; }
        }

        private void Rgb_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_sync || RVal == null) return;
            var hex = $"#{(byte)RSlider.Value:x2}{(byte)GSlider.Value:x2}{(byte)BSlider.Value:x2}";
            SetHex(hex);
        }

        private void Hex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_sync) return;
            var t = HexBox.Text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (IsHex(t))
            {
                var caret = HexBox.CaretIndex;
                SetHex(t);
                _sync = true;
                HexBox.Text = t;                 // keep exactly what they typed (normalized)
                HexBox.CaretIndex = Math.Min(caret, t.Length);
                _sync = false;
            }
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string hex) SetHex(hex);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedHex = _hex;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
