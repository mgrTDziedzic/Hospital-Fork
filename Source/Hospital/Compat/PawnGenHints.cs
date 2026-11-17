using Verse;

namespace Hospital.Compat;

// Optional per-mod hints that shape the PawnGenerationRequest for a forced surgery (debug actions), so the
// generated pawn is likely to fit the surgery. Hints only raise the hit rate: the pawn is always validated
// afterwards (SurgeryUtility.CanForceOn), so a wrong or missing hint only costs a retry.
// No supported mod active, or no rule for the recipe: the request is left as it is.
public static class PawnGenHints
{
        public static void Apply(RecipeDef recipe, ref PawnGenerationRequest request)
        {
                if (recipe == null) return;
                PlasteelCompat.TryApplyHints(recipe, ref request);
        }
}
