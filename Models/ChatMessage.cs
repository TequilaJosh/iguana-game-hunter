using System;

namespace GameTracker.Models
{
    /// <summary>A single chat message from a connected live source.</summary>
    public class ChatMessage
    {
        /// <summary>Originating platform name, lowercase (e.g. "twitch", "youtube", "tiktok", "kick", "restream").</summary>
        public string Platform { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string UserColor { get; set; } = string.Empty; // hex (#RRGGBB) if provided
        public DateTime At { get; set; } = DateTime.Now;
    }
}
