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
    /// Redeems activate a preset for its TimerSeconds; the overlay shows a countdown and
    /// the voice auto-reverts to dry at zero.
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

        private sealed class Chain
        {
            public PitchShiftVocoderEffect? Pitch;
            public IOnlineFilter? Fx;
            public float Process(float s)
            {
                if (Pitch != null) s = Pitch.Process(s);
                if (Fx != null) s = Fx.Process(s);
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

        /// <summary>Build the DSP chain for a preset (shared with previews).</summary>
        private static Chain BuildChain(MorphPreset p, int sampleRate)
        {
            var chain = new Chain();
            if (p.PitchSemitones != 0)
                chain.Pitch = new PitchShiftVocoderEffect(sampleRate, Math.Pow(2, p.PitchSemitones / 12.0));
            chain.Fx = (IOnlineFilter?)(p.Effect switch
            {
                "robot" => new RobotEffect(hopSize: 128, fftSize: 512),
                "whisper" => new WhisperEffect(hopSize: 128, fftSize: 512),
                "echo" => new EchoEffect(sampleRate, 0.22f, 0.5f),
                "distortion" => new DistortionEffect(DistortionMode.SoftClipping, 18),
                "flanger" => new FlangerEffect(sampleRate),
                "vibrato" => new VibratoEffect(sampleRate),
                "tremolo" => new TremoloEffect(sampleRate, 0.7f, 7),
                "autowah" => new AutowahEffect(sampleRate),
                _ => (object?)null,
            });
            return chain;
        }

        /// <summary>Activate a saved morph by name; auto-reverts after its TimerSeconds.</summary>
        public static bool ActivateByName(string name)
        {
            var s = SettingsService.LoadMorph();
            var p = s.Presets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p == null) return false;
            return Activate(p);
        }

        public static bool Activate(MorphPreset p)
        {
            if (!IsRunning && !Start()) return false;
            int sr = _capture?.WaveFormat.SampleRate ?? 48000;
            _chain = BuildChain(p, sr);
            _activeName = p.Name;

            _revert?.Dispose();
            int secs = Math.Max(5, p.TimerSeconds);
            _revert = new Timer(_ => ClearMorph(broadcast: true), null, TimeSpan.FromSeconds(secs), Timeout.InfiniteTimeSpan);
            OverlayServer.SetMorph(p.Name, secs);
            return true;
        }

        /// <summary>Back to the streamer's normal (dry) voice.</summary>
        public static void ClearMorph(bool broadcast = true)
        {
            _chain = null;
            _activeName = string.Empty;
            _revert?.Dispose();
            _revert = null;
            if (broadcast) OverlayServer.SetMorph(null, 0);
        }
    }
}
