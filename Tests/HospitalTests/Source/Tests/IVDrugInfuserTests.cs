using System;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // IV Drug Infuser compat (IVDrugInfuserCompat): a dose given by the infuser is billed and recorded for the drug policy.
    // IV Drug Infuser is reached by reflection (no assembly reference). Skipped (RequiresMod) when the mod is not active.
    public static class IVDrugInfuserTests
    {
        private const string ModKey = global::Hospital.Compat.SupportedMods.IVDrugInfuser;

        private static void Infuse(ThingDef def, Pawn pawn)
        {
            var helper = global::HarmonyLib.AccessTools.TypeByName("IVDrugInfuser.Helper");
            Assert.NotNull(helper, "IVDrugInfuser.Helper type missing");
            var method = helper.GetMethod("Infused", new[] { typeof(Thing), typeof(Pawn) });
            Assert.NotNull(method, "Helper.Infused(Thing, Pawn) missing");
            method.Invoke(null, new object[] { ThingMaker.MakeThing(def), pawn });
        }

        // Given: an active patient. When: the infuser gives one medicine and one drug. Then: the bill grows by the tend
        //        price (medicine) and the administer price (drug); LastDrugTick is set for both.
        [HospitalTest("IVD1: infused medicine and drug are billed and recorded in LastDrugTick", RequiresMod = ModKey)]
        public static void InfusedDosesAreBilled(TestContext ctx)
        {
            var medicine = ThingDefOf.MedicineHerbal;
            var drug = DefDatabase<ThingDef>.GetNamed("Beer");
            var patient = VanillaCompatTests.Visitor(ctx, false);
            var data = VanillaCompatTests.Register(ctx, patient);
            var start = data.Bill;
            var tick = Find.TickManager.TicksGame;

            Infuse(medicine, patient);
            Assert.True(Math.Abs(data.Bill - start - global::Hospital.Compat.IVDrugInfuserCompat.PriceOf(medicine)) < 0.01f, "medicine bill delta");
            Assert.True(data.LastDrugTick.TryGetValue(medicine, out var t1) && t1 == tick, "LastDrugTick for medicine");

            var afterMed = data.Bill;
            Infuse(drug, patient);
            Assert.True(Math.Abs(data.Bill - afterMed - global::Hospital.Compat.IVDrugInfuserCompat.PriceOf(drug)) < 0.01f, "drug bill delta");
            Assert.True(data.LastDrugTick.TryGetValue(drug, out var t2) && t2 == tick, "LastDrugTick for drug");
        }

        // Given: a pawn that is not a patient. Then: nothing is billed or recorded (no patient data to check, no exception).
        [HospitalTest("IVD2: infusing a non-patient does not throw", RequiresMod = ModKey)]
        public static void NonPatientIgnored(TestContext ctx)
        {
            var colonist = VanillaCompatTests.Colonist(ctx);
            Infuse(ThingDefOf.MedicineHerbal, colonist);
            Assert.True(!colonist.IsPatient(out _), "colonist became a patient");
        }
    }
}
