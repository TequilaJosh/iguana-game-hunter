using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTracker.Models
{
    public enum ChatSegmentKind { Text, Emote }

    /// <summary>A piece of a chat message: either plain text or an emote image.</summary>
    public class ChatSegment
    {
        public ChatSegmentKind Kind { get; set; } = ChatSegmentKind.Text;
        public string Text { get; set; } = string.Empty;   // text run, or the emote's alt/name
        public string Url { get; set; } = string.Empty;    // emote image URL (Emote only)

        public static ChatSegment PlainText(string t) => new() { Kind = ChatSegmentKind.Text, Text = t };
        public static ChatSegment Emote(string url, string alt) =>
            new() { Kind = ChatSegmentKind.Emote, Url = url, Text = alt };
    }

    /// <summary>A single chat message from a connected live source.</summary>
    public class ChatMessage
    {
        /// <summary>Originating platform name, lowercase (e.g. "twitch", "youtube", "tiktok", "kick", "restream").</summary>
        public string Platform { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string UserColor { get; set; } = string.Empty; // hex (#RRGGBB) if provided
        public DateTime At { get; set; } = DateTime.Now;

        /// <summary>The message broken into text + emote segments (for rich rendering).</summary>
        public List<ChatSegment> Segments { get; set; } = new();

        /// <summary>Plain-text form (emotes flattened to their alt/name) for matching and logging.</summary>
        public string Text => string.Concat(Segments.Select(s => s.Text));
    }
}
