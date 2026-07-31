using System;
using System.Collections.Generic;
using System.Linq;

namespace WSOYappinator
{
    public static class VoiceEventMaps
    {
        public static readonly Dictionary<string, VoiceEvent> StringAliases =
            new Dictionary<string, VoiceEvent>(StringComparer.OrdinalIgnoreCase)
            {
                ["jump"] = VoiceEvent.RareClip,
                ["death"] = VoiceEvent.Death,
                ["damage"] = VoiceEvent.takeDamage,
                ["rwr"] = VoiceEvent.RwrOn,
                ["kill"] = VoiceEvent.killGeneric,
                ["hit"] = VoiceEvent.HitMarker,
                ["nuclear"] = VoiceEvent.fireNuclear,
                ["bomb"] = VoiceEvent.fireBomb,
                ["missile"] = VoiceEvent.fireMissile,
                ["fox2"] = VoiceEvent.fireFox2,
                ["fox3"] = VoiceEvent.fireFox3,
                ["guns"] = VoiceEvent.guns,
                ["spawn"] = VoiceEvent.Spawn,
                ["rwr_on"] = VoiceEvent.RwrOn,
                ["rwr_off"] = VoiceEvent.RwrOff,
                ["rwr_on_fox1"] = VoiceEvent.RwrOnFox1,
                ["rwr_on_fox2"] = VoiceEvent.RwrOnFox2,
                ["rwr_on_fox3"] = VoiceEvent.RwrOnFox3,
                ["rwr_off_fox1"] = VoiceEvent.RwrOffFox1,
                ["rwr_off_fox2"] = VoiceEvent.RwrOffFox2,
                ["rwr_off_fox3"] = VoiceEvent.RwrOffFox3,
            };

        public static readonly Dictionary<VoiceEvent, VoiceEvent[]> Fallbacks = new Dictionary<VoiceEvent, VoiceEvent[]>()
        {
            { VoiceEvent.fireFox2,new[] { VoiceEvent.fireMissile } },
            { VoiceEvent.fireFox3,new[] { VoiceEvent.fireMissile } },
            { VoiceEvent.fireAGM,new[] { VoiceEvent.fireMissile } },
            { VoiceEvent.fireARM,new[] { VoiceEvent.fireMissile } },
            { VoiceEvent.fireAGR,new[] { VoiceEvent.fireMissile } },
            { VoiceEvent.fireCruise,new[] { VoiceEvent.fireMissile } },

            { VoiceEvent.RwrOnFox1,new[] { VoiceEvent.RwrOn } },
            { VoiceEvent.RwrOnFox2,new[] { VoiceEvent.RwrOn } },
            { VoiceEvent.RwrOnFox3,new[] { VoiceEvent.RwrOn } },

            { VoiceEvent.RwrOffFox1,new[] { VoiceEvent.RwrOff } },
            { VoiceEvent.RwrOffFox2,new[] { VoiceEvent.RwrOff } },
            { VoiceEvent.RwrOffFox3,new[] { VoiceEvent.RwrOff } },

            { VoiceEvent.engineDamage,new[] { VoiceEvent.takeDamage } },

            { VoiceEvent.killShip,new[] { VoiceEvent.killGeneric} },
            { VoiceEvent.killMissile,new[] { VoiceEvent.killGeneric} },
            { VoiceEvent.killAircraft,new[] { VoiceEvent.killGeneric} },
            { VoiceEvent.killBuilding,new[] { VoiceEvent.killGround, VoiceEvent.killGeneric } },
            { VoiceEvent.killGround,new[] { VoiceEvent.killGeneric} },
        };

        public static bool TryParse(string token, out VoiceEvent evt)
        {
            if (!string.IsNullOrEmpty(token) && token.All(char.IsDigit))
            {
                evt = default;
                return false;
            }
            if (string.IsNullOrEmpty(token) || !char.IsLetter(token[0])) { evt = default; return false; }
            if (Enum.TryParse(token, true, out evt) && Enum.IsDefined(typeof(VoiceEvent), evt)) return true;
            if (StringAliases.TryGetValue(token, out evt)) return true;

            evt = default;
            return false;
        }

        public static IEnumerable<VoiceEvent> ExpandWithFallbacks(VoiceEvent evt)
        {
            yield return evt;
            if (Fallbacks.TryGetValue(evt, out VoiceEvent[] chain))
            {
                foreach (VoiceEvent f in chain) yield return f;
            }
        }
    }
}
