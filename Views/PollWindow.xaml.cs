using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Create and run a chat poll (!vote) with live overlay results.</summary>
    public partial class PollWindow : Window
    {
        public PollWindow()
        {
            InitializeComponent();
            AddOptionBox("Option 1");
            AddOptionBox("Option 2");
            UpdateButtons();
        }

        private void AddOptionBox(string placeholder)
        {
            var tb = new TextBox
            {
                Style = (Style)FindResource("In"),
                Margin = new Thickness(0, 0, 0, 5),
                Tag = placeholder,
            };
            OptionBoxes.Children.Add(tb);
        }

        private void AddOption_Click(object sender, RoutedEventArgs e)
        {
            if (OptionBoxes.Children.Count >= 6) return;
            AddOptionBox("Option " + (OptionBoxes.Children.Count + 1));
        }

        private List<string> ReadOptions() =>
            OptionBoxes.Children.OfType<TextBox>()
                .Select(t => t.Text.Trim())
                .Where(t => t.Length > 0)
                .ToList();

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            var opts = ReadOptions();
            if (string.IsNullOrWhiteSpace(QuestionBox.Text) || opts.Count < 2)
            {
                Status.Text = "Need a question and at least 2 options.";
                return;
            }
            PollService.Start(QuestionBox.Text, opts);
            Status.Text = "Poll is LIVE — chat votes with !vote 1-" + opts.Count;
            UpdateButtons();
        }

        private void End_Click(object sender, RoutedEventArgs e)
        {
            PollService.End();
            Status.Text = "Voting closed — winner highlighted on the overlay.";
            UpdateButtons();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            PollService.Clear();
            Status.Text = "Poll cleared from the overlay.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            EndBtn.IsEnabled = PollService.IsOpen;
            StartBtn.IsEnabled = !PollService.IsOpen;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
