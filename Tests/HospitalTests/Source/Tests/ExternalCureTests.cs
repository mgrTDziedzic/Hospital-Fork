using System.Linq;
using System.Reflection;
using Hospital;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // CheckExternallyCuredPatients: a cure that bypasses vanilla (MedPod removes hediffs straight from
    // hediffSet.hediffs) must be detected by hediff state alone. Production runs it every 250 ticks;
    // the tests call it directly.
    public static class ExternalCureTests
    {
        private static readonly MethodInfo CheckAll = typeof(HospitalMapComponent).GetMethod("CheckExternallyCuredPatients", BindingFlags.NonPublic | BindingFlags.Instance);

        private class Setup
        {
            public HospitalMapComponent Hospital;
            public Pawn Pawn;
            public PatientData Data;
            public Bill_Medical Bill;
            public Hediff Hediff;
        }

        // Spawns a patient registered in the hospital with a tracked cure. Adds either a hediff (hediffDef)
        // or a non-fresh missing part on the part, plus the matching surgery bill.
        private static Setup MakePatient(TestContext ctx, RecipeDef recipe, BodyPartDef partDef, HediffDef hediffDef, float trackedSeverity, bool partMissing)
        {
            Assert.NotNull(CheckAll, "HospitalMapComponent.CheckExternallyCuredPatients not found (renamed?)");
            Assert.NotNull(recipe, "recipe def missing");
            var hospital = ctx.Map.GetComponent<HospitalMapComponent>();
            Assert.NotNull(hospital, "map has no HospitalMapComponent");
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, true);
            Assert.True(pawn.Spawned, "test pawn not spawned");
            var part = pawn.RaceProps.body.GetPartsWithDef(partDef).FirstOrDefault();
            Assert.NotNull(part, "test pawn lacks body part " + partDef.defName);

            Hediff hediff;
            if (hediffDef != null) hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
            else
            {
                var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn);
                missing.Part = part;
                missing.IsFresh = false;
                hediff = missing;
            }
            pawn.health.AddHediff(hediff, part);

            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Surgery)
            {
                CureRecipe = recipe,
                HasPendingSurgeryBill = true,
                TrackedHediffSeverity = trackedSeverity,
                PartMissingAtArrival = partMissing
            };
            hospital.Patients.Add(pawn, data);
            ctx.OnCleanup(() => hospital.Patients.Remove(pawn));

            global::Hospital.Utilities.SurgeryUtility.CreatePatientBill(pawn, recipe, part);
            var bill = pawn.health.surgeryBills.Bills.OfType<Bill_Medical>().FirstOrDefault(b => b.recipe == recipe);
            Assert.NotNull(bill, "surgery bill was not created");
            return new Setup { Hospital = hospital, Pawn = pawn, Data = data, Bill = bill, Hediff = hediff };
        }

        // Carcinoma + "excise carcinoma" (removesHediff) on the torso.
        private static Setup MakeCarcinomaPatient(TestContext ctx, float trackedSeverity)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ExciseCarcinoma");
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("Carcinoma");
            Assert.NotNull(recipe, "RecipeDef ExciseCarcinoma not found");
            Assert.NotNull(hediffDef, "HediffDef Carcinoma not found");
            return MakePatient(ctx, recipe, BodyPartDefOf.Torso, hediffDef, trackedSeverity, false);
        }

        private static void RemoveHediffLikeMedPod(Setup s)
        {
            Assert.True(s.Pawn.health.hediffSet.hediffs.Remove(s.Hediff), "hediff was not on the pawn");
        }

        private static void Run(Setup s) { CheckAll.Invoke(s.Hospital, null); }

        private static bool BillStillThere(Setup s) { return s.Pawn.health.surgeryBills.Bills.Contains(s.Bill); }

        // Given: a tracked patient with Carcinoma (severity 0.3) and a pending excise-carcinoma bill.
        // When:  the hediff is removed straight from the hediff list (as MedPod does) and the check runs.
        // Then:  the patient is billed for the cure, the surgery bill is deleted and the tracking data is reset.
        [HospitalTest("MedPod: a removesHediff cure that vanishes abruptly is billed, the bill deleted and tracking reset")]
        public static void AbruptVanishIsBilled(TestContext ctx)
        {
            var s = MakeCarcinomaPatient(ctx, 0.3f);
            float billBefore = s.Data.Bill;
            RemoveHediffLikeMedPod(s);

            Run(s);

            Assert.True(s.Data.Bill > billBefore, "patient was not billed for the external cure (Bill " + s.Data.Bill + ")");
            Assert.False(BillStillThere(s), "surgery bill still on the patient");
            Assert.False(s.Data.HasPendingSurgeryBill, "HasPendingSurgeryBill not cleared");
            Assert.Equal(-1f, s.Data.TrackedHediffSeverity, "TrackedHediffSeverity not reset");
            Assert.False(s.Data.PartMissingAtArrival, "PartMissingAtArrival not reset");
        }

        // Given: a tracked patient whose Carcinoma had already faded to a tiny severity (0.05).
        // When:  the hediff is removed and the check runs.
        // Then:  nobody is charged (it healed on its own), but the bill and pending flag are cleared.
        [HospitalTest("MedPod: a hediff that faded naturally is not billed, but its bill and pending flag are cleared")]
        public static void FadedHediffNotBilled(TestContext ctx)
        {
            var s = MakeCarcinomaPatient(ctx, 0.05f);
            float billBefore = s.Data.Bill;
            RemoveHediffLikeMedPod(s);

            Run(s);

            Assert.Equal(billBefore, s.Data.Bill, "faded condition must not be charged");
            Assert.False(BillStillThere(s), "surgery bill still on the patient");
            Assert.False(s.Data.HasPendingSurgeryBill, "HasPendingSurgeryBill not cleared");
            Assert.Equal(-1f, s.Data.TrackedHediffSeverity, "TrackedHediffSeverity not reset");
        }

        // Given: a patient who arrived with a missing leg and a pending peg-leg bill.
        // When:  (control) the check runs while the leg is still missing.
        // Then:  nothing happens: bill stays, no charge.
        // When:  only the leg's own missing-part hediff is removed (partial regrowth: foot/toes still missing) and the check runs.
        // Then:  nothing happens: bill stays, no charge.
        // When:  all remaining missing-part hediffs on the leg and its descendants are removed and the check runs.
        // Then:  the patient is billed, the bill deleted and the arrival flag reset.
        [HospitalTest("MedPod: a regrown missing part counts as cured and is billed only once the whole limb is back (nothing happens while still missing)")]
        public static void RegrownPartIsBilled(TestContext ctx)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("InstallPegLeg");
            Assert.NotNull(recipe, "RecipeDef InstallPegLeg not found");
            var s = MakePatient(ctx, recipe, BodyPartDefOf.Leg, null, -1f, true);
            var hospital = s.Hospital; var pawn = s.Pawn; var data = s.Data; var bill = s.Bill; var missing = s.Hediff;

            // control: still missing -> nothing happens
            CheckAll.Invoke(hospital, null);
            Assert.True(pawn.health.surgeryBills.Bills.Contains(bill) && data.HasPendingSurgeryBill, "bill resolved while the part was still missing");
            Assert.Equal(0f, data.Bill, "charged while the part was still missing");

            // partial regrowth: only the leg hediff gone, descendants (foot, toes) still missing -> not cured yet
            var legPart = missing.Part;
            pawn.health.hediffSet.hediffs.Remove(missing);
            CheckAll.Invoke(hospital, null);
            Assert.True(pawn.health.surgeryBills.Bills.Contains(bill) && data.HasPendingSurgeryBill, "bill resolved while descendants of the part were still missing");
            Assert.Equal(0f, data.Bill, "charged while descendants of the part were still missing");

            // whole limb regrown: remove the remaining missing-part hediffs on the leg subtree
            pawn.health.hediffSet.hediffs.RemoveAll(h =>
            {
                if (!(h is Hediff_MissingPart)) return false;
                for (var p = h.Part; p != null; p = p.parent) if (p == legPart) return true;
                return false;
            });
            CheckAll.Invoke(hospital, null);

            Assert.True(data.Bill > 0f, "patient was not billed for the regrown part");
            Assert.False(pawn.health.surgeryBills.Bills.Contains(bill), "surgery bill still on the patient");
            Assert.False(data.HasPendingSurgeryBill, "HasPendingSurgeryBill not cleared");
            Assert.False(data.PartMissingAtArrival, "PartMissingAtArrival not reset");
        }

        // Part 1
        // Given: a tracked patient with Carcinoma still present and a pending bill.
        // When:  the bill disappears without going through BillStack.Delete and the check runs.
        // Then:  the pending flag is cleared and tracking reset, but nobody is charged.
        // Part 2
        // Given: the same setup, but the Carcinoma is removed too.
        // When:  the bill disappears and the check runs.
        // Then:  the flag is cleared and the patient is billed, since the cure is verifiable.
        [HospitalTest("MedPod: a bill that vanished silently clears the pending flag, and charges only if the cure is verifiable")]
        public static void VanishedBillClearsFlag(TestContext ctx)
        {
            // condition still present: flag cleared, no charge
            var s = MakeCarcinomaPatient(ctx, 0.3f);
            s.Pawn.health.surgeryBills.Bills.Remove(s.Bill); // bypasses BillStack.Delete (and our prefix)
            Assert.True(s.Data.HasPendingSurgeryBill, "precondition: flag should still be set");
            Run(s);
            Assert.False(s.Data.HasPendingSurgeryBill, "flag not cleared after the bill vanished");
            Assert.Equal(0f, s.Data.Bill, "charged although the condition is still there");
            Assert.Equal(-1f, s.Data.TrackedHediffSeverity, "TrackedHediffSeverity not reset");

            // condition gone as well: flag cleared and charged
            var s2 = MakeCarcinomaPatient(ctx, 0.3f);
            s2.Pawn.health.surgeryBills.Bills.Remove(s2.Bill);
            RemoveHediffLikeMedPod(s2);
            Run(s2);
            Assert.False(s2.Data.HasPendingSurgeryBill, "flag not cleared (cured + bill vanished)");
            Assert.True(s2.Data.Bill > 0f, "not billed although the condition is verifiably cured");
        }

        // Given: an old-save patient with no tracked severity (-1) and a pending bill.
        // When:  the hediff is removed and the check runs.
        // Then:  the patient is left untouched: bill stays, flag stays, no charge.
        [HospitalTest("MedPod: an old-save patient without tracking data is left untouched")]
        public static void UntrackedPatientUntouched(TestContext ctx)
        {
            var s = MakeCarcinomaPatient(ctx, -1f);
            RemoveHediffLikeMedPod(s);

            Run(s);

            Assert.True(BillStillThere(s), "bill of an untracked patient must stay");
            Assert.True(s.Data.HasPendingSurgeryBill, "HasPendingSurgeryBill must stay set");
            Assert.Equal(0f, s.Data.Bill, "untracked patient must not be charged");
            Assert.Equal(-1f, s.Data.TrackedHediffSeverity, "TrackedHediffSeverity changed");
            Assert.False(s.Data.PartMissingAtArrival, "PartMissingAtArrival changed");
        }
    }
}
