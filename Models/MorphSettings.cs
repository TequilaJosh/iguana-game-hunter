using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>A saved streamer voice-morph: pitch + effect + how long a redeem keeps it on.</summary>
    public class MorphPreset
    {
        public string Name { get; set; } = string.Empty;
        public int PitchSemitones { get; set; } = 0;        // -12 .. +12
        public string Effect { get; set; } = "none";        // none/robot/whisper/echo/distortion/flanger/vibrato/tremolo/autowah
        public int TimerSeconds { get; set; } = 60;         // redeem duration; overlay counts it down
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
