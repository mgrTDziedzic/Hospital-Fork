using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

using Hospital.Utilities;

namespace Hospital.Compat;

// Plasteel Surgery (Continued) is supported without any reference to it (default blacklist by defName,
// worker check by type name). Not installed: nothing happens and nothing is logged.
public static class PlasteelCompat
{
        // Called once at startup (HospitalMod).
        public static void Startup()
        {
                var key = SupportedMods.PlasteelSurgery;
                if (SupportedMods.IsActive(key)) Log.Message("[Hospital] " + SupportedMods.DisplayName(key) + " (" + SupportedMods.ActiveId(key) + ") detected, compatibility enabled");
        }

        private static bool hintsBroken;

        // Reads gender / bodyType of the recipe's "PlasteelSurgery.SurgeryDef" extension by reflection.
        // False: mod absent, not a body surgery, no extension, or API problem (one info line, then off for the session).
        private static bool TryReadExtension(RecipeDef recipe, out Gender gender, out BodyTypeDef bodyType)
        {
                gender = Gender.None;
                bodyType = null;
                if (hintsBroken || !SupportedMods.IsActive(SupportedMods.PlasteelSurgery)) return false;
                if (!SurgeryUtility.IsPlasteelBodyWorker(recipe.workerClass)) return false;
                try
                {
                        var ext = recipe.modExtensions?.FirstOrDefault(e => e.GetType().FullName == "PlasteelSurgery.SurgeryDef");
                        if (ext == null) return false;
                        var type = ext.GetType();
                        var genderField = type.GetField("gender", BindingFlags.Public | BindingFlags.Instance);
                        var bodyField = type.GetField("bodyType", BindingFlags.Public | BindingFlags.Instance);
                        if (genderField == null || bodyField == null) throw new MissingFieldException(type.FullName);
                        gender = (Gender)genderField.GetValue(ext);
                        bodyType = bodyField.GetValue(ext) as BodyTypeDef;
                        return true;
                }
                catch (Exception e)
                {
                        hintsBroken = true;
                        Log.Message("[Hospital] Plasteel Surgery compatibility for body surgeries is off (API differs: " + e.GetType().Name + ")");
                        return false;
                }
        }

        // Feminize (target body Female) / Masculinize (target body Male) have no gender in the extension; patients
        // only request them when the pawn's gender matches. Decided by the body type, not by defName.
        // Everything else (and mod absent): plausible.
        public static bool IsPlausibleRequest(Pawn pawn, RecipeDef recipe)
        {
                if (!TryReadExtension(recipe, out var gender, out var body)) return true;
                if (gender != Gender.None) return true;
                if (body == BodyTypeDefOf.Female) return pawn.gender == Gender.Female;
                if (body == BodyTypeDefOf.Male) return pawn.gender == Gender.Male;
                return true;
        }

        private static bool IsLaryngoplastyWorker(System.Type type)
        {
                for (; type != null; type = type.BaseType)
                        if (type.FullName == "PlasteelSurgery.PS_Recipes_Surgery_Laryngoplasty") return true;
                return false;
        }

        // Plasteel Surgery loaded and this is its Laryngoplasty recipe.
        public static bool IsLaryngoplasty(RecipeDef recipe)
        {
                return recipe != null && SupportedMods.IsActive(SupportedMods.PlasteelSurgery) && IsLaryngoplastyWorker(recipe.workerClass);
        }

        private static bool HasLaryngoplastyTrait(Pawn pawn)
        {
                return pawn.story.traits.allTraits.Any(t => t.def == TraitDefOf.AnnoyingVoice || t.def == TraitDefOf.CreepyBreathing);
        }

        // Which of the two traits the pawn could get (mirrors the checks of vanilla trait generation: conflicts,
        // backstory, kind). Not a trait the pawn already has.
        private static List<TraitDef> GrantableLaryngoplastyTraits(Pawn pawn)
        {
                var result = new List<TraitDef>();
                foreach (var def in new[] { TraitDefOf.AnnoyingVoice, TraitDefOf.CreepyBreathing })
                {
                        if (def == null || def.degreeDatas.NullOrEmpty()) continue;
                        if (pawn.story.traits.allTraits.Any(t => def.ConflictsWith(t))) continue;
                        int degree = def.degreeDatas[0].degree;
                        var story = pawn.story;
                        if (story.Childhood != null && story.Childhood.DisallowsTrait(def, degree)) continue;
                        if (story.Adulthood != null && story.Adulthood.DisallowsTrait(def, degree)) continue;
                        var kind = pawn.kindDef;
                        if (kind != null && (kind.disallowedTraits.NotNullAndContains(def)
                                             || (kind.disallowedTraitsWithDegree != null && kind.disallowedTraitsWithDegree.Any(r => r.def == def && (!r.degree.HasValue || r.degree.Value == degree))))) continue;
                        result.Add(def);
                }
                return result;
        }

        // Laryngoplasty is a "prepared" surgery: the worker only accepts pawns with Annoying voice / Creepy breathing
        // (or the botched hediff), so Hospital adds a trait first (TryPrepareLaryngoplasty). True: the recipe is
        // Laryngoplasty, the pawn has none of those yet, and one can be added (adult humanlike, not conflicting or
        // disallowed). Pawns that already have a trait/hediff go through the normal worker check (false here).
        public static bool CanPrepareLaryngoplasty(Pawn pawn, RecipeDef recipe)
        {
                if (!IsLaryngoplasty(recipe) || pawn?.story?.traits == null || !pawn.RaceProps.Humanlike) return false;
                if (!pawn.DevelopmentalStage.Adult()) return false;
                if (HasLaryngoplastyTrait(pawn)) return false;
                // The worker takes the first part of the recipe's fixed part def (same lookup as Plasteel).
                if (recipe.appliedOnFixedBodyParts.NullOrEmpty() || pawn.RaceProps.body.GetPartsWithDef(recipe.appliedOnFixedBodyParts[0]).FirstOrDefault() == null) return false;
                return GrantableLaryngoplastyTraits(pawn).Count > 0;
        }

        // Adds one of the traits (random among the allowed ones) when CanPrepareLaryngoplasty. Returns the added trait
        // (to undo with RemoveLaryngoplastyTrait) or null when nothing was added.
        public static Trait TryPrepareLaryngoplasty(Pawn pawn, RecipeDef recipe)
        {
                if (!CanPrepareLaryngoplasty(pawn, recipe)) return null;
                var def = GrantableLaryngoplastyTraits(pawn).RandomElement();
                var trait = new Trait(def, def.degreeDatas[0].degree);
                pawn.story.traits.GainTrait(trait);
                return pawn.story.traits.allTraits.Contains(trait) ? trait : null;
        }

        public static void RemoveLaryngoplastyTrait(Pawn pawn, Trait trait)
        {
                if (trait != null && pawn.story.traits.allTraits.Contains(trait)) pawn.story.traits.RemoveTrait(trait);
        }

        // ---------- Beauty surgeries (BaseAlterBeauty and subclasses) ----------

        private static bool beautyBroken;
        private static TraitDef beautyDef;
        private static readonly Dictionary<System.Type, MethodInfo> allowedDegreesMethods = new Dictionary<System.Type, MethodInfo>();

        // What TryPrepareBeauty changed, so it can be undone: the removed original trait (null = pawn had none)
        // and the added one (null = target degree 0, no trait).
        public class BeautyPrep
        {
                public Trait Removed;
                public Trait Added;
        }

        private static bool IsBeautyWorker(System.Type type)
        {
                for (; type != null; type = type.BaseType)
                        if (type.FullName == "PlasteelSurgery.BaseAlterBeauty") return true;
                return false;
        }

        // Plasteel Surgery loaded and this is one of its beauty recipes (Increase/Decrease/Make*).
        public static bool IsBeautyRecipe(RecipeDef recipe)
        {
                return recipe != null && !beautyBroken && SupportedMods.IsActive(SupportedMods.PlasteelSurgery) && IsBeautyWorker(recipe.workerClass);
        }

        private static void DisableBeauty(Exception e)
        {
                beautyBroken = true;
                Log.Message("[Hospital] Plasteel Surgery compatibility for beauty surgeries is off (API differs: " + (e?.GetType().Name ?? "unknown") + ")");
        }

        // Same def lookup as Plasteel (BaseAlterBeauty.beauty).
        private static TraitDef BeautyDef()
        {
                return beautyDef ??= DefDatabase<TraitDef>.GetNamedSilentFail("Beauty");
        }

        // The recipe worker's protected AllowedDegrees(); null on API problems (compat then off).
        private static List<int> ReadAllowedDegrees(RecipeDef recipe)
        {
                try
                {
                        var workerType = recipe.Worker.GetType();
                        if (!allowedDegreesMethods.TryGetValue(workerType, out var method))
                        {
                                method = workerType.GetMethod("AllowedDegrees", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (method == null) throw new MissingMethodException(workerType.FullName, "AllowedDegrees");
                                allowedDegreesMethods[workerType] = method;
                        }
                        return method.Invoke(recipe.Worker, null) as List<int> ?? throw new InvalidCastException("AllowedDegrees");
                }
                catch (Exception e)
                {
                        DisableBeauty(e);
                        return null;
                }
        }

        // Degrees (0 = no trait) the worker accepts that the pawn could get, ignoring the Beauty trait it has now.
        private static List<int> GrantableBeautyDegrees(Pawn pawn, RecipeDef recipe, TraitDef beauty, Trait current)
        {
                var result = new List<int>();
                var allowed = ReadAllowedDegrees(recipe);
                if (allowed == null) return result;
                var story = pawn.story;
                var kind = pawn.kindDef;
                foreach (int degree in allowed)
                {
                        if (degree == 0) { result.Add(0); continue; }
                        if (beauty.degreeDatas.NullOrEmpty() || !beauty.degreeDatas.Any(d => d.degree == degree)) continue;
                        if (story.traits.allTraits.Any(t => t != current && beauty.ConflictsWith(t))) continue;
                        if (story.Childhood != null && story.Childhood.DisallowsTrait(beauty, degree)) continue;
                        if (story.Adulthood != null && story.Adulthood.DisallowsTrait(beauty, degree)) continue;
                        if (kind != null && (kind.disallowedTraits.NotNullAndContains(beauty)
                                             || (kind.disallowedTraitsWithDegree != null && kind.disallowedTraitsWithDegree.Any(r => r.def == beauty && (!r.degree.HasValue || r.degree.Value == degree))))) continue;
                        result.Add(degree);
                }
                return result;
        }

        // The pawn's own Beauty trait, or null. Out: false when it cannot be swapped (gene-sourced, suppressed).
        private static bool TryGetCurrentBeauty(Pawn pawn, TraitDef beauty, out Trait current)
        {
                current = pawn.story.traits.allTraits.FirstOrDefault(t => t.def == beauty);
                return current == null || (current.sourceGene == null && !current.Suppressed);
        }

        // Beauty recipes are "prepared" surgeries: the worker only offers a pawn whose Beauty degree is in its
        // AllowedDegrees(), so Hospital sets such a trait first (TryPrepareBeauty). True: beauty recipe, the
        // worker rejects the pawn as it is, and an allowed degree could be given to it.
        public static bool CanPrepareBeauty(Pawn pawn, RecipeDef recipe)
        {
                if (!IsBeautyRecipe(recipe) || pawn?.story?.traits == null || !pawn.RaceProps.Humanlike) return false;
                var beauty = BeautyDef();
                if (beauty == null || !TryGetCurrentBeauty(pawn, beauty, out var current)) return false;
                var allowed = ReadAllowedDegrees(recipe);
                if (allowed == null) return false;
                int currentDegree = current?.Degree ?? 0;
                if (allowed.Contains(currentDegree)) return false; // the worker accepts the pawn as it is
                return GrantableBeautyDegrees(pawn, recipe, beauty, current).Count > 0;
        }

        // Swaps the pawn's Beauty trait to a random allowed degree when CanPrepareBeauty. Returns what changed
        // (undo with RestoreBeauty) or null when nothing was changed. On a failed swap the original is restored.
        public static BeautyPrep TryPrepareBeauty(Pawn pawn, RecipeDef recipe)
        {
                if (!CanPrepareBeauty(pawn, recipe)) return null;
                var beauty = BeautyDef();
                TryGetCurrentBeauty(pawn, beauty, out var current);
                var degrees = GrantableBeautyDegrees(pawn, recipe, beauty, current);
                if (degrees.Count == 0) return null;
                var prep = new BeautyPrep { Removed = current };
                int degree = degrees.RandomElement();
                try
                {
                        if (current != null) pawn.story.traits.RemoveTrait(current);
                        if (degree != 0)
                        {
                                var added = new Trait(beauty, degree);
                                pawn.story.traits.GainTrait(added);
                                if (!pawn.story.traits.allTraits.Contains(added))
                                {
                                        RestoreBeauty(pawn, prep);
                                        return null;
                                }
                                prep.Added = added;
                        }
                        return prep;
                }
                catch (Exception e)
                {
                        RestoreBeauty(pawn, prep);
                        DisableBeauty(e);
                        return null;
                }
        }

        // Undoes TryPrepareBeauty: removes the added trait and gives the original back.
        public static void RestoreBeauty(Pawn pawn, BeautyPrep prep)
        {
                if (prep == null) return;
                var traits = pawn.story.traits;
                if (prep.Added != null && traits.allTraits.Contains(prep.Added)) traits.RemoveTrait(prep.Added);
                if (prep.Removed != null && !traits.allTraits.Contains(prep.Removed)) traits.GainTrait(prep.Removed);
        }

        // Laryngoplasty: patient generated with a voice/breathing trait. Body-type surgeries (BaseAlterBody) only work on adults of the right gender whose body type differs from
        // the target. Mod absent or recipe not a body surgery: nothing happens.
        public static void TryApplyHints(RecipeDef recipe, ref PawnGenerationRequest request)
        {
                if (SupportedMods.IsActive(SupportedMods.PlasteelSurgery) && IsLaryngoplastyWorker(recipe.workerClass))
                {
                        // Debug forced path. Normal play prepares the pawn instead (TryPrepareLaryngoplasty); here the trait
                        // is generated up front, so the prepare step finds it and adds nothing. Both are single-degree traits.
                        request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
                        var trait = Rand.Bool ? TraitDefOf.AnnoyingVoice : TraitDefOf.CreepyBreathing;
                        if (trait != null) request.ForcedTraits = new List<TraitDef> { trait };
                        return;
                }
                if (!TryReadExtension(recipe, out var gender, out var targetBody)) return;
                request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
                bool explicitBody = targetBody != null;
                // Feminize / Masculinize: patient of the matching gender (see IsPlausibleRequest).
                if (gender == Gender.None && (targetBody == BodyTypeDefOf.Female || targetBody == BodyTypeDefOf.Male))
                        gender = targetBody == BodyTypeDefOf.Female ? Gender.Female : Gender.Male;
                if (gender != Gender.None) request.FixedGender = gender;
                if (targetBody == null && gender != Gender.None)
                        targetBody = gender == Gender.Male ? BodyTypeDefOf.Male : BodyTypeDefOf.Female;

                var options = new List<BodyTypeDef> { BodyTypeDefOf.Thin, BodyTypeDefOf.Fat, BodyTypeDefOf.Hulk };
                // Male/Female are only safe when the target is known (otherwise it depends on the pawn's gender),
                // and not for Feminize/Masculinize (Thin/Fat/Hulk only).
                bool genderBody = explicitBody && (targetBody == BodyTypeDefOf.Female || targetBody == BodyTypeDefOf.Male);
                if (targetBody != null && !genderBody) { options.Add(BodyTypeDefOf.Male); options.Add(BodyTypeDefOf.Female); }
                options.RemoveAll(b => b == null || b == targetBody);
                if (options.Count > 0) request.ForceBodyType = options.RandomElement();
        }
}
