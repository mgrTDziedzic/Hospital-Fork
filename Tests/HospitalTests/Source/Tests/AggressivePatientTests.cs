using System.Collections.Generic;
using System.Linq;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Aggressive patients: CureScaria must not leak onto patients, and with the AcceptAggressive challenge the
    // patient arrives sedated (downed) so Hediff_Scaria stays quiet until the sedation wears off.
    // Map settings and the blacklist are restored in cleanup. Needs a non-hostile humanlike faction in the test save
    // and a colonist who meets CureScaria's skill requirement plus its ingredients on the map (see messages).
    public static class AggressivePatientTests
    {
        private static RecipeDef CureScaria()
        {
            var r = DefDatabase<RecipeDef>.GetNamedSilentFail("CureScaria");
            Assert.NotNull(r, "precondition: CureScaria recipe not found");
            return r;
        }

        // Surgery on, challenge as given, and only CureScaria left unrefused. Everything restored on cleanup.
        private static HospitalMapComponent Setup(TestContext ctx, bool aggressive, RecipeDef allowed)
        {
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            bool surgery = hospital.AcceptSurgery, agg = hospital.AcceptAggressive;
            var refused = hospital.refusedOperations;
            ctx.OnCleanup(() =>
            {
                hospital.AcceptSurgery = surgery;
                hospital.AcceptAggressive = agg;
                hospital.refusedOperations = refused;
            });
            hospital.AcceptSurgery = true;
            hospital.AcceptAggressive = aggressive;
            hospital.refusedOperations = ThingDefOf.Human.AllRecipes.Where(r => r != allowed).Distinct().ToList();
            return hospital;
        }

        private static Pawn Patient(TestContext ctx)
        {
            var pawn = VanillaCompatTests.Visitor(ctx, false);
            ctx.OnCleanup(() =>
            {
                try { if (!pawn.Destroyed && pawn.InMentalState) pawn.MentalState.RecoverFromState(); } catch { }
            });
            return pawn;
        }

        // Given: a surgery picker built with and without the challenge. Then: CureScaria is only listed with it.
        [HospitalTest("Aggr1: CureScaria is in CandidateSurgeries / IsBlacklistableSurgery only when the challenge is allowed")]
        public static void PickerFollowsToggle(TestContext ctx)
        {
            var scaria = CureScaria();
            Assert.False(SurgeryUtility.CandidateSurgeries().Contains(scaria), "listed with challenge off");
            Assert.False(SurgeryUtility.CandidateSurgeries(false).Contains(scaria), "listed with challenge off (explicit)");
            Assert.True(SurgeryUtility.CandidateSurgeries(true).Contains(scaria), "missing with challenge on");
            Assert.False(SurgeryUtility.IsBlacklistableSurgery(scaria), "blacklistable with challenge off");
            Assert.True(SurgeryUtility.IsBlacklistableSurgery(scaria, true), "not blacklistable with challenge on");
            Assert.True(SurgeryUtility.IsAggressionSurgery(scaria), "CureScaria is not an aggression surgery");
        }

        // Given: challenge OFF, every recipe except CureScaria refused. When: AddRandomSurgeryBill runs repeatedly.
        // Then: it always falls back to wounds (true) and the patient never gets Scaria or sedation.
        [HospitalTest("Aggr2: with the challenge off, AddRandomSurgeryBill never gives a patient Scaria")]
        public static void NeverScariaWhenOff(TestContext ctx)
        {
            var hospital = Setup(ctx, false, CureScaria());
            for (int i = 0; i < 5; i++)
            {
                var pawn = Patient(ctx);
                var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);
                bool fallback = SurgeryUtility.AddRandomSurgeryBill(pawn, data, hospital);
                Assert.True(fallback, "expected the wounds fallback (true), attempt " + i);
                Assert.False(pawn.health.hediffSet.HasHediff(HediffDefOf.Scaria), "patient got Scaria, attempt " + i);
                Assert.False(pawn.health.hediffSet.HasHediff(HospitalDefOf.Hospital_TransportSedation), "patient got sedation");
                Assert.False(data.Sedated, "Sedated flag set");
            }
        }

        // Given: challenge ON (surgery on), only CureScaria allowed. When: AddRandomSurgeryBill runs.
        // Then: bill added, patient has Scaria + sedation, is downed and calm after a Scaria tick, the sedation lasts
        // SedationHours*2500 ticks and PatientData.Sedated is set.
        [HospitalTest("Aggr3: with the challenge on, a CureScaria patient is sedated, downed and not berserk")]
        public static void SedatedWhenOn(TestContext ctx)
        {
            var hospital = Setup(ctx, true, CureScaria());
            var pawn = Patient(ctx);
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);
            bool fallback = SurgeryUtility.AddRandomSurgeryBill(pawn, data, hospital);
            Assert.False(fallback, "fell back to wounds (is a colonist with the skill and the ingredients available?)");

            var scaria = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Scaria);
            var sedation = pawn.health.hediffSet.GetFirstHediffOfDef(HospitalDefOf.Hospital_TransportSedation);
            Assert.NotNull(scaria, "no Scaria");
            Assert.NotNull(sedation, "no sedation");
            Assert.True(pawn.Downed, "patient is not downed");
            Assert.True(data.Sedated, "Sedated flag not set");
            Assert.True(data.HasPendingSurgeryBill, "no pending surgery bill");

            var disappears = sedation.TryGetComp<HediffComp_Disappears>();
            Assert.NotNull(disappears, "sedation has no Disappears comp");
            Assert.Equal((int)(HospitalMod.Settings.SedationHours * 2500f), disappears.ticksToDisappear, "ticksToDisappear");

            for (int i = 0; i < 5; i++) scaria.TickInterval(1);
            Assert.False(pawn.InMentalState, "patient is in a mental state while sedated");
        }

        // Given: the Aggr3 patient. When: the sedation is removed and Scaria ticks. Then: BerserkPermanent starts.
        [HospitalTest("Aggr4: when the sedation is gone the Scaria patient turns BerserkPermanent")]
        public static void WakesUpBerserk(TestContext ctx)
        {
            var hospital = Setup(ctx, true, CureScaria());
            var pawn = Patient(ctx);
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery);
            Assert.False(SurgeryUtility.AddRandomSurgeryBill(pawn, data, hospital), "fell back to wounds");
            var scaria = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Scaria);
            Assert.NotNull(scaria, "no Scaria");

            pawn.health.RemoveHediff(pawn.health.hediffSet.GetFirstHediffOfDef(HospitalDefOf.Hospital_TransportSedation));
            Assert.False(pawn.Downed, "patient still downed after the sedation was removed");
            for (int i = 0; i < 5; i++) scaria.TickInterval(1);
            Assert.True(pawn.InMentalState && pawn.MentalStateDef == MentalStateDefOf.BerserkPermanent,
                "patient is not BerserkPermanent, state: " + (pawn.MentalStateDef?.defName ?? "none"));
        }

        // Given: vanilla and modded hediff defs. Then: Scaria is an aggression hediff, ordinary diseases are not.
        [HospitalTest("Aggr5: IsAggressionHediff is true for Scaria, false for Flu and Plague")]
        public static void AggressionHediffCheck(TestContext ctx)
        {
            Assert.True(DiseaseUtility.IsAggressionHediff(HediffDefOf.Scaria), "Scaria");
            Assert.False(DiseaseUtility.IsAggressionHediff(DefDatabase<HediffDef>.GetNamed("Flu")), "Flu");
            Assert.False(DiseaseUtility.IsAggressionHediff(DefDatabase<HediffDef>.GetNamed("Plague")), "Plague");
            Assert.False(DiseaseUtility.IsAggressionHediff(null), "null");
            Assert.False(DiseaseUtility.CandidateDiseases().Contains(HediffDefOf.Scaria), "Scaria is a disease candidate");
        }

        // Given: a registered sedated patient dies (Pawn.Kill). Then: PatientDied applies the normal death penalty once
        // (goodwill drops by exactly DeathGoodwillPenalty) and vanilla adds nothing on top.
        [HospitalTest("Aggr6: a sedated aggressive patient dying costs exactly the normal death goodwill penalty")]
        public static void DeathPenaltyOnce(TestContext ctx)
        {
            VanillaCompatTests.ProtectGoodwill(ctx);
            var hospital = Setup(ctx, true, CureScaria());
            var pawn = Patient(ctx);
            var data = VanillaCompatTests.Register(ctx, pawn);
            Assert.False(SurgeryUtility.AddRandomSurgeryBill(pawn, data, hospital), "fell back to wounds");

            int penalty = HospitalMod.Settings.DeathGoodwillPenalty;
            var faction = pawn.Faction;
            int before = faction.GoodwillWith(Faction.OfPlayer);
            Assert.True(before - penalty > -100, "precondition: goodwill too low to measure (" + before + ")");

            pawn.Kill(null);
            Assert.Equal(before - penalty, faction.GoodwillWith(Faction.OfPlayer), "goodwill after death");
        }
    }
}
