using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using GameTracker.Models;

namespace GameTracker.Services.Chat
{
    /// <summary>
    /// Aggregators (Social Stream Ninja, Restream) deliver chat bodies as HTML — emotes
    /// arrive as &lt;img&gt; tags. Parse that into segments: emote images keep their URL
    /// (for real rendering) and alt/name; other tags are dropped; entities decoded.
    /// Plain text (e.g. Twitch IRC) becomes a single text segment.
    /// </summary>
    internal static class ChatText
    {
        private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex UrlRx = new(@"https?://[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Giphy share/embed/media links (Twitch's GIF picker sends these). Captures the GIF id.
        private static readonly Regex GiphyId = new(
            @"(?:giphy\.com/(?:gifs|clips|embed)/(?:[a-z0-9]+-)*|giphy\.com/media/|i\.giphy\.com/(?:media/)?)([A-Za-z0-9]{6,})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Return a direct animated-image URL for a link, or null if it isn't a GIF.
        private static string? GifUrlFor(string url)
        {
            var m = GiphyId.Match(url);
            if (m.Success) return $"https://media.giphy.com/media/{m.Groups[1].Value}/giphy.gif";
            var bare = url.Split('?')[0];
            if (bare.EndsWith(".gif", System.StringComparison.OrdinalIgnoreCase)) return url;   // any direct .gif
            return null;
        }

        // Pull GIF links out of text segments into their own Gif segments so they render
        // as images. Non-GIF links stay as text (normal clickable/plain links).
        public static List<ChatSegment> ExtractGifs(List<ChatSegment> segs)
        {
            var outSegs = new List<ChatSegment>();
            void addPlain(string t)
            {
                if (string.IsNullOrEmpty(t)) return;
                if (outSegs.Count > 0 && outSegs[^1].Kind == ChatSegmentKind.Text) outSegs[^1].Text += t;
                else outSegs.Add(ChatSegment.PlainText(t));
            }
            foreach (var seg in segs)
            {
                if (seg.Kind != ChatSegmentKind.Text || seg.Text.IndexOf("http", System.StringComparison.OrdinalIgnoreCase) < 0)
                { outSegs.Add(seg); continue; }
                var text = seg.Text; int last = 0; bool any = false;
                foreach (Match m in UrlRx.Matches(text))
                {
                    var gif = GifUrlFor(m.Value);
                    if (gif == null) continue;
                    any = true;
                    if (m.Index > last) addPlain(text.Substring(last, m.Index - last));
                    outSegs.Add(ChatSegment.Gif(gif));
                    last = m.Index + m.Length;
                }
                if (!any) { outSegs.Add(seg); continue; }
                if (last < text.Length) addPlain(text.Substring(last));
            }
            return outSegs;
        }

        public static List<ChatSegment> Parse(string? s)
        {
            var segs = new List<ChatSegment>();
            if (string.IsNullOrEmpty(s)) return segs;

            if (s.IndexOf('<') < 0) { AddText(segs, s); return ExtractGifs(segs); }

            int last = 0;
            foreach (Match m in Tag.Matches(s))
            {
                if (m.Index > last) AddText(segs, s.Substring(last, m.Index - last));

                var tag = m.Value;
                if (tag.StartsWith("<img", System.StringComparison.OrdinalIgnoreCase))
                {
                    var src = Attr(tag, "src");
                    var alt = Attr(tag, "alt");
                    if (string.IsNullOrEmpty(alt)) alt = Attr(tag, "title");
                    if (!string.IsNullOrEmpty(src))
                        segs.Add(ChatSegment.Emote(src, WebUtility.HtmlDecode(alt)));
                    else if (!string.IsNullOrEmpty(alt))
                        AddText(segs, alt);
                }
                // any other tag is dropped
                last = m.Index + m.Length;
            }
            if (last < s.Length) AddText(segs, s.Substring(last));
            return ExtractGifs(segs);
        }

        /// <summary>Flatten to plain text (emotes become their alt/name).</summary>
        public static string Plain(string? s) =>
            Whitespace.Replace(string.Concat(Parse(s).Select(x => x.Text)), " ").Trim();

        private static void AddText(List<ChatSegment> segs, string raw)
        {
            var t = WebUtility.HtmlDecode(raw);
            if (string.IsNullOrEmpty(t)) return;
            if (segs.Count > 0 && segs[^1].Kind == ChatSegmentKind.Text)
                segs[^1].Text += t;                 // merge adjacent text
            else
                segs.Add(ChatSegment.PlainText(t));
        }

        private static string Attr(string tag, string name)
        {
            var m = Regex.Match(tag, name + @"\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        /// <summary>
        /// Build segments for a Twitch IRC message using its "emotes" tag
        /// (id:start-end,.../id2:... with code-point offsets) so emotes render as images.
        /// </summary>
        public static List<ChatSegment> ParseTwitch(string? message, string? emotesTag)
        {
            var msg = message ?? string.Empty;
            if (string.IsNullOrEmpty(emotesTag)) return Parse(msg);

            var ranges = new List<(int start, int end, string id)>();
            foreach (var block in emotesTag.Split('/'))
            {
                int colon = block.IndexOf(':');
                if (colon <= 0) continue;
                var id = block.Substring(0, colon);
                foreach (var r in block.Substring(colon + 1).Split(','))
                {
                    int dash = r.IndexOf('-');
                    if (dash <= 0) continue;
                    if (int.TryParse(r.Substring(0, dash), out int s) &&
                        int.TryParse(r.Substring(dash + 1), out int e))
                        ranges.Add((s, e, id));
                }
            }
            if (ranges.Count == 0) return Parse(msg);
            ranges.Sort((a, b) => a.start.CompareTo(b.start));

            var cps = CodePoints(msg);                 // Twitch offsets are code-point based
            var segs = new List<ChatSegment>();
            int pos = 0;
            foreach (var (start, end, id) in ranges)
            {
                if (start < pos || start < 0 || end >= cps.Count || end < start) continue;
                if (start > pos) AddText(segs, Join(cps, pos, start - pos));
                segs.Add(ChatSegment.Emote(
                    $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/2.0",
                    Join(cps, start, end - start + 1)));
                pos = end + 1;
            }
            if (pos < cps.Count) AddText(segs, Join(cps, pos, cps.Count - pos));
            return ExtractGifs(segs);
        }

        private static List<string> CodePoints(string s)
        {
            var list = new List<string>(s.Length);
            for (int i = 0; i < s.Length;)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                { list.Add(s.Substring(i, 2)); i += 2; }
                else { list.Add(s[i].ToString()); i++; }
            }
            return list;
        }

        private static string Join(List<string> cps, int start, int count) =>
            string.Concat(cps.GetRange(start, count));
    }
}
