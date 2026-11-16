namespace GameTracker.Models
{
    /// <summary>A chat command wired to a sound file the streamer plays as an alert.</summary>
    public class SoundAlert
    {
        public string Command { get; set; } = string.Empty;   // e.g. "!airhorn"
        public string FilePath { get; set; } = string.Empty;  // full path to a .mp3 / .wav
        public double Volume { get; set; } = 1.0;             // 0.0–1.0 playback volume

        // Fire when the command's bare word (no leading "!") shows up anywhere in a
        // message, not just as the first word. Matches whole words only, so an alert
        // on "right" triggers on "you're right" but not on "bright".
        public bool MatchAnywhere { get; set; } = false;
    }
}
