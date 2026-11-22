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
                        Log.Message("[Hospital] " + label + " detected, compatibility enabled");
                }
                catch (Exception e)
                {
                        Log.Message("[Hospital] " + label + " detected, but compatibility failed to start and is disabled: " + e.GetType().Name);
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
                pawn.AddToBill(hospital, PriceOf(def));
                data.LastDrugTick[def] = Find.TickManager.TicksGame;
        }
}
