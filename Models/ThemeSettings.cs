using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>The themeable colors that define the app's look (accents, background, and text).</summary>
    public class ThemeSettings
    {
        public string PresetName { get; set; } = "Reptile";
        public string Accent { get; set; } = "#7cc44a";      // primary highlight (headers, buttons)
        public string AccentDeep { get; set; } = "#4a7c3a";  // darker accent (borders, rims)
        public string Accent2 { get; set; } = "#d4a437";     // secondary accent (amber highlights)
        public string BgBase { get; set; } = "#0a1410";      // window background base
        public string BgTile { get; set; } = "#1c2a1e";      // scale-pattern line color

        // Text colors — let a theme flip between light-on-dark and dark-on-light (light mode).
        public string Text { get; set; } = "#e8e0c4";        // primary text
        public string TextDim { get; set; } = "#a8c488";     // labels / secondary text
        public string TextFaint { get; set; } = "#7a9070";   // hints / tertiary text

        public static readonly List<ThemeSettings> Presets = new()
        {
            new() { PresetName = "Reptile", Accent = "#7cc44a", AccentDeep = "#4a7c3a", Accent2 = "#d4a437", BgBase = "#0a1410", BgTile = "#1c2a1e", Text = "#e8e0c4", TextDim = "#a8c488", TextFaint = "#7a9070" },
            new() { PresetName = "Amber",   Accent = "#e0a020", AccentDeep = "#8a6018", Accent2 = "#f0d060", BgBase = "#141008", BgTile = "#2a1e0c", Text = "#e8e0c4", TextDim = "#a8c488", TextFaint = "#7a9070" },
            new() { PresetName = "Ocean",   Accent = "#4ab8c4", AccentDeep = "#2a6a7c", Accent2 = "#e0c060", BgBase = "#08131a", BgTile = "#0c2430", Text = "#e8e0c4", TextDim = "#a8c488", TextFaint = "#7a9070" },
            new() { PresetName = "Royal",   Accent = "#9a6ce0", AccentDeep = "#5a3a9c", Accent2 = "#e0b040", BgBase = "#120a1a", BgTile = "#241c34", Text = "#e8e0c4", TextDim = "#a8c488", TextFaint = "#7a9070" },
            new() { PresetName = "Crimson", Accent = "#e0554a", AccentDeep = "#8a2f28", Accent2 = "#e0a040", BgBase = "#1a0c0a", BgTile = "#301816", Text = "#e8e0c4", TextDim = "#a8c488", TextFaint = "#7a9070" },
            new() { PresetName = "Mono",    Accent = "#b8c4b0", AccentDeep = "#6a7a68", Accent2 = "#d0d0c0", BgBase = "#121414", BgTile = "#24282a", Text = "#e8e0c4", TextDim = "#a8c488", TextFaint = "#7a9070" },

            // Kingdom Hearts — near-black with keyblade blue and a gold crown accent.
            new() { PresetName = "Kingdom Hearts", Accent = "#3aa0ff", AccentDeep = "#1e5bb0", Accent2 = "#e8c34a", BgBase = "#05060d", BgTile = "#12203c", Text = "#ffffff", TextDim = "#9fb6e0", TextFaint = "#5f7098" },
            // Legend of Zelda — Hyrule forest green with Triforce gold.
            new() { PresetName = "Legend of Zelda", Accent = "#4fae57", AccentDeep = "#2c6d34", Accent2 = "#f2c531", BgBase = "#0a1409", BgTile = "#17301a", Text = "#f4f7ea", TextDim = "#a9c69a", TextFaint = "#6f8a63" },
            // Mario — red cap on overalls-blue, coin-gold accent.
            new() { PresetName = "Mario", Accent = "#e0392b", AccentDeep = "#8f1d15", Accent2 = "#ffcc00", BgBase = "#0e1633", BgTile = "#1d2f66", Text = "#ffffff", TextDim = "#aeb7e0", TextFaint = "#6b76a8" },
            // Simple neutral dark.
            new() { PresetName = "Dark", Accent = "#8ab4f8", AccentDeep = "#3b6ea5", Accent2 = "#e0b060", BgBase = "#141518", BgTile = "#2a2c33", Text = "#ececf1", TextDim = "#b4b8c2", TextFaint = "#7e828c" },
            // Simple light — dark text on a near-white background.
            new() { PresetName = "Light", Accent = "#3a66c8", AccentDeep = "#27478f", Accent2 = "#c8901a", BgBase = "#f4f5f8", BgTile = "#dbe0ec", Text = "#1b1e27", TextDim = "#4a4f5e", TextFaint = "#7c8291" },
        };
    }
}
