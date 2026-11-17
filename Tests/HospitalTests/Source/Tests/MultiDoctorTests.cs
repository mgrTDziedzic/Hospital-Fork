using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Multi-Doctor Surgery compat (MultiDoctorCompat): patient surgery bills get MD's default team only when
    // MD's Fast Operation is ON. MD is reached by reflection (no assembly reference), like the main mod does.
    // MD1-MD5 are skipped (RequiresMod) when MD is not active; MD0 runs always.
    // The tests change MD's DefaultSurgeryTeamComponent (a world component) and restore it in cleanup.
    public static class MultiDoctorTests
    {
        private const string MdId = global::Hospital.Compat.SupportedMods.MultiDoctor;
        private const string BillExName = "MultiDoctorSurgery.BillMedicalEx";

        // ---------- helpers ----------

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool MdLoaded()
        {
            return global::Hospital.Compat.SupportedMods.IsActive(MdId) && FindType("MultiDoctorSurgery.DefaultSurgeryTeamComponent") != null;
        }

        // MD's world component; its current state is restored when the test ends.
        private static object TeamComponent(TestContext ctx)
        {
            var type = FindType("MultiDoctorSurgery.DefaultSurgeryTeamComponent");
            var comp = Find.World.GetComponent(type);
            Assert.NotNull(comp, "MD DefaultSurgeryTeamComponent missing on the world");
            var lead = Get(comp, "defaultLeadSurgeon");
            var assistants = new List<Pawn>(((IEnumerable)Get(comp, "defaultAssistants")).OfType<Pawn>());
            var fast = Get(comp, "fastOperationEnabled");
            ctx.OnCleanup(() =>
            {
                Set(comp, "defaultLeadSurgeon", lead);
                Set(comp, "defaultAssistants", assistants);
                Set(comp, "fastOperationEnabled", fast);
            });
            return comp;
        }

        private static object Get(object o, string field) { return o.GetType().GetField(field).GetValue(o); }
        private static void Set(object o, string field, object value) { o.GetType().GetField(field).SetValue(o, value); }

        private static void SetTeam(object comp, Pawn lead, IEnumerable<Pawn> assistants, bool fast)
        {
            Set(comp, "defaultLeadSurgeon", lead);
            Set(comp, "defaultAssistants", assistants.ToList());
            Set(comp, "fastOperationEnabled", fast);
        }

        // A spawned colonist that can really work as a doctor (Doctor work type not disabled by backstory or traits,
        // Manipulation, Medicine skill usable), with Doctor priority 1 and the given Medicine level.
        // Generation is random, so it retries a few times; fails the precondition if no suitable pawn comes out.
        private static Pawn Doctor(TestContext ctx, int medicine)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var p = VanillaCompatTests.Colonist(ctx);
                p.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                var skill = p.skills.GetSkill(SkillDefOf.Medicine);
                if (p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) || skill.TotallyDisabled
                    || !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                p.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
                skill.Level = medicine;
                Assert.True(p.workSettings.WorkIsActive(WorkTypeDefOf.Doctor), "precondition: Doctor work type is not active on the test doctor");
                return p;
            }
            Assert.Fail("precondition: could not generate a colonist able to do the Doctor work type in 10 attempts");
            return null;
        }

        // Calls MD's own checks by reflection so a failure names the exact reason.
        private static void AssertCanOperate(Pawn lead, Pawn patient, RecipeDef recipe)
        {
            var util = FindType("MultiDoctorSurgery.SurgeryTeamUtility");
            Assert.NotNull(util, "MD SurgeryTeamUtility not found");
            bool Call(string name, params object[] args)
            {
                var m = util.GetMethods().FirstOrDefault(x => x.Name == name && x.GetParameters().Length >= args.Length);
                Assert.NotNull(m, "MD SurgeryTeamUtility." + name + " not found");
                var full = new object[m.GetParameters().Length];
                for (int k = 0; k < full.Length; k++) full[k] = k < args.Length ? args[k] : m.GetParameters()[k].DefaultValue;
                return (bool)m.Invoke(null, full);
            }
            Assert.True(lead.Spawned && lead.Map == patient.Map, "precondition: lead and patient are not on the same map");
            Assert.True(Call("IsDoctor", lead, patient.Map), "precondition: MD IsDoctor(lead) is false (not spawned/downed/drafted/not player faction/Doctor off/no Manipulation)");
            Assert.True(lead.CanReach(patient, Verse.AI.PathEndMode.Touch, Danger.Deadly), "precondition: lead cannot reach the patient");
            Assert.True(Call("CanAssist", lead, patient), "precondition: MD CanAssist(lead, patient) is false");
            Assert.True(recipe.PawnSatisfiesSkillRequirements(lead), "precondition: lead does not satisfy the recipe's skill requirements");
            Assert.True(Call("CanOperate", lead, patient, recipe), "precondition: MD CanOperate(lead, patient, recipe) is false");
        }

        private static RecipeDef DentureRecipe()
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("InstallDenture");
            Assert.NotNull(recipe, "RecipeDef InstallDenture not found");
            return recipe;
        }

        private static BodyPartRecord Jaw(Pawn patient)
        {
            var jaw = patient.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName == "Jaw");
            Assert.NotNull(jaw, "test pawn has no jaw");
            return jaw;
        }

        private static Bill_Medical Create(TestContext ctx, Pawn patient, RecipeDef recipe, BodyPartRecord part)
        {
            var bill = SurgeryUtility.CreatePatientBill(patient, recipe, part);
            ctx.OnCleanup(() => { if (patient.health.surgeryBills.Bills.Contains(bill)) patient.health.surgeryBills.Delete(bill); });
            Assert.True(patient.health.surgeryBills.Bills.Contains(bill), "bill is not on the patient");
            return bill;
        }

        private static void AssertPlain(Bill_Medical bill, string what)
        {
            Assert.True(bill.GetType() == typeof(Bill_Medical), what + ": bill type is " + bill.GetType().FullName + ", expected plain Bill_Medical");
        }

        // ---------- tests ----------

        // Given: no default team in play (MD absent, or Fast Operation off).
        // When:  a patient bill is created. Then: exactly a Bill_Medical with the part set, on the patient's stack.
        [HospitalTest("MD0: with no active default team the patient bill is a plain Bill_Medical with its part")]
        public static void PlainWithoutTeam(TestContext ctx)
        {
            if (MdLoaded()) Set(TeamComponent(ctx), "fastOperationEnabled", false);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            VanillaCompatTests.Register(ctx, patient);
            var jaw = Jaw(patient);

            var bill = Create(ctx, patient, DentureRecipe(), jaw);

            AssertPlain(bill, "no team");
            Assert.True(bill.Part == jaw, "bill part is not the jaw");
        }

        // Given: Fast Operation ON, a capable lead (Medicine 20, Doctor on) and one assistant.
        // When:  a patient bill is created. Then: BillMedicalEx with surgeon = lead, doctors = lead + assistant,
        //        pawnRestriction = lead and the right part.
        [HospitalTest("MD1: Fast Operation ON gives the patient bill MD's default team (BillMedicalEx, lead, assistant, restriction)", RequiresMod = MdId)]
        public static void TeamBill(TestContext ctx)
        {
            var comp = TeamComponent(ctx);
            var lead = Doctor(ctx, 20);
            var assistant = Doctor(ctx, 5);
            SetTeam(comp, lead, new[] { assistant }, true);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            VanillaCompatTests.Register(ctx, patient);
            var jaw = Jaw(patient);
            AssertCanOperate(lead, patient, DentureRecipe());

            var bill = Create(ctx, patient, DentureRecipe(), jaw);

            Assert.Equal(BillExName, bill.GetType().FullName, "bill type");
            Assert.True(Get(bill, "surgeon") == lead, "surgeon is not the default lead");
            var doctors = ((IEnumerable)Get(bill, "assignedDoctors")).OfType<Pawn>().ToList();
            Assert.Equal(2, doctors.Count, "assignedDoctors count");
            Assert.True(doctors[0] == lead && doctors[1] == assistant, "assignedDoctors is not lead + assistant");
            Assert.True(bill.PawnRestriction == lead, "pawnRestriction is not the lead");
            Assert.True(bill.Part == jaw, "bill part is not the jaw");
        }

        // Given: a default team set but Fast Operation OFF. Then: plain bill (MD would open its dialog, Hospital never does).
        [HospitalTest("MD2: default team set but Fast Operation OFF gives a plain bill", RequiresMod = MdId)]
        public static void FastOperationOff(TestContext ctx)
        {
            var comp = TeamComponent(ctx);
            SetTeam(comp, Doctor(ctx, 20), new[] { Doctor(ctx, 5) }, false);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            VanillaCompatTests.Register(ctx, patient);

            AssertPlain(Create(ctx, patient, DentureRecipe(), Jaw(patient)), "fast operation off");
        }

        // Given: Fast Operation ON but the lead's Medicine is below the recipe's requirement. Then: plain bill.
        // Fails the precondition if no such recipe exists.
        [HospitalTest("MD3: a lead below the recipe's skill requirement gives a plain bill", RequiresMod = MdId)]
        public static void LeadTooUnskilled(TestContext ctx)
        {
            var recipe = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r =>
                r.IsSurgery && r.skillRequirements != null && r.skillRequirements.Any(s => s.skill == SkillDefOf.Medicine && s.minLevel >= 1));
            Assert.NotNull(recipe, "precondition: no surgery recipe with a Medicine requirement");
            var comp = TeamComponent(ctx);
            SetTeam(comp, Doctor(ctx, 0), new Pawn[0], true);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            VanillaCompatTests.Register(ctx, patient);

            AssertPlain(Create(ctx, patient, recipe, null), "unskilled lead");
        }

        // Given: Fast Operation ON and a capable lead. When: the administer-drug bill is created (the call DrugUtility
        // makes for scheduled drugs). Then: plain bill (MD excludes Administer* recipes).
        [HospitalTest("MD4: an administer-drug bill stays a plain bill even with an active team", RequiresMod = MdId)]
        public static void DrugBillStaysPlain(TestContext ctx)
        {
            var recipe = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsDrug).Select(d => DrugUtility.AdministerRecipeFor(d))
                .FirstOrDefault(r => r != null);
            Assert.NotNull(recipe, "precondition: no Administer recipe");
            var comp = TeamComponent(ctx);
            SetTeam(comp, Doctor(ctx, 20), new Pawn[0], true);
            var patient = VanillaCompatTests.Visitor(ctx, false);
            VanillaCompatTests.Register(ctx, patient);

            AssertPlain(Create(ctx, patient, recipe, null), "drug bill");
        }

        // Given: a team bill that is the patient's pending cure. When: it is deleted. Then: HasPendingSurgeryBill is cleared
        // (BillStack.Delete handling, same as for a plain bill).
        [HospitalTest("MD5: deleting a team bill clears HasPendingSurgeryBill like a plain bill", RequiresMod = MdId)]
        public static void DeleteTeamBill(TestContext ctx)
        {
            var comp = TeamComponent(ctx);
            var lead = Doctor(ctx, 20);
            SetTeam(comp, lead, new Pawn[0], true);
            var recipe = DentureRecipe();
            var patient = VanillaCompatTests.Visitor(ctx, false);
            var data = VanillaCompatTests.Register(ctx, patient);
            AssertCanOperate(lead, patient, recipe);
            var bill = Create(ctx, patient, recipe, Jaw(patient));
            Assert.Equal(BillExName, bill.GetType().FullName, "precondition: team bill");
            data.CureRecipe = recipe;
            data.HasPendingSurgeryBill = true;

            patient.health.surgeryBills.Delete(bill);

            Assert.False(data.HasPendingSurgeryBill, "pending flag still set after deleting the team bill");
            Assert.False(patient.health.surgeryBills.Bills.Contains(bill), "bill still on the patient");
        }
    }
}
