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

/// <summary>Kind of disease of a sick patient; picks the activity pool of the backstory text.</summary>
public enum DiseaseCategory
{
    Generic = 0,
    Respiratory = 1,
    Insect = 2,
    Parasite = 3,
    Mechanites = 4,
    Infection = 5,
    Toxic = 6
}

/// <summary>
/// Generated backstory of wounded and sick patients ("X suffered severe injuries while petting the hare.").
/// Text comes from the vanilla grammar engine (RulePackDef Hospital_WoundBackstory / Hospital_DiseaseBackstory).
/// Only a seed, the category and (diseases) the disease defName are saved; the text is resolved on demand with its own Rand state, so the game RNG is untouched
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
        data.BackstoryDisease = null;
        int seed = Rand.Int;
        data.BackstorySeed = seed == 0 ? 1 : seed;
        data.ClearBackstoryCache();
    }

    /// <summary>Pure: disease category of a hediff defName (vanilla names, then substrings for modded diseases). Unknown gives Generic.</summary>
    public static DiseaseCategory CategoryOfDisease(string defName)
    {
        if (defName.NullOrEmpty()) return DiseaseCategory.Generic;
        switch (defName)
        {
            case "Flu":
            case "Plague":
            case "LungRot": return DiseaseCategory.Respiratory;
            case "Malaria":
            case "SleepingSickness": return DiseaseCategory.Insect;
            case "GutWorms":
            case "MuscleParasites": return DiseaseCategory.Parasite;
            case "FibrousMechanites":
            case "SensoryMechanites": return DiseaseCategory.Mechanites;
            case "WoundInfection": return DiseaseCategory.Infection;
            case "ToxicBuildup": return DiseaseCategory.Toxic;
        }
        string lower = defName.ToLowerInvariant();
        if (lower.Contains("mechanite")) return DiseaseCategory.Mechanites;
        if (lower.Contains("infection")) return DiseaseCategory.Infection;
        if (lower.Contains("worm") || lower.Contains("parasite")) return DiseaseCategory.Parasite;
        if (lower.Contains("malaria") || lower.Contains("sleepingsickness") || lower.Contains("mosquito")) return DiseaseCategory.Insect;
        if (lower.Contains("lung") || lower.Contains("flu") || lower.Contains("plague") || lower.Contains("pneumon")) return DiseaseCategory.Respiratory;
        if (lower.Contains("toxic") || lower.Contains("poison")) return DiseaseCategory.Toxic;
        return DiseaseCategory.Generic;
    }

    /// <summary>Stores the disease category, keeps the disease defName (set by DiseaseUtility) and stores a new non-zero seed. Call once, right after the arrival disease exists.</summary>
    public static void AssignDiseaseBackstory(Pawn pawn, PatientData data)
    {
        if (data.BackstoryDisease.NullOrEmpty())
        {
            AssignWoundBackstory(pawn, data); // no disease recorded: use the wound texts
            return;
        }
        data.BackstoryWound = CategoryOfDisease(data.BackstoryDisease).ToString();
        int seed = Rand.Int;
        data.BackstorySeed = seed == 0 ? 1 : seed;
        data.ClearBackstoryCache();
    }

    /// <summary>Assigns the wound or disease backstory by the patient's current type (Type is Wounds after a fallback from a disease). Surgery patients get none.</summary>
    public static void AssignBackstory(Pawn pawn, PatientData data)
    {
        if (data.Type == PatientType.Disease) AssignDiseaseBackstory(pawn, data);
        else if (data.Type == PatientType.Wounds) AssignWoundBackstory(pawn, data);
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
            if (data.BackstoryDisease.NullOrEmpty())
            {
                var category = ParseCategory(data.BackstoryWound);
                data.BackstoryCache = Resolve(pawn, data.BackstorySeed, category);
                data.LeaveCache = ResolveLeave(pawn, data.BackstorySeed, category);
            }
            else
            {
                var category = ParseDiseaseCategory(data.BackstoryWound);
                var disease = DefDatabase<HediffDef>.GetNamedSilentFail(data.BackstoryDisease); // null (mod removed): fallback label
                data.BackstoryCache = Resolve(pawn, data.BackstorySeed, category, disease);
                data.LeaveCache = ResolveLeave(pawn, data.BackstorySeed, category, disease);
            }
            data.BackstoryCacheSeed = data.BackstorySeed;
        }
        return true;
    }

    public static WoundCategory ParseCategory(string name)
    {
        return Enum.TryParse(name, out WoundCategory category) ? category : WoundCategory.Generic;
    }

    public static DiseaseCategory ParseDiseaseCategory(string name)
    {
        return Enum.TryParse(name, out DiseaseCategory category) ? category : DiseaseCategory.Generic;
    }

    /// <summary>The activity phrase of a seed (e.g. "petting the hare"); backstory and leave line both use it. "" on error.</summary>
    public static string ResolveActivity(Pawn pawn, int seed, WoundCategory category)
    {
        return ResolveRoot(pawn, seed ^ ActivitySalt, Wound(category), "activity", null, false);
    }

    public static string ResolveActivity(Pawn pawn, int seed, DiseaseCategory category, HediffDef disease)
    {
        return ResolveRoot(pawn, seed ^ ActivitySalt, Disease(category, disease), "activity", null, false);
    }

    /// <summary>Resolves the backstory for a seed and category. Rand state is restored afterwards; any error gives "" (cosmetic only).</summary>
    public static string Resolve(Pawn pawn, int seed, WoundCategory category)
    {
        return ResolveBackstory(pawn, seed, Wound(category));
    }

    /// <summary>Disease variant; <paramref name="disease"/> null (unknown def) uses a generic illness label.</summary>
    public static string Resolve(Pawn pawn, int seed, DiseaseCategory category, HediffDef disease)
    {
        return ResolveBackstory(pawn, seed, Disease(category, disease));
    }

    /// <summary>Resolves the leave line for a seed and category, with the same activity as the backstory.</summary>
    public static string ResolveLeave(Pawn pawn, int seed, WoundCategory category)
    {
        return ResolveLeaveLine(pawn, seed, Wound(category));
    }

    public static string ResolveLeave(Pawn pawn, int seed, DiseaseCategory category, HediffDef disease)
    {
        return ResolveLeaveLine(pawn, seed, Disease(category, disease));
    }

    // What a text is about: the rule pack, the category constant and (diseases) the disease def.
    private struct Subject
    {
        public RulePackDef Pack;
        public string ConstantName;
        public string Category;
        public bool IsDisease;
        public HediffDef Disease;
    }

    private static Subject Wound(WoundCategory category) => new Subject
    {
        Pack = HospitalDefOf.Hospital_WoundBackstory, ConstantName = "WOUND", Category = category.ToString()
    };

    private static Subject Disease(DiseaseCategory category, HediffDef disease) => new Subject
    {
        Pack = HospitalDefOf.Hospital_DiseaseBackstory, ConstantName = "DISEASE_category", Category = category.ToString(),
        IsDisease = true, Disease = disease
    };

    private static string ResolveBackstory(Pawn pawn, int seed, Subject subject)
    {
        string activity = ResolveRoot(pawn, seed ^ ActivitySalt, subject, "activity", null, false);
        if (activity.NullOrEmpty()) return string.Empty;
        return ResolveRoot(pawn, seed ^ BackstorySalt, subject, "r_backstory", activity, true);
    }

    private static string ResolveLeaveLine(Pawn pawn, int seed, Subject subject)
    {
        string activity = ResolveRoot(pawn, seed ^ ActivitySalt, subject, "activity", null, false);
        if (activity.NullOrEmpty()) return string.Empty;
        return ResolveRoot(pawn, seed ^ LeaveSalt, subject, "r_leave", activity, true);
    }

    // fixedActivity: injected as a high priority rule, so the pack's own activity rules are ignored.
    private static string ResolveRoot(Pawn pawn, int seed, Subject subject, string root, string fixedActivity, bool capitalize)
    {
        if (subject.Pack == null) return string.Empty;
        Rand.PushState(seed);
        try
        {
            var request = default(GrammarRequest);
            request.Includes.Add(subject.Pack);
            request.Rules.AddRange(GrammarUtility.RulesForPawn("PATIENT", pawn, request.Constants));
            request.Constants[subject.ConstantName] = subject.Category;
            if (subject.IsDisease)
            {
                if (subject.Disease != null) request.Rules.AddRange(GrammarUtility.RulesForDef("DISEASE", subject.Disease));
                else request.Rules.Add(new Rule_String("DISEASE_label", "a mysterious illness"));
            }
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
