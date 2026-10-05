using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NWaves.Effects;
using NWaves.Filters.Base;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Live streamer voice morph. Runs the mic through an NWaves DSP chain continuously
    /// (dry passthrough when no morph is active) and plays it to a chosen output device.
    /// Capture it in OBS with an "Application Audio Capture" source.
    /// Redeems activate a preset for its TimerSeconds; several redeems stack (their effects
    /// combine, the stronger winning where they share one), each dropping out when its own
    /// timer ends. The overlay counts down to when the voice is fully back to dry.
    /// </summary>
    public static class VoiceMorphService
    {
        private static readonly object Gate = new();
        private static WasapiCapture? _capture;
        private static WasapiOut? _out;
        private static BufferedWaveProvider? _buf;
        private static int _channels = 1;
        private static volatile Chain? _chain;      // null = dry passthrough
        private static Timer? _revert;
        private static string _activeName = string.Empty;

        // Keep-alive: the mic chain should behave like a permanent microphone, so a watchdog
        // restarts it whenever the audio path dies (device unplugged, VoiceMeeter/driver
        // restarted, default device switched, the stream stalls). _desired = the user wants
        // it running; _faulted = an unexpected stop needs recovery; _lastDataUtc = last time
        // capture delivered a buffer (silence still counts — WASAPI delivers during silence).
        private static volatile bool _desired;
        private static volatile bool _faulted;
        private static DateTime _lastDataUtc = DateTime.MinValue;
        private static DateTime _lastStartUtc = DateTime.MinValue;
        private static Timer? _watchdog;
        private static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(5);

        public static bool IsRunning { get; private set; }
        public static string LastError { get; private set; } = string.Empty;
        public static string ActiveMorph => _activeName;

        /// <summary>Sentinel output meaning "process but play nothing back to the streamer".</summary>
        public const string NoneOutput = "(none)";

        /// <summary>One fader in the Voice Mixer. Bidir faders also do something to the LEFT
        /// (an "opposite" effect); Left/Right name the two directions for the readout.</summary>
        public sealed record MixBar(string Key, string Label, bool Bidir = false,
                                    string Left = "", string Right = "");

        /// <summary>The mixer's effect faders, in the order they're shown and processed.</summary>
        // Order is both the display order and the processing order (tone first, space last).
        public static readonly MixBar[] MixBars =
        {
            new("tone",   "Tone", Bidir: true, Left: "warm", Right: "grit"),
            new("robot",  "Robot"),
            new("wobble", "Wobble"),
            new("chorus", "Chorus (shimmer)"),
            new("reverb", "Reverb (space)"),
            new("echo",   "Echo"),
        };

        // Right-of-centre (positive) effect at full strength; the WetDry wrapper scales it.
        private static IOnlineFilter? MakeEffect(string key, int sr) => key switch
        {
            "reverb" => new ReverbFilter(sr, roomSize: 0.90f, damp: 0.20f, wet: 0.90f),
            "echo"   => new EchoEffect(sr, 0.28f, 0.40f),
            "chorus" => new ChorusEffect(sr, new[] { 0.6f, 1.1f }, new[] { 0.002f, 0.0025f }),
            "tone"   => new DistortionEffect(DistortionMode.SoftClipping, 18),   // grit / bright
            "robot"  => new RobotEffect(hopSize: 128, fftSize: 512),
            "wobble" => new TremoloEffect(sr, 0.7f, 6),
            _        => null,
        };

        // Left-of-centre (negative) "opposite" effect for bidirectional faders.
        private static IOnlineFilter? MakeEffectInverse(string key, int sr) => key switch
        {
            "tone" => new OnePoleLowpassFilter(sr, 1100f),   // warm / mellow — the opposite of grit
            _      => null,
        };

        private sealed class Chain
        {
            public PitchShiftVocoderEffect? Pitch;
            public IOnlineFilter[]? Fx;      // effects applied in order (some voices layer several)
            public float Process(float s)
            {
                if (Pitch != null) s = Pitch.Process(s);
                if (Fx != null) foreach (var f in Fx) s = f.Process(s);
                return s;
            }
        }

        // ---- devices ----

        public static List<string> InputDevices()
        {
            try
            {
                using var e = new MMDeviceEnumerator();
                return e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                        .Select(d => d.FriendlyName).ToList();
            }
            catch { return new List<string>(); }
        }

        public static List<string> OutputDevices()
        {
            try
            {
                using var e = new MMDeviceEnumerator();
                return e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                        .Select(d => d.FriendlyName).ToList();
            }
            catch { return new List<string>(); }
        }

        private static MMDevice? Find(DataFlow flow, string name)
        {
            var e = new MMDeviceEnumerator();
            var all = e.EnumerateAudioEndPoints(flow, DeviceState.Active);
            var match = all.FirstOrDefault(d => d.FriendlyName == name);
            if (match != null) return match;
            try
            {
                return flow == DataFlow.Capture
                    ? e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch { return null; }
        }

        // ---- engine ----

        /// <summary>Start the always-on mic chain from the saved settings (and keep it alive).</summary>
        public static bool Start()
        {
            var s = SettingsService.LoadMorph();
            _desired = s.Enabled;
            EnsureWatchdog();               // watchdog runs for the app's lifetime once started
            if (!s.Enabled) { Stop(); return false; }
            return StartInternal(s);
        }

        // The watchdog keeps the pipeline running like a real microphone: while the user wants
        // it on, restart whenever it isn't running, faulted, or the capture stream has stalled.
        private static void EnsureWatchdog()
        {
            if (_watchdog != null) return;
            _watchdog = new Timer(_ => WatchdogTick(), null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        private static void WatchdogTick()
        {
            if (!_desired) return;
            bool stalled = IsRunning && DateTime.UtcNow - _lastDataUtc > StallAfter;
            if (!IsRunning || _faulted || stalled)
            {
                if (DateTime.UtcNow - _lastStartUtc < TimeSpan.FromSeconds(2)) return; // don't hammer
                try { StartInternal(SettingsService.LoadMorph()); } catch { /* try again next tick */ }
            }
        }

        // Bring the device chain up (tearing down any previous one first). Safe to call
        // repeatedly — this is what both Start() and the watchdog use.
        private static bool StartInternal(MorphSettings s)
        {
            lock (Gate)
            {
                StopCore();
                _lastStartUtc = DateTime.UtcNow;
                try
                {
                    bool silent = s.OutputDevice == NoneOutput;
                    var mic = Find(DataFlow.Capture, s.InputDevice);
                    var spk = silent ? null : Find(DataFlow.Render, s.OutputDevice);
                    if (mic == null || (!silent && spk == null)) { LastError = "No audio device found."; return false; }

                    _capture = new WasapiCapture(mic, true, 20);
                    _channels = _capture.WaveFormat.Channels;
                    int sr = _capture.WaveFormat.SampleRate;

                    if (!silent)
                    {
                        _buf = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sr, 1))
                        {
                            DiscardOnBufferOverflow = true,
                            BufferDuration = TimeSpan.FromSeconds(2),
                        };
                        _out = new WasapiOut(spk, AudioClientShareMode.Shared, true, 60);
                        _out.PlaybackStopped += OnPlaybackStopped;
                        _out.Init(_buf);
                        _out.Play();
                    }
                    // silent: _buf stays null; OnAudio still runs the chain but discards output.

                    _capture.DataAvailable += OnAudio;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capture.StartRecording();

                    _lastDataUtc = DateTime.UtcNow;
                    _faulted = false;
                    IsRunning = true;
                    LastError = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    StopCore();
                    return false;
                }
            }
        }

        // Unexpected stops (device removed, driver restart, format change) flag a fault so the
        // watchdog rebuilds the chain. Our own teardown unsubscribes first, so these only fire
        // for genuine failures, not for StopCore().
        private static void OnPlaybackStopped(object? sender, StoppedEventArgs e) { if (_desired) _faulted = true; }
        private static void OnRecordingStopped(object? sender, StoppedEventArgs e) { if (_desired) _faulted = true; }

        private static void OnAudio(object? sender, WaveInEventArgs e)
        {
            _lastDataUtc = DateTime.UtcNow;   // liveness — updated even in silent mode
            var chain = _chain;
            var buf = _buf;
            if (buf == null) return;
            try
            {
                // Incoming is IEEE float (WASAPI mix format). Downmix to mono, run the chain.
                var wb = new WaveBuffer(e.Buffer);
                int samples = e.BytesRecorded / 4;
                int frames = samples / Math.Max(1, _channels);
                var outBytes = new byte[frames * 4];
                var outWb = new WaveBuffer(outBytes);
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int c = 0; c < _channels; c++) sum += wb.FloatBuffer[f * _channels + c];
                    float s = sum / _channels;
                    if (chain != null) s = chain.Process(s);
                    outWb.FloatBuffer[f] = Math.Clamp(s, -1f, 1f);
                }
                buf.AddSamples(outBytes, 0, outBytes.Length);
            }
            catch { /* keep the stream alive */ }
        }

        /// <summary>User-initiated stop: the mic chain should stay down (watchdog won't revive it).</summary>
        public static void Stop()
        {
            _desired = false;
            lock (Gate) StopCore();
        }

        // Tear down the device chain. Unsubscribes the fault handlers FIRST so our own stop
        // doesn't look like a failure to the watchdog. Does not change _desired.
        private static void StopCore()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnAudio;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.StopRecording();
                    _capture.Dispose();
                }
            }
            catch { }
            try
            {
                if (_out != null)
                {
                    _out.PlaybackStopped -= OnPlaybackStopped;
                    _out.Dispose();
                }
            }
            catch { }
            _capture = null; _out = null; _buf = null;
            IsRunning = false;
            ClearMorph(broadcast: false);
        }

        // ---- morph activation ----

        /// <summary>The preset's full effect chain (pitch + effects) as a flat filter list, so a
        /// TTS preview can run synthesized speech through the exact same modulation.</summary>
        public static IOnlineFilter[] BuildFilters(MorphPreset p, int sampleRate)
        {
            var chain = BuildChain(p, sampleRate);
            var list = new List<IOnlineFilter>();
            if (chain.Pitch != null) list.Add(chain.Pitch);
            if (chain.Fx != null) list.AddRange(chain.Fx);
            return list.ToArray();
        }

        // ---- effect stages (what lets several voices combine) ----

        /// <summary>One effect in a voice: which effect it is (<paramref name="Key"/>), how strong
        /// (<paramref name="Amount"/>, -1..1 — only the bidirectional "tone" uses the sign), and how
        /// to build its filter. When voices stack, each Key is used once, from the strongest voice.</summary>
        private sealed record Stage(string Key, float Amount, Func<int, IOnlineFilter> Make);

        // Processing order for combined chains: tone → spectral → modulation → space. Every
        // single voice's own effects already run in this relative order, so a voice on its own
        // sounds exactly as it did before stacking existed.
        private static readonly string[] StageOrder =
            { "tone", "robot", "whisper", "autowah", "flanger", "vibrato", "wobble", "chorus", "reverb", "echo" };

        /// <summary>A preset broken into keyed effect stages.</summary>
        private static List<Stage> PresetStages(MorphPreset p)
        {
            var list = new List<Stage>();

            // Mixer voices: every off-centre fader. Positive = the effect; negative
            // (bidirectional faders only) = its opposite.
            if (p.Mix != null && p.Mix.Values.Any(v => v != 0))
            {
                foreach (var bar in MixBars)
                {
                    if (!p.Mix.TryGetValue(bar.Key, out var amt) || amt == 0) continue;
                    var key = bar.Key;
                    float wet = Math.Clamp(Math.Abs(amt), 0, 100) / 100f;
                    if (amt > 0)
                        list.Add(new Stage(key, wet, sr => new WetDryFilter(MakeEffect(key, sr)!, wet)));
                    else if (bar.Bidir)
                        list.Add(new Stage(key, -wet, sr => new WetDryFilter(MakeEffectInverse(key, sr)!, wet)));
                }
                return list;
            }

            // Legacy single-effect voices (the Voice Redeems editor and older saved presets),
            // mapped onto the mixer's effect keys where they're the same kind of effect.
            switch (p.Effect)
            {
                case "robot":      list.Add(new Stage("robot", 1f, sr => new RobotEffect(hopSize: 128, fftSize: 512))); break;
                case "whisper":    list.Add(new Stage("whisper", 1f, sr => new WhisperEffect(hopSize: 128, fftSize: 512))); break;
                case "echo":       list.Add(new Stage("echo", 1f, sr => new EchoEffect(sr, 0.22f, 0.5f))); break;
                case "distortion": list.Add(new Stage("tone", 1f, sr => new DistortionEffect(DistortionMode.SoftClipping, 18))); break;
                case "flanger":    list.Add(new Stage("flanger", 1f, sr => new FlangerEffect(sr))); break;
                case "vibrato":    list.Add(new Stage("vibrato", 1f, sr => new VibratoEffect(sr))); break;
                case "tremolo":    list.Add(new Stage("wobble", 1f, sr => new TremoloEffect(sr, 0.7f, 7))); break;
                case "autowah":    list.Add(new Stage("autowah", 1f, sr => new AutowahEffect(sr))); break;

                // The "voice of God" family — deep pitch (set via the preset) plus a big
                // reverberant space, a layered chorus and a booming echo.
                case "yhwh":
                    list.Add(new Stage("chorus", 1f, sr => new ChorusEffect(sr, new[] { 0.5f, 0.9f }, new[] { 0.002f, 0.0025f })));
                    list.Add(new Stage("reverb", 1f, sr => new ReverbFilter(sr, roomSize: 0.90f, damp: 0.20f, wet: 0.55f)));
                    list.Add(new Stage("echo", 1f, sr => new EchoEffect(sr, 0.25f, 0.3f)));
                    break;
                case "cathedral":
                    list.Add(new Stage("reverb", 1f, sr => new ReverbFilter(sr, roomSize: 0.94f, damp: 0.15f, wet: 0.60f)));
                    list.Add(new Stage("echo", 1f, sr => new EchoEffect(sr, 0.35f, 0.35f)));
                    break;
                case "angelic":
                    list.Add(new Stage("chorus", 1f, sr => new ChorusEffect(sr, new[] { 0.8f, 1.2f, 1.6f }, new[] { 0.0025f, 0.003f, 0.0035f })));
                    list.Add(new Stage("reverb", 1f, sr => new ReverbFilter(sr, roomSize: 0.85f, damp: 0.30f, wet: 0.50f)));
                    break;
                // Skeletor — matches the mixer preset: heavy raspy grit + a strong wobble.
                case "skeletor":
                    list.Add(new Stage("tone", 0.76f, sr => new WetDryFilter(new DistortionEffect(DistortionMode.SoftClipping, 22), 0.76f)));
                    list.Add(new Stage("wobble", 0.69f, sr => new WetDryFilter(new TremoloEffect(sr, 0.7f, 6), 0.69f)));
                    break;
            }
            return list;
        }

        /// <summary>
        /// Combine voices (oldest first) into one chain. Different effects all apply; when two
        /// voices share an effect, the stronger one wins. Pitch likewise takes the biggest shift.
        /// Ties go to the newer voice. A single voice yields exactly its own chain.
        /// </summary>
        private static Chain Merge(IEnumerable<MorphPreset> presets, int sampleRate)
        {
            int pitch = 0;
            var best = new Dictionary<string, Stage>();
            foreach (var p in presets)
            {
                if (p.PitchSemitones != 0 && Math.Abs(p.PitchSemitones) >= Math.Abs(pitch))
                    pitch = p.PitchSemitones;
                foreach (var st in PresetStages(p))
                    if (!best.TryGetValue(st.Key, out var cur) || Math.Abs(st.Amount) >= Math.Abs(cur.Amount))
                        best[st.Key] = st;
            }

            var chain = new Chain();
            if (pitch != 0)
                chain.Pitch = new PitchShiftVocoderEffect(sampleRate, Math.Pow(2, pitch / 12.0));
            var ordered = best.Values
                .OrderBy(s => { int i = Array.IndexOf(StageOrder, s.Key); return i < 0 ? int.MaxValue : i; })
                .Select(s => s.Make(sampleRate))
                .ToArray();
            chain.Fx = ordered.Length > 0 ? ordered : null;
            return chain;
        }

        /// <summary>Build the DSP chain for a preset (shared with previews).</summary>
        private static Chain BuildChain(MorphPreset p, int sampleRate) => Merge(new[] { p }, sampleRate);

        // Voices currently on, oldest first. Each keeps its own end time; the live chain is
        // all of them merged (see Merge). Guarded by MorphGate (separate from the device Gate).
        private sealed class ActiveVoice { public MorphPreset Preset = null!; public DateTime EndsUtc; }
        private static readonly List<ActiveVoice> _active = new();
        private static readonly object MorphGate = new();

        /// <summary>Activate a saved morph by name (redeems). Stacks on top of any voices already
        /// on — each runs for its own TimerSeconds — instead of replacing them.</summary>
        public static bool ActivateByName(string name)
        {
            var s = SettingsService.LoadMorph();
            var p = s.Presets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p == null) return false;
            return Activate(p, stack: true);
        }

        /// <summary>
        /// Turn a voice on for its TimerSeconds. <paramref name="stack"/> = true (redeems) layers it
        /// with whatever's already on; false (Voice Morph lab audition) replaces everything so you
        /// hear only this voice.
        /// </summary>
        public static bool Activate(MorphPreset p, bool stack = false)
        {
            if (!IsRunning && !Start()) return false;
            int secs = Math.Max(5, p.TimerSeconds);
            lock (MorphGate)
            {
                if (!stack) _active.Clear();
                var ends = DateTime.UtcNow.AddSeconds(secs);
                // Redeeming a voice that's already on refreshes it (keeps the later end time) and
                // makes it the newest layer, rather than stacking a second copy of it.
                int i = _active.FindIndex(a => SameVoice(a.Preset, p));
                if (i >= 0)
                {
                    if (_active[i].EndsUtc > ends) ends = _active[i].EndsUtc;
                    _active.RemoveAt(i);
                }
                _active.Add(new ActiveVoice { Preset = p, EndsUtc = ends });
                RebuildLocked();
            }
            return true;
        }

        private static bool SameVoice(MorphPreset a, MorphPreset b) =>
            ReferenceEquals(a, b) ||
            (!string.IsNullOrEmpty(a.Name) && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Drop expired voices, rebuild the merged chain, re-arm the timer for the next voice to
        // end, and update the overlay pill. Call with MorphGate held.
        private static void RebuildLocked()
        {
            var now = DateTime.UtcNow;
            _active.RemoveAll(a => a.EndsUtc <= now);
            _revert?.Dispose();
            _revert = null;

            if (_active.Count == 0)
            {
                _chain = null;
                _activeName = string.Empty;
                OverlayServer.SetMorph(null, 0);
                return;
            }

            int sr = _capture?.WaveFormat.SampleRate ?? 48000;
            _chain = Merge(_active.Select(a => a.Preset), sr);
            _activeName = string.Join(" + ", _active.Select(a => a.Preset.Name));

            // Wake when the next voice ends so it drops out on time; the others keep going.
            var due = _active.Min(a => a.EndsUtc) - now;
            if (due < TimeSpan.FromMilliseconds(50)) due = TimeSpan.FromMilliseconds(50);
            _revert = new Timer(_ => { lock (MorphGate) RebuildLocked(); }, null, due, Timeout.InfiniteTimeSpan);

            // The overlay counts down to when the voice is fully back to normal.
            int untilNormal = (int)Math.Ceiling((_active.Max(a => a.EndsUtc) - now).TotalSeconds);
            OverlayServer.SetMorph(_activeName, untilNormal);
        }

        /// <summary>Back to the streamer's normal (dry) voice — turns every active voice off.</summary>
        public static void ClearMorph(bool broadcast = true)
        {
            lock (MorphGate)
            {
                _active.Clear();
                _chain = null;
                _activeName = string.Empty;
                _revert?.Dispose();
                _revert = null;
            }
            if (broadcast) OverlayServer.SetMorph(null, 0);
        }
    }
}
