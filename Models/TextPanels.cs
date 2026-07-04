using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>One line of a text-panel overlay: content, look, and optional marquee scroll.</summary>
    public class TextPanelLine
    {
        public string Text { get; set; } = string.Empty;
        public string Font { get; set; } = string.Empty;   // "" = default (Segoe UI)
        public int Size { get; set; } = 24;                // px
        public string Color { get; set; } = "#e8e0c4";
        public bool Scroll { get; set; } = false;          // marquee left-scroll
        public int Speed { get; set; } = 80;               // pixels per second
    }

    /// <summary>
    /// A custom OBS text overlay: a styled header, a divider, and up to 10 styled lines.
    /// Served at /panel/1..5 by the overlay server; updates live as the streamer types.
    /// </summary>
    public class TextPanel
    {
        public string HeaderText { get; set; } = string.Empty;
        public string HeaderFont { get; set; } = string.Empty;
        public int HeaderSize { get; set; } = 28;
        public string HeaderColor { get; set; } = "#7cc44a";
        public List<TextPanelLine> Lines { get; set; } = new();
    }
}
