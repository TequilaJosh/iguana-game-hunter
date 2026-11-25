using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>
    /// Picker for the bundled tracker sets. Returns the catalog ids the streamer chose;
    /// the caller copies those into their own list (see <see cref="Services.TrackerService"/>).
    /// </summary>
    public partial class TrackerCatalogWindow : Window
    {
        private readonly List<Row> _rows;

        private TrackerCatalogWindow()
        {
            InitializeComponent();
            _rows = TrackerCatalog.All
                .OrderBy(s => Array.IndexOf(TrackerCatalog.Systems, s.System))
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Row.From)
                .ToList();
            SetList.ItemsSource = _rows;
            CountText.Text = $"{_rows.Count} sets bundled — NES, SNES, PS1 and PS2.";
            Loaded += (_, _) => FilterBox.Focus();
        }

        /// <summary>Show the picker. Returns the chosen catalog ids, or null if cancelled.</summary>
        public static List<string>? Pick(Window? owner)
        {
            var w = new TrackerCatalogWindow();
            if (owner != null && !ReferenceEquals(owner, w)) w.Owner = owner;
            return w.ShowDialog() == true
                ? w.SetList.SelectedItems.OfType<Row>().Select(r => r.Id).ToList()
                : null;
        }

        private void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            var q = FilterBox.Text.Trim();
            var shown = q.Length == 0
                ? _rows
                : _rows.Where(r => r.Matches(q)).ToList();
            SetList.ItemsSource = shown;
            CountText.Text = q.Length == 0
                ? $"{_rows.Count} sets bundled — NES, SNES, PS1 and PS2."
                : $"{shown.Count} of {_rows.Count} match “{q}”.";
        }

        private void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SetList.SelectedItem is Row) Ok_Click(sender, e);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (SetList.SelectedItems.Count == 0) return;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>One line in the list.</summary>
        public sealed class Row
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string System { get; init; } = string.Empty;
            public string Summary { get; init; } = string.Empty;
            public string Count { get; init; } = string.Empty;

            public static Row From(TrackerSet s) => new()
            {
                Id = s.Id,
                Name = s.Name,
                System = s.System,
                Summary = string.Join(" · ", (s.Groups ?? new List<TrackerGroup>()).Select(g => g.Name)),
                Count = s.Total + " items",
            };

            public bool Matches(string q) =>
                Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                System.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                Summary.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }
}
