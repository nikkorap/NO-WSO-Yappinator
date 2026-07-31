using BepInEx.Configuration;
using UnityEngine;

namespace WSOYappinator
{
    public sealed class AudioPlayer
    {
        private readonly GameObject host;
        private readonly ConfigEntry<int> volumePercent;
        private AudioSource _source;

        public AudioPlayer(GameObject host, ConfigEntry<int> volumePercent)
        {
            this.host = host;
            this.volumePercent = volumePercent;
        }

        private AudioSource Ensure()
        {
            if (_source == null)
            {
                _source = host.AddComponent<AudioSource>();
                _source.playOnAwake = false;
            }
            return _source;
        }
        public void StopImmediate()
        {
            if (_source == null) return;
            _source.Stop();
            _source.clip = null;
        }
        public void TryPlay(AudioClip clip, float volumeMultiplier = 1f)
        {
            AudioSource src = Ensure();
            if (src != null && src.isPlaying) src.Stop();
            src.volume = Mathf.Min(Mathf.Clamp(volumePercent.Value / 100f, 0f, 2f), 2f) * volumeMultiplier;
            src.clip = clip;
            src.Play();
        }
    }
}
