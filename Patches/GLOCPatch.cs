using HarmonyLib;
using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.Text;

namespace WSOYappinator.Patches
{
    [HarmonyPatch(typeof(GLOC), "LOC")]
    internal static class SimulateGlocPatch
    {
        private static void Postfix(GLOC __instance)
        {
            Plugin.instance.TriggerVoiceline(VoiceEvent.onGLOC);
        }
    }
}
