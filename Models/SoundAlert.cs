namespace GameTracker.Models
{
    /// <summary>A chat command wired to a sound file the streamer plays as an alert.</summary>
    public class SoundAlert
    {
        public string Command { get; set; } = string.Empty;   // e.g. "!airhorn"
        public string FilePath { get; set; } = string.Empty;  // full path to a .mp3 / .wav
    }
}
