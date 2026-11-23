using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.Utilities
{
    public static class PatientUtility
    {
        // Map.GetComponent<T> is a linear scan over all map components (mod components sit at the
        // end), and IsPatient runs in hot paths such as JobGiver_Work.PawnCanUseWorkGiver (every
        // pawn x every work giver). Weak keys: a discarded Map drops its entry with it.
        private static readonly ConditionalWeakTable<Map, HospitalMapComponent> HospitalByMap =
            new ConditionalWeakTable<Map, HospitalMapComponent>();

        public static HospitalMapComponent HospitalOf(Map map)
        {
            if (map == null) return null;
            if (HospitalByMap.TryGetValue(map, out var cached)) return cached;
            var hospital = map.GetComponent<HospitalMapComponent>();
            // While a map is being loaded its component list is built and then replaced by the
            // saved components (Map.ExposeComponents), so only cache outside a Scribe pass.
            if (hospital != null && Scribe.mode == LoadSaveMode.Inactive) HospitalByMap.Add(map, hospital);
            return hospital;
        }

        // activeOnly = false (default): true for every registered patient on the pawn's map, also discharged (Dismissed) ones.
        // activeOnly = true: true only for registered patients that are not Dismissed. Needs pawn.Map != null.
        public static bool IsPatient(this Pawn pawn,out HospitalMapComponent hospital, bool activeOnly = false)
        {
            hospital = null;
            if (pawn?.Map == null) return false;
            hospital = HospitalOf(pawn.Map);
            if (hospital == null) return false;
            return hospital.GetPatientData(pawn, out var patientData) && (!activeOnly || !patientData.Dismissed);
        }
        public static void AddToBill(this Pawn pawn, HospitalMapComponent hospital, float silver, string reason = null)
        {
            if (pawn == null) return;
            if (!hospital.GetPatientData(pawn, out var patientData)) return;
            float before = patientData.Bill;
            patientData.Bill += silver;
            patientData.Bill = Mathf.Clamp(patientData.Bill, 0, 4000);
            LogBill(pawn, patientData.Bill - before, patientData.Bill, reason);
        }

        // Dev mode only. DevMode is checked first so nothing is formatted otherwise. Raw bill values; payout shown with the silver multiplier.
        public static void LogBill(Pawn pawn, float added, float total, string reason)
        {
            if (!Prefs.DevMode) return;
            float mult = HospitalMod.Settings.SilverMultiplier;
            string payout = mult != 1f ? $" (payout x{mult:0.##} = {total * mult:0.#})" : "";
            Log.Message($"[Hospital] Bill {added:+0.#;-0.#;+0} {pawn?.LabelShort} ({reason ?? "unspecified"}) -> total {total:0.#}{payout}");
        }
        
        public static bool GetPatientRating(this Pawn pawn, out float score, HospitalMapComponent hospital)
        {
            // idea: Rating new screen. Stars. Last 10 reviews. Store thoughts gained during stay. New thought about staff friendliness. Goodwill gained for 4 and 5 star. Loss for less than 3.
            score = 0.0f;
            if (pawn == null) return false;
            if (!hospital.GetPatientData(pawn, out var patientData)) return false;
            score = Score(pawn, patientData);
            return true;
        }
        
        public static bool GetPatientTimeInHospital(this Pawn pawn, out int ticks, HospitalMapComponent hospital)
        {
            ticks = 0;
            if (pawn == null) return false;
            if (!hospital.GetPatientData(pawn, out var patientData)) return false;
            ticks = GenDate.TicksGame - patientData.ArrivedAtTick;
            return true;
        }
        
        // The pawn currently receiving its arrival wounds/disease. It is not registered as a patient
        // yet at that point, so patches on other mods' damage reactions check this instead.
        public static Pawn PawnBeingInjured { get; private set; }

        // > 0 while arrival wounds/disease/surgery are applied to a pawn that is about to be put in a drop pod (or
        // sampled). Vanilla damage side effects (blood filth, "Headshot" text, wound voice, damage effecters) are
        // skipped then, see DamageEffectSuppressionPatches. A counter, so nested scopes are safe.
        public static int SuppressDamageEffectsDepth;
        public static bool SuppressDamageEffects => SuppressDamageEffectsDepth > 0;

        private static void BeginArrivalDamage(Pawn pawn)
        {
            PawnBeingInjured = pawn;
            SuppressDamageEffectsDepth++;
        }

        private static void EndArrivalDamage(Pawn pawn)
        {
            PawnBeingInjured = null;
            if (SuppressDamageEffectsDepth > 0) SuppressDamageEffectsDepth--;
            try
            {
                // Vanilla keeps a wounded effecter on the pawn; it was never triggered while suppressed.
                var health = pawn?.health;
                if (health?.woundedEffecter != null)
                {
                    health.woundedEffecter.Cleanup();
                    health.woundedEffecter = null;
                }
            }
            catch (System.Exception)
            {
                // cosmetic only
            }
        }

        // spec == null: random problem exactly as before. With a spec, forced choices are used; if a forced
        // choice cannot be applied to this pawn the usual fallback to wounds happens, with a warning.
        public static void DamagePawn(Pawn pawn, PatientData patientData, HospitalMapComponent hospital, PatientSpec spec = null)
        {
            BeginArrivalDamage(pawn);
            try
            {
                bool failed = false;
                switch (patientData.Type)
                {
                    case PatientType.Disease:
                        failed = DiseaseUtility.AddRandomDisease(pawn, patientData, hospital, spec?.Disease, spec != null && spec.IgnoreRestrictions);
                        break;
                    case PatientType.Wounds:
                        AddWounds(pawn, patientData, spec?.Wounds ?? WoundKind.Random);
                        break;
                    case PatientType.Surgery:
                        failed = SurgeryUtility.AddRandomSurgeryBill(pawn, patientData, hospital, spec);
                        break;
                }
                if (failed)
                {
                    if (spec != null)
                    {
                        string text = "Hospital Debug: forced " + patientData.Type + " patient failed for " + pawn.LabelShort
                                      + " (" + (spec.Disease?.defName ?? spec.Surgery?.defName ?? "random") + "), fell back to wounds";
                        Log.Message(text);
                        Messages.Message(text, MessageTypeDefOf.RejectInput, false);
                    }
                    patientData.Type = PatientType.Wounds;
                    AddWounds(pawn, patientData, spec?.Wounds ?? WoundKind.Random);
                }
            }
            finally
            {
                EndArrivalDamage(pawn);
            }
        }

        private static void AddWounds(Pawn pawn, PatientData patientData, WoundKind kind)
        {
            switch (kind)
            {
                case WoundKind.Gunshot: WoundsUtility.AddGunshotWounds(pawn, patientData); break;
                case WoundKind.Bruises: WoundsUtility.AddBruisesWounds(pawn, patientData); break;
                case WoundKind.Burns: WoundsUtility.AddBurnWounds(pawn, patientData); break;
                default: WoundsUtility.AddRandomWounds(pawn, patientData); break;
            }
        }

        public static void DamagePawn(Pawn pawn, PatientData patientData, HospitalMapComponent hospital, IncidentWorker_MassCasualtyEvent.MCEType type)
        {
            BeginArrivalDamage(pawn);
            try
            {
                switch (type)
                {
                    case IncidentWorker_MassCasualtyEvent.MCEType.Pandemic:
                        if (DiseaseUtility.AddPlague(pawn, patientData)) WoundsUtility.AddRandomWounds(pawn, patientData);
                        break;
                    case IncidentWorker_MassCasualtyEvent.MCEType.Raid:
                        WoundsUtility.AddGunshotWounds(pawn, patientData);
                        break;
                    case IncidentWorker_MassCasualtyEvent.MCEType.Crash:
                        WoundsUtility.AddBruisesWounds(pawn, patientData);
                        break;
                    case IncidentWorker_MassCasualtyEvent.MCEType.Fire:
                        WoundsUtility.AddBurnWounds(pawn, patientData);
                        break;
                }
            }
            finally
            {
                EndArrivalDamage(pawn);
            }
        }
        
        public static float CalculateSilverToReceive(Pawn pawn, PatientData patientData)
        {
            return patientData.Bill * HospitalMod.Settings.SilverMultiplier;
        }

        public static int CalculateGoodwillToGain(Pawn pawn, PatientData patientData)
        {
            if (pawn.needs?.mood == null) return 0;
            float score = Score(pawn, patientData);
            int baseGoodwill;
            if (score > 0.9f) baseGoodwill = 5;
            else if (score > 0.8f) baseGoodwill = 4;
            else if (score > 0.7f) baseGoodwill = 3;
            else if (score > 0.6f) baseGoodwill = 2;
            else if (pawn.needs.mood.curLevelInt > pawn.mindState.mentalBreaker.BreakThresholdMinor) baseGoodwill = 1; // ok stay
            else if (pawn.needs.mood.curLevelInt < pawn.mindState.mentalBreaker.BreakThresholdMajor) return -1; // very unhappy stay
            else return 0;
            // scale positive goodwill by the configurable difficulty multiplier
            return Mathf.RoundToInt(baseGoodwill * HospitalMod.Settings.GoodwillGainMultiplier);
        }

        private static float Score(Pawn pawn, PatientData patientData)
        {
            if (pawn.needs?.mood == null) return 0;
            return Math.Min((pawn.needs.mood.CurInstantLevel - patientData.InitialMood)*3.5f, 100.0f);
        }
        
        public static bool DangersOnMap(Map map, out TaggedString reasons)
        {

            reasons = null;
            if (map.GetComponent<HospitalMapComponent>().AcceptDanger) return false;
        
            var fallout = map.GameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout);
            var potentiallyDangerous = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.IsPrisoner && !p.Downed && !IsFogged(p) && !p.InContainerEnclosed).ToArray();
            var hostileFactions = potentiallyDangerous.Where(p => p.Faction != null).Select(p => p.Faction).Where(f => f.HostileTo(Faction.OfPlayer)).ToArray();
            var winter = map.GameConditionManager.ConditionIsActive(GameConditionDefOf.VolcanicWinter);
            var manhunters = potentiallyDangerous.Where(p => p.InAggroMentalState);        

            if (!fallout && !winter && !hostileFactions.Any() && !manhunters.Any()) return false; // All clear

            var reasonList = new List<string>(); // string, so we can check for Distinct later
            if (fallout) reasonList.Add("!!! " + GameConditionDefOf.ToxicFallout.LabelCap);
            if (winter) reasonList.Add("!!! " + GameConditionDefOf.VolcanicWinter.LabelCap);

            foreach (var f in hostileFactions)
            {
                reasonList.Add("!!! " + f.def.pawnsPlural.CapitalizeFirst());
            }

            var manhunterNames = manhunters.GroupBy(p => p.MentalStateDef);
            foreach (var manhunter in manhunterNames)
            {
                if (manhunter.Count() > 1)
                    reasonList.Add($"!!! {manhunter.First().GetKindLabelPlural()} ({manhunter.First().MentalStateDef.label})");
                else if (manhunter.Count() == 1)
                    reasonList.Add($"!!! {manhunter.First().LabelShort} ({manhunter.First().MentalStateDef.label})");
            }

            reasons = reasonList.Distinct().Aggregate((a, b) => a + " " + b);
            return true;
        }   
        
        public static bool IsFogged(Pawn pawn)
        {
            return pawn.MapHeld.fogGrid.IsFogged(pawn.PositionHeld);
        }


    }
}