using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>A named, reusable wheel list (e.g. "Bizhawk Shuffler", "Random RPG Night").</summary>
    public class WheelPreset
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Items { get; set; } = new();
    }
}
