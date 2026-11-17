using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hospital.Utilities;

/// <summary>The three patient groups that each get their own drug policy.</summary>
public enum DrugList
{
    All = 0,
    Infection = 1,
    Surgery = 2,
    Injuries = 3,
    Scars = 4
}

/// <summary>
/// Automatic "Administer X" medical bills for patients, driven by vanilla drug policies
/// (an entry with allowScheduled ticked = the doctors give that drug).
/// </summary>
public static class DrugUtility
{
    private static readonly Dictionary<ThingDef, RecipeDef> recipeCache = new Dictionary<ThingDef, RecipeDef>();

    /// <summary>The vanilla "Administer_&lt;drug&gt;" recipe for a drug, or null when the drug has none.</summary>
    public static RecipeDef AdministerRecipeFor(ThingDef drug)
    {
        if (drug == null) return null;
        if (recipeCache.TryGetValue(drug, out var cached) && cached != null) return cached;
        var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("Administer_" + drug.defName);
        if (recipe == null || !IsAdministerRecipe(recipe) || DrugOf(recipe) != drug) recipe = null;
        recipeCache[drug] = recipe;
        return recipe;
    }

    public static bool IsAdministerRecipe(RecipeDef recipe)
    {
        return recipe?.workerClass != null && typeof(Recipe_AdministerIngestible).IsAssignableFrom(recipe.workerClass);
    }

    public static bool IsAdministerBill(Bill bill)
    {
        return bill is Bill_Medical && IsAdministerRecipe(bill.recipe);
    }

    /// <summary>The drug an administer recipe hands out (its single ingredient).</summary>
    public static ThingDef DrugOf(RecipeDef recipe)
    {
        if (!IsAdministerRecipe(recipe) || recipe.ingredients.NullOrEmpty()) return null;
        return recipe.ingredients[0].filter?.AllowedThingDefs.FirstOrDefault();
    }

    /// <summary>An immunizable hediff the pawn has not yet become fully immune to.</summary>
    public static bool HasUnfinishedInfection(Pawn pawn)
    {
        var hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null) return false;
        foreach (var hediff in hediffs)
        {
            var immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immunizable != null && !immunizable.FullyImmune) return true;
        }
        return false;
    }

    /// <summary>A non-permanent injury (bleeding or not) or blood loss.</summary>
    public static bool HasInjuries(Pawn pawn)
    {
        var hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null) return false;
        foreach (var hediff in hediffs)
        {
            if (hediff.def == HediffDefOf.BloodLoss) return true;
            if (hediff is Hediff_Injury injury && !injury.IsPermanent()) return true;
        }
        return false;
    }

    /// <summary>A permanent injury (scar, old wound, brain scar...).</summary>
    public static bool HasScars(Pawn pawn)
    {
        var hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null) return false;
        foreach (var hediff in hediffs)
        {
            if (hediff is Hediff_Injury injury && injury.IsPermanent()) return true;
        }
        return false;
    }

    /// <summary>
    /// The patient would be sent home (same rule as Trigger_SentAway, ignoring drug bills): no rest needed (a tended
    /// but still sick or healing patient still needs rest), no real surgery, no pending cure. Healed patients get no new drug bills.
    /// </summary>
    public static bool IsOtherwiseHealed(Pawn pawn, PatientData data)
    {
        return !SurgeryUtility.ShouldSeekMedicalRestIgnoringBills(pawn)
               && !SurgeryUtility.HasRealSurgeryBills(pawn)
               && !(data?.HasPendingSurgeryBill ?? false);
    }

    public static bool ListApplies(DrugList list, Pawn pawn, PatientData data)
    {
        switch (list)
        {
            case DrugList.Infection:
                return HasUnfinishedInfection(pawn);
            case DrugList.Surgery:
                return SurgeryUtility.HasRealSurgeryBills(pawn) || (data?.HasPendingSurgeryBill ?? false);
            case DrugList.Injuries:
                return HasInjuries(pawn);
            case DrugList.Scars:
                return HasScars(pawn);
            default:
                return true;
        }
    }

    /// <summary>Is this drug waiting as a bill on the pawn?</summary>
    public static bool HasPendingBillFor(Pawn pawn, ThingDef drug)
    {
        foreach (var bill in pawn.health.surgeryBills.Bills)
        {
            if (IsAdministerBill(bill) && DrugOf(bill.recipe) == drug) return true;
        }
        return false;
    }

    /// <summary>Frequency, mood and joy limits of one policy entry (same rules as vanilla drug policies).</summary>
    public static bool EntryDue(DrugPolicyEntry entry, Pawn pawn, PatientData data, int now)
    {
        if (entry == null || entry.drug == null || !entry.allowScheduled) return false;
        if (entry.onlyIfMoodBelow < 1f && pawn.needs?.mood != null && pawn.needs.mood.CurLevelPercentage >= entry.onlyIfMoodBelow) return false;
        if (entry.onlyIfJoyBelow < 1f && pawn.needs?.joy != null && pawn.needs.joy.CurLevelPercentage >= entry.onlyIfJoyBelow) return false;
        if (data != null && data.LastDrugTick.TryGetValue(entry.drug, out int last))
        {
            if (now - last < (int)(entry.daysFrequency * GenDate.TicksPerDay)) return false;
        }
        return true;
    }

    /// <summary>
    /// Adds the administer bills a patient is due, at the top of their bill list. Returns the number added.
    /// Union of every applicable list; one waiting bill per drug.
    /// </summary>
    public static int ScheduleFor(HospitalMapComponent hospital, Pawn pawn, PatientData data, int now)
    {
        if (pawn?.health?.surgeryBills == null || data == null || data.Dismissed || !pawn.Spawned) return 0;
        if (IsOtherwiseHealed(pawn, data)) return 0;

        List<ThingDef> due = null;
        bool teetotaler = false, teetotalerKnown = false;
        foreach (DrugList list in System.Enum.GetValues(typeof(DrugList)))
        {
            var policy = hospital.GetDrugPolicy(list);
            if (policy == null) continue;
            if (!teetotalerKnown) { teetotaler = pawn.IsTeetotaler(); teetotalerKnown = true; }
            if (teetotaler && !hospital.GetDrugTeetotal(list)) continue;
            if (!ListApplies(list, pawn, data)) continue;
            for (int i = 0; i < policy.Count; i++)
            {
                var entry = policy[i];
                if (!EntryDue(entry, pawn, data, now)) continue;
                due ??= new List<ThingDef>();
                if (!due.Contains(entry.drug)) due.Add(entry.drug);
            }
        }
        if (due == null) return 0;

        int added = 0;
        foreach (var drug in due)
        {
            if (pawn.health.surgeryBills.Count >= BillStack.MaxCount) break;
            if (HasPendingBillFor(pawn, drug)) continue;
            var recipe = AdministerRecipeFor(drug);
            if (recipe == null || !recipe.AvailableOnNow(pawn)) continue;
            var bill = SurgeryUtility.CreatePatientBill(pawn, recipe, null);
            // Doctors take bills top to bottom: the drug goes before the surgery.
            var bills = pawn.health.surgeryBills.Bills;
            if (bills.Remove(bill)) bills.Insert(0, bill);
            added++;
        }
        return added;
    }

    /// <summary>Removes the administer bills still waiting on a pawn (a leaving patient must not keep them).</summary>
    public static void RemoveAdministerBills(Pawn pawn)
    {
        var stack = pawn?.health?.surgeryBills;
        if (stack == null) return;
        foreach (var bill in stack.Bills.Where(IsAdministerBill).ToList()) stack.Delete(bill);
    }
}
