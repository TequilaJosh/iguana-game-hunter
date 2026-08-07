using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Speech.Synthesis;

namespace GameTracker.Services
{
    /// <summary>A selectable voice = a base engine voice plus a pitch shift.</summary>
    public sealed class VoiceProfile
    {
        public string Label { get; set; } = string.Empty;   // e.g. "David (deep)"
        public string Voice { get; set; } = string.Empty;   // installed engine voice name
        public int Pitch { get; set; }                       // -2..2 (x-low .. x-high)
    }

    /// <summary>
    /// Offline text-to-speech via the Windows speech engine. Since Windows exposes only a
    /// couple of base voices, we expand them into many by pitch-shifting (SSML). Utterances
    /// play one at a time (correct voice/pitch each), and flood messages are dropped.
    /// </summary>
    public sealed class TtsService : IDisposable
    {
        private readonly SpeechSynthesizer _synth = new();
        private readonly Queue<Item> _q = new();
        private readonly object _gate = new();
        private bool _speaking;

        public int MaxBacklog { get; set; } = 5;

        private readonly record struct Item(string Text, string Voice, int Pitch, int Rate, int Volume);

        public TtsService()
        {
            try { _synth.SetOutputToDefaultAudioDevice(); } catch { /* no audio device */ }
            _synth.SpeakCompleted += (_, _) => { lock (_gate) { _speaking = false; } Pump(); };
        }

        public static List<string> InstalledVoices()
        {
            try
            {
                using var s = new SpeechSynthesizer();
                return s.GetInstalledVoices().Where(v => v.Enabled)
                        .Select(v => v.VoiceInfo.Name).ToList();
            }
            catch { return new List<string>(); }
        }

        // A palette of voices: each installed voice at five pitch steps.
        private static readonly (int val, string tag)[] Steps =
        {
            (-2, "very deep"), (-1, "deep"), (0, ""), (1, "bright"), (2, "high"),
        };

        public static List<VoiceProfile> BuildProfiles()
        {
            var list = new List<VoiceProfile>();
            foreach (var v in InstalledVoices())
            {
                var shortName = v.Replace("Microsoft ", "").Replace(" Desktop", "").Trim();
                foreach (var (val, tag) in Steps)
                    list.Add(new VoiceProfile
                    {
                        Voice = v,
                        Pitch = val,
                        Label = tag.Length == 0 ? shortName : $"{shortName} ({tag})",
                    });
            }
            return list;
        }

        public void Speak(string text, string? voice, int pitch, int rate, int volume)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_gate)
            {
                if (_q.Count >= MaxBacklog) return;   // drop when flooded
                _q.Enqueue(new Item(text, voice ?? string.Empty, pitch, rate, volume));
            }
            Pump();
        }

        private void Pump()
        {
            Item it;
            lock (_gate)
            {
                if (_speaking || _q.Count == 0) return;
                it = _q.Dequeue();
                _speaking = true;
            }
            try
            {
                if (!string.IsNullOrEmpty(it.Voice)) { try { _synth.SelectVoice(it.Voice); } catch { } }
                _synth.Rate = Math.Clamp(it.Rate, -10, 10);
                _synth.Volume = Math.Clamp(it.Volume, 0, 100);

                var pitch = PitchName(it.Pitch);
                if (pitch != null) _synth.SpeakSsmlAsync(Ssml(it.Text, pitch));
                else _synth.SpeakAsync(it.Text);
            }
            catch { lock (_gate) { _speaking = false; } }
        }

        private static string? PitchName(int p) => p switch
        {
            <= -2 => "x-low",
            -1 => "low",
            1 => "high",
            >= 2 => "x-high",
            _ => null,   // 0 = normal, no SSML needed
        };

        private static string Ssml(string text, string pitch)
        {
            var esc = SecurityElement.Escape(text) ?? text;
            return "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">" +
                   $"<prosody pitch=\"{pitch}\">{esc}</prosody></speak>";
        }

        public void StopAll()
        {
            lock (_gate) { _q.Clear(); }
            try { _synth.SpeakAsyncCancelAll(); } catch { }
            lock (_gate) { _speaking = false; }
        }

        public void Dispose()
        {
            try { _synth.SpeakAsyncCancelAll(); } catch { }
            try { _synth.Dispose(); } catch { }
        }
    }
}
