using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HospitalTests
{
    // Regression tests for the bugs listed in .claude/bug-analysis.md (IDs B3, B4, B5, A4, A5, A8).
    // Tests are written to FAIL on the pre-fix code (except B3, a guard). Where the old code would loop forever,
    // a counter throws after a few dozen attempts instead of freezing the game (see CountingHediff / MakeHediffGuard).
    public static class BugFixTests
    {
        // ---------- helpers ----------

        // A concrete hediff that counts how many instances are created and can be made "never tendable".
        // The count is the attempt counter of the disease generators: the constructor throws past Limit.
        public class CountingHediff : Hediff
        {
            public static int Created;
            public static int Limit = int.MaxValue;
            public static bool NeverTendable;

            public CountingHediff()
            {
                if (++Created > Limit)
                    throw new AssertException("disease generator did not stop: more than " + Limit + " hediffs were created (unbounded retry loop)");
            }

            public override bool TendableNow(bool ignoreTimer = false)
            {
                return !NeverTendable && base.TendableNow(ignoreTimer);
            }
        }

        // Registers a synthetic disease def in the database (so CandidateDiseases sees it); removed again on cleanup.
        private static HediffDef AddDisease(TestContext ctx, string defName, float lethalSeverity)
        {
            var def = new HediffDef
            {
                defName = defName,
                label = defName,
                description = "test disease",
                hediffClass = typeof(CountingHediff),
                tendable = true,
                makesSickThought = true,
                lethalSeverity = lethalSeverity,
                initialSeverity = 0.01f
            };
            DefDatabase<HediffDef>.Add(def);
            var remove = typeof(DefDatabase<HediffDef>).GetMethod("Remove", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(remove, "DefDatabase<HediffDef>.Remove not found");
            ctx.OnCleanup(() => remove.Invoke(null, new object[] { def }));
            CountingHediff.Created = 0;
            CountingHediff.Limit = int.MaxValue;
            CountingHediff.NeverTendable = false;
            ctx.OnCleanup(() => { CountingHediff.Limit = int.MaxValue; CountingHediff.NeverTendable = false; });
            return def;
        }

        // Blacklists every disease except the given ones for the duration of the test.
        private static void OnlyAllowDiseases(TestContext ctx, HospitalMapComponent hospital, params HediffDef[] allowed)
        {
            var saved = hospital.refusedDiseases;
            hospital.refusedDiseases = DefDatabase<HediffDef>.AllDefsListForReading.Where(d => !allowed.Contains(d)).ToList();
            ctx.OnCleanup(() => hospital.refusedDiseases = saved);
        }

        // Unspawned generated pawn without any hediff (random old injuries could make HasHediffsNeedingTend true).
        private static Pawn CleanPawn(TestContext ctx)
        {
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, false);
            foreach (var h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
            Assert.Equal(0, pawn.health.hediffSet.hediffs.Count, "precondition: pawn still has hediffs");
            return pawn;
        }

        private static PatientData NewData(PatientType type)
        {
            return new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, type);
        }

        // ---------- B4: unbounded retry in the disease generators ----------

        // Given: the only allowed disease candidate has lethalSeverity -1 (modded "no lethal severity" disease): its severity is
        //        always 0, so it never makes the pawn sick. The old loop kept adding it forever (CountingHediff throws after 60).
        // When:  AddRandomDisease runs for a pawn without other hediffs.
        // Then:  such a def is no candidate at all, the function returns true (fallback to wounds) and the pawn got no hediff.
        [HospitalTest("B4: a disease with lethalSeverity <= 0 is never a candidate; AddRandomDisease returns true (fallback) without adding hediffs")]
        public static void DiseaseWithoutLethalSeverityIsSkipped(TestContext ctx)
        {
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var def = AddDisease(ctx, "HospitalTestNoLethalSeverity", -1f);
            CountingHediff.Limit = 60;
            OnlyAllowDiseases(ctx, hospital, def);
            var pawn = CleanPawn(ctx);
            var data = NewData(PatientType.Disease);

            bool failed = DiseaseUtility.AddRandomDisease(pawn, data, hospital);

            Assert.False(DiseaseUtility.CandidateDiseases().Contains(def), "lethalSeverity <= 0 disease is still a candidate");
            Assert.True(failed, "AddRandomDisease did not return the failure value (true)");
            Assert.Equal(0, pawn.health.hediffSet.hediffs.Count, "hediffs left on the pawn");
            Assert.True(string.IsNullOrEmpty(data.Diagnosis), "diagnosis set although no disease was added");
        }

        // Given: the only candidate is a valid disease (lethalSeverity 1) whose hediff never needs tending (e.g. odd modded hediff).
        // When:  AddRandomDisease runs.
        // Then:  it gives up after a bounded number of attempts (<= 25), returns true and removes the hediffs of the failed attempts.
        [HospitalTest("B4: AddRandomDisease gives up after a bounded number of attempts, returns true and leaves no failed hediffs on the pawn")]
        public static void RandomDiseaseRetryIsBounded(TestContext ctx)
        {
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var def = AddDisease(ctx, "HospitalTestNeverTendable", 1f);
            CountingHediff.NeverTendable = true;
            CountingHediff.Limit = 60; // old code: throws here instead of freezing the game
            OnlyAllowDiseases(ctx, hospital, def);
            var pawn = CleanPawn(ctx);
            var data = NewData(PatientType.Disease);

            bool failed = DiseaseUtility.AddRandomDisease(pawn, data, hospital);

            Assert.True(CountingHediff.Created >= 1, "the test candidate was never tried (is it a candidate?)");
            Assert.True(CountingHediff.Created <= 25, "too many attempts: " + CountingHediff.Created);
            Assert.True(failed, "AddRandomDisease did not return the failure value (true)");
            Assert.Equal(0, pawn.health.hediffSet.hediffs.Count(h => h.def == def), "failed attempts piled up on the pawn");
            Assert.True(string.IsNullOrEmpty(data.Diagnosis), "diagnosis set although no disease was added");
        }

        // Prefix for HediffMaker.MakeHediff: throws once the guarded call made too many hediffs.
        private static bool GuardActive;
        private static int GuardCount;
        private const int GuardLimit = 100;
        public static bool MakeHediffGuard()
        {
            if (GuardActive && ++GuardCount > GuardLimit)
                throw new AssertException("AddPlague did not stop: more than " + GuardLimit + " hediffs were made (unbounded retry loop)");
            return true;
        }

        // Given: a pawn fully immune to plague (the plague never makes them sick; the old loop then swapped in Hangover forever).
        //        A Harmony prefix on HediffMaker.MakeHediff throws after 100 calls instead of freezing the game.
        // When:  AddPlague runs.
        // Then:  it returns true (caller falls back to wounds), makes a bounded number of hediffs, leaves none on the pawn, sets no diagnosis.
        [HospitalTest("B4: AddPlague for a pawn immune to plague gives up after a bounded number of attempts and leaves no hediffs")]
        public static void PlagueRetryIsBounded(TestContext ctx)
        {
            var pawn = CleanPawn(ctx);
            pawn.health.immunity.TryAddImmunityRecord(HediffDefOf.Plague, HediffDefOf.Plague);
            var record = pawn.health.immunity.GetImmunityRecord(HediffDefOf.Plague);
            Assert.NotNull(record, "precondition: plague has no immunity record (no Immunizable comp?)");
            record.immunity = 1f;
            Assert.True(pawn.health.immunity.GetImmunity(HediffDefOf.Plague) >= 1f, "precondition: pawn is not immune");
            var data = NewData(PatientType.Disease);

            var harmony = new Harmony("Adamas.Hospital.Tests.B4Guard");
            var original = AccessTools.Method(typeof(HediffMaker), nameof(HediffMaker.MakeHediff));
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(BugFixTests), nameof(MakeHediffGuard)));
            ctx.OnCleanup(() => { GuardActive = false; harmony.Unpatch(original, HarmonyPatchType.Prefix, harmony.Id); });
            GuardCount = 0;
            GuardActive = true;
            bool failed;
            try { failed = DiseaseUtility.AddPlague(pawn, data); }
            finally { GuardActive = false; }

            Assert.True(GuardCount <= 25, "too many attempts: " + GuardCount);
            Assert.True(failed, "AddPlague did not return the failure value (true)");
            Assert.Equal(0, pawn.health.hediffSet.hediffs.Count, "failed attempts piled up on the pawn");
            Assert.True(string.IsNullOrEmpty(data.Diagnosis), "diagnosis set although no plague was added");
        }

        // ---------- A5: infection / lung branches report success without adding anything ----------

        // Given: a candidate disease whose name contains "infection", and a pawn without any scarifiable body part
        //        (canScarify cleared on every BodyPartDef for the duration of the call).
        // When:  AddRandomDisease runs.
        // Then:  it returns true (fallback to wounds), no diagnosis / bill / cure is set and no hediff was added.
        [HospitalTest("A5: AddRandomDisease with an infection and no suitable body part returns true and sets no diagnosis")]
        public static void InfectionWithoutPartFallsBack(TestContext ctx)
        {
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var def = AddDisease(ctx, "HospitalTestInfection", 1f);
            OnlyAllowDiseases(ctx, hospital, def);
            var pawn = CleanPawn(ctx);
            var data = NewData(PatientType.Disease);

            var parts = DefDatabase<BodyPartDef>.AllDefsListForReading.Where(d => d.canScarify).ToList();
            Assert.True(parts.Count > 0, "precondition: no scarifiable body part defs");
            ctx.OnCleanup(() => { foreach (var d in parts) d.canScarify = true; });
            foreach (var d in parts) d.canScarify = false;

            bool failed = DiseaseUtility.AddRandomDisease(pawn, data, hospital);

            AssertNothingAdded(failed, pawn, data);
        }

        // Given: a candidate disease whose name contains "lung", and a pawn without any body part named "lung"
        //        (the lung part defs are renamed for the duration of the call).
        // When:  AddRandomDisease runs.
        // Then:  it returns true (fallback to wounds), no diagnosis / bill / cure is set and no hediff was added.
        [HospitalTest("A5: AddRandomDisease with lung rot and no lung returns true and sets no diagnosis")]
        public static void LungRotWithoutLungFallsBack(TestContext ctx)
        {
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var def = AddDisease(ctx, "HospitalTestLungRot", 1f);
            OnlyAllowDiseases(ctx, hospital, def);
            var pawn = CleanPawn(ctx);
            var data = NewData(PatientType.Disease);

            var lungs = DefDatabase<BodyPartDef>.AllDefsListForReading.Where(d => d.defName.ToLower().Contains("lung")).ToList();
            Assert.True(lungs.Count > 0, "precondition: no lung body part def");
            var names = lungs.ToDictionary(d => d, d => d.defName);
            ctx.OnCleanup(() => { foreach (var kv in names) kv.Key.defName = kv.Value; });
            foreach (var d in lungs) d.defName = "Respirator" + d.defName.Substring(4);

            bool failed = DiseaseUtility.AddRandomDisease(pawn, data, hospital);

            AssertNothingAdded(failed, pawn, data);
        }

        private static void AssertNothingAdded(bool failed, Pawn pawn, PatientData data)
        {
            Assert.True(failed, "AddRandomDisease returned success (false) although nothing was added");
            Assert.True(string.IsNullOrEmpty(data.Diagnosis), "diagnosis set without a hediff: " + data.Diagnosis);
            Assert.True(string.IsNullOrEmpty(data.Cure), "cure set without a hediff: " + data.Cure);
            Assert.Equal(0f, data.Bill, "bill set without a hediff");
            Assert.Equal(0, pawn.health.hediffSet.hediffs.Count, "hediffs added");
        }

        // ---------- B5: arresting a patient ----------

        // Given: a registered patient (guest of the player) who owns a patient lord, like after an arrival.
        // When:  the player arrests them the vanilla way: guest.SetGuestStatus(player, Prisoner) -> lord.Notify_PawnLost(MadePrisoner).
        // Then:  they are a prisoner of the player (vanilla sets the status after the lord callback returns) and are no longer
        //        on the hospital roster (the old code only marked them Dismissed, so every patient patch still treated the prisoner as a patient).
        [HospitalTest("B5: an arrested patient stays a prisoner of the player and is removed from the Patients roster")]
        public static void ArrestedPatientLeavesRoster(TestContext ctx)
        {
            VanillaCompatTests.ProtectGoodwill(ctx);
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var map = ctx.Map;
            var patient = VanillaCompatTests.Visitor(ctx, false);
            var faction = patient.Faction;

            // The lord is created first so its cleanup runs last (after the patient was unregistered; removing it would pay a registered patient).
            var lord = LordMaker.MakeNewLord(faction, new LordJob_VisitColonyAsPatient(faction), map, new List<Pawn> { patient });
            ctx.OnCleanup(() => { if (map.lordManager.lords.Contains(lord)) map.lordManager.RemoveLord(lord); });
            VanillaCompatTests.Register(ctx, patient);
            Assert.True(patient.GetLord() == lord, "precondition: patient is not in the lord");

            patient.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);

            Assert.True(patient.guest.IsPrisoner, "arrested patient is not a prisoner");
            Assert.True(patient.guest.HostFaction == Faction.OfPlayer, "arrested patient is not a prisoner of the player");
            Assert.False(hospital.Patients.ContainsKey(patient), "arrested patient is still on the Patients roster");
            Assert.False(patient.IsPatient(out _), "arrested patient is still treated as a patient");
        }

        // ---------- B6: failed surgery keeps the patient ----------

        // Given: a registered patient (bill 50, guest of the player) who is downed; failure penalty set to 0.
        // When:  the vanilla botched-surgery thought method runs (the patched failure path).
        // Then:  the patient is still registered, not Dismissed, still a Guest, Bill is 0, and Trigger_SentAway.SentAway is false while downed;
        //        once healed and back on their feet it is true (the patient is then sent home without paying for the failed attempt).
        [HospitalTest("B6: after a failed surgery the patient stays registered and is not sent away while downed")]
        public static void FailedSurgeryKeepsPatient(TestContext ctx)
        {
            VanillaCompatTests.ProtectGoodwill(ctx);
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            int oldPenalty = HospitalMod.Settings.SurgeryFailGoodwillPenalty;
            HospitalMod.Settings.SurgeryFailGoodwillPenalty = 0;
            ctx.OnCleanup(() => HospitalMod.Settings.SurgeryFailGoodwillPenalty = oldPenalty);

            var patient = VanillaCompatTests.Visitor(ctx, false);
            var data = VanillaCompatTests.Register(ctx, patient, 50f);
            VanillaCompatTests.Down(patient);

            var fail = typeof(SurgeryOutcome_Failure).GetMethod("TryGainBotchedSurgeryThought", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(fail, "SurgeryOutcome_Failure.TryGainBotchedSurgeryThought not found");
            fail.Invoke(new SurgeryOutcome_Failure(), new object[] { patient, VanillaCompatTests.Colonist(ctx) });

            Assert.True(hospital.Patients.ContainsKey(patient), "patient was dropped from the roster after a failed surgery");
            Assert.False(data.Dismissed, "patient was dismissed after a failed surgery");
            Assert.True(patient.guest.GuestStatus == GuestStatus.Guest, "patient is no longer a guest");
            Assert.Equal(0f, data.Bill, "failed surgery must zero the bill");

            var sentAway = typeof(Trigger_SentAway).GetMethod("SentAway", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(sentAway, "Trigger_SentAway.SentAway not found");
            Assert.False((bool)sentAway.Invoke(null, new object[] { patient }), "downed patient would be sent away");

            // Recovery: lift the forced downed state and re-evaluate (adding/removing a hediff triggers the vanilla state check).
            if (PatientUtility.DangersOnMap(ctx.Map, out _)) return; // SentAway is always false with dangers on the map; recovery part not testable
            patient.health.forceDowned = false;
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Arm).First());
            patient.health.AddHediff(cut);
            patient.health.RemoveHediff(cut);
            Assert.False(patient.Downed, "precondition: patient is still downed after recovery");
            Assert.True((bool)sentAway.Invoke(null, new object[] { patient }), "recovered patient is not sent away");
        }

        // ---------- B3: patients in flight vs RemoveLostPatients ----------

        // Given: a patient who just arrived and is still inside the drop pod (Skyfaller -> ActiveTransporter -> innerContainer -> pawn).
        // When:  the safety net RemoveLostPatients runs (private, called by reflection; normally every 2000 ticks).
        // Then:  the patient is still registered, because MapHeld resolves through the drop pod to the map.
        //        Expected to PASS on the old code too: B3 turned out not to be a bug, this is a regression guard.
        [HospitalTest("B3: RemoveLostPatients keeps a patient who is still inside the arrival drop pod (MapHeld works through the Skyfaller)")]
        public static void RemoveLostPatientsKeepsPatientInDropPod(TestContext ctx)
        {
            var hospital = VanillaCompatTests.HospitalOf(ctx);
            var added = ArrivalTests.FireArrival(ctx, out _);
            Assert.True(added.Count >= 1, "no patient was added");
            foreach (var pair in added)
            {
                Assert.True(!pair.Key.Spawned && ThingOwnerUtility.AnyParentIs<Skyfaller>(pair.Key), "precondition: patient is not inside the drop pod");
                Assert.True(pair.Key.MapHeld == ctx.Map, "MapHeld of a pawn in a drop pod is not the map");
            }

            var remove = typeof(HospitalMapComponent).GetMethod("RemoveLostPatients", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(remove, "HospitalMapComponent.RemoveLostPatients not found");
            remove.Invoke(hospital, null);

            foreach (var pair in added)
                Assert.True(hospital.Patients.ContainsKey(pair.Key), "patient in the drop pod was dropped from the roster");
        }

        // ---------- A4: special part blocks of the surgery generator ----------

        private static BodyPartRecord Core(Pawn pawn) => pawn.RaceProps.body.corePart;

        // Given: a recipe whose defName contains "deaf" and a pawn with both ears; the part chosen so far is the torso.
        // When:  the name-based part overrides run.
        // Then:  the result is an ear (the old code looked for a kidney).
        [HospitalTest("A4: a 'deaf' surgery recipe targets an ear, not a kidney")]
        public static void DeafRecipeTargetsEar(TestContext ctx)
        {
            var pawn = CleanPawn(ctx);
            var recipe = new RecipeDef { defName = "HospitalTestDeafness" };

            var part = SurgeryUtility.ApplyNameBasedPartOverrides(recipe, pawn, Core(pawn));

            Assert.NotNull(part, "no part selected");
            Assert.Equal("ear", part.def.label.ToLower(), "selected part for a deafness recipe");
        }

        // Given: a "glaucoma" recipe and a pawn missing both eyes; the part chosen so far is the torso.
        // When:  the overrides run.
        // Then:  the earlier choice is kept (the old code overwrote it with null, losing the part).
        [HospitalTest("A4: a special recipe keeps the already chosen part when the pawn lacks the special part")]
        public static void MissingSpecialPartKeepsChosenPart(TestContext ctx)
        {
            var pawn = CleanPawn(ctx);
            foreach (var eye in pawn.RaceProps.body.AllParts.Where(p => p.def.label.ToLower() == "eye").ToList())
                pawn.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, eye), eye);
            Assert.False(pawn.health.hediffSet.GetNotMissingParts().Any(p => p.def.label.ToLower() == "eye"), "precondition: pawn still has an eye");
            var chosen = Core(pawn);
            var recipe = new RecipeDef { defName = "HospitalTestGlaucoma" };

            var part = SurgeryUtility.ApplyNameBasedPartOverrides(recipe, pawn, chosen);

            Assert.True(part == chosen, "the chosen part was overwritten: " + (part?.Label ?? "null"));
        }

        // Given: recipes for kidney and for a hediff named "toothache" on a healthy pawn.
        // When:  the overrides run.
        // Then:  the kidney recipe targets a kidney, the toothache recipe a jaw (rules that worked before still work).
        [HospitalTest("A4: kidney and toothache rules still select their part")]
        public static void KidneyAndToothacheRulesStillWork(TestContext ctx)
        {
            var pawn = CleanPawn(ctx);
            var kidney = SurgeryUtility.ApplyNameBasedPartOverrides(new RecipeDef { defName = "HospitalTestKidneyStone" }, pawn, Core(pawn));
            Assert.Equal("kidney", kidney?.def.label.ToLower(), "kidney recipe");
            var toothache = new HediffDef { defName = "Toothache" };
            var jaw = SurgeryUtility.ApplyNameBasedPartOverrides(new RecipeDef { defName = "HospitalTestPullTooth", removesHediff = toothache }, pawn, Core(pawn));
            Assert.Equal("jaw", jaw?.def.label.ToLower(), "toothache recipe");
        }

        // ---------- A8: feeding bill ----------

        // Given: a registered patient and feeding jobs (feeder -> patient, meal) with the patched MakeNewToils run.
        // When:  the driver is cleaned up with an interrupting condition, then with Succeeded but no food, then with Succeeded and a meal.
        // Then:  the interrupted feeding and the foodless one bill nothing (and log no error); the completed one bills clamp(meal value * 1.1, 0, 70).
        [HospitalTest("A8: feeding bills the patient only when the job succeeded with food; an interrupted feeding is free")]
        public static void FeedingBilledOnlyWhenSucceeded(TestContext ctx)
        {
            var toils = typeof(JobDriver_FoodFeedPatient).GetMethod("MakeNewToils", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(toils, "JobDriver_FoodFeedPatient.MakeNewToils not found");
            var feeder = VanillaCompatTests.Colonist(ctx);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            var data = VanillaCompatTests.Register(ctx, patient);

            void Feed(LocalTargetInfo food, JobCondition condition)
            {
                var driver = new JobDriver_FoodFeedPatient { pawn = feeder, job = JobMaker.MakeJob(JobDefOf.FeedPatient, food, patient) };
                toils.Invoke(driver, null); // lazy iterator: only the postfix (AddFinishAction) runs
                driver.Cleanup(condition);
            }

            var meal = ctx.Track(ThingMaker.MakeThing(ThingDefOf.MealSimple));
            Feed(meal, JobCondition.InterruptForced);
            Feed(meal, JobCondition.Incompletable);
            Assert.Equal(0f, data.Bill, "an interrupted feeding was billed");

            Feed(LocalTargetInfo.Invalid, JobCondition.Succeeded); // old code: NullReferenceException logged as an error
            Assert.Equal(0f, data.Bill, "a feeding without food was billed");

            Feed(meal, JobCondition.Succeeded);
            float expected = UnityEngine.Mathf.Clamp(meal.MarketValue * 1.1f, 0, 70);
            Assert.True(Math.Abs(data.Bill - expected) < 0.01f, "completed feeding: expected bill " + expected + " but was " + data.Bill);
        }

        // Given: a faction list and a predicate that rejects every generated pawn (a faction that only yields androids).
        // When:  TryGenerateAcceptedPawn runs with a cap of 5 attempts.
        // Then:  it returns false after exactly 5 generations (old code looped forever) and every rejected pawn is discarded.
        [HospitalTest("A18: patient generation gives up after a bounded number of attempts and discards the rejected pawns")]
        public static void PatientGenerationIsBounded(TestContext ctx)
        {
            var factions = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && f.RandomPawnKind() != null).ToList();
            Assert.True(factions.Count > 0, "test save has no humanlike faction");
            var generated = new List<Pawn>();
            int calls = 0;
            Pawn Gen(Faction f)
            {
                if (++calls > 50) throw new AssertException("generation did not stop (unbounded loop)");
                var p = IncidentHelper.GeneratePawn(f);
                generated.Add(p);
                return p;
            }
            ctx.OnCleanup(() => { foreach (var p in generated) IncidentHelper.DiscardGeneratedPawn(p); });

            bool ok = IncidentHelper.TryGenerateAcceptedPawn(factions, p => false, 5, out var pawn, out var faction, Gen);
            Assert.False(ok, "should give up when every pawn is rejected");
            Assert.True(pawn == null && faction == null, "no pawn expected");
            Assert.Equal(5, calls, "attempt count");
            foreach (var p in generated)
                Assert.True(p.Discarded || !Find.WorldPawns.Contains(p), "rejected pawn leaked in the world pawn pool: " + p.LabelShort);
        }

        // Given: a generator that returns no pawn (faction without pawn kinds), then the real generator.
        // Then:  the first gives up (bounded), the second returns an accepted pawn with its faction.
        [HospitalTest("A18/B10: null pawn from the generator is skipped; an accepted pawn is returned with its faction")]
        public static void PatientGenerationNullAndAccepted(TestContext ctx)
        {
            var factions = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && f.RandomPawnKind() != null).ToList();
            Assert.True(factions.Count > 0, "test save has no humanlike faction");
            int calls = 0;
            bool ok = IncidentHelper.TryGenerateAcceptedPawn(factions, p => true, 4, out _, out _, f => { calls++; return null; });
            Assert.False(ok, "null pawns must not count as success");
            Assert.Equal(4, calls, "attempt count");

            ok = IncidentHelper.TryGenerateAcceptedPawn(factions, IncidentHelper.IsSuitablePatient, 20, out var pawn, out var faction);
            Assert.True(ok, "a normal humanlike pawn should be accepted within 20 attempts");
            ctx.OnCleanup(() => IncidentHelper.DiscardGeneratedPawn(pawn));
            Assert.True(pawn.Faction == faction, "returned faction must match the pawn");
            Assert.False(IncidentHelper.TryGenerateAcceptedPawn(new List<Faction>(), p => true, 4, out _, out _), "empty faction list");
        }

        // Then: with 0 beds the MCE count is <= 0 (the worker refuses it), with beds it stays in [bed - bed/5, bed].
        [HospitalTest("A19: MCE pawn count is <= 0 without beds and the worker returns false for it")]
        public static void McePawnCountWithoutBeds(TestContext ctx)
        {
            Assert.True(IncidentWorker_MassCasualtyEvent.RollPawnCount(0) <= 0, "0 beds must not give a positive count");
            for (int i = 0; i < 50; i++)
            {
                int n = IncidentWorker_MassCasualtyEvent.RollPawnCount(10);
                Assert.True(n >= 8 && n <= 10, "10 beds -> count " + n);
            }
            var map = ctx.Map;
            var hospital = map.GetComponent<HospitalMapComponent>();
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail("MassCasualtyEvent");
            if (def == null) return; // incident def has another name; the helper checks above still apply
            var worker = def.Worker as IncidentWorker_MassCasualtyEvent;
            Assert.NotNull(worker, "MCE worker type");
            bool mc = hospital.MassCasualties;
            int patientsBefore = hospital.Patients.Count;
            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            ctx.OnCleanup(() => hospital.MassCasualties = mc);
            hospital.MassCasualties = true;
            bool fired = worker.TryExecuteWorker(new IncidentParms { target = map, forced = true, pawnCount = -1 });
            Assert.False(fired, "MCE with a non-positive pawn count must not fire");
            Assert.Equal(patientsBefore, hospital.Patients.Count, "patients created by an empty MCE");
            Assert.Equal(lettersBefore, Find.LetterStack.LettersListForReading.Count, "letter sent by an empty MCE");
        }

        [HospitalTest("A6: silver payment is rounded, never a 0 stack, and split by the stack limit")]
        public static void SilverStacksAreValid(TestContext ctx)
        {
            Assert.Equal(0, HospitalMapComponent.SilverStacks(0f, 100).Count, "0 silver");
            Assert.Equal(0, HospitalMapComponent.SilverStacks(0.4f, 100).Count, "0.4 silver must not produce a 0 stack");
            var one = HospitalMapComponent.SilverStacks(1.2f, 100);
            Assert.True(one.Count == 1 && one[0] == 1, "1.2 silver -> one stack of 1");
            var big = HospitalMapComponent.SilverStacks(250f, 100);
            Assert.True(big.Count == 3 && big[0] == 100 && big[1] == 100 && big[2] == 50, "250 with limit 100 -> 100,100,50");
            Assert.Equal(4000, HospitalMapComponent.SilverStacks(4000f, ThingDefOf.Silver.stackLimit).Sum(), "total preserved");
            Assert.Equal(5, HospitalMapComponent.SilverStacks(5f, 0).Sum(), "stack limit 0 must not hang or lose silver");
        }

        [HospitalTest("A16: wound damage budget is at least 1 even with a tiny severity multiplier")]
        public static void WoundDamageNeverZero(TestContext ctx)
        {
            Assert.True(WoundsUtility.RollDamage(0f, 0.01f) >= 1, "5 * 0.01 truncated to 0");
            Assert.True(WoundsUtility.RollDamage(0f, 0.1f) >= 1, "5 * 0.1 truncated to 0");
            Assert.Equal(50, WoundsUtility.RollDamage(1f, 1f), "full roll, multiplier 1");
            Assert.Equal(100, WoundsUtility.RollDamage(1f, 2f), "full roll, multiplier 2");
        }

        [HospitalTest("B9: IsPatientWork handles a null workType and still accepts Patient / PatientBedRest")]
        public static void PatientWorkNullWorkType(TestContext ctx)
        {
            Assert.False(Hospital.Patches.JobGiver_Work_Patch.PawnCanUseWorkGiver.IsPatientWork(null), "null workType");
            var patient = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Patient");
            if (patient != null) Assert.True(Hospital.Patches.JobGiver_Work_Patch.PawnCanUseWorkGiver.IsPatientWork(patient), "Patient work type");
            Assert.False(Hospital.Patches.JobGiver_Work_Patch.PawnCanUseWorkGiver.IsPatientWork(WorkTypeDefOf.Cleaning), "Cleaning is not patient work");
        }
    }
}
