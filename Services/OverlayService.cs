using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Writes the current "Now Playing" state to small files OBS can read:
    ///   - individual .txt files for OBS Text sources (game / elapsed / requester / status)
    ///   - now_playing.txt (combined one-liner)
    ///   - overlay.html (styled, transparent card) for an OBS Browser source
    /// Files update while a session is live and clear when idle.
    /// </summary>
    public static class OverlayService
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter", "overlay");

        public static string FolderPath => Folder;

        private static string _lastSignature = string.Empty;

        // Latest state, kept so the combined all.html can be re-rendered whenever
        // the game/session OR the chat updates (the two arrive on different triggers).
        private static bool _cLive;
        private static string _cTitle = string.Empty, _cElapsed = string.Empty, _cRequester = string.Empty;
        private static IReadOnlyList<string> _cChallenges = new List<string>();
        private static IReadOnlyList<ChatMessage> _cChat = new List<ChatMessage>();

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                // Always refresh the README so it reflects the current overlay files.
                File.WriteAllText(Path.Combine(Folder, "README.txt"), ReadmeText);
                Update(null); // write idle/empty files
            }
            catch { /* best-effort */ }
        }

        public static void Update(Game? active)
        {
            try
            {
                bool live = active != null && active.IsSessionActive;
                var session = active?.ActiveSession;

                string title = live ? active!.Title : string.Empty;
                string requester = live ? active!.Requester : string.Empty;
                string elapsed = live && session != null ? Format(session.Duration) : string.Empty;
                string status = live ? "Now Playing" : "Offline";
                var challenges = live ? active!.WheelResults : new List<string>();

                // Only rewrite when something changed (keeps idle quiet; ticks while live).
                var sig = $"{live}|{title}|{requester}|{elapsed}|{string.Join("␟", challenges)}";
                if (sig == _lastSignature) return;
                _lastSignature = sig;

                Directory.CreateDirectory(Folder);

                string combined = !live
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(requester)
                        ? $"▶ {title}  •  {elapsed}"
                        : $"▶ {title}  •  {elapsed}  •  req: {requester}";

                Write("game.txt", title);
                Write("requester.txt", requester);
                Write("elapsed.txt", elapsed);
                Write("status.txt", status);
                Write("now_playing.txt", combined);
                Write("overlay.html", BuildHtml(live, title, elapsed, requester));

                // Challenges (rolled from the game's wheel).
                Write("challenges.txt", string.Join(Environment.NewLine,
                    challenges.Select((c, i) => $"{i + 1}. {c}")));
                Write("challenges.html", BuildChallengesHtml(challenges));

                // Cache for the combined dashboard, then refresh it.
                _cLive = live; _cTitle = title; _cElapsed = elapsed; _cRequester = requester;
                _cChallenges = challenges.ToList();
                WriteCombined();
            }
            catch { /* best-effort */ }
        }

        private static void Write(string name, string content) =>
            File.WriteAllText(Path.Combine(Folder, name), content ?? string.Empty);

        private static string Format(TimeSpan t) =>
            $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

        private static string BuildHtml(bool live, string title, string elapsed, string requester)
        {
            string body;
            if (!live)
            {
                body = string.Empty; // transparent / nothing shows when offline
            }
            else
            {
                var reqHtml = string.IsNullOrWhiteSpace(requester)
                    ? string.Empty
                    : $"<span class=\"req\">req: {Enc(requester)}</span>";
                body =
                    "<div class=\"card\">" +
                    "<span class=\"dot\"></span>" +
                    $"<span class=\"title\">{Enc(title)}</span>" +
                    $"<span class=\"time\">{Enc(elapsed)}</span>" +
                    reqHtml +
                    "</div>";
            }

            return
"<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
"<meta http-equiv=\"refresh\" content=\"1\">" +
"<style>" +
"html,body{margin:0;padding:8px;background:transparent;overflow:hidden;" +
"font-family:'Segoe UI',Segoe,sans-serif;}" +
".card{display:inline-flex;align-items:center;gap:14px;padding:12px 18px;border-radius:12px;" +
"background:rgba(10,20,16,0.85);border:2px solid #4a7c3a;" +
"box-shadow:0 4px 18px rgba(0,0,0,0.5);}" +
".dot{width:13px;height:13px;border-radius:50%;background:#7cc44a;box-shadow:0 0 10px #7cc44a;}" +
".title{font-size:26px;font-weight:700;color:#e8e0c4;}" +
".time{font-family:Consolas,monospace;font-size:26px;font-weight:700;color:#7cc44a;}" +
".req{font-size:15px;font-weight:600;color:#d4a437;}" +
"</style></head><body>" + body + "</body></html>";
        }

        private static string BuildChallengesHtml(IReadOnlyList<string> challenges)
        {
            string body;
            if (challenges.Count == 0)
            {
                body = string.Empty; // transparent when there are no active challenges
            }
            else
            {
                var rows = string.Concat(challenges.Select((c, i) =>
                    $"<div class=\"row\"><span class=\"num\">{i + 1}</span>" +
                    $"<span class=\"txt\">{Enc(c)}</span></div>"));
                body =
                    "<div class=\"card\">" +
                    "<div class=\"head\">\U0001F3A1 CHALLENGES</div>" +
                    rows +
                    "</div>";
            }

            return
"<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
"<meta http-equiv=\"refresh\" content=\"1\">" +
"<style>" +
"html,body{margin:0;padding:8px;background:transparent;overflow:hidden;" +
"font-family:'Segoe UI',Segoe,sans-serif;}" +
".card{display:inline-block;min-width:240px;padding:12px 16px;border-radius:12px;" +
"background:rgba(10,20,16,0.85);border:2px solid #4a7c3a;box-shadow:0 4px 18px rgba(0,0,0,0.5);}" +
".head{font-size:15px;font-weight:700;color:#d4a437;letter-spacing:1px;margin-bottom:8px;}" +
".row{display:flex;align-items:flex-start;margin:5px 0;}" +
".num{min-width:22px;height:22px;border-radius:50%;background:#243a26;color:#7cc44a;" +
"font-weight:700;font-size:13px;text-align:center;line-height:22px;margin-right:10px;}" +
".txt{color:#e8e0c4;font-size:18px;font-weight:600;line-height:22px;}" +
"</style></head><body>" + body + "</body></html>";
        }

        private static string Enc(string s) => (s ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // ---------- Live chat overlay ----------

        // Plain glyphs (not emoji) so we can color them ourselves — WPF renders emoji monochrome.
        /// <summary>Per-platform symbol glyph (coloured via ChatColorHex).</summary>
        public static string ChatSymbol(string platform) => (platform ?? string.Empty).ToLowerInvariant() switch
        {
            "twitch" => "♥",   // ♥
            "youtube" => "▶",  // ▶
            "tiktok" => "♪",   // ♪
            "restream" => "⟳", // ⟳
            _ => "●",          // ●
        };

        /// <summary>Per-platform brand colour for the symbol.</summary>
        public static string ChatColorHex(string platform) => (platform ?? string.Empty).ToLowerInvariant() switch
        {
            "twitch" => "#9146FF",
            "youtube" => "#FF0000",
            "tiktok" => "#25F4EE",
            "kick" => "#53FC18",
            "facebook" => "#1877F2",
            "restream" => "#1f6feb",
            _ => "#b6e08a",
        };

        private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

        /// <summary>Write the recent chat messages to chat.html (and refresh the combined all.html).</summary>
        public static void WriteChatHtml(IReadOnlyList<ChatMessage> messages)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                _cChat = messages;
                File.WriteAllText(Path.Combine(Folder, "chat.html"), ChatHtml(ChatRowsHtml(messages)));
                WriteCombined();
            }
            catch { /* best-effort */ }
        }

        public static void ClearChatHtml()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                _cChat = new List<ChatMessage>();
                File.WriteAllText(Path.Combine(Folder, "chat.html"), ChatHtml(string.Empty));
                WriteCombined();
            }
            catch { /* best-effort */ }
        }

        private static string ChatRowsHtml(IReadOnlyList<ChatMessage> messages)
        {
            var rows = new StringBuilder();
            foreach (var m in messages)
            {
                var color = HexColor.IsMatch(m.UserColor ?? string.Empty) ? m.UserColor : "#b6e08a";

                var body = new StringBuilder();
                foreach (var seg in m.Segments)
                {
                    if (seg.Kind == ChatSegmentKind.Emote && !string.IsNullOrEmpty(seg.Url))
                        body.Append($"<img class=\"emote\" src=\"{Enc(seg.Url)}\" alt=\"{Enc(seg.Text)}\">");
                    else
                        body.Append(Enc(seg.Text));
                }

                rows.Append(
                    "<div class=\"row\">" +
                    $"<span class=\"sym\" style=\"color:{ChatColorHex(m.Platform)}\">{ChatSymbol(m.Platform)}</span>" +
                    $"<span class=\"user\" style=\"color:{color}\">{Enc(m.User)}</span>" +
                    $"<span class=\"msg\">{body}</span></div>");
            }
            return rows.ToString();
        }

        // Render the overlay at a fixed design width and uniformly scale it to the OBS
        // source size, so resizing in OBS zooms cleanly instead of reflowing/smooshing.
        private const string ScaleScript =
"<script>(function(){var W=360;function f(){var v=document.getElementById('vp');if(!v)return;" +
"var s=window.innerWidth/W;v.style.transformOrigin='top left';v.style.transform='scale('+s+')';" +
"v.style.width=W+'px';v.style.height=(window.innerHeight/s)+'px';}f();" +
"window.addEventListener('resize',f);})();</script>";

        private static string ChatHtml(string rows) =>
"<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
"<meta http-equiv=\"refresh\" content=\"1\">" +
"<style>" +
"html,body{margin:0;padding:0;background:transparent;overflow:hidden;" +
"font-family:'Segoe UI',Segoe,sans-serif;}" +
"#vp{position:absolute;top:0;left:0;}" +
".box{position:absolute;inset:8px;background:rgba(10,20,16,0.92);border:2px solid #4a7c3a;" +
"border-radius:12px;box-shadow:0 6px 22px rgba(0,0,0,0.55);box-sizing:border-box;" +
"display:flex;flex-direction:column;overflow:hidden;}" +
".head{padding:8px 14px;color:#7cc44a;font-size:13px;font-weight:700;letter-spacing:1px;" +
"border-bottom:1px solid #2e4a30;flex:0 0 auto;}" +
".wrap{flex:1 1 auto;display:flex;flex-direction:column;justify-content:flex-end;" +
"padding:8px 14px;overflow:hidden;}" +
".row{margin:3px 0;font-size:20px;line-height:1.3;}" +
".sym{margin-right:6px;font-weight:bold;}" +
".user{font-weight:700;margin-right:5px;}" +
".msg{color:#ffffff;}" +
".emote{height:1.2em;vertical-align:middle;margin:0 1px;}" +
"</style></head><body><div id=\"vp\">" +
"<div class=\"box\"><div class=\"head\">● LIVE CHAT</div>" +
"<div class=\"wrap\">" + rows + "</div></div></div>" + ScaleScript + "</body></html>";

        // ---------- Combined dashboard (all.html) ----------

        private static void WriteCombined()
        {
            try { File.WriteAllText(Path.Combine(Folder, "all.html"), CombinedHtml()); }
            catch { /* best-effort */ }
        }

        /// <summary>One panel with Now Playing + timer, Challenges and Live Chat — a single OBS source.</summary>
        private static string CombinedHtml()
        {
            var sb = new StringBuilder();

            if (_cLive)
            {
                var req = string.IsNullOrWhiteSpace(_cRequester)
                    ? string.Empty : $"<span class=\"req\">req: {Enc(_cRequester)}</span>";
                sb.Append(
                    "<div class=\"sec\"><div class=\"hd\">● NOW PLAYING</div>" +
                    "<div class=\"np\"><span class=\"dot\"></span>" +
                    $"<span class=\"title\">{Enc(_cTitle)}</span>" +
                    $"<span class=\"time\">{Enc(_cElapsed)}</span>{req}</div></div>");
            }

            if (_cChallenges.Count > 0)
            {
                var rows = new StringBuilder();
                for (int i = 0; i < _cChallenges.Count; i++)
                    rows.Append($"<div class=\"ch-row\"><span class=\"num\">{i + 1}</span>" +
                                $"<span class=\"ch-txt\">{Enc(_cChallenges[i])}</span></div>");
                sb.Append("<div class=\"sec chlist\"><div class=\"hd\">\U0001F3A1 CHALLENGES</div>" + rows + "</div>");
            }

            sb.Append("<div class=\"sec chat\"><div class=\"hd\">● LIVE CHAT</div>" +
                      "<div class=\"wrap\">" + ChatRowsHtml(_cChat) + "</div></div>");

            return
"<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
"<meta http-equiv=\"refresh\" content=\"1\">" +
"<style>" +
"html,body{margin:0;padding:0;background:transparent;overflow:hidden;font-family:'Segoe UI',Segoe,sans-serif;}" +
"#vp{position:absolute;top:0;left:0;}" +
".panel{position:absolute;inset:8px;display:flex;flex-direction:column;" +
"background:rgba(10,20,16,0.92);border:2px solid #4a7c3a;border-radius:12px;" +
"box-shadow:0 6px 22px rgba(0,0,0,0.55);box-sizing:border-box;overflow:hidden;}" +
".sec{padding:9px 14px;border-bottom:1px solid #2e4a30;flex:0 0 auto;}" +
".sec.chlist{max-height:34%;overflow:hidden;}" +
".sec.chat{flex:1 1 auto;display:flex;flex-direction:column;border-bottom:0;min-height:0;}" +
".hd{color:#7cc44a;font-size:12px;font-weight:700;letter-spacing:1px;margin-bottom:6px;}" +
".np{display:flex;align-items:center;flex-wrap:wrap;gap:10px;}" +
".dot{width:11px;height:11px;border-radius:50%;background:#7cc44a;box-shadow:0 0 8px #7cc44a;flex:0 0 auto;}" +
".np .title{font-size:20px;font-weight:700;color:#e8e0c4;}" +
".np .time{font-family:Consolas,monospace;font-size:20px;font-weight:700;color:#7cc44a;margin-left:auto;}" +
".np .req{font-size:13px;font-weight:600;color:#d4a437;flex-basis:100%;}" +
".ch-row{display:flex;align-items:flex-start;margin:3px 0;}" +
".num{min-width:20px;height:20px;border-radius:50%;background:#243a26;color:#7cc44a;font-weight:700;" +
"font-size:12px;text-align:center;line-height:20px;margin-right:9px;flex:0 0 auto;}" +
".ch-txt{color:#e8e0c4;font-size:15px;line-height:20px;}" +
".chat .wrap{flex:1 1 auto;display:flex;flex-direction:column;justify-content:flex-end;overflow:hidden;padding-top:2px;}" +
".row{margin:3px 0;font-size:17px;line-height:1.3;}" +
".sym{margin-right:6px;font-weight:bold;}" +
".user{font-weight:700;margin-right:5px;}" +
".msg{color:#ffffff;}" +
".emote{height:1.2em;vertical-align:middle;margin:0 1px;}" +
"</style></head><body><div id=\"vp\"><div class=\"panel\">" + sb + "</div></div>" + ScaleScript + "</body></html>";
        }

        private const string ReadmeText =
@"LazerGuanas Game Hunter - Stream Overlay files
================================================

These files update automatically while a play session is running, and clear
when no session is active.

OBS - Text sources (simplest):
  Add a Text (GDI+) source -> check ""Read from file"" -> point it at one of:
    game.txt        - current game title
    elapsed.txt     - session time (H:MM:SS)
    requester.txt   - who requested the game
    status.txt      - ""Now Playing"" / ""Offline""
    now_playing.txt - combined one-liner

OBS - Browser source (styled card):
  Add a Browser source -> check ""Local file"" -> select overlay.html
  Set width ~600, height ~120. It has a transparent background and shows
  nothing while offline.

Challenges (from a game's custom wheel):
    challenges.txt  - the rolled challenges, numbered, one per line
    challenges.html - a styled Browser-source card listing current challenges
  Open a game's wheel (right-click a card -> Wheel) and spin to add challenges.
  They appear here for whichever game's session is currently running.

Live chat:
    chat.html - a styled Browser-source overlay of recent chat, with a per-platform
                symbol, the viewer's colored name, and real emote images.
  Open the Chat window in the app and connect a source (Twitch / Social Stream Ninja /
  Restream). Keep that window open while streaming; this clears when you close it.

All-in-one panel (easiest):
    all.html - a single Browser source that shows Now Playing + timer, the current
               Challenges, and Live Chat together in one themed panel. Add ONE Browser
               source -> ""Local file"" -> all.html (try ~360 wide, ~700 tall).
               Sections appear only when they have content; chat fills the rest.

Start a session in the app (the Start button on a game card) and these update live.
";
    }
}
