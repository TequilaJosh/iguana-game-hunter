using System.Windows;
using System.Windows.Input;

namespace GameTracker.Views
{
    /// <summary>
    /// Tiny always-on-top prompt for the quick-note hotkey: type a clip label, Enter saves.
    /// </summary>
    public partial class QuickNoteWindow : Window
    {
        public string NoteText { get; private set; } = string.Empty;

        public QuickNoteWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Activate();
                NoteInput.Focus();
            };
        }

        private void NoteInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NoteText = NoteInput.Text;
                DialogResult = true;
                Close();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
