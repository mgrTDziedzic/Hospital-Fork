using RimWorld;
using Verse;

namespace Hospital.MainTab
{
    public class PawnColumnWorker_Cure : PawnColumnWorker_Text
    {
        public override string GetTextFor(Pawn pawn)
        {
            return PatientTableHelper.TextOf(pawn, cure: true);
        }
    }
}
