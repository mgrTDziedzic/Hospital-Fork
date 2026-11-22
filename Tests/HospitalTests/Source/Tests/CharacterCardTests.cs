using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hospital;
using Hospital.Patches;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Patient chip in the Bio tab (CharacterCardPatch). Story1-5 live in BackstoryTests.cs.
    public static class CharacterCardTests
    {
        // Given: the game is loaded with Hospital.
        // When:  the Harmony patch info of CharacterCardUtility.DoTopStack is read.
        // Then:  it carries a transpiler owned by Hospital (the anchor call was found); if not, the chip is off (check the log for the info line).
        [HospitalTest("Story6: the Bio tab transpiler is applied to CharacterCardUtility.DoTopStack")]
        public static void TranspilerApplied(TestContext ctx)
        {
            var method = AccessTools.Method(typeof(CharacterCardUtility), "DoTopStack");
            Assert.NotNull(method, "CharacterCardUtility.DoTopStack not found");
            var info = Harmony.GetPatchInfo(method);
            Assert.True(info != null && info.Transpilers.Any(p => p.owner == "Niejeden.HospitalFork"), "no Hospital transpiler on DoTopStack");
        }

        // Given: a spawned non-patient (colonist) and an empty element list.
        // When:  AddPatientChip is called.
        // Then:  nothing is added.
        [HospitalTest("Story7: the Bio tab chip is not added for non-patients")]
        public static void NoChipForNonPatient(TestContext ctx)
        {
            var pawn = VanillaCompatTests.Colonist(ctx);
            var list = new List<GenUI.AnonymousStackElement>();
            CharacterCardPatch.AddPatientChip(list, pawn);
            Assert.Equal(0, list.Count, "elements added for a non-patient");
        }

        // Given: a spawned pawn registered as an active patient, and a list that already holds one element.
        // When:  AddPatientChip is called; then the patient is marked Dismissed and it is called again.
        // Then:  exactly one element with a positive width is appended for the active patient, none for the dismissed one.
        [HospitalTest("Story8: the Bio tab chip is added once for an active patient, not for a dismissed one")]
        public static void ChipForActivePatient(TestContext ctx)
        {
            var pawn = VanillaCompatTests.Visitor(ctx, hostile: false);
            var data = VanillaCompatTests.Register(ctx, pawn);
            var list = new List<GenUI.AnonymousStackElement> { new GenUI.AnonymousStackElement { width = 1f, drawer = _ => { } } };
            CharacterCardPatch.AddPatientChip(list, pawn);
            Assert.Equal(2, list.Count, "elements after the call");
            Assert.True(list[1].width > 0f && list[1].drawer != null, "chip has no width or drawer");
            data.Dismissed = true;
            var list2 = new List<GenUI.AnonymousStackElement>();
            CharacterCardPatch.AddPatientChip(list2, pawn);
            Assert.Equal(0, list2.Count, "elements for a dismissed patient");
        }
    }
}
