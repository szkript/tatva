using UnityEngine;

namespace Tatva
{
    /// <summary>Feeds the synth into the output; must sit next to the AudioListener.</summary>
    public sealed class SynthHost : MonoBehaviour
    {
        public Synth Synth { get; private set; }
        long latency;

        void Awake()
        {
            var cfg = AudioSettings.GetConfiguration();
            cfg.dspBufferSize = 512;
            AudioSettings.Reset(cfg);
            Synth = new Synth(AudioSettings.outputSampleRate);
            AudioSettings.GetDSPBufferSize(out int len, out _);
            latency = len + Synth.Sr / 200;
        }

        /// <summary>Sample time for a note that should sound as soon as possible.</summary>
        public long At => Synth.Now + latency;

        void OnAudioFilterRead(float[] data, int channels) => Synth?.Render(data, channels);
    }
}
