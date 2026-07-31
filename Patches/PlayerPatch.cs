using HarmonyLib;
using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.Text;
/*
namespace WSOYappinator.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.SetAircraft))]
    internal static class PlayerPatch
    {
        private static void Postfix(Player __instance, Aircraft aircraft)
        {
            if (GameManager.IsLocalPlayer(__instance) && __instance.Aircraft != null) Plugin.i.HandleLocalAircraftChanged(aircraft);
        }
    }
}*/