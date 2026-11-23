using Hospital.Utilities;
using RimWorld;
using Verse;

namespace Hospital.MainTab
{
    public class PawnColumnWorker_Bill : PawnColumnWorker_Text
    {
        public override string GetTextFor(Pawn pawn)
        {
            var hospital = PatientTableHelper.Hospital;
            if (hospital != null && hospital.GetPatientData(pawn, out PatientData patientData))
            {
                return PatientUtility.CalculateSilverToReceive(pawn, patientData).ToStringMoney();
            }
            return string.Empty;
        }

        // Sort by the amount, not by the formatted text.
        public override int Compare(Pawn a, Pawn b)
        {
            return PatientTableHelper.BillOf(a).CompareTo(PatientTableHelper.BillOf(b));
        }
    }
}
