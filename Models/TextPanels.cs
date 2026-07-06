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
        public string Image { get; set; } = string.Empty;  // local image path ("" = none)
        public int ImageWidth { get; set; } = 120;         // display width in px
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

        // Optional bordered side blocks flanking the main panel (hidden when empty).
        public string LeftText { get; set; } = string.Empty;
        public string LeftFont { get; set; } = string.Empty;
        public int LeftSize { get; set; } = 20;
        public string LeftColor { get; set; } = "#e8e0c4";
        public string LeftImage { get; set; } = string.Empty;      // local image path ("" = none)
        public int LeftImageWidth { get; set; } = 200;             // display width in px
        public string LeftDir { get; set; } = "v";                 // "v" vertical (default) | "h" horizontal
        public bool LeftScroll { get; set; } = false;
        public int LeftSpeed { get; set; } = 60;                   // pixels per second
        public string RightText { get; set; } = string.Empty;
        public string RightFont { get; set; } = string.Empty;
        public int RightSize { get; set; } = 20;
        public string RightColor { get; set; } = "#e8e0c4";
        public string RightImage { get; set; } = string.Empty;
        public int RightImageWidth { get; set; } = 200;
        public string RightDir { get; set; } = "v";
        public bool RightScroll { get; set; } = false;
        public int RightSpeed { get; set; } = 60;

        // Background opacity for this overlay, 0–100 (default fully opaque).
        public int Opacity { get; set; } = 100;
    }
}
