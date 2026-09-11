using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>A saved streamer voice-morph: pitch + effect(s) + how long a redeem keeps it on.</summary>
    public class MorphPreset
    {
        public string Name { get; set; } = string.Empty;
        public int PitchSemitones { get; set; } = 0;        // -12 .. +12
        public string Effect { get; set; } = "none";        // legacy single-effect (still used by the Voice Redeems editor)
        public int TimerSeconds { get; set; } = 60;         // redeem duration; overlay counts it down

        /// <summary>
        /// Mixer voices: effect key → intensity 0..100 (0 = off). When any entry is &gt; 0 this
        /// takes precedence over <see cref="Effect"/>, letting a voice blend several effects.
        /// Empty for legacy/single-effect presets.
        /// </summary>
        public Dictionary<string, int> Mix { get; set; } = new();
    }

    /// <summary>Live mic morph engine configuration.</summary>
    public class MorphSettings
    {
        public bool Enabled { get; set; } = false;          // run the mic chain (dry when no morph active)
        public string InputDevice { get; set; } = string.Empty;   // "" = default mic
        public string OutputDevice { get; set; } = string.Empty;  // "" = default output
        public List<MorphPreset> Presets { get; set; } = new();
    }
}
