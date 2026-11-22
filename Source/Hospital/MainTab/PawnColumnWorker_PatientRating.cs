using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.MainTab
{
    public class PawnColumnWorker_PatientRating : PawnColumnWorker_Text
    {
        protected internal float score;

        public override string GetTextFor(Pawn pawn)
        {
            float rating = PatientTableHelper.RatingOf(pawn);
            if (rating < 0f && !IsPatient(pawn)) return string.Empty;
            score = rating;
            return Mathf.Clamp01(score).ToStringPercent();
        }

        private static bool IsPatient(Pawn pawn)
        {
            var hospital = PatientTableHelper.Hospital;
            return hospital != null && hospital.GetPatientData(pawn, out _);
        }

        // Sort by the score, not by the formatted text.
        public override int Compare(Pawn a, Pawn b)
        {
            return PatientTableHelper.RatingOf(a).CompareTo(PatientTableHelper.RatingOf(b));
        }
    }
}
