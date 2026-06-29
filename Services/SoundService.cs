using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Plays streamer-defined sound alerts when their command appears in chat.
    /// MediaPlayer handles both .mp3 and .wav. Must be used on the UI thread.
    /// </summary>
    public sealed class SoundService
    {
        private List<SoundAlert> _alerts = new();
        private readonly List<MediaPlayer> _active = new(); // keep players alive until they finish

        public void SetAlerts(IEnumerable<SoundAlert>? alerts) =>
            _alerts = (alerts ?? Enumerable.Empty<SoundAlert>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Command) && !string.IsNullOrWhiteSpace(a.FilePath))
                .ToList();

        /// <summary>If the message's first word matches an alert command, play it. Returns the command played.</summary>
        public string? CheckAndPlay(string? message)
        {
            if (string.IsNullOrWhiteSpace(message) || _alerts.Count == 0) return null;

            var first = message.TrimStart().Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
            var alert = _alerts.FirstOrDefault(a =>
                string.Equals(a.Command.Trim(), first, StringComparison.OrdinalIgnoreCase));
            if (alert == null) return null;

            Play(alert.FilePath);
            return alert.Command;
        }

        public void Play(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

                var player = new MediaPlayer();
                void Done(object? s, EventArgs e)
                {
                    try { player.Close(); } catch { }
                    _active.Remove(player);
                }
                player.MediaEnded += Done;
                player.MediaFailed += (s, e) => Done(s, e);

                _active.Add(player);
                player.Open(new Uri(filePath));
                player.Play();
            }
            catch { /* best-effort */ }
        }
    }
}
