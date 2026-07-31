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
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.Inner(typeof(CountermeasureManager), "CountermeasureStation"), "Fire", new Type[] { typeof(Aircraft) });
        static void Postfix(CountermeasureManager __instance, Aircraft aircraft)
        {
            if (!GameManager.IsLocalAircraft(aircraft)) return;

            int ammo = Traverse.Create(__instance).Field("ammo").GetValue<int>();
            if (ammo > 1 && ammo <= 5) Plugin.instance.TriggerVoiceline(VoiceEvent.noFlares, 10f);
        }
    }
}
