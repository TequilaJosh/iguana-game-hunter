using System;
using System.IO;
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

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var readme = Path.Combine(Folder, "README.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme, ReadmeText);
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

                // Only rewrite when something changed (keeps idle quiet; ticks while live).
                var sig = $"{live}|{title}|{requester}|{elapsed}";
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

        private static string Enc(string s) => (s ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

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

Start a session in the app (the Start button on a game card) and these update live.
";
    }
}
