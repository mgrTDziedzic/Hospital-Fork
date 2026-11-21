using System.Linq;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    public static class ArrivalEffectTests
    {
        private static int FilthNear(Map map, IntVec3 cell)
        {
            return GenRadial.RadialCellsAround(cell, 3f, true).Where(c => c.InBounds(map)).Sum(c => c.GetThingList(map).Count(t => t is Filth));
        }

        // Given: a spawned pawn on a cell with no filth around.
        // When:  arrival wounds are applied through PatientUtility.DamagePawn (the shared path of arrivals, mass casualty events and the sampler).
        // Then:  the pawn is wounded, but no blood filth appeared near the cell, the pawn has no wounded effecter and the suppress flag is off again.
        //        Control: outside that scope the same pawn drops blood filth as in vanilla.
        [HospitalTest("Arrival: wounds applied to the landing pawn leave no blood filth or wounded effecter, and the suppress flag is reset")]
        public static void ArrivalWoundsLeaveNoBlood(TestContext ctx)
        {
            var map = ctx.Map;
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
            foreach (var c in GenRadial.RadialCellsAround(cell, 3f, true).Where(x => x.InBounds(map)))
                foreach (var f in c.GetThingList(map).OfType<Filth>().ToList()) f.Destroy(DestroyMode.Vanish);

            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            GenSpawn.Spawn(pawn, cell, map);
            var data = new PatientData(Find.TickManager.TicksGame, pawn.MarketValue, 0.5f, PatientType.Wounds);
            Assert.False(PatientUtility.SuppressDamageEffects, "precondition: flag already on");

            // several rolls so a lucky 50% blood roll cannot hide the bug
            for (int i = 0; i < 5; i++)
                PatientUtility.DamagePawn(pawn, data, hospital, new PatientSpec { Type = PatientType.Wounds, Wounds = WoundKind.Gunshot });

            Assert.True(pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(), "pawn has no injuries");
            Assert.Equal(0, FilthNear(map, cell), "blood filth near the landing cell");
            Assert.True(pawn.health.woundedEffecter == null, "wounded effecter left on the pawn");
            Assert.False(PatientUtility.SuppressDamageEffects, "suppress flag still on after DamagePawn");
            Assert.Equal(0, PatientUtility.SuppressDamageEffectsDepth, "suppress depth");

            pawn.health.DropBloodFilth();
            Assert.True(FilthNear(map, cell) > 0, "control: blood filth is not dropped outside the arrival scope");
        }
    }
}
