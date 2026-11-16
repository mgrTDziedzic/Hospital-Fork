using System;
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
    public class IncidentWorker_MassCasualtyEvent : IncidentWorker
    {
        public enum MCEType
        {
            Pandemic = 0,
            Raid = 1,
            Crash = 2,
            Fire = 3
        }
        
        public override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
            {
                return false;
            }
            
            Map map = (Map)parms.target;
            if (!map.GetComponent<HospitalMapComponent>().MassCasualties)
            {
                return false;
            }            

            return IncidentHelper.CanSpawnPatient(map);
        }

        public override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            HospitalMapComponent hospital = map.GetComponent<HospitalMapComponent>();
            if (!hospital.MassCasualties)
            {
                return false;
            }            
            if (!IncidentHelper.CanSpawnPatient(map))
            {
                return false;
            }
            if (IncidentHelper.MapTooDangerousForMce(map, hospital)) return false;

            if (parms.pawnCount == 0)
            {
                parms.pawnCount = RollPawnCount(hospital.BedCount());
            }
            if (parms.pawnCount <= 0) return false; // no beds: an empty event would only send a letter about 0 pawns
            List<Faction> factions = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden && !f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction && !f.def.defName.ToUpper().Contains("VREA") && hospital.IsFactionAllowed(f.def)).ToList();
            if (factions.Count == 0) return false;
            parms.faction = factions.RandomElement();
            /*foreach (Faction def in factions)
            {
                Log.Message(def.def.defName);
            } */
            // Resolve the landing cell(s) before generating any pawn, so a failure leaves no half-created patients.
            var landingCells = new List<IntVec3>();
            for (int i = 0; i < parms.pawnCount; i++)
            {
                if (!IncidentHelper.TryFindPatientLandingCell(map, out var cell)) return false;
                landingCells.Add(cell);
            }

            List<Pawn> list = new List<Pawn>();
            list.AddRange(IncidentHelper.GetKnownPawns(parms).InRandomOrder().Take((int)Math.Floor(parms.pawnCount * 0.20f)).ToList());
            int pawnsToGenerate = parms.pawnCount - list.Count;
            var oneFaction = new List<Faction> { parms.faction };
            for (int i = 0; i < pawnsToGenerate; i++)
            {
                // bounded retries; skip androids / non-bleeding pawns like a normal arrival
                if (!IncidentHelper.TryGenerateAcceptedPawn(oneFaction, IncidentHelper.IsSuitablePatient,
                        IncidentHelper.MaxPawnGenerationAttempts, out Pawn generated, out _)) break;
                list.Add(generated);
            }
            if (list.Count == 0) return false;

            MCEType type = (MCEType)Rand.Range(0, 4);
            // MCE pandemics use the plague: honour the disease blacklist
            if (type == MCEType.Pandemic && !hospital.IsDiseaseAllowed(HediffDefOf.Plague))
            {
                type = (MCEType)Rand.Range(1, 4);
            }

            for (int i = 0; i < list.Count; i++)
            {
                var pawn = list[i];
                SpawnPatient(map, pawn, type, landingCells[Math.Min(i, landingCells.Count - 1)]);
                LordMaker.MakeNewLord(parms.faction, CreateLordJob(parms, list), map, new List<Pawn> { pawn });
            }

            //pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("Patient"), pawn.Position, 1000);
            //var diagnosis = patient.Diagnosis;
            // a pandemic, a raid (gunshot wounds), a transport pod crash (scratches,bruises,etc), a colony fire (burns)
            TaggedString text = def.letterText.Formatted(("MCE_"+type).Translate(), list.Count);
            //text += " " + patient.baseCost.ToStringMoney();
            TaggedString title = def.letterLabel;
            SendStandardLetter(title, text, LetterDefOf.ThreatSmall, parms, list);

            return true;
        }

        // A bit more than the hospital can handle. Can be <= 0 (no beds).
        public static int RollPawnCount(int bedCount)
        {
            return bedCount + Rand.Range(-bedCount / 5, 1);
        }

        protected virtual PatientData SpawnPatient(Map map, Pawn pawn, MCEType type, IntVec3 loc)
        {
            IncidentHelper.SetUpNewPatient(pawn);
            
            PatientType ptype = PatientType.Wounds;
            if (type.Equals(MCEType.Pandemic)) ptype = PatientType.Disease;
            
            //type = PatientType.Surgery; // debug
            //Log.Message(pawn.Label + " -> " +type.ToString());
            PatientData data = new PatientData(GenDate.TicksGame, pawn.MarketValue, pawn.needs.mood.curLevelInt, ptype);
            //TryFindEntryCell(map, out var cell);
            //GenSpawn.Spawn(pawn, cell, map);
            HospitalMapComponent hospital = map.GetComponent<HospitalMapComponent>();

            // Spawn the pawn on the map first so it has a valid Map while we apply damage/disease.
            // Damaging an unspawned pawn (pawn.Map == null) collides with hediff comps and other
            // mods' patches that dereference pawn.Map (sound makers, FleckMaker, CompPostPostAdd, ...).
            GenSpawn.Spawn(pawn, loc, map);
            PatientUtility.DamagePawn(pawn, data, hospital, type);

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