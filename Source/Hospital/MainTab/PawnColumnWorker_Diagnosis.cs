using RimWorld;
using Verse;

namespace Hospital.MainTab
{
    public class PawnColumnWorker_Diagnosis : PawnColumnWorker_Text
    {
        public override string GetTextFor(Pawn pawn)
        {
            return PatientTableHelper.TextOf(pawn, cure: false);
        }

        // Tooltip: the generated backstory (empty for other patients, or when the setting is off).
        public override string GetTip(Pawn pawn)
        {
            var hospital = PatientTableHelper.Hospital;
            if (hospital != null && hospital.GetPatientData(pawn, out PatientData patientData))
            {
                return Hospital.Utilities.BackstoryUtility.GetBackstory(pawn, patientData);
            }
            return string.Empty;
        }
    }
}
