using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WSOYappinator
{
    public sealed class VoicelineCatalog
    {
        private readonly System.Random _rnd = new System.Random();
        public readonly Dictionary<VoiceEvent, List<AudioClip>> Clips = new Dictionary<VoiceEvent, List<AudioClip>>();
        private readonly Dictionary<VoiceEvent, List<AudioClip>> _shuffled = new Dictionary<VoiceEvent, List<AudioClip>>();
        private readonly Dictionary<VoiceEvent, int> _indices = new Dictionary<VoiceEvent, int>();
        private readonly HashSet<AudioClip> _allClips = new HashSet<AudioClip>();

        public void Clear()
        {
            _shuffled.Clear();
            _indices.Clear();
            Clips.Clear();
            foreach (AudioClip c in _allClips) if (c != null) UnityEngine.Object.Destroy(c);
            _allClips.Clear();
        }

        public void RegisterAllFromFolder(string audioSetFolder, IEnumerable<VoiceEvent> knownEvents)
        {
            if (!Directory.Exists(audioSetFolder))
            {
                Plugin.instance.Log.LogWarning($"Missing audio folder: {audioSetFolder}");
                return;
            }

            HashSet<VoiceEvent> keySet = new HashSet<VoiceEvent>(knownEvents);
            Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in Directory.EnumerateFiles(audioSetFolder).Where(UnityWebRequestAudioLoader.SupportsFile))
            {
                string name = Path.GetFileNameWithoutExtension(path);

                IEnumerable<string> tokens = Regex.Split(name, "[^A-Za-z0-9]+").Where(t => !string.IsNullOrEmpty(t) && !t.All(char.IsDigit));
                HashSet<VoiceEvent> matched = new HashSet<VoiceEvent>();

                if (VoiceEventMaps.TryParse(name, out VoiceEvent fullMatch) && keySet.Contains(fullMatch))
                {
                    matched.Add(fullMatch);
                }
                else
                {
                    foreach (string t in tokens)
                    {
                        if (VoiceEventMaps.TryParse(t, out VoiceEvent evt) && keySet.Contains(evt)) matched.Add(evt);
                    }
                }

                if (matched.Count == 0) continue;

                if (!clipCache.TryGetValue(path, out AudioClip clip))
                {
                    clip = UnityWebRequestAudioLoader.LoadClip(path);
                    if (clip == null) continue;
                    clipCache[path] = clip;
                    _allClips.Add(clip);
                }

                foreach (VoiceEvent evt in matched)
                {
                    if (!Clips.TryGetValue(evt, out List<AudioClip> list)) Clips[evt] = list = new List<AudioClip>();
                    list.Add(clip);
                }
            }

            foreach (KeyValuePair<VoiceEvent, List<AudioClip>> kvp in Clips) Plugin.instance.Log.LogInfo($"Registered [{kvp.Key}] ({kvp.Value.Count} clips)");
        }

        public AudioClip RequestClip(VoiceEvent key)
        {
            if (!Clips.TryGetValue(key, out List<AudioClip> clips) || clips == null || clips.Count == 0)
                return null;

            if (!_shuffled.TryGetValue(key, out List<AudioClip> pool) || pool == null || pool.Count != clips.Count)
            {
                pool = clips.Where(c => c != null).ToList();
                if (pool.Count == 0)
                {
                    _shuffled.Remove(key);
                    _indices.Remove(key);
                    return null;
                }
                Shuffle(pool, _rnd);
                _shuffled[key] = pool;
                _indices[key] = 0;
            }

            if (!_indices.TryGetValue(key, out int idx) || idx < 0 || idx >= pool.Count)
                idx = 0;

            AudioClip clip = pool[idx];
            if (clip == null)
            {
                int i = (idx + 1) % pool.Count;
                while (i != idx && pool[i] == null) i = (i + 1) % pool.Count;
                clip = pool[i];
                idx = i;
                if (clip == null) return null;
            }

            _indices[key] = (idx + 1) % pool.Count;
            return clip;
        }

        public bool HasClips(VoiceEvent key) => Clips.TryGetValue(key, out List<AudioClip> list) && list != null && list.Count > 0;

        private static void Shuffle<T>(IList<T> list, System.Random rnd = null)
        {
            if (rnd == null) rnd = new System.Random();
            for (int i = 0; i < list.Count; i++)
            {
                int j = rnd.Next(i, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
