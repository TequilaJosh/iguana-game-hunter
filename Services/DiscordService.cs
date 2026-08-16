using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GameTracker.Services
{
    /// <summary>Posts messages to a Discord channel via its webhook URL (no bot/login needed).</summary>
    public static class DiscordService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static bool LooksLikeWebhook(string? url)
        {
            url = (url ?? string.Empty).Trim();
            return url.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Send a structured clip event to the companion bot's ingest endpoint,
        /// authenticated with the shared token. Returns false on any failure.
        /// </summary>
        public static async Task<bool> PostClipToBotAsync(string? ingestUrl, string? token, object payload)
        {
            ingestUrl = (ingestUrl ?? string.Empty).Trim();
            token = (token ?? string.Empty).Trim();
            if (ingestUrl.Length == 0 || token.Length == 0) return false;
            if (!ingestUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !ingestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, ingestUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                var resp = await _http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>
        /// Forward a chat "!" game command to the bot's /game bridge and return its reply
        /// text (or null on failure). Used for the cross-platform Tavern Tales play.
        /// </summary>
        public static async Task<string?> PlayGameAsync(string? ingestUrl, string? token, string platform, string user, string text)
        {
            ingestUrl = (ingestUrl ?? string.Empty).Trim();
            token = (token ?? string.Empty).Trim();
            if (ingestUrl.Length == 0 || token.Length == 0) return null;
            if (!ingestUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

            try
            {
                var gameUrl = new Uri(new Uri(ingestUrl), "/game");
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { platform, user, text });
                using var req = new HttpRequestMessage(HttpMethod.Post, gameUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                var body = await resp.Content.ReadAsStringAsync();
                return Newtonsoft.Json.Linq.JObject.Parse(body).Value<string?>("reply");
            }
            catch { return null; }
        }

        /// <summary>
        /// Post a stream recap to the companion bot's /recap endpoint. The bot renders it as an
        /// embed in the server's tavern/clips channel. Returns false on any failure.
        /// </summary>
        public static async Task<bool> PostRecapToBotAsync(string? ingestUrl, string? token, string recap)
        {
            ingestUrl = (ingestUrl ?? string.Empty).Trim();
            token = (token ?? string.Empty).Trim();
            recap = (recap ?? string.Empty).Trim();
            if (ingestUrl.Length == 0 || token.Length == 0 || recap.Length == 0) return false;
            if (!ingestUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                var recapUrl = new Uri(new Uri(ingestUrl), "/recap");
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { recap });
                using var req = new HttpRequestMessage(HttpMethod.Post, recapUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                var resp = await _http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public enum BotTest { NotConfigured, Unreachable, BadToken, NoClipChannel, Ok }

        /// <summary>
        /// Check the companion bot without posting anything: is it reachable, is the token
        /// valid, and does the server have a clips channel set. Powers the "Test bot" button.
        /// </summary>
        public static async Task<BotTest> VerifyBotAsync(string? ingestUrl, string? token)
        {
            ingestUrl = (ingestUrl ?? string.Empty).Trim();
            token = (token ?? string.Empty).Trim();
            if (ingestUrl.Length == 0 || token.Length == 0) return BotTest.NotConfigured;
            if (!ingestUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return BotTest.NotConfigured;

            try
            {
                var verifyUrl = new Uri(new Uri(ingestUrl), "/verify");   // /clip -> /verify on the same host
                using var req = new HttpRequestMessage(HttpMethod.Post, verifyUrl)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                var resp = await _http.SendAsync(req);
                if ((int)resp.StatusCode == 401) return BotTest.BadToken;
                if (!resp.IsSuccessStatusCode) return BotTest.Unreachable;

                var body = await resp.Content.ReadAsStringAsync();
                bool hasChannel = false;
                try { hasChannel = Newtonsoft.Json.Linq.JObject.Parse(body).Value<bool?>("clipChannel") ?? false; }
                catch { /* treat unparseable as no channel */ }
                return hasChannel ? BotTest.Ok : BotTest.NoClipChannel;
            }
            catch { return BotTest.Unreachable; }
        }

        /// <summary>Fire-and-forget post; returns false if the URL is unusable or the send fails.</summary>
        public static async Task<bool> PostAsync(string? webhookUrl, string content)
        {
            webhookUrl = (webhookUrl ?? string.Empty).Trim();
            content = (content ?? string.Empty).Trim();
            if (!LooksLikeWebhook(webhookUrl) || content.Length == 0) return false;
            if (content.Length > 1900) content = content[..1900];   // Discord caps at 2000

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { content });
                using var body = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync(webhookUrl, body);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
