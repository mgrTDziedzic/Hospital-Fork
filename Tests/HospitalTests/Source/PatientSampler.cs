using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Hospital;
using Hospital.Utilities;
using RimWorld;
using Verse;

namespace HospitalTests
{
    // Debug: simulates N patient arrivals with the normal-play rules (no PatientSpec) and logs what they got.
    // Shares the faction list, pawn generation, type roll and DamagePawn path with IncidentWorker_PatientArrives
    // (IncidentHelper). No PatientArrived, lord, drop pod or letter; every generated pawn is discarded afterwards.
    public static class PatientSampler
    {
        private class Sample
        {
            public PatientType Rolled;
            public PatientType Final;
            public string Detail; // surgery recipe / disease hediff / worst injury def
            public string DetailLabel;
        }

        public static void Run(int count)
        {
            Map map = Find.CurrentMap;
            var hospital = map?.GetComponent<HospitalMapComponent>();
            if (hospital == null)
            {
                Messages.Message("Hospital Debug: no HospitalMapComponent on this map", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var factions = IncidentHelper.GetPotentialPatientFactions(map, hospital, out bool manhunter);
            if (factions.Count == 0)
            {
                string t = "Hospital Debug: no eligible patient faction right now (allowed, neutral/allied, no hostile pawns on the map)";
                Log.Message(t);
                Messages.Message(t, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!IncidentHelper.TryFindPatientLandingCell(map, out var loc))
            {
                Messages.Message("Hospital Debug: no landing cell found", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var worldBefore = new HashSet<Pawn>(Find.WorldPawns.AllPawnsAliveOrDead);
            var samples = new List<Sample>();
            var filters = new FilterStats();
            int filterPawns = 0;
            int genFailures = 0, exceptions = 0;
            var watch = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                Pawn pawn = null;
                try
                {
                    if (!IncidentHelper.TryGenerateAcceptedPawn(factions, IncidentHelper.IsSuitablePatient,
                            IncidentHelper.MaxPawnGenerationAttempts, out pawn, out _))
                    {
                        genFailures++;
                        continue;
                    }
                    if (filterPawns < MaxFilterPawns) { filterPawns++; filters.AddPawn(pawn, hospital); }
                    PatientData data = IncidentHelper.PrepareAndInjure(map, hospital, pawn, loc, null, out var rolled);
                    samples.Add(Describe(pawn, data, rolled));
                }
                catch (Exception e)
                {
                    exceptions++;
                    Log.Message("Hospital Debug sampler: sample " + (i + 1) + " failed: " + e.GetType().Name + ": " + e.Message);
                }
                finally
                {
                    Cleanup(pawn);
                }
            }

            // Relatives generated together with the patients (canGeneratePawnRelations) end up in the world pawn pool.
            int relatives = 0;
            try
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead.Where(p => !worldBefore.Contains(p)).ToList())
                {
                    if (p.Spawned || p.Discarded) continue;
                    IncidentHelper.DiscardGeneratedPawn(p, silent: true);
                    relatives++;
                }
            }
            catch (Exception e)
            {
                Log.Message("Hospital Debug sampler: relative cleanup failed: " + e.Message);
            }
            int leftover = Find.WorldPawns.AllPawnsAliveOrDead.Count(p => !worldBefore.Contains(p) && !p.Discarded);
            watch.Stop();

            string summary = BuildSummary(count, samples, genFailures, exceptions, relatives, leftover, watch.Elapsed, hospital, manhunter);
            try { summary += filters.Report(map, filterPawns); }
            catch (Exception e) { summary += "\nSurgery filter breakdown failed: " + e.Message; }
            Log.Message(summary);
            Messages.Message("Hospital Debug: patient sample of " + count + " done, see log", MessageTypeDefOf.NeutralEvent, false);
        }

        private static Sample Describe(Pawn pawn, PatientData data, PatientType rolled)
        {
            var s = new Sample { Rolled = rolled, Final = data.Type };
            switch (data.Type)
            {
                case PatientType.Surgery:
                    var recipe = pawn.health.surgeryBills.Bills.OfType<Bill_Medical>().FirstOrDefault()?.recipe ?? data.CureRecipe;
                    s.Detail = recipe?.defName ?? "(none)";
                    s.DetailLabel = recipe?.label ?? data.Diagnosis;
                    break;
                case PatientType.Disease:
                    var hediff = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.Label == data.Diagnosis);
                    s.Detail = hediff?.def.defName ?? "(unknown)";
                    s.DetailLabel = data.Diagnosis;
                    break;
                default:
                    var worst = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().OrderByDescending(h => h.Severity).FirstOrDefault();
                    s.Detail = worst?.def.defName ?? "(none)";
                    s.DetailLabel = worst?.def.label ?? "";
                    break;
            }
            return s;
        }

        private static void Cleanup(Pawn pawn)
        {
            if (pawn == null) return;
            try
            {
                // Direct list clear: no BillStack.Delete patches (Hospital, other mods) run for these throwaway bills.
                pawn.health?.surgeryBills?.Bills.Clear();
                if (pawn.Spawned) pawn.DeSpawn();
            }
            catch (Exception e)
            {
                Log.Message("Hospital Debug sampler: cleanup failed: " + e.Message);
            }
            // silent: wounds leave battle log entries, generated relations leave tales; vanilla removes them without warnings.
            IncidentHelper.DiscardGeneratedPawn(pawn, silent: true);
        }

        private static string BuildSummary(int n, List<Sample> samples, int genFailures, int exceptions, int relatives, int leftover,
            TimeSpan time, HospitalMapComponent hospital, bool manhunter)
        {
            int ok = samples.Count;
            var sb = new StringBuilder();
            sb.AppendLine("Hospital Debug: patient distribution sample");
            sb.AppendLine("Requested " + n + ", recorded " + ok + ", generation failures " + genFailures + ", exceptions " + exceptions
                          + ", time " + time.TotalSeconds.ToString("0.0") + " s");
            sb.AppendLine("Settings: AcceptSurgery=" + hospital.AcceptSurgery + ", AcceptAggressive=" + hospital.AcceptAggressive
                          + (manhunter ? " (manhunter on map: a real arrival would be skipped unless danger is accepted)" : ""));
            sb.AppendLine("World pawns: " + relatives + " generated relatives discarded, " + leftover + " new pawns left in the world pool");
            if (ok == 0) return sb.ToString();

            sb.AppendLine();
            sb.AppendLine("Rolled type:");
            AppendTable(sb, samples.GroupBy(s => s.Rolled.ToString()).ToDictionary(g => g.Key, g => g.Count()), ok);
            sb.AppendLine("Final type (after fallbacks):");
            AppendTable(sb, samples.GroupBy(s => s.Final.ToString()).ToDictionary(g => g.Key, g => g.Count()), ok);
            int fallbacks = samples.Count(s => s.Rolled != s.Final);
            sb.AppendLine("Fallbacks to another type: " + fallbacks + " (" + Pct(fallbacks, ok) + ")");
            foreach (var g in samples.Where(s => s.Rolled != s.Final).GroupBy(s => s.Rolled + " -> " + s.Final).OrderByDescending(g => g.Count()))
                sb.AppendLine("  " + g.Key + ": " + g.Count());

            foreach (PatientType type in new[] { PatientType.Surgery, PatientType.Disease, PatientType.Wounds })
            {
                var list = samples.Where(s => s.Final == type).ToList();
                if (list.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine(type + " (" + list.Count + "), by " + (type == PatientType.Wounds ? "worst injury" : type == PatientType.Surgery ? "recipe" : "hediff") + ":");
                var rows = list.GroupBy(s => s.Detail).Select(g => new { Key = g.Key, Count = g.Count(), Label = g.First().DetailLabel })
                    .OrderByDescending(r => r.Count).ThenBy(r => r.Key).ToList();
                int w = Math.Max(10, rows.Max(r => r.Key.Length));
                foreach (var r in rows)
                    sb.AppendLine("  " + r.Count.ToString().PadLeft(5) + "  " + Pct(r.Count, list.Count).PadLeft(6) + "  " + r.Key.PadRight(w) + "  " + r.Label);
            }
            return sb.ToString();
        }

        private const int MaxFilterPawns = 100;
        private const int MaxListed = 12;

        // Which EligibleSurgeries check rejects which recipe (first failing check wins, same order and same code as
        // SurgeryUtility.EligibleSurgeries). Checks on the pawn are counted over the first MaxFilterPawns generated
        // pawns (before they are injured); recipe/colony checks are listed by recipe.
        private class FilterStats
        {
            private readonly Dictionary<SurgeryUtility.SurgeryFilter, HashSet<string>> recipeLevel = new Dictionary<SurgeryUtility.SurgeryFilter, HashSet<string>>();
            private readonly Dictionary<SurgeryUtility.SurgeryFilter, int> pawnLevel = new Dictionary<SurgeryUtility.SurgeryFilter, int>();
            private readonly Dictionary<RecipeDef, int> passed = new Dictionary<RecipeDef, int>();
            private int recipes;

            public void AddPawn(Pawn pawn, HospitalMapComponent hospital)
            {
                recipes = 0;
                foreach (RecipeDef recipe in pawn.def.AllRecipes)
                {
                    recipes++;
                    var f = SurgeryUtility.PawnStageRejection(pawn, hospital, recipe, false);
                    switch (f)
                    {
                        case SurgeryUtility.SurgeryFilter.None:
                            passed[recipe] = passed.TryGetValue(recipe) + 1;
                            break;
                        case SurgeryUtility.SurgeryFilter.DoesNotFit:
                        case SurgeryUtility.SurgeryFilter.Implausible:
                            pawnLevel[f] = pawnLevel.TryGetValue(f) + 1;
                            break;
                        default:
                            if (!recipeLevel.TryGetValue(f, out var set)) recipeLevel[f] = set = new HashSet<string>();
                            set.Add(recipe.defName);
                            break;
                    }
                }
            }

            public string Report(Map map, int pawns)
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("Surgery filters (EligibleSurgeries order; " + recipes + " human recipes, checked on " + pawns + " pawns before injury):");
                if (pawns == 0) return sb.ToString();
                var late = new Dictionary<SurgeryUtility.SurgeryFilter, List<string>>();
                int eligibleAny = 0;
                foreach (var kv in passed)
                {
                    var f = SurgeryUtility.MapStageRejection(map, kv.Key);
                    if (f == SurgeryUtility.SurgeryFilter.None) { eligibleAny++; continue; }
                    if (!late.TryGetValue(f, out var l)) late[f] = l = new List<string>();
                    l.Add(kv.Key.defName);
                }
                void Recipes(string title, SurgeryUtility.SurgeryFilter f, IEnumerable<string> names)
                {
                    var list = names.OrderBy(n => n).ToList();
                    sb.AppendLine("  " + title + ": " + list.Count + (list.Count == 0 ? "" : " (" + Join(list) + ")"));
                }
                Recipes("aggressive surgeries not accepted", SurgeryUtility.SurgeryFilter.Aggressive, Get(SurgeryUtility.SurgeryFilter.Aggressive));
                Recipes("research / not available now", SurgeryUtility.SurgeryFilter.Research, Get(SurgeryUtility.SurgeryFilter.Research));
                Recipes("blacklisted (IsSurgeryRecipeAllowed)", SurgeryUtility.SurgeryFilter.Blacklist, Get(SurgeryUtility.SurgeryFilter.Blacklist));
                Recipes("isViolation", SurgeryUtility.SurgeryFilter.Violation, Get(SurgeryUtility.SurgeryFilter.Violation));
                sb.AppendLine("  does not fit the pawn (CanAssignAsIs): " + pawnLevel.TryGetValue(SurgeryUtility.SurgeryFilter.DoesNotFit) + " recipe-pawn pairs");
                Recipes("not assignable by name", SurgeryUtility.SurgeryFilter.Name, Get(SurgeryUtility.SurgeryFilter.Name));
                sb.AppendLine("  implausible request (IsPlausibleRequest): " + pawnLevel.TryGetValue(SurgeryUtility.SurgeryFilter.Implausible) + " recipe-pawn pairs");
                Recipes("no colonist with the skill", SurgeryUtility.SurgeryFilter.Skill, late.TryGetValue(SurgeryUtility.SurgeryFilter.Skill) ?? new List<string>());
                Recipes("missing ingredients", SurgeryUtility.SurgeryFilter.Ingredients, late.TryGetValue(SurgeryUtility.SurgeryFilter.Ingredients) ?? new List<string>());
                sb.AppendLine("  eligible for at least one sampled pawn: " + eligibleAny + " recipes");
                return sb.ToString();
            }

            private IEnumerable<string> Get(SurgeryUtility.SurgeryFilter f) =>
                recipeLevel.TryGetValue(f, out var set) ? set : (IEnumerable<string>)new string[0];

            private static string Join(List<string> list) =>
                string.Join(", ", list.Take(MaxListed)) + (list.Count > MaxListed ? ", +" + (list.Count - MaxListed) + " more" : "");
        }

        private static void AppendTable(StringBuilder sb, Dictionary<string, int> counts, int total)
        {
            foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                sb.AppendLine("  " + kv.Value.ToString().PadLeft(5) + "  " + Pct(kv.Value, total).PadLeft(6) + "  " + kv.Key);
        }

        private static string Pct(int part, int total) => (total == 0 ? 0f : 100f * part / total).ToString("0.0") + "%";
    }
}
