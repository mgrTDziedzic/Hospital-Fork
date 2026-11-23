using RimWorld;
using Verse;

namespace Hospital;

[DefOf]
public static class HospitalDefOf
{
    // Sedation of aggressive patients (see SurgeryUtility.AddTransportSedation).
    public static HediffDef Hospital_TransportSedation;

    // Generated wound backstory grammar (see Utilities/BackstoryUtility.cs).
    public static RulePackDef Hospital_WoundBackstory;
    public static RulePackDef Hospital_DiseaseBackstory;

    static HospitalDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(HospitalDefOf));
    }
}
