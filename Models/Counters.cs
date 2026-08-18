namespace GameTracker.Models
{
    /// <summary>
    /// A named on-stream counter (deaths, catchphrases, "chaos" tallies…). Shown on the
    /// overlay for a specific game (or every game), bumped with buttons or global hotkeys.
    /// </summary>
    public class GameCounter
    {
        public string Name { get; set; } = string.Empty;   // e.g. "Deaths"
        public int Value { get; set; }
        public string Color { get; set; } = "#7cc44a";
        public bool Show { get; set; } = true;              // include on the overlay
        public string Game { get; set; } = string.Empty;    // game title it belongs to; "" = all games
        public HotkeyBinding? IncHotkey { get; set; }       // global hotkey: +1
        public HotkeyBinding? DecHotkey { get; set; }       // global hotkey: -1
    }
}
