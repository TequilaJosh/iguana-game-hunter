using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>
    /// Set up (or change / turn off) a game's pop-up reminder: when it first shows, how often it
    /// repeats, and an optional note. Result is read by the caller after ShowDialog().
    /// </summary>
    public partial class ReminderWindow : Window
    {
        private const int DaysAhead = 60;
        private const int CustomTag = -1;

        /// <summary>Units for a custom repeat ("every N …"), in minutes.</summary>
        private static readonly (string Label, int Minutes)[] Units =
            { ("minutes", 1), ("hours", 60), ("days", 1440), ("weeks", 10080) };

        private bool IsCustom => RepeatBox.SelectedItem is ComboBoxItem { Tag: int t } && t == CustomTag;

        /// <summary>The reminder to store (set when the user clicks Save).</summary>
        public GameReminder? Result { get; private set; }

        /// <summary>True when the user clicked "Turn off reminder".</summary>
        public bool TurnedOff { get; private set; }

        public ReminderWindow(Game game)
        {
            InitializeComponent();
            GameTitle.Text = game.Title;

            var existing = game.HasReminder ? game.Reminder : null;
            // New reminder: the next full hour from now.
            var start = existing?.NextAt ?? DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1);

            // Day: today, tomorrow, then each date for the next couple of months.
            var today = DateTime.Now.Date;
            for (int i = 0; i <= DaysAhead; i++)
            {
                var d = today.AddDays(i);
                var label = i == 0 ? "Today" : i == 1 ? "Tomorrow" : d.ToString("ddd MMM d");
                DayBox.Items.Add(new ComboBoxItem { Content = label, Tag = d });
            }
            // Keep an existing date that falls outside the list selectable.
            if (start.Date < today || start.Date > today.AddDays(DaysAhead))
                DayBox.Items.Insert(0, new ComboBoxItem { Content = start.ToString("ddd MMM d, yyyy"), Tag = start.Date });
            SelectByTag(DayBox, start.Date);

            for (int h = 1; h <= 12; h++) HourBox.Items.Add(new ComboBoxItem { Content = h.ToString(), Tag = h });
            for (int m = 0; m < 60; m++) MinuteBox.Items.Add(new ComboBoxItem { Content = m.ToString("00"), Tag = m });
            AmPmBox.Items.Add(new ComboBoxItem { Content = "AM", Tag = "AM" });
            AmPmBox.Items.Add(new ComboBoxItem { Content = "PM", Tag = "PM" });
            int h12 = start.Hour % 12 == 0 ? 12 : start.Hour % 12;
            SelectByTag(HourBox, h12);
            SelectByTag(MinuteBox, start.Minute);
            SelectByTag(AmPmBox, start.Hour < 12 ? "AM" : "PM");

            foreach (var mins in GameReminder.RepeatChoices)
                RepeatBox.Items.Add(new ComboBoxItem
                {
                    Content = char.ToUpper(GameReminder.DescribeRepeat(mins)[0]) + GameReminder.DescribeRepeat(mins)[1..],
                    Tag = mins,
                });
            RepeatBox.Items.Add(new ComboBoxItem { Content = "Custom…", Tag = CustomTag });
            foreach (var (label, mins) in Units)
                CustomUnit.Items.Add(new ComboBoxItem { Content = label, Tag = mins });
            RepeatBox.SelectionChanged += (_, _) =>
                CustomRow.Visibility = IsCustom ? Visibility.Visible : Visibility.Collapsed;

            // Default for a new reminder: keep nagging every hour until turned off. An existing
            // interval that isn't one of the presets opens on Custom with its value filled in.
            int repeat = existing?.RepeatMinutes ?? 60;
            if (GameReminder.RepeatChoices.Contains(repeat))
            {
                SelectByTag(RepeatBox, repeat);
                CustomNum.Text = "3";
                SelectByTag(CustomUnit, 60);
            }
            else
            {
                SelectByTag(RepeatBox, CustomTag);
                var unit = Units.Select(u => u.Minutes).Where(u => repeat % u == 0).Max();
                CustomNum.Text = (repeat / unit).ToString();
                SelectByTag(CustomUnit, unit);
            }

            NoteBox.Text = existing?.Note ?? string.Empty;
            TurnOffBtn.Visibility = existing != null ? Visibility.Visible : Visibility.Collapsed;

            foreach (var box in new[] { DayBox, HourBox, MinuteBox, AmPmBox })
                box.SelectionChanged += (_, _) => UpdateHint();
            UpdateHint();
        }

        private static void SelectByTag(ComboBox box, object tag)
        {
            var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, tag));
            box.SelectedItem = item ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }

        private static T TagOf<T>(ComboBox box) => (T)((ComboBoxItem)box.SelectedItem).Tag;

        private DateTime ChosenTime()
        {
            var day = TagOf<DateTime>(DayBox);
            int hour = TagOf<int>(HourBox) % 12 + (TagOf<string>(AmPmBox) == "PM" ? 12 : 0);
            return day.AddHours(hour).AddMinutes(TagOf<int>(MinuteBox));
        }

        // A time in the past isn't an error — it just means "remind me right away".
        private void UpdateHint()
        {
            if (DayBox.SelectedItem == null || HourBox.SelectedItem == null ||
                MinuteBox.SelectedItem == null || AmPmBox.SelectedItem == null) return;
            bool past = ChosenTime() <= DateTime.Now;
            Hint.Text = "That time has already passed — the first reminder will pop up right away.";
            Hint.Visibility = past ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            int repeat = TagOf<int>(RepeatBox);
            if (IsCustom)
            {
                if (!int.TryParse((CustomNum.Text ?? "").Trim(), out int n) || n < 1 || n > 999)
                {
                    ErrorText.Text = "Enter how often as a whole number from 1 to 999.";
                    ErrorText.Visibility = Visibility.Visible;
                    CustomNum.Focus();
                    return;
                }
                repeat = n * TagOf<int>(CustomUnit);
            }
            Result = new GameReminder
            {
                Enabled = true,
                NextAt = ChosenTime(),
                RepeatMinutes = repeat,
                Note = (NoteBox.Text ?? string.Empty).Trim(),
            };
            DialogResult = true;
        }

        private void TurnOff_Click(object sender, RoutedEventArgs e)
        {
            TurnedOff = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
