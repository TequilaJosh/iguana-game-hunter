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

    /// <summary>A chat badge: an image (Url) if available, else a short colored text label.</summary>
    public class ChatBadge
    {
        public string Label { get; set; } = string.Empty;  // e.g. "MOD" (used when no image)
        public string Url { get; set; } = string.Empty;    // badge image URL (preferred if set)
        public string Color { get; set; } = "#4a7c3a";     // background for the text label
    }

    /// <summary>What kind of stream event a message represents. Chat = an ordinary message.</summary>
    public enum ChatEventKind { Chat, Gift, Follow, Subscribe, Like, Share, Raid, Other }

    /// <summary>A single chat message from a connected live source.</summary>
    public class ChatMessage
    {
        /// <summary>Originating platform name, lowercase (e.g. "twitch", "youtube", "tiktok", "kick", "restream").</summary>
        public string Platform { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string UserColor { get; set; } = string.Empty; // hex (#RRGGBB) if provided
        public string AvatarUrl { get; set; } = string.Empty; // account photo, if the source provides one
        public List<ChatBadge> Badges { get; set; } = new();  // role/platform badges
        public DateTime At { get; set; } = DateTime.Now;

        /// <summary>The message broken into text + emote segments (for rich rendering).</summary>
        public List<ChatSegment> Segments { get; set; } = new();

        // ── Stream events (gifts, follows, subs…) ──────────────────────────────
        /// <summary>The event this message represents. Default Chat = a normal message.</summary>
        public ChatEventKind EventKind { get; set; } = ChatEventKind.Chat;
        /// <summary>Gift name if this is a gift (e.g. "Rose", "Galaxy"). Empty otherwise.</summary>
        public string GiftName { get; set; } = string.Empty;
        /// <summary>Coin/diamond value of a gift (TikTok coins), or generic donation amount. 0 if unknown.</summary>
        public int CoinValue { get; set; }
        /// <summary>Gift combo/repeat count (TikTok gifts can be sent in bursts). 1 by default.</summary>
        public int Repeat { get; set; } = 1;
        /// <summary>Human-readable event summary for logs/feed (e.g. "sent 5× Rose", "followed").</summary>
        public string EventText { get; set; } = string.Empty;

        public bool IsEvent => EventKind != ChatEventKind.Chat;

        /// <summary>Plain-text form (emotes flattened to their alt/name) for matching and logging.</summary>
        public string Text => string.Concat(Segments.Select(s => s.Text));
    }
}
