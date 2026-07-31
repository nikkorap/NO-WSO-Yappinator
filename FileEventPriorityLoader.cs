using System;
using System.Collections.Generic;
using System.IO;

namespace WSOYappinator
{
    public sealed class FileEventPriorityLoader : IEventPriorityLoader
    {
        private const string DefaultResourceName = "WSOYappinator.eventPriorities.txt";

        public Dictionary<string, int> Load(string audioSetFolder)
        {
            Dictionary<string, int> dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(audioSetFolder, "eventPriorities.txt");

            if (!File.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(audioSetFolder);
                    using (Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(DefaultResourceName))
                    {
                        if (s == null)
                        {
                            Plugin.instance.Log.LogWarning(
                                $"Default priorities resource not found: {DefaultResourceName}");
                            return dict;
                        }

                        using (StreamReader reader = new StreamReader(s))
                        {
                            File.WriteAllText(path, reader.ReadToEnd());
                        }
                    }
                    Plugin.instance.Log.LogInfo($"Created default priorities at: {path}");
                }
                catch (Exception ex)
                {
                    Plugin.instance.Log.LogError($"Failed to create default priorities: {ex}");
                    return dict;
                }
            }
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                string[] parts = line.Split(new char[] { '=' }, 2, StringSplitOptions.None);
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                if (int.TryParse(parts[1].Trim(), out int pri))
                    dict[key] = pri;
                else
                    Plugin.instance.Log.LogWarning($"bad priority for [{key}]: {parts[1]}");
            }

            Plugin.instance.Log.LogInfo($"loaded {dict.Count} priorities from {path}");
            return dict;
        }
    }
}