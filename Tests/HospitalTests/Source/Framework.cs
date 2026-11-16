using System;
using System.Collections.Generic;
using Verse;

namespace HospitalTests
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HospitalTestAttribute : Attribute
    {
        public string Description;
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

        public Map Map => Find.CurrentMap;

        public T Track<T>(T thing) where T : Thing
        {
            cleanups.Add(() =>
            {
                if (thing == null) return;
                if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
                if (thing is Pawn p && !p.Discarded) p.Discard(true);
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
        }
    }
}
