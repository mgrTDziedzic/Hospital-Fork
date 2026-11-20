using System.Linq;
using Verse;

namespace Hospital.Utilities;

// Marker extension on every Patient Landing Spot def (default 1x1 and the cosmetic variants).
// variant = true: hidden from the build menu when the setting is off or the def is invalid.
public class PatientLandingSpotExtension : DefModExtension
{
    public bool variant;
}

[StaticConstructorOnStartup]
public static class LandingSpotUtility
{
    static LandingSpotUtility()
    {
        // Failsafe: report invalid variant defs once. They are hidden from the build menu and ignored as landing spots.
        foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            var ext = def.GetModExtension<PatientLandingSpotExtension>();
            if (ext != null && ext.variant && !IsValidLandingDef(def))
                Log.Error("Hospital: patient landing spot variant " + def.defName + " must be square, odd-sized and not rotatable (size " + def.size + ", rotatable " + def.rotatable + "). It is hidden and ignored.");
        }
    }

    public static bool IsLandingSpotDef(ThingDef def) => def?.GetModExtension<PatientLandingSpotExtension>() != null;

    public static bool IsVariantDef(ThingDef def) => def?.GetModExtension<PatientLandingSpotExtension>()?.variant == true;

    // Odd square and not rotatable: then Thing.Position is the centre cell of the footprint.
    public static bool IsValidLandingDef(ThingDef def) => def != null && def.size.x == def.size.z && def.size.x % 2 == 1 && !def.rotatable;

    // True when the build menu may show this def (the default spot is always shown).
    public static bool IsBuildable(ThingDef def)
    {
        if (!IsVariantDef(def)) return true;
        return HospitalMod.Settings.EnableLandingSpotVariants && IsValidLandingDef(def);
    }
}
