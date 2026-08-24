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
        public string OutputDevice { get; set; } = "";       // "" = system default output device
        public bool PerChatterVoices { get; set; } = true;   // each chatter gets their own saved voice
        public string Voice { get; set; } = string.Empty;    // single-voice mode: base voice name
        public string Effect { get; set; } = "normal";       // single-voice mode: funny-voice effect key
        public int Rate { get; set; } = 0;                   // -10 (slow) .. 10 (fast)
        public int Volume { get; set; } = 100;               // 0..100
        public bool ReadName { get; set; } = true;           // "Name says: ..." vs message only
        public bool SkipCommands { get; set; } = true;       // don't read messages starting with "!"
        public int MaxChars { get; set; } = 500;             // truncate long messages
        public List<CustomVoice> Custom { get; set; } = new(); // user-made voice presets

        // Ignore rules: these messages are never read aloud.
        public List<string> IgnoreUsers { get; set; } = new() { "StreamElements" };
        public List<string> IgnoreKeywords { get; set; } = new();
        public bool SkipRedeemMessages { get; set; } = true; // "<user> redeemed <reward>" announcements
        public bool SkipLinks { get; set; } = true;          // strip URLs from spoken text (skip pure-link messages)
        public bool SkipEmotes { get; set; } = false;        // strip chat emotes (skip emote-only messages)

        // Bad-word filter: bleep listed words in spoken chat with a chicken bawk.
        // BadWords is the full, editable list (seeded from BadWordDefaults on first run,
        // tracked by BadWordsInit so an intentionally-emptied list isn't re-seeded).
        public bool BleepBadWords { get; set; } = true;
        public List<string> BadWords { get; set; } = new();
        public bool BadWordsInit { get; set; } = false;
    }
}
