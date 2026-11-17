using System;
using System.Collections.Generic;
using System.Linq;
using Hospital;
using Hospital.Compat;
using Hospital.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace HospitalTests
{
    // MedPod detection (by thing class, not defName) and the central mod-def filters (exact prefixes).
    public static class ModCompatTests
    {
        private static ThingDef FirstMedPodBed()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.IsBed && MedPodCompat.IsMedPodBed(d));
        }

        // Given: MedPod active. When: a MedPod bed is made. Then: IsMedPodBed is true; the new comp has Hospital and Surgery off.
        [HospitalTest("MP1: a MedPod bed is detected by class; a new one starts as a normal bed without surgery", RequiresMod = SupportedMods.MedPod)]
        public static void MedPodBedDetected(TestContext ctx)
        {
            var def = FirstMedPodBed();
            Assert.NotNull(def, "no bed def with thing class MedPod.Building_BedMedPod");
            Assert.True(MedPodCompat.IsMedPodBed(def), "IsMedPodBed false for " + def.defName);

            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            var comp = thing.TryGetComp<CompHospitalBed>();
            Assert.NotNull(comp, "MedPod bed has no CompHospitalBed");
            Assert.False(comp.Hospital, "a new MedPod bed must not be in hospital mode");
            Assert.False(comp.Surgery, "a new MedPod bed must not be a surgery bed");
        }

        // Given: vanilla beds (and a null def). Then: never treated as MedPods, with or without the mod.
        [HospitalTest("MP2: vanilla beds are not MedPod beds")]
        public static void VanillaBedNotMedPod(TestContext ctx)
        {
            Assert.False(MedPodCompat.IsMedPodBed(ThingDefOf.Bed), "Bed");
            var hospitalBed = DefDatabase<ThingDef>.GetNamedSilentFail("HospitalBed");
            if (hospitalBed != null) Assert.False(MedPodCompat.IsMedPodBed(hospitalBed), "HospitalBed");
            Assert.False(MedPodCompat.IsMedPodBed(null), "null");
        }

        private static bool HasNode(ThinkNode node, string typeName, int depth = 0)
        {
            if (node == null || depth > 20) return false;
            if (node.GetType().FullName == typeName) return true;
            return node.subNodes != null && node.subNodes.Any(n => HasNode(n, typeName, depth + 1));
        }

        // Given: MedPod active. Then: the Patient duty think tree holds MedPod's patient job giver
        // (Patches/MedPod.xml applied through MayRequireAnyOf).
        [HospitalTest("MP3: the Patient duty think tree contains JobGiver_PatientGoToMedPod", RequiresMod = SupportedMods.MedPod)]
        public static void ThinkTreePatched(TestContext ctx)
        {
            var duty = DefDatabase<DutyDef>.GetNamed("Patient");
            Assert.True(HasNode(duty.thinkNode, "MedPod.JobGiver_PatientGoToMedPod"), "JobGiver_PatientGoToMedPod missing from the Patient duty");
        }

        private static T Make<T>(string defName) where T : Def, new()
        {
            return new T { defName = defName };
        }

        // Given: synthetic defs. Then: exact prefixes match, similar names do not.
        [HospitalTest("FLT1: ModDefFilters match exact prefixes (VREA_, GR_, VDE) and not look-alike names")]
        public static void FilterPrefixes(TestContext ctx)
        {
            Assert.True(ModDefFilters.IsAndroidDef(Make<RecipeDef>("VREA_RemoveArtificialPart")), "VREA_ recipe");
            Assert.True(ModDefFilters.IsAndroidDef(Make<FactionDef>("VREA_AndroidAwakenedUtopia")), "VREA_ faction");
            Assert.False(ModDefFilters.IsAndroidDef(Make<FactionDef>("MyVREAThing")), "MyVREAThing");
            Assert.False(ModDefFilters.IsAndroidDef(Make<FactionDef>("vrea_lower")), "lower case is not a match");

            Assert.True(ModDefFilters.IsExcludedSurgery(Make<RecipeDef>("GR_InstallBearClaws")), "GR_ recipe");
            Assert.True(ModDefFilters.IsExcludedSurgery(Make<RecipeDef>("VREA_RemoveArtificialPart")), "VREA_ recipe is an excluded surgery");
            foreach (var name in new[] { "InstallUpgradedArm", "InstallIntegratedPart", "RegrowLimb", "GraftSkin" })
                Assert.False(ModDefFilters.IsExcludedSurgery(Make<RecipeDef>(name)), name + " must not be excluded");

            Assert.True(ModDefFilters.IsExcludedDisease(Make<HediffDef>("GR_ChickenRimPox")), "GR_ disease");
            Assert.True(ModDefFilters.IsExcludedDisease(Make<HediffDef>("VDE_SeparationSickness")), "VDE disease");
            Assert.False(ModDefFilters.IsExcludedDisease(Make<HediffDef>("Flu")), "Flu");
            Assert.False(ModDefFilters.IsExcludedDisease(Make<HediffDef>("Gangrene")), "Gangrene (contains 'gr')");
        }

        // The assignment filter as it was before the prefix change, for vanilla recipes only (no mod terms matter there).
        private static bool OldAccepted(RecipeDef recipe)
        {
            var n = recipe.defName.ToLower();
            return recipe.IsSurgery && !recipe.isViolation && !n.Contains("abasia") && !n.Contains("coma") && !n.Contains("remove")
                   && !n.Contains("anesthetize") && !n.Contains("blood") && !n.Contains("admin") && !n.Contains("rib") && !n.Contains("harvest")
                   && !recipe.defName.ToUpper().Contains("VREA") && !recipe.defName.ToUpper().Contains("GR");
        }

        // Given: every recipe of the loaded game. When: the merged name predicate runs (IsBlacklistableSurgery).
        // Then: nothing the old filter accepted is rejected now (the new filter may only accept more).
        [HospitalTest("FLT2: the merged surgery name filter still accepts every recipe the old filter accepted")]
        public static void MergedSurgeryFilterNoRegression(TestContext ctx)
        {
            var rejected = DefDatabase<RecipeDef>.AllDefsListForReading
                // Aggression surgeries (CureScaria) were accepted by the old filter but are now deliberately rejected
                // unless the aggressive patients challenge is on, so they are not regressions.
                .Where(r => OldAccepted(r) && !SurgeryUtility.IsAggressionSurgery(r) && !SurgeryUtility.IsBlacklistableSurgery(r)).Select(r => r.defName).ToList();
            Assert.True(rejected.Count == 0, "newly rejected recipes: " + string.Join(", ", rejected));
        }
    }
}
