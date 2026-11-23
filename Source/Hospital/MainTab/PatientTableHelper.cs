using Hospital.Utilities;
using UnityEngine;
using Verse;

namespace Hospital.MainTab
{
    // Shared by the Hospital tab column workers: the current map's HospitalMapComponent, refreshed when it is null,
    // the map changed or two seconds passed. Safe to call from Compare before anything was drawn.
    internal static class PatientTableHelper
    {
        private static HospitalMapComponent cached;
        private static float lastTimeCached;

        public static HospitalMapComponent Hospital
        {
            get
            {
                Map map = Find.CurrentMap;
                if (cached == null || cached.map != map || Time.unscaledTime > lastTimeCached + 2)
                {
                    lastTimeCached = Time.unscaledTime;
                    cached = map?.GetComponent<HospitalMapComponent>();
                }
                return cached;
            }
        }

        // Sort keys: non-patients sort as -1 (before every patient value).
        public static float RatingOf(Pawn pawn)
        {
            var hospital = Hospital;
            return hospital != null && pawn.GetPatientRating(out float score, hospital) ? score : -1f;
        }

        public static int TicksOf(Pawn pawn)
        {
            var hospital = Hospital;
            return hospital != null && pawn.GetPatientTimeInHospital(out int ticks, hospital) ? ticks : -1;
        }

        public static float BillOf(Pawn pawn)
        {
            var hospital = Hospital;
            return hospital != null && hospital.GetPatientData(pawn, out var data) ? PatientUtility.CalculateSilverToReceive(pawn, data) : -1f;
        }

        public static string TextOf(Pawn pawn, bool cure)
        {
            var hospital = Hospital;
            if (hospital != null && hospital.GetPatientData(pawn, out var data)) return (cure ? data.Cure : data.Diagnosis) ?? string.Empty;
            return string.Empty;
        }
    }
}
