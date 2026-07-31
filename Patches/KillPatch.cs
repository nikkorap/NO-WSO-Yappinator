using HarmonyLib;
using NuclearOption.Networking;

namespace WSOYappinator.Patches
{
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.ReportKillAction))]
    internal static class KillPatch
    {
        private static void Postfix(Player player, Unit target, float factor)
        {
            if (!GameManager.IsLocalPlayer(player)) return;

            VoiceEvent evt = VoiceEvent.killGeneric;
            if (target is GroundVehicle) evt = VoiceEvent.killGround;
            else if (target is Building) evt = VoiceEvent.killBuilding;
            else if (target is Aircraft) evt = VoiceEvent.killAircraft;
            else if (target is Missile) evt = VoiceEvent.killMissile;
            else if (target is Ship) evt = VoiceEvent.killShip;
            
            Plugin.instance.TriggerVoiceline(evt);
        } 
    }
}