using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>
    /// Configuration for the /ticker banner overlay. These are encoded into the ticker
    /// URL (…/ticker?kinds=…&amp;size=…&amp;scroll=…&amp;speed=…&amp;pos=…&amp;bg=…&amp;accent=…&amp;labels=…)
    /// when the streamer copies it.
    /// </summary>
    public class TickerSettings
    {
        /// <summary>Which event slots appear, in order (sub, follow, gift, raid, redeem, clip).</summary>
        public List<string> Kinds { get; set; } = new() { "sub", "follow", "gift" };

        /// <summary>Base text size in px (the OBS source rectangle sets the overall size).</summary>
        public int Size { get; set; } = 20;

        /// <summary>Horizontal scroll: "off", "rtl" (right→left) or "ltr" (left→right).</summary>
        public string Scroll { get; set; } = "off";

        /// <summary>Scroll speed in pixels per second (when scrolling).</summary>
        public int Speed { get; set; } = 60;

        /// <summary>Vertical anchor: "bottom" or "top".</summary>
        public string Position { get; set; } = "bottom";

        /// <summary>Bar background opacity, 0–100.</summary>
        public int BgOpacity { get; set; } = 86;

        /// <summary>Accent colour (hex) for labels, dividers and gift amounts.</summary>
        public string Accent { get; set; } = "#7cc44a";

        /// <summary>Show the "NEWEST SUB" style labels above each value.</summary>
        public bool ShowLabels { get; set; } = true;
    }
}
