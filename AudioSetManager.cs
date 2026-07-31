using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WSOYappinator
{
    public sealed class AudioSetManager
    {
        public string AudioSetFolder { get; private set; }
        public Dictionary<VoiceEvent, int> EventPriorities { get; private set; } = new Dictionary<VoiceEvent, int>();

        private readonly IEventPriorityLoader _priorityLoader;
        private readonly VoicelineCatalog _catalog;
        public VoicelineCatalog Catalog => _catalog;

        public AudioSetManager(IEventPriorityLoader priorityLoader, VoicelineCatalog catalog)
        {
            _priorityLoader = priorityLoader;
            _catalog = catalog;
        }

        public void Initialize(string baseFolder, string selectedSet)
        {
            AudioSetFolder = Path.Combine(baseFolder, selectedSet);
            Plugin.instance.Log.LogInfo($"Initializing set folder: {AudioSetFolder}");

            _catalog.Clear();
            EventPriorities.Clear();

            if (!Directory.Exists(AudioSetFolder))
            {
                Plugin.instance.Log.LogWarning($"audio set folder not found: {AudioSetFolder}");
                return;
            }

            Dictionary<string, int> raw = _priorityLoader.Load(AudioSetFolder);

            foreach (KeyValuePair<string, int> kvp in raw)
            {
                if (VoiceEventMaps.TryParse(kvp.Key, out VoiceEvent evt)) EventPriorities[evt] = kvp.Value;
                else Plugin.instance.Log.LogWarning($"Unknown event key in priorities: [{kvp.Key}]");
                
            }

            Plugin.instance.Log.LogInfo($"Registering events for set [{selectedSet}] ({EventPriorities.Count} events)");

            var allEvents = Enum.GetValues(typeof(VoiceEvent)).Cast<VoiceEvent>();
            _catalog.RegisterAllFromFolder(AudioSetFolder, allEvents);
        }
    }
}
