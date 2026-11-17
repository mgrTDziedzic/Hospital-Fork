using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace HospitalTests
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HospitalTestAttribute : Attribute
    {
        public string Description;
        /// <summary>SupportedMods key of an optional mod the test needs; the test is skipped (not run, nothing logged) when it is not active.</summary>
        public string RequiresMod;
        /// <summary>Package ID of a DLC the test needs (e.g. "Ludeon.RimWorld.Biotech" for children); skipped silently without it.</summary>
        public string RequiresDlc;
        public HospitalTestAttribute(string description = null) { Description = description; }
    }

    public class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = "expected true") { if (!condition) throw new AssertException(message); }
        public static void False(bool condition, string message = "expected false") { if (condition) throw new AssertException(message); }
        public static void NotNull(object o, string message = "expected non-null") { if (o == null) throw new AssertException(message); }
        public static void Fail(string message) { throw new AssertException(message); }
        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException((message != null ? message + ": " : "") + "expected <" + expected + "> but was <" + actual + ">");
        }
    }

    /// <summary>Handed to a test; everything tracked here is undone after the test, pass or fail.</summary>
    public class TestContext
    {
        private readonly List<Action> cleanups = new List<Action>();
        private readonly Map guardMap;
        private readonly HashSet<Lord> lordsBefore = new HashSet<Lord>();
        private readonly HashSet<Letter> lettersBefore = new HashSet<Letter>();
        private readonly HashSet<Thing> corpsesBefore = new HashSet<Thing>();

        // Snapshot of lords and letters; RunCleanup removes what the test (or another mod reacting to it) left behind.
        public TestContext()
        {
            guardMap = Find.CurrentMap;
            if (guardMap?.lordManager != null) foreach (var l in guardMap.lordManager.lords) lordsBefore.Add(l);
            if (Find.LetterStack != null) foreach (var l in Find.LetterStack.LettersListForReading) lettersBefore.Add(l);
            if (guardMap?.listerThings != null) foreach (var c in guardMap.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)) corpsesBefore.Add(c);
        }

        /// <summary>Discards a pawn the right way: world pawns must go through WorldPawns (Pawn.Discard warns on them).</summary>
        public static void DiscardPawn(Pawn p)
        {
            if (p == null || p.Discarded) return;
            var wp = Find.WorldPawns;
            if (wp != null && wp.Contains(p)) wp.RemoveAndDiscardPawnViaGC(p);
            else p.Discard(true);
        }

        public Map Map => Find.CurrentMap;

        public T Track<T>(T thing) where T : Thing
        {
            cleanups.Add(() =>
            {
                if (thing == null) return;
                // A pawn killed during the test is Destroyed already, but its corpse stays on the map: remove it too.
                var corpse = (thing as Pawn)?.Corpse;
                if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
                if (corpse != null && !corpse.Destroyed) corpse.Destroy(DestroyMode.Vanish);
                if (thing is Pawn p) DiscardPawn(p);
            });
            return thing;
        }

        public void OnCleanup(Action a) { cleanups.Add(a); }

        public void RunCleanup()
        {
            for (int i = cleanups.Count - 1; i >= 0; i--)
            {
                try { cleanups[i](); }
                catch (Exception e) { Log.Warning("[HospitalTests] cleanup failed: " + e); }
            }
            cleanups.Clear();
            GuardLeaks();
        }

        // After cleanup: warn about lords the test left behind (not an error: another mod may have created them), then remove them
        // and any new letters so they do not pile up across tests.
        private void GuardLeaks()
        {
            try
            {
                if (guardMap?.lordManager != null)
                {
                    var leaked = guardMap.lordManager.lords.Where(l => !lordsBefore.Contains(l)).ToList();
                    if (leaked.Count > 0)
                    {
                        Log.Warning("[HospitalTests] test leaked " + leaked.Count + " lord(s), removing: "
                            + string.Join(", ", leaked.Select(l => (l.LordJob?.GetType().Name ?? "?") + " (" + l.ownedPawns.Count + " pawns)")));
                        foreach (var l in leaked) guardMap.lordManager.RemoveLord(l);
                    }
                }
                if (guardMap?.listerThings != null)
                {
                    var corpses = guardMap.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Where(c => !corpsesBefore.Contains(c)).ToList();
                    if (corpses.Count > 0)
                    {
                        Log.Warning("[HospitalTests] test leaked " + corpses.Count + " corpse(s), removing");
                        foreach (var c in corpses)
                        {
                            var inner = (c as Corpse)?.InnerPawn;
                            if (!c.Destroyed) c.Destroy(DestroyMode.Vanish);
                            DiscardPawn(inner);
                        }
                    }
                }
                if (Find.LetterStack != null)
                    foreach (var letter in Find.LetterStack.LettersListForReading.Where(l => !lettersBefore.Contains(l)).ToList())
                        Find.LetterStack.RemoveLetter(letter);
            }
            catch (Exception e) { Log.Warning("[HospitalTests] leak guard failed: " + e); }
        }
    }
}
