using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.MainTab {
    public class PawnColumnWorker_ButtonDisallow : PawnColumnWorker
    {
        private const int ManageButtonHeight = 32;
        private float lastTimeCached;
        private HospitalMapComponent hospital;
        
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (Time.unscaledTime > lastTimeCached + 2 || Find.CurrentMap != hospital.map)
            {
                lastTimeCached = Time.unscaledTime;
                hospital = Find.CurrentMap.GetComponent<HospitalMapComponent>();
            }
            
            PatientData patientData;
            if (hospital.GetPatientData(pawn, out patientData))
            {
                if (patientData.CureRecipe != null)
                {
                    if (Widgets.ButtonText(rect, "Disallow".Translate()))
                    {
                        CameraJumper.TryJumpAndSelect(pawn);
                        hospital.RefuseOperation(pawn, patientData.CureRecipe);
                    }
                }
            }
        }

        public override void DoHeader(Rect rect, PawnTable table)
        {
            // Same size as the vanilla Assign tab policy buttons: column width (max 360) x 32.
            Rect buttonRect = new Rect(rect.x, rect.y + (rect.height - ManageButtonHeight), Mathf.Min(rect.width, 360f), ManageButtonHeight);
            if (Widgets.ButtonText(buttonRect, "HospitalMapSettings".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MapSettings(Find.CurrentMap));
            }
        }
        
        public override int GetMinWidth(PawnTable table)
        {
            return Mathf.Max(base.GetMinWidth(table), 120);
        }

        public override int GetMinHeaderHeight(PawnTable table)
        {
            return Mathf.Max(base.GetMinHeaderHeight(table), ManageButtonHeight);
        }

        public override int GetMaxWidth(PawnTable table)
        {
            return Mathf.Min(base.GetMaxWidth(table), this.GetMinWidth(table));
        }
    }
}