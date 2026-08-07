using System;
using NAudio.Wave;

namespace GameTracker.Services
{
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
}
