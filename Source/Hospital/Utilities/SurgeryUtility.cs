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
        // Exception: Plasteel Laryngoplasty is prepared by adding a voice/breathing trait (see CanAssignAsIs and
        // AddSurgeryBill); it stays "as is" here because the generic checks still apply once the trait exists.
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
                // Plasteel Laryngoplasty: a pawn that can be given Annoying voice / Creepy breathing is eligible
                // (AddSurgeryBill adds the trait). Mod absent: always false.
                if (Compat.PlasteelCompat.CanPrepareLaryngoplasty(pawn, recipe)) return true;
                // Plasteel beauty recipes: a pawn that can be given an allowed Beauty degree is eligible (AddSurgeryBill sets it).
                if (Compat.PlasteelCompat.CanPrepareBeauty(pawn, recipe)) return true;
                var worker = recipe.Worker;
                if (recipe.targetsBodyPart && !worker.GetPartsToApplyOn(pawn, recipe).Any()) return false;
                if (worker is Recipe_Surgery surgery && !surgery.CompletableEver(pawn)) return false;
                // Plasteel Surgery body-type mods put an adult body type on a child (checked by name, no assembly reference).
                if (!pawn.DevelopmentalStage.Adult() && IsPlasteelBodyWorker(recipe.workerClass)) return false;
                return true;
        }

        internal static bool IsPlasteelBodyWorker(System.Type type)
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

        // Recipes that can be given to this pawn as a patient surgery. ignoreRestrictions (debug) skips the mod
        // settings (aggressive switch, surgery blacklist) and the colonist-skill / ingredient checks; the pawn
        // validity checks always stay (without them vanilla deletes the bill and the patient gets stuck).
        public static List<RecipeDef> EligibleSurgeries(Pawn pawn, HospitalMapComponent hospital, bool ignoreRestrictions = false)
        {
	        List<RecipeDef> list = new List<RecipeDef>();
	        foreach (RecipeDef recipe in pawn.def.AllRecipes)
	        {
		        if (PawnStageRejection(pawn, hospital, recipe, ignoreRestrictions) != SurgeryFilter.None) continue;
		        if (!ignoreRestrictions && MapStageRejection(pawn.MapHeld, recipe) != SurgeryFilter.None) continue;
		        list.Add(recipe);
	        }
	        return list;
        }

        // The checks of EligibleSurgeries, in order. Also used by the debug patient sampler to report which check
        // rejects which recipe (first failing check wins).
        public enum SurgeryFilter { None, Aggressive, Research, Blacklist, Violation, DoesNotFit, Name, Implausible, Skill, Ingredients }

        // Checks that need only the pawn, the settings and the recipe (no map state besides research).
        public static SurgeryFilter PawnStageRejection(Pawn pawn, HospitalMapComponent hospital, RecipeDef recipe, bool ignoreRestrictions)
        {
	        bool allowAggressive = ignoreRestrictions || AllowsAggressive(hospital);
	        if (!allowAggressive && IsAggressionSurgery(recipe)) return SurgeryFilter.Aggressive;
	        if (!ignoreRestrictions && !recipe.AvailableNow) return SurgeryFilter.Research; // debug: research / availability only matters for UI listings
	        if (!ignoreRestrictions && !hospital.IsSurgeryRecipeAllowed(recipe)) return SurgeryFilter.Blacklist;
	        if (recipe.isViolation) return SurgeryFilter.Violation;
	        if (!CanAssignAsIs(pawn, recipe)) return SurgeryFilter.DoesNotFit;
	        if (!IsAssignableByName(recipe)) return SurgeryFilter.Name;
	        if (!Compat.SurgeryFilters.IsPlausibleRequest(pawn, recipe)) return SurgeryFilter.Implausible;
	        return SurgeryFilter.None;
        }

        // Checks that depend on the colony: someone with the skill, and the ingredients in stock (skipped in debug ignore mode).
        public static SurgeryFilter MapStageRejection(Map map, RecipeDef recipe)
        {
	        if (!(from p in map.mapPawns.PawnsInFaction(Faction.OfPlayer)
		            where p.IsFreeColonist || p.IsColonyMechPlayerControlled
		            select p).Any((Pawn col) => recipe.PawnSatisfiesSkillRequirements(col)))
		        return SurgeryFilter.Skill;
	        if (recipe.PotentiallyMissingIngredients(null, map).Any()) return SurgeryFilter.Ingredients;
	        return SurgeryFilter.None;
        }

        // The pawn-validity part of EligibleSurgeries (ignoreRestrictions = true) for one recipe. Needs no map,
        // so it can run on a freshly generated, unspawned pawn (used to pick a pawn that fits a forced surgery).
        public static bool CanForceOn(Pawn pawn, RecipeDef recipe)
        {
	        return !recipe.isViolation && CanAssignAsIs(pawn, recipe) && IsAssignableByName(recipe)
		               && Compat.SurgeryFilters.IsPlausibleRequest(pawn, recipe);
        }

        public static bool AddRandomSurgeryBill(Pawn pawn, PatientData patientData, HospitalMapComponent hospital)
        {
	        return AddRandomSurgeryBill(pawn, patientData, hospital, null);
        }

        // spec?.Surgery != null: that recipe is used (debug); it must be valid for this pawn, otherwise fails (true).
        // Returns true on failure (caller falls back to wounds), like before.
        public static bool AddRandomSurgeryBill(Pawn pawn, PatientData patientData, HospitalMapComponent hospital, PatientSpec spec)
        {
	        bool ignore = spec != null && spec.IgnoreRestrictions;
	        if (spec?.Surgery != null && ignore)
	        {
		        // Validate only the forced recipe: building the full list would run every recipe's worker on the pawn.
		        if (!pawn.def.AllRecipes.Contains(spec.Surgery) || !CanForceOn(pawn, spec.Surgery))
		        {
			        Log.Message("Hospital Debug: surgery " + spec.Surgery.defName + " is not valid for " + pawn.LabelShort);
			        return true;
		        }
		        return AddSurgeryBill(pawn, patientData, hospital, spec.Surgery);
	        }

	        List<RecipeDef> list = EligibleSurgeries(pawn, hospital, ignore);
	        if (spec?.Surgery != null)
	        {
		        if (!list.Contains(spec.Surgery))
		        {
			        Log.Message("Hospital Debug: surgery " + spec.Surgery.defName + " is not valid for " + pawn.LabelShort);
			        return true;
		        }
		        return AddSurgeryBill(pawn, patientData, hospital, spec.Surgery);
	        }

	        if (list.Count == 0) {
		        Log.Message($"tried to find surgery but could not find one");
		        return true; // in case someone blacklisted all possible surgery recipes
			}

	        return AddSurgeryBill(pawn, patientData, hospital, list.RandomElement());
        }

        // Prepares the pawn for the recipe (hediff / missing part), creates the bill and fills the patient data.
        // Returns true on failure.
        public static bool AddSurgeryBill(Pawn pawn, PatientData patientData, HospitalMapComponent hospital, RecipeDef selectedRecipe)
        {
	        // Plasteel Laryngoplasty: give the pawn a voice/breathing trait first (the worker needs it to offer any part).
	        // If the worker still offers no part, undo and fail (caller falls back to wounds).
	        var addedTrait = Compat.PlasteelCompat.TryPrepareLaryngoplasty(pawn, selectedRecipe);
	        if (addedTrait != null && !selectedRecipe.Worker.GetPartsToApplyOn(pawn, selectedRecipe).Any())
	        {
		        Compat.PlasteelCompat.RemoveLaryngoplastyTrait(pawn, addedTrait);
		        Log.Message("tried to prepare Laryngoplasty but the recipe has no valid part");
		        return true;
	        }

	        // Plasteel beauty recipes: set an allowed Beauty degree first (the worker only offers the core part then).
	        // If the worker still offers no part, restore the original trait and fail (caller falls back to wounds).
	        var beautyPrep = Compat.PlasteelCompat.TryPrepareBeauty(pawn, selectedRecipe);
	        if (beautyPrep != null && !selectedRecipe.Worker.GetPartsToApplyOn(pawn, selectedRecipe).Any())
	        {
		        Compat.PlasteelCompat.RestoreBeauty(pawn, beautyPrep);
		        Log.Message("tried to prepare a beauty surgery but the recipe has no valid part");
		        return true;
	        }

	        BodyPartRecord part = null;
	        if (!selectedRecipe.appliedOnFixedBodyParts.NullOrEmpty()) // vanilla initializes it to an empty list
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