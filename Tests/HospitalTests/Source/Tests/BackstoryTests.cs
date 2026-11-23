using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Generated wound and disease backstories (BackstoryUtility, RulePackDefs Hospital_WoundBackstory / Hospital_DiseaseBackstory).
    public static class BackstoryTests
    {
        private static HediffDef Def(string defName)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            Assert.NotNull(def, "HediffDef " + defName + " not found");
            return def;
        }

        private static Pawn TestPawn(TestContext ctx)
        {
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer));
            return ctx.Track(pawn);
        }

        // Given: lists of (injury def, severity) pairs.
        // When:  BackstoryUtility.Classify runs on them.
        // Then:  the category with the largest severity sum wins; gunshots, burns, crush/bruise/crack (Blunt) and cut/stab/scratch/bite (Sharp) map as planned; no injuries or unknown defs give Generic.
        [HospitalTest("Story1: Classify picks the wound category with the largest severity sum, Generic when there is none")]
        public static void ClassifyPure(TestContext ctx)
        {
            var gunshot = Def("Gunshot"); var burn = Def("Burn"); var crush = Def("Crush"); var bruise = Def("Bruise");
            var cut = Def("Cut"); var stab = Def("Stab"); var scratch = Def("Scratch"); var bite = Def("Bite");
            Assert.Equal(WoundCategory.Generic, BackstoryUtility.Classify(new (HediffDef, float)[0]), "no injuries");
            Assert.Equal(WoundCategory.Gunshot, BackstoryUtility.Classify(new[] { (gunshot, 10f), (cut, 2f) }), "gunshot dominant");
            Assert.Equal(WoundCategory.Burn, BackstoryUtility.Classify(new[] { (burn, 8f), (burn, 3f), (gunshot, 5f) }), "burns dominant (sum 11 > 5)");
            Assert.Equal(WoundCategory.Blunt, BackstoryUtility.Classify(new[] { (crush, 4f), (bruise, 4f), (cut, 5f) }), "crush + bruise (8) > cut (5)");
            Assert.Equal(WoundCategory.Sharp, BackstoryUtility.Classify(new[] { (cut, 2f), (stab, 2f), (scratch, 1f), (bite, 1f), (bruise, 5f) }), "sharp sum 6 > blunt 5");
            Assert.Equal(WoundCategory.Generic, BackstoryUtility.Classify(new[] { (Def("Flu"), 1f) }), "unknown def");
        }

        // Given: a test pawn and each wound category, 300 seeds per category.
        // When:  the backstory text is resolved.
        // Then:  it is non-empty, has no ERR / CANNOT_RESOLVE marker or unresolved symbol and contains the pawn's short name.
        [HospitalTest("Story2: every category x 300 seeds gives a non-empty text with the pawn's name and no grammar error")]
        public static void TextsAreValid(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            string name = pawn.Name.ToStringShort;
            foreach (WoundCategory category in Enum.GetValues(typeof(WoundCategory)))
            {
                for (int i = 1; i <= 300; i++)
                {
                    int seed = i * 7919;
                    string text = BackstoryUtility.Resolve(pawn, seed, category);
                    string where = category + " seed " + seed + ": \"" + text + "\"";
                    Assert.False(string.IsNullOrWhiteSpace(text), "empty text, " + where);
                    Assert.False(text.Contains("ERR") || text.Contains("CANNOT_RESOLVE"), "grammar error marker, " + where);
                    Assert.False(text.Contains("[") || text.Contains("]"), "unresolved symbol, " + where);
                    Assert.True(text.Contains(name), "pawn name missing, " + where);
                }
            }
        }

        // Given: a seed and category.
        // When:  the text is resolved twice, and the next Rand value is compared with and without a Resolve call in between.
        // Then:  the texts are identical (the saved seed gives the same text after loading) and the game's Rand sequence is not disturbed.
        [HospitalTest("Story3: the same seed gives the same text and Resolve leaves the Rand state untouched")]
        public static void DeterministicAndRandSafe(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            string a = BackstoryUtility.Resolve(pawn, 123456, WoundCategory.Gunshot);
            string b = BackstoryUtility.Resolve(pawn, 123456, WoundCategory.Gunshot);
            Assert.Equal(a, b, "same seed, same category");
            Assert.False(string.IsNullOrEmpty(a), "empty text");

            // Known state: push a seed, read the next value without and with a Resolve call in between.
            Rand.PushState(987654);
            int expected = Rand.Int;
            Rand.PopState();
            Rand.PushState(987654);
            BackstoryUtility.Resolve(pawn, 555, WoundCategory.Burn);
            int actual = Rand.Int;
            Rand.PopState();
            Assert.Equal(expected, actual, "next Rand value changed by Resolve");
        }

        // Given: a PatientData with a backstory seed and wound category.
        // When:  it is saved to a temp file with the Scribe and loaded back; a PatientData without a backstory (old save) also goes through.
        // Then:  seed and category are intact and give the same text; the old-save data has seed 0 and GetBackstory returns "".
        [HospitalTest("Story4: BackstorySeed and BackstoryWound survive save/load; old saves load with seed 0 and no text")]
        public static void RoundTrip(TestContext ctx)
        {
            SetShow(ctx, true);
            var pawn = TestPawn(ctx);
            var data = new PatientData { Type = PatientType.Wounds, BackstorySeed = 424242, BackstoryWound = WoundCategory.Burn.ToString() };
            var loaded = SaveAndLoad(data);
            Assert.Equal(424242, loaded.BackstorySeed, "seed");
            Assert.Equal(WoundCategory.Burn.ToString(), loaded.BackstoryWound, "wound category");
            Assert.Equal(BackstoryUtility.GetBackstory(pawn, data), BackstoryUtility.GetBackstory(pawn, loaded), "text after load");

            var old = SaveAndLoad(new PatientData { Type = PatientType.Wounds });
            Assert.Equal(0, old.BackstorySeed, "old save seed");
            Assert.Equal(string.Empty, BackstoryUtility.GetBackstory(pawn, old), "old save text");
        }

        // Given: the hospital is open with a free bed; wounds, disease and surgery arrivals are forced one by one.
        // When:  PatientArrives fires.
        // Then:  Wounds and Disease patients get a backstory seed, a known category and a non-empty text (a disease patient also its disease defName, a wounds patient none); Surgery patients have seed 0.
        [HospitalTest("Story5: arrival smoke - Wounds and Disease patients get a backstory seed, Surgery patients do not")]
        public static void ArrivalAssignsForWoundsAndDisease(TestContext ctx)
        {
            SetShow(ctx, true);
            var wounds = ArrivalTests.FireArrival(ctx, out _, new PatientSpec { Type = PatientType.Wounds, IgnoreRestrictions = true });
            Assert.True(wounds.Count >= 1, "no wounds patient arrived");
            foreach (var pair in wounds)
            {
                Assert.True(pair.Value.Type == PatientType.Wounds, "forced Wounds gave " + pair.Value.Type);
                Assert.True(pair.Value.BackstorySeed != 0, "Wounds patient without backstory seed");
                Assert.True(Enum.TryParse(pair.Value.BackstoryWound, out WoundCategory _), "unknown category " + pair.Value.BackstoryWound);
                Assert.True(string.IsNullOrEmpty(pair.Value.BackstoryDisease), "Wounds patient with a disease defName");
                Assert.False(string.IsNullOrEmpty(BackstoryUtility.GetBackstory(pair.Key, pair.Value)), "empty backstory text");
            }
            var disease = ArrivalTests.FireArrival(ctx, out _, new PatientSpec { Type = PatientType.Disease, IgnoreRestrictions = true });
            Assert.True(disease.Count >= 1, "no disease patient arrived");
            foreach (var pair in disease)
            {
                // A disease that could not be applied falls back to wounds (Type Wounds): then the wound backstory applies.
                Assert.True(pair.Value.BackstorySeed != 0, "Disease/fallback patient without backstory seed");
                Assert.False(string.IsNullOrEmpty(BackstoryUtility.GetBackstory(pair.Key, pair.Value)), "empty backstory text");
                if (pair.Value.Type == PatientType.Disease)
                {
                    Assert.False(string.IsNullOrEmpty(pair.Value.BackstoryDisease), "Disease patient without disease defName");
                    Assert.True(Enum.TryParse(pair.Value.BackstoryWound, out DiseaseCategory _), "unknown disease category " + pair.Value.BackstoryWound);
                }
                else
                {
                    Assert.True(string.IsNullOrEmpty(pair.Value.BackstoryDisease), "fallback wounds patient with a disease defName");
                    Assert.True(Enum.TryParse(pair.Value.BackstoryWound, out WoundCategory _), "unknown wound category " + pair.Value.BackstoryWound);
                }
            }
            var surgery = ArrivalTests.FireArrival(ctx, out _, new PatientSpec { Type = PatientType.Surgery, IgnoreRestrictions = true });
            foreach (var pair in surgery.Where(p => p.Value.Type == PatientType.Surgery))
                Assert.Equal(0, pair.Value.BackstorySeed, "Surgery patient backstory seed");
        }

        // Sets Settings.ShowPatientBackstories for the test and restores the old value on cleanup.
        private static void SetShow(TestContext ctx, bool value)
        {
            bool before = HospitalMod.Settings.ShowPatientBackstories;
            ctx.OnCleanup(() => HospitalMod.Settings.ShowPatientBackstories = before);
            HospitalMod.Settings.ShowPatientBackstories = value;
        }

        // Given: the setting "Show patient backstories" is off.
        // When:  a Wounds arrival is forced and GetBackstory is called; then the setting is turned on.
        // Then:  the seed is still assigned (RNG use must not depend on a per-client setting), GetBackstory is "" while off and non-empty when on.
        [HospitalTest("Story9: with ShowPatientBackstories off the seed is still assigned but GetBackstory returns empty")]
        public static void SettingOffHidesText(TestContext ctx)
        {
            SetShow(ctx, false);
            var wounds = ArrivalTests.FireArrival(ctx, out _, new PatientSpec { Type = PatientType.Wounds, IgnoreRestrictions = true });
            Assert.True(wounds.Count >= 1, "no wounds patient arrived");
            foreach (var pair in wounds.Where(p => p.Value.Type == PatientType.Wounds))
            {
                Assert.True(pair.Value.BackstorySeed != 0, "seed not assigned with the setting off");
                Assert.Equal(string.Empty, BackstoryUtility.GetBackstory(pair.Key, pair.Value), "text while off");
                HospitalMod.Settings.ShowPatientBackstories = true;
                Assert.False(string.IsNullOrEmpty(BackstoryUtility.GetBackstory(pair.Key, pair.Value)), "text after turning on");
                HospitalMod.Settings.ShowPatientBackstories = false;
            }
        }

        // Given: a test pawn and each wound category, 300 seeds per category.
        // When:  the activity, the backstory and the leave line are resolved for the same seed.
        // Then:  the activity text (resolved separately) is contained in both the backstory and the leave line, and the leave line has no error marker or unresolved symbol.
        [HospitalTest("Story10: backstory and leave line of one seed use the same activity")]
        public static void LeaveLineSharesActivity(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            string name = pawn.Name.ToStringShort;
            foreach (WoundCategory category in Enum.GetValues(typeof(WoundCategory)))
            {
                for (int i = 1; i <= 300; i++)
                {
                    int seed = i * 104729;
                    string activity = BackstoryUtility.ResolveActivity(pawn, seed, category);
                    string story = BackstoryUtility.Resolve(pawn, seed, category);
                    string leave = BackstoryUtility.ResolveLeave(pawn, seed, category);
                    string where = category + " seed " + seed + ": activity \"" + activity + "\", story \"" + story + "\", leave \"" + leave + "\"";
                    Assert.False(string.IsNullOrWhiteSpace(activity), "empty activity, " + where);
                    Assert.True(story.Contains(activity), "backstory lacks the activity, " + where);
                    Assert.True(leave.Contains(activity), "leave line lacks the activity, " + where);
                    Assert.True(leave.Contains(name), "leave line lacks the pawn name, " + where);
                    Assert.False(leave.Contains("ERR") || leave.Contains("CANNOT_RESOLVE") || leave.Contains("[") || leave.Contains("]"), "grammar error, " + where);
                }
            }
        }

        // Given: a patient with a seed, then the same seed again, and the Rand state compared with and without resolving.
        // When:  the leave line is resolved twice.
        // Then:  both results are equal and the next Rand value is unchanged by the call.
        [HospitalTest("Story11: the leave line is deterministic and leaves the Rand state untouched")]
        public static void LeaveLineDeterministic(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            string a = BackstoryUtility.ResolveLeave(pawn, 777777, WoundCategory.Sharp);
            string b = BackstoryUtility.ResolveLeave(pawn, 777777, WoundCategory.Sharp);
            Assert.Equal(a, b, "same seed");
            Assert.False(string.IsNullOrEmpty(a), "empty leave line");

            Rand.PushState(246810);
            int expected = Rand.Int;
            Rand.PopState();
            Rand.PushState(246810);
            BackstoryUtility.ResolveLeave(pawn, 31337, WoundCategory.Burn);
            int actual = Rand.Int;
            Rand.PopState();
            Assert.Equal(expected, actual, "next Rand value changed by ResolveLeave");
        }

        // Given: a wounded PatientData with a seed.
        // When:  GetLeaveLine is called with the setting on, with seed 0, and with the setting off.
        // Then:  it is non-empty and equal to ResolveLeave with the setting on; empty with seed 0 and when the setting is off.
        [HospitalTest("Story12: GetLeaveLine is empty without a seed or with ShowPatientBackstories off")]
        public static void LeaveLineGates(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            SetShow(ctx, true);
            var data = new PatientData { Type = PatientType.Wounds, BackstorySeed = 13579, BackstoryWound = WoundCategory.Gunshot.ToString() };
            string line = BackstoryUtility.GetLeaveLine(pawn, data);
            Assert.Equal(BackstoryUtility.ResolveLeave(pawn, 13579, WoundCategory.Gunshot), line, "leave line");
            Assert.False(string.IsNullOrEmpty(line), "empty with setting on");
            Assert.Equal(string.Empty, BackstoryUtility.GetLeaveLine(pawn, new PatientData { Type = PatientType.Wounds }), "seed 0");
            HospitalMod.Settings.ShowPatientBackstories = false;
            Assert.Equal(string.Empty, BackstoryUtility.GetLeaveLine(pawn, data), "setting off");
        }

        // Given: disease defNames (vanilla, modded-style names, unknown, null).
        // When:  BackstoryUtility.CategoryOfDisease runs on them.
        // Then:  vanilla diseases map to Respiratory / Insect / Parasite / Mechanites / Infection / Toxic, name fragments catch modded names, anything else gives Generic.
        [HospitalTest("Story13: CategoryOfDisease maps vanilla defNames and name fragments, Generic otherwise")]
        public static void DiseaseClassifyPure(TestContext ctx)
        {
            Assert.Equal(DiseaseCategory.Respiratory, BackstoryUtility.CategoryOfDisease("Flu"), "Flu");
            Assert.Equal(DiseaseCategory.Respiratory, BackstoryUtility.CategoryOfDisease("Plague"), "Plague");
            Assert.Equal(DiseaseCategory.Respiratory, BackstoryUtility.CategoryOfDisease("LungRot"), "LungRot");
            Assert.Equal(DiseaseCategory.Insect, BackstoryUtility.CategoryOfDisease("Malaria"), "Malaria");
            Assert.Equal(DiseaseCategory.Insect, BackstoryUtility.CategoryOfDisease("SleepingSickness"), "SleepingSickness");
            Assert.Equal(DiseaseCategory.Parasite, BackstoryUtility.CategoryOfDisease("GutWorms"), "GutWorms");
            Assert.Equal(DiseaseCategory.Parasite, BackstoryUtility.CategoryOfDisease("MuscleParasites"), "MuscleParasites");
            Assert.Equal(DiseaseCategory.Mechanites, BackstoryUtility.CategoryOfDisease("FibrousMechanites"), "FibrousMechanites");
            Assert.Equal(DiseaseCategory.Mechanites, BackstoryUtility.CategoryOfDisease("SomeMod_SensoryMechanites2"), "mechanite fragment");
            Assert.Equal(DiseaseCategory.Infection, BackstoryUtility.CategoryOfDisease("WoundInfection"), "WoundInfection");
            Assert.Equal(DiseaseCategory.Infection, BackstoryUtility.CategoryOfDisease("ModdedInfectionX"), "infection fragment");
            Assert.Equal(DiseaseCategory.Toxic, BackstoryUtility.CategoryOfDisease("ToxicBuildup"), "ToxicBuildup");
            Assert.Equal(DiseaseCategory.Respiratory, BackstoryUtility.CategoryOfDisease("SuperFlu"), "flu fragment");
            Assert.Equal(DiseaseCategory.Generic, BackstoryUtility.CategoryOfDisease("SomethingElse"), "unknown");
            Assert.Equal(DiseaseCategory.Generic, BackstoryUtility.CategoryOfDisease(null), "null");
            Assert.Equal(DiseaseCategory.Generic, BackstoryUtility.CategoryOfDisease(""), "empty");
        }

        // The defs used with every category in the text tests: a real vanilla disease, and null (unknown def).
        private static IEnumerable<HediffDef> DiseaseDefsForText()
        {
            yield return Def("Flu");
            yield return null;
        }

        // Given: a test pawn, each disease category, 300 seeds per category, with a real disease def and with no def.
        // When:  the disease backstory text is resolved.
        // Then:  it is non-empty, has no ERR / CANNOT_RESOLVE marker or unresolved symbol, contains the pawn's short name and the disease label (or the fallback label without a def).
        [HospitalTest("Story14: every disease category x 300 seeds gives a non-empty text with the pawn's name, the disease label and no grammar error")]
        public static void DiseaseTextsAreValid(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            string name = pawn.Name.ToStringShort;
            foreach (var disease in DiseaseDefsForText())
            {
                string label = disease != null ? disease.label : "a mysterious illness";
                foreach (DiseaseCategory category in Enum.GetValues(typeof(DiseaseCategory)))
                {
                    for (int i = 1; i <= 300; i++)
                    {
                        int seed = i * 7919;
                        string text = BackstoryUtility.Resolve(pawn, seed, category, disease);
                        string where = category + " " + (disease?.defName ?? "no def") + " seed " + seed + ": \"" + text + "\"";
                        Assert.False(string.IsNullOrWhiteSpace(text), "empty text, " + where);
                        Assert.False(text.Contains("ERR") || text.Contains("CANNOT_RESOLVE"), "grammar error marker, " + where);
                        Assert.False(text.Contains("[") || text.Contains("]"), "unresolved symbol, " + where);
                        Assert.True(text.Contains(name), "pawn name missing, " + where);
                        Assert.True(text.Contains(label), "disease label missing, " + where);
                    }
                }
            }
        }

        // Given: a test pawn, each disease category, 300 seeds per category, with a real disease def and with no def.
        // When:  the activity, the backstory and the leave line are resolved for the same seed.
        // Then:  the activity text is contained in both the backstory and the leave line, and the leave line has the pawn's name and no error marker or unresolved symbol.
        [HospitalTest("Story15: disease backstory and leave line of one seed use the same activity")]
        public static void DiseaseLeaveLineSharesActivity(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            string name = pawn.Name.ToStringShort;
            foreach (var disease in DiseaseDefsForText())
            {
                foreach (DiseaseCategory category in Enum.GetValues(typeof(DiseaseCategory)))
                {
                    for (int i = 1; i <= 300; i++)
                    {
                        int seed = i * 104729;
                        string activity = BackstoryUtility.ResolveActivity(pawn, seed, category, disease);
                        string story = BackstoryUtility.Resolve(pawn, seed, category, disease);
                        string leave = BackstoryUtility.ResolveLeave(pawn, seed, category, disease);
                        string where = category + " " + (disease?.defName ?? "no def") + " seed " + seed + ": activity \"" + activity + "\", story \"" + story + "\", leave \"" + leave + "\"";
                        Assert.False(string.IsNullOrWhiteSpace(activity), "empty activity, " + where);
                        Assert.True(story.Contains(activity), "backstory lacks the activity, " + where);
                        Assert.True(leave.Contains(activity), "leave line lacks the activity, " + where);
                        Assert.True(leave.Contains(name), "leave line lacks the pawn name, " + where);
                        Assert.False(leave.Contains("ERR") || leave.Contains("CANNOT_RESOLVE") || leave.Contains("[") || leave.Contains("]"), "grammar error, " + where);
                    }
                }
            }
        }

        // Given: a PatientData with a disease backstory (seed, category, disease defName), one whose disease def does not exist (mod removed), and a wound PatientData.
        // When:  they are saved with the Scribe and loaded back, and GetBackstory / GetLeaveLine are called.
        // Then:  seed, category and defName survive and give the same text; the unknown def gives a non-empty text with the fallback label and no unresolved symbol; the wound data keeps loading as before with no disease defName.
        [HospitalTest("Story16: disease backstory survives save/load; a missing disease def falls back to a generic label; wound data is unchanged")]
        public static void DiseaseRoundTrip(TestContext ctx)
        {
            SetShow(ctx, true);
            var pawn = TestPawn(ctx);
            var data = new PatientData { Type = PatientType.Disease, BackstorySeed = 515151, BackstoryWound = DiseaseCategory.Respiratory.ToString(), BackstoryDisease = "Flu" };
            var loaded = SaveAndLoad(data);
            Assert.Equal(515151, loaded.BackstorySeed, "seed");
            Assert.Equal(DiseaseCategory.Respiratory.ToString(), loaded.BackstoryWound, "disease category");
            Assert.Equal("Flu", loaded.BackstoryDisease, "disease defName");
            string text = BackstoryUtility.GetBackstory(pawn, loaded);
            Assert.False(string.IsNullOrEmpty(text), "empty text after load");
            Assert.Equal(BackstoryUtility.GetBackstory(pawn, data), text, "text after load");
            Assert.True(text.Contains(Def("Flu").label), "disease label missing: " + text);
            Assert.False(string.IsNullOrEmpty(BackstoryUtility.GetLeaveLine(pawn, loaded)), "empty leave line after load");

            var missing = SaveAndLoad(new PatientData { Type = PatientType.Disease, BackstorySeed = 616161, BackstoryWound = DiseaseCategory.Insect.ToString(), BackstoryDisease = "Hospital_NoSuchDisease" });
            string missingText = BackstoryUtility.GetBackstory(pawn, missing);
            Assert.False(string.IsNullOrEmpty(missingText), "empty text for a missing def");
            Assert.True(missingText.Contains("a mysterious illness"), "fallback label missing: " + missingText);
            Assert.False(missingText.Contains("[") || missingText.Contains("]") || missingText.Contains("ERR"), "grammar error: " + missingText);
            Assert.False(string.IsNullOrEmpty(BackstoryUtility.GetLeaveLine(pawn, missing)), "empty leave line for a missing def");

            var wound = SaveAndLoad(new PatientData { Type = PatientType.Wounds, BackstorySeed = 424242, BackstoryWound = WoundCategory.Burn.ToString() });
            Assert.True(string.IsNullOrEmpty(wound.BackstoryDisease), "wound data with a disease defName");
            Assert.Equal(BackstoryUtility.Resolve(pawn, 424242, WoundCategory.Burn), BackstoryUtility.GetBackstory(pawn, wound), "wound text unchanged");
        }

        // Given: a patient whose disease could not be applied (Type already Wounds), a Disease patient with a defName and a Surgery patient.
        // When:  BackstoryUtility.AssignBackstory runs on each.
        // Then:  the fallback patient gets a wound category and no disease defName; the disease patient keeps its defName and gets a disease category; the surgery patient gets nothing.
        [HospitalTest("Story17: AssignBackstory picks wound or disease texts by the final patient type, none for surgery")]
        public static void AssignByType(TestContext ctx)
        {
            var pawn = TestPawn(ctx);
            var fallback = new PatientData { Type = PatientType.Wounds, BackstoryDisease = null };
            BackstoryUtility.AssignBackstory(pawn, fallback);
            Assert.True(fallback.BackstorySeed != 0, "fallback seed");
            Assert.True(Enum.TryParse(fallback.BackstoryWound, out WoundCategory _), "fallback category " + fallback.BackstoryWound);
            Assert.True(string.IsNullOrEmpty(fallback.BackstoryDisease), "fallback disease defName");

            var sick = new PatientData { Type = PatientType.Disease, BackstoryDisease = "Malaria" };
            BackstoryUtility.AssignBackstory(pawn, sick);
            Assert.True(sick.BackstorySeed != 0, "disease seed");
            Assert.Equal(DiseaseCategory.Insect.ToString(), sick.BackstoryWound, "disease category");
            Assert.Equal("Malaria", sick.BackstoryDisease, "disease defName kept");

            var surgery = new PatientData { Type = PatientType.Surgery };
            BackstoryUtility.AssignBackstory(pawn, surgery);
            Assert.Equal(0, surgery.BackstorySeed, "surgery seed");
        }

        private static PatientData SaveAndLoad(PatientData data)
        {
            string path = Path.Combine(Path.GetTempPath(), "hospital_backstorytest_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Scribe.saver.InitSaving(path, "root");
                try
                {
                    Scribe_Deep.Look(ref data, "data");
                    Scribe.saver.FinalizeSaving();
                }
                catch { Scribe.saver.ForceStop(); throw; }

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
