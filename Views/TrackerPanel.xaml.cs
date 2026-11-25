using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Where the tracker is living right now (changes the pop-out button).</summary>
    public enum TrackerPanelMode { Docked, Window }

    /// <summary>
    /// The randomizer dashboard: pick a board, click items off as you find them. The same
    /// control is used docked under the chat and popped out into its own window, and both
    /// read/write through <see cref="TrackerService"/> so they never disagree — and neither
    /// does the OBS overlay.
    /// </summary>
    public partial class TrackerPanel : UserControl, INotifyPropertyChanged
    {
        private const string NoGame = "(not linked)";

        private readonly ObservableCollection<GroupVm> _groups = new();
        private bool _loading = true;
        private bool _editing;
        private string _signature = string.Empty;

        /// <summary>The host asked to move the tracker (dock → window, or window → dock).</summary>
        public event Action? PopOutRequested;

        public TrackerPanel()
        {
            InitializeComponent();
            GroupList.ItemsSource = _groups;

            Loaded += (_, _) =>
            {
                TrackerService.Changed += OnServiceChanged;
                RefreshAll();
            };
            Unloaded += (_, _) =>
            {
                TrackerService.Changed -= OnServiceChanged;
                TrackerService.Flush();
            };
        }

        private TrackerPanelMode _mode = TrackerPanelMode.Docked;

        public TrackerPanelMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                if (PopBtn == null) return;
                bool docked = _mode == TrackerPanelMode.Docked;
                PopBtn.Content = docked ? "⬍" : "⬌";
                PopBtn.ToolTip = docked
                    ? "Pop the tracker out into its own window"
                    : "Put the tracker back under the chat";
            }
        }

        // ── edit mode drives the template visibilities ────────────────────────
        public Visibility EditVis => _editing ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ViewVis => _editing ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private void OnServiceChanged()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnServiceChanged)); return; }
            // Our own edits already updated the UI; only rebuild when the shape changed.
            if (Signature() == _signature) { RefreshStates(); return; }
            RefreshAll();
        }

        // ── building the view ─────────────────────────────────────────────────

        private static string Signature()
        {
            var set = TrackerService.Active;
            if (set == null) return "none";
            return set.Id + "|" + set.Name + "|" + string.Join(",",
                (set.Groups ?? new List<TrackerGroup>()).Select(g => g.Name + ":" + (g.Items?.Count ?? 0)));
        }

        /// <summary>Rebuild everything (set list, groups, chips, footer).</summary>
        public void RefreshAll()
        {
            _loading = true;
            try
            {
                var sets = TrackerService.Sets.ToList();
                var active = TrackerService.Active;

                SetCombo.ItemsSource = sets;
                SetCombo.SelectedItem = active;
                SetCombo.IsEnabled = sets.Count > 0;

                _groups.Clear();
                if (active != null)
                {
                    var accent = BrushOf(active.Color);
                    foreach (var g in active.Groups ?? new List<TrackerGroup>())
                        _groups.Add(new GroupVm(g, accent, OnItemsChanged));
                }

                EmptyText.Visibility = active == null ? Visibility.Visible : Visibility.Collapsed;
                EditBtn.IsEnabled = active != null;

                SetNameBox.Text = active?.Name ?? string.Empty;
                RefreshGameCombo(active);

                _signature = Signature();
                RefreshStates();
            }
            finally { _loading = false; }
        }

        /// <summary>Update the tick states + progress without rebuilding the list.</summary>
        private void RefreshStates()
        {
            foreach (var g in _groups) g.RefreshItems();

            var set = TrackerService.Active;
            int done = set?.Done ?? 0, total = set?.Total ?? 0;
            ProgressText.Text = total > 0 ? $"{done}/{total}" : string.Empty;

            // The bar lives inside a Border whose width we only know once laid out.
            var track = (ProgressFill.Parent as Border);
            double w = track?.ActualWidth ?? 0;
            ProgressFill.Width = (total > 0 && w > 0) ? Math.Max(0, w * done / total) : 0;
            if (track != null && track.ActualWidth <= 0)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    double w2 = track.ActualWidth;
                    ProgressFill.Width = (total > 0 && w2 > 0) ? Math.Max(0, w2 * done / total) : 0;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (!_loading) RefreshStates();   // keep the bar honest when the window resizes
        }

        private void RefreshGameCombo(TrackerSet? set)
        {
            var titles = new List<string> { NoGame };
            if (Application.Current.MainWindow is MainWindow mw)
            {
                try { titles.AddRange(mw.AllGameTitles()); } catch { }
            }
            var linked = set?.Game ?? string.Empty;
            if (linked.Length > 0 && !titles.Contains(linked, StringComparer.OrdinalIgnoreCase))
                titles.Add(linked);
            GameCombo.ItemsSource = titles;
            GameCombo.SelectedItem = linked.Length == 0
                ? NoGame
                : titles.FirstOrDefault(t => t.Equals(linked, StringComparison.OrdinalIgnoreCase)) ?? NoGame;
        }

        private static Brush BrushOf(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(0x7c, 0xc4, 0x4a));
        }

        private void OnItemsChanged() => TrackerService.Touch();

        // ── clicking items ────────────────────────────────────────────────────

        private static ItemVm? VmOf(object sender) => (sender as FrameworkElement)?.Tag as ItemVm;

        private static int Step() =>
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10 : 1;

        private void Item_Click(object sender, RoutedEventArgs e)
        {
            if (VmOf(sender) is not { } vm) return;
            vm.Advance(Step());
            RefreshStates();
            TrackerService.Touch();
        }

        private void Item_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (VmOf(sender) is not { } vm) return;
            e.Handled = true;
            vm.Advance(-Step());
            RefreshStates();
            TrackerService.Touch();
        }

        // ── editing ───────────────────────────────────────────────────────────

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            _editing = !_editing;
            EditFooter.Visibility = EditVis;
            Raise(nameof(EditVis));
            Raise(nameof(ViewVis));
            if (!_editing) { TrackerService.Save(); RefreshAll(); }
        }

        private void AddItem_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox box || box.Tag is not GroupVm g) return;
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            box.Text = string.Empty;
            g.Model.Items.Add(TrackerItem.Parse(text));
            e.Handled = true;
            TrackerService.Save();
            RefreshAll();
        }

        private void AddGroup_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var set = TrackerService.Active;
            var text = AddGroupBox.Text.Trim();
            if (set == null || text.Length == 0) return;
            AddGroupBox.Text = string.Empty;
            set.Groups.Add(new TrackerGroup { Name = text });
            e.Handled = true;
            TrackerService.Save();
            RefreshAll();
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            // The ✕ is only visible in edit mode; refuse it otherwise so nothing but an
            // explicit edit can ever remove an item.
            if (!_editing || VmOf(sender) is not { } vm) return;
            foreach (var g in TrackerService.Active?.Groups ?? new List<TrackerGroup>())
                g.Items.Remove(vm.Model);
            TrackerService.Save();
            RefreshAll();
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!_editing || (sender as FrameworkElement)?.Tag is not GroupVm g) return;
            var set = TrackerService.Active;
            if (set == null) return;
            if (g.Model.Items.Count > 0 &&
                MessageBox.Show(Window.GetWindow(this),
                    $"Remove the group “{g.Model.Name}” and its {g.Model.Items.Count} item(s)?",
                    "Remove group", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                != MessageBoxResult.OK) return;
            set.Groups.Remove(g.Model);
            TrackerService.Save();
            RefreshAll();
        }

        private void SetName_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var set = TrackerService.Active;
            if (set == null) return;
            var name = SetNameBox.Text.Trim();
            if (name.Length == 0 || name == set.Name) return;
            set.Name = name;
            TrackerService.Save();
            RefreshAll();
        }

        private void SetName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { SetName_Changed(sender, e); e.Handled = true; }
        }

        private void GameCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var set = TrackerService.Active;
            if (set == null) return;
            var pick = GameCombo.SelectedItem as string ?? NoGame;
            set.Game = pick == NoGame ? string.Empty : pick;
            TrackerService.Save();
        }

        private void SetColor_Click(object sender, RoutedEventArgs e)
        {
            var set = TrackerService.Active;
            if (set == null) return;
            var hex = ColorPickerWindow.Pick(Window.GetWindow(this), set.Color);
            if (hex == null) return;
            set.Color = hex;
            TrackerService.Save();
            RefreshAll();
        }

        // ── the board menu ────────────────────────────────────────────────────

        private void Sets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b) return;
            var has = TrackerService.Active != null;
            var menu = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

            void Add(string header, Action act, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += (_, _) => act();
                menu.Items.Add(mi);
            }

            Add("\U0001F4DA  Add a built-in set…", AddFromCatalog);
            Add("✨  New empty board", NewEmpty);
            Add("\U0001F4CB  Duplicate this board", Duplicate, has);
            menu.Items.Add(new Separator());
            Add("\U0001F5D1  Delete this board", DeleteSet, has);
            menu.IsOpen = true;
        }

        private void AddFromCatalog()
        {
            var ids = TrackerCatalogWindow.Pick(Window.GetWindow(this));
            if (ids == null || ids.Count == 0) return;
            foreach (var id in ids) TrackerService.AddFromCatalog(id);
            RefreshAll();
        }

        private void NewEmpty()
        {
            var set = new TrackerSet { Name = "New board" };
            set.Groups.Add(new TrackerGroup { Name = "Key Items" });
            set.Groups.Add(new TrackerGroup { Name = "Quests" });
            TrackerService.Add(set);
            RefreshAll();
            if (!_editing) Edit_Click(this, new RoutedEventArgs());
        }

        private void Duplicate()
        {
            var set = TrackerService.Active;
            if (set == null) return;
            var copy = set.Clone(newId: true);
            copy.Name = set.Name + " (copy)";
            TrackerService.Add(copy);
            RefreshAll();
        }

        private void DeleteSet_Click(object sender, RoutedEventArgs e) => DeleteSet();

        private void DeleteSet()
        {
            var set = TrackerService.Active;
            if (set == null) return;
            if (MessageBox.Show(Window.GetWindow(this),
                    $"Delete the board “{set.Name}”?\n\nIts items and this run's progress go with it.",
                    "Delete board", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK) return;
            TrackerService.Remove(set);
            RefreshAll();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var set = TrackerService.Active;
            if (set == null || set.Done == 0) return;
            if (MessageBox.Show(Window.GetWindow(this),
                    $"Untick all {set.Done} collected item(s) on “{set.Name}”?\n\nThe items stay — only this run's progress is cleared.",
                    "Reset run", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                != MessageBoxResult.OK) return;
            set.ResetRun();
            TrackerService.Save();
            RefreshAll();
        }

        private void SetCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (SetCombo.SelectedItem is not TrackerSet set) return;
            if (set.Id == TrackerService.ActiveId) return;
            TrackerService.SetActive(set.Id);
            RefreshAll();
        }

        private void Pop_Click(object sender, RoutedEventArgs e) => PopOutRequested?.Invoke();

        // ── view models ───────────────────────────────────────────────────────

        /// <summary>A group of chips. Renaming it writes straight through to the model.</summary>
        public sealed class GroupVm : INotifyPropertyChanged
        {
            private readonly Action _changed;

            public GroupVm(TrackerGroup model, Brush accent, Action changed)
            {
                Model = model;
                _changed = changed;
                Items = new ObservableCollection<ItemVm>(
                    (model.Items ?? new List<TrackerItem>()).Select(i => new ItemVm(i, accent)));
            }

            public TrackerGroup Model { get; }
            public ObservableCollection<ItemVm> Items { get; }

            public string Name
            {
                get => Model.Name;
                set
                {
                    var v = (value ?? string.Empty).Trim();
                    if (v == Model.Name) return;
                    Model.Name = v;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                    _changed();
                }
            }

            public void RefreshItems() { foreach (var i in Items) i.Refresh(); }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        /// <summary>One chip. Colours are worked out here so the XAML stays plain bindings.</summary>
        public sealed class ItemVm : INotifyPropertyChanged
        {
            private static readonly Brush Faint = Frozen(Color.FromArgb(0x30, 0xff, 0xff, 0xff));
            private static readonly Brush Clear = Frozen(Colors.Transparent);
            private static readonly Brush Dim = Frozen(Color.FromRgb(0xa8, 0xc4, 0x88));

            private readonly Brush _accent;
            private readonly Brush _accentSoft;

            public ItemVm(TrackerItem model, Brush accent)
            {
                Model = model;
                _accent = accent;
                _accentSoft = Soften(accent, 0x33);
            }

            public TrackerItem Model { get; }

            public string Display => string.IsNullOrWhiteSpace(Model.Icon)
                ? Model.Name
                : Model.Icon + " " + Model.Name;

            /// <summary>What the rename box shows/writes — keeps the "*8" counter suffix.</summary>
            public string EditName
            {
                get => Model.IsCountable ? $"{Model.Name} *{Model.Max}" : Model.Name;
                set
                {
                    var parsed = TrackerItem.Parse(value);
                    if (parsed.Name.Length == 0) { Refresh(); return; }
                    Model.Name = parsed.Name;
                    Model.Max = parsed.Max;
                    if (Model.Max > 1) Model.Count = Math.Min(Model.Count, Model.Max);
                    Refresh();
                    TrackerService.Touch();
                }
            }

            public string CountText => Model.IsCountable ? $"{Model.Count}/{Model.Max}" : string.Empty;

            public string Tip => Model.IsCountable
                ? $"{Model.Name} — click to add one (Shift +10), right-click to take one back"
                : Model.Done ? $"{Model.Name} — got it (right-click to untick)"
                             : $"{Model.Name} — click when you find it";

            public Brush ChipBrush => Model.Done ? _accentSoft : Clear;
            public Brush EdgeBrush => Model.Done ? _accent : Faint;
            public Brush DotBrush => Model.Done ? _accent : Clear;
            public Brush TextBrush => Model.Done ? _accent : Dim;
            public FontWeight Weight => Model.Done ? FontWeights.Bold : FontWeights.Normal;

            public void Advance(int delta) { Model.Advance(delta); Refresh(); }

            public void Refresh()
            {
                foreach (var n in new[] { nameof(Display), nameof(EditName), nameof(CountText),
                                          nameof(Tip), nameof(ChipBrush), nameof(EdgeBrush),
                                          nameof(DotBrush), nameof(TextBrush), nameof(Weight) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            }

            private static Brush Frozen(Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }

            // A see-through version of the board colour, for the collected chip's fill.
            private static Brush Soften(Brush b, byte alpha)
            {
                if (b is SolidColorBrush s)
                {
                    var c = s.Color;
                    return Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));
                }
                return Clear;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
