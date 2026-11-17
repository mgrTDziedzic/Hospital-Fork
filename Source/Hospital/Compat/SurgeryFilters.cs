using Verse;

namespace Hospital.Compat;

// Optional per-mod rules for which surgeries a Hospital patient may REQUEST (random assignment and debug
// validation only; manual bills in the health tab are untouched). Default: plausible.
public static class SurgeryFilters
{
        public static bool IsPlausibleRequest(Pawn pawn, RecipeDef recipe)
        {
                return PlasteelCompat.IsPlausibleRequest(pawn, recipe);
        }
}
