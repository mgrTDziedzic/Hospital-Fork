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
        public List<bool> openingHours = new System.Collections.Generic.List<bool>
        {
            false,false,false,false,false,false,false,true, //8
            false,false,false,true, //12
            false,false,false,true,//16
            false,false,false,true,//20
            false,false,false,false
        };
        public List<RecipeDef> refusedOperations = new List<RecipeDef>();
        public List<HediffDef> refusedDiseases = new List<HediffDef>();
        public List<FactionDef> refusedFactions = new List<FactionDef>();
        public FoodPolicy PatientFoodPolicy;
        public bool MassCasualties;
        public bool AcceptSurgery;
        public bool AcceptDanger;
        
        public HospitalMapComponent(Map map) : base(map)
        {
            Patients = new Dictionary<Pawn, PatientData>();
            PatientFoodPolicy = Current.Game.foodRestrictionDatabase.DefaultFoodRestriction();
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            openingHours ??= new System.Collections.Generic.List<bool>
            {
                false,false,false,false,false,false,false,true, //8
                false,false,false,true, //12
                false,false,false,true,//16
                false,false,false,true,//20
                false,false,false,false
            };
            refusedOperations ??= new List<RecipeDef>();
            refusedDiseases ??= new List<HediffDef>();
            refusedFactions ??= new List<FactionDef>();

            Scribe_Collections.Look(ref openingHours, "openingHours");
            Scribe_Collections.Look(ref refusedOperations, "refusedOperations");
            Scribe_Collections.Look(ref refusedDiseases, "refusedDiseases");
            Scribe_Collections.Look(ref refusedFactions, "refusedFactions");
            refusedOperations ??= new List<RecipeDef>();
            refusedDiseases ??= new List<HediffDef>();
            refusedFactions ??= new List<FactionDef>();
            Scribe_Values.Look(ref openForBusiness, "openForBusiness", false);
            Patients ??= new Dictionary<Pawn, PatientData>();
            // logNullErrors: false - a reference to a destroyed pawn is always saved as null, so a
            // patient that was lost without being removed (see RemoveLostPatients) used to log
            // "Null key while loading dictionary" on load. The null entry itself is just skipped.
            Scribe_Collections.Look(ref Patients, "patients", LookMode.Reference, LookMode.Deep, ref _colonistsKeysWorkingList, ref _colonistsValuesWorkingList, logNullErrors: false);
            Scribe_References.Look(ref PatientFoodPolicy, "PatientFoodPolicy");
            PatientFoodPolicy ??= Current.Game.foodRestrictionDatabase.DefaultFoodRestriction();
            Scribe_Values.Look(ref MassCasualties, "massCasualties", false);
            Scribe_Values.Look(ref AcceptSurgery, "acceptSurgery", true);
            Scribe_Values.Look(ref AcceptDanger, "AcceptDanger", false);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (Find.TickManager.TicksGame % 2000 == 0) RemoveLostPatients();
            if (Find.TickManager.TicksGame % 250 == 0) CheckExternallyCuredPatients();
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
                bool stillMissing = false;
                for (BodyPartRecord p = part; p != null && !stillMissing; p = p.parent)
                {
                    stillMissing = hediffs.Any(h => h is Hediff_MissingPart && h.Part == p);
                }
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
                bool stillMissing = hediffs.Any(h => h is Hediff_MissingPart && Targeted(h.Part));
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
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
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
                }
                else
                {
                    Messages.Message("HospitalPatientLeavesNoFaction".Translate(pawn.NameFullColored, silver.ToStringMoney()),
                        MessageTypeDefOf.NeutralEvent);
                }

                if (silver > 0)
                {
                    var silverThing = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silverThing.stackCount = (int)silver;
                    GenPlace.TryPlaceThing(silverThing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }

                RemoveFromPatientList(pawn);
            }
            else
            {
                //Log.Message($"{pawn.NameFullColored} leaves but is not a patient anymore?");   
            }
        }

        public void PatientDied(Pawn pawn)
        {
            if (Patients.TryGetValue(pawn, out var patientData))
            {
                if (pawn.Faction != null)
                {
                    int penalty = HospitalMod.Settings.DeathGoodwillPenalty;
                    Messages.Message("HospitalPatientDied".Translate(pawn.NameFullColored, penalty, pawn.Faction.name),
                        MessageTypeDefOf.PawnDeath);
                    pawn.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -penalty, false);
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
                        MessageTypeDefOf.PawnDeath);
                    pawn.Faction.TryAffectGoodwillWith(Faction.OfPlayer, -penalty, false);
                }
                patientData.Bill = 0f;
                RemoveFromPatientList(pawn);
                PatientLeftTheMap(pawn);
            }
            // else - was not a patient?
        }        
        
        public void DismissPatient(Pawn pawn)
        {
            if (Patients.TryGetValue(pawn, out var patientData) && !patientData.Dismissed)
            {
                // Clear pending surgery flag when manually dismissing
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

        public void AddSurgeryBill(Pawn pawn, float amount)
        {
            if (pawn == null) return;
            if (!Patients.TryGetValue(pawn, out var patientData)) return;
            patientData.Bill += amount;
            patientData.Bill = Mathf.Clamp(patientData.Bill, 0, 4000);
        }
    }

 
}