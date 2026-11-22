using System;
using System.Collections.Generic;
using System.Linq;
using Hospital.Patches;
using Hospital.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital
{
    public class HospitalMapComponent : MapComponent
    {
        public Dictionary<Pawn, PatientData> Patients;
        private List<Pawn> _colonistsKeysWorkingList;
        private List<PatientData> _colonistsValuesWorkingList;
        
        public bool openForBusiness = false;
        public List<bool> openingHours = DefaultOpeningHours();
        public List<RecipeDef> refusedOperations = new List<RecipeDef>();
        public List<HediffDef> refusedDiseases = new List<HediffDef>();
        public List<FactionDef> refusedFactions = new List<FactionDef>();
        public FoodPolicy PatientFoodPolicy;
        // Vanilla drug policies handed to patients (null = none). Entries with allowScheduled are administered.
        public DrugPolicy DrugsAllPatients;
        public DrugPolicy DrugsInfection;
        public DrugPolicy DrugsSurgery;
        public DrugPolicy DrugsInjuries;
        public DrugPolicy DrugsScars;
        // Give a list's drugs to teetotalers too. Defaults: All off, Infection/Surgery on.
        public bool DrugsAllTeetotal = false;
        public bool DrugsInfectionTeetotal = true;
        public bool DrugsSurgeryTeetotal = true;
        public bool DrugsInjuriesTeetotal = true;
        public bool DrugsScarsTeetotal = false;
        public bool MassCasualties;
        public bool AcceptSurgery;
        public bool AcceptDanger;
        // Challenge: allow aggressive patients (CureScaria), sedated on arrival. Only matters with AcceptSurgery.
        public bool AcceptAggressive;
        // Mass casualty events ignore dangers on the map (manhunters etc.) unless this is switched off.
        public bool MceAcceptDanger = true;
        
        public HospitalMapComponent(Map map) : base(map)
        {
            Patients = new Dictionary<Pawn, PatientData>();
            PatientFoodPolicy = Current.Game.foodRestrictionDatabase.DefaultFoodRestriction();
            // New maps only: on load Scribe_Collections replaces this list with the saved one.
            refusedOperations.AddRange(DefaultRefusedRecipes());
        }

        // Cosmetic surgeries (Plasteel Surgery) refused on new maps: face worsening and body-type mods.
        // Masculinize / Feminize / Sex Change are deliberately not listed.
        static readonly string[] DefaultRefusedOperations =
        {
            "PS_DecreaseBeautyBasic", "PS_DecreaseBeautyMid",
            "PS_MakeThin", "PS_MakeStandard", "PS_MakeHulk", "PS_MakeFat", "PS_MakeBB"
        };

        // Resolves the default names; recipes of mods that are not loaded are skipped.
        public static List<RecipeDef> DefaultRefusedRecipes()
        {
            var result = new List<RecipeDef>();
            foreach (var name in DefaultRefusedOperations)
            {
                var def = DefDatabase<RecipeDef>.GetNamedSilentFail(name);
                if (def != null) result.Add(def);
            }
            return result;
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref openingHours, "openingHours");
            Scribe_Collections.Look(ref refusedOperations, "refusedOperations");
            Scribe_Collections.Look(ref refusedDiseases, "refusedDiseases");
            Scribe_Collections.Look(ref refusedFactions, "refusedFactions");
            Scribe_Values.Look(ref openForBusiness, "openForBusiness", false);
            // logNullErrors: false - a reference to a destroyed pawn is always saved as null, so a
            // patient that was lost without being removed (see RemoveLostPatients) used to log
            // "Null key while loading dictionary" on load. The null entry itself is just skipped.
            Scribe_Collections.Look(ref Patients, "patients", LookMode.Reference, LookMode.Deep, ref _colonistsKeysWorkingList, ref _colonistsValuesWorkingList, logNullErrors: false);
            Scribe_References.Look(ref PatientFoodPolicy, "PatientFoodPolicy");
            PatientFoodPolicy ??= Current.Game.foodRestrictionDatabase.DefaultFoodRestriction();
            Scribe_References.Look(ref DrugsAllPatients, "DrugsAllPatients");
            Scribe_References.Look(ref DrugsInfection, "DrugsInfection");
            Scribe_References.Look(ref DrugsSurgery, "DrugsSurgery");
            Scribe_References.Look(ref DrugsInjuries, "DrugsInjuries");
            Scribe_References.Look(ref DrugsScars, "DrugsScars");
            Scribe_Values.Look(ref DrugsAllTeetotal, "DrugsAllTeetotal", false);
            Scribe_Values.Look(ref DrugsInfectionTeetotal, "DrugsInfectionTeetotal", true);
            Scribe_Values.Look(ref DrugsSurgeryTeetotal, "DrugsSurgeryTeetotal", true);
            Scribe_Values.Look(ref DrugsInjuriesTeetotal, "DrugsInjuriesTeetotal", true);
            Scribe_Values.Look(ref DrugsScarsTeetotal, "DrugsScarsTeetotal", false);
            Scribe_Values.Look(ref MassCasualties, "massCasualties", false);
            Scribe_Values.Look(ref AcceptSurgery, "acceptSurgery", false);
            Scribe_Values.Look(ref AcceptDanger, "AcceptDanger", false);
            Scribe_Values.Look(ref AcceptAggressive, "AcceptAggressive", false);
            Scribe_Values.Look(ref MceAcceptDanger, "MceAcceptDanger", true);

            // Scribe_Collections.Look sets a collection to null when its node is missing from the
            // save (older mod version), so defaults have to be restored AFTER Look. Patients only
            // in PostLoadInit, after the dictionary has been rebuilt from its working lists.
            if (Scribe.mode == LoadSaveMode.LoadingVars) EnsureValidSettings();
            else if (Scribe.mode == LoadSaveMode.PostLoadInit) Patients ??= new Dictionary<Pawn, PatientData>();
        }

        public static List<bool> DefaultOpeningHours() => new List<bool>
        {
            false,false,false,false,false,false,false,true, //8
            false,false,false,true, //12
            false,false,false,true,//16
            false,false,false,true,//20
            false,false,false,false
        };

        // Repairs settings a save may lack: missing lists, and an opening-hours table that is
        // not exactly one entry per hour (IsOpen indexes it by hour of day).
        public void EnsureValidSettings()
        {
            if (openingHours == null) openingHours = DefaultOpeningHours();
            else if (openingHours.Count != GenDate.HoursPerDay)
            {
                if (openingHours.Count > GenDate.HoursPerDay)
                    openingHours.RemoveRange(GenDate.HoursPerDay, openingHours.Count - GenDate.HoursPerDay);
                while (openingHours.Count < GenDate.HoursPerDay) openingHours.Add(false);
            }
            refusedOperations ??= new List<RecipeDef>();
            refusedDiseases ??= new List<HediffDef>();
            refusedFactions ??= new List<FactionDef>();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (Find.TickManager.TicksGame % 2000 == 0) RemoveLostPatients();
            if (Find.TickManager.TicksGame % 250 == 0)
            {
                CheckExternallyCuredPatients();
                ScheduleDrugBills();
            }
        }

        public DrugPolicy GetDrugPolicy(DrugList list)
        {
            switch (list)
            {
                case DrugList.Infection: return DrugsInfection;
                case DrugList.Surgery: return DrugsSurgery;
                case DrugList.Injuries: return DrugsInjuries;
                case DrugList.Scars: return DrugsScars;
                default: return DrugsAllPatients;
            }
        }

        public bool GetDrugTeetotal(DrugList list)
        {
            switch (list)
            {
                case DrugList.Infection: return DrugsInfectionTeetotal;
                case DrugList.Surgery: return DrugsSurgeryTeetotal;
                case DrugList.Injuries: return DrugsInjuriesTeetotal;
                case DrugList.Scars: return DrugsScarsTeetotal;
                default: return DrugsAllTeetotal;
            }
        }

        // The UI calls these; the Synced variants below are the Multiplayer sync methods
        // (plain ints/bools, so no policy object has to be serialised).
        public void SetDrugPolicy(DrugList list, DrugPolicy policy) => SetDrugPolicySynced((int)list, policy?.id ?? -1);

        public void SetDrugTeetotal(DrugList list, bool value) => SetDrugTeetotalSynced((int)list, value);

        public void SetDrugPolicySynced(int list, int policyId)
        {
            DrugPolicy policy = policyId < 0 ? null : Current.Game.drugPolicyDatabase.AllPolicies.FirstOrDefault(p => p.id == policyId);
            switch ((DrugList)list)
            {
                case DrugList.Infection: DrugsInfection = policy; break;
                case DrugList.Surgery: DrugsSurgery = policy; break;
                case DrugList.Injuries: DrugsInjuries = policy; break;
                case DrugList.Scars: DrugsScars = policy; break;
                default: DrugsAllPatients = policy; break;
            }
        }

        public void SetDrugTeetotalSynced(int list, bool value)
        {
            switch ((DrugList)list)
            {
                case DrugList.Infection: DrugsInfectionTeetotal = value; break;
                case DrugList.Surgery: DrugsSurgeryTeetotal = value; break;
                case DrugList.Injuries: DrugsInjuriesTeetotal = value; break;
                case DrugList.Scars: DrugsScarsTeetotal = value; break;
                default: DrugsAllTeetotal = value; break;
            }
        }

        // Adds the "Administer X" bills the drug policies call for (see DrugUtility.ScheduleFor).
        public void ScheduleDrugBills()
        {
            if (DrugsAllPatients == null && DrugsInfection == null && DrugsSurgery == null && DrugsInjuries == null && DrugsScars == null) return;
            int now = Find.TickManager.TicksGame;
            foreach (var pair in Patients.ToList())
            {
                try
                {
                    DrugUtility.ScheduleFor(this, pair.Key, pair.Value, now);
                }
                catch (Exception e)
                {
                    Log.Warning($"Hospital: scheduling drugs failed for {pair.Key}: {e.Message}");
                }
            }
        }

        // A tracked severity above this counts as "still sick" when the hediff disappears.
        // Natural healing lowers severity gradually (a few hundredths per 250-tick check), so a
        // hediff last seen at or below this value faded naturally; one last seen above it
        // vanished abruptly (instant cure by another mod, healer serum, dev tools...).
        private const float AbruptVanishSeverityThreshold = 0.1f;

        // Purely state-based (no knowledge of any cure source). Scans hediffSet.hediffs directly
        // because mods may remove hediffs from the list without refreshing caches.
        private void CheckExternallyCuredPatients()
        {
            foreach (var pair in Patients.ToList())
            {
                Pawn pawn = pair.Key;
                PatientData data = pair.Value;
                if (pawn == null || data == null || data.Dismissed || !pawn.Spawned || pawn.health?.hediffSet == null) continue;
                if (!data.HasPendingSurgeryBill || data.CureRecipe == null) continue;
                try
                {
                    CheckExternallyCured(pawn, data);
                }
                catch (Exception e)
                {
                    Log.Warning($"Hospital: cured-externally check failed for {pawn}: {e.Message}");
                }
            }
        }

        private void CheckExternallyCured(Pawn pawn, PatientData data)
        {
            RecipeDef recipe = data.CureRecipe;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            Bill_Medical bill = pawn.health.surgeryBills.Bills.OfType<Bill_Medical>().FirstOrDefault(b => b.recipe == recipe);

            if (bill == null)
            {
                // The bill was removed without going through BillStack.Delete (e.g. the game drops
                // a bill that is no longer valid after the condition was cured), so our Delete
                // prefix never cleared the flag and the patient would wait forever.
                ResolveVanishedBill(pawn, data, recipe, hediffs);
                return;
            }

            bool cured = false;
            bool faded = false;

            if (recipe.removesHediff != null)
            {
                if (data.TrackedHediffSeverity < 0f) return;
                BodyPartRecord part = bill?.Part;
                Hediff found = hediffs.FirstOrDefault(h => h.def == recipe.removesHediff && (part == null || h.Part == part));
                if (found != null)
                {
                    // Chronic hediffs (tumors) report ~0 severity and never fade: keep their floor.
                    if (found is Hediff_Injury || found.Severity > AbruptVanishSeverityThreshold)
                        data.TrackedHediffSeverity = Mathf.Max(found.Severity, 0.01f);
                    return;
                }
                if (data.TrackedHediffSeverity > AbruptVanishSeverityThreshold) cured = true;
                else faded = true;
            }
            else if (data.PartMissingAtArrival)
            {
                BodyPartRecord part = bill?.Part;
                if (part == null) return;
                if (pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(part)) return;
                // Vanilla adds one MissingPart hediff per part of the subtree (leg -> foot -> toes) and
                // MedPod removes them one by one, parent first. So descendants must be checked too,
                // or the bill looks cured while the foot/toes are still missing. Parts under a
                // prosthetic are skipped (MedPod skips them as well). Raw list: MedPod edits it directly.
                HediffSet hSet = pawn.health.hediffSet;
                bool stillMissing = hediffs.Any(h => h is Hediff_MissingPart && h.Part != null
                    && (IsSelfOrAncestor(h.Part, part) || IsSelfOrAncestor(part, h.Part))
                    && !hSet.PartOrAnyAncestorHasDirectlyAddedParts(h.Part));
                if (stillMissing) return;
                cured = true;
            }
            else if (data.TrackedHediffSeverity >= 0f)
            {
                BodyPartRecord part = bill?.Part;
                if (part == null) return;
                float sum = 0f;
                bool any = false;
                foreach (Hediff h in hediffs)
                {
                    if (h is Hediff_Injury && h.Part == part) { any = true; sum += h.Severity; }
                }
                if (any)
                {
                    data.TrackedHediffSeverity = sum;
                    return;
                }
                // Faded naturally: the transplant bill stays valid, as before.
                if (data.TrackedHediffSeverity > AbruptVanishSeverityThreshold) cured = true;
                else return;
            }
            else return;

            if (cured)
            {
                Billing_Surgery_Patch.BillingHelper.BillForSurgery(pawn, bill);
                Messages.Message("HospitalConditionCuredExternally".Translate(pawn.NameShortColored), pawn, MessageTypeDefOf.PositiveEvent);
            }
            else if (faded)
            {
                // Surgery is impossible now; no charge. Release the patient instead of leaving them stuck.
            }
            // Deleting the bill fires the BillStack.Delete prefix (clears HasPendingSurgeryBill).
            pawn.health.surgeryBills.Delete(bill);
            data.TrackedHediffSeverity = -1f;
            data.PartMissingAtArrival = false;
        }

        // True if anc is p itself or one of its ancestors.
        private static bool IsSelfOrAncestor(BodyPartRecord anc, BodyPartRecord p)
        {
            for (; p != null; p = p.parent) if (p == anc) return true;
            return false;
        }

        // Without the bill we no longer know the exact part, so judge by the recipe's target
        // part defs. The surgery can't happen anymore either way, so the flag is always cleared;
        // the surgery price is charged only when the condition is verifiably cured.
        private void ResolveVanishedBill(Pawn pawn, PatientData data, RecipeDef recipe, List<Hediff> hediffs)
        {
            bool cured = false;
            if (recipe.removesHediff != null)
            {
                cured = data.TrackedHediffSeverity > AbruptVanishSeverityThreshold
                        && !hediffs.Any(h => h.def == recipe.removesHediff);
            }
            else if (data.PartMissingAtArrival)
            {
                List<BodyPartDef> targets = recipe.appliedOnFixedBodyParts;
                bool Targeted(BodyPartRecord p) => p != null && (targets.NullOrEmpty() || targets.Contains(p.def));
                // Descendants (foot/toes) carry their own MissingPart hediffs, so check ancestors too.
                bool TargetedOrUnder(BodyPartRecord p)
                {
                    for (; p != null; p = p.parent) if (Targeted(p)) return true;
                    return false;
                }
                bool stillMissing = hediffs.Any(h => h is Hediff_MissingPart && h.Part != null
                    && TargetedOrUnder(h.Part)
                    && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(h.Part));
                bool prosthetic = hediffs.Any(h => h is Hediff_AddedPart && Targeted(h.Part));
                cured = !stillMissing && !prosthetic;
            }
            // Organ injuries: the exact organ is unknown without the bill - release without charge.

            if (cured)
            {
                Billing_Surgery_Patch.BillingHelper.BillForSurgery(pawn, recipe);
                Messages.Message("HospitalConditionCuredExternally".Translate(pawn.NameShortColored), pawn, MessageTypeDefOf.PositiveEvent);
            }
            data.HasPendingSurgeryBill = false;
            data.TrackedHediffSeverity = -1f;
            data.PartMissingAtArrival = false;
        }

        // Safety net for patients that left without passing one of our hooks (kidnapped, carried
        // off the map, destroyed by another mod, ...). They would keep a bed reserved forever.
        private void RemoveLostPatients()
        {
            if (Patients.RemoveAll(pair => pair.Key == null || pair.Key.Destroyed || pair.Key.MapHeld != map) > 0)
            {
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }
        }

        // Finds the hospital a pawn is registered at, also when the pawn is not spawned
        // (carried, in a container, already off the map) and so has no Map of its own.
        public static HospitalMapComponent FindHospitalOf(Pawn pawn)
        {
            if (pawn == null) return null;
            foreach (var m in Find.Maps)
            {
                var hospital = m.GetComponent<HospitalMapComponent>();
                if (hospital != null && hospital.Patients.ContainsKey(pawn)) return hospital;
            }
            return null;
        }

        public bool IsOpen()
        {
            if (!openForBusiness) return false;
            return openingHours[GenLocalDate.HourOfDay(map)];
        }

        public void PatientArrived(Pawn pawn, PatientData data)
        {
            Patients.Add(pawn, data);
            if (Prefs.DevMode) PatientUtility.LogBill(pawn, data.Bill, data.Bill, "base fee");
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
        
        // Splits a payment into stacks of at most stackLimit. Rounds to whole silver; nothing for < 1.
        public static List<int> SilverStacks(float silver, int stackLimit)
        {
            var stacks = new List<int>();
            int count = Mathf.RoundToInt(silver);
            int limit = Mathf.Max(1, stackLimit);
            while (count > 0)
            {
                int stack = Mathf.Min(count, limit);
                stacks.Add(stack);
                count -= stack;
            }
            return stacks;
        }

        public void PatientLeaves(Pawn pawn)
        {
            if (Patients.TryGetValue(pawn, out var patientData) && !patientData.Dismissed)
            {
                float silver = PatientUtility.CalculateSilverToReceive(pawn, patientData);
                silver = Mathf.Clamp(silver, 0, 4000);

                // Goodwill and the departure message are not conditional on the bill. A patient
                // can legitimately owe nothing - an implant install inflicts no wound, so nothing
                // adds to their bill - and those patients used to walk out with no payment, no
                // goodwill and no message at all, which reads as the mod doing nothing.
                if (pawn.Faction != null)
                {
                    int goodwill = PatientUtility.CalculateGoodwillToGain(pawn, patientData);
                    Messages.Message(
                        "HospitalPatientLeaves".Translate(pawn.NameFullColored, silver.ToStringMoney(), goodwill,
                            pawn.Faction.name), MessageTypeDefOf.NeutralEvent);
                    pawn.Faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwill, false);
                if (Prefs.DevMode) Log.Message($"[Hospital] Bill paid {pawn.LabelShort}: {silver:0.#} silver (bill {patientData.Bill:0.#})");
                }
                else
                {
                    Messages.Message("HospitalPatientLeavesNoFaction".Translate(pawn.NameFullColored, silver.ToStringMoney()),
                        MessageTypeDefOf.NeutralEvent);
                }

                foreach (int stack in SilverStacks(silver, ThingDefOf.Silver.stackLimit))
                {
                    var silverThing = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silverThing.stackCount = stack;
                    GenPlace.TryPlaceThing(silverThing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }

                RemoveFromPatientList(pawn);
            }
            else
            {
                //Log.Message($"{pawn.NameFullColored} leaves but is not a patient anymore?");   
            }
        }

        /// <summary>
        /// True for medical deaths (no damage info: illness, blood loss, failed surgery) and deaths caused by the player's faction.
        /// </summary>
        public static bool PlayerResponsibleForDeath(DamageInfo? dinfo)
        {
            if (dinfo == null) return true;
            return dinfo.Value.Instigator?.Faction == Faction.OfPlayer;
        }

        public void PatientDied(Pawn pawn, DamageInfo? dinfo = null)
        {
            if (Patients.TryGetValue(pawn, out var patientData))
            {
                if (pawn.Faction != null)
                {
                    if (HospitalMod.Settings.PenalizeNonPlayerDeaths || PlayerResponsibleForDeath(dinfo))
                    {
                        int penalty = HospitalMod.Settings.DeathGoodwillPenalty;
                        Messages.Message("HospitalPatientDied".Translate(pawn.NameFullColored, penalty, pawn.Faction.name),
                            MessageTypeDefOf.PawnDeath);
                        pawn.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -penalty, false);
                    }
                    else
                    {
                        Messages.Message("HospitalPatientDiedNoPenalty".Translate(pawn.NameFullColored, pawn.Faction.name),
                            MessageTypeDefOf.PawnDeath);
                    }
                }
                RemoveFromPatientList(pawn);
                PatientLeftTheMap(pawn);
            }
            // else - was not a patient?
        }
        
        public void SurgeryFailed(Pawn pawn)
        {
            if (Patients.TryGetValue(pawn, out var patientData))
            {
                if (pawn.Faction != null)
                {
                    int penalty = HospitalMod.Settings.SurgeryFailGoodwillPenalty;
                    Messages.Message("HospitalSurgeryFailed".Translate(pawn.NameFullColored, penalty, pawn.Faction.name),
                        MessageTypeDefOf.NegativeEvent);
                    pawn.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -penalty, false);
                }
                patientData.Bill = 0f;
                patientData.SurgeryFailedTick = Find.TickManager.TicksGame;
                // Temporary: the patient stays registered and recovers; Trigger_SentAway sends them home afterwards.
            }
            // else - was not a patient?
        }        
        
        public void DismissPatient(Pawn pawn)
        {
            if (Patients.TryGetValue(pawn, out var patientData) && !patientData.Dismissed)
            {
                // Clear pending surgery flag when manually dismissing
                if (Prefs.DevMode) Log.Message($"[Hospital] Bill reset {pawn.LabelShort} (was {patientData.Bill:0.#}, surgery failed)");
                // (handles case where user removed surgery bill but patient is stuck)
                patientData.HasPendingSurgeryBill = false;

                Messages.Message("HospitalPatientDismissed".Translate(pawn.NameFullColored), MessageTypeDefOf.NeutralEvent);
                RemoveFromPatientList(pawn);
            }
            // else - was not a patient?

        }

        private void RemoveFromPatientList(Pawn pawn)
        {
            if (Patients.TryGetValue(pawn, out var patientData))
            {
                patientData.Dismissed = true;
            }
            DrugUtility.RemoveAdministerBills(pawn); // leftover drug bills must not outlive the stay
            pawn.playerSettings.selfTend = true; // in case the patient gets hurt while walking home
            pawn.guest.SetGuestStatus(null); // might fix the "patients stay hanging around" issue
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
        
        public void PatientLeftTheMap(Pawn pawn)
        {
            Patients.Remove(pawn);
        }

        // A patient the player captured and recruited is now a colonist. Purge any lingering
        // patient state so they behave like a normal colonist: drop the roster entry and clear
        // the forced "Patient" go-to-bed duty (otherwise they refuse to work and just wander).
        public void StopBeingPatient(Pawn pawn)
        {
            if (pawn == null) return;
            bool wasKnown = Patients.Remove(pawn);
            if (pawn.mindState?.duty?.def != null && pawn.mindState.duty.def.defName == "Patient")
            {
                pawn.mindState.duty = null;
            }
            if (wasKnown) MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        public bool IsSurgeryRecipeAllowed(RecipeDef recipe)
        {
            return !refusedOperations.Exists(def => def.Equals(recipe));
        }

        public bool IsDiseaseAllowed(HediffDef disease)
        {
            return !refusedDiseases.Exists(def => def.Equals(disease));
        }

        public bool IsFactionAllowed(FactionDef faction)
        {
            return !refusedFactions.Exists(def => def.Equals(faction));
        }

        // Generic toggles used by the blacklist dialog (registered as Multiplayer sync methods).
        public void SetRefusedOperation(RecipeDef recipe, bool refused)
        {
            if (refused) { if (!refusedOperations.Contains(recipe)) refusedOperations.Add(recipe); }
            else refusedOperations.Remove(recipe);
        }

        public void SetRefusedDisease(HediffDef disease, bool refused)
        {
            if (refused) { if (!refusedDiseases.Contains(disease)) refusedDiseases.Add(disease); }
            else refusedDiseases.Remove(disease);
        }

        public void SetRefusedFaction(FactionDef faction, bool refused)
        {
            if (refused) { if (!refusedFactions.Contains(faction)) refusedFactions.Add(faction); }
            else refusedFactions.Remove(faction);
        }

        public void RefuseOperation(Pawn pawn, RecipeDef recipe)
        {
            if (!refusedOperations.Exists(def => def.Equals(recipe)))
            {
                refusedOperations.Add(recipe);
                Messages.Message("HospitalBlacklisted".Translate(recipe.LabelCap), MessageTypeDefOf.NeutralEvent);
            }

            DismissPatient(pawn);
        }
        
        public void UnRefuseOperation(RecipeDef recipe)
        {
            if (refusedOperations.Exists(def => def.Equals(recipe)))
            {
                refusedOperations.Remove(recipe);
            }
        }

        public void SetPatientFoodPolicy(FoodPolicy localRestriction)
        {
            PatientFoodPolicy = localRestriction;
        }

        public bool IsFull()
        {
            // check if we have enough beds left for colonists
            return FreeMedicalBeds() <= 0;
        }

        public int FreeMedicalBeds()
        {
            return BedCount() - Patients.Count(pair => !pair.Value.Dismissed);            
        }

        public int BedCount()
        {
            return map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().Count(bed => bed.Medical
                && !bed.ForPrisoners
                && bed.def.building.bed_humanlike
                && !bed.IsBurning()
                && bed.Spawned
                && bed.TryGetComp<CompHospitalBed>() != null
                && bed.TryGetComp<CompHospitalBed>().Hospital
                && bed.Map == map);
        }

        public IEnumerable<Pawn> ActivePatientsList()
        {
            return Patients.Where(pair => !pair.Value.Dismissed).Select(pair => pair.Key);
        }

        public bool GetPatientData(Pawn pawn, out PatientData patientData)
        {
            return Patients.TryGetValue(pawn, out patientData);
        }

        public void AddSurgeryBill(Pawn pawn, float amount, string reason = null)
        {
            if (pawn == null) return;
            if (!Patients.TryGetValue(pawn, out var patientData)) return;
            patientData.Bill += amount;
            patientData.Bill = Mathf.Clamp(patientData.Bill, 0, 4000);
        }
    }

 
}            float before = patientData.Bill;
            PatientUtility.LogBill(pawn, patientData.Bill - before, patientData.Bill, reason);
