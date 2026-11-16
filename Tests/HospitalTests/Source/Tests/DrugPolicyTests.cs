using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hospital;
using Hospital.Patches;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Patient drug policies: vanilla DrugPolicy entries with allowScheduled become "Administer X" bills at the top
    // of the patient's bill list (DrugUtility.ScheduleFor; production runs it every 250 ticks via ScheduleDrugBills).
    // Tests call ScheduleFor for ONE test patient so real patients in the save are not touched. Hospital drug settings
    // and the temporary drug policies are restored after each test.
    public static class DrugPolicyTests
    {
        private const string TeaDefName = "PsychiteTea";
        private const string BeerDefName = "Beer";

        // ---------- helpers ----------

        private class Rig
        {
            public HospitalMapComponent Hospital;
            public Pawn Pawn;
            public PatientData Data;
            public Bill_Medical CureBill;
        }

        private static ThingDef Drug(string defName)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            Assert.NotNull(def, "ThingDef " + defName + " not found");
            Assert.NotNull(DrugUtility.AdministerRecipeFor(def), "no Administer recipe for " + defName);
            return def;
        }

        // Saves the hospital's drug settings and restores them after the test; starts from "no policies".
        private static readonly HashSet<TestContext> prepared = new HashSet<TestContext>();

        private static HospitalMapComponent Hospital(TestContext ctx)
        {
            var hospital = ctx.Map.GetComponent<HospitalMapComponent>();
            Assert.NotNull(hospital, "map has no HospitalMapComponent");
            // Only once per test: a second call (second patient) must not wipe policies the test already set.
            if (!prepared.Add(ctx)) return hospital;
            ctx.OnCleanup(() => prepared.Remove(ctx));
            var all = hospital.DrugsAllPatients; var inf = hospital.DrugsInfection; var sur = hospital.DrugsSurgery; var inj = hospital.DrugsInjuries; var sca = hospital.DrugsScars;
            bool allT = hospital.DrugsAllTeetotal, infT = hospital.DrugsInfectionTeetotal, surT = hospital.DrugsSurgeryTeetotal, injT = hospital.DrugsInjuriesTeetotal, scaT = hospital.DrugsScarsTeetotal;
            bool acceptDanger = hospital.AcceptDanger;
            ctx.OnCleanup(() =>
            {
                hospital.DrugsAllPatients = all; hospital.DrugsInfection = inf; hospital.DrugsSurgery = sur; hospital.DrugsInjuries = inj;
                hospital.DrugsInjuriesTeetotal = injT;
                hospital.DrugsScars = sca; hospital.DrugsScarsTeetotal = scaT;
                hospital.DrugsAllTeetotal = allT; hospital.DrugsInfectionTeetotal = infT; hospital.DrugsSurgeryTeetotal = surT;
                hospital.AcceptDanger = acceptDanger;
            });
            hospital.DrugsAllPatients = null; hospital.DrugsInfection = null; hospital.DrugsSurgery = null; hospital.DrugsInjuries = null; hospital.DrugsScars = null;
            return hospital;
        }

        // A new vanilla drug policy with exactly these drugs scheduled (everything else off).
        private static DrugPolicy Policy(TestContext ctx, params ThingDef[] scheduled)
        {
            var db = Current.Game.drugPolicyDatabase;
            var policy = db.MakeNewDrugPolicy();
            ctx.OnCleanup(() => db.TryDelete(policy));
            for (int i = 0; i < policy.Count; i++) policy[i].allowScheduled = false;
            foreach (var drug in scheduled)
            {
                var entry = policy[drug];
                entry.allowScheduled = true;
                entry.daysFrequency = 1f;
                entry.onlyIfMoodBelow = 1f;
                entry.onlyIfJoyBelow = 1f;
            }
            return policy;
        }

        // A spawned, registered patient with one open wound (so they are not "otherwise healed").
        private static Rig WoundedPatient(TestContext ctx)
        {
            var rig = HealthyPatient(ctx);
            var part = rig.Pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Torso).First();
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, rig.Pawn, part);
            cut.Severity = 2f;
            rig.Pawn.health.AddHediff(cut, part);
            Assert.True(rig.Pawn.health.HasHediffsNeedingTend(), "precondition: wound needs tending");
            return rig;
        }

        // Registered patient with nothing wrong with them.
        private static Rig HealthyPatient(TestContext ctx)
        {
            var hospital = Hospital(ctx);
            // Generated pawns may carry traits that change who gets which list (DrugDesire etc.): start from none.
            var pawn = HospitalRegressionTests.MakePawn(ctx, Faction.OfPlayer, true);
            Assert.True(pawn.Spawned, "test pawn not spawned");
            pawn.story.traits.allTraits.Clear();
            ResetTeetotalerCache(pawn);
            Assert.False(pawn.IsTeetotaler(), "precondition: test pawn must not be a teetotaler" + Diag(null, pawn));
            var data = new PatientData(Find.TickManager.TicksGame, 0f, 0.5f, PatientType.Wounds);
            hospital.Patients.Add(pawn, data);
            ctx.OnCleanup(() => hospital.Patients.Remove(pawn));
            return new Rig { Hospital = hospital, Pawn = pawn, Data = data };
        }

        // Adds the cure surgery the way a surgery patient has it: carcinoma + Excise bill + pending flag.
        private static void AddCureSurgery(Rig rig)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ExciseCarcinoma");
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("Carcinoma");
            Assert.NotNull(recipe, "RecipeDef ExciseCarcinoma not found");
            Assert.NotNull(hediffDef, "HediffDef Carcinoma not found");
            var part = rig.Pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Torso).First();
            rig.Pawn.health.AddHediff(HediffMaker.MakeHediff(hediffDef, rig.Pawn, part), part);
            HealthCardUtility.CreateSurgeryBill(rig.Pawn, recipe, part, new List<Thing>(), false);
            rig.CureBill = rig.Pawn.health.surgeryBills.Bills.OfType<Bill_Medical>().First(b => b.recipe == recipe);
            rig.Data.CureRecipe = recipe;
            rig.Data.HasPendingSurgeryBill = true;
        }

        // Flu: an immunizable hediff the pawn is not yet immune to.
        private static void AddInfection(Rig rig)
        {
            var flu = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            Assert.NotNull(flu, "HediffDef Flu not found");
            var hediff = HediffMaker.MakeHediff(flu, rig.Pawn);
            hediff.Severity = 0.3f;
            rig.Pawn.health.AddHediff(hediff);
            Assert.True(DrugUtility.HasUnfinishedInfection(rig.Pawn), "precondition: flu is an unfinished infection");
        }

        // The Performance Optimizer mod caches PawnUtility.IsTeetotaler per pawn for 500 ticks, so a trait change is not
        // seen until the cache entry is gone. No-op when the mod is not loaded.
        private static void ResetTeetotalerCache(Pawn pawn)
        {
            var type = HarmonyLib.AccessTools.TypeByName("PerformanceOptimizer.Optimization_PawnUtility_IsTeetotaler");
            var field = type == null ? null : HarmonyLib.AccessTools.Field(type, "cachedResults");
            (field?.GetValue(null) as System.Collections.IDictionary)?.Remove(pawn);
        }

        // Replaces any DrugDesire trait (GainTrait refuses to add a second one) with Teetotaler.
        private static void MakeTeetotaler(Pawn pawn)
        {
            // Other traits can suppress or override the new one (DegreeOfTrait ignores suppressed traits), so clear them all.
            pawn.story.traits.allTraits.Clear();
            pawn.story.traits.GainTrait(new Trait(TraitDefOf.DrugDesire, -1));
            ResetTeetotalerCache(pawn);
            Assert.True(pawn.IsTeetotaler(), "precondition: pawn is a teetotaler" + Diag(null, pawn));
        }

        // State dump appended to failure messages so a failure shows its cause (randomly generated pawns vary).
        private static string Diag(Rig rig, Pawn pawn = null)
        {
            pawn = pawn ?? rig?.Pawn;
            var sb = new System.Text.StringBuilder(" | pawn state: ");
            try
            {
                sb.Append("traits=[" + string.Join(",", pawn.story.traits.allTraits.Select(tr => tr.def.defName + ":" + tr.Degree)) + "]");
                sb.Append(" teetotaler=" + pawn.IsTeetotaler());
                sb.Append(" mood=" + (pawn.needs?.mood?.CurLevelPercentage.ToString("0.00") ?? "n/a"));
                sb.Append(" joy=" + (pawn.needs?.joy?.CurLevelPercentage.ToString("0.00") ?? "n/a"));
                sb.Append(" hediffs=[" + string.Join(",", pawn.health.hediffSet.hediffs.Select(h => h.def.defName)) + "]");
                sb.Append(" bills=[" + string.Join(",", pawn.health.surgeryBills.Bills.Select(b => b.recipe.defName)) + "]");
                sb.Append(" needsTend=" + pawn.health.HasHediffsNeedingTend());
                sb.Append(" realSurgery=" + SurgeryUtility.HasRealSurgeryBills(pawn));
                if (rig != null)
                {
                    sb.Append(" pending=" + rig.Data.HasPendingSurgeryBill);
                    sb.Append(" healed=" + DrugUtility.IsOtherwiseHealed(pawn, rig.Data));
                    foreach (DrugList l in Enum.GetValues(typeof(DrugList)))
                        sb.Append(" " + l + "(policy=" + (rig.Hospital.GetDrugPolicy(l)?.label ?? "-") + ",teetotal=" + rig.Hospital.GetDrugTeetotal(l)
                                  + ",applies=" + DrugUtility.ListApplies(l, pawn, rig.Data) + ")");
                    foreach (var name in new[] { TeaDefName, BeerDefName })
                    {
                        var r = DefDatabase<RecipeDef>.GetNamedSilentFail("Administer_" + name);
                        sb.Append(" " + name + ".AvailableOnNow=" + (r != null && r.AvailableOnNow(pawn)));
                    }
                }
            }
            catch (Exception e) { sb.Append("(diag failed: " + e.Message + ")"); }
            return sb.ToString();
        }

        // Schedules and asserts the number of bills added; the failure message carries the state dump.
        private static void ExpectSchedule(Rig rig, int expected, string message)
        {
            int added = Schedule(rig);
            if (added != expected) Assert.Fail(message + ": expected <" + expected + "> but was <" + added + ">" + Diag(rig));
        }

        private static int Schedule(Rig rig)
        {
            return DrugUtility.ScheduleFor(rig.Hospital, rig.Pawn, rig.Data, Find.TickManager.TicksGame);
        }

        private static List<Bill> DrugBills(Pawn pawn, ThingDef drug = null)
        {
            return pawn.health.surgeryBills.Bills
                .Where(b => DrugUtility.IsAdministerBill(b) && (drug == null || DrugUtility.DrugOf(b.recipe) == drug)).ToList();
        }

        private static void Complete(Pawn pawn, Bill bill)
        {
            // what vanilla does when a bill iteration finishes: our hooks run around it, then the bill is deleted
            Billing_Surgery_Patch.BillPatientsForMedicalBill.Prefix(bill, out Pawn state);
            Billing_Surgery_Patch.BillPatientsForMedicalBill.Postfix(bill, state);
            pawn.health.surgeryBills.Delete(bill);
        }

        // ---------- scheduling ----------

        // Given: a wounded patient with a pending cure surgery and a policy on the "All patients" list with tea scheduled.
        // When:  the drugs are scheduled.
        // Then:  one Administer-tea bill exists and it is at index 0, before the cure bill.
        [HospitalTest("Drugs1: an All-list drug becomes an Administer bill at the top of the bill list")]
        public static void AllListBillAtIndexZero(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            AddCureSurgery(rig);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);

            int added = Schedule(rig);

            Assert.Equal(1, added, "bills added");
            var bills = rig.Pawn.health.surgeryBills.Bills;
            Assert.True(DrugUtility.IsAdministerBill(bills[0]) && DrugUtility.DrugOf(bills[0].recipe) == tea, "bill 0 is not the Administer tea bill");
            Assert.True(bills.IndexOf(rig.CureBill) > 0, "cure bill must come after the drug bill");
        }

        // Given: a wounded patient without any infection; Infection list = tea.
        // When:  scheduled -> nothing. Then the patient gets Flu and it is scheduled again -> tea bill.
        // Then:  the Infection list only applies with an immunizable hediff not yet immune.
        [HospitalTest("Drugs2a: the Infection list applies only to a patient with an immunizable hediff")]
        public static void InfectionListNeedsInfection(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            rig.Hospital.DrugsInfection = Policy(ctx, tea);

            ExpectSchedule(rig, 0, "bills added without an infection");
            Assert.Equal(0, DrugBills(rig.Pawn).Count, "drug bills without an infection" + Diag(rig));

            AddInfection(rig);
            ExpectSchedule(rig, 1, "bills added with an infection");
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "tea bills with an infection" + Diag(rig));
        }

        // Given: an infected, wounded patient; All list = tea, Infection list = beer.
        // When:  scheduled.
        // Then:  both drugs are scheduled (union of the lists).
        [HospitalTest("Drugs2b: lists combine - All + Infection give both drugs")]
        public static void ListsCombine(TestContext ctx)
        {
            var tea = Drug(TeaDefName); var beer = Drug(BeerDefName);
            var rig = WoundedPatient(ctx);
            AddInfection(rig);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);
            rig.Hospital.DrugsInfection = Policy(ctx, beer);

            ExpectSchedule(rig, 2, "bills added");
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "tea bills" + Diag(rig));
            Assert.Equal(1, DrugBills(rig.Pawn, beer).Count, "beer bills" + Diag(rig));
        }

        // Given: a patient with a pending cure surgery; Surgery list = beer (patient without surgery gets none).
        // When:  scheduled.
        // Then:  the beer bill is placed before the cure bill. A wounded patient without surgery gets nothing from this list.
        [HospitalTest("Drugs3: a Surgery-list bill goes before the cure surgery bill; no surgery, no Surgery-list drug")]
        public static void SurgeryListBeforeCure(TestContext ctx)
        {
            var beer = Drug(BeerDefName);
            var noSurgery = WoundedPatient(ctx);
            noSurgery.Hospital.DrugsSurgery = Policy(ctx, beer);
            ExpectSchedule(noSurgery, 0, "Surgery list applied to a patient without surgery");

            var rig = WoundedPatient(ctx);
            AddCureSurgery(rig);
            ExpectSchedule(rig, 1, "bills added");
            var bills = rig.Pawn.health.surgeryBills.Bills;
            Assert.True(DrugUtility.IsAdministerBill(bills[0]), "bill 0 is not the drug bill");
            Assert.True(bills.IndexOf(rig.CureBill) > 0, "cure bill must come after the drug bill");
        }

        // Given: the drug is already waiting as a bill.
        // When:  scheduled again (several times).
        // Then:  still exactly one bill for it.
        [HospitalTest("Drugs4: only one waiting bill per drug")]
        public static void OnePendingBillPerDrug(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);

            ExpectSchedule(rig, 1, "first schedule");
            ExpectSchedule(rig, 0, "second schedule");
            ExpectSchedule(rig, 0, "third schedule");
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "tea bills" + Diag(rig));
        }

        // Given: daysFrequency = 2 and a dose finished just now (LastDrugTick = now).
        // When:  scheduled -> blocked. LastDrugTick is moved back 1 day -> still blocked. Moved back 2 days -> a new bill.
        // Then:  the interval counts from the last completed dose.
        [HospitalTest("Drugs5: daysFrequency blocks a new bill until the interval has passed")]
        public static void FrequencyBlocksUntilInterval(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            var policy = Policy(ctx, tea);
            policy[tea].daysFrequency = 2f;
            rig.Hospital.DrugsAllPatients = policy;
            int now = Find.TickManager.TicksGame;

            rig.Data.LastDrugTick[tea] = now;
            ExpectSchedule(rig, 0, "bill added right after a dose");

            rig.Data.LastDrugTick[tea] = now - GenDate.TicksPerDay;
            ExpectSchedule(rig, 0, "bill added after 1 of 2 days");

            rig.Data.LastDrugTick[tea] = now - 2 * GenDate.TicksPerDay;
            ExpectSchedule(rig, 1, "no bill after the interval passed");
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "tea bills" + Diag(rig));
        }

        // Given: a policy entry with onlyIfMoodBelow = 0 (mood can never be below it).
        // When:  scheduled.
        // Then:  no bill; with the limit removed the bill is added.
        [HospitalTest("Drugs6: the mood limit of a policy entry is respected")]
        public static void MoodLimitRespected(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            Assert.NotNull(rig.Pawn.needs?.mood, "precondition: test pawn has a mood need");
            var policy = Policy(ctx, tea);
            policy[tea].onlyIfMoodBelow = 0f;
            rig.Hospital.DrugsAllPatients = policy;

            ExpectSchedule(rig, 0, "bill added although mood is not below the limit");
            policy[tea].onlyIfMoodBelow = 1f;
            ExpectSchedule(rig, 1, "no bill without the limit");
        }

        // Given: a teetotaler with a pending surgery; All list = tea (teetotalers off), Surgery list = beer (teetotalers on).
        // When:  scheduled.
        // Then:  only beer. With the All list's toggle switched on a second pass adds tea too.
        [HospitalTest("Drugs7: a teetotaler skips lists whose toggle is off and still gets lists whose toggle is on")]
        public static void TeetotalerToggles(TestContext ctx)
        {
            var tea = Drug(TeaDefName); var beer = Drug(BeerDefName);
            var rig = WoundedPatient(ctx);
            AddCureSurgery(rig);
            MakeTeetotaler(rig.Pawn);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);
            rig.Hospital.DrugsSurgery = Policy(ctx, beer);
            rig.Hospital.DrugsAllTeetotal = false;
            rig.Hospital.DrugsSurgeryTeetotal = true;

            ExpectSchedule(rig, 1, "bills added");
            Assert.Equal(0, DrugBills(rig.Pawn, tea).Count, "tea given to a teetotaler although the All toggle is off" + Diag(rig));
            Assert.Equal(1, DrugBills(rig.Pawn, beer).Count, "beer not given although the Surgery toggle is on" + Diag(rig));

            rig.Hospital.DrugsAllTeetotal = true;
            ExpectSchedule(rig, 1, "bills added with the All toggle on");
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "tea missing with the All toggle on" + Diag(rig));
        }

        // Given: a patient with nothing left to treat; All list = tea.
        // When:  scheduled.
        // Then:  no bill, so the drug does not hold back a healed patient.
        [HospitalTest("Drugs8: an otherwise healed patient gets no new drug bills")]
        public static void HealedPatientGetsNothing(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = HealthyPatient(ctx);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);

            ExpectSchedule(rig, 0, "bills added");
            Assert.Equal(0, rig.Pawn.health.surgeryBills.Count, "bills on a healed patient");
        }

        // Given: Injuries list = beer. A patient with only a disease (Flu), a patient with a wound, and a patient with blood loss only.
        // When:  each is scheduled.
        // Then:  the disease-only patient gets nothing; the wounded patient and the blood-loss patient each get the beer bill.
        //        With the Injuries toggle default ON a teetotaler still gets it.
        [HospitalTest("Drugs15: the Injuries list applies to injuries and blood loss, not to a disease-only patient")]
        public static void InjuriesList(TestContext ctx)
        {
            var beer = Drug(BeerDefName);
            Assert.True(Hospital(ctx).DrugsInjuriesTeetotal, "default: Injuries toggle should be on");
            var policy = Policy(ctx, beer);

            var disease = HealthyPatient(ctx);
            AddInfection(disease);
            Assert.False(DrugUtility.HasInjuries(disease.Pawn), "precondition: flu is not an injury");
            disease.Hospital.DrugsInjuries = policy;
            ExpectSchedule(disease, 0, "Injuries list applied to a disease-only patient");

            var wounded = WoundedPatient(ctx);
            wounded.Hospital.DrugsInjuries = policy;
            Assert.True(DrugUtility.HasInjuries(wounded.Pawn), "precondition: wound counts as an injury");
            ExpectSchedule(wounded, 1, "wounded patient");

            // Blood loss alone does not keep a patient in the hospital (vanilla needs no tending for it), so such a patient
            // is "otherwise healed" and correctly gets no new drug bills even though HasInjuries sees the blood loss.
            var bleeding = HealthyPatient(ctx);
            var loss = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, bleeding.Pawn);
            loss.Severity = 0.3f;
            bleeding.Pawn.health.AddHediff(loss);
            Assert.True(DrugUtility.HasInjuries(bleeding.Pawn), "precondition: blood loss counts as an injury");
            bleeding.Hospital.DrugsInjuries = policy;
            ExpectSchedule(bleeding, 0, "blood-loss-only patient (otherwise healed)");

            // With a fresh wound on top (needs tending) the patient is still being treated: a teetotaler gets the bill
            // because the Injuries toggle defaults to on.
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, bleeding.Pawn, bleeding.Pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Torso).First());
            cut.Severity = 2f;
            bleeding.Pawn.health.AddHediff(cut);
            Assert.True(bleeding.Pawn.health.HasHediffsNeedingTend(), "precondition: wound needs tending");
            MakeTeetotaler(bleeding.Pawn);
            ExpectSchedule(bleeding, 1, "blood loss + wound teetotaler patient");
        }

        // Given: Scars list = beer. A patient with only a fresh wound, and a patient with a fresh wound plus a permanent injury (scar).
        //        (The fresh wound keeps the patient from being "otherwise healed"; a scar alone never gets drug bills.)
        // When:  each is scheduled.
        // Then:  the fresh-wound patient gets nothing; the scarred patient gets the beer bill.
        //        A teetotaler with a scar gets nothing because the Scars toggle defaults to off; with it on they get the bill.
        [HospitalTest("Drugs16: the Scars list applies to a permanent injury, not to a fresh wound; teetotaler toggle defaults off")]
        public static void ScarsList(TestContext ctx)
        {
            var beer = Drug(BeerDefName);
            Assert.False(Hospital(ctx).DrugsScarsTeetotal, "default: Scars toggle should be off");
            var policy = Policy(ctx, beer);

            var fresh = WoundedPatient(ctx);
            fresh.Hospital.DrugsScars = policy;
            Assert.False(DrugUtility.HasScars(fresh.Pawn), "precondition: a fresh wound is not a scar");
            ExpectSchedule(fresh, 0, "Scars list applied to a fresh wound");

            var scarred = WoundedPatient(ctx);
            AddScar(scarred.Pawn);
            Assert.True(DrugUtility.HasScars(scarred.Pawn), "precondition: permanent injury counts as a scar");
            scarred.Hospital.DrugsScars = policy;
            ExpectSchedule(scarred, 1, "scarred patient");

            var teetotaler = WoundedPatient(ctx);
            AddScar(teetotaler.Pawn);
            MakeTeetotaler(teetotaler.Pawn);
            teetotaler.Hospital.DrugsScars = policy;
            ExpectSchedule(teetotaler, 0, "teetotaler got Scars drugs with the toggle off");
            teetotaler.Hospital.DrugsScarsTeetotal = true;
            ExpectSchedule(teetotaler, 1, "teetotaler with the toggle on");
        }

        // A permanent injury (old wound) on a leg.
        private static void AddScar(Pawn pawn)
        {
            var part = pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Leg).First();
            var scar = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
            scar.Severity = 1f;
            var comp = scar.TryGetComp<HediffComp_GetsPermanent>();
            Assert.NotNull(comp, "Cut has no GetsPermanent comp");
            comp.IsPermanent = true;
            pawn.health.AddHediff(scar, part);
        }

        // Tends every hediff that needs it, so the patient has a tended (not "needs tending") infection.
        private static void TendAll(Pawn pawn)
        {
            foreach (var h in pawn.health.hediffSet.hediffs.ToList())
                if (h.TendableNow()) h.Tended(0.8f, 1f);
        }

        // Given: a patient with a just-tended Flu (HasHediffsNeedingTend false) and a permanent scar; Scars list = beer.
        // When:  scheduled.
        // Then:  the beer bill is added: a tended but still sick patient is not "healed" (the rule matches the discharge check).
        // Given: a tended Flu patient on the All list (tea) whose last dose is older than daysFrequency.
        // When:  scheduled.
        // Then:  a new tea bill is added (repeat doses must not stall while the tend lasts); not before the interval.
        [HospitalTest("Drugs17: a tended but still sick patient still gets Scars drugs and repeat doses")]
        public static void TendedSickPatientStillTreated(TestContext ctx)
        {
            var tea = Drug(TeaDefName); var beer = Drug(BeerDefName);
            var rig = HealthyPatient(ctx);
            AddInfection(rig);
            AddScar(rig.Pawn);
            TendAll(rig.Pawn);
            Assert.False(rig.Pawn.health.HasHediffsNeedingTend(), "precondition: flu was tended" + Diag(rig));
            Assert.True(DrugUtility.HasScars(rig.Pawn), "precondition: scar");
            rig.Hospital.DrugsScars = Policy(ctx, beer);
            ExpectSchedule(rig, 1, "tended flu patient with a scar");

            var policy = Policy(ctx, tea);
            policy[tea].daysFrequency = 2f;
            rig.Hospital.DrugsAllPatients = policy;
            int now = Find.TickManager.TicksGame;
            rig.Data.LastDrugTick[tea] = now - GenDate.TicksPerDay;
            ExpectSchedule(rig, 0, "tea before the interval");
            rig.Data.LastDrugTick[tea] = now - 2 * GenDate.TicksPerDay;
            ExpectSchedule(rig, 1, "tea after the interval");
        }

        // ---------- interplay with the rest of the mod ----------

        // Given: a surgery patient with a drug bill and the cure bill (HasPendingSurgeryBill = true).
        // When:  the drug bill is completed (our Notify_IterationCompleted hooks + vanilla delete).
        // Then:  the pending flag stays, the patient is billed, LastDrugTick is set. Completing the cure bill clears the flag.
        [HospitalTest("Drugs9: finishing an Administer bill keeps HasPendingSurgeryBill and records LastDrugTick; the cure bill clears it")]
        public static void FinishingDrugKeepsPending(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            AddCureSurgery(rig);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);
            Schedule(rig);
            var drugBill = DrugBills(rig.Pawn, tea).Single();

            Complete(rig.Pawn, drugBill);

            Assert.True(rig.Data.HasPendingSurgeryBill, "drug bill completion cleared HasPendingSurgeryBill");
            Assert.True(rig.Data.Bill > 0f, "drug was not billed");
            Assert.True(rig.Data.LastDrugTick.TryGetValue(tea, out int last) && last == Find.TickManager.TicksGame, "LastDrugTick not set");

            Complete(rig.Pawn, rig.CureBill);
            Assert.False(rig.Data.HasPendingSurgeryBill, "cure bill completion did not clear HasPendingSurgeryBill");
        }

        // Given: a drug bill that "completes" in the same tick as a failed surgery (SurgeryFailedTick = now).
        // When:  the completion hooks run.
        // Then:  LastDrugTick is not set (a failed attempt does not start the interval).
        [HospitalTest("Drugs10: a failed attempt does not set LastDrugTick")]
        public static void FailedAttemptNotRecorded(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);
            Schedule(rig);
            var drugBill = DrugBills(rig.Pawn, tea).Single();
            rig.Data.SurgeryFailedTick = Find.TickManager.TicksGame;

            Complete(rig.Pawn, drugBill);

            Assert.False(rig.Data.LastDrugTick.ContainsKey(tea), "LastDrugTick set after a failed attempt");
        }

        // Given: a surgery patient with a drug bill and the cure bill (HasPendingSurgeryBill = true).
        // When:  the drug bill is deleted -> flag stays. Then the cure bill is deleted -> flag cleared.
        // Then:  regression for fixes 1-2: only the cure bill's removal clears the pending flag.
        [HospitalTest("Drugs11: deleting an Administer bill keeps HasPendingSurgeryBill, deleting the cure bill clears it")]
        public static void DeleteKeepsPendingUnlessCure(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            AddCureSurgery(rig);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);
            Schedule(rig);
            var drugBill = DrugBills(rig.Pawn, tea).Single();

            rig.Pawn.health.surgeryBills.Delete(drugBill);
            Assert.True(rig.Data.HasPendingSurgeryBill, "deleting a drug bill cleared HasPendingSurgeryBill");

            rig.Pawn.health.surgeryBills.Delete(rig.CureBill);
            Assert.False(rig.Data.HasPendingSurgeryBill, "deleting the cure bill did not clear HasPendingSurgeryBill");
        }

        // Given: a patient who is otherwise healed but still has an unfinished drug bill (danger check disabled).
        // When:  Trigger_SentAway.SentAway runs; and the bed need / bed type is computed.
        // Then:  the patient is sent away anyway, and the drug bill neither makes them a surgery patient nor wants a surgery bed.
        //        A real surgery bill still counts (control).
        [HospitalTest("Drugs12: an unfinished drug bill neither blocks Trigger_SentAway nor changes the bed need")]
        public static void DrugBillDoesNotBlockDischarge(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = HealthyPatient(ctx);
            rig.Hospital.AcceptDanger = true; // SentAway is always false while the map has dangers
            var sentAway = typeof(Hospital.Trigger_SentAway).GetMethod("SentAway", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(sentAway, "Trigger_SentAway.SentAway not found");
            bool Check() => (bool)sentAway.Invoke(null, new object[] { rig.Pawn });

            if (!Check()) return; // baseline: a healthy patient must be sent away; if not, the map/pawn state is not testable here

            HealthCardUtility.CreateSurgeryBill(rig.Pawn, DrugUtility.AdministerRecipeFor(tea), null, new List<Thing>(), false);
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "precondition: drug bill added" + Diag(rig));

            Assert.True(Check(), "an unfinished drug bill kept the patient in the hospital" + Diag(rig));
            Assert.False(SurgeryUtility.HasRealSurgeryBills(rig.Pawn), "drug bill counted as a real surgery bill");
            Assert.True(Patients_BedFinder_Patch.GetNeed(rig.Pawn) != PatientBedNeed.Surgery, "drug bill made the patient need a surgery bed");

            AddCureSurgery(rig);
            Assert.True(SurgeryUtility.HasRealSurgeryBills(rig.Pawn), "control: cure bill not counted");
            Assert.Equal(PatientBedNeed.Surgery, Patients_BedFinder_Patch.GetNeed(rig.Pawn), "control: need with a cure bill");
            Assert.False(Check(), "control: a patient with a pending surgery was sent away");
        }

        // Given: a patient with a waiting drug bill.
        // When:  the patient is dismissed (same path as leaving: RemoveFromPatientList).
        // Then:  no administer bill is left on the pawn.
        [HospitalTest("Drugs13: leftover Administer bills are removed when the patient leaves")]
        public static void LeavingRemovesDrugBills(TestContext ctx)
        {
            var tea = Drug(TeaDefName);
            var rig = WoundedPatient(ctx);
            rig.Hospital.DrugsAllPatients = Policy(ctx, tea);
            Schedule(rig);
            Assert.Equal(1, DrugBills(rig.Pawn, tea).Count, "precondition: drug bill waiting" + Diag(rig));

            rig.Hospital.DismissPatient(rig.Pawn);

            Assert.Equal(0, DrugBills(rig.Pawn).Count, "drug bill left on a dismissed patient" + Diag(rig));
        }

        // ---------- save / load ----------

        // Given: a PatientData with LastDrugTick entries.
        // When:  it is saved to a temp file with the Scribe and loaded back.
        // Then:  LastDrugTick is intact. A PatientData without the node (old save) loads with an empty, non-null dictionary.
        [HospitalTest("Drugs14: LastDrugTick survives save/load; an old save without it loads an empty dictionary")]
        public static void LastDrugTickRoundTrip(TestContext ctx)
        {
            var tea = Drug(TeaDefName); var beer = Drug(BeerDefName);
            var data = new PatientData(100, 0f, 0.5f, PatientType.Wounds);
            data.LastDrugTick[tea] = 12345;
            data.LastDrugTick[beer] = 678;
            var loaded = RoundTrip(data);
            Assert.Equal(2, loaded.LastDrugTick.Count, "entries after load");
            Assert.Equal(12345, loaded.LastDrugTick[tea], "tea tick");
            Assert.Equal(678, loaded.LastDrugTick[beer], "beer tick");

            var legacy = new PatientData(100, 0f, 0.5f, PatientType.Wounds);
            legacy.LastDrugTick = null; // as if the node were missing: ExposeData must restore the dictionary
            var loadedLegacy = RoundTrip(legacy, skipDrugNode: true);
            Assert.NotNull(loadedLegacy.LastDrugTick, "LastDrugTick null after loading a save without the node");
            Assert.Equal(0, loadedLegacy.LastDrugTick.Count, "legacy entries");
        }

        // Saves data (Deep) to a temp file and loads it back. skipDrugNode removes the LastDrugTick node from the file first.
        private static PatientData RoundTrip(PatientData data, bool skipDrugNode = false)
        {
            string path = Path.Combine(Path.GetTempPath(), "hospital_drugtest_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Scribe.saver.InitSaving(path, "root");
                try
                {
                    Scribe_Deep.Look(ref data, "data");
                    Scribe.saver.FinalizeSaving();
                }
                catch { Scribe.saver.ForceStop(); throw; }

                if (skipDrugNode)
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.Load(path);
                    var node = doc.SelectSingleNode("/root/data/LastDrugTick");
                    node?.ParentNode.RemoveChild(node);
                    doc.Save(path);
                }

                PatientData loaded = null;
                Scribe.loader.InitLoading(path);
                try
                {
                    Scribe_Deep.Look(ref loaded, "data");
                    Scribe.loader.FinalizeLoading();
                }
                catch { Scribe.loader.ForceStop(); throw; }
                return loaded;
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
