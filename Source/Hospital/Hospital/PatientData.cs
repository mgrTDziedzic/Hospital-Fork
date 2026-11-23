using System.Collections.Generic;
using Verse;

namespace Hospital;

public enum PatientType
{
    Test = 0,
    Wounds = 1,
    Disease = 2,
    Surgery = 3
}

public class PatientData : IExposable
{
    public PatientData(int arrivedAtTick, float initialMarketValue, float initialMood, PatientType type)
    {
        this.ArrivedAtTick = arrivedAtTick;
        this.InitialMarketValue = initialMarketValue;
        Type = type;
        InitialMood = initialMood;
    }

    public PatientData()
    {
    }

    public int ArrivedAtTick;
    public float InitialMarketValue;
    public float InitialMood;
    public PatientType Type;
    public float Bill;
    public string Cure;
    public string Diagnosis;
    public RecipeDef CureRecipe;
    public bool Dismissed;
    public bool HasPendingSurgeryBill;
    // Not saved: tick of the last failed surgery. Vanilla still completes the bill after a failure, which must not be billed.
    public int SurgeryFailedTick = -1;
    // Cured-externally tracking (see HospitalMapComponent.CheckExternallyCuredPatients). Defaults = not tracked.
    public bool PartMissingAtArrival;
    // Arrived sedated by their faction (aggressive patient challenge, saved).
    public bool Sedated;
    public float TrackedHediffSeverity = -1f;
    // Tick the hospital last finished administering each drug (saved); drives the drug policy frequency.
    public Dictionary<ThingDef, int> LastDrugTick = new Dictionary<ThingDef, int>();

    // Generated backstory (see Utilities/BackstoryUtility.cs). Seed 0 = none (old saves, surgery patients).
    public int BackstorySeed;
    // Category name: a WoundCategory, or a DiseaseCategory when BackstoryDisease is set (field name kept for old saves).
    public string BackstoryWound;
    // Defname of the disease hediff of a disease patient's backstory; null = wound backstory (saved).
    public string BackstoryDisease;
    // Not saved: resolved text for BackstoryCacheSeed.
    public string BackstoryCache;
    public string LeaveCache;
    public int BackstoryCacheSeed;

    public void ClearBackstoryCache()
    {
        BackstoryCache = null;
        LeaveCache = null;
        BackstoryCacheSeed = 0;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref ArrivedAtTick, "ArrivedAtTick");
        Scribe_Values.Look(ref InitialMarketValue, "InitialMarketValue");
        Scribe_Values.Look(ref InitialMood, "InitialMood");
        Scribe_Values.Look(ref Bill, "Bill");
        Scribe_Values.Look(ref Cure, "Cure");
        Scribe_Values.Look(ref Diagnosis, "Diagnosis");
        Scribe_Values.Look(ref Dismissed, "Dismissed");
        Scribe_Values.Look(ref HasPendingSurgeryBill, "HasPendingSurgeryBill");
        Scribe_Values.Look(ref PartMissingAtArrival, "PartMissingAtArrival");
        Scribe_Values.Look(ref Sedated, "Sedated");
        Scribe_Values.Look(ref BackstorySeed, "BackstorySeed");
        Scribe_Values.Look(ref BackstoryWound, "BackstoryWound");
        Scribe_Values.Look(ref BackstoryDisease, "BackstoryDisease");
        Scribe_Values.Look(ref TrackedHediffSeverity, "TrackedHediffSeverity", -1f);
        Scribe_Defs.Look(ref CureRecipe, "CureRecipe");
        Scribe_Collections.Look(ref LastDrugTick, "LastDrugTick", LookMode.Def, LookMode.Value);
        LastDrugTick ??= new Dictionary<ThingDef, int>();
    }
}