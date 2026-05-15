using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace WSOYappinator.Patches
{
    [HarmonyPatch]
    internal static class CountermeasureStation_Fire_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.Inner(typeof(CountermeasureManager), "CountermeasureStation"), "Fire", [typeof(Aircraft)]);
        static void Postfix(CountermeasureManager __instance, Aircraft aircraft)
        {
            if (!GameManager.IsLocalAircraft(aircraft)) return;

            int ammo = Traverse.Create(__instance).Field("ammo").GetValue<int>();

            if (ammo == 0) {
                // dummy to prevent triggering 'flares' on zero
            }
            else if (ammo == 1) 
            {
                Plugin.I.TriggerVoiceline(VoiceEvent.jammer);
            }
            else if (ammo is >= 2 and <= 4) 
            {
                Plugin.I.TriggerVoiceline(VoiceEvent.noFlares, 10f);
            }
            else if (ammo is > 4 and <= 20) 
            {
                Plugin.I.TriggerVoiceline(VoiceEvent.lowFlares);
            }
            else
            {
                Plugin.I.TriggerVoiceline(VoiceEvent.flares);
            }
        }
    }
}
