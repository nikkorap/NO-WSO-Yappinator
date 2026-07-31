using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WSOYappinator.Patches
{
    [HarmonyPatch(typeof(MountedMissile), nameof(MountedMissile.Fire))]
    internal static class FireMissilePatch
    {
        private static void Postfix(MountedMissile __instance, Unit owner, Unit target, Vector3 inheritedVelocity,
                                    WeaponStation weaponStation, GlobalPosition aimpoint)
        {
            if (GameManager.IsLocalAircraft(owner as Aircraft)) Plugin.instance.TriggerVoiceline(ResolveFireEvent(__instance));
        }

        private static readonly Dictionary<WeaponInfo, VoiceEvent> _fireEventCache = new Dictionary<WeaponInfo, VoiceEvent>();

        private static VoiceEvent ResolveFireEvent(MountedMissile mm)
        {
            WeaponInfo info = mm?.info;
            if (info == null) return VoiceEvent.fireMissile;
            if (_fireEventCache.TryGetValue(info, out VoiceEvent cached)) return cached;

            VoiceEvent result;
            if (info.nuclear) result = VoiceEvent.fireNuclear;
            else if (info.bomb || info.name.IndexOf("glide", StringComparison.OrdinalIgnoreCase) >= 0) result = VoiceEvent.fireBomb;
            else if (info.laserGuided) result = VoiceEvent.fireAGR;
            else
            {
                MissileSeeker seeker = info.weaponPrefab ? info.weaponPrefab.GetComponent<MissileSeeker>() : null;
                if (seeker is IRSeeker) result = VoiceEvent.fireFox2;
                else if (seeker is ARMSeeker) result = VoiceEvent.fireARM;
                else if (seeker is ARHSeeker) result = VoiceEvent.fireFox3;
                else if (seeker is OpticalSeeker) result = VoiceEvent.fireAGM;
                else if (seeker is OpticalSeekerCruiseMissile) result = VoiceEvent.fireCruise;
                else result = VoiceEvent.fireMissile;
            }

            _fireEventCache[info] = result;
            return result;
        }

    }
}