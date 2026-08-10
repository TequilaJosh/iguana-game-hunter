using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>A user-made voice preset: a name + base voice + funny-voice effect.</summary>
    public class CustomVoice
    {
        public string Name { get; set; } = string.Empty;
        public string Voice { get; set; } = string.Empty;
        public string Effect { get; set; } = "normal";
    }

    /// <summary>Text-to-speech options for reading incoming chat aloud.</summary>
    public class ChatTtsSettings
    {
        public bool Enabled { get; set; } = false;
        public bool PerChatterVoices { get; set; } = true;   // each chatter gets their own saved voice
        public string Voice { get; set; } = string.Empty;    // single-voice mode: base voice name
        public string Effect { get; set; } = "normal";       // single-voice mode: funny-voice effect key
        public int Rate { get; set; } = 0;                   // -10 (slow) .. 10 (fast)
        public int Volume { get; set; } = 100;               // 0..100
        public bool ReadName { get; set; } = true;           // "Name says: ..." vs message only
        public bool SkipCommands { get; set; } = true;       // don't read messages starting with "!"
        public int MaxChars { get; set; } = 200;             // truncate long messages
        public List<CustomVoice> Custom { get; set; } = new(); // user-made voice presets

        // Ignore rules: these messages are never read aloud.
        public List<string> IgnoreUsers { get; set; } = new() { "StreamElements" };
        public List<string> IgnoreKeywords { get; set; } = new();
        public bool SkipRedeemMessages { get; set; } = true; // "<user> redeemed <reward>" announcements
    }
}
