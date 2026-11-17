using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace Hospital.Compat;

// Multi-Doctor Surgery (MasterPNJ.MultiDoctor) soft compatibility, by reflection on type names (no assembly reference).
// When MD's "Fast Operation" is ON and a default surgery team is set, patient surgery bills get that team.
// MD itself only applies the default team to bills made through the health tab; patient bills skip that path.
// MD not installed: nothing happens and nothing is logged. MD installed: one info line at startup; if its API differs
// or an exception occurs: one info message instead, and the compat is off for the rest of the session.
public static class MultiDoctorCompat
{
        private static bool initialized;
        private static bool active;
        private static Type componentType;
        private static FieldInfo leadField, assistantsField, fastField;
        private static MethodInfo createBill, canOperate, assignTeam;
        private static Type billExType;

        private static bool Active
        {
                get
                {
                        if (!initialized) Init();
                        return active;
                }
        }

        // Called once at startup (HospitalMod): looks MD up and logs one info line when it is installed.
        public static void Startup()
        {
                if (!initialized) Init();
        }

        private static void Init()
        {
                initialized = true;
                if (!SupportedMods.IsActive(SupportedMods.MultiDoctor)) return;
                try
                {
                        componentType = FindType("MultiDoctorSurgery.DefaultSurgeryTeamComponent");
                        billExType = FindType("MultiDoctorSurgery.BillMedicalEx");
                        var patchType = FindType("MultiDoctorSurgery.Patches.Patch_HealthCardUtility_CreateSurgeryBill");
                        var utilType = FindType("MultiDoctorSurgery.SurgeryTeamUtility");
                        leadField = componentType?.GetField("defaultLeadSurgeon", BindingFlags.Public | BindingFlags.Instance);
                        assistantsField = componentType?.GetField("defaultAssistants", BindingFlags.Public | BindingFlags.Instance);
                        fastField = componentType?.GetField("fastOperationEnabled", BindingFlags.Public | BindingFlags.Instance);
                        createBill = patchType?.GetMethod("CreateBill", BindingFlags.Public | BindingFlags.Static, null,
                                new[] { typeof(RecipeDef), typeof(List<Thing>), typeof(Pawn) }, null);
                        canOperate = utilType?.GetMethod("CanOperate", BindingFlags.Public | BindingFlags.Static, null,
                                new[] { typeof(Pawn), typeof(Pawn), typeof(RecipeDef) }, null);
                        assignTeam = billExType == null ? null : utilType?.GetMethod("AssignTeam", BindingFlags.Public | BindingFlags.Static, null,
                                new[] { billExType, typeof(Pawn), typeof(IEnumerable<Pawn>) }, null);
                        active = leadField != null && assistantsField != null && fastField != null
                                 && createBill != null && canOperate != null && assignTeam != null;
                }
                catch (Exception)
                {
                        active = false;
                }
                if (active) Log.Message("[Hospital] " + SupportedMods.DisplayName(SupportedMods.MultiDoctor) + " (" + SupportedMods.ActiveId(SupportedMods.MultiDoctor) + ") detected, compatibility enabled");
                else LogDisabled();
        }

        private static Type FindType(string fullName)
        {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                        var t = asm.GetType(fullName, false);
                        if (t != null) return t;
                }
                return null;
        }

        private static void LogDisabled()
        {
                active = false;
                Log.Message("[Hospital] Multi-Doctor Surgery detected, but its default surgery team can't be used with this version; patients get standard single-surgeon surgery.");
        }

        // Creates the patient's bill with MD's default team and returns true, or returns false and changes nothing
        // (caller then builds the plain bill). Needs a spawned patient. No random numbers, no UI.
        public static bool TryCreateTeamBill(Pawn patient, RecipeDef recipe, BodyPartRecord part, out Bill_Medical bill)
        {
                bill = null;
                if (!Active) return false;
                try
                {
                        var component = Find.World?.GetComponent(componentType);
                        if (component == null || !(bool)fastField.GetValue(component)) return false;
                        var lead = leadField.GetValue(component) as Pawn;
                        if (lead == null) return false;
                        if (!(bool)canOperate.Invoke(null, new object[] { lead, patient, recipe })) return false;

                        var created = createBill.Invoke(null, new object[] { recipe, new List<Thing>(), patient }) as Bill_Medical;
                        if (created == null || created.GetType() != billExType) return false; // MD excludes this recipe

                        patient.health.surgeryBills.AddBill(created);
                        created.Part = part; // after AddBill, same order as the plain path
                        var assistants = ((IEnumerable)assistantsField.GetValue(component)).OfType<Pawn>().ToList();
                        // A failed AssignTeam is harmless: the bill then behaves like vanilla.
                        assignTeam.Invoke(null, new object[] { created, lead, assistants });
                        bill = created;
                        return true;
                }
                catch (Exception)
                {
                        LogDisabled();
                        return false;
                }
        }
}
