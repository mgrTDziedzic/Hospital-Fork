using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Hospital.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hospital.Patches;

// Bio tab (character card): adds a "Hospital patient" chip with the diagnosis and backstory to the top element stack.
// Transpiler on the private CharacterCardUtility.DoTopStack: the chip is added right after the quest-lodger chips
// (call to QuestUtility.AppendInspectStringsFromQuestParts), before the stack is drawn. When the vanilla method
// differs and the anchor is not found, nothing is patched (one info line) and the rest of the mod is unaffected.
[HarmonyPatch]
public static class CharacterCardPatch
{
    private static readonly FieldInfo StackField = AccessTools.Field(typeof(CharacterCardUtility), "tmpStackElements");
    private static readonly MethodInfo AnchorMethod = AccessTools.Method(typeof(QuestUtility), nameof(QuestUtility.AppendInspectStringsFromQuestParts),
        new[] { typeof(System.Action<string, Quest>), typeof(ISelectable), typeof(int).MakeByRefType() });
    private static readonly MethodInfo AddChipMethod = AccessTools.Method(typeof(CharacterCardPatch), nameof(AddPatientChip));
    private static readonly Color ChipBackground = new Color(1f, 1f, 1f, 0.1f);
    private const float Padding = 12f;

    static MethodBase TargetMethod() => AccessTools.Method(typeof(CharacterCardUtility), "DoTopStack");

    static bool Prepare() => TargetMethod() != null;

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        int index = -1;
        if (StackField != null && AnchorMethod != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(AnchorMethod))
                {
                    index = i;
                    break;
                }
            }
        }
        if (index < 0)
        {
            Log.Message("Hospital: character card layout differs from the expected one, patient chip in the Bio tab is off");
            return list;
        }
        list.InsertRange(index + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldsfld, StackField),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, AddChipMethod)
        });
        return list;
    }

    /// <summary>Adds the chip for active patients; does nothing for everyone else.</summary>
    public static void AddPatientChip(List<GenUI.AnonymousStackElement> elements, Pawn pawn)
    {
        if (elements == null || pawn == null) return;
        if (!pawn.IsPatient(out var hospital, activeOnly: true)) return;
        string label = "HospitalPatientChip".Translate();
        float width = Text.CalcSize(label).x + Padding;
        elements.Add(new GenUI.AnonymousStackElement
        {
            width = width,
            drawer = r =>
            {
                Color color = GUI.color;
                GUI.color = ChipBackground;
                GUI.DrawTexture(r, BaseContent.WhiteTex);
                GUI.color = color;
                Widgets.DrawHighlightIfMouseover(r);
                var textRect = new Rect(r.x + Padding / 2f, r.y, r.width - Padding, r.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(textRect, label);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Mouse.IsOver(r)) TooltipHandler.TipRegion(r, TipFor(pawn, hospital, label));
            }
        });
    }

    private static string TipFor(Pawn pawn, HospitalMapComponent hospital, string title)
    {
        string tip = title;
        if (!hospital.GetPatientData(pawn, out var data)) return tip;
        if (!data.Diagnosis.NullOrEmpty()) tip += "\n\n" + data.Diagnosis;
        string backstory = BackstoryUtility.GetBackstory(pawn, data);
        if (!backstory.NullOrEmpty()) tip += "\n\n" + backstory;
        return tip;
    }
}
