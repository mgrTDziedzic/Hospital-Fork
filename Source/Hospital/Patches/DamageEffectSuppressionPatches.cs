using HarmonyLib;
using Hospital.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.Patches;

/*
 * Arrival wounds are applied to a pawn that is spawned on the landing cell for a moment, before it is put
 * in a drop pod. The vanilla damage side effects would show on that cell (blood filth, "Headshot" text, wound
 * voice, damage effecters). While PatientUtility.SuppressDamageEffects is on (only inside DamagePawn) these
 * prefixes skip them. Outside that scope they do nothing, so normal combat is unaffected.
 */
[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodFilth))]
public static class SuppressBloodFilthPatch
{
    public static bool Prefix() => !PatientUtility.SuppressDamageEffects;
}

[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodSmear))]
public static class SuppressBloodSmearPatch
{
    public static bool Prefix() => !PatientUtility.SuppressDamageEffects;
}

[HarmonyPatch(typeof(MoteMaker), nameof(MoteMaker.ThrowText), typeof(Vector3), typeof(Map), typeof(string), typeof(Color), typeof(float))]
public static class SuppressThrowTextPatch
{
    public static bool Prefix() => !PatientUtility.SuppressDamageEffects;
}

[HarmonyPatch(typeof(MoteMaker), nameof(MoteMaker.ThrowText), typeof(Vector3), typeof(Map), typeof(string), typeof(float))]
public static class SuppressThrowTextShortPatch
{
    public static bool Prefix() => !PatientUtility.SuppressDamageEffects;
}

[HarmonyPatch(typeof(DamageWorker_AddInjury), "PlayWoundedVoiceSound")]
public static class SuppressWoundedVoicePatch
{
    public static bool Prefix() => !PatientUtility.SuppressDamageEffects;
}

[HarmonyPatch(typeof(Effecter), nameof(Effecter.Trigger), typeof(TargetInfo), typeof(TargetInfo), typeof(int))]
public static class SuppressEffecterTriggerPatch
{
    // Trigger returns the effecter itself; callers (the wounded effecter) keep it and Cleanup it later.
    public static bool Prefix(Effecter __instance, ref Effecter __result)
    {
        if (!PatientUtility.SuppressDamageEffects) return true;
        __result = __instance;
        return false;
    }
}
