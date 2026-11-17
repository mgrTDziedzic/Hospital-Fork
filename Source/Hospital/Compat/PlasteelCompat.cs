using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

using Hospital.Utilities;

namespace Hospital.Compat;

// Plasteel Surgery (Continued) is supported without any reference to it (default blacklist by defName,
// worker check by type name). Not installed: nothing happens and nothing is logged.
public static class PlasteelCompat
{
        // Called once at startup (HospitalMod).
        public static void Startup()
        {
                var key = SupportedMods.PlasteelSurgery;
                if (SupportedMods.IsActive(key)) Log.Message("[Hospital] " + SupportedMods.DisplayName(key) + " (" + SupportedMods.ActiveId(key) + ") detected, compatibility enabled");
        }

        private static bool hintsBroken;

        // Body-type surgeries (BaseAlterBody) only work on adults of the right gender whose body type differs from
        // the target. Reads the recipe's "PlasteelSurgery.SurgeryDef" extension (gender, bodyType) by reflection.
        // Mod absent or recipe not a body surgery: nothing happens. Any problem: one info line, hints off.
        public static void TryApplyHints(RecipeDef recipe, ref PawnGenerationRequest request)
        {
                if (hintsBroken || !SupportedMods.IsActive(SupportedMods.PlasteelSurgery)) return;
                if (!SurgeryUtility.IsPlasteelBodyWorker(recipe.workerClass)) return;
                try
                {
                        var ext = recipe.modExtensions?.FirstOrDefault(e => e.GetType().FullName == "PlasteelSurgery.SurgeryDef");
                        if (ext == null) return;
                        var type = ext.GetType();
                        var genderField = type.GetField("gender", BindingFlags.Public | BindingFlags.Instance);
                        var bodyField = type.GetField("bodyType", BindingFlags.Public | BindingFlags.Instance);
                        if (genderField == null || bodyField == null) throw new MissingFieldException(type.FullName);
                        var gender = (Gender)genderField.GetValue(ext);
                        var targetBody = bodyField.GetValue(ext) as BodyTypeDef;

                        request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
                        if (gender != Gender.None) request.FixedGender = gender;
                        if (targetBody == null && gender != Gender.None)
                                targetBody = gender == Gender.Male ? BodyTypeDefOf.Male : BodyTypeDefOf.Female;

                        var options = new List<BodyTypeDef> { BodyTypeDefOf.Thin, BodyTypeDefOf.Fat, BodyTypeDefOf.Hulk };
                        // Male/Female are only safe when the target is known (otherwise it depends on the pawn's gender).
                        if (targetBody != null) { options.Add(BodyTypeDefOf.Male); options.Add(BodyTypeDefOf.Female); }
                        options.RemoveAll(b => b == null || b == targetBody);
                        if (options.Count > 0) request.ForceBodyType = options.RandomElement();
                }
                catch (Exception e)
                {
                        hintsBroken = true;
                        Log.Message("[Hospital] Plasteel Surgery pawn hints are off (API differs: " + e.GetType().Name + ")");
                }
        }
}
