using System.Collections.Generic;
using System.Linq;
using Hospital;
using Hospital.Patches;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Need-based hospital bed choice (Patients_BedFinder_Patch): surgery -> SurgerySuccessChanceFactor,
    // non-immune immunizable disease -> ImmunityGainSpeedFactor, otherwise MedicalTendQualityOffset.
    // Room cleanliness and light also change the stats, so each test reads the real stat values, requires them to differ,
    // and passes the worse bed FIRST (the old code took the first valid bed).
    public static class BedSelectionTests
    {
        // ---------- helpers ----------

        private static Building_Bed MakeBed(TestContext ctx, ThingDef bedDef, QualityCategory? quality = null)
        {
            var map = ctx.Map;
            var stuff = bedDef.MadeFromStuff ? GenStuff.DefaultStuffFor(bedDef) : null;
            var bed = (Building_Bed)ThingMaker.MakeThing(bedDef, stuff);
            bed.SetFaction(Faction.OfPlayer);
            if (quality.HasValue)
            {
                var q = bed.TryGetComp<CompQuality>();
                Assert.NotNull(q, "precondition: " + bedDef.defName + " has no quality");
                q.SetQuality(quality.Value, ArtGenerationContext.Colony);
            }
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 25,
                    c => GenAdj.OccupiedRect(c, Rot4.North, bedDef.size).InBounds(map)
                         && GenAdj.OccupiedRect(c, Rot4.North, bedDef.size).Cells.All(x => x.Standable(map) && x.GetEdifice(map) == null),
                    out var cell))
                Assert.Fail("no free cell for a test bed");
            GenSpawn.Spawn(bed, cell, map, Rot4.North);
            ctx.Track(bed);
            bed.Medical = true;
            var comp = bed.GetComp<CompHospitalBed>();
            Assert.NotNull(comp, "bed has no CompHospitalBed");
            comp.Hospital = true;
            Assert.True(bed.Medical, "precondition: test bed is not medical");
            return bed;
        }

        private static ThingDef HospitalBedDef => DefDatabase<ThingDef>.GetNamed("HospitalBed");

        private static void AddSurgeryBill(Pawn pawn)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("InstallPegLeg");
            Assert.NotNull(recipe, "recipe InstallPegLeg missing");
            var part = pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Leg).First();
            global::Hospital.Utilities.SurgeryUtility.CreatePatientBill(pawn, recipe, part);
            Assert.True(pawn.health.surgeryBills.Count > 0, "precondition: surgery bill was not created");
        }

        private static void AddFlu(Pawn pawn)
        {
            var flu = DefDatabase<HediffDef>.GetNamed("Flu");
            pawn.health.AddHediff(flu);
            Assert.True(pawn.health.hediffSet.HasHediff(flu), "precondition: flu was not added");
        }

        private static void AddCut(Pawn pawn)
        {
            var part = pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Arm).First();
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
            cut.Severity = 4f;
            pawn.health.AddHediff(cut, part);
        }

        // Asserts that the two beds differ in the stat for this need and returns them as (worse, better).
        private static (Building_Bed worse, Building_Bed better) Order(Building_Bed a, Building_Bed b, PatientBedNeed need)
        {
            float sa = Patients_BedFinder_Patch.ScoreBed(a, need), sb = Patients_BedFinder_Patch.ScoreBed(b, need);
            Assert.True(System.Math.Abs(sa - sb) > 0.001f, "precondition: both test beds have the same " + need + " score (" + sa + ")");
            return sa < sb ? (a, b) : (b, a);
        }

        // ---------- need classification ----------

        // Given: a clean pawn. When: a flu is added, then a surgery bill. Then: Tend -> Immunity -> Surgery.
        [HospitalTest("BedPick: patient need is Tend for wounds, Immunity for a non-immune disease, Surgery when a surgery bill exists")]
        public static void NeedClassification(TestContext ctx)
        {
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            AddCut(pawn);
            Assert.Equal(PatientBedNeed.Tend, Patients_BedFinder_Patch.GetNeed(pawn), "wounded pawn");
            AddFlu(pawn);
            Assert.Equal(PatientBedNeed.Immunity, Patients_BedFinder_Patch.GetNeed(pawn), "pawn with flu");
            AddSurgeryBill(pawn);
            Assert.Equal(PatientBedNeed.Surgery, Patients_BedFinder_Patch.GetNeed(pawn), "pawn with flu and a surgery bill");
        }

        // ---------- stat-based choice ----------

        // Given: two hospital beds, awful and legendary quality (quality scales SurgerySuccessChanceFactor), neither marked Surgery.
        // When:  a pawn with a surgery bill picks between them, worse bed listed first.
        // Then:  the bed with the higher surgery success factor is chosen.
        [HospitalTest("BedPick: a surgery patient gets the bed with the higher surgery success chance factor")]
        public static void SurgeryPicksBestSurgeryFactor(TestContext ctx)
        {
            var (worse, better) = Order(MakeBed(ctx, HospitalBedDef, QualityCategory.Awful), MakeBed(ctx, HospitalBedDef, QualityCategory.Legendary), PatientBedNeed.Surgery);
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            AddSurgeryBill(pawn);
            var picked = Patients_BedFinder_Patch.SelectBed(new List<Building_Bed> { worse, better }, pawn);
            Assert.True(picked == better, "picked the bed with surgery factor " + Patients_BedFinder_Patch.ScoreBed(picked, PatientBedNeed.Surgery)
                                          + " instead of " + Patients_BedFinder_Patch.ScoreBed(better, PatientBedNeed.Surgery));
        }

        // Given: a plain wooden bed set to medical (immunity factor 1) and a hospital bed (1.11).
        // When:  a pawn with flu picks between them, worse bed listed first.
        // Then:  the bed with the higher immunity gain speed factor is chosen.
        [HospitalTest("BedPick: a patient with a non-immune disease gets the bed with the higher immunity gain speed factor")]
        public static void DiseasePicksBestImmunity(TestContext ctx)
        {
            var (worse, better) = Order(MakeBed(ctx, ThingDefOf.Bed), MakeBed(ctx, HospitalBedDef), PatientBedNeed.Immunity);
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            AddFlu(pawn);
            var picked = Patients_BedFinder_Patch.SelectBed(new List<Building_Bed> { worse, better }, pawn);
            Assert.True(picked == better, "did not pick the bed with the higher immunity gain speed factor");
        }

        // Given: a plain wooden bed set to medical (tend offset 0) and a hospital bed (+10%).
        // When:  a wounded pawn picks between them, worse bed listed first.
        // Then:  the bed with the higher medical tend quality offset is chosen.
        [HospitalTest("BedPick: a wounded patient gets the bed with the higher medical tend quality offset")]
        public static void WoundsPickBestTendQuality(TestContext ctx)
        {
            var (worse, better) = Order(MakeBed(ctx, ThingDefOf.Bed), MakeBed(ctx, HospitalBedDef), PatientBedNeed.Tend);
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            AddCut(pawn);
            var picked = Patients_BedFinder_Patch.SelectBed(new List<Building_Bed> { worse, better }, pawn);
            Assert.True(picked == better, "did not pick the bed with the higher tend quality offset");
        }

        // ---------- surgery flag ----------

        // Given: a legendary hospital bed NOT marked Surgery and an awful one marked Surgery.
        // When:  a surgery patient picks; then a wounded patient picks from the Surgery bed only.
        // Then:  the surgery patient gets the Surgery bed despite worse stats; the wounded patient falls back to the Surgery bed.
        [HospitalTest("BedPick: the Surgery bed flag beats stats; a non-surgery patient falls back to a Surgery bed when it is the only one")]
        public static void SurgeryFlagBeatsStats(TestContext ctx)
        {
            var good = MakeBed(ctx, HospitalBedDef, QualityCategory.Legendary);
            var surgeryBed = MakeBed(ctx, HospitalBedDef, QualityCategory.Awful);
            surgeryBed.GetComp<CompHospitalBed>().Surgery = true;

            var surgeon = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            AddSurgeryBill(surgeon);
            Assert.True(Patients_BedFinder_Patch.SelectBed(new List<Building_Bed> { good, surgeryBed }, surgeon) == surgeryBed,
                "surgery patient did not get the bed marked Surgery");

            var wounded = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            AddCut(wounded);
            Assert.True(Patients_BedFinder_Patch.SelectBed(new List<Building_Bed> { surgeryBed, good }, wounded) == good,
                "wounded patient did not prefer the non-surgery bed");
            Assert.True(Patients_BedFinder_Patch.SelectBed(new List<Building_Bed> { surgeryBed }, wounded) == surgeryBed,
                "wounded patient did not fall back to the only (Surgery) bed");
            Assert.True(Patients_BedFinder_Patch.SelectBed(new List<Building_Bed>(), wounded) == null, "empty candidate list must give null");
        }

        // ---------- wiring through RestUtility.FindBedFor ----------

        // Given: a registered, wounded patient and two test hospital beds (wooden medical bed + hospital bed).
        // When:  vanilla RestUtility.FindBedFor runs (Hospital postfix applies).
        // Then:  the result is the bed SelectBed picks from every valid hospital bed on the map (the save may have its own beds).
        [HospitalTest("BedPick: RestUtility.FindBedFor returns the need-based best hospital bed for a patient")]
        public static void FindBedForUsesSelection(TestContext ctx)
        {
            MakeBed(ctx, ThingDefOf.Bed);
            MakeBed(ctx, HospitalBedDef);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            AddCut(patient);
            VanillaCompatTests.Register(ctx, patient);

            var candidates = ctx.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                .Where(b => b.Medical && b.GetComp<CompHospitalBed>() is CompHospitalBed c && c.Hospital
                            && RestUtility.IsValidBedFor(b, patient, patient, checkSocialProperness: false))
                .ToList();
            Assert.True(candidates.Count >= 2, "precondition: test beds are not valid for the patient (" + candidates.Count + " candidates)");

            var expected = Patients_BedFinder_Patch.SelectBed(candidates, patient);
            var result = RestUtility.FindBedFor(patient, patient, false, false, GuestStatus.Guest);
            Assert.True(result == expected, "FindBedFor returned " + (result?.ThingID ?? "null") + ", expected " + expected.ThingID);
        }
    }
}
