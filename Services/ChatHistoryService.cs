using System;
using System.Collections.Generic;
using System.IO;
using GameTracker.Models;
using Newtonsoft.Json;

namespace GameTracker.Services
{
    /// <summary>The live chat at the moment the app restarted to apply an update.</summary>
    public class ChatSnapshot
    {
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
        public bool WasConnected { get; set; }                       // reconnect after the restart
        public List<ChatMessage> Messages { get; set; } = new();     // oldest first
    }

    /// <summary>
    /// Carries the chat window's messages across an update restart, so the chat (and the
    /// overlay's chat box) come back showing what was there instead of starting empty.
    /// One-shot, like <see cref="WindowStateService"/>: written just before the update
    /// restart, consumed (and deleted) on the next startup.
    /// </summary>
    public static class ChatHistoryService
    {
        // A snapshot older than this is from an update that never completed — don't resurrect it.
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

        private static readonly string File_ = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazerGuanas Game Hunter", "chat-restore.json");

        public static void Save(ChatSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                File.WriteAllText(File_, JsonConvert.SerializeObject(snapshot));
            }
            catch { /* best-effort — an update must never fail over chat history */ }
        }

        /// <summary>The saved snapshot (deleting the file), or null if there's none or it's stale.</summary>
        public static ChatSnapshot? LoadAndClear()
        {
            try
            {
                if (!File.Exists(File_)) return null;
                var json = File.ReadAllText(File_);
                File.Delete(File_);
                var snap = JsonConvert.DeserializeObject<ChatSnapshot>(json);
                if (snap == null || DateTime.UtcNow - snap.SavedUtc > MaxAge) return null;
                return snap;
            }
            catch { return null; }
        }
    }
}
