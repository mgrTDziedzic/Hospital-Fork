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

        // Reads gender / bodyType of the recipe's "PlasteelSurgery.SurgeryDef" extension by reflection.
        // False: mod absent, not a body surgery, no extension, or API problem (one info line, then off for the session).
        private static bool TryReadExtension(RecipeDef recipe, out Gender gender, out BodyTypeDef bodyType)
        {
                gender = Gender.None;
                bodyType = null;
                if (hintsBroken || !SupportedMods.IsActive(SupportedMods.PlasteelSurgery)) return false;
                if (!SurgeryUtility.IsPlasteelBodyWorker(recipe.workerClass)) return false;
                try
                {
                        var ext = recipe.modExtensions?.FirstOrDefault(e => e.GetType().FullName == "PlasteelSurgery.SurgeryDef");
                        if (ext == null) return false;
                        var type = ext.GetType();
                        var genderField = type.GetField("gender", BindingFlags.Public | BindingFlags.Instance);
                        var bodyField = type.GetField("bodyType", BindingFlags.Public | BindingFlags.Instance);
                        if (genderField == null || bodyField == null) throw new MissingFieldException(type.FullName);
                        gender = (Gender)genderField.GetValue(ext);
                        bodyType = bodyField.GetValue(ext) as BodyTypeDef;
                        return true;
                }
                catch (Exception e)
                {
                        hintsBroken = true;
                        Log.Message("[Hospital] Plasteel Surgery compatibility for body surgeries is off (API differs: " + e.GetType().Name + ")");
                        return false;
                }
        }

        // Feminize (target body Female) / Masculinize (target body Male) have no gender in the extension; patients
        // only request them when the pawn's gender matches. Decided by the body type, not by defName.
        // Everything else (and mod absent): plausible.
        public static bool IsPlausibleRequest(Pawn pawn, RecipeDef recipe)
        {
                if (!TryReadExtension(recipe, out var gender, out var body)) return true;
                if (gender != Gender.None) return true;
                if (body == BodyTypeDefOf.Female) return pawn.gender == Gender.Female;
                if (body == BodyTypeDefOf.Male) return pawn.gender == Gender.Male;
                return true;
        }

        // Body-type surgeries (BaseAlterBody) only work on adults of the right gender whose body type differs from
        // the target. Mod absent or recipe not a body surgery: nothing happens.
        public static void TryApplyHints(RecipeDef recipe, ref PawnGenerationRequest request)
        {
                if (!TryReadExtension(recipe, out var gender, out var targetBody)) return;
                request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
                bool explicitBody = targetBody != null;
                // Feminize / Masculinize: patient of the matching gender (see IsPlausibleRequest).
                if (gender == Gender.None && (targetBody == BodyTypeDefOf.Female || targetBody == BodyTypeDefOf.Male))
                        gender = targetBody == BodyTypeDefOf.Female ? Gender.Female : Gender.Male;
                if (gender != Gender.None) request.FixedGender = gender;
                if (targetBody == null && gender != Gender.None)
                        targetBody = gender == Gender.Male ? BodyTypeDefOf.Male : BodyTypeDefOf.Female;

                var options = new List<BodyTypeDef> { BodyTypeDefOf.Thin, BodyTypeDefOf.Fat, BodyTypeDefOf.Hulk };
                // Male/Female are only safe when the target is known (otherwise it depends on the pawn's gender),
                // and not for Feminize/Masculinize (Thin/Fat/Hulk only).
                bool genderBody = explicitBody && (targetBody == BodyTypeDefOf.Female || targetBody == BodyTypeDefOf.Male);
                if (targetBody != null && !genderBody) { options.Add(BodyTypeDefOf.Male); options.Add(BodyTypeDefOf.Female); }
                options.RemoveAll(b => b == null || b == targetBody);
                if (options.Count > 0) request.ForceBodyType = options.RandomElement();
        }
}
