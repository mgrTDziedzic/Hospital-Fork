using Verse;

namespace Hospital
{
    public enum WoundKind
    {
        Random = 0,
        Gunshot = 1,
        Bruises = 2,
        Burns = 3
    }

    // Optional, never saved description of the patient to generate. Null / unset fields mean "random".
    // Used by the debug actions; the normal arrival passes no spec.
    public class PatientSpec
    {
        public PatientType? Type;
        public WoundKind? Wounds;
        public HediffDef Disease;
        public RecipeDef Surgery;
        // Debug: skip mod settings (surgery switch, blacklists, aggressive switch) and the
        // colonist-skill / ingredient checks. Pawn validity checks always stay.
        public bool IgnoreRestrictions;
        // Set by the worker when it already told the user why nothing was generated.
        public bool FailureReported;
    }
}
