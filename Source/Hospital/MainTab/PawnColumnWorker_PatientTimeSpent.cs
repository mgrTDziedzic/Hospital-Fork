using System;
using RimWorld;
using Verse;

namespace Hospital.MainTab
{
    public class PawnColumnWorker_PatientTimeSpent : PawnColumnWorker_Text
    {
        protected internal int ticks;

        public override string GetTextFor(Pawn pawn)
        {
            int t = PatientTableHelper.TicksOf(pawn);
            if (t < 0) return string.Empty;
            ticks = t;
            return Math.Truncate(ticks / 2500.0) + " h";
        }

        // Sort by the ticks, not by the formatted text.
        public override int Compare(Pawn a, Pawn b)
        {
            return PatientTableHelper.TicksOf(a).CompareTo(PatientTableHelper.TicksOf(b));
        }
    }
}
