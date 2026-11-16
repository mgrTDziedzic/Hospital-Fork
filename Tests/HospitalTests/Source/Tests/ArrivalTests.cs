using System.Collections.Generic;
using System.Linq;
using Hospital;
using RimWorld;
using Verse;

namespace HospitalTests
{
    public static class ArrivalTests
    {
        // Given: the hospital is open with a free hospital bed and danger accepted.
        // When:  the PatientArrives incident is forced.
        // Then:  new patients are registered with sane PatientData, are guests under a new lord and arrive in a drop pod.
        [HospitalTest("Smoke: PatientArrives registers patients with sane PatientData, as guests under a new lord, arriving in drop pods")]
        public static void PatientArrivalSmoke(TestContext ctx)
        {
            var map = ctx.Map;
            var hospital = map.GetComponent<HospitalMapComponent>();
            Assert.NotNull(hospital, "map has no HospitalMapComponent");
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail("PatientArrives");
            Assert.NotNull(def, "IncidentDef PatientArrives not found");
            var worker = def.Worker as IncidentWorker_PatientArrives;
            Assert.NotNull(worker, "PatientArrives worker is not IncidentWorker_PatientArrives");

            // Open the hospital for the duration of the test.
            bool openBefore = hospital.openForBusiness;
            var hoursBefore = hospital.openingHours;
            bool dangerBefore = hospital.AcceptDanger;
            ctx.OnCleanup(() =>
            {
                hospital.openForBusiness = openBefore;
                hospital.openingHours = hoursBefore;
                hospital.AcceptDanger = dangerBefore;
            });
            hospital.openForBusiness = true;
            hospital.openingHours = Enumerable.Repeat(true, GenDate.HoursPerDay).ToList();
            hospital.AcceptDanger = true; // do not let weather conditions of the test map block the arrival

            // Need a free hospital bed; add one when the save has none free.
            if (hospital.IsFull())
            {
                var bedDef = DefDatabase<ThingDef>.GetNamed("HospitalBed");
                var bed = (Building_Bed)ThingMaker.MakeThing(bedDef, bedDef.MadeFromStuff ? GenStuff.DefaultStuffFor(bedDef) : null);
                bed.SetFaction(Faction.OfPlayer);
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 25,
                        c => GenAdj.OccupiedRect(c, Rot4.North, bedDef.size).InBounds(map)
                             && GenAdj.OccupiedRect(c, Rot4.North, bedDef.size).Cells.All(x => x.Standable(map) && x.GetEdifice(map) == null),
                        out var cell))
                    Assert.Fail("no free cell for a hospital bed");
                GenSpawn.Spawn(bed, cell, map, Rot4.North);
                ctx.Track(bed);
                var comp = bed.GetComp<CompHospitalBed>();
                Assert.NotNull(comp, "hospital bed has no CompHospitalBed");
                comp.Hospital = true;
            }
            Assert.False(hospital.IsFull(), "precondition: no free hospital bed");

            var patientsBefore = new HashSet<Pawn>(hospital.Patients.Keys);
            var lettersBefore = new HashSet<Letter>(Find.LetterStack.LettersListForReading);
            var lordsBefore = new HashSet<Verse.AI.Group.Lord>(map.lordManager.lords);
            ctx.OnCleanup(() => CleanUpArrivals(map, hospital, patientsBefore, lettersBefore, lordsBefore));

            Assert.True(IncidentHelper.CanSpawnPatient(map),
                "CanSpawnPatient is false even with the hospital open and a free bed (no reachable edge cell?)");

            var parms = new IncidentParms { target = map, forced = true };
            bool fired = worker.TryExecuteWorker(parms);
            Assert.True(fired,
                "TryExecuteWorker returned false (no allied/neutral humanlike faction, hostile pawn on the map, or no landing cell?)");

            var added = hospital.Patients.Where(p => !patientsBefore.Contains(p.Key)).ToList();
            Assert.True(added.Count >= 1, "no patient was added to hospital.Patients");
            foreach (var pair in added)
            {
                Pawn pawn = pair.Key;
                PatientData data = pair.Value;
                Assert.NotNull(data, "patient has null PatientData");
                Assert.False(data.Dismissed, "new patient is marked dismissed");
                Assert.Equal(Find.TickManager.TicksGame, data.ArrivedAtTick, "ArrivedAtTick");
                Assert.True(data.InitialMarketValue > 0f, "InitialMarketValue not set: " + data.InitialMarketValue);
                Assert.True(data.InitialMood >= 0f && data.InitialMood <= 1f, "InitialMood out of range: " + data.InitialMood);
                Assert.True(data.Type != PatientType.Test, "PatientType left at Test");
                Assert.False(string.IsNullOrEmpty(data.Diagnosis), "Diagnosis empty (type " + data.Type + ")");
                Assert.False(string.IsNullOrEmpty(data.Cure), "Cure empty (type " + data.Type + ")");
                Assert.True(data.Bill >= 0f, "negative Bill");
                if (data.HasPendingSurgeryBill)
                {
                    Assert.NotNull(data.CureRecipe, "pending surgery bill but no CureRecipe");
                    Assert.True(pawn.health.surgeryBills.Bills.Any(b => b.recipe == data.CureRecipe), "pending surgery flag set but pawn has no such bill");
                }
                Assert.True(pawn.guest != null && pawn.guest.GuestStatus == GuestStatus.Guest, "patient is not a guest");
                Assert.False(pawn.Dead, "patient is dead");
                // arrives in a drop pod: not spawned yet, held by a skyfaller
                bool inPod = map.listerThings.AllThings.OfType<Skyfaller>().Any(sf => sf.innerContainer.Contains(pawn));
                Assert.True(inPod || pawn.Spawned, "patient is neither in a drop pod nor spawned");
                Assert.True(map.lordManager.lords.Any(l => !lordsBefore.Contains(l) && l.ownedPawns.Contains(pawn)), "patient has no new lord");
            }
        }

        private static void CleanUpArrivals(Map map, HospitalMapComponent hospital, HashSet<Pawn> patientsBefore, HashSet<Letter> lettersBefore, HashSet<Verse.AI.Group.Lord> lordsBefore)
        {
            var pawns = hospital.Patients.Keys.Where(p => !patientsBefore.Contains(p)).ToList();
            // Unregister first: removing the lord runs LordToil_Patient.Cleanup -> PatientLeaves, which would pay silver etc. for registered patients.
            foreach (var p in pawns) hospital.Patients.Remove(p);
            foreach (var lord in map.lordManager.lords.Where(l => !lordsBefore.Contains(l)).ToList())
                map.lordManager.RemoveLord(lord);
            foreach (var pod in map.listerThings.AllThings.OfType<Skyfaller>().Where(sf => pawns.Any(p => sf.innerContainer.Contains(p))).ToList())
            {
                foreach (var p in pawns) pod.innerContainer.Remove(p);
                if (!pod.Destroyed) pod.Destroy(DestroyMode.Vanish);
            }
            foreach (var p in pawns)
            {
                if (p.Spawned) p.DeSpawn();
                if (!p.Destroyed) p.Destroy(DestroyMode.Vanish);
                if (!p.Discarded) p.Discard(true);
            }
            foreach (var letter in Find.LetterStack.LettersListForReading.Where(l => !lettersBefore.Contains(l)).ToList())
                Find.LetterStack.RemoveLetter(letter);
        }
    }
}
