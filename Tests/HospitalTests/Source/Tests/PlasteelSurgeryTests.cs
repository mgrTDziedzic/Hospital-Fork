using System.Collections.Generic;
using System.Linq;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Plasteel Surgery (Continued) compatibility: default blacklist on new maps and CanAssignAsIs, which keeps
    // Hospital from assigning a surgery the recipe worker rejects (vanilla would delete such a bill and leave
    // the patient stuck). Tests that need Plasteel return early (pass) when the mod is not loaded.
    // Test pawns are discarded in cleanup, so trait / body type / gender changes need no explicit revert.
    public static class PlasteelSurgeryTests
    {
        private const string PlasteelId = global::Hospital.Compat.SupportedMods.PlasteelSurgery;

        // ---------- helpers ----------

        private static RecipeDef Recipe(string defName)
        {
            return DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
        }

        private static Pawn Adult(TestContext ctx)
        {
            return VanillaCompatTests.Visitor(ctx, false);
        }

        // Replaces the pawn's trait of this def; degree null = no such trait.
        private static void SetTrait(Pawn pawn, TraitDef def, int? degree)
        {
            var traits = pawn.story.traits;
            foreach (var t in traits.allTraits.Where(x => x.def == def).ToList()) traits.RemoveTrait(t, true);
            if (degree.HasValue) traits.GainTrait(new Trait(def, degree.Value, true), true);
        }

        private static void SetBeauty(Pawn pawn, int? degree)
        {
            SetTrait(pawn, DefDatabase<TraitDef>.GetNamed("Beauty"), degree);
        }

        private static bool Can(Pawn pawn, string recipeDefName)
        {
            var recipe = Recipe(recipeDefName);
            Assert.NotNull(recipe, "recipe " + recipeDefName + " missing");
            return SurgeryUtility.CanAssignAsIs(pawn, recipe);
        }

        // ---------- default blacklist ----------

        // Given: Plasteel Surgery loaded. When: the default list is resolved.
        // Then: face worsening and body-type recipes are in it; Masculinize, Feminize and Sex Change are not.
        [HospitalTest("PS1: default refused recipes hold Decrease Beauty and body-type mods, not Masculinize/Feminize/Sex Change", RequiresMod = PlasteelId)]
        public static void DefaultListContents(TestContext ctx)
        {
            
            var list = HospitalMapComponent.DefaultRefusedRecipes();

            foreach (var name in new[] { "PS_DecreaseBeautyBasic", "PS_DecreaseBeautyMid", "PS_MakeThin", "PS_MakeStandard", "PS_MakeHulk", "PS_MakeFat" })
                Assert.True(list.Contains(Recipe(name)), name + " missing from the default list");
            var bb = Recipe("PS_MakeBB");
            if (bb != null) Assert.True(list.Contains(bb), "PS_MakeBB missing from the default list");
            foreach (var name in new[] { "PS_MakeLookMale", "PS_MakeLookFemale", "PS_ChangeSex" })
            {
                var def = Recipe(name);
                if (def != null) Assert.False(list.Contains(def), name + " must not be in the default list");
            }
            Assert.False(list.Contains(null), "the list contains a null recipe");
        }

        // Given: a freshly constructed component (not registered on the map, new-map case).
        // When: its blacklist is read. Then: defaults are in refusedOperations; Decrease Beauty refused, Feminize allowed.
        [HospitalTest("PS2: a new HospitalMapComponent refuses the defaults; Feminize stays allowed", RequiresMod = PlasteelId)]
        public static void NewComponentHasDefaults(TestContext ctx)
        {
            
            var fresh = new HospitalMapComponent(ctx.Map);

            foreach (var def in HospitalMapComponent.DefaultRefusedRecipes())
                Assert.True(fresh.refusedOperations.Contains(def), def.defName + " not in refusedOperations of a new component");
            Assert.False(fresh.IsSurgeryRecipeAllowed(Recipe("PS_DecreaseBeautyMid")), "Decrease Beauty (Mid) must be refused");
            var feminize = Recipe("PS_MakeLookFemale");
            if (feminize != null) Assert.True(fresh.IsSurgeryRecipeAllowed(feminize), "Feminize must stay allowed");
        }

        // ---------- worker-rejected recipes ----------

        private static int BeautyDegree(Pawn pawn)
        {
            return pawn.story.traits.DegreeOfTrait(DefDatabase<TraitDef>.GetNamed("Beauty"));
        }

        // Beauty recipes are prepared surgeries (Hospital sets an allowed Beauty degree). Given: a Beautiful (+2) pawn.
        // Then: every Increase Beauty recipe is assignable (prepared), and checking eligibility leaves the trait alone.
        // Same at Beauty 0.
        [HospitalTest("PS3: Increase Beauty is assignable to any pawn that can be given an allowed beauty degree", RequiresMod = PlasteelId)]
        public static void BeautyRules(TestContext ctx)
        {
            var pawn = Adult(ctx);

            SetBeauty(pawn, 2);
            Assert.True(Can(pawn, "PS_IncreaseBeautyBasic"), "Basic on Beautiful");
            Assert.True(Can(pawn, "PS_IncreaseBeautyMid"), "Mid on Beautiful");
            Assert.True(Can(pawn, "PS_IncreaseBeautyAdv"), "Adv on Beautiful");
            Assert.Equal(2, BeautyDegree(pawn), "checking eligibility must not change the trait");

            SetBeauty(pawn, null);
            Assert.True(Can(pawn, "PS_IncreaseBeautyBasic"), "Basic at beauty 0");
            Assert.True(Can(pawn, "PS_IncreaseBeautyMid"), "Mid at beauty 0");
            Assert.True(Can(pawn, "PS_IncreaseBeautyAdv"), "Adv at beauty 0");
            Assert.Equal(0, BeautyDegree(pawn), "checking eligibility must not change the trait");
        }

        // Given: Beauty is disallowed for the pawn's kind (restored in cleanup), pawn Staggeringly ugly (-2).
        // Then: recipes that need a Beauty trait to be set are not assignable (Basic needs -1; Decrease Mid needs -1/+2);
        // recipes whose allowed degree is 0 (Increase Mid, Decrease Basic accepts 0/+1) still are.
        [HospitalTest("PS10: beauty recipes that need a trait the pawn cannot get are not assignable", RequiresMod = PlasteelId)]
        public static void DecreaseBeautyRules(TestContext ctx)
        {
            if (Recipe("PS_DecreaseBeautyBasic") == null || Recipe("PS_DecreaseBeautyMid") == null) return;
            var pawn = Adult(ctx);
            var beauty = DefDatabase<TraitDef>.GetNamed("Beauty");
            var kind = pawn.kindDef;
            var saved = kind.disallowedTraits;
            ctx.OnCleanup(() => kind.disallowedTraits = saved);
            kind.disallowedTraits = new List<TraitDef>(saved ?? new List<TraitDef>()) { beauty };
            SetBeauty(pawn, -2);

            Assert.False(Can(pawn, "PS_IncreaseBeautyBasic"), "Basic needs -1, which is disallowed");
            Assert.False(Can(pawn, "PS_DecreaseBeautyMid"), "Decrease Mid needs -1 or +2, which are disallowed");
            Assert.True(Can(pawn, "PS_IncreaseBeautyMid"), "Increase Mid needs no trait (degree 0)");
            Assert.True(Can(pawn, "PS_DecreaseBeautyBasic"), "Decrease Basic accepts degree 0");
        }

        // Given: a pawn at each start degree. When: AddSurgeryBill runs for Increase Beauty (Basic: only -1 allowed).
        // Then: it succeeds, the pawn is now Ugly (-1) and the bill is pending.
        [HospitalTest("PS13: AddSurgeryBill sets the allowed beauty degree for Increase Beauty (Basic)", RequiresMod = PlasteelId)]
        public static void BeautyPrepared(TestContext ctx)
        {
            var recipe = Recipe("PS_IncreaseBeautyBasic");
            if (recipe == null) return;
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            foreach (int? start in new int?[] { 2, null })
            {
                var pawn = Adult(ctx);
                SetBeauty(pawn, start);
                var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);

                bool failed = SurgeryUtility.AddSurgeryBill(pawn, data, hospital, recipe);

                Assert.False(failed, "AddSurgeryBill failed from " + start);
                Assert.Equal(-1, BeautyDegree(pawn), "beauty degree from " + start);
                Assert.Equal(1, pawn.story.traits.allTraits.Count(t => t.def.defName == "Beauty"), "exactly one Beauty trait");
                Assert.True(data.HasPendingSurgeryBill, "pending flag from " + start);
            }
        }

        // Given: a pawn at Beauty 0 (Mid) and one Staggeringly ugly (Basic); Decrease recipes are blacklisted by default but can be re-enabled.
        // When: AddSurgeryBill runs for Decrease Beauty Mid (allowed -1 or +2) and Basic (allowed 0 or +1).
        // Then: both succeed, the pawn's Beauty degree is one the recipe's worker accepts, and the bill is pending.
        [HospitalTest("PS15: AddSurgeryBill sets an allowed beauty degree for Decrease Beauty (Basic and Mid)", RequiresMod = PlasteelId)]
        public static void DecreaseBeautyPrepared(TestContext ctx)
        {
            var mid = Recipe("PS_DecreaseBeautyMid");
            var basic = Recipe("PS_DecreaseBeautyBasic");
            if (mid == null || basic == null) return;
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var cases = new[] { (mid, (int?)null, new[] { -1, 2 }), (basic, (int?)-2, new[] { 0, 1 }) };
            foreach (var (recipe, start, allowed) in cases)
            {
                var pawn = Adult(ctx);
                SetBeauty(pawn, start);
                var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);

                bool failed = SurgeryUtility.AddSurgeryBill(pawn, data, hospital, recipe);

                Assert.False(failed, recipe.defName + " AddSurgeryBill failed");
                Assert.True(allowed.Contains(BeautyDegree(pawn)), recipe.defName + " degree " + BeautyDegree(pawn) + " not allowed");
                Assert.True(data.HasPendingSurgeryBill, recipe.defName + " pending flag");
            }
        }

        // Given: Beautiful (+2) pawn, recipe Increase Beauty Mid (allowed degree 0 = no trait). When: AddSurgeryBill runs.
        // Then: the Beauty trait is gone. When TryPrepareBeauty is undone with RestoreBeauty on another +2 pawn, +2 is back.
        [HospitalTest("PS14: beauty preparation can target 'no trait' and can be undone", RequiresMod = PlasteelId)]
        public static void BeautyPrepareAndRestore(TestContext ctx)
        {
            var recipe = Recipe("PS_IncreaseBeautyMid");
            if (recipe == null) return;
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var pawn = Adult(ctx);
            SetBeauty(pawn, 2);
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);

            bool failed = SurgeryUtility.AddSurgeryBill(pawn, data, hospital, recipe);

            Assert.False(failed, "AddSurgeryBill failed");
            Assert.Equal(0, BeautyDegree(pawn), "Beauty trait removed");
            Assert.False(pawn.story.traits.allTraits.Any(t => t.def.defName == "Beauty"), "no Beauty trait left");

            var other = Adult(ctx);
            SetBeauty(other, 2);
            var prep = global::Hospital.Compat.PlasteelCompat.TryPrepareBeauty(other, recipe);
            Assert.NotNull(prep, "prepared");
            Assert.Equal(0, BeautyDegree(other), "prepared to no trait");
            global::Hospital.Compat.PlasteelCompat.RestoreBeauty(other, prep);
            Assert.Equal(2, BeautyDegree(other), "restored");
        }

        private static bool HasVoiceTrait(Pawn pawn)
        {
            return pawn.story.traits.allTraits.Any(t => t.def == TraitDefOf.AnnoyingVoice || t.def == TraitDefOf.CreepyBreathing);
        }

        private static List<TraitDef> VoiceTraits()
        {
            return new List<TraitDef> { TraitDefOf.AnnoyingVoice, TraitDefOf.CreepyBreathing };
        }

        // Laryngoplasty is a prepared surgery (Hospital adds the trait). Given: an adult without either trait.
        // Then: assignable; with a trait already: assignable too (as before).
        [HospitalTest("PS4: Laryngoplasty is assignable to an adult without the traits and to one that has a trait", RequiresMod = PlasteelId)]
        public static void LaryngoplastyRules(TestContext ctx)
        {
            if (Recipe("PS_Recipes_Surgery_Laryngoplasty") == null) return;
            var pawn = Adult(ctx);
            SetTrait(pawn, TraitDefOf.AnnoyingVoice, null);
            SetTrait(pawn, TraitDefOf.CreepyBreathing, null);

            Assert.True(Can(pawn, "PS_Recipes_Surgery_Laryngoplasty"), "without the traits (prepared)");
            Assert.False(HasVoiceTrait(pawn), "checking eligibility must not add a trait");
            SetTrait(pawn, TraitDefOf.CreepyBreathing, 0);

            Assert.True(Can(pawn, "PS_Recipes_Surgery_Laryngoplasty"), "with Creepy Breathing");
        }

        // Given: an adult whose traits conflict with both voice traits (simulated by a conflicting-trait def list on the
        // trait defs, restored in cleanup). Then: Laryngoplasty is not assignable. Skipped when no conflicting trait can be built.
        [HospitalTest("PS11: Laryngoplasty is not assignable when neither trait can be added", RequiresMod = PlasteelId)]
        public static void LaryngoplastyNotGrantable(TestContext ctx)
        {
            if (Recipe("PS_Recipes_Surgery_Laryngoplasty") == null) return;
            var pawn = Adult(ctx);
            SetTrait(pawn, TraitDefOf.AnnoyingVoice, null);
            SetTrait(pawn, TraitDefOf.CreepyBreathing, null);
            // Any trait the pawn has becomes conflicting with both voice traits via their conflictingTraits lists.
            var other = pawn.story.traits.allTraits.FirstOrDefault()?.def;
            if (other == null) return;
            var voice = VoiceTraits();
            var saved = voice.Select(d => d.conflictingTraits).ToList();
            ctx.OnCleanup(() => { for (int i = 0; i < voice.Count; i++) voice[i].conflictingTraits = saved[i]; });
            foreach (var d in voice)
            {
                d.conflictingTraits = new List<TraitDef>(d.conflictingTraits ?? new List<TraitDef>()) { other };
            }

            Assert.False(Can(pawn, "PS_Recipes_Surgery_Laryngoplasty"), "neither trait can be added");
        }

        // Given: an adult without the traits and a free voice part. When: AddSurgeryBill runs for Laryngoplasty.
        // Then: it returns false, the pawn has exactly one of the two traits and a bill exists (no double trait on a second run).
        [HospitalTest("PS12: AddSurgeryBill gives a Laryngoplasty patient exactly one voice trait", RequiresMod = PlasteelId)]
        public static void LaryngoplastyPrepared(TestContext ctx)
        {
            var recipe = Recipe("PS_Recipes_Surgery_Laryngoplasty");
            if (recipe == null) return;
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var pawn = Adult(ctx);
            SetTrait(pawn, TraitDefOf.AnnoyingVoice, null);
            SetTrait(pawn, TraitDefOf.CreepyBreathing, null);
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);

            bool failed = SurgeryUtility.AddSurgeryBill(pawn, data, hospital, recipe);

            Assert.False(failed, "AddSurgeryBill failed");
            Assert.Equal(1, pawn.story.traits.allTraits.Count(t => t.def == TraitDefOf.AnnoyingVoice || t.def == TraitDefOf.CreepyBreathing), "voice traits");
            Assert.True(data.HasPendingSurgeryBill, "pending flag");
        }

        // Given: a Super-immune (degree +1) pawn. Then: Immunity Boost is not assignable; without the trait it is.
        [HospitalTest("PS5: Immunity Boost is not assigned to a Super-immune pawn", RequiresMod = PlasteelId)]
        public static void ImmunityBoostRules(TestContext ctx)
        {
            if (Recipe("PS_Recipes_Surgery_ImmunityBoost") == null) return;
            var pawn = Adult(ctx);
            var immunity = DefDatabase<TraitDef>.GetNamed("Immunity");

            SetTrait(pawn, immunity, 1);
            Assert.False(Can(pawn, "PS_Recipes_Surgery_ImmunityBoost"), "Super-immune");

            SetTrait(pawn, immunity, null);
            Assert.True(Can(pawn, "PS_Recipes_Surgery_ImmunityBoost"), "no immunity trait");
        }

        // Given: a Thin male adult. Then: Make Thin not assignable (current type), Make Fat is, Make BB (female only) is not.
        [HospitalTest("PS6: body-type recipes reject the current body type and the wrong gender", RequiresMod = PlasteelId)]
        public static void BodyTypeRules(TestContext ctx)
        {
            if (Recipe("PS_MakeThin") == null) return;
            var pawn = Adult(ctx);
            pawn.gender = Gender.Male;
            pawn.story.bodyType = BodyTypeDefOf.Thin;

            Assert.False(Can(pawn, "PS_MakeThin"), "Make Thin on a Thin pawn");
            Assert.True(Can(pawn, "PS_MakeFat"), "Make Fat on a Thin pawn");
            if (Recipe("PS_MakeBB") != null) Assert.False(Can(pawn, "PS_MakeBB"), "Make BB on a male pawn");
        }

        // Given: a child (generated at age 8; the test fails if the game gives an adult stage). Then: no body-type recipe is assignable,
        // although the Plasteel worker itself would accept them.
        [HospitalTest("PS7: body-type recipes are never assigned to a child", RequiresMod = PlasteelId, RequiresDlc = "Ludeon.RimWorld.Biotech")]
        public static void BodyTypeNotForChildren(TestContext ctx)
        {
            if (Recipe("PS_MakeThin") == null) return;
            var child = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, fixedBiologicalAge: 8, fixedChronologicalAge: 8));
            ctx.Track(child);
            Assert.False(child.DevelopmentalStage.Adult(), "precondition: generated pawn is not a child");
            Assert.NotNull(child.story?.bodyType, "precondition: child has no body type");

            foreach (var name in new[] { "PS_MakeThin", "PS_MakeStandard", "PS_MakeHulk", "PS_MakeFat" })
            {
                var recipe = Recipe(name);
                if (recipe == null) continue;
                Assert.False(SurgeryUtility.CanAssignAsIs(child, recipe), name + " must not be assigned to a child");
            }
        }

        // ---------- end to end ----------

        // Given: a Beautiful pawn and every recipe except Increase Beauty refused (list swapped, restored in cleanup).
        // When: AddRandomSurgeryBill runs. Then: it returns true (caller falls back to wounds), adds no bill and sets no pending flag.
        [HospitalTest("PS9: AddRandomSurgeryBill adds no bill when the only candidates are impossible for the pawn", RequiresMod = PlasteelId)]
        public static void NoImpossibleBill(TestContext ctx)
        {
                        var hospital = VanillaCompatTests.HospitalOf(ctx);
            var pawn = Adult(ctx);
            SetBeauty(pawn, 2);
            var saved = hospital.refusedOperations;
            ctx.OnCleanup(() => hospital.refusedOperations = saved);
            hospital.refusedOperations = ThingDefOf.Human.AllRecipes
                .Where(r => !r.defName.StartsWith("PS_IncreaseBeauty")).Distinct().ToList();
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);
            int billsBefore = pawn.health.surgeryBills.Count;

            bool fallback = SurgeryUtility.AddRandomSurgeryBill(pawn, data, hospital);

            Assert.True(fallback, "expected the wounds fallback (true)");
            Assert.Equal(billsBefore, pawn.health.surgeryBills.Count, "surgery bills");
            Assert.False(data.HasPendingSurgeryBill, "pending flag must stay false");
        }
    }
}
