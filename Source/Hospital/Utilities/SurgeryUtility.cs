using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.Utilities;

public class SurgeryUtility
{
        // Bills on the pawn that are real treatment; automatic "Administer drug" bills do not count
        // (they must not keep a patient in bed or move them to a surgery bed).
        public static bool HasRealSurgeryBills(Pawn pawn)
        {
                var bills = pawn?.health?.surgeryBills?.Bills;
                if (bills == null) return false;
                foreach (var bill in bills)
                {
                        if (!DrugUtility.IsAdministerBill(bill)) return true;
                }
                return false;
        }

        // Vanilla HealthAIUtility.ShouldSeekMedicalRest, minus the surgery-bill part. Vanilla treats ANY bill that
        // should be done now (and a doctor exists) as "needs rest", which would keep a patient in the hospital for
        // an automatic drug bill. Real surgery bills are checked separately by the callers (HasRealSurgeryBills).
        public static bool ShouldSeekMedicalRestIgnoringBills(Pawn pawn)
        {
                if ((pawn.Downed && !LifeStageUtility.AlwaysDowned(pawn))
                    || pawn.health.HasHediffsNeedingTend()
                    || pawn.health.hediffSet.InLabor())
                        return true;
                return pawn.health.hediffSet.HasTendedAndHealingInjury()
                       || pawn.health.hediffSet.HasImmunizableNotImmuneHediff();
        }

        // True for surgery recipes the mod is willing to assign as a random "needs an operation" event.
        // Mirrors the per-pawn filtering in AddRandomSurgeryBill, minus the runtime checks, so the
        // blacklist picker dialog shows exactly the recipes that could otherwise show up.
        public static bool IsBlacklistableSurgery(RecipeDef recipe)
        {
                return recipe.IsSurgery
                       && !recipe.isViolation
                       && !recipe.defName.ToLower().Contains("abasia")
                       && !recipe.defName.ToLower().Contains("coma")
                       && !recipe.defName.ToLower().Contains("remove")
                       && !recipe.defName.ToLower().Contains("anesthetize")
                       && !recipe.defName.ToLower().Contains("blood")
                       && !recipe.defName.ToLower().Contains("admin")
                       && !recipe.defName.ToLower().Contains("rib")
                       && !recipe.defName.ToLower().Contains("harvest")
                       && !recipe.defName.ToUpper().Contains("VREA") // androids expanded
                       && !recipe.defName.ToUpper().Contains("GR"); // weird genetics expanded surgeries
        }

        // All surgeries applicable to humanlike patients that may be randomly assigned.
        public static IEnumerable<RecipeDef> CandidateSurgeries()
        {
                return ThingDefOf.Human.AllRecipes.Where(IsBlacklistableSurgery).Distinct();
        }

        // Hardcoded parts for modded recipes (disease overhauled etc.) whose target is not derivable from the recipe.
        // A rule overrides `part` only when the pawn really has that part; otherwise the part chosen so far is kept.
        public static BodyPartRecord ApplyNameBasedPartOverrides(RecipeDef recipe, Pawn pawn, BodyPartRecord part)
        {
                string recipeName = recipe.defName.ToLower();
                string hediffName = recipe.removesHediff?.defName.ToLower();

                BodyPartRecord Find(string label) => pawn.health.hediffSet.GetNotMissingParts()
                        .FirstOrDefault(record => record.def.label.ToLower().Equals(label)) ?? part;

                if (hediffName == "appendicitis") part = Find("torso");
                if (hediffName == "toothache") part = Find("jaw");
                if (recipeName.Contains("kidney")) part = Find("kidney");
                if (recipeName.Contains("deaf")) part = Find("ear");
                if (recipeName.Contains("glaucoma") || recipeName.Contains("strabismus")) part = Find("eye");
                if (recipeName.Contains("arrhythmia") || recipeName.Contains("heart")) part = Find("heart");
                if (recipeName.Contains("spinal") || recipeName.Contains("hernia")) part = Find("spine");
                if (recipeName.Contains("gastritis")) part = Find("stomach");
                return part;
        }

        public static bool AddRandomSurgeryBill(Pawn pawn, PatientData patientData, HospitalMapComponent hospital)
        {
	        List<RecipeDef> list = new List<RecipeDef>();
	        //Log.Message($"pawn.def.AllRecipes " + pawn.def.AllRecipes.Count);
	        foreach (RecipeDef recipe in pawn.def.AllRecipes)
	        {
		        if (recipe.AvailableNow 
		            && hospital.IsSurgeryRecipeAllowed(recipe)
		            && !recipe.isViolation
		            //&& recipe.Worker.GetPartsToApplyOn(pawn, recipe).Any()
		            && !recipe.defName.ToLower().Contains("abasia")
		            && !recipe.defName.ToLower().Contains("coma")
		            && !recipe.defName.ToLower().Contains("remove")
		            && !recipe.defName.ToLower().Contains("anesthetize")
		            && !recipe.defName.ToLower().Contains("blood")
		            && !recipe.defName.ToLower().Contains("admin")
		            && !recipe.defName.ToLower().Contains("rib")
		            && !recipe.defName.ToLower().Contains("harvest")
		            && !recipe.defName.ToUpper().Contains("VREA") // androids expanded
		            && !recipe.defName.ToUpper().Contains("GR") // weird genetics expanded surgeries
					)
		        {
			        if (!(from p in pawn.MapHeld.mapPawns.PawnsInFaction(Faction.OfPlayer)
				            where p.IsFreeColonist || p.IsColonyMechPlayerControlled
				            select p).Any((Pawn col) => recipe.PawnSatisfiesSkillRequirements(col)))
			        {
				        continue;
			        }
			        if (!recipe.PotentiallyMissingIngredients(null, pawn.MapHeld).Any())
			        {
				        list.Add(recipe);
				        //Log.Message($"surgery recipe added:" + recipe.defName);
			        }
		        }
	        }

	        if (list.Count == 0) {
		        Log.Message($"tried to find surgery but could not find one");
		        return true; // in case someone blacklisted all possible surgery recipes
			}

	        RecipeDef selectedRecipe = list.RandomElement();
	        //Log.Message($"recipe selected:" + selectedRecipe.defName);
	        
	        BodyPartRecord part = null;
	        if (selectedRecipe.appliedOnFixedBodyParts != null)
	        {
		        BodyPartDef bpd = selectedRecipe.appliedOnFixedBodyParts.RandomElement();
		        foreach (BodyPartRecord notMissingPart in pawn.health.hediffSet.GetNotMissingParts())
		        {
			        if (notMissingPart.def.Equals(bpd))
			        {
				        part = notMissingPart;
				        //Log.Message($"part selected1:" + part.def.defName);
			        }
		        }
	        }
	        if (part == null && selectedRecipe.Worker.GetPartsToApplyOn(pawn, selectedRecipe).Any())
	        {
		        part = selectedRecipe.Worker.GetPartsToApplyOn(pawn, selectedRecipe).RandomElement();
		        //Log.Message($"part selected2:" + part.def.defName);
	        }
	        part = ApplyNameBasedPartOverrides(selectedRecipe, pawn, part);
	        if (part == null)
	        {
		        IEnumerable<BodyPartRecord> source = from x in pawn.health.hediffSet.GetNotMissingParts()
			        where pawn.health.hediffSet.GetPartHealth(x) >= 2f && x.def.canScarify
			        select x;
		        if (!source.Any())
		        {
			        Log.Message($"tried to find part but could not find one");
			        return true;
		        }
		        part = source.RandomElement();
		        //Log.Message($"part selected3:" + part.def.defName);
	        }
	        //Log.Message($"part selected:" + part.Label);
	        if (selectedRecipe.removesHediff != null)
	        {
		        /*
		         * if the operation removes a hediff, we need to add it first...
		         */
		        //Log.Message($"removesHediff selected:" + selectedRecipe.removesHediff.label);
		        Hediff hediff = HediffMaker.MakeHediff(selectedRecipe.removesHediff, pawn);
		        hediff.Part = part;
		        pawn.health.AddHediff(hediff, part);
		        patientData.TrackedHediffSeverity = Mathf.Max(hediff.Severity, 0.1f);
		        patientData.Diagnosis = selectedRecipe.removesHediff.label;
	        }
	        else if (selectedRecipe.workerClass == typeof(Recipe_InstallArtificialBodyPart) ||
	                 selectedRecipe.workerClass == typeof(Recipe_InstallNaturalBodyPart))
	        {
		        /*
				* if we are installing an artificial body part, it is more fun to amputate the body part first
				*/
		        if (!part.def.canSuggestAmputation)
		        {
			        // For organs (can't be amputated), damage them significantly so patient doesn't heal before surgery
			        float partHealth = pawn.health.hediffSet.GetPartHealth(part);
			        float desiredDamage = partHealth * 0.5f; // 50% damage to ensure they need the transplant

			        DamageDef damageType = HealthUtility.RandomPermanentInjuryDamageType(
				        part.def.frostbiteVulnerability > 0f && pawn.RaceProps.ToolUser);
			        HediffDef hediffDefFromDamage = HealthUtility.GetHediffDefFromDamage(damageType, pawn, part);

			        Hediff hediff = HediffMaker.MakeHediff(hediffDefFromDamage, pawn, part);
			        hediff.Severity = desiredDamage;
			        pawn.health.AddHediff(hediff, part);
		        }
		        else
		        {
			        Hediff_MissingPart hediffMissingPart =
				        (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn);
			        hediffMissingPart.lastInjury = HealthUtility.GetHediffDefFromDamage(
				        HealthUtility.RandomPermanentInjuryDamageType(part.def.frostbiteVulnerability > 0f &&
				                                                      pawn.RaceProps.ToolUser), pawn, part);
			        hediffMissingPart.Part = part;
			        hediffMissingPart.IsFresh = false;
			        pawn.health.AddHediff(hediffMissingPart, part);
				        patientData.PartMissingAtArrival = true;			        
		        }


		        patientData.Diagnosis =
			        TranslatorFormattedStringExtensions.Translate("DiagnosisArtificialBodyPart", selectedRecipe.addsHediff != null ? selectedRecipe.addsHediff.label: part.Label);
	        }
	        else
	        {
		        /*
		         * all other surgeries
		         */
		        patientData.Diagnosis =
			        TranslatorFormattedStringExtensions.Translate("DiagnosisSurgeryOnPart", part.Label);
	        }

	        // Pass an empty ingredient list rather than null: vanilla accepts null, but other mods'
	        // CreateSurgeryBill patches (e.g. Multi-Doctor Surgery) iterate it and throw.
	        HealthCardUtility.CreateSurgeryBill(pawn, selectedRecipe, part, new List<Thing>(), false);
	        patientData.Cure = selectedRecipe.Worker.GetLabelWhenUsedOn(pawn, part);
	        patientData.CureRecipe = selectedRecipe;

	        if (pawn.health.surgeryBills.Count == 0)
	        {
		        Log.Message($"something went wrong creating surgery bill - recipe:" + selectedRecipe.label + " part:" + part.Label);
		        return true;
	        }

	        // Mark that this patient has a pending surgery that needs billing
	        patientData.HasPendingSurgeryBill = true;

	        return false;
        }
}