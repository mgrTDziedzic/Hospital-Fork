using System;
using System.Collections.Generic;
using System.Linq;
using Hospital;
using Hospital.Utilities;
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
        private const string Category = "Hospital Debug";

        [DebugAction(Category, "Patient arrives (forced)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PatientArrives()
        {
            FireForced("PatientArrives", (hospital, parms) => ((IncidentWorker_PatientArrives)IncidentWorkerOf("PatientArrives")).TryExecute(parms, null));
        }

        [DebugAction(Category, "Mass casualty event (forced)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MassCasualtyEvent()
        {
            FireForced("MassCasualtyEvent", (hospital, parms) => ((IncidentWorker_MassCasualtyEvent)IncidentWorkerOf("MassCasualtyEvent")).TryExecute(parms, null), mce: true);
        }

        // All specific actions below ignore the mod settings (surgery switch, blacklists, aggressive switch,
        // opening hours, danger, MCE toggle). Only pawn validity checks stay (a stuck or dead patient tests nothing).
        [DebugActionYielder]
        public static IEnumerable<DebugActionNode> SpecificYielder()
        {
            yield return new DebugActionNode("Patient (specific): Wounds...")
            {
                category = Category,
                childGetter = () => Enum.GetValues(typeof(WoundKind)).Cast<WoundKind>()
                    .Select(kind => Patient("Wounds: " + kind, new PatientSpec { Type = PatientType.Wounds, Wounds = kind })).ToList()
            };
            yield return new DebugActionNode("Patient (specific): Disease...")
            {
                category = Category,
                childGetter = () => DiseaseUtility.CandidateDiseases()
                    .OrderBy(d => d.LabelCap.ToString()).ThenBy(d => d.defName)
                    .Select(d => Patient(d.LabelCap + " (" + d.defName + ")", new PatientSpec { Type = PatientType.Disease, Disease = d })).ToList()
            };
            yield return new DebugActionNode("Patient (specific): Surgery...")
            {
                category = Category,
                childGetter = SurgeryNodes
            };
            // Normal-play rules (no spec): what do patients get with the current settings?
            yield return new DebugActionNode("Patient distribution sample...")
            {
                category = Category,
                childGetter = () => new List<DebugActionNode>
                {
                    new DebugActionNode("100 patients", DebugActionType.Action, () => PatientSampler.Run(100)),
                    new DebugActionNode("500 patients", DebugActionType.Action, () => PatientSampler.Run(500))
                }
            };
            yield return new DebugActionNode("Backstory sample...", DebugActionType.Action, BackstorySample) { category = Category };
            yield return new DebugActionNode("Mass casualty (specific)...")
            {
                category = Category,
                childGetter = () => Enum.GetValues(typeof(IncidentWorker_MassCasualtyEvent.MCEType)).Cast<IncidentWorker_MassCasualtyEvent.MCEType>()
                    .Select(type => new DebugActionNode(type.ToString() + "...")
                    {
                        childGetter = () => new List<DebugActionNode>
                        {
                            Mce(type, "Default count", 0), Mce(type, "1 pawn", 1), Mce(type, "3 pawns", 3)
                        }
                    }).ToList()
            };
        }

        // Shows 20 generated wound backstories per category in a window (and logs a copy) for a throwaway test pawn.
        private static void BackstorySample()
        {
            bool openBefore = Log.openOnMessage;
            Log.openOnMessage = false;
            Pawn pawn = null;
            try
            {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer));
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Hospital backstory sample (pawn " + pawn.Name.ToStringShort + ")");
                foreach (WoundCategory category in Enum.GetValues(typeof(WoundCategory)))
                {
                    sb.AppendLine();
                    sb.AppendLine("== " + category);
                    for (int i = 0; i < 20; i++)
                    {
                        int seed = Rand.Int;
                        sb.AppendLine(BackstoryUtility.Resolve(pawn, seed, category));
                        sb.AppendLine("    " + BackstoryUtility.ResolveLeave(pawn, seed, category));
                    }
                }
                foreach (DiseaseCategory category in Enum.GetValues(typeof(DiseaseCategory)))
                {
                    sb.AppendLine();
                    string defName = category == DiseaseCategory.Insect ? "Malaria" : category == DiseaseCategory.Parasite ? "GutWorms"
                        : category == DiseaseCategory.Infection ? "WoundInfection" : category == DiseaseCategory.Toxic ? "ToxicBuildup" : "Flu";
                    var flu = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                    sb.AppendLine("== Disease " + category + " (" + defName + ")");
                    for (int i = 0; i < 20; i++)
                    {
                        int seed = Rand.Int;
                        sb.AppendLine(BackstoryUtility.Resolve(pawn, seed, category, flu));
                        sb.AppendLine("    " + BackstoryUtility.ResolveLeave(pawn, seed, category, flu));
                    }
                }
                string text = sb.ToString();
                Log.Message(System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", ""));
                Find.WindowStack.Add(new Window_TextSample("Backstory sample", text));
            }
            finally
            {
                Log.openOnMessage = openBefore;
                // Generated, never passed to the world: discard without WorldPawns warnings.
                if (pawn != null) IncidentHelper.DiscardGeneratedPawn(pawn, silent: true);
            }
        }

        private static List<DebugActionNode> SurgeryNodes()
        {
            var list = new List<DebugActionNode>
            {
                Patient("Random surgery", new PatientSpec { Type = PatientType.Surgery })
            };
            var recipes = SurgeryUtility.CandidateSurgeries(allowAggressive: true)
                .OrderBy(r => r.LabelCap.ToString()).ThenBy(r => r.defName).ToList();
            DebugActionNode Node(RecipeDef r) =>
                Patient(r.LabelCap + " (" + r.defName + ")", new PatientSpec { Type = PatientType.Surgery, Surgery = r });
            if (recipes.Count <= 40)
            {
                list.AddRange(recipes.Select(Node));
            }
            else
            {
                foreach (var group in recipes.GroupBy(r => char.ToUpperInvariant(r.LabelCap.ToString().FirstOrDefault())).OrderBy(g => g.Key))
                {
                    var recipesInGroup = group.ToList();
                    list.Add(new DebugActionNode(group.Key + " (" + recipesInGroup.Count + ")...")
                    {
                        childGetter = () => recipesInGroup.Select(Node).ToList()
                    });
                }
            }
            return list;
        }

        private static DebugActionNode Patient(string label, PatientSpec spec)
        {
            spec.IgnoreRestrictions = true;
            return new DebugActionNode(label, DebugActionType.Action, () =>
                FireForced("PatientArrives", (hospital, parms) => ((IncidentWorker_PatientArrives)IncidentWorkerOf("PatientArrives")).TryExecute(parms, spec), spec: spec));
        }

        private static DebugActionNode Mce(IncidentWorker_MassCasualtyEvent.MCEType type, string label, int count)
        {
            return new DebugActionNode(label, DebugActionType.Action, () =>
                FireForced("MassCasualtyEvent", (hospital, parms) =>
                {
                    parms.pawnCount = count; // 0 = default (bed based)
                    return ((IncidentWorker_MassCasualtyEvent)IncidentWorkerOf("MassCasualtyEvent")).TryExecute(parms, type);
                }, mce: true));
        }

        private static IncidentWorker IncidentWorkerOf(string defName) => DefDatabase<IncidentDef>.GetNamed(defName).Worker;

        private static void FireForced(string incidentDefName, Func<HospitalMapComponent, IncidentParms, bool> fire, bool mce = false, PatientSpec spec = null)
        {
            Map map = Find.CurrentMap;
            var hospital = map?.GetComponent<HospitalMapComponent>();
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDefName);
            if (hospital == null || def == null)
            {
                Messages.Message("Hospital Debug: no HospitalMapComponent or IncidentDef " + incidentDefName, MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool logOpenBefore = Log.openOnMessage;
            Log.openOnMessage = false; // the arrival writes dev-mode log lines; do not pop the log window open
            bool open = hospital.openForBusiness;
            var hours = hospital.openingHours;
            bool danger = hospital.AcceptDanger;
            bool mceSetting = hospital.MassCasualties;
            bool mceDanger = hospital.MceAcceptDanger;
            bool fired;
            try
            {
                hospital.openForBusiness = true;
                hospital.openingHours = Enumerable.Repeat(true, GenDate.HoursPerDay).ToList();
                hospital.AcceptDanger = true;
                if (mce)
                {
                    hospital.MassCasualties = true;
                    hospital.MceAcceptDanger = true;
                }

                var parms = new IncidentParms { target = map };
                fired = fire(hospital, parms);
            }
            finally
            {
                Log.openOnMessage = logOpenBefore;
                hospital.openForBusiness = open;
                hospital.openingHours = hours;
                hospital.AcceptDanger = danger;
                hospital.MassCasualties = mceSetting;
                hospital.MceAcceptDanger = mceDanger;
            }

            if (!fired && spec?.FailureReported != true)
            {
                string reason = hospital.IsFull()
                    ? "no free hospital bed (medical bed with the Hospital toggle on)"
                    : "no eligible faction, no reachable map edge / landing cell, or no suitable pawn (see log)";
                Messages.Message("Hospital Debug: " + incidentDefName + " did not fire: " + reason, MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
