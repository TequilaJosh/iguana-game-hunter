using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Vorbis;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Plays streamer-defined sound alerts when their command appears in chat.
    /// Uses NAudio so it isn't limited to Windows Media Foundation codecs:
    ///   - .ogg            -> NAudio.Vorbis (managed Ogg Vorbis decoder)
    ///   - everything else -> NAudio AudioFileReader (wav/mp3/aiff natively;
    ///                        wma/m4a/aac/flac via Media Foundation)
    /// Output goes through WaveOutEvent, which needs no window or UI thread.
    /// </summary>
    public sealed class SoundService
    {
        private List<SoundAlert> _alerts = new();
        private readonly List<IWavePlayer> _active = new(); // keep players alive until they finish

        /// <summary>When true, incoming chat commands won't trigger sounds (panic/anti-spam).</summary>
        public bool Muted { get; set; }

        /// <summary>Immediately stop and dispose every currently-playing alert.</summary>
        public void StopAll()
        {
            foreach (var p in _active.ToList())
            {
                try { p.Stop(); } catch { }
                try { p.Dispose(); } catch { }
            }
            _active.Clear();
        }

        public void SetAlerts(IEnumerable<SoundAlert>? alerts) =>
            _alerts = (alerts ?? Enumerable.Empty<SoundAlert>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Command) && !string.IsNullOrWhiteSpace(a.FilePath))
                .ToList();

        /// <summary>If the message's first word matches an alert command, play it. Returns the command played.</summary>
        public string? CheckAndPlay(string? message)
        {
            if (Muted || string.IsNullOrWhiteSpace(message) || _alerts.Count == 0) return null;

            var first = message.TrimStart().Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
            var alert = _alerts.FirstOrDefault(a =>
                string.Equals(a.Command.Trim(), first, StringComparison.OrdinalIgnoreCase));
            if (alert == null) return null;

            Play(alert.FilePath);
            return alert.Command;
        }

        public void Play(string filePath) => Play(filePath, null);

        /// <summary>Play a sound. <paramref name="onError"/> (raised on the caller's context) reports failures.</summary>
        public void Play(string filePath, Action<string>? onError)
        {
            if (string.IsNullOrWhiteSpace(filePath)) { onError?.Invoke("No sound file set."); return; }
            if (!File.Exists(filePath)) { onError?.Invoke("File not found: " + filePath); return; }

            WaveStream reader;
            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                reader = ext == ".ogg"
                    ? new VorbisWaveReader(filePath)
                    : new AudioFileReader(filePath);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Couldn't open this file: " + ex.Message);
                return;
            }

            var output = new WaveOutEvent();
            void Cleanup()
            {
                try { output.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
                _active.Remove(output);
            }
            output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null) onError?.Invoke("Couldn't play: " + e.Exception.Message);
                Cleanup();
            };

            try
            {
                output.Init(reader);
                _active.Add(output);
                output.Play();
            }
            catch (Exception ex)
            {
                onError?.Invoke("Couldn't play: " + ex.Message);
                Cleanup();
            }
        }
    }
}
