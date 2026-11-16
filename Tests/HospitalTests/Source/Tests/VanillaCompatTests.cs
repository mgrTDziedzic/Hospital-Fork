using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hospital;
using Hospital.Patches;
using Hospital.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HospitalTests
{
    // Hospital must leave vanilla behaviour untouched for pawns that are NOT hospital patients.
    // Each test runs a patched vanilla path for a non-patient (colonist / hostile raider / neutral visitor)
    // and asserts the vanilla outcome plus "nothing Hospital-related changed" (see Snap). Where cheap,
    // a positive control runs the same path for a registered patient and asserts Hospital's behaviour,
    // which proves the test really goes through the patch.
    // Expectations come from the 1.6 decompiled vanilla code; vanilla private constants are copied where needed.
    public static class VanillaCompatTests
    {
        private const string HarmonyId = "Adamas.Hospital";

        // ---------- helpers ----------

        internal static Faction FindFaction(bool hostile)
        {
            var player = Faction.OfPlayer;
            var f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x =>
                x != player && !x.IsPlayer && !x.def.hidden && x.def.humanlikeFaction && !x.defeated
                && (hostile ? x.HostileTo(player) : !x.HostileTo(player)));
            Assert.NotNull(f, "precondition: no " + (hostile ? "hostile" : "non-hostile") + " humanlike faction in the test save");
            return f;
        }

        internal static Pawn Visitor(TestContext ctx, bool hostile)
        {
            return HospitalRegressionTests.MakePawn(ctx, FindFaction(hostile), true);
        }

        internal static Pawn Colonist(TestContext ctx)
        {
            return HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, true);
        }

        internal static HospitalMapComponent HospitalOf(TestContext ctx)
        {
            var h = ctx.Map.GetComponent<HospitalMapComponent>();
            Assert.NotNull(h, "map has no HospitalMapComponent");
            return h;
        }

        // Registers a spawned pawn as an active patient (guest of the player), unregistered again on cleanup.
        // withLord: real patients always own a LordJob_VisitColonyAsPatient lord (IncidentWorker_PatientArrives). Without it,
        // other mods (e.g. Hospitality's CheckForStateChange postfix) see a lordless recovered guest and react (letters, lords).
        // Pass false when the test makes its own lord. Only non-player-faction pawns get a lord.
        internal static PatientData Register(TestContext ctx, Pawn p, float bill = 0f, bool withLord = true)
        {
            var hospital = HospitalOf(ctx);
            Assert.True(p.Spawned, "patient must be spawned (IsPatient needs pawn.Map)");
            var map = p.Map;
            var faction = p.Faction;
            if (withLord && p.GetLord() == null && faction != null && faction != Faction.OfPlayer)
            {
                var lord = LordMaker.MakeNewLord(faction, new LordJob_VisitColonyAsPatient(faction), map, new List<Pawn> { p });
                // Cleanups run in reverse: added first, so the lord is removed AFTER the unregistration below
                // (removing it for a registered patient runs PatientLeaves: silver, goodwill).
                ctx.OnCleanup(() => { if (map.lordManager.lords.Contains(lord)) map.lordManager.RemoveLord(lord); });
            }
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Wounds) { Bill = bill };
            hospital.Patients.Add(p, data);
            ctx.OnCleanup(() => hospital.Patients.Remove(p));
            p.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Guest);
            Assert.True(p.IsPatient(out _), "precondition: registered pawn is not seen as a patient");
            return data;
        }

        // Faction goodwill can change as a side effect of a control run; put it back afterwards.
        internal static void ProtectGoodwill(TestContext ctx)
        {
            var saved = new List<(FactionRelation rel, int goodwill, FactionRelationKind kind)>();
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f.IsPlayer) continue;
                var rel = f.RelationWith(Faction.OfPlayer, true);
                if (rel != null) saved.Add((rel, rel.baseGoodwill, rel.kind));
            }
            ctx.OnCleanup(() => { foreach (var s in saved) { s.rel.baseGoodwill = s.goodwill; s.rel.kind = s.kind; } });
        }

        private class State
        {
            public Dictionary<Pawn, float> Bills;
            public int Silver;
            public Dictionary<Faction, int> Goodwill;
            public int Letters;
        }

        // Everything Hospital could touch: roster + bills, silver lying on the map, goodwill, letters.
        private static State Snap(TestContext ctx)
        {
            var hospital = HospitalOf(ctx);
            return new State
            {
                Bills = hospital.Patients.ToDictionary(kv => kv.Key, kv => kv.Value.Bill),
                Silver = ctx.Map.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t => t.stackCount),
                Goodwill = Find.FactionManager.AllFactionsListForReading.Where(f => !f.IsPlayer).ToDictionary(f => f, f => f.PlayerGoodwill),
                Letters = Find.LetterStack.LettersListForReading.Count
            };
        }

        private static void AssertSame(State before, State after, string what)
        {
            Assert.Equal(before.Bills.Count, after.Bills.Count, what + ": hospital.Patients count");
            foreach (var kv in before.Bills)
            {
                Assert.True(after.Bills.TryGetValue(kv.Key, out var bill), what + ": patient left the roster");
                Assert.Equal(kv.Value, bill, what + ": patient bill");
            }
            Assert.Equal(before.Silver, after.Silver, what + ": silver on the map");
            foreach (var kv in before.Goodwill) Assert.Equal(kv.Value, after.Goodwill[kv.Key], what + ": goodwill with " + kv.Key.Name);
            Assert.Equal(before.Letters, after.Letters, what + ": letters");
        }

        // Gives the pawn exactly one fresh, tendable cut. Generated pawns may carry random old injuries / scars
        // (permanent injuries are never "tended"), so those are removed first to keep the tests deterministic.
        private static Hediff AddCut(Pawn pawn)
        {
            foreach (var old in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) pawn.health.RemoveHediff(old);
            var part = pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Arm).First();
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
            cut.Severity = 4f;
            pawn.health.AddHediff(cut, part);
            return cut;
        }

        // forceDowned alone does not down a pawn (it only matters inside the private MakeDowned / state update),
        // so call the vanilla MakeDowned itself, like vanilla does for lethal damage.
        internal static void Down(Pawn pawn)
        {
            var make = typeof(Pawn_HealthTracker).GetMethod("MakeDowned", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(make, "Pawn_HealthTracker.MakeDowned not found");
            pawn.health.forceDowned = true;
            make.Invoke(pawn.health, new object[] { null, null });
            Assert.True(pawn.Downed, "precondition: pawn is not downed after MakeDowned");
        }

        private static Building_Bed MakeMedicalBed(TestContext ctx)
        {
            var map = ctx.Map;
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
            Assert.True(bed.Medical, "precondition: HospitalBed is not medical");
            return bed;
        }

        private static bool IsPatched(MethodBase m)
        {
            if (m == null) return false;
            var info = Harmony.GetPatchInfo(m);
            return info != null && info.Prefixes.Concat(info.Postfixes).Any(p => p.owner == HarmonyId);
        }

        // ---------- all patches are applied ----------

        // Given: the game with Hospital loaded.
        // When:  the Harmony patch info of every vanilla target is read.
        // Then:  each of them carries a patch owned by Hospital (so the tests below exercise live patches).
        [HospitalTest("Vanilla: every Hospital Harmony patch is applied to its vanilla target method")]
        public static void AllPatchesApplied(TestContext ctx)
        {
            var targets = new List<(string name, MethodBase method)>
            {
                ("HealthCardUtility.DrawHealthSummary", AccessTools.Method(typeof(HealthCardUtility), "DrawHealthSummary")),
                ("RecipeWorker.IsViolationOnPawn", AccessTools.Method(typeof(RecipeWorker), "IsViolationOnPawn")),
                ("Recipe_AdministerIngestible.IsViolationOnPawn", AccessTools.DeclaredMethod(typeof(Recipe_AdministerIngestible), "IsViolationOnPawn")),
                ("Recipe_InstallArtificialBodyPart.IsViolationOnPawn", AccessTools.DeclaredMethod(typeof(Recipe_InstallArtificialBodyPart), "IsViolationOnPawn")),
                ("Building_Bed.DrawColorTwo", AccessTools.PropertyGetter(typeof(Building_Bed), "DrawColorTwo")),
                ("JobDriver_FoodFeedPatient.MakeNewToils", AccessTools.DeclaredMethod(typeof(JobDriver_FoodFeedPatient), "MakeNewToils")),
                ("TendUtility.DoTend", AccessTools.Method(typeof(TendUtility), "DoTend")),
                ("Bill.Notify_IterationCompleted", AccessTools.DeclaredMethod(typeof(Bill), "Notify_IterationCompleted")),
                ("BillStack.Delete", AccessTools.DeclaredMethod(typeof(BillStack), "Delete")),
                ("JobGiver_Work.PawnCanUseWorkGiver", AccessTools.DeclaredMethod(typeof(JobGiver_Work), "PawnCanUseWorkGiver")),
                ("Pawn.Kill", AccessTools.DeclaredMethod(typeof(Pawn), "Kill")),
                ("SurgeryOutcome_Failure.TryGainBotchedSurgeryThought", AccessTools.DeclaredMethod(typeof(SurgeryOutcome_Failure), "TryGainBotchedSurgeryThought")),
                ("Faction.Notify_MemberExitedMap", AccessTools.DeclaredMethod(typeof(Faction), "Notify_MemberExitedMap")),
                ("Pawn.SetFaction", AccessTools.DeclaredMethod(typeof(Pawn), "SetFaction")),
                ("RestUtility.FindBedFor", AccessTools.Method(typeof(RestUtility), "FindBedFor", new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(bool), typeof(GuestStatus?) })),
                ("KidnapAIUtility.ReachableWoundedGuest", AccessTools.Method(typeof(KidnapAIUtility), "ReachableWoundedGuest")),
                ("ThinkNode_ConditionalNPCCanSelfTendNow.Satisfied", AccessTools.DeclaredMethod(typeof(ThinkNode_ConditionalNPCCanSelfTendNow), "Satisfied")),
                ("JobDriver_TendPatient.TryMakePreToilReservations", AccessTools.DeclaredMethod(typeof(JobDriver_TendPatient), "TryMakePreToilReservations")),
            };
            var bad = targets.Where(t => !IsPatched(t.method)).Select(t => t.name).ToList();
            Assert.True(bad.Count == 0, "not patched by Hospital (or target not found): " + string.Join(", ", bad));
        }

        // ---------- Allow_Operations_Patch / PatchNoViolation ----------

        // Given: a visitor (other faction), a colonist (same faction as the bill doer) and a registered patient.
        // When:  the patched DrawHealthSummary prefix decides about allowOperations (the Rect drawing itself needs a GUI frame, so the prefix is called directly).
        // Then:  non-patients keep whatever flag they came with; only the patient gets allowOperations = true.
        [HospitalTest("Vanilla: DrawHealthSummary keeps allowOperations unchanged for non-patients; a patient gets it forced on")]
        public static void DrawHealthSummaryPrefix(TestContext ctx)
        {
            var visitor = Visitor(ctx, false);
            var colonist = Colonist(ctx);
            foreach (var p in new[] { visitor, colonist })
                foreach (bool start in new[] { false, true })
                {
                    bool flag = start;
                    AllowOperationsPatch.AllowOperationsOnPatients.Prefix(p, ref flag);
                    Assert.Equal(start, flag, "allowOperations changed for a non-patient (" + p.Faction + ")");
                }

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            bool f = false;
            AllowOperationsPatch.AllowOperationsOnPatients.Prefix(patient, ref f);
            Assert.True(f, "control: patient did not get allowOperations");
        }

        // Given: RecipeWorker with isViolation = true, a visitor of another faction, a colonist and a patient.
        // When:  IsViolationOnPawn is called with the player faction as bill doer.
        // Then:  vanilla (other faction -> recipe.isViolation, own faction -> false) for non-patients; false for the patient.
        [HospitalTest("Vanilla: RecipeWorker.IsViolationOnPawn is unchanged for non-patients; patients are never a violation")]
        public static void ViolationBaseRecipeWorker(TestContext ctx)
        {
            var violating = new RecipeWorker { recipe = new RecipeDef { isViolation = true } };
            var harmless = new RecipeWorker { recipe = new RecipeDef { isViolation = false } };
            var visitor = Visitor(ctx, false);
            var raider = Visitor(ctx, true);
            var colonist = Colonist(ctx);
            Assert.True(violating.IsViolationOnPawn(visitor, null, Faction.OfPlayer), "visitor: vanilla returns recipe.isViolation (true)");
            Assert.True(violating.IsViolationOnPawn(raider, null, Faction.OfPlayer), "raider: vanilla returns recipe.isViolation (true)");
            Assert.False(harmless.IsViolationOnPawn(visitor, null, Faction.OfPlayer), "visitor: vanilla returns recipe.isViolation (false)");
            Assert.False(violating.IsViolationOnPawn(colonist, null, Faction.OfPlayer), "colonist of the doer's faction is never a violation");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            Assert.False(violating.IsViolationOnPawn(patient, null, Faction.OfPlayer), "control: operation on a patient must not be a violation");
        }

        // Given: a real "administer non-medical drug" recipe (vanilla: violation on a pawn of another faction without dependency).
        // When:  IsViolationOnPawn is called for colonist, visitor and patient.
        // Then:  colonist false, visitor true (vanilla), patient false (Hospital).
        [HospitalTest("Vanilla: Recipe_AdministerIngestible.IsViolationOnPawn is unchanged for non-patients; patients are never a violation")]
        public static void ViolationAdministerIngestible(TestContext ctx)
        {
            var recipe = DefDatabase<RecipeDef>.AllDefs.FirstOrDefault(r => r.workerClass == typeof(Recipe_AdministerIngestible)
                && !r.ingredients.NullOrEmpty() && r.ingredients[0].filter.AllowedThingDefs.Any()
                && r.ingredients[0].filter.AllowedThingDefs.First().IsNonMedicalDrug);
            Assert.NotNull(recipe, "no non-medical-drug administer recipe found");
            var worker = (Recipe_AdministerIngestible)recipe.Worker;

            var visitor = Visitor(ctx, false);
            var colonist = Colonist(ctx);
            // vanilla returns false for pawns already dependent on the drug; keep the expectation simple
            foreach (var p in new[] { visitor, colonist })
                foreach (var h in p.health.hediffSet.hediffs.OfType<Hediff_Addiction>().ToList()) p.health.RemoveHediff(h);

            Assert.False(worker.IsViolationOnPawn(colonist, null, Faction.OfPlayer), "colonist: vanilla false (same faction)");
            Assert.True(worker.IsViolationOnPawn(visitor, null, Faction.OfPlayer), "visitor: vanilla true (non-medical drug on another faction)");

            var patient = Visitor(ctx, false);
            foreach (var h in patient.health.hediffSet.hediffs.OfType<Hediff_Addiction>().ToList()) patient.health.RemoveHediff(h);
            Register(ctx, patient);
            Assert.False(worker.IsViolationOnPawn(patient, null, Faction.OfPlayer), "control: patient must not be a violation");
        }

        // Given: an artificial-part recipe (not better-than-natural) and healthy legs.
        // When:  IsViolationOnPawn is called for colonist, visitor with a healthy part (Harvest intent), visitor with a bad hediff on the part (Amputate intent) and a patient.
        // Then:  false / true / false (vanilla) and false (Hospital) for the patient.
        [HospitalTest("Vanilla: Recipe_InstallArtificialBodyPart.IsViolationOnPawn is unchanged for non-patients; patients are never a violation")]
        public static void ViolationInstallArtificialBodyPart(TestContext ctx)
        {
            var recipe = DefDatabase<RecipeDef>.AllDefs.FirstOrDefault(r => r.workerClass == typeof(Recipe_InstallArtificialBodyPart)
                && r.addsHediff?.addedPartProps != null && !r.addsHediff.addedPartProps.betterThanNatural && !r.appliedOnFixedBodyParts.NullOrEmpty());
            Assert.NotNull(recipe, "no suitable install-artificial-part recipe found");
            var worker = (Recipe_InstallArtificialBodyPart)recipe.Worker;

            Pawn Make(bool colonist)
            {
                var p = colonist ? Colonist(ctx) : Visitor(ctx, false);
                return p;
            }
            BodyPartRecord PartOf(Pawn p) => p.RaceProps.body.GetPartsWithDef(recipe.appliedOnFixedBodyParts[0]).First();

            var colonistPawn = Make(true);
            Assert.False(worker.IsViolationOnPawn(colonistPawn, PartOf(colonistPawn), Faction.OfPlayer), "colonist: vanilla false (same faction)");

            var visitor = Make(false);
            Assert.True(worker.IsViolationOnPawn(visitor, PartOf(visitor), Faction.OfPlayer), "visitor, healthy part: vanilla true (Harvest intent)");

            var injured = Make(false);
            var part = PartOf(injured);
            var bad = HediffMaker.MakeHediff(HediffDefOf.Cut, injured, part);
            bad.Severity = 2f;
            injured.health.AddHediff(bad, part);
            Assert.False(worker.IsViolationOnPawn(injured, part, Faction.OfPlayer), "visitor, injured part: vanilla false (Amputate intent)");

            var patient = Make(false);
            Register(ctx, patient);
            Assert.False(worker.IsViolationOnPawn(patient, PartOf(patient), Faction.OfPlayer), "control: patient must not be a violation");
        }

        // ---------- BedColorPatch ----------

        // Vanilla private colours (Building_Bed), copied because they are not accessible.
        private static readonly Color VanillaNormal = new Color(161f / 255f, 213f / 255f, 180f / 255f);
        private static readonly Color VanillaMedical = new Color(99f / 255f, 159f / 255f, 226f / 255f);
        private static readonly Color VanillaMedicalForPrisoner = new Color(167f / 255f, 96f / 255f, 39f / 255f);

        // Given: a plain bed, a medical bed with Hospital=false, and a medical prisoner bed (Hospital=true) - none should be recoloured.
        // When:  DrawColorTwo is read.
        // Then:  the vanilla sheet colours. Control: a medical bed with Hospital=true shows Hospital's colour, Surgery another one.
        [HospitalTest("Vanilla: bed sheet colour is unchanged for plain, non-hospital medical and prisoner beds; hospital/surgery beds are recoloured")]
        public static void BedColourVanilla(TestContext ctx)
        {
            var (plain, plainComp) = HospitalRegressionTests.MakeBed(ctx);
            plainComp.Hospital = true;
            plainComp.Surgery = true; // non-medical bed: flags must not matter
            Assert.Equal(VanillaNormal, plain.DrawColorTwo, "plain bed colour");

            var med = MakeMedicalBed(ctx);
            var comp = med.GetComp<CompHospitalBed>();
            comp.Hospital = false;
            Assert.Equal(VanillaMedical, med.DrawColorTwo, "medical bed with Hospital=false");

            var prisoner = MakeMedicalBed(ctx);
            prisoner.ForOwnerType = BedOwnerType.Prisoner;
            Assert.True(prisoner.ForPrisoners, "precondition: bed not set to prisoners");
            Assert.True(prisoner.GetComp<CompHospitalBed>().Hospital, "precondition: default Hospital=true");
            Assert.Equal(VanillaMedicalForPrisoner, prisoner.DrawColorTwo, "prisoner medical bed (Hospital=true) colour");

            comp.Hospital = true;
            Assert.True(med.DrawColorTwo != VanillaMedical, "control: hospital bed keeps the vanilla colour");
            comp.Surgery = true;
            var surgeryColour = med.DrawColorTwo;
            comp.Surgery = false;
            Assert.True(surgeryColour != med.DrawColorTwo, "control: surgery bed has the same colour as a hospital bed");
        }

        // ---------- Billing_Feed / Billing_Tend / Billing_Surgery ----------

        // Given: a feeding job (feeder -> deliveree, meal) with the patched MakeNewToils run.
        // When:  the driver is cleaned up with JobCondition.Succeeded (runs the finish actions).
        // Then:  nobody is billed for a non-patient; a patient's bill grows by clamp(meal value * 1.1, 0, 70).
        [HospitalTest("Vanilla: feeding a non-patient adds no bill; feeding a patient bills meal value x1.1")]
        public static void FeedBilling(TestContext ctx)
        {
            var toils = typeof(JobDriver_FoodFeedPatient).GetMethod("MakeNewToils", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(toils, "JobDriver_FoodFeedPatient.MakeNewToils not found");
            var feeder = Colonist(ctx);

            float Feed(Pawn deliveree, Thing meal)
            {
                var driver = new JobDriver_FoodFeedPatient { pawn = feeder, job = JobMaker.MakeJob(JobDefOf.FeedPatient, meal, deliveree) };
                toils.Invoke(driver, null); // lazy iterator: only the postfix (AddFinishAction) runs
                driver.Cleanup(JobCondition.Succeeded);
                return meal.MarketValue;
            }

            var visitor = Visitor(ctx, false);
            var before = Snap(ctx);
            Feed(visitor, ctx.Track(ThingMaker.MakeThing(ThingDefOf.MealSimple)));
            AssertSame(before, Snap(ctx), "feeding a non-patient visitor");

            var patient = Visitor(ctx, false);
            var data = Register(ctx, patient);
            var meal = ctx.Track(ThingMaker.MakeThing(ThingDefOf.MealSimple));
            float expected = Mathf.Clamp(meal.MarketValue * 1.1f, 0, 70);
            Feed(patient, meal);
            Assert.True(Math.Abs(data.Bill - expected) < 0.01f, "control: patient bill expected " + expected + " but was " + data.Bill);
        }

        // Given: an injured visitor (non-patient), then an injured registered patient; a colonist doctor and industrial medicine.
        // When:  TendUtility.DoTend runs.
        // Then:  vanilla tending happens in both cases (hediff tended); only the patient is billed clamp(medicine value * 1.2, 0, 70).
        [HospitalTest("Vanilla: tending a non-patient works as in vanilla and bills nothing; tending a patient bills medicine value x1.2")]
        public static void TendBilling(TestContext ctx)
        {
            var doctor = Colonist(ctx);

            var visitor = Visitor(ctx, false);
            var visitorCut = AddCut(visitor);
            var medicine = ctx.Track(ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial));
            var before = Snap(ctx);
            TendUtility.DoTend(doctor, visitor, (Medicine)medicine);
            Assert.True(visitorCut.IsTended(), "vanilla tend did not happen for a non-patient");
            AssertSame(before, Snap(ctx), "tending a non-patient");

            var patient = Visitor(ctx, false);
            var patientCut = AddCut(patient);
            var data = Register(ctx, patient);
            var medicine2 = ctx.Track(ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial));
            float expected = Mathf.Clamp(medicine2.MarketValue * 1.2f, 0, 70);
            TendUtility.DoTend(doctor, patient, (Medicine)medicine2);
            Assert.True(patientCut.IsTended(), "control: patient was not tended");
            Assert.True(Math.Abs(data.Bill - expected) < 0.01f, "control: patient bill expected " + expected + " but was " + data.Bill);
        }

        // Creates a surgery bill on the pawn that is no longer completable (the carcinoma is removed again), so
        // Notify_IterationCompleted skips ApplyOnPawn (no random surgery outcome) but still runs the bill-stack deletion.
        private static Bill_Medical MakeDeadSurgeryBill(Pawn pawn)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ExciseCarcinoma");
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("Carcinoma");
            Assert.NotNull(recipe, "RecipeDef ExciseCarcinoma not found");
            Assert.NotNull(hediffDef, "HediffDef Carcinoma not found");
            var part = pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Torso).First();
            var hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
            pawn.health.AddHediff(hediff, part);
            HealthCardUtility.CreateSurgeryBill(pawn, recipe, part, new List<Thing>(), false);
            var bill = pawn.health.surgeryBills.Bills.OfType<Bill_Medical>().FirstOrDefault(b => b.recipe == recipe);
            Assert.NotNull(bill, "surgery bill was not created");
            pawn.health.hediffSet.hediffs.Remove(hediff);
            return bill;
        }

        // Given: a surgery bill on a colonist, on a visitor (non-patients) and a plain production bill.
        // When:  Bill.Notify_IterationCompleted runs (prefix captures the patient, postfix bills) and the bill is removed from the stack (BillStack.Delete prefix).
        // Then:  vanilla: the bill is gone from the stack; nothing in Hospital changed. Control: a patient's bill is charged, the pending flag cleared.
        [HospitalTest("Vanilla: completing/deleting a medical bill on a non-patient changes nothing in Hospital; on a patient it is charged and clears the pending flag")]
        public static void SurgeryBilling(TestContext ctx)
        {
            var doctor = Colonist(ctx);
            foreach (var p in new[] { Colonist(ctx), Visitor(ctx, false) })
            {
                var bill = MakeDeadSurgeryBill(p);
                var before = Snap(ctx);
                bill.Notify_IterationCompleted(doctor, new List<Thing>());
                Assert.False(p.health.surgeryBills.Bills.Contains(bill), "vanilla: completed bill must leave the stack");
                AssertSame(before, Snap(ctx), "completing a surgery bill on a non-patient");

                var bill2 = MakeDeadSurgeryBill(p);
                before = Snap(ctx);
                p.health.surgeryBills.Delete(bill2);
                Assert.False(p.health.surgeryBills.Bills.Contains(bill2), "vanilla: deleted bill must leave the stack");
                AssertSame(before, Snap(ctx), "deleting a surgery bill on a non-patient");
            }

            // non-medical bill: the prefix must capture nothing
            var anyRecipe = DefDatabase<RecipeDef>.AllDefs.First(r => r.workerClass == typeof(RecipeWorker) || r.products != null);
            Billing_Surgery_Patch.BillPatientsForMedicalBill.Prefix(new Bill_Production(anyRecipe), out Pawn state);
            Assert.True(state == null, "prefix captured a pawn for a non-medical bill");

            var patient = Visitor(ctx, false);
            var data = Register(ctx, patient);
            var patientBill = MakeDeadSurgeryBill(patient);
            data.HasPendingSurgeryBill = true;
            data.CureRecipe = patientBill.recipe; // only the cure bill settles the pending flag
            patientBill.Notify_IterationCompleted(doctor, new List<Thing>());
            Assert.True(data.Bill > 0f, "control: completed surgery was not charged");
            Assert.False(data.HasPendingSurgeryBill, "control: pending flag not cleared");
        }

        // ---------- EnemySelfPreservation ----------

        // Given: a non-patient, a patient and the pawn currently receiving arrival wounds (PawnBeingInjured).
        // When:  the ESP prefix runs (called directly, so the ESP mod itself is not needed).
        // Then:  true (ESP runs) for null and non-patients; false (ESP skipped) for patients and the pawn being injured.
        [HospitalTest("Vanilla: Enemy Self Preservation prefix lets ESP run for non-patients and skips it only for patients / pawns being injured")]
        public static void EspPrefix(TestContext ctx)
        {
            Pawn nobody = null;
            Assert.True(EnemySelfPreservation_Patch.Prefix(ref nobody), "null pawn must not skip ESP");
            var raider = Visitor(ctx, true);
            var colonist = Colonist(ctx);
            Assert.True(EnemySelfPreservation_Patch.Prefix(ref raider), "raider: ESP must run");
            Assert.True(EnemySelfPreservation_Patch.Prefix(ref colonist), "colonist: ESP must run");

            var setter = typeof(PatientUtility).GetProperty("PawnBeingInjured")?.GetSetMethod(true);
            Assert.NotNull(setter, "PatientUtility.PawnBeingInjured setter not found");
            var injured = Visitor(ctx, false);
            setter.Invoke(null, new object[] { injured });
            ctx.OnCleanup(() => setter.Invoke(null, new object[] { null }));
            Assert.False(EnemySelfPreservation_Patch.Prefix(ref injured), "control: pawn being injured must skip ESP");
            setter.Invoke(null, new object[] { null });
            Assert.True(EnemySelfPreservation_Patch.Prefix(ref injured), "after arrival wounds the pawn is a normal non-patient again");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            Assert.False(EnemySelfPreservation_Patch.Prefix(ref patient), "control: patient must skip ESP");
        }

        // ---------- JobGiver_Work ----------

        private static bool VanillaCanUseWorkGiver(Pawn pawn, WorkGiver g)
        {
            // mirror of vanilla JobGiver_Work.PawnCanUseWorkGiver (1.6)
            if (!g.def.nonColonistsCanDo && !pawn.IsColonist && !pawn.IsColonyMech && !pawn.IsColonySubhuman) return false;
            if (pawn.WorkTagIsDisabled(g.def.workTags)) return false;
            if (g.def.workType != null && pawn.WorkTypeIsDisabled(g.def.workType)) return false;
            if (g.ShouldSkip(pawn)) return false;
            if (g.MissingRequiredCapacity(pawn) != null) return false;
            if (pawn.RaceProps.IsMechanoid && !g.def.canBeDoneByMechs) return false;
            return true;
        }

        // Given: every WorkGiverDef in the game, a colonist and a neutral visitor (non-patients), then a patient.
        // When:  the real (patched) JobGiver_Work.PawnCanUseWorkGiver runs.
        // Then:  non-patients get exactly the vanilla answer for every giver; the patient may only use Patient / PatientBedRest work.
        [HospitalTest("Vanilla: JobGiver_Work.PawnCanUseWorkGiver gives vanilla answers for non-patients; a patient may only do Patient/PatientBedRest work")]
        public static void WorkGiverFilter(TestContext ctx)
        {
            var method = typeof(JobGiver_Work).GetMethod("PawnCanUseWorkGiver", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "JobGiver_Work.PawnCanUseWorkGiver not found");
            var jobGiver = new JobGiver_Work();
            bool Real(Pawn p, WorkGiver g) => (bool)method.Invoke(jobGiver, new object[] { p, g });

            var colonist = Colonist(ctx);
            var visitor = Visitor(ctx, false);
            var patient = Visitor(ctx, false);
            Register(ctx, patient);

            int compared = 0, patientWork = 0;
            foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
            {
                WorkGiver giver;
                bool expected;
                try { giver = def.Worker; expected = giver != null && VanillaCanUseWorkGiver(colonist, giver); }
                catch (Exception) { continue; } // a giver that cannot be evaluated outside its job context
                if (giver == null) continue;
                Assert.Equal(expected, Real(colonist, giver), "colonist, giver " + def.defName);
                Assert.Equal(VanillaCanUseWorkGiver(visitor, giver), Real(visitor, giver), "visitor, giver " + def.defName);
                bool isPatientWork = def.workType != null && (def.workType.defName.EqualsIgnoreCase("Patient") || def.workType.defName.EqualsIgnoreCase("PatientBedRest"));
                Assert.Equal(isPatientWork, Real(patient, giver), "control: patient, giver " + def.defName);
                if (isPatientWork) patientWork++;
                compared++;
            }
            Assert.True(compared > 10, "too few work givers compared: " + compared);
            Assert.True(patientWork > 0, "control: no Patient/PatientBedRest work giver exists, so the patient branch was not exercised");
        }

        // ---------- PatientOutcome_Patches ----------

        // Given: a hostile raider (non-patient) and, as control, a registered patient (death penalty set to 0 so goodwill stays put).
        // When:  Pawn.Kill runs.
        // Then:  raider dies and the roster, silver, goodwill and letters are unchanged; the patient is removed from the roster.
        [HospitalTest("Vanilla: killing a non-patient changes nothing in Hospital; killing a patient removes it from the roster")]
        public static void KillOutcome(TestContext ctx)
        {
            ProtectGoodwill(ctx);
            var raider = Visitor(ctx, true);
            var before = Snap(ctx);
            raider.Kill(null);
            ctx.OnCleanup(() => raider.Corpse?.Destroy());
            Assert.True(raider.Dead, "raider did not die");
            AssertSame(before, Snap(ctx), "killing a non-patient raider");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            int oldPenalty = HospitalMod.Settings.DeathGoodwillPenalty;
            HospitalMod.Settings.DeathGoodwillPenalty = 0;
            ctx.OnCleanup(() => HospitalMod.Settings.DeathGoodwillPenalty = oldPenalty);
            patient.Kill(null);
            ctx.OnCleanup(() => patient.Corpse?.Destroy());
            Assert.True(patient.Dead, "control: patient did not die");
            Assert.False(HospitalOf(ctx).Patients.ContainsKey(patient), "control: dead patient still on the roster");
        }

        // Given: a non-patient visitor and a registered patient with bill 50 (failure penalty set to 0).
        // When:  the vanilla botched-surgery thought method runs.
        // Then:  the visitor gets the BotchedMySurgery memory (vanilla) and nothing else changes; the patient's bill is zeroed and it stays registered (not dismissed).
        [HospitalTest("Vanilla: a botched surgery on a non-patient only gives the vanilla thought; on a patient it zeroes the bill and keeps the patient")]
        public static void BotchedSurgery(TestContext ctx)
        {
            ProtectGoodwill(ctx);
            var method = typeof(SurgeryOutcome_Failure).GetMethod("TryGainBotchedSurgeryThought", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "SurgeryOutcome_Failure.TryGainBotchedSurgeryThought not found");
            var outcome = new SurgeryOutcome_Failure();
            var surgeon = Colonist(ctx);

            var visitor = Visitor(ctx, false);
            var before = Snap(ctx);
            method.Invoke(outcome, new object[] { visitor, surgeon });
            Assert.True(visitor.needs.mood.thoughts.memories.Memories.Any(m => m.def == ThoughtDefOf.BotchedMySurgery), "vanilla botched-surgery memory missing");
            AssertSame(before, Snap(ctx), "botched surgery on a non-patient");

            var patient = Visitor(ctx, false);
            var data = Register(ctx, patient, 50f);
            int oldPenalty = HospitalMod.Settings.SurgeryFailGoodwillPenalty;
            HospitalMod.Settings.SurgeryFailGoodwillPenalty = 0;
            ctx.OnCleanup(() => HospitalMod.Settings.SurgeryFailGoodwillPenalty = oldPenalty);
            method.Invoke(outcome, new object[] { patient, surgeon });
            Assert.Equal(0f, data.Bill, "control: failed surgery must zero the bill");
            Assert.True(HospitalOf(ctx).Patients.ContainsKey(patient), "control: patient must stay registered after failed surgery");
            Assert.False(data.Dismissed, "control: patient must not be dismissed after failed surgery");
        }

        // Given: a hostile raider with timesGuestTendedToByPlayer = 3, a neutral non-patient with 0, and a registered neutral patient with 3.
        // When:  Faction.Notify_MemberExitedMap runs for each.
        // Then:  non-patients run vanilla (counter reset to 0, goodwill/roster unchanged); the patient skips vanilla (counter kept) and is dropped from the roster.
        [HospitalTest("Vanilla: Notify_MemberExitedMap runs the original for non-patients; for a patient it is skipped and the patient leaves the roster")]
        public static void MemberExitedMap(TestContext ctx)
        {
            ProtectGoodwill(ctx);
            var raider = Visitor(ctx, true);
            raider.mindState.timesGuestTendedToByPlayer = 3;
            var before = Snap(ctx);
            raider.Faction.Notify_MemberExitedMap(raider, false);
            Assert.Equal(0, raider.mindState.timesGuestTendedToByPlayer, "vanilla did not run for a raider (counter not reset)");
            AssertSame(before, Snap(ctx), "raider exiting the map");

            var visitor = Visitor(ctx, false);
            before = Snap(ctx);
            visitor.Faction.Notify_MemberExitedMap(visitor, false);
            AssertSame(before, Snap(ctx), "untended neutral visitor exiting the map");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            patient.mindState.timesGuestTendedToByPlayer = 3;
            patient.Faction.Notify_MemberExitedMap(patient, false);
            Assert.Equal(3, patient.mindState.timesGuestTendedToByPlayer, "control: original ran for a patient");
            Assert.False(HospitalOf(ctx).Patients.ContainsKey(patient), "control: patient still on the roster after leaving the map");
        }

        // Given: a neutral visitor (non-patient) and a registered patient.
        // When:  each joins the player faction (SetFaction), as on recruitment.
        // Then:  the visitor simply changes faction without side effects on the roster; the patient is dropped from the roster.
        [HospitalTest("Vanilla: SetFaction(player) on a non-patient only changes the faction; for a patient it also clears the roster entry")]
        public static void SetFactionToPlayer(TestContext ctx)
        {
            ProtectGoodwill(ctx);
            var visitor = Visitor(ctx, false);
            var before = Snap(ctx);
            visitor.SetFaction(Faction.OfPlayer);
            Assert.True(visitor.Faction == Faction.OfPlayer, "vanilla SetFaction did not change the faction");
            AssertSame(before, Snap(ctx), "recruiting a non-patient");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            patient.SetFaction(Faction.OfPlayer);
            Assert.True(patient.Faction == Faction.OfPlayer, "control: faction not changed");
            Assert.False(HospitalOf(ctx).Patients.ContainsKey(patient), "control: recruited patient still on the roster");
        }

        // ---------- Patients_BedFinder ----------

        // Given: a hospital bed on the map, a colonist and a neutral visitor (non-patients), a sentinel result, then a guest patient not in bed.
        // When:  the FindBedFor postfix runs.
        // Then:  the vanilla result (sentinel or null) is untouched for non-patients; the patient is handed a hospital bed.
        [HospitalTest("Vanilla: the FindBedFor postfix leaves the vanilla result alone for non-patients; a patient is given a hospital bed")]
        public static void FindBedPostfix(TestContext ctx)
        {
            var bed = MakeMedicalBed(ctx);
            var comp = bed.GetComp<CompHospitalBed>();
            comp.Hospital = true;
            var colonist = Colonist(ctx);
            var visitor = Visitor(ctx, false);

            foreach (var p in new[] { colonist, visitor })
            {
                Building_Bed result = bed;
                Patients_BedFinder_Patch.FindBedPatch.Postfix(ref result, p, p);
                Assert.True(result == bed, "postfix replaced the vanilla bed for a non-patient");
                result = null;
                Patients_BedFinder_Patch.FindBedPatch.Postfix(ref result, p, p);
                Assert.True(result == null, "postfix invented a bed for a non-patient");
            }

            // vanilla only lets a medical bed be used by a pawn that ShouldSeekMedicalRest (so the patient needs a wound),
            // and the traveler (the carrier) must be of the bed's faction - for a real patient that is a colonist, not the patient.
            var patient = Visitor(ctx, false);
            AddCut(patient);
            Register(ctx, patient);
            Assert.True(HealthAIUtility.ShouldSeekMedicalRest(patient), "precondition: injured patient does not seek medical rest");
            Building_Bed found = null;
            Patients_BedFinder_Patch.FindBedPatch.Postfix(ref found, patient, colonist);
            Assert.NotNull(found, "control: patient was not given a bed (is a hospital bed free and reachable?)");
            Assert.True(found.Medical && found.GetComp<CompHospitalBed>().Hospital, "control: patient got a non-hospital bed");
        }

        // ---------- TakeWoundedGuestPatch ----------

        // Given: a hostile raider searcher and a downed non-patient guest of the same faction.
        // When:  KidnapAIUtility.ReachableWoundedGuest runs (the real, patched method).
        // Then:  vanilla result (a wounded guest is returned). Control: the postfix nulls a patient result and keeps null / non-patients.
        [HospitalTest("Vanilla: ReachableWoundedGuest still returns downed non-patient guests; a patient result is replaced by null")]
        public static void WoundedGuestKidnap(TestContext ctx)
        {
            var searcher = Visitor(ctx, true);
            var guest = HospitalRegressionTests.MakePawn(ctx, searcher.Faction, true);
            Down(guest);
            Assert.True(guest.guest != null && !guest.IsPrisoner, "precondition: test guest is not a non-prisoner guest");
            var found = KidnapAIUtility.ReachableWoundedGuest(searcher);
            Assert.NotNull(found, "vanilla would return the downed guest, Hospital returned null");

            Pawn result = null;
            TakeWoundedGuestPatch.HasJobOnThing_Patch.Postfix(ref result);
            Assert.True(result == null, "null result changed");
            result = guest;
            TakeWoundedGuestPatch.HasJobOnThing_Patch.Postfix(ref result);
            Assert.True(result == guest, "non-patient result was replaced");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            result = patient;
            TakeWoundedGuestPatch.HasJobOnThing_Patch.Postfix(ref result);
            Assert.True(result == null, "control: patient result must become null");
        }

        // ---------- ThinkNode_ConditionalNPCCanSelfTendNow ----------

        // Given: an injured neutral visitor on a quiet map, a colonist and a raider.
        // When:  the real (patched) Satisfied runs, then again after the visitor is registered as patient.
        // Then:  vanilla answers (visitor true, colonist false, raider false); the patient may not self-tend (false).
        [HospitalTest("Vanilla: NPC self-tend condition is unchanged for non-patients; patients may not self-tend")]
        public static void NpcSelfTendCondition(TestContext ctx)
        {
            var satisfied = typeof(ThinkNode_ConditionalNPCCanSelfTendNow).GetMethod("Satisfied", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(satisfied, "ThinkNode_ConditionalNPCCanSelfTendNow.Satisfied not found");
            var node = new ThinkNode_ConditionalNPCCanSelfTendNow();
            bool Call(Pawn p) => (bool)satisfied.Invoke(node, new object[] { p });

            // The raider is created only at the end: a hostile-faction pawn standing nearby is an "active threat"
            // for the visitor (vanilla looks 5 regions around it) and would make the vanilla answer false.
            var visitor = Visitor(ctx, false);
            AddCut(visitor);
            var colonist = Colonist(ctx);
            AddCut(colonist);

            Assert.True(Call(visitor), "vanilla: an injured neutral visitor self-tends (precondition failed: active threat within 5 regions of the visitor?)");
            Assert.False(Call(colonist), "vanilla: colonists never use the NPC self-tend node");

            Register(ctx, visitor);
            Assert.False(Call(visitor), "control: a patient must not self-tend");

            var raider = Visitor(ctx, true);
            AddCut(raider);
            Assert.False(Call(raider), "vanilla: hostile pawns never use the NPC self-tend node");
        }

        // Given: self-tend jobs (target = the worker itself) for a colonist, a non-patient visitor and a patient; plus a doctor tending the patient.
        // When:  JobDriver_TendPatient.TryMakePreToilReservations runs.
        // Then:  true (vanilla reservation succeeds) for non-patients and for someone else tending the patient; false for the patient tending itself.
        [HospitalTest("Vanilla: self-tend reservations succeed for non-patients; only a patient tending itself is refused")]
        public static void SelfTendReservation(TestContext ctx)
        {
            bool Reserve(Pawn worker, Pawn target)
            {
                var driver = new JobDriver_TendPatient { pawn = worker, job = JobMaker.MakeJob(JobDefOf.TendPatient, target) };
                try { return driver.TryMakePreToilReservations(false); }
                finally { worker.Map.reservationManager.ReleaseAllClaimedBy(worker); }
            }

            var colonist = Colonist(ctx);
            var visitor = Visitor(ctx, false);
            Assert.True(Reserve(colonist, colonist), "colonist self-tend reservation");
            Assert.True(Reserve(visitor, visitor), "non-patient visitor self-tend reservation");

            var patient = Visitor(ctx, false);
            Register(ctx, patient);
            Assert.True(Reserve(colonist, patient), "a doctor tending a patient must still reserve it");
            Assert.False(Reserve(patient, patient), "control: a patient tending itself must be refused");
        }

        // ---------- CompHospitalBed ----------

        // Given: a plain bed with Hospital=false and a colonist.
        // When:  the colonist is assigned the bed.
        // Then:  assignment works and the owner is kept; colour is the vanilla one. (Plain beds are never counted by the hospital.)
        [HospitalTest("Vanilla: a plain bed with Hospital=false can be assigned to a colonist and keeps its owner")]
        public static void PlainBedAssignable(TestContext ctx)
        {
            var (bed, comp) = HospitalRegressionTests.MakeBed(ctx);
            var colonist = Colonist(ctx);
            comp.Hospital = false;
            bed.CompAssignableToPawn.TryAssignPawn(colonist);
            Assert.True(bed.OwnersForReading.Contains(colonist), "vanilla assignment failed on a Hospital=false bed");
            comp.Hospital = false; // no-op setter call must not strip owners
            Assert.True(bed.OwnersForReading.Contains(colonist), "owner lost on a Hospital=false bed");
            Assert.Equal(VanillaNormal, bed.DrawColorTwo, "bed colour");
        }

        // Given: a medical hospital bed switched to Hospital=false, and an injured colonist.
        // When:  hospital capacity and RestUtility.IsValidBedFor are evaluated.
        // Then:  the bed is not counted as hospital capacity (count unchanged) but is a valid medical bed for the colonist, exactly as with Hospital=true;
        //        switching to Hospital=true adds exactly one bed of capacity.
        [HospitalTest("Vanilla: a medical bed with Hospital=false is usable by a colonist like vanilla and not counted as hospital capacity")]
        public static void MedicalBedNotHospital(TestContext ctx)
        {
            var hospital = HospitalOf(ctx);
            int baseline = hospital.BedCount();
            var colonist = Colonist(ctx);
            AddCut(colonist);

            var bed = MakeMedicalBed(ctx);
            var comp = bed.GetComp<CompHospitalBed>();
            comp.Hospital = false;
            Assert.Equal(baseline, hospital.BedCount(), "Hospital=false bed counted as hospital capacity");
            Assert.True(RestUtility.IsValidBedFor(bed, colonist, colonist, false, allowMedBedEvenIfSetToNoCare: true),
                "Hospital=false medical bed is not a valid bed for an injured colonist (vanilla would allow it; precondition: reachable and ShouldSeekMedicalRest)");

            comp.Hospital = true;
            Assert.Equal(baseline + 1, hospital.BedCount(), "control: Hospital=true bed not counted");
            Assert.True(RestUtility.IsValidBedFor(bed, colonist, colonist, false, allowMedBedEvenIfSetToNoCare: true),
                "Hospital=true must not change vanilla bed validity for a colonist");
        }

        // B11 is a known design question (not asserted as a bug): CompHospitalBed.PostPostMake and Scribe default
        // both give hospitalInt = true, so existing medical beds become hospital beds when the mod is added to a save.
        // Given: a freshly made medical bed.  When: nothing is touched.  Then: Hospital is true, Surgery is false.
        [HospitalTest("B11 (pinned current behaviour, design question): a freshly made medical bed defaults to Hospital=true, Surgery=false")]
        public static void BedDefaultsToHospital(TestContext ctx)
        {
            var bedDef = DefDatabase<ThingDef>.GetNamed("HospitalBed");
            var bed = (Building_Bed)ctx.Track(ThingMaker.MakeThing(bedDef, bedDef.MadeFromStuff ? GenStuff.DefaultStuffFor(bedDef) : null));
            var comp = bed.GetComp<CompHospitalBed>();
            Assert.NotNull(comp, "bed has no CompHospitalBed");
            Assert.True(comp.Hospital, "default of Hospital changed (B11 pinned: true)");
            Assert.False(comp.Surgery, "default of Surgery changed (pinned: false)");
        }

        // ---------- A1: global Rand state ----------

        // Approach: Rand's seed is a private static that only changes by assignment (Rand.seed = x / Seed = x / PushState(seed) / PopState);
        // every normal Rand.Value / Range call only advances the private "iterations" counter.
        // So: push a known seed (12345), run each wound generator on a fresh pawn, and assert the seed is still 12345.
        // The old bug (Rand.seed = (uint)SummaryHealthPercent, i.e. 0 or 1) would show up as a seed != 12345.
        // We also assert the iterations counter advanced, so we know the generator really consumed RNG (the test is not vacuous).
        [HospitalTest("A1: generating patient wounds does not reseed the global Rand (seed stays, counter only advances)")]
        public static void WoundsDoNotReseedRand(TestContext ctx)
        {
            var seedField = typeof(Rand).GetField("seed", BindingFlags.NonPublic | BindingFlags.Static);
            var iterField = typeof(Rand).GetField("iterations", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(seedField, "Rand.seed not found (renamed?)");
            Assert.NotNull(iterField, "Rand.iterations not found (renamed?)");

            var generators = new List<(string name, Action<Pawn, PatientData> run)>
            {
                ("AddRandomWounds", WoundsUtility.AddRandomWounds),
                ("AddGunshotWounds", WoundsUtility.AddGunshotWounds),
                ("AddBruisesWounds", WoundsUtility.AddBruisesWounds),
                ("AddBurnWounds", WoundsUtility.AddBurnWounds),
            };
            // pawns first: generating them consumes Rand too and is not what we measure
            var pawns = generators.Select(_ => HospitalRegressionTests.MakePawn(ctx, null, false)).ToList();

            const int Seed = 12345;
            Rand.PushState(Seed);
            ctx.OnCleanup(Rand.PopState);
            for (int i = 0; i < generators.Count; i++)
            {
                uint itersBefore = (uint)iterField.GetValue(null);
                generators[i].run(pawns[i], new PatientData());
                Assert.Equal((uint)Seed, (uint)seedField.GetValue(null), generators[i].name + " reseeded the global Rand");
                Assert.True((uint)iterField.GetValue(null) > itersBefore, generators[i].name + " did not consume any RNG (test would prove nothing)");
            }
        }
    }
}
