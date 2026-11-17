using RimWorld;
using Verse;

namespace Hospital.Compat;

// Plasteel Surgery (Continued) is supported without any reference to it (default blacklist by defName,
// worker check by type name). Not installed: nothing happens and nothing is logged.
public static class PlasteelCompat
{
        // Called once at startup (HospitalMod).
        public static void Startup()
        {
                var key = SupportedMods.PlasteelSurgery;
                if (SupportedMods.IsActive(key)) Log.Message("[Hospital] " + SupportedMods.DisplayName(key) + " (" + SupportedMods.ActiveId(key) + ") detected, compatibility enabled");
        }
}
