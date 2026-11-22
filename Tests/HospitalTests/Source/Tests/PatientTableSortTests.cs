using Hospital;
using Hospital.MainTab;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Sortable Hospital tab columns: numeric Compare overrides (not the formatted text) on fresh worker instances.
    public static class PatientTableSortTests
    {
        // Given: two registered patients with bills 9 and 100 (text order "9" > "100", numeric order 9 < 100).
        // When:  PawnColumnWorker_Bill.Compare is called on a fresh worker, both ways, and against a non-patient.
        // Then:  the larger bill sorts after the smaller one (numeric, not text), the reverse is the opposite sign, and a non-patient sorts before both.
        [HospitalTest("Sort1: the bill column compares by amount, with a fresh worker instance, non-patients first")]
        public static void BillCompare(TestContext ctx)
        {
            var small = VanillaCompatTests.Visitor(ctx, hostile: false);
            var big = VanillaCompatTests.Visitor(ctx, hostile: false);
            var outsider = VanillaCompatTests.Colonist(ctx);
            VanillaCompatTests.Register(ctx, small, bill: 9f);
            VanillaCompatTests.Register(ctx, big, bill: 100f);
            var worker = new PawnColumnWorker_Bill();
            Assert.True(worker.Compare(small, big) < 0, "9 should sort before 100");
            Assert.True(worker.Compare(big, small) > 0, "100 should sort after 9");
            Assert.True(worker.Compare(outsider, small) < 0, "non-patient should sort before a patient");
        }

        // Given: two registered patients, one arrived 10 hours earlier than the other.
        // When:  PawnColumnWorker_PatientTimeSpent.Compare is called on a fresh worker.
        // Then:  the patient who spent more ticks in the hospital sorts after the other one.
        [HospitalTest("Sort2: the time spent column compares by ticks, with a fresh worker instance")]
        public static void TimeSpentCompare(TestContext ctx)
        {
            var older = VanillaCompatTests.Visitor(ctx, hostile: false);
            var newer = VanillaCompatTests.Visitor(ctx, hostile: false);
            var olderData = VanillaCompatTests.Register(ctx, older);
            VanillaCompatTests.Register(ctx, newer);
            olderData.ArrivedAtTick = Find.TickManager.TicksGame - 25000;
            var worker = new PawnColumnWorker_PatientTimeSpent();
            Assert.True(worker.Compare(older, newer) > 0, "longer stay should sort after the shorter one");
            Assert.True(worker.Compare(newer, older) < 0, "shorter stay should sort before");
        }

        // Given: two registered patients whose InitialMood differs (-1 and 1), so the first has the higher rating score.
        // When:  PawnColumnWorker_PatientRating.Compare is called on a fresh worker; the text columns are asked for text without any drawing.
        // Then:  the higher score sorts after the lower one; Diagnosis and Cure GetTextFor and Compare do not throw on fresh instances.
        [HospitalTest("Sort3: the rating column compares by score; text columns work on fresh worker instances")]
        public static void RatingCompareAndFreshWorkers(TestContext ctx)
        {
            var high = VanillaCompatTests.Visitor(ctx, hostile: false);
            var low = VanillaCompatTests.Visitor(ctx, hostile: false);
            var highData = VanillaCompatTests.Register(ctx, high);
            var lowData = VanillaCompatTests.Register(ctx, low);
            highData.InitialMood = -1f;
            lowData.InitialMood = 1f;
            highData.Diagnosis = "A"; lowData.Diagnosis = "B";
            highData.Cure = "A"; lowData.Cure = "B";
            var rating = new PawnColumnWorker_PatientRating();
            Assert.True(rating.Compare(high, low) > 0, "higher rating should sort after the lower one");
            Assert.True(rating.Compare(low, high) < 0, "lower rating should sort before");
            var diagnosis = new PawnColumnWorker_Diagnosis();
            var cure = new PawnColumnWorker_Cure();
            Assert.Equal("A", diagnosis.GetTextFor(high), "diagnosis text");
            Assert.Equal("B", cure.GetTextFor(low), "cure text");
            Assert.True(diagnosis.Compare(high, low) < 0, "diagnosis A should sort before B");
            Assert.True(cure.Compare(low, high) > 0, "cure B should sort after A");
        }
    }
}
