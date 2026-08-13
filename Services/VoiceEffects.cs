using System;
using NAudio.Wave;

namespace GameTracker.Services
{
    // Runs any NWaves streaming effect over an NAudio sample stream.
    internal sealed class NWavesProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly NWaves.Filters.Base.IOnlineFilter _fx;

        public NWavesProvider(ISampleProvider src, NWaves.Filters.Base.IOnlineFilter fx)
        {
            _src = src;
            _fx = fx;
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            for (int i = 0; i < n; i++)
                buffer[offset + i] = Math.Clamp(_fx.Process(buffer[offset + i]), -1f, 1f);
            return n;
        }
    }

    // Ring modulation — multiplies the signal by a low sine, giving a robotic/metallic tone.
    internal sealed class RingModProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly double _inc;
        private double _phase;

        public RingModProvider(ISampleProvider src, double freqHz = 50)
        {
            _src = src;
            _inc = 2 * Math.PI * freqHz / src.WaveFormat.SampleRate;
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            int ch = Math.Max(1, WaveFormat.Channels);
            for (int i = 0; i < n; i += ch)
            {
                float m = (float)(0.5 + 0.5 * Math.Sin(_phase));
                for (int c = 0; c < ch && i + c < n; c++)
                    buffer[offset + i + c] *= m;
                _phase += _inc;
                if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
            }
            return n;
        }
    }

    // Feedback echo — a spooky "ghost" repeat.
    internal sealed class EchoProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly float[] _delay;
        private readonly float _decay;
        private int _pos;

        public EchoProvider(ISampleProvider src, double delaySeconds = 0.22, float decay = 0.5f)
        {
            _src = src;
            _decay = decay;
            int len = Math.Max(1, (int)(src.WaveFormat.SampleRate * delaySeconds) * Math.Max(1, src.WaveFormat.Channels));
            _delay = new float[len];
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            for (int i = 0; i < n; i++)
            {
                float echoed = buffer[offset + i] + _delay[_pos] * _decay;
                _delay[_pos] = echoed;
                buffer[offset + i] = echoed;
                if (++_pos >= _delay.Length) _pos = 0;
            }
            return n;
        }
    }

    // Tremolo — wobbling amplitude for an "alien" warble.
    internal sealed class TremoloProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly double _inc;
        private readonly float _depth;
        private double _phase;

        public TremoloProvider(ISampleProvider src, double freqHz = 7, float depth = 0.6f)
        {
            _src = src;
            _depth = depth;
            _inc = 2 * Math.PI * freqHz / src.WaveFormat.SampleRate;
        }

        public WaveFormat WaveFormat => _src.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            int ch = Math.Max(1, WaveFormat.Channels);
            for (int i = 0; i < n; i += ch)
            {
                float m = (float)(1.0 - _depth * (0.5 + 0.5 * Math.Sin(_phase)));
                for (int c = 0; c < ch && i + c < n; c++)
                    buffer[offset + i + c] *= m;
                _phase += _inc;
                if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
            }
            return n;
        }
    }

    // Plays a cached mono float buffer once (used for the bundled chicken "bawk" clip).
    internal sealed class ClipSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private readonly WaveFormat _fmt;
        private int _pos;

        public ClipSampleProvider(float[] data, int sampleRate)
        {
            _data = data;
            _fmt = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat => _fmt;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }

    // Procedural cartoon chicken "bawk" — fallback censor if the bundled clip won't load. Generates a
    // buzzy, warbling squawk with a rise-then-fall pitch contour and a two-bump envelope,
    // so no audio file needs to be shipped. Finite length; returns 0 once done.
    internal sealed class ChickenSquawkProvider : ISampleProvider
    {
        private readonly WaveFormat _fmt;
        private readonly int _channels;
        private readonly double _sr;
        private readonly long _total;    // total frames
        private readonly double _baseHz;
        private long _pos;
        private double _phase;

        public ChickenSquawkProvider(int sampleRate, int channels, double seconds, int variation)
        {
            _fmt = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Math.Max(1, channels));
            _channels = Math.Max(1, channels);
            _sr = sampleRate;
            _total = Math.Max(1, (long)(seconds * sampleRate));
            _baseHz = 600 + (variation % 6) * 30;   // slight per-word variety
        }

        public WaveFormat WaveFormat => _fmt;

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _channels;
            int written = 0;
            for (int f = 0; f < frames && _pos < _total; f++, _pos++)
            {
                double t = (double)_pos / _total;                 // 0..1 across the squawk
                double contour = t < 0.15 ? 0.7 + 2.0 * t          // quick rise …
                                          : 1.0 - 0.5 * (t - 0.15) / 0.85;  // … then fall
                double warble = 1.0 + 0.18 * Math.Sin(2 * Math.PI * 32 * t);
                _phase += _baseHz * contour * warble / _sr;
                double saw = 2.0 * (_phase - Math.Floor(_phase + 0.5));       // rich harmonics
                double sq = Math.Sign(Math.Sin(2 * Math.PI * _phase));        // nasal edge
                double tone = 0.75 * saw + 0.25 * sq;

                double atk = Math.Min(1.0, t / 0.02);              // 20 ms attack
                double rel = Math.Min(1.0, (1.0 - t) / 0.15);      // release near the end
                double bump = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 1.3 * t);    // "b'GAWK" two-bump
                float s = (float)(0.28 * tone * atk * rel * bump);

                for (int c = 0; c < _channels; c++) buffer[offset + written++] = s;
            }
            return written;
        }
    }
}
