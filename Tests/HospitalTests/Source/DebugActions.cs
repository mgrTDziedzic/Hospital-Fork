using System;
using System.Linq;
using Hospital;
using LudeonTK;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Manual testing helpers: force the hospital incidents on the current map, regardless of
    // opening hours, danger settings or the per-map mass casualty switch. Those settings are
    // changed only for the duration of the incident and restored right after.
    public static class HospitalDebugActions
    {
        [DebugAction("Hospital Debug", "Patient arrives (forced)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PatientArrives()
        {
            FireForced("PatientArrives", hospital => { });
        }

        [DebugAction("Hospital Debug", "Mass casualty event (forced)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MassCasualtyEvent()
        {
            FireForced("MassCasualtyEvent", hospital =>
            {
                hospital.MassCasualties = true;
                hospital.MceAcceptDanger = true;
            });
        }

        private static void FireForced(string incidentDefName, Action<HospitalMapComponent> extraSettings)
        {
            Map map = Find.CurrentMap;
            var hospital = map?.GetComponent<HospitalMapComponent>();
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDefName);
            if (hospital == null || def == null)
            {
                Messages.Message("Hospital Debug: no HospitalMapComponent or IncidentDef " + incidentDefName, MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool open = hospital.openForBusiness;
            var hours = hospital.openingHours;
            bool danger = hospital.AcceptDanger;
            bool mce = hospital.MassCasualties;
            bool mceDanger = hospital.MceAcceptDanger;
            bool fired;
            try
            {
                hospital.openForBusiness = true;
                hospital.openingHours = Enumerable.Repeat(true, GenDate.HoursPerDay).ToList();
                hospital.AcceptDanger = true;
                extraSettings(hospital);

                var parms = new IncidentParms { target = map };
                // TryExecuteWorker is protected on IncidentWorker; the publicized Hospital overrides are public.
                switch (def.Worker)
                {
                    case IncidentWorker_PatientArrives w: fired = w.TryExecuteWorker(parms); break;
                    case IncidentWorker_MassCasualtyEvent w: fired = w.TryExecuteWorker(parms); break;
                    default: fired = false; break;
                }
            }
            finally
            {
                hospital.openForBusiness = open;
                hospital.openingHours = hours;
                hospital.AcceptDanger = danger;
                hospital.MassCasualties = mce;
                hospital.MceAcceptDanger = mceDanger;
            }

            if (!fired)
            {
                string reason = hospital.IsFull()
                    ? "no free hospital bed (medical bed with the Hospital toggle on)"
                    : "no eligible faction, no reachable map edge / landing cell, or no suitable pawn (see log)";
                Messages.Message("Hospital Debug: " + incidentDefName + " did not fire: " + reason, MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
