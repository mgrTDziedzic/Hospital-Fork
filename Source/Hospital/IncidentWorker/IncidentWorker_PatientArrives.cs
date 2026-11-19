using System.Collections.Generic;
using System.Linq;
using Hospital.Utilities;
using RimWorld;
using Verse;
using Verse.AI.Group;
#if RW15
// 1.6 renamed ActiveDropPodInfo to ActiveTransporterInfo. Same shape, same members.
using ActiveTransporterInfo = RimWorld.ActiveDropPodInfo;
#endif

namespace Hospital
{
    // modified IncidentWorker_VisitorGroup
    public class IncidentWorker_PatientArrives : IncidentWorker
    {
        public override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
            {
                return false;
            }

            Map map = (Map)parms.target;
            return IncidentHelper.CanSpawnPatient(map);
        }
        
        public override bool TryExecuteWorker(IncidentParms parms)
        {
            return TryExecute(parms, null);
        }

        // spec == null: normal random arrival. Otherwise the spec fixes type / wounds / disease / surgery (debug).
        public bool TryExecute(IncidentParms parms, PatientSpec spec)
        {
            Map map = (Map)parms.target;
            if (!IncidentHelper.CanSpawnPatient(map))
            {
                return false;
            }
            HospitalMapComponent hospital = map.GetComponent<HospitalMapComponent>();
            List<Faction> potentialPatientFactions = IncidentHelper.GetPotentialPatientFactions(map, hospital, out bool manhunterOnMap);
            if (manhunterOnMap && !hospital.AcceptDanger) return false; // not a good idea to visit now :)

            if (potentialPatientFactions.Count == 0) return false;

            // Resolve the landing cell before generating anything, so a failure leaves no half-created patient.
            if (!IncidentHelper.TryFindPatientLandingCell(map, out var loc)) return false;

            // Bounded retries: a faction that only yields androids / non-bleeding pawns must not freeze the game.
            Pawn pawn;
            Faction patientFaction;
            RecipeDef forcedSurgery = spec?.Surgery;
            bool generated;
            if (forcedSurgery == null)
            {
                generated = IncidentHelper.TryGenerateAcceptedPawn(potentialPatientFactions, IncidentHelper.IsSuitablePatient,
                    IncidentHelper.MaxPawnGenerationAttempts, out pawn, out patientFaction);
            }
            else
            {
                // Debug: a forced surgery needs a pawn it can be done on (optional mod hints + validity check).
                generated = IncidentHelper.TryGenerateAcceptedPawn(potentialPatientFactions,
                    p => IncidentHelper.IsSuitablePatient(p) && SurgeryUtility.CanForceOn(p, forcedSurgery),
                    IncidentHelper.MaxForcedSurgeryAttempts, out pawn, out patientFaction,
                    f => IncidentHelper.GeneratePawn(f, forcedSurgery));
            }
            if (!generated)
            {
                if (forcedSurgery != null)
                {
                    string failText = "Hospital Debug: no pawn found that surgery " + forcedSurgery.defName + " can be done on";
                    Log.Message(failText);
                    Messages.Message(failText, MessageTypeDefOf.RejectInput, false);
                    spec.FailureReported = true;
                }
                else
                {
                    Log.Message("Hospital: could not generate a suitable patient, skipping this arrival");
                }
                return false;
            }
            parms.faction = patientFaction;

            PatientData patient = SpawnPatient(map, pawn, loc, spec);
            var list = new List<Pawn> { pawn };
            LordMaker.MakeNewLord(parms.faction, CreateLordJob(parms, list), map, list);
            //pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Patient"), pawn.Position, 1000);
            var diagnosis = patient.Diagnosis;
            TaggedString text = def.letterText.Formatted(pawn.Named("PAWN"), diagnosis).AdjustedFor(pawn);
            if (patient.Sedated)
            {
                text += "\n\n" + "PatientSedatedWarning".Translate(HospitalMod.Settings.SedationHours.ToString("0.#"));
            }
            //text += " " + patient.baseCost.ToStringMoney();
            TaggedString title = def.letterLabel.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn);
            if (HospitalMod.Settings.ShowMessageAtArrival)
            {
                Messages.Message(title + ": " + text, MessageTypeDefOf.PositiveEvent);                
            }
            else
            {
                SendStandardLetter(title, text, LetterDefOf.PositiveEvent, parms, pawn);
            }

            return true;
        }

        protected virtual PatientData SpawnPatient(Map map, Pawn pawn, IntVec3 loc, PatientSpec spec = null)
        {
            HospitalMapComponent hospital = map.GetComponent<HospitalMapComponent>();
            PatientData data = IncidentHelper.PrepareAndInjure(map, hospital, pawn, loc, spec, out _);

            // Now scoop the (already damaged) pawn back into a drop pod for the arrival animation.
            if (pawn.Spawned) pawn.DeSpawn();
            var activeDropPodInfo = new ActiveTransporterInfo();
            activeDropPodInfo.innerContainer.TryAdd(pawn, 1);
            activeDropPodInfo.openDelay = 60;
            activeDropPodInfo.leaveSlag = false;
            activeDropPodInfo.despawnPodBeforeSpawningThing = true;
            activeDropPodInfo.spawnWipeMode = WipeMode.Vanish;
            DropPodUtility.MakeDropPodAt(loc, map, activeDropPodInfo);

            hospital.PatientArrived(pawn, data);
            // this hack is needed to cancel the current patient goes to bed job and start a new one
            pawn.jobs.StopAll();
            pawn.jobs.JobTrackerTick();
            pawn.foodRestriction = new Pawn_FoodRestrictionTracker(pawn)
            {
                CurrentFoodPolicy = hospital.PatientFoodPolicy
            };
            return data;
        }
        
        protected virtual LordJob_VisitColonyAsPatient CreateLordJob(IncidentParms parms, List<Pawn> pawns)
        {
            //RCellFinder.TryFindRandomSpotJustOutsideColony(pawns[0], out var result);
            return new LordJob_VisitColonyAsPatient(parms.faction);
        }
    }
}