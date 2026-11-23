using System;
using System.Reflection;
using HarmonyLib;
using Hospital.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.Compat;

// IV Drug Infuser (akaster.ivdruginfuser) soft compatibility, by reflection on type names (no assembly reference).
// The infuser doses the pawn in a linked bed straight from its fuel tank, without a bill, tend or feed job, so none of
// Hospital's billing hooks fire. Every dose goes through IVDrugInfuser.Helper.Infused(Thing, Pawn); it is patched here
// (Prefix + Postfix) to bill active patients and to record the dose for the patient drug policy.
// Mod absent: nothing happens and nothing is logged. Present: one info line at startup. API changed or an exception:
// one info line, compat off for the rest of the session.
public static class IVDrugInfuserCompat
{
        private const string HelperType = "IVDrugInfuser.Helper";
        private const string MethodName = "Infused";

        private static bool disabled;

        // Called once at startup (HospitalMod).
        public static void Startup()
        {
                var key = SupportedMods.IVDrugInfuser;
                if (!SupportedMods.IsActive(key)) return;
                var label = SupportedMods.DisplayName(key) + " (" + SupportedMods.ActiveId(key) + ")";
                try
                {
                        var type = AccessTools.TypeByName(HelperType);
                        var method = type?.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static, null,
                                new[] { typeof(Thing), typeof(Pawn) }, null);
                        if (method == null)
                        {
                                Log.Message("[Hospital] " + label + " detected, but its API differs: compatibility disabled");
                                return;
                        }
                        var harmony = HospitalMod.harmonyInstance;
                        harmony.Patch(method,
                                prefix: new HarmonyMethod(typeof(IVDrugInfuserCompat), nameof(Prefix)),
                                postfix: new HarmonyMethod(typeof(IVDrugInfuserCompat), nameof(Postfix)));
                        PatchTakeDrug(harmony, label);
                        Log.Message("[Hospital] " + label + " detected, compatibility enabled");
                }
                catch (Exception e)
                {
                        Log.Message("[Hospital] " + label + " detected, but compatibility failed to start and is disabled: " + e.GetType().Name);
                }
        }

        // Dose limiting: CompMultiRefuelable.TakeDrug (private) is the single place a dose starts, before ConsumeFuel(1f).
        // Skipping it keeps the fuel; the infuser retries on its next rare tick. Needs the Pawn (protected) and FuelThingDef getters.
        private static MethodInfo pawnGetter, fuelDefGetter;
        private static bool limitOff;
        private static readonly System.Collections.Generic.HashSet<string> withheldLogged = new System.Collections.Generic.HashSet<string>();

        private static void PatchTakeDrug(Harmony harmony, string label)
        {
            try
            {
                var comp = AccessTools.TypeByName("IVDrugInfuser.CompMultiRefuelable");
                var take = comp == null ? null : AccessTools.Method(comp, "TakeDrug", Type.EmptyTypes);
                pawnGetter = comp == null ? null : AccessTools.PropertyGetter(comp, "Pawn");
                fuelDefGetter = comp == null ? null : AccessTools.PropertyGetter(comp, "FuelThingDef");
                if (take == null || pawnGetter == null || fuelDefGetter == null
                    || pawnGetter.ReturnType != typeof(Pawn) || fuelDefGetter.ReturnType != typeof(ThingDef))
                {
                        limitOff = true;
                        Log.Message("[Hospital] " + label + ": dose limiting by patient drug policy is off (API differs), billing stays on");
                        return;
                }
                harmony.Patch(take, prefix: new HarmonyMethod(typeof(IVDrugInfuserCompat), nameof(TakeDrugPrefix)));
            }
            catch (Exception e)
            {
                limitOff = true;
                Log.Message("[Hospital] " + label + ": dose limiting by patient drug policy failed to start and is off: " + e.GetType().Name);
            }
        }

        // false = skip the dose. Whitelist for active patients: medicine always; other drugs only when a patient drug policy allows them now.
        public static bool TakeDrugPrefix(object __instance)
        {
            if (limitOff || disabled) return true;
            try
            {
                var pawn = pawnGetter.Invoke(__instance, null) as Pawn;
                if (pawn == null || !pawn.IsPatient(out var hospital, activeOnly: true)) return true;
                var def = fuelDefGetter.Invoke(__instance, null) as ThingDef;
                if (def == null || def.IsMedicine) return true;
                if (!hospital.GetPatientData(pawn, out var data)) return true;
                if (DrugUtility.PolicyAllowsNow(hospital, pawn, data, def, Find.TickManager.TicksGame))
                {
                    if (Prefs.DevMode) withheldLogged.Remove(pawn.ThingID + def.defName);
                    return true;
                }
                if (Prefs.DevMode && withheldLogged.Add(pawn.ThingID + def.defName))
                    Log.Message("[Hospital] IV Drug Infuser: " + def.defName + " withheld from " + pawn.LabelShortCap + " (not allowed by patient drug policy / not due yet)");
                return false;
            }
            catch (Exception e)
            {
                limitOff = true;
                Log.Message("[Hospital] IV Drug Infuser dose limiting disabled after an error: " + e.GetType().Name);
                return true;
            }
        }

        // The thing is destroyed inside Infused: keep its def.
        public static void Prefix(Thing thing, out ThingDef __state)
        {
                __state = thing?.def;
        }

        public static void Postfix(Pawn infusedPawn, ThingDef __state)
        {
                if (disabled || __state == null) return;
                try
                {
                        Bill(infusedPawn, __state);
                }
                catch (Exception e)
                {
                        disabled = true;
                        Log.Message("[Hospital] IV Drug Infuser compatibility disabled after an error: " + e.GetType().Name);
                }
        }

        // Price: medicine uses the tend rule, everything else the administer-bill rule.
        public static float PriceOf(ThingDef def)
        {
                return def.IsMedicine
                        ? Mathf.Clamp(def.BaseMarketValue * 1.2f, 0, 70)
                        : Mathf.Max(def.BaseMarketValue, 10f);
        }

        internal static void Bill(Pawn pawn, ThingDef def)
        {
                if (pawn == null || !pawn.IsPatient(out var hospital, activeOnly: true)) return;
                if (!hospital.GetPatientData(pawn, out var data)) return;
                pawn.AddToBill(hospital, PriceOf(def), Prefs.DevMode ? "IV Drug Infuser: " + def.label : null);
                data.LastDrugTick[def] = Find.TickManager.TicksGame;
                // A waiting Administer bill for the same drug would give a second dose.
                if (!def.IsMedicine) DrugUtility.RemoveAdministerBillsFor(pawn, def);
        }
}
