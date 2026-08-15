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
        public string VideoPath { get; set; } = string.Empty; // optional video played on the overlay
        public HotkeyBinding? Hotkey { get; set; }            // optional global hotkey (fires free)
        public string MorphPreset { get; set; } = string.Empty; // optional streamer voice-morph to activate
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

        // Points (on by default: 25 points every 5 minutes)
        public bool PointsEnabled { get; set; } = true;
        public string PointsName { get; set; } = "Points";
        public string BalanceCommand { get; set; } = "!points"; // change if it collides with another bot
        public int PointsIntervalMinutes { get; set; } = 5;
        public int PointsPerInterval { get; set; } = 25;

        // Perks: first chatter of the stream + daily visit streaks (0 = off).
        public int FirstChatterBonus { get; set; } = 50;
        public int StreakBonusPerDay { get; set; } = 10;

        // Chat style
        public string ChatStyle { get; set; } = "log";      // log | boxes
        public List<string> BoxColors { get; set; } = new()
        {
            "#7cc44a", "#d4a437", "#4aa3c4", "#c44a7c", "#9146FF",
        };

        // Point redeems
        public List<EffectRedeem> Redeems { get; set; } = new();

        // Discord: post clips (the !clip command and any Twitch clip links dropped in
        // chat) to a channel. If a bot ingest URL + token are set, clips go to the
        // companion bot; otherwise they fall back to a plain webhook.
        public string DiscordWebhook { get; set; } = "";
        public bool PostClips { get; set; } = true;
        public string BotIngestUrl { get; set; } = "";     // e.g. https://your-host/clip
        public string BotIngestToken { get; set; } = "";   // matches the bot's INGEST_TOKEN
    }
}
