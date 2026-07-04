using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Editor for up to 5 custom OBS text panels (/panel/1..5): a styled header, a divider,
    /// and styled lines with optional marquee scrolling. Saves + pushes live while typing.
    /// </summary>
    public partial class TextPanelsWindow : Window
    {
        private const int MaxPanels = 5;
        private const int MaxLines = 10;

        private readonly ObservableCollection<PanelVm> _panels = new();
        private readonly DispatcherTimer _pushTimer;
        private bool _loading = true;

        /// <summary>System fonts + the bundled bonus font, shared by every combo box.</summary>
        public static List<string> FontChoices { get; } = BuildFonts();

        public TextPanelsWindow()
        {
            InitializeComponent();

            // Debounced save+push so typing feels live without hammering disk/sockets.
            _pushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _pushTimer.Tick += (_, _) => { _pushTimer.Stop(); SaveAndPush(); };

            foreach (var p in SettingsService.LoadTextPanels().Take(MaxPanels))
                _panels.Add(PanelVm.From(p, OnChanged));
            Renumber();
            PanelList.ItemsSource = _panels;
            _loading = false;
        }

        private static List<string> BuildFonts()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var en = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
                foreach (var ff in Fonts.SystemFontFamilies)
                {
                    try
                    {
                        var name = ff.FamilyNames.TryGetValue(en, out var n)
                            ? n : ff.FamilyNames.Values.FirstOrDefault() ?? ff.Source;
                        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                    }
                    catch { }
                }
            }
            catch { }
            var list = new List<string> { "", OverlayServer.BundledFontName };
            list.AddRange(names.Where(n =>
                !string.Equals(n, OverlayServer.BundledFontName, StringComparison.OrdinalIgnoreCase)));
            return list;
        }

        private void OnChanged()
        {
            if (_loading) return;
            _pushTimer.Stop();
            _pushTimer.Start();
        }

        private void SaveAndPush()
        {
            var panels = _panels.Select(p => p.ToModel()).ToList();
            SettingsService.SaveTextPanels(panels);
            OverlayServer.PushTextPanels(panels);
        }

        private void Renumber()
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].SetIndex(i + 1);
            AddPanelBtn.IsEnabled = _panels.Count < MaxPanels;
        }

        private void AddPanel_Click(object sender, RoutedEventArgs e)
        {
            if (_panels.Count >= MaxPanels) return;
            var vm = PanelVm.From(new TextPanel
            {
                HeaderText = "MY HEADER",
                Lines = new List<TextPanelLine> { new() { Text = "your text here" } },
            }, OnChanged);
            _panels.Add(vm);
            Renumber();
            OnChanged();
        }

        private void RemovePanel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PanelVm vm)
            {
                _panels.Remove(vm);
                Renumber();
                OnChanged();
            }
        }

        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PanelVm vm && vm.Lines.Count < MaxLines)
            {
                vm.Lines.Add(LineVm.From(new TextPanelLine(), OnChanged));
                OnChanged();
            }
        }

        private void RemoveLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not LineVm line) return;
            foreach (var p in _panels)
            {
                if (p.Lines.Remove(line)) { OnChanged(); return; }
            }
        }

        private void PickHeaderColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PanelVm vm)
            {
                var hex = ColorPickerWindow.Pick(this, vm.HeaderColor);
                if (hex != null) vm.HeaderColor = hex;
            }
        }

        private void PickLineColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is LineVm vm)
            {
                var hex = ColorPickerWindow.Pick(this, vm.Color);
                if (hex != null) vm.Color = hex;
            }
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PanelVm vm)
            {
                try { Clipboard.SetText(vm.Url); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _pushTimer.Stop();
            SaveAndPush();
            Close();
        }

        // ---- view models ----

        public class PanelVm : INotifyPropertyChanged
        {
            private Action _changed = () => { };
            private int _index = 1;
            private string _headerText = "", _headerFont = "", _headerColor = "#7cc44a";
            private int _headerSize = 28;

            public ObservableCollection<LineVm> Lines { get; } = new();
            public List<string> Fonts => FontChoices;

            public string Title => "Overlay " + _index;
            public string Url => $"http://localhost:{OverlayServer.Port}/panel/{_index}";

            public string HeaderText { get => _headerText; set { _headerText = value; Bump(nameof(HeaderText)); } }
            public string HeaderFont { get => _headerFont; set { _headerFont = value ?? ""; Bump(nameof(HeaderFont)); } }
            public int HeaderSize { get => _headerSize; set { _headerSize = Math.Clamp(value, 8, 200); Bump(nameof(HeaderSize)); } }
            public string HeaderColor
            {
                get => _headerColor;
                set { _headerColor = value; Bump(nameof(HeaderColor)); Raise(nameof(HeaderBrush)); }
            }
            public Brush HeaderBrush => TryBrush(_headerColor);

            public void SetIndex(int i) { _index = i; Raise(nameof(Title)); Raise(nameof(Url)); }

            public static PanelVm From(TextPanel p, Action changed)
            {
                var vm = new PanelVm
                {
                    _headerText = p.HeaderText ?? "",
                    _headerFont = p.HeaderFont ?? "",
                    _headerSize = p.HeaderSize,
                    _headerColor = p.HeaderColor ?? "#7cc44a",
                };
                foreach (var l in (p.Lines ?? new List<TextPanelLine>()).Take(MaxLines))
                    vm.Lines.Add(LineVm.From(l, changed));
                vm._changed = changed;
                return vm;
            }

            public TextPanel ToModel() => new()
            {
                HeaderText = HeaderText,
                HeaderFont = HeaderFont,
                HeaderSize = HeaderSize,
                HeaderColor = HeaderColor,
                Lines = Lines.Select(l => l.ToModel()).ToList(),
            };

            private void Bump(string n) { Raise(n); _changed(); }
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public class LineVm : INotifyPropertyChanged
        {
            private Action _changed = () => { };
            private string _text = "", _font = "", _color = "#e8e0c4";
            private int _size = 24, _speed = 80;
            private bool _scroll;

            public List<string> Fonts => FontChoices;

            public string Text { get => _text; set { _text = value; Bump(nameof(Text)); } }
            public string Font { get => _font; set { _font = value ?? ""; Bump(nameof(Font)); } }
            public int Size { get => _size; set { _size = Math.Clamp(value, 8, 200); Bump(nameof(Size)); } }
            public bool Scroll { get => _scroll; set { _scroll = value; Bump(nameof(Scroll)); } }
            public int Speed { get => _speed; set { _speed = Math.Clamp(value, 20, 300); Bump(nameof(Speed)); } }
            public string Color
            {
                get => _color;
                set { _color = value; Bump(nameof(Color)); Raise(nameof(ColorBrush)); }
            }
            public Brush ColorBrush => TryBrush(_color);

            public static LineVm From(TextPanelLine l, Action changed) => new()
            {
                _text = l.Text ?? "",
                _font = l.Font ?? "",
                _size = l.Size,
                _color = l.Color ?? "#e8e0c4",
                _scroll = l.Scroll,
                _speed = l.Speed,
                _changed = changed,
            };

            public TextPanelLine ToModel() => new()
            {
                Text = Text, Font = Font, Size = Size, Color = Color, Scroll = Scroll, Speed = Speed,
            };

            private void Bump(string n) { Raise(n); _changed(); }
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private static Brush TryBrush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Transparent; }
        }
    }
}
