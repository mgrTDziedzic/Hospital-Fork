using Hospital;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // "Penalize deaths not caused by me" (Settings.PenalizeNonPlayerDeaths, default off).
    // Needs a hostile and a non-hostile humanlike faction in the test save (like VanillaCompatTests).
    public static class DeathPenaltyTests
    {
        // Kills a registered patient with the given instigator (null = no DamageInfo) and returns (goodwill before, after, penalty).
        private static (int before, int after, int penalty, Pawn patient) KillPatient(TestContext ctx, bool penalizeAll, Thing instigator, bool noDinfo)
        {
            VanillaCompatTests.ProtectGoodwill(ctx);
            var settings = HospitalMod.Settings;
            bool oldFlag = settings.PenalizeNonPlayerDeaths;
            settings.PenalizeNonPlayerDeaths = penalizeAll;
            ctx.OnCleanup(() => settings.PenalizeNonPlayerDeaths = oldFlag);

            var patient = VanillaCompatTests.Visitor(ctx, false);
            VanillaCompatTests.Register(ctx, patient);
            int penalty = settings.DeathGoodwillPenalty;
            var faction = patient.Faction;
            int before = faction.GoodwillWith(Faction.OfPlayer);
            Assert.True(penalty > 0, "precondition: DeathGoodwillPenalty is 0, nothing to measure");
            Assert.True(before - penalty > -100, "precondition: goodwill too low to measure (" + before + ")");

            if (noDinfo) patient.Kill(null);
            else patient.Kill(new DamageInfo(DamageDefOf.Cut, 500f, 0f, -1f, instigator));
            ctx.OnCleanup(() => patient.Corpse?.Destroy());
            Assert.True(patient.Dead, "patient did not die");
            Assert.False(VanillaCompatTests.HospitalOf(ctx).Patients.ContainsKey(patient), "dead patient still on the roster");
            return (before, faction.GoodwillWith(Faction.OfPlayer), penalty, patient);
        }

        // Given: option off, a registered patient of a non-hostile faction.
        // When:  a hostile raider (not the player's faction) kills them.
        // Then:  goodwill is unchanged and the patient is removed from the roster.
        [HospitalTest("Death1: option off, a patient killed by a hostile pawn costs no goodwill")]
        public static void HostileKillNoPenalty(TestContext ctx)
        {
            var raider = VanillaCompatTests.Visitor(ctx, true);
            var r = KillPatient(ctx, false, raider, false);
            Assert.Equal(r.before, r.after, "goodwill after a hostile kill");
        }

        // Given: option off, a registered patient and a player colonist.
        // When:  the colonist kills the patient.
        // Then:  the death penalty is applied.
        [HospitalTest("Death2: option off, a patient killed by a player colonist costs the death penalty")]
        public static void ColonistKillPenalty(TestContext ctx)
        {
            var colonist = VanillaCompatTests.Colonist(ctx);
            var r = KillPatient(ctx, false, colonist, false);
            Assert.Equal(r.before - r.penalty, r.after, "goodwill after a colonist kill");
        }

        // Given: option off, a registered patient.
        // When:  they die with no DamageInfo (illness, blood loss, failed surgery: Kill(null)).
        // Then:  the death penalty is applied.
        [HospitalTest("Death3: option off, a medical death (no killer) costs the death penalty")]
        public static void MedicalDeathPenalty(TestContext ctx)
        {
            var r = KillPatient(ctx, false, null, true);
            Assert.Equal(r.before - r.penalty, r.after, "goodwill after a medical death");
        }

        // Given: option on, a registered patient.
        // When:  a hostile raider kills them.
        // Then:  the death penalty is applied (original behaviour).
        [HospitalTest("Death4: option on, a patient killed by a hostile pawn costs the death penalty")]
        public static void OptionOnHostileKillPenalty(TestContext ctx)
        {
            var raider = VanillaCompatTests.Visitor(ctx, true);
            var r = KillPatient(ctx, true, raider, false);
            Assert.Equal(r.before - r.penalty, r.after, "goodwill after a hostile kill with the option on");
        }

        // Given: DamageInfo with a player colonist, a hostile pawn, no instigator, and no DamageInfo at all.
        // Then:  PlayerResponsibleForDeath is true for null and the colonist, false for the hostile pawn and for no instigator.
        [HospitalTest("Death5: PlayerResponsibleForDeath is true for null dinfo and player instigators only")]
        public static void ResponsibleCheck(TestContext ctx)
        {
            var colonist = VanillaCompatTests.Colonist(ctx);
            var raider = VanillaCompatTests.Visitor(ctx, true);
            Assert.True(HospitalMapComponent.PlayerResponsibleForDeath(null), "null dinfo");
            Assert.True(HospitalMapComponent.PlayerResponsibleForDeath(new DamageInfo(DamageDefOf.Cut, 5f, 0f, -1f, colonist)), "player colonist");
            Assert.False(HospitalMapComponent.PlayerResponsibleForDeath(new DamageInfo(DamageDefOf.Cut, 5f, 0f, -1f, raider)), "hostile pawn");
            Assert.False(HospitalMapComponent.PlayerResponsibleForDeath(new DamageInfo(DamageDefOf.Cut, 5f, 0f, -1f, null)), "no instigator");
        }
    }
}
