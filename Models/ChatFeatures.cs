using System.Collections.Generic;

namespace GameTracker.Models
{
    /// <summary>A stream effect viewers can trigger by spending points.</summary>
    public class EffectRedeem
    {
        public string Command { get; set; } = string.Empty;   // e.g. "!confetti"
        public string Effect { get; set; } = "confetti";      // confetti | fireworks | shake | custom
        public int Cost { get; set; } = 100;
        public string SoundPath { get; set; } = string.Empty; // custom: optional sound (played in-app)
        public string ImagePath { get; set; } = string.Empty; // custom: optional image (shown on overlay)
        public double Volume { get; set; } = 1.0;
    }

    /// <summary>Streamer-configurable chat features: counts, chatters list, points, style, redeems.</summary>
    public class ChatFeatureSettings
    {
        // Chatter count header
        public bool ShowCount { get; set; } = true;
        public bool CountPerSource { get; set; } = false;   // false = cumulative
        public bool CountOnOverlay { get; set; } = true;    // false = streamer chat only

        // Active chatters list
        public bool ShowChattersOnOverlay { get; set; } = false;
        public int LurkMinutes { get; set; } = 5;           // active -> lurking after this idle time
        public int RemoveMinutes { get; set; } = 15;        // lurking -> removed after this much longer

        // Reply in chat (via Social Stream Ninja) with @mentions for requests/points/redeems.
        public bool ReplyInChat { get; set; } = true;

        // Points
        public bool PointsEnabled { get; set; } = false;
        public string PointsName { get; set; } = "Points";
        public int PointsIntervalMinutes { get; set; } = 5;
        public int PointsPerInterval { get; set; } = 10;

        // Chat style
        public string ChatStyle { get; set; } = "log";      // log | boxes
        public List<string> BoxColors { get; set; } = new()
        {
            "#7cc44a", "#d4a437", "#4aa3c4", "#c44a7c", "#9146FF",
        };

        // Point redeems
        public List<EffectRedeem> Redeems { get; set; } = new();
    }
}
