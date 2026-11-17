using RimWorld;
using Verse;

namespace Hospital;

[DefOf]
public static class HospitalDefOf
{
    // Sedation of aggressive patients (see SurgeryUtility.AddTransportSedation).
    public static HediffDef Hospital_TransportSedation;

    static HospitalDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(HospitalDefOf));
    }
}
