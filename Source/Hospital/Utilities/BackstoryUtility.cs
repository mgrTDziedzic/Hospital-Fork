using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace Hospital.Utilities;

/// <summary>Kind of injury that dominates a wounded patient; picks the activity pool of the backstory text.</summary>
public enum WoundCategory
{
    Generic = 0,
    Gunshot = 1,
    Burn = 2,
    Blunt = 3,
    Sharp = 4
}

/// <summary>
/// Generated backstory of wounded patients ("X suffered severe injuries while petting the hare.").
/// Text comes from the vanilla grammar engine (RulePackDef Hospital_WoundBackstory). Only a seed and the wound
/// category are saved; the text is resolved on demand with its own Rand state, so the game RNG is untouched
/// and the text is the same after every load.
/// </summary>
public static class BackstoryUtility
{
    /// <summary>Wound category of one injury hediff def (by defName; Generic for anything unknown).</summary>
    public static WoundCategory CategoryOf(HediffDef def)
    {
        switch (def?.defName)
        {
            case "Gunshot": return WoundCategory.Gunshot;
            case "Burn": return WoundCategory.Burn;
            case "Crush":
            case "Bruise":
            case "Crack": return WoundCategory.Blunt;
            case "Cut":
            case "Stab":
            case "Scratch":
            case "Bite": return WoundCategory.Sharp;
            default: return WoundCategory.Generic;
        }
    }

    /// <summary>Pure: the category with the largest severity sum. Unknown defs and no injuries give Generic.</summary>
    public static WoundCategory Classify(IEnumerable<(HediffDef def, float severity)> injuries)
    {
        var sums = new float[Enum.GetValues(typeof(WoundCategory)).Length];
        foreach (var (def, severity) in injuries)
        {
            var category = CategoryOf(def);
            if (category != WoundCategory.Generic) sums[(int)category] += severity;
        }
        var best = WoundCategory.Generic;
        float bestSum = 0f;
        for (int i = 1; i < sums.Length; i++)
        {
            if (sums[i] > bestSum)
            {
                bestSum = sums[i];
                best = (WoundCategory)i;
            }
        }
        return best;
    }

    public static WoundCategory Classify(Pawn pawn)
    {
        if (pawn?.health?.hediffSet == null) return WoundCategory.Generic;
        return Classify(pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => (h.def, h.Severity)));
    }

    /// <summary>Stores the wound category and a new non-zero seed. Call once, right after the arrival wounds exist.</summary>
    public static void AssignWoundBackstory(Pawn pawn, PatientData data)
    {
        data.BackstoryWound = Classify(pawn).ToString();
        int seed = Rand.Int;
        data.BackstorySeed = seed == 0 ? 1 : seed;
        data.ClearBackstoryCache();
    }

    private const int ActivitySalt = 0;
    private const int BackstorySalt = 0x2545F491;
    private const int LeaveSalt = 0x5F3759DF;

    /// <summary>The backstory text, or "" when the patient has none (old save, other patient type), the display setting is off or resolving fails.</summary>
    public static string GetBackstory(Pawn pawn, PatientData data)
    {
        if (!HospitalMod.Settings.ShowPatientBackstories) return string.Empty; // display only; the seed is always assigned (MP)
        if (!Prepare(pawn, data)) return string.Empty;
        return data.BackstoryCache;
    }

    /// <summary>One sentence for the leave message that reuses the backstory's activity; "" in the same cases as GetBackstory.</summary>
    public static string GetLeaveLine(Pawn pawn, PatientData data)
    {
        if (!HospitalMod.Settings.ShowPatientBackstories) return string.Empty;
        if (!Prepare(pawn, data)) return string.Empty;
        return data.LeaveCache;
    }

    // Fills the unsaved caches for the current seed. False when the patient has no backstory.
    private static bool Prepare(Pawn pawn, PatientData data)
    {
        if (pawn == null || data == null || data.BackstorySeed == 0) return false;
        if (data.BackstoryCache == null || data.BackstoryCacheSeed != data.BackstorySeed)
        {
            var category = ParseCategory(data.BackstoryWound);
            data.BackstoryCache = Resolve(pawn, data.BackstorySeed, category);
            data.LeaveCache = ResolveLeave(pawn, data.BackstorySeed, category);
            data.BackstoryCacheSeed = data.BackstorySeed;
        }
        return true;
    }

    public static WoundCategory ParseCategory(string name)
    {
        return Enum.TryParse(name, out WoundCategory category) ? category : WoundCategory.Generic;
    }

    /// <summary>The activity phrase of a seed (e.g. "petting the hare"); backstory and leave line both use it. "" on error.</summary>
    public static string ResolveActivity(Pawn pawn, int seed, WoundCategory category)
    {
        return ResolveRoot(pawn, seed ^ ActivitySalt, category, "activity", null, false);
    }

    /// <summary>Resolves the backstory for a seed and category. Rand state is restored afterwards; any error gives "" (cosmetic only).</summary>
    public static string Resolve(Pawn pawn, int seed, WoundCategory category)
    {
        string activity = ResolveActivity(pawn, seed, category);
        if (activity.NullOrEmpty()) return string.Empty;
        return ResolveRoot(pawn, seed ^ BackstorySalt, category, "r_backstory", activity, true);
    }

    /// <summary>Resolves the leave line for a seed and category, with the same activity as <see cref="Resolve"/>.</summary>
    public static string ResolveLeave(Pawn pawn, int seed, WoundCategory category)
    {
        string activity = ResolveActivity(pawn, seed, category);
        if (activity.NullOrEmpty()) return string.Empty;
        return ResolveRoot(pawn, seed ^ LeaveSalt, category, "r_leave", activity, true);
    }

    // fixedActivity: injected as a high priority rule, so the pack's own activity rules are ignored.
    private static string ResolveRoot(Pawn pawn, int seed, WoundCategory category, string root, string fixedActivity, bool capitalize)
    {
        if (HospitalDefOf.Hospital_WoundBackstory == null) return string.Empty;
        Rand.PushState(seed);
        try
        {
            var request = default(GrammarRequest);
            request.Includes.Add(HospitalDefOf.Hospital_WoundBackstory);
            request.Rules.AddRange(GrammarUtility.RulesForPawn("PATIENT", pawn, request.Constants));
            request.Constants["WOUND"] = category.ToString();
            if (fixedActivity != null) request.Rules.Add(new Rule_String("activity(priority=10)->" + fixedActivity));
            return GrammarResolver.Resolve(root, request, "Hospital patient backstory", capitalizeFirstSentence: capitalize) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
        finally
        {
            Rand.PopState();
        }
    }
}
