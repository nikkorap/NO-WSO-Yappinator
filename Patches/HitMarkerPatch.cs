using HarmonyLib;
using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.Text;

namespace WSOYappinator.Patches
{
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.DisplayHit))]
    internal static class HitMarkerPatch
    {
        private static void Postfix(CombatHUD __instance, GlobalPosition hitPosition, Unit hitUnit)
        {
            if (!GameManager.IsLocalAircraft(__instance.aircraft)) return;
            Plugin.instance.TriggerVoiceline(VoiceEvent.HitMarker);
        }
    }
}
