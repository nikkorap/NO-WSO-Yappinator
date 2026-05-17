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
            if (fuelRatio < Plugin.I.fuelRatioBingo.Value) Plugin.I.TryGated(VoiceEvent.fuelLow);
            if (__instance.radarAlt < Plugin.I.lowAltitude.Value && __instance.speed > 100f) Plugin.I.TryGated(VoiceEvent.lowflying);
            if (__instance.radarAlt > Plugin.I.highAltitude.Value && __instance.speed > 100f) Plugin.I.TryGated(VoiceEvent.highflying);
        }
    }
}
