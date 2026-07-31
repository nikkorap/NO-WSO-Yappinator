namespace WSOYappinator
{
    public enum VoiceEvent
    {
        Death,
        Eject,
        engineLost,
        onGLOC,
        fireNuclear,
        partDetach,

        // Enginedamage, takeDamage is fallback
        engineDamage,
        takeDamage,
        noFlares,

        // RWR alerts, RwrOn is fallback
        RwrOnFox3,
        RwrOnFox2,
        RwrOnFox1,
        RwrOn,
        radarPingLocked,
        beingJammed,

        // Kills, killgeneric is fallback
        killAircraft,
        killShip,
        killGround,
        killMissile,
        killBuilding,
        killGeneric,

        HitMarker,

        // Missiles, fireMissile is fallback
        fireFox2,
        fireFox3,
        fireAGM,
        fireARM,
        fireAGR,
        fireCruise,
        fireMissile,

        fireBomb,
        guns,
        Disembark,
        onSortieSuccess,
        Touchdown,
        RareClip,
        Spawn,
        radarPingNew,

        // RWR stop alerts, RwrOff is fallback
        RwrOffFox3,
        RwrOffFox2,
        RwrOffFox1,
        RwrOff,

        Rearm,
        fuelLow,
        lowflying,
        highflying,
        GearUp,
        GearDown,
        FlightAssistOn,
        FlightAssistOff,
        AutohoverOn,
        AutohoverOff
    }
}
/*
noAmmo,
fireTroops,
idleChatter,
burning,
fuelLeak,
hookAttached, 
hookReleased, 
cargoDeployed,
waypointReached,
objectiveComplete,
objectiveFailed,
objectiveDestroyed,
missionVictory,
missionDefeat,
firstNukeStrike,*/