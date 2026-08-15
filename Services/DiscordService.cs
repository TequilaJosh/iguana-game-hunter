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
