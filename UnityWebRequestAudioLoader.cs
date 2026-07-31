using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace WSOYappinator
{
    public static class UnityWebRequestAudioLoader
    {
        public static bool SupportsFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".wav" || ext == ".ogg" || ext == ".mp3";
        }

        public static AudioClip LoadClip(string path)
        {
            AudioType type = GetAudioType(path);
            if (type == AudioType.UNKNOWN) return null;

            UnityWebRequest loader = UnityWebRequestMultimedia.GetAudioClip(path, type);
            loader.SendWebRequest();

            while (true)
            {
                if (loader.isDone) break;
            }

            if (loader.error != null)
            {
                Plugin.instance.Log.LogError(loader.error);
                return null;
            }
            AudioClip clip = DownloadHandlerAudioClip.GetContent(loader);
            if (clip && clip.loadState == AudioDataLoadState.Loaded)
            {
                clip.name = path.TrimStart(Plugin.instance.Info.Location.ToCharArray());
                return clip;
            }

            Plugin.instance.Log.LogError($"Failed to load clip:{path}");
            return null;

        }

        private static AudioType GetAudioType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}