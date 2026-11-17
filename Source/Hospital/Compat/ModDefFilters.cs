using System;
using Verse;

namespace Hospital.Compat;

// Defs of other mods that Hospital must not use as patients, diseases or surgeries. These filters do nothing
// without the mod (no such defs exist) and never log. Matching is by exact defName prefix, not substring,
// so unrelated defs (e.g. "InstallUpgradedArm") are never caught by accident.
public static class ModDefFilters
{
        // Vanilla Races Expanded - Android: factions, pawn kinds, recipes (verified against its public source).
        public const string AndroidPrefix = "VREA_";
        // Genetic Rim: recipes and hediffs (verified against its public source).
        public const string GeneticRimPrefix = "GR_";
        // Separation sickness hediff of a "VDE" mod. NOT confirmed from a public source; kept as the original
        // substring check (case-sensitive) until it is.
        public const string VdeMarker = "VDE";

        private static bool Starts(Def def, string prefix)
        {
                return def?.defName != null && def.defName.StartsWith(prefix, StringComparison.Ordinal);
        }

        public static bool IsAndroidDef(Def def) { return Starts(def, AndroidPrefix); }

        public static bool IsGeneticRimDef(Def def) { return Starts(def, GeneticRimPrefix); }

        // Mod diseases that are never handed to patients (mostly lethal Genetic Rim diseases, separation sickness).
        public static bool IsExcludedDisease(HediffDef def)
        {
                return IsGeneticRimDef(def) || (def?.defName != null && def.defName.Contains(VdeMarker));
        }

        // Surgeries of other mods that are never assigned (android parts, Genetic Rim implants).
        public static bool IsExcludedSurgery(RecipeDef recipe)
        {
                return IsAndroidDef(recipe) || IsGeneticRimDef(recipe);
        }
}
