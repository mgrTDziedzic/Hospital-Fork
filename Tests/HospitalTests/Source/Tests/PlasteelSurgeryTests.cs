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

        // Given: a Beautiful (+2) pawn. Then: no Increase Beauty recipe is assignable.
        // When the pawn has Beauty 0: only Mid is.
        [HospitalTest("PS3: Increase Beauty is not assigned when the worker rejects the pawn's beauty", RequiresMod = PlasteelId)]
        public static void BeautyRules(TestContext ctx)
        {
                        var pawn = Adult(ctx);

            SetBeauty(pawn, 2);
            Assert.False(Can(pawn, "PS_IncreaseBeautyBasic"), "Basic on Beautiful");
            Assert.False(Can(pawn, "PS_IncreaseBeautyMid"), "Mid on Beautiful");
            Assert.False(Can(pawn, "PS_IncreaseBeautyAdv"), "Adv on Beautiful");

            SetBeauty(pawn, null);
            Assert.False(Can(pawn, "PS_IncreaseBeautyBasic"), "Basic at beauty 0");
            Assert.True(Can(pawn, "PS_IncreaseBeautyMid"), "Mid at beauty 0");
            Assert.False(Can(pawn, "PS_IncreaseBeautyAdv"), "Adv at beauty 0");
        }

        // Given: a Staggeringly ugly (-2) pawn. Then: neither Decrease Beauty recipe is assignable (nothing left to worsen).
        // When the pawn is Ugly (-1): Mid is assignable, Basic is not.
        [HospitalTest("PS10: Decrease Beauty is not assigned when the worker rejects the pawn's beauty (Basic at 0/+1, Mid at -1/+2)", RequiresMod = PlasteelId)]
        public static void DecreaseBeautyRules(TestContext ctx)
        {
            if (Recipe("PS_DecreaseBeautyBasic") == null || Recipe("PS_DecreaseBeautyMid") == null) return;
            var pawn = Adult(ctx);

            SetBeauty(pawn, -2);
            Assert.False(Can(pawn, "PS_DecreaseBeautyBasic"), "Basic on Staggeringly ugly");
            Assert.False(Can(pawn, "PS_DecreaseBeautyMid"), "Mid on Staggeringly ugly");

            SetBeauty(pawn, -1);
            Assert.False(Can(pawn, "PS_DecreaseBeautyBasic"), "Basic on Ugly");
            Assert.True(Can(pawn, "PS_DecreaseBeautyMid"), "Mid on Ugly");
        }

        // Given: a pawn without Annoying Voice / Creepy Breathing. Then: Laryngoplasty is not assignable.
        // When the pawn gets Creepy Breathing: it is.
        [HospitalTest("PS4: Laryngoplasty is only assigned to a pawn with Annoying Voice or Creepy Breathing", RequiresMod = PlasteelId)]
        public static void LaryngoplastyRules(TestContext ctx)
        {
            if (Recipe("PS_Recipes_Surgery_Laryngoplasty") == null) return;
            var pawn = Adult(ctx);
            SetTrait(pawn, TraitDefOf.AnnoyingVoice, null);
            SetTrait(pawn, TraitDefOf.CreepyBreathing, null);

            Assert.False(Can(pawn, "PS_Recipes_Surgery_Laryngoplasty"), "without the traits");
            SetTrait(pawn, TraitDefOf.CreepyBreathing, 0);

            Assert.True(Can(pawn, "PS_Recipes_Surgery_Laryngoplasty"), "with Creepy Breathing");
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
