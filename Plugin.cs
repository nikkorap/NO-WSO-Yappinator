using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WSOYappinator
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin I { get; private set; }
        public ManualLogSource Log => Logger;
         
        private ConfigEntry<bool> EnableMod;
        private ConfigEntry<float> cooldownSeconds;
        public ConfigEntry<float> minDamage;
        private ConfigEntry<int> Volume;
        public ConfigEntry<float> rareClipFrequency;
        private ConfigEntry<int> BaseChancePercent;
        private ConfigEntry<string> SelectedSet;
        private ConfigEntry<bool> VerboseLogs;
        private ConfigEntry<float> idleAfterSeconds;
        public ConfigEntry<float> lowAltitude;
        public ConfigEntry<float> highAltitude;
        public ConfigEntry<float> fuelRatioBingo;
        private float _nextIdleAttemptAt;
        private string _audioRoot;
        private Harmony _harmony;

        private AudioSetManager _setManager;
        private VoicelineCatalog _catalog;
        private AudioPlayer _player;
        private VoicelineDirector _director;
        private AircraftEventBinder _binder;

        private bool _inGameWorld;
        private Aircraft _ac;
        private bool _ejected;
        public static bool seenNukes = false;
        public static bool reachedTactical = false;
        public static bool reachedStrategic = false;
        private bool Active => EnableMod.Value && _inGameWorld;

        private void Awake()
        {
            I = this;

            _audioRoot = Path.GetDirectoryName(Info.Location);
            Directory.CreateDirectory(_audioRoot);
            VerboseLogs = Config.Bind("General", "Verbose Logs", false, "Log every voiceline play attempt + outcome");

            EnableMod = Config.Bind("General", "Enable Mod", true, "Enable or disable the Voiceline Mod");
            cooldownSeconds = Config.Bind("General", "Minimum Cooldown (s)", 4f, "Minimum cooldown per voiceline event of the same priority");
            minDamage = Config.Bind("General", "minimum damage to play voiceline", 15000f);
            Volume = Config.Bind("General", "Voiceline Volume", 100, new ConfigDescription("Global playback volume for voicelines", new AcceptableValueRange<int>(0, 200)));
            rareClipFrequency = Config.Bind("General", "rare Clip Frequency %", 1f, new ConfigDescription("percentage chance for a rare clip to play instead of a normal clip. set to 0 to disable", new AcceptableValueRange<float>(0f, 100f)));
            BaseChancePercent = Config.Bind("General", "Base Chance %", 100, new ConfigDescription("Base chance added to event priority to determine play probability (0-100).", new AcceptableValueRange<int>(0, 100)));
            idleAfterSeconds = Config.Bind(
                "General",
                "Idle After (s)",
                45f,
                "Play the Idle event if no voiceline has successfully played for this many seconds. Set to 0 to disable."
            );

            lowAltitude = Config.Bind("General", "Low altitude below", 5f);
            highAltitude = Config.Bind("General", "High altitude above", 15000f);
            fuelRatioBingo =  Config.Bind("General", "Bingo fuel below ratio", 0.2f, new ConfigDescription("Ratio below the bingo fuel event is trigger", new AcceptableValueRange<float>(0.01f, 1f)));

            string[] sets = AudioPackDiscovery.Discover(_audioRoot);
            string firstSet = sets.Length == 0 ? "" : sets[0];
            SelectedSet = sets.Length > 0
                ? Config.Bind("General", "Voiceline Set", firstSet, new ConfigDescription("Which voiceline folder to use", new AcceptableValueList<string>(sets)))
                : Config.Bind("General", "Voiceline Set", "", "Which voiceline folder to use (no sets found yet)");

            _catalog = new VoicelineCatalog();
            _setManager = new AudioSetManager(new FileEventPriorityLoader(), _catalog);
            _player = new AudioPlayer(gameObject, Volume);
            _director = new VoicelineDirector(
                _catalog,
                _player,
                GetPriority,
                () => cooldownSeconds.Value,
                () => BaseChancePercent.Value,
                () => VerboseLogs.Value,
                Log);

            _binder = new AircraftEventBinder(
                onEvent: (evt, extra) => OnAircraftEvent(evt),
                onInfo: _ => { },
                verbose: () => false);

            SelectedSet.SettingChanged += (_, __) => { InitializeSet(); _director.Reset(stopAudio: true); };

            InitializeSet();

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            _harmony?.UnpatchSelf();

            _binder?.Dispose();
            _director?.Reset(stopAudio: true);

            if (ReferenceEquals(I, this)) I = null;
        }

        private void Update()
        {
            if (!Active)
            {
                _binder.Bind(null);
                _ac = null;
                _ejected = false;
                return;
            }

            GameManager.GetLocalAircraft(out Aircraft cur);
            if (cur != null && !cur) cur = null;

            if (!ReferenceEquals(cur, _ac))
            {
                if (_ac != null && cur == null) FinalizeAircraftExit(_ac);
                _ac = cur;
                _ejected = false;
                _director.ResetTransient();
            }

            _binder.Bind(_ac);

            if (idleAfterSeconds.Value > 0f && 
                Time.time >= _nextIdleAttemptAt && 
                Time.time - _director.LastPlayedAt >= idleAfterSeconds.Value)
            {
                TriggerVoiceline(VoiceEvent.Idle);
                _nextIdleAttemptAt = Time.time + 2f;
            }
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            _inGameWorld = string.Equals(newScene.name, "GameWorld", StringComparison.OrdinalIgnoreCase);
            if (!_inGameWorld)
            {
                _binder.Bind(null);
                _ac = null;
                _ejected = false;
                _director.Reset(stopAudio: true);
                seenNukes = false;
                reachedTactical = false;
                reachedStrategic = false;
            }
            else
            {
                _director.ResetTransient();
            }
        }

        private void InitializeSet()
        {
            if (string.IsNullOrWhiteSpace(SelectedSet.Value))
            {
                _catalog.Clear();
                _setManager.EventPriorities.Clear();
                Log.LogWarning("No voiceline set selected (or no sets found).");
                return;
            }

            _setManager.Initialize(_audioRoot, SelectedSet.Value);
        }

        private int GetPriority(VoiceEvent e)
            => _setManager.EventPriorities.TryGetValue(e, out int p) ? p : 0;

        private void OnAircraftEvent(VoiceEvent evt)
        {
            if (evt == VoiceEvent.Eject) _ejected = true;
            TriggerVoiceline(evt, (evt == VoiceEvent.radarPingNew || evt == VoiceEvent.radarPingLocked) ? 15f : 0.5f);
        }

        private void FinalizeAircraftExit(Aircraft aircraft)
        {
            _director.ResetTransient();
            if (_ejected) return;

            try
            {
                if (!aircraft) { TriggerVoiceline(VoiceEvent.Death); return; }

                var hq = aircraft.NetworkHQ;
                var tr = aircraft.transform;
                if (!tr || hq == null) { TriggerVoiceline(VoiceEvent.Death); return; }

                var pos = tr.position;
                TriggerVoiceline(aircraft.IsLanded() && hq.AnyNearAirbase(pos, out _) ? VoiceEvent.Disembark : VoiceEvent.Death);
            }
            catch { TriggerVoiceline(VoiceEvent.Death); }
        }

        public void TryGated(VoiceEvent evt, int basePct = 5, int stepPct = 5, int maxPct = 50, float rollCooldownSeconds = 5f)
        {
            if (!Active) return;
            _director.TryGated(evt, basePct, stepPct, maxPct, rollCooldownSeconds);
        }

        public bool TriggerVoiceline(VoiceEvent requested, float suppressTime = 0.5f)
        {
            if (!Active) return false;
            if (_ac == null && requested != VoiceEvent.Death && requested != VoiceEvent.Disembark) return false;
            return _director.TryPlay(requested, suppressTime);
        }
    }
}
