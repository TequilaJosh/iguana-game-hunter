using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Add / rename / remove / reorder the global suggestion types. Persisted on Save.</summary>
    public partial class SuggestionTypesWindow : Window
    {
        private readonly ObservableCollection<TypeItem> _items = new();

        public SuggestionTypesWindow()
        {
            InitializeComponent();
            foreach (var t in SuggestionTypes.All) _items.Add(new TypeItem { Name = t });
            TypesList.ItemsSource = _items;
        }

        private void Add_Click(object sender, RoutedEventArgs e) => AddFromBox();

        private void NewBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { AddFromBox(); e.Handled = true; }
        }

        private void AddFromBox()
        {
            var name = NewBox.Text.Trim();
            if (name.Length == 0) return;
            if (_items.Any(i => string.Equals(i.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            {
                NewBox.Clear();
                return; // already present
            }
            _items.Add(new TypeItem { Name = name });
            NewBox.Clear();
            TypesList.SelectedIndex = _items.Count - 1;
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            int i = TypesList.SelectedIndex;
            if (i < 0) return;
            _items.RemoveAt(i);
            TypesList.SelectedIndex = Math.Min(i, _items.Count - 1);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

        private void Move(int delta)
        {
            int i = TypesList.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _items.Count) return;
            _items.Move(i, j);
            TypesList.SelectedIndex = j;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            foreach (var t in SuggestionTypes.Defaults()) _items.Add(new TypeItem { Name = t });
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var list = _items
                .Select(i => (i.Name ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0) list = SuggestionTypes.Defaults();

            SuggestionTypes.Set(list);
            SettingsService.SaveSuggestionTypes(SuggestionTypes.All.ToList());
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>Editable wrapper so the list rows two-way bind for in-place rename.</summary>
        public class TypeItem : INotifyPropertyChanged
        {
            private string _name = string.Empty;
            public string Name
            {
                get => _name;
                set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
