using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WSOYappinator
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin instance { get; private set; }
        public ManualLogSource Log => Logger;
        private readonly System.Random _rnd = new System.Random();
        public ConfigEntry<bool> EnableMod;
        private ConfigEntry<float> cooldownSeconds;
        public ConfigEntry<float> minDamage;
        private ConfigEntry<int> Volume;
        private ConfigEntry<string> SelectedSet;
        private AudioSetManager _setManager;
        private VoicelineCatalog _catalog;
        private IEventPriorityLoader _priorityLoader;
        private string _audioRoot;
        private float _nextPlayableAt = 0f;
        private float _priorityExpiresAt = 0f;
        private readonly Dictionary<VoiceEvent, float> _suppressUntil = new Dictionary<VoiceEvent, float>();
        private Aircraft _ac;
        private Action _unsubscribeAcEvents;
        private ConfigEntry<int> BaseChancePercent;
        private int currentPriority = 0;
        private AudioPlayer _player;
        private ConfigEntry<bool> VerboseLogs;
        private Harmony harmony;
        private readonly Dictionary<string, ConfigEntry<bool>> _aircraftToggles = new Dictionary<string, ConfigEntry<bool>>();
        private ConfigEntry<bool> RefreshAircraftList;
        private ConfigEntry<bool> ImaginaryCopilot;
        private ConfigEntry<bool> RandomVoicepackOnSpawn;
        private readonly Dictionary<string, ConfigEntry<string>> _aircraftVoicepacks = new Dictionary<string, ConfigEntry<string>>();
        private int _cachedCrewCapacity = 0;

        public Dictionary<VoiceEvent, int> eventPriorities = new Dictionary<VoiceEvent, int>();
        private struct GateState
        {
            public bool fired;
            public int fails;
            public float nextRollAt;
        }

        private readonly Dictionary<VoiceEvent, GateState> _sortieGate = new Dictionary<VoiceEvent, GateState>();
        private void Awake()
        {
            instance = this;

            _audioRoot = Path.Combine(Path.GetDirectoryName(Info.Location), "audio");
            Directory.CreateDirectory(_audioRoot);

            EnableMod = Config.Bind("General", "Enable Mod", true, "Enable or disable the Voiceline Mod");
            //todo implement toggle
            //EnableMod.SettingChanged += null;

            cooldownSeconds = Config.Bind("General", "Minimum Cooldown (s)", 4f, "Minimum cooldown per voiceline event of the same priority");
            minDamage = Config.Bind("General", "minimum damage to play voiceline", 15000f);
            Volume = Config.Bind("General", "Voiceline Volume", 100, new ConfigDescription("Global playback volume for voicelines", new AcceptableValueRange<int>(0, 200)));
            BaseChancePercent = Config.Bind("General", "Base Chance %", 100,
                new ConfigDescription("Base chance added to event priority to determine play probability (0-100).",
                new AcceptableValueRange<int>(0, 100)));
            VerboseLogs = Config.Bind("General", "Verbose Logs", false, "enable verbose logs");

            ImaginaryCopilot = Config.Bind("General", "Imaginary copilot", false, "Enable the mod even if the aircraft has 1 or fewer crew members (for single-seat or unmanned aircraft).");
            RandomVoicepackOnSpawn = Config.Bind("General", "Random Voicepack On Spawn", false, "Toggle for switching to a different voicepack every time we spawn in.");

            RefreshAircraftList = Config.Bind("General", "Scan for Aircraft", false,
                new ConfigDescription("Click to re-scan for aircraft prefabs. Useful if you've added new aircraft mods without restarting.",
                null, new ConfigurationManagerAttributes { HideDefaultButton = true }));

            RefreshAircraftList.SettingChanged += (s, e) =>
            {
                if (RefreshAircraftList.Value)
                {
                    DiscoverAircraftAndBindConfig();
                    RefreshAircraftList.Value = false;
                }
            };

            string[] sets = Directory.GetDirectories(_audioRoot).Select(Path.GetFileName).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            string firstSet = sets.Length == 0 ? "" : sets[0];
            SelectedSet = sets.Length > 0
                ? Config.Bind("General", "Voiceline Set", firstSet, new ConfigDescription("Which voiceline folder to use", new AcceptableValueList<string>(sets)))
                : Config.Bind("General", "Voiceline Set", "", "Which voiceline folder to use (no sets found yet)");


            _priorityLoader = new FileEventPriorityLoader(); 
            _catalog = new VoicelineCatalog();
            _setManager = new AudioSetManager(_priorityLoader, _catalog);
            _player = new AudioPlayer(gameObject, Volume);

            SelectedSet.SettingChanged += (_, __) =>
            {
                Log.LogInfo($"Global Voiceline Set changed to [{SelectedSet.Value}]");
                ReloadCurrentVoicepack();
            };
            RandomVoicepackOnSpawn.SettingChanged += (_, __) => ReloadCurrentVoicepack();

            InitializeSet();

            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            
            StartCoroutine(InitConfigRoutine());
            
            Log.LogInfo($"Mod loaded!");
        }

        private System.Collections.IEnumerator InitConfigRoutine()
        {
            yield return new UnityEngine.WaitForSecondsRealtime(5f);
            DiscoverAircraftAndBindConfig();
        }
        bool inGameWorld;

        private System.Collections.IEnumerator MwsDiscoveryRoutine(Aircraft ac)
        {
            while (ac != null && _currentMws == null && ReferenceEquals(ac, _ac))
            {
                UpdateMws(ac);
                if (_currentMws != null) yield break;
                yield return new UnityEngine.WaitForSeconds(1f);
            }
        }
        
        private MissileWarning _currentMws;
        [HarmonyPatch(typeof(Player), nameof(Player.SetAircraft))]
        static class Patch_SetAircraft
        {
            static void Postfix(Player __instance, Aircraft aircraft)
            {
                if (!instance.EnableMod.Value || !GameManager.IsLocalPlayer(__instance)) return;
                instance.SwapAircraft(aircraft);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RemoveAircraft))]
        static class Patch_RemoveAircraft
        {
            static void Postfix(Player __instance)
            {
                if (!GameManager.IsLocalPlayer(__instance)) return;
                instance.SwapAircraft(null);
            }
        }
        public void TryGated(VoiceEvent evt, int basePct = 5, int stepPct = 5, int maxPct = 50, float rollCooldownSeconds = 5f)
        {
            GateState st = _sortieGate.TryGetValue(evt, out GateState s) ? s : default;
            if (st.fired) return;
            float now = Time.time;
            if (now < st.nextRollAt) return;

            int chance = Mathf.Clamp(basePct + st.fails * stepPct, 0, maxPct);

            if (_rnd.Next(100) < chance)
            {
                bool played = TriggerVoiceline(evt);
                if (played)
                {
                    st.fired = true;
                    st.nextRollAt = float.PositiveInfinity;
                }
            }
            else
            {
                st.fails++;
                st.nextRollAt = now + rollCooldownSeconds;
            }

            _sortieGate[evt] = st;
        }


        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            DiscoverAircraftAndBindConfig();
            inGameWorld = string.Equals(newScene.name, "GameWorld", StringComparison.OrdinalIgnoreCase);

            if (!inGameWorld)
            {
                _player?.StopImmediate();

                SwapAircraft(null);

                _nextPlayableAt = 0f;
                _priorityExpiresAt = 0f;
                currentPriority = 0;
                _sortieGate?.Clear();
                UnsubscribeMws();
            }
            else
            {
                _sortieGate?.Clear();
            }
        }
        private void SwapAircraft(Aircraft next)
        {
            if (!EnableMod.Value || !inGameWorld) next = null;
            if (!_ac) _ac = null;
            if (!next) next = null;
            if (_ac == next) return;

            try { _unsubscribeAcEvents?.Invoke(); } catch { }
            _unsubscribeAcEvents = null;

            _ac = next;
            _cachedCrewCapacity = GetCrewCapacity(_ac);

            if (_ac != null)
            {
                if (VerboseLogs.Value) DumpAircraftComponents(_ac);
                try { _unsubscribeAcEvents = SubscribeAircraftEvents(_ac); }
                catch { _ac = null; _unsubscribeAcEvents = null; }
            }
        }

        private Action SubscribeAircraftEvents(Aircraft aircraft)
        {
            if (aircraft == null)
            {
                Log.LogWarning($"{nameof(SubscribeAircraftEvents)}: aircraft is null");
                return () => { };
            }

            ReloadCurrentVoicepack(true);

            _sortieGate.Clear();





            void OnEject() => TriggerVoiceline(VoiceEvent.Eject);
            void OnTouchdown() => TriggerVoiceline(VoiceEvent.Touchdown);

            void OnSortieSuccess(float _)
            {
                _sortieGate?.Clear();
                TriggerVoiceline(VoiceEvent.onSortieSuccess);
            }

            void OnRearmHandler()
            {
                TriggerVoiceline(VoiceEvent.Rearm);
            }



            void OnDamage(UnitPart.OnApplyDamage e)
            {
                if ((e.impactDamage + e.pierceDamage + e.fireDamage + e.blastDamage) > minDamage.Value)
                    TriggerVoiceline(VoiceEvent.takeDamage);
            }

            void OnSetGear(Aircraft.OnSetGear e)
            {
                switch (e.gearState)
                {
                    case LandingGear.GearState.LockedRetracted: TriggerVoiceline(VoiceEvent.GearUp); break;
                    case LandingGear.GearState.LockedExtended: TriggerVoiceline(VoiceEvent.GearDown); break;
                }
            }

            void OnSetFlightAssist(Aircraft.OnFlightAssistToggle e)
            {
                if (IsRotary(aircraft))
                {
                    // Invert: turning it off (enabled=false) triggers Enable audio, and vice versa
                    TriggerVoiceline(e.enabled ? VoiceEvent.FlightAssistOff : VoiceEvent.FlightAssistOn);
                }
                else
                {
                    TriggerVoiceline(e.enabled ? VoiceEvent.FlightAssistOn : VoiceEvent.FlightAssistOff);
                }
            }

            void OnAutoHoverChanged()
                => TriggerVoiceline(aircraft.GetControlsFilter().IsAutoHoverEnabled()
                    ? VoiceEvent.AutohoverOn
                    : VoiceEvent.AutohoverOff);



            ControlsFilter cf = aircraft.GetControlsFilter();

            Dictionary<UnitPart, Action> partUnsub = new Dictionary<UnitPart, Action>();

            Action SubscribePart(UnitPart part)
            {
                if (part == null) return () => { };

                part.onApplyDamage += OnDamage;

                IEngine engine = null;
                try { part.TryGetComponent(out engine); } catch { }

                void OnEngineDisable() => TriggerVoiceline(VoiceEvent.engineLost);
                void OnEngineDamage() => TriggerVoiceline(VoiceEvent.engineDamage);

                if (engine != null)
                {
                    engine.OnEngineDisable += OnEngineDisable;
                    engine.OnEngineDamage += OnEngineDamage;
                }

                void OnThisPartDetached(UnitPart p)
                {
                    if (!ReferenceEquals(p, part)) return;

                    if (partUnsub.TryGetValue(part, out Action unsub))
                    {
                        unsub();
                        partUnsub.Remove(part);
                    }

                    TriggerVoiceline(VoiceEvent.partDetach);
                }

                part.onPartDetached += OnThisPartDetached;

                return () =>
                {
                    if (part == null) return;

                    part.onApplyDamage -= OnDamage;
                    part.onPartDetached -= OnThisPartDetached;

                    if (engine != null)
                    {
                        engine.OnEngineDisable -= OnEngineDisable;
                        engine.OnEngineDamage -= OnEngineDamage;
                    }
                };
            }

            UnitPart[] partsSnapshot = aircraft.GetAllParts()?.ToArray() ?? new UnitPart[0];
            foreach (UnitPart part in partsSnapshot)
            {
                Action unsub = SubscribePart(part);
                partUnsub[part] = unsub;
            }

            if (cf != null) cf.OnSetAutoHover += OnAutoHoverChanged;

            _currentMws = null;
            instance.StartCoroutine(instance.MwsDiscoveryRoutine(aircraft));

            void OnRadarWarning(Aircraft.OnRadarWarning e)
            {
                if (!aircraft.KnownRadarWarning(e.emitter)) TriggerVoiceline(VoiceEvent.radarPingNew, 15f);
                if (e.isTarget) TriggerVoiceline(VoiceEvent.radarPingLocked, 15f);
            }

            void OnJamHandler(Unit.JamEventArgs _) => TriggerVoiceline(VoiceEvent.beingJammed);

            aircraft.onRadarWarning += OnRadarWarning;
            aircraft.onJam += OnJamHandler;




            aircraft.onEject += OnEject;
            aircraft.OnTouchdown += OnTouchdown;
            aircraft.onSortieSuccessful += OnSortieSuccess;
            aircraft.onSetGear += OnSetGear;
            aircraft.onSetFlightAssist += OnSetFlightAssist;

            aircraft.OnRearmUnit += OnRearmHandler;

            TriggerVoiceline(VoiceEvent.Spawn);

            return () =>
            {
                if (cf != null) cf.OnSetAutoHover -= OnAutoHoverChanged;

                aircraft.onRadarWarning -= OnRadarWarning;
                aircraft.onJam -= OnJamHandler;




                aircraft.onEject -= OnEject;
                aircraft.OnTouchdown -= OnTouchdown;
                aircraft.onSortieSuccessful -= OnSortieSuccess;
                aircraft.onSetGear -= OnSetGear;
                aircraft.onSetFlightAssist -= OnSetFlightAssist;

                aircraft.OnRearmUnit -= OnRearmHandler;

                foreach (KeyValuePair<UnitPart, Action> kv in partUnsub)
                {
                    try { kv.Value?.Invoke(); } catch { }
                }

                partUnsub.Clear();
                _sortieGate.Clear();
                _suppressUntil.Clear();

                if (aircraft.IsLanded() && aircraft.NetworkHQ.AnyNearAirbase(aircraft.transform.position, out Airbase _))
                    TriggerVoiceline(VoiceEvent.Disembark);
                else
                    TriggerVoiceline(VoiceEvent.Death);
            };
        }

        private void InitializeSet()
        {
            _setManager.Initialize(_audioRoot, SelectedSet.Value);
            eventPriorities = _setManager.EventPriorities;
        }

        private void ReloadCurrentVoicepack(bool isSpawning = false)
        {
            string[] availableSets = Directory.GetDirectories(_audioRoot).Select(Path.GetFileName).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            if (availableSets.Length == 0) return;

            string selectedPack = SelectedSet.Value;

            if (_ac != null)
            {
                if (_ac.definition != null)
                {
                    BindAircraftVoicepackConfig(_ac.definition); // Ensure config is bound if it wasn't discovered during scene load
                }

                if (RandomVoicepackOnSpawn.Value && isSpawning)
                {
                    selectedPack = availableSets[_rnd.Next(availableSets.Length)];
                    Log.LogInfo($"RandomVoicepackOnSpawn is enabled. Picked random set: {selectedPack}");
                }
                else
                {
                    string keyToLookup = (_ac.definition != null && !string.IsNullOrEmpty(_ac.definition.name)) ? _ac.definition.name : _ac.gameObject.name.Replace("(Clone)", "");
                    if (_aircraftVoicepacks.TryGetValue(keyToLookup, out var linkedPack))
                    {
                        string pack = linkedPack.Value;
                        if (pack != "Global Default" && !string.IsNullOrWhiteSpace(pack) && availableSets.Contains(pack))
                        {
                            selectedPack = pack;
                            Log.LogInfo($"Aircraft linked voicepack used for {keyToLookup}: {selectedPack}");
                        }
                    }
                }
            }

            _setManager.Initialize(_audioRoot, selectedPack);
            eventPriorities = _setManager.EventPriorities;
            Log.LogInfo($"Loaded voicepack: {selectedPack}");
        }

        int GetPriority(VoiceEvent e) => eventPriorities.TryGetValue(e, out int p) ? p : 0;

        public bool TriggerVoiceline(VoiceEvent requested, float suppressTime = 0.5f)
        {
            if (!_ac) return false;

            // Dynamic volume multiplier logic: instead of returning false (disabling the mod),
            // we will play at 0 volume if the conditions aren't met.
            // This allows the mod's internal state (cooldowns, suppressors) to remain updated.
            float volumeMultiplier = (_cachedCrewCapacity >= 2 || ImaginaryCopilot.Value) ? 1f : 0f;

            if (_aircraftToggles.TryGetValue(_ac.unitName, out var toggle) && !toggle.Value)
            {
                return false;
            }

            float now = Time.time;
            if (now >= _priorityExpiresAt) currentPriority = 0;

            VoiceEvent effective = requested;
            if (requested != VoiceEvent.RareClip &&
                _catalog.HasClips(VoiceEvent.RareClip) &&
                _rnd.Next(100) == 0 &&
                GetPriority(requested) <= GetPriority(VoiceEvent.RareClip))
            {
                effective = VoiceEvent.RareClip;
            }

            int effPriority = GetPriority(effective);

            if (now < _nextPlayableAt && effPriority <= currentPriority)
            {
                if (VerboseLogs.Value) Log.LogDebug($"Cooldown for [{effective}] (effPri {effPriority} <= currPri {currentPriority}).");
                return false;
            }

            bool Suppressed(VoiceEvent k) => _suppressUntil.TryGetValue(k, out float u) && now < u;
            if (Suppressed(requested) || Suppressed(effective)) return false;

            bool applyChanceGate = now >= _nextPlayableAt;
            int playChance = Mathf.Clamp(BaseChancePercent.Value + effPriority, 0, 100);
            if (applyChanceGate && _rnd.Next(100) >= playChance)
            {
                if (VerboseLogs.Value) Log.LogDebug($"Chance roll blocked [{effective}] at {playChance}% (priority {effPriority}, base {BaseChancePercent.Value}).");
                return false;
            }

            const int ExactWeight = 3;
            var pool = new List<(VoiceEvent evt, int weight)>(4);
            int totalWeight = 0;

            if (_catalog.HasClips(effective))
            {
                pool.Add((effective, ExactWeight));
                totalWeight += ExactWeight;
            }
            if (VoiceEventMaps.Fallbacks.TryGetValue(effective, out VoiceEvent[] chain))
            {
                foreach (VoiceEvent fb in chain)
                {
                    if (_catalog.HasClips(fb))
                    {
                        pool.Add((fb, 1));
                        totalWeight += 1;
                    }
                }
            }

            if (pool.Count == 0)
            {
                if (VerboseLogs.Value) Log.LogDebug($"No clips found for [{effective}] (requested [{requested}]) or its fallbacks.");
                return true;
            }

            AudioClip chosenClip = null;
            VoiceEvent chosenKey = effective;

            while (pool.Count > 0 && totalWeight > 0)
            {
                int roll = _rnd.Next(totalWeight);
                int acc = 0;
                int pick = 0;
                for (; pick < pool.Count; pick++)
                {
                    acc += pool[pick].weight;
                    if (roll < acc) break;
                }

                chosenKey = pool[pick].evt;
                chosenClip = _catalog.RequestClip(chosenKey);
                if (chosenClip != null) break;

                totalWeight -= pool[pick].weight;
                pool.RemoveAt(pick);
            }

            if (chosenClip == null)
            {
                if (VerboseLogs.Value) Log.LogDebug($"Weighted pool had candidates but none yielded a usable clip right now (effective [{effective}], requested [{requested}]).");
                return false;
            }

            if (VerboseLogs.Value && !chosenKey.Equals(effective)) Log.LogDebug($"Weighted pool picked fallback: [{effective}] -> [{chosenKey}]");

            if (Suppressed(chosenKey)) return false;
            float suppressUntil = now + suppressTime;
            _suppressUntil[requested] = suppressUntil;
            _suppressUntil[effective] = suppressUntil;
            _suppressUntil[chosenKey] = suppressUntil;

            _player.TryPlay(chosenClip, volumeMultiplier);
            
            if (VerboseLogs.Value && volumeMultiplier == 0f) Log.LogDebug($"[DEBUG] Muting voiceline: Pilot count is {_cachedCrewCapacity} (Imaginary Copilot: {ImaginaryCopilot.Value})");

            float cooldown = Mathf.Max(cooldownSeconds.Value, chosenClip.length);
            _nextPlayableAt = now + cooldown;
            _priorityExpiresAt = now + cooldown;
            currentPriority = Mathf.Max(effPriority, GetPriority(chosenKey));

            if (VerboseLogs.Value && !applyChanceGate) Log.LogDebug($"Played during cooldown preemption path.");

            Log.LogDebug($"Played [{requested}] (effective {effective}, actual {chosenKey}): {chosenClip.name} " +
                         $"(chance {playChance}%, pri {effPriority}, currPri-> {currentPriority}, cd {cooldown:0.00}s).");
            return true;
        }

        private void UpdateMws(Aircraft ac)
        {
            if (ac == null)
            {
                UnsubscribeMws();
                return;
            }

            if (_currentMws != null) return; // Already subscribed

            MissileWarning mws = ac.GetMissileWarningSystem();
            if (mws == null) mws = ac.GetComponentInChildren<MissileWarning>();
            
            if (mws != null)
            {
                _currentMws = mws;
                Log.LogInfo($"MWS FOUND on {ac.unitName}! Subscribing...");
                
                mws.onMissileWarning += OnMissileWarningHandler;
                mws.offMissileWarning += OffMissileWarningHandler;
            }
        }

        private void UnsubscribeMws()
        {
            if (_currentMws != null)
            {
                _currentMws.onMissileWarning -= OnMissileWarningHandler;
                _currentMws.offMissileWarning -= OffMissileWarningHandler;
                _currentMws = null;
            }
        }

        private void OnMissileWarningHandler(MissileWarning.OnMissileWarning e) => TriggerVoiceline(MissileOnKey(e.missile));
        private void OffMissileWarningHandler(MissileWarning.OffMissileWarning e) => TriggerVoiceline(MissileOffKey(e.missile));

        private VoiceEvent MissileOnKey(Missile m)
        {
            if (m.TryGetComponent(out SARHSeeker _)) return VoiceEvent.RwrOnFox1;
            if (m.TryGetComponent(out IRSeeker _)) return VoiceEvent.RwrOnFox2;
            if (m.TryGetComponent(out ARHSeeker _)) return VoiceEvent.RwrOnFox3;
            return VoiceEvent.RwrOn;
        }

        private VoiceEvent MissileOffKey(Missile m)
        {
            if (m.TryGetComponent(out SARHSeeker _)) return VoiceEvent.RwrOffFox1;
            if (m.TryGetComponent(out IRSeeker _)) return VoiceEvent.RwrOffFox2;
            if (m.TryGetComponent(out ARHSeeker _)) return VoiceEvent.RwrOffFox3;
            return VoiceEvent.RwrOff;
        }

        public class AircraftIdentity
        {
            public string GoName;
            public string UnitName;
            public string[] Aliases;
        }

        public static readonly AircraftIdentity[] AircraftCatalog =
        {
            new AircraftIdentity { GoName = "AttackHelo1",   UnitName = "SAH-46 Chicane",  Aliases = new[] { "AttackHelo1", "SAH-46 Chicane", "SAH-46L" } },
            new AircraftIdentity { GoName = "SmallFighter1", UnitName = "FS-20 Vortex",    Aliases = new[] { "SmallFighter1", "FS-20 Vortex", "FS-20B" } },
            new AircraftIdentity { GoName = "FastBomber1",   UnitName = "Alkyon AB-4",     Aliases = new[] { "FastBomber1", "Alkyon AB-4", "AB-4 Alkyon", "AB-4M" } },
            new AircraftIdentity { GoName = "UFO",           UnitName = "???",              Aliases = new[] { "UFO", "UF-0" } },
            new AircraftIdentity { GoName = "COIN",          UnitName = "CI-22 Cricket",    Aliases = new[] { "CI-22", "COIN" } },
            new AircraftIdentity { GoName = "EW1",           UnitName = "EW-25 Medusa",    Aliases = new[] { "EW1", "EW-25 Medusa", "EA-25 Medusa", "EA-25B" } },
            new AircraftIdentity { GoName = "QuadVTOL1",     UnitName = "VL-49 Tarantula", Aliases = new[] { "QuadVTOL1", "VL-49 Tarantula", "VL-49D" } },
            new AircraftIdentity { GoName = "Darkreach",     UnitName = "SFB-81 Darkreach", Aliases = new[] { "SFB", "SFB-81" } },
            new AircraftIdentity { GoName = "Trainer",       UnitName = "T/A-30 Compass",   Aliases = new[] { "TA-30 Compass", "T/A-30 Compass", "TA-30YH" } },
            new AircraftIdentity { GoName = "UtilityHelo1",  UnitName = "UH-90 Ibis",      Aliases = new[] { "UH-90 Ibis", "UH-90K" } },
            new AircraftIdentity { GoName = "CAS1",          UnitName = "A-19 Brawler",    Aliases = new[] { "A-19 Brawler", "A-19C" } },
            new AircraftIdentity { GoName = "FS-12",         UnitName = "FS-12 Revoker",   Aliases = new[] { "FS-12 Revoker", "FS-12V", "Fighter1" } },
            new AircraftIdentity { GoName = "Multirole1",    UnitName = "KR-67 Ifrit",     Aliases = new[] { "KR-67 Ifrit", "KR-67A" } },

            new AircraftIdentity { GoName = "Aryx_F16M_KingViper",   UnitName = "F-16M King Viper", Aliases = new[] { "Aryx_F16M_KingViper_AircraftDefinition" } },
            new AircraftIdentity { GoName = "Aryx_LightHelicopter1", UnitName = "RAH-72 Knockout",  Aliases = new[] { "Aryx_LightHelicopter1_Definition" } },
            new AircraftIdentity { GoName = "Aryx_LightFighter1",    UnitName = "F-99 Shrike",      Aliases = new[] { "Aryx_LightFighter1_Definition" } },
            new AircraftIdentity { GoName = "Aryx_MiG-15",           UnitName = "MiG-15",           Aliases = new[] { "Aryx_MiG-15_AircraftDefinition" } },
            new AircraftIdentity { GoName = "P_Trisurface1",         UnitName = "FS-3 Ternion",    Aliases = new[] { "P_Trisurface1_definition", "FS-3 Ternion", "FS-3E" } },
            new AircraftIdentity { GoName = "Aryx_MC260_Chimera",    UnitName = "MC-260 Chimera",   Aliases = new[] { "Aryx_MC260_Chimera_Definition" } },
            new AircraftIdentity { GoName = "Kestrel",               UnitName = "FQ-106 Kestrel",   Aliases = new[] { "kestrel_definition" } },
            new AircraftIdentity { GoName = "VTOLTrainer1",          UnitName = "VT-7 Vagrant",     Aliases = new[] { "VTOLTrainer1", "VT-7 Vagrant", "VT-7" } },
        };

        public static string GetFriendlyAircraftName(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var ident in AircraftCatalog)
            {
                if (string.Equals(ident.GoName, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ident.UnitName, key, StringComparison.OrdinalIgnoreCase) ||
                    (ident.Aliases != null && ident.Aliases.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase))))
                {
                    return ident.UnitName;
                }
            }
            return null;
        }

        private void BindAircraftVoicepackConfig(AircraftDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.name) || def.unitPrefab == null) return;
            string acKey = def.name;
            if (_aircraftToggles.ContainsKey(acKey)) return;

            bool isControllable = def.unitPrefab.GetComponentInChildren<ControlsFilter>(true) != null;
            if (isControllable)
            {
                string friendlyName = GetFriendlyAircraftName(acKey) ?? def.unitName;
                if (string.IsNullOrEmpty(friendlyName)) friendlyName = acKey;

                var attr = new ConfigurationManagerAttributes { Category = "Aircraft", Order = 0, HideDefaultButton = true, DispName = $"{friendlyName} - Enable Yappinator" };
                _aircraftToggles[acKey] = Config.Bind("Aircraft", $"{acKey}_Enable", true,
                    new ConfigDescription($"Enable mod for {friendlyName} ({acKey})", null, attr));

                Aircraft acPrefab = def.unitPrefab.GetComponent<Aircraft>();
                int crewCount = GetCrewCapacity(acPrefab);
                string crewSuffix = crewCount > 1 ? "multiseater" : "singleseater";
                string dispName = $"{friendlyName} {crewSuffix}";

                var sets = Directory.GetDirectories(_audioRoot).Select(Path.GetFileName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                sets.Insert(0, "Global Default");
                string firstSet = "Global Default";

                var voiceAttr = new ConfigurationManagerAttributes { Category = "Aircraft", Order = 0, HideDefaultButton = true, DispName = $"{friendlyName} - Voicepack" };
                var entry = Config.Bind("Aircraft", $"{acKey}_Voicepack", firstSet,
                    new ConfigDescription($"Selected voicepack for {dispName} ({acKey})", new AcceptableValueList<string>(sets.ToArray()), voiceAttr));
                entry.SettingChanged += (_, __) => ReloadCurrentVoicepack();
                _aircraftVoicepacks[acKey] = entry;

                if (VerboseLogs.Value) Log.LogInfo($"Discovered controllable aircraft dynamically: {acKey} ({friendlyName})");
            }
        }

        private void DiscoverAircraftAndBindConfig()
        {
            AircraftDefinition[] allAircraft = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            if (allAircraft == null) return;

            foreach (AircraftDefinition def in allAircraft)
            {
                BindAircraftVoicepackConfig(def);
            }
        }

        private bool IsRotary(Aircraft ac)
        {
            if (ac == null) return false;
            // Helicopters in Nuclear Option typically have a Helicopter component or can be identified by type
            return ac.GetComponent("Helicopter") != null;
        }

        private int GetCrewCapacity(Aircraft ac)
        {
            if (ac == null) return 0;

            try
            {
                // Based on diagnostic logs, the Aircraft class has a 'pilots' field (Pilot[]).
                // We access it via reflection to get the definitive crew count.
                var pilotsField = ac.GetType().GetField("pilots", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                if (pilotsField != null)
                {
                    var pilots = (Array)pilotsField.GetValue(ac);
                    if (pilots != null)
                    {
                        int count = pilots.Length;
                        if (VerboseLogs.Value) Log.LogInfo($"[DEBUG] Definitive pilot detection for {ac.unitName}: {count}");
                        return count;
                    }
                }
            }
            catch (Exception e)
            {
                if (VerboseLogs.Value) Log.LogWarning($"Reflection-based pilot detection failed: {e.Message}");
            }

            // Fallback for unexpected cases
            return 1;
        }

        private void DumpAircraftComponents(Aircraft ac)
        {
            if (ac == null) return;
            Log.LogInfo($"[DIAGNOSTIC] --- Deep Scan for {ac.unitName} ---");

            // 1. Log all components
            var comps = ac.GetComponentsInChildren<Component>(true);
            HashSet<string> compNames = new HashSet<string>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                compNames.Add(c.GetType().Name);
            }
            Log.LogInfo($"[DIAGNOSTIC] Components found: {string.Join(", ", compNames)}");

            // 2. Log all fields on the Aircraft/Unit types
            try
            {
                var type = ac.GetType();
                Log.LogInfo($"[DIAGNOSTIC] Fields on {type.Name}:");
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                foreach (var f in fields)
                {
                    Log.LogInfo($"[DIAGNOSTIC]   Field: {f.Name} ({f.FieldType.Name})");
                }

                Log.LogInfo($"[DIAGNOSTIC] Properties on {type.Name}:");
                var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                foreach (var p in props)
                {
                    Log.LogInfo($"[DIAGNOSTIC]   Property: {p.Name} ({p.PropertyType.Name})");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"[DIAGNOSTIC] Reflection error: {e.Message}");
            }
            
            Log.LogInfo($"[DIAGNOSTIC] --- Scan Complete ---");
        }

        /// <summary>
        /// Helper class for BepInEx.ConfigurationManager metadata.
        /// </summary>
        public class ConfigurationManagerAttributes
        {
            public bool? Browsable { get; set; }
            public string Category { get; set; }
            public object DefaultValue { get; set; }
            public string Description { get; set; }
            public string DispName { get; set; }
            public int? Order { get; set; }
            public bool? IsAdvanced { get; set; }
            public bool? ReadOnly { get; set; }
            public bool? HideDefaultButton { get; set; }
            public bool? HideSettingName { get; set; }
            public Action<ConfigEntryBase> CustomDrawer { get; set; }
        }
    }
}

