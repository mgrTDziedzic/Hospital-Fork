using HarmonyLib;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace Hospital.Patches;

// Hides Patient Landing Spot variants from the build menu (setting off or invalid def).
// Placed variants keep working. The "copy" gizmo on a placed variant also uses Designator_Build.Visible, so it is hidden too.
[HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.Visible), MethodType.Getter)]
public static class LandingSpotVariantsPatch
{
    public static void Postfix(Designator_Build __instance, ref bool __result)
    {
        if (!__result) return;
        if (__instance.PlacingDef is ThingDef def && !LandingSpotUtility.IsBuildable(def))
            __result = false;
    }
}
