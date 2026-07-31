using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace WSOYappinator.Patches
{
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.CheckNeedsFuel))]
    internal static class AircraftPatches
    {
        private static void Postfix(Aircraft __instance, float fuelRatio)
        {
            if (!GameManager.IsLocalAircraft(__instance)) return;
            if (fuelRatio < 0.2f) Plugin.instance.TryGated(VoiceEvent.fuelLow);
            if (__instance.radarAlt < 5f && __instance.speed > 100f) Plugin.instance.TryGated(VoiceEvent.lowflying);
            if (__instance.radarAlt > 15000f && __instance.speed > 100f) Plugin.instance.TryGated(VoiceEvent.highflying);
        }
    }
}
