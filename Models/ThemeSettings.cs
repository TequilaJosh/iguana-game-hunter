using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>The five themeable colors that define the app's look.</summary>
    public class ThemeSettings
    {
        public string PresetName { get; set; } = "Reptile";
        public string Accent { get; set; } = "#7cc44a";      // primary highlight (headers, buttons)
        public string AccentDeep { get; set; } = "#4a7c3a";  // darker accent (borders, rims)
        public string Accent2 { get; set; } = "#d4a437";     // secondary accent (amber highlights)
        public string BgBase { get; set; } = "#0a1410";      // window background base
        public string BgTile { get; set; } = "#1c2a1e";      // scale-pattern line color

        public static readonly List<ThemeSettings> Presets = new()
        {
            new() { PresetName = "Reptile", Accent = "#7cc44a", AccentDeep = "#4a7c3a", Accent2 = "#d4a437", BgBase = "#0a1410", BgTile = "#1c2a1e" },
            new() { PresetName = "Amber",   Accent = "#e0a020", AccentDeep = "#8a6018", Accent2 = "#f0d060", BgBase = "#141008", BgTile = "#2a1e0c" },
            new() { PresetName = "Ocean",   Accent = "#4ab8c4", AccentDeep = "#2a6a7c", Accent2 = "#e0c060", BgBase = "#08131a", BgTile = "#0c2430" },
            new() { PresetName = "Royal",   Accent = "#9a6ce0", AccentDeep = "#5a3a9c", Accent2 = "#e0b040", BgBase = "#120a1a", BgTile = "#241c34" },
            new() { PresetName = "Crimson", Accent = "#e0554a", AccentDeep = "#8a2f28", Accent2 = "#e0a040", BgBase = "#1a0c0a", BgTile = "#301816" },
            new() { PresetName = "Mono",    Accent = "#b8c4b0", AccentDeep = "#6a7a68", Accent2 = "#d0d0c0", BgBase = "#121414", BgTile = "#24282a" },
        };
    }
}
