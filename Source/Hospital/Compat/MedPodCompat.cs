using Verse;

namespace Hospital.Compat;

// MedPod (sumghai.Medpod). MedPod beds are recognised by their thing class (type name, no assembly reference),
// not by a "Pod" substring in the defName. Without the mod nothing happens and nothing is logged.
// The think tree entry for MedPod patients is added in Patches/MedPod.xml (MayRequireAnyOf).
public static class MedPodCompat
{
        private const string BedClass = "MedPod.Building_BedMedPod";

        public static bool IsMedPodBed(ThingDef def)
        {
                if (def == null || !SupportedMods.IsActive(SupportedMods.MedPod)) return false;
                for (var type = def.thingClass; type != null; type = type.BaseType)
                {
                        if (type.FullName == BedClass) return true;
                }
                return false;
        }

        // Called once at startup (HospitalMod).
        public static void Startup()
        {
                var key = SupportedMods.MedPod;
                if (SupportedMods.IsActive(key)) Log.Message("[Hospital] " + SupportedMods.DisplayName(key) + " (" + SupportedMods.ActiveId(key) + ") detected, compatibility enabled");
        }
}
