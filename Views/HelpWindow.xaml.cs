using System;
using System.Windows;
using System.Windows.Input;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>Help page: keyboard shortcuts (rebindable) and quick tips.</summary>
    public partial class HelpWindow : Window
    {
        private readonly HotkeyConfig _config;
        private readonly Action _onBeginCapture;
        private readonly Action _onApply;

        private string? _capturing; // "Toggle" | "Clip" | "Note"

        public HelpWindow(HotkeyConfig config, Action onBeginCapture, Action onApply)
        {
            InitializeComponent();
            _config = config;
            _onBeginCapture = onBeginCapture;
            _onApply = onApply;

            RefreshChips();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void RefreshChips()
        {
            ToggleChip.Text = _config.Toggle.Display;
            ClipChip.Text = _config.Clip.Display;
            NoteChip.Text = _config.Note.Display;
        }

        private void ChangeToggle_Click(object sender, RoutedEventArgs e) => BeginCapture("Toggle");
        private void ChangeClip_Click(object sender, RoutedEventArgs e) => BeginCapture("Clip");
        private void ChangeNote_Click(object sender, RoutedEventArgs e) => BeginCapture("Note");

        private void BeginCapture(string action)
        {
            // Free the global hotkeys so the new combo reaches this window.
            _onBeginCapture();
            _capturing = action;
            StatusText.Text = $"Press a new combo for \"{action}\"… (Esc to cancel)";
            SetChipText(action, "…");
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturing == null) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(key)) return; // wait for the real key

            e.Handled = true;

            if (key == Key.Escape)
            {
                _capturing = null;
                StatusText.Text = "Cancelled.";
                RefreshChips();
                _onApply();           // re-register unchanged
                return;
            }

            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None)
            {
                StatusText.Text = "Include a modifier (Ctrl, Alt, or Shift) so it won't fire during normal typing.";
                return;               // stay in capture mode
            }

            var binding = new HotkeyBinding(mods, key);
            switch (_capturing)
            {
                case "Toggle": _config.Toggle = binding; break;
                case "Clip": _config.Clip = binding; break;
                case "Note": _config.Note = binding; break;
            }

            _capturing = null;
            RefreshChips();
            StatusText.Text = $"Set to {binding.Display}.";
            _onApply();               // save + re-register
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _config.Toggle = new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Alt, Key.S);
            _config.Clip = new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Alt, Key.C);
            _config.Note = new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Alt, Key.N);
            _capturing = null;
            RefreshChips();
            StatusText.Text = "Reset to defaults.";
            _onApply();
        }

        private void SetChipText(string action, string text)
        {
            switch (action)
            {
                case "Toggle": ToggleChip.Text = text; break;
                case "Clip": ClipChip.Text = text; break;
                case "Note": NoteChip.Text = text; break;
            }
        }

        private static bool IsModifierKey(Key k) =>
            k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
              or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
