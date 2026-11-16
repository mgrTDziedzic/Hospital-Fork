using System;
using System.Linq;
using System.Reflection;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    public static class HospitalRegressionTests
    {
        internal static Pawn MakePawn(TestContext ctx, Faction faction, bool spawn)
        {
            var map = ctx.Map;
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, faction,
                PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
            ctx.Track(pawn);
            if (spawn)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
                GenSpawn.Spawn(pawn, cell, map);
            }
            return pawn;
        }

        internal static (Building_Bed bed, CompHospitalBed comp) MakeBed(TestContext ctx)
        {
            var map = ctx.Map;
            // plain (non-medical) bed so vanilla allows assigning owners; Hospital injects its comp into every bed that can be medical
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 25,
                    c => c.Standable(map) && c.GetEdifice(map) == null && GenAdj.OccupiedRect(c, Rot4.North, bed.def.size).InBounds(map)
                         && GenAdj.OccupiedRect(c, Rot4.North, bed.def.size).Cells.All(x => x.Standable(map) && x.GetEdifice(map) == null),
                    out var cell))
                Assert.Fail("no free cell for a test bed near the map centre");
            GenSpawn.Spawn(bed, cell, map, Rot4.North);
            ctx.Track(bed);
            var comp = bed.GetComp<CompHospitalBed>();
            Assert.NotNull(comp, "bed has no CompHospitalBed (injector did not run?)");
            return (bed, comp);
        }

        [HospitalTest("A3: switching a bed to hospital mode removes colonist owners")]
        public static void BedHospitalModeRemovesColonistOwner(TestContext ctx)
        {
            var (bed, comp) = MakeBed(ctx);
            var colonist = MakePawn(ctx, Faction.OfPlayer, true);
            comp.Hospital = false; // normal bed
            bed.CompAssignableToPawn.TryAssignPawn(colonist);
            Assert.True(bed.OwnersForReading.Contains(colonist), "could not assign colonist to test bed");

            comp.Hospital = true;

            Assert.False(bed.OwnersForReading.Contains(colonist), "colonist must lose the bed in hospital mode");
        }

        // Given: a bed in hospital mode owned by a registered patient.
        // When:  the bed is switched back to normal mode.
        // Then:  the patient loses the bed.
        [HospitalTest("A3: switching a bed back to normal mode removes patient owners (patients may own it in hospital mode)")]
        public static void BedNormalModeRemovesPatientOwner(TestContext ctx)
        {
            var (bed, comp) = MakeBed(ctx);
            var patient = MakePawn(ctx, Faction.OfPlayer, true);
            var hospital = ctx.Map.GetComponent<HospitalMapComponent>();
            Assert.NotNull(hospital, "map has no HospitalMapComponent");
            comp.Hospital = false;
            bed.CompAssignableToPawn.TryAssignPawn(patient);
            Assert.True(bed.OwnersForReading.Contains(patient), "could not assign patient to test bed");
            hospital.Patients.Add(patient, new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Test));
            ctx.OnCleanup(() => hospital.Patients.Remove(patient));
            comp.Hospital = true; // patient owner is fine in hospital mode
            Assert.True(bed.OwnersForReading.Contains(patient), "patient should keep the bed in hospital mode");

            comp.Hospital = false;

            Assert.False(bed.OwnersForReading.Contains(patient), "patient must lose the bed in normal mode");
        }

        private static readonly MethodInfo DamageParts = typeof(WoundsUtility).GetMethod("DamageParts", BindingFlags.NonPublic | BindingFlags.Static);

        [HospitalTest("A14: DamageParts leaves forceDowned false")]
        public static void DamagePartsResetsForceDowned(TestContext ctx)
        {
            Assert.NotNull(DamageParts, "WoundsUtility.DamageParts not found (renamed?)");
            var pawn = MakePawn(ctx, null, false);
            Assert.False(pawn.health.forceDowned, "precondition");

            DamageParts.Invoke(null, new object[] { pawn, 20, null });

            Assert.False(pawn.health.forceDowned, "forceDowned left set after DamageParts");
        }

        // Given: a pawn and a bogus damage def that makes DamageParts fail.
        // When:  DamageParts is called and throws.
        // Then:  forceDowned is still false afterwards.
        [HospitalTest("A14: forceDowned is reset even when DamageParts throws")]
        public static void DamagePartsResetsForceDownedOnException(TestContext ctx)
        {
            Assert.NotNull(DamageParts, "WoundsUtility.DamageParts not found (renamed?)");
            var pawn = MakePawn(ctx, null, false);
            // A bogus damage def makes the hediff lookup blow up (or at least go through the failure path).
            var bogus = new DamageDef { defName = "HospitalTestBogus", label = "bogus" };
            try { DamageParts.Invoke(null, new object[] { pawn, 20, bogus }); }
            catch (TargetInvocationException) { /* expected */ }

            Assert.False(pawn.health.forceDowned, "forceDowned left set after DamageParts threw");
        }

        // Given: a map with a Patient Landing Spot (one is placed if the save has none).
        // When:  a patient landing cell is requested.
        // Then:  the cell is valid, in bounds and is the landing spot.
        [HospitalTest("A2: TryFindPatientLandingCell returns a valid in-bounds cell and prefers the Patient Landing Spot")]
        public static void LandingCellIsValid(TestContext ctx)
        {
            var map = ctx.Map;
            var spotDef = ThingDef.Named("PatientLandingSpot");
            var spots = map.listerBuildings.AllBuildingsColonistOfDef(spotDef).Where(b => b.Spawned).Select(b => b.Position).ToList();

            Assert.True(IncidentHelper.TryFindPatientLandingCell(map, out var cell), "no landing cell found");
            Assert.True(cell.IsValid && cell.InBounds(map), "landing cell invalid or out of bounds: " + cell);
            if (spots.Count > 0)
                Assert.True(spots.Contains(cell), "existing Patient Landing Spot ignored; got " + cell);
            else
            {
                // no spot in the test save: place one and expect exactly that cell
                var spot = ctx.Track(ThingMaker.MakeThing(spotDef));
                spot.SetFaction(Faction.OfPlayer);
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 20, c => c.Standable(map) && c.GetEdifice(map) == null, out var spotCell))
                    Assert.Fail("no free cell for a test landing spot");
                GenSpawn.Spawn(spot, spotCell, map);
                Assert.True(IncidentHelper.TryFindPatientLandingCell(map, out var cell2), "no cell with a spot present");
                Assert.Equal(spotCell, cell2, "spot not preferred");
            }
        }

        // Given: hospital settings as an old save leaves them: null lists, too-short and too-long opening hours.
        // When:  EnsureValidSettings runs.
        // Then:  null lists are restored to defaults, short hours padded with closed, long hours truncated to 24.
        [HospitalTest("B1: EnsureValidSettings restores null settings and fixes opening hours to 24 entries (pad short, truncate long)")]
        public static void EnsureValidSettingsRepairsOldSaveState(TestContext ctx)
        {
            var hospital = ctx.Map.GetComponent<HospitalMapComponent>();
            Assert.NotNull(hospital, "map has no HospitalMapComponent");
            var hours = hospital.openingHours;
            var ops = hospital.refusedOperations;
            var diseases = hospital.refusedDiseases;
            var factions = hospital.refusedFactions;
            ctx.OnCleanup(() =>
            {
                hospital.openingHours = hours;
                hospital.refusedOperations = ops;
                hospital.refusedDiseases = diseases;
                hospital.refusedFactions = factions;
            });

            // node missing in the save -> Scribe_Collections.Look leaves null
            hospital.openingHours = null;
            hospital.refusedOperations = null;
            hospital.refusedDiseases = null;
            hospital.refusedFactions = null;
            hospital.EnsureValidSettings();
            Assert.NotNull(hospital.openingHours, "openingHours not restored");
            Assert.Equal(24, hospital.openingHours.Count, "restored openingHours size");
            Assert.True(hospital.openingHours.SequenceEqual(HospitalMapComponent.DefaultOpeningHours()), "restored openingHours are not the defaults");
            Assert.NotNull(hospital.refusedOperations, "refusedOperations not restored");
            Assert.NotNull(hospital.refusedDiseases, "refusedDiseases not restored");
            Assert.NotNull(hospital.refusedFactions, "refusedFactions not restored");
            hospital.IsOpen(); // threw NRE before the fix

            // too short: saved values kept, padded with "closed"
            hospital.openingHours = Enumerable.Repeat(true, 5).ToList();
            hospital.EnsureValidSettings();
            Assert.Equal(24, hospital.openingHours.Count, "short openingHours not padded");
            Assert.True(hospital.openingHours.Take(5).All(h => h) && hospital.openingHours.Skip(5).All(h => !h), "short openingHours padded wrongly");

            // too long: truncated
            hospital.openingHours = Enumerable.Repeat(true, 30).ToList();
            hospital.EnsureValidSettings();
            Assert.Equal(24, hospital.openingHours.Count, "long openingHours not truncated");
        }
    }
}
