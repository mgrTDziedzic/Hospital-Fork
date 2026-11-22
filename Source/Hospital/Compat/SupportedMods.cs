using System.Collections.Generic;
using Verse;

namespace Hospital.Compat;

// Central list of supported optional mods. Each mod has a key, a display name and one or more package IDs
// (an original and its "Continued" forks may all be listed). More IDs (originals, forks) are added only after
// manual testing confirms the same API and passing tests.
// Detection ignores the "_steam" suffix (ModsConfig.IsActive does not).
public static class SupportedMods
{
        public const string MultiDoctor = "MultiDoctor";
        public const string PlasteelSurgery = "PlasteelSurgery";
        public const string MedPod = "MedPod";
        public const string EnemySelfPreservation = "EnemySelfPreservation";
        public const string IVDrugInfuser = "IVDrugInfuser";

        private class Entry
        {
                public string Name;
                public string[] Ids;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
                { MultiDoctor, new Entry { Name = "Multi-Doctor Surgery", Ids = new[] { "MasterPNJ.MultiDoctor" } } },
                { PlasteelSurgery, new Entry { Name = "Plasteel Surgery", Ids = new[] { "Mlie.PlasteelSurgery" } } },
                { MedPod, new Entry { Name = "MedPod", Ids = new[] { "sumghai.Medpod" } } },
                { EnemySelfPreservation, new Entry { Name = "Enemy Self Preservation", Ids = new[] { "Mlie.EnemySelfPreservation" } } },
                { IVDrugInfuser, new Entry { Name = "IV Drug Infuser", Ids = new[] { "akaster.ivdruginfuser" } } },
        };

        private static readonly Dictionary<string, string> Detected = new Dictionary<string, string>();

        // True when any package ID of the mod is active. Unknown key: false, silently. Cached per key.
        public static bool IsActive(string key)
        {
                return ActiveId(key) != null;
        }

        // The detected package ID, or null.
        public static string ActiveId(string key)
        {
                if (key == null) return null;
                if (Detected.TryGetValue(key, out var cached)) return cached;
                string found = null;
                if (Entries.TryGetValue(key, out var entry))
                {
                        foreach (var id in entry.Ids)
                        {
                                if (ModLister.GetActiveModWithIdentifier(id, ignorePostfix: true) != null) { found = id; break; }
                        }
                }
                Detected[key] = found;
                return found;
        }

        public static string DisplayName(string key)
        {
                return key != null && Entries.TryGetValue(key, out var e) ? e.Name : key;
        }

        public static string IdsText(string key)
        {
                return key != null && Entries.TryGetValue(key, out var e) ? string.Join(", ", e.Ids) : "unknown";
        }
}
