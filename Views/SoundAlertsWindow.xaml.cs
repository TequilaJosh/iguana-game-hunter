using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using GameTracker.Models;
using GameTracker.Services;

namespace GameTracker.Views
{
    /// <summary>Set up chat-command → sound-file alerts. Persisted on Save.</summary>
    public partial class SoundAlertsWindow : Window
    {
        private readonly ObservableCollection<AlertItem> _items = new();
        private readonly SoundService _tester = new();

        public SoundAlertsWindow()
        {
            InitializeComponent();
            foreach (var a in SettingsService.LoadSoundAlerts())
                _items.Add(new AlertItem { Command = a.Command, FilePath = a.FilePath });
            AlertList.ItemsSource = _items;
        }

        private void Add_Click(object sender, RoutedEventArgs e) =>
            _items.Add(new AlertItem { Command = "!" });

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is AlertItem item)
                _items.Remove(item);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not AlertItem item) return;
            var dlg = new OpenFileDialog
            {
                Title = "Choose a sound file",
                Filter =
                    "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.alac;*.aif;*.aiff;*.mp2;*.mpa;*.adts;*.ac3;*.amr;*.3gp;*.opus;*.ogg" +
                    "|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) == true)
                item.FilePath = dlg.FileName;
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is AlertItem item)
                _tester.Play(item.FilePath);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var list = _items
                .Where(i => !string.IsNullOrWhiteSpace(i.Command) && !string.IsNullOrWhiteSpace(i.FilePath))
                .Select(i => new SoundAlert { Command = i.Command.Trim(), FilePath = i.FilePath.Trim() })
                .ToList();
            SettingsService.SaveSoundAlerts(list);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        public class AlertItem : INotifyPropertyChanged
        {
            private string _command = string.Empty;
            private string _filePath = string.Empty;

            public string Command
            {
                get => _command;
                set { _command = value; OnChanged(nameof(Command)); }
            }
            public string FilePath
            {
                get => _filePath;
                set { _filePath = value; OnChanged(nameof(FilePath)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
