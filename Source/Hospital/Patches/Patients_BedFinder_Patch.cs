using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace Hospital.Patches;

/// <summary>What a patient mainly needs from a bed; decides which bed stat is compared.</summary>
public enum PatientBedNeed
{
    Tend = 0,     // wounds and anything else: MedicalTendQualityOffset, then BedRestEffectiveness
    Immunity = 1, // an immunizable disease that is not yet immune: ImmunityGainSpeedFactor
    Surgery = 2   // pending surgery bill: SurgerySuccessChanceFactor
}

public class Patients_BedFinder_Patch
{
    /// <summary>
    /// Patients get the best hospital bed for what they need. Surgery patients prefer beds marked as surgery
    /// beds and other patients prefer beds that are not; inside that group the bed with the best stat for the
    /// patient's need wins (surgery success, immunity gain, tend quality).
    /// </summary>
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.FindBedFor), typeof(Pawn), typeof(Pawn), typeof(bool), typeof(bool), typeof(GuestStatus?))]
    public class FindBedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Building_Bed __result, Pawn sleeper, Pawn traveler)
        {
            if (!sleeper.IsPatient(out _) || sleeper.InBed()) return;

            var candidates = new List<Building_Bed>();
            foreach (var bed in sleeper.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (!bed.Medical) continue;
                var comp = bed.GetComp<CompHospitalBed>();
                if (comp == null || !comp.Hospital) continue;
                if (!RestUtility.IsValidBedFor(bed, sleeper, traveler, checkSocialProperness: false)) continue;
                candidates.Add(bed);
            }

            __result = SelectBed(candidates, sleeper) ?? __result;
        }
    }

    /// <summary>
    /// Picks the best bed for the patient from beds that are already valid hospital beds for them.
    /// Null when the list is empty.
    /// </summary>
    public static Building_Bed SelectBed(IList<Building_Bed> candidates, Pawn patient)
    {
        if (candidates == null || candidates.Count == 0) return null;

        bool wantsSurgeryBed = SurgeryUtility.HasRealSurgeryBills(patient);
        var preferred = candidates.Where(b => b.GetComp<CompHospitalBed>().Surgery == wantsSurgeryBed).ToList();
        var pool = preferred.Count > 0 ? preferred : candidates;

        var need = GetNeed(patient);
        var origin = patient.Spawned ? patient.Position : IntVec3.Invalid;
        Building_Bed best = null;
        float bestScore = float.MinValue;
        int bestDist = int.MaxValue;
        foreach (var bed in pool)
        {
            float score = ScoreBed(bed, need);
            int dist = origin.IsValid ? bed.Position.DistanceToSquared(origin) : 0;
            // higher score wins; on a tie (same bed type in a similar room) the nearer bed wins
            if (best == null || score > bestScore + 0.0001f || (Math.Abs(score - bestScore) <= 0.0001f && dist < bestDist))
            {
                best = bed;
                bestScore = score;
                bestDist = dist;
            }
        }
        return best;
    }

    /// <summary>Surgery bill beats disease, disease beats everything else.</summary>
    public static PatientBedNeed GetNeed(Pawn patient)
    {
        if (SurgeryUtility.HasRealSurgeryBills(patient)) return PatientBedNeed.Surgery;
        foreach (var hediff in patient.health.hediffSet.hediffs)
        {
            var immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immunizable != null && !immunizable.FullyImmune) return PatientBedNeed.Immunity;
        }
        return PatientBedNeed.Tend;
    }

    /// <summary>
    /// Bed score for a need. Stats are read from the spawned bed, so facilities (vitals monitor), room
    /// cleanliness, light and bed quality are included where vanilla applies them.
    /// </summary>
    public static float ScoreBed(Building_Bed bed, PatientBedNeed need)
    {
        switch (need)
        {
            case PatientBedNeed.Surgery:
                return bed.GetStatValue(StatDefOf.SurgerySuccessChanceFactor);
            case PatientBedNeed.Immunity:
                return bed.GetStatValue(StatDefOf.ImmunityGainSpeedFactor);
            default:
                // tend quality first; rest effectiveness (at most ~1) only breaks ties between equal offsets
                return bed.GetStatValue(StatDefOf.MedicalTendQualityOffset) * 100f
                       + bed.GetStatValue(StatDefOf.BedRestEffectiveness);
        }
    }
}
