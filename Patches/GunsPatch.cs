using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace WSOYappinator.Patches
{    
    [HarmonyPatch(typeof(HUDBoresightState), "DisplayLead")]
    internal static class GunsPatch
    {
        private static bool _lastBoth;
        private static void Postfix(bool __result, ref bool lookingAtTarget)
        {
            bool both = __result && lookingAtTarget;

            if (both && !_lastBoth)
                Plugin.instance.TriggerVoiceline(VoiceEvent.guns);

            _lastBoth = both;
        }
    }
}
