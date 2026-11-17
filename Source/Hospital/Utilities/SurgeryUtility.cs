using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.Utilities;

public class SurgeryUtility
{
        // Bills the mod puts on a patient are built directly, not through HealthCardUtility.CreateSurgeryBill.
        // Mods patch that method for the player's health tab: Multi-Doctor Surgery swaps in its own bill and
        // opens an "assign doctors" window, and closing that window without confirming deletes the bill -
        // a surgery patient then had nothing left to treat and went home right after arrival.
        // Multi-Doctor Surgery with Fast Operation ON: the bill gets its default surgery team instead
        // (MultiDoctorCompat). Otherwise a plain Bill_Medical, as below.
        // Empty ingredient list rather than null (other mods' Bill_Medical code iterates it).
        public static Bill_Medical CreatePatientBill(Pawn pawn, RecipeDef recipe, BodyPartRecord part)
        {
                if (Compat.MultiDoctorCompat.TryCreateTeamBill(pawn, recipe, part, out var teamBill)) return teamBill;
                var bill = new Bill_Medical(recipe, new List<Thing>());
                pawn.health.surgeryBills.AddBill(bill);
                bill.Part = part; // after AddBill, same order as vanilla
                return bill;
        }

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
        // Surgeries that cure an aggression-causing hediff (CureScaria). The mod adds that hediff to the patient
        // first, so they are only offered when the map's "aggressive patients" challenge is on (allowAggressive).
        public static bool IsBlacklistableSurgery(RecipeDef recipe, bool allowAggressive = false)
        {
                return recipe.IsSurgery
                       && !recipe.isViolation
                       && (allowAggressive || !IsAggressionSurgery(recipe))
                       && IsAssignableByName(recipe);
        }

        public static bool IsAggressionSurgery(RecipeDef recipe)
        {
                return recipe.removesHediff != null && DiseaseUtility.IsAggressionHediff(recipe.removesHediff);
        }

        // Aggressive patients are only generated when surgery is accepted AND the challenge is on.
        public static bool AllowsAggressive(HospitalMapComponent hospital)
        {
                return hospital != null && hospital.AcceptSurgery && hospital.AcceptAggressive;
        }

        // Puts the patient under transport sedation (downed, unconscious) for SedationHours; Scaria does nothing
        // while the pawn is downed, so the berserk only starts when it wears off.
        public static void AddTransportSedation(Pawn pawn, PatientData patientData)
        {
                var def = Hospital.HospitalDefOf.Hospital_TransportSedation;
                var sedation = HediffMaker.MakeHediff(def, pawn);
                pawn.health.AddHediff(sedation);
                var disappears = sedation.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                {
                        int ticks = Mathf.RoundToInt(HospitalMod.Settings.SedationHours * GenDate.TicksPerHour);
                        disappears.disappearsAfterTicks = ticks;
                        disappears.ticksToDisappear = ticks;
                }
                patientData.Sedated = true;
        }

        // Name-based exclusions shared by the blacklist picker and the random assignment: generic vanilla words
        // plus other mods' surgeries (android parts, Genetic Rim implants; exact prefixes, see ModDefFilters).
        private static bool IsAssignableByName(RecipeDef recipe)
        {
                var name = recipe.defName.ToLower();
                return !name.Contains("abasia")
                       && !name.Contains("coma")
                       && !name.Contains("remove")
                       && !name.Contains("anesthetize")
                       && !name.Contains("blood")
                       && !name.Contains("admin")
                       && !name.Contains("rib")
                       && !name.Contains("harvest")
                       && !Compat.ModDefFilters.IsExcludedSurgery(recipe);
        }

        // All surgeries applicable to humanlike patients that may be randomly assigned.
        public static IEnumerable<RecipeDef> CandidateSurgeries(bool allowAggressive = false)
        {
                return ThingDefOf.Human.AllRecipes.Where(r => IsBlacklistableSurgery(r, allowAggressive)).Distinct();
        }

        // Recipes the mod does not prepare the pawn for (no hediff added first, no part removed first):
        // the surgery is assigned as it is, so the pawn must already be a valid target.
        public static bool IsAsIsRecipe(RecipeDef recipe)
        {
                return recipe.removesHediff == null
                       && recipe.workerClass != typeof(Recipe_InstallArtificialBodyPart)
                       && recipe.workerClass != typeof(Recipe_InstallNaturalBodyPart);
        }

        // Vanilla Bill_Medical.CompletableEver rejects a bill whose part is not in GetPartsToApplyOn, and
        // BillStack.RemoveIncompletableBills then deletes it without BillStack.Delete (so HasPendingSurgeryBill
        // stays true and the patient is stuck). Never assign a recipe the worker would reject.
        public static bool CanAssignAsIs(Pawn pawn, RecipeDef recipe)
        {
                if (!IsAsIsRecipe(recipe)) return true;
                var worker = recipe.Worker;
                if (recipe.targetsBodyPart && !worker.GetPartsToApplyOn(pawn, recipe).Any()) return false;
                if (worker is Recipe_Surgery surgery && !surgery.CompletableEver(pawn)) return false;
                // Plasteel Surgery body-type mods put an adult body type on a child (checked by name, no assembly reference).
                if (!pawn.DevelopmentalStage.Adult() && IsPlasteelBodyWorker(recipe.workerClass)) return false;
                return true;
        }

        private static bool IsPlasteelBodyWorker(System.Type type)
        {
                for (; type != null; type = type.BaseType)
                {
                        if (type.FullName == "PlasteelSurgery.BaseAlterBody") return true;
                }
                return false;
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
	        bool allowAggressive = AllowsAggressive(hospital);
	        //Log.Message($"pawn.def.AllRecipes " + pawn.def.AllRecipes.Count);
	        foreach (RecipeDef recipe in pawn.def.AllRecipes)
	        {
		        if (!allowAggressive && IsAggressionSurgery(recipe)) continue;
		        if (recipe.AvailableNow 
		            && hospital.IsSurgeryRecipeAllowed(recipe)
		            && !recipe.isViolation
		            && CanAssignAsIs(pawn, recipe)
		            && IsAssignableByName(recipe)
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
	        if (IsAsIsRecipe(selectedRecipe) && selectedRecipe.targetsBodyPart)
	        {
		        // The worker must accept the chosen part, otherwise vanilla removes the bill again.
		        var validParts = selectedRecipe.Worker.GetPartsToApplyOn(pawn, selectedRecipe).ToList();
		        if (validParts.Count > 0 && (part == null || !validParts.Contains(part)))
			        part = validParts.RandomElement();
	        }
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
		        // Sedate first: Hediff_Scaria starts the berserk on its first tick unless the pawn is downed.
		        if (IsAggressionSurgery(selectedRecipe)) AddTransportSedation(pawn, patientData);
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

	        CreatePatientBill(pawn, selectedRecipe, part);
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