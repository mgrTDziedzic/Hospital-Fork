using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Hospital;

public class Settings : ModSettings
{
    public bool ShowMessageAtArrival;
    public bool PreventIncapacitatedArrivals = true;
    public float SilverMultiplier = 1f;
    // Shows the cosmetic 3x3 / 5x5 Patient Landing Spot variants in the build menu.
    public bool EnableLandingSpotVariants = true;

    // Difficulty tuning - all defaults reproduce the original hardcoded behaviour.
    public float GoodwillGainMultiplier = 1f;
    public int DeathGoodwillPenalty = 10;
    // false: the death penalty applies only to deaths the player is responsible for (see HospitalMapComponent.PlayerResponsibleForDeath).
    public bool PenalizeNonPlayerDeaths;
    public int SurgeryFailGoodwillPenalty = 1;
    // Hours an aggressive patient stays sedated after arrival (aggressive patients challenge).
    public float SedationHours = 8f;
    public float DiseaseSeverityMultiplier = 1f;
    public float WoundSeverityMultiplier = 1f;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref SilverMultiplier, "silverMultiplier", 1f);
        Scribe_Values.Look(ref EnableLandingSpotVariants, "enableLandingSpotVariants", true);
        Scribe_Values.Look(ref ShowMessageAtArrival, "showMessageAtArrival", false);
        Scribe_Values.Look(ref PreventIncapacitatedArrivals, "preventIncapacitatedArrivals", true);
        Scribe_Values.Look(ref GoodwillGainMultiplier, "goodwillGainMultiplier", 1f);
        Scribe_Values.Look(ref DeathGoodwillPenalty, "deathGoodwillPenalty", 10);
        Scribe_Values.Look(ref PenalizeNonPlayerDeaths, "penalizeNonPlayerDeaths", false);
        Scribe_Values.Look(ref SurgeryFailGoodwillPenalty, "surgeryFailGoodwillPenalty", 1);
        Scribe_Values.Look(ref SedationHours, "sedationHours", 8f);
        Scribe_Values.Look(ref DiseaseSeverityMultiplier, "diseaseSeverityMultiplier", 1f);
        Scribe_Values.Look(ref WoundSeverityMultiplier, "woundSeverityMultiplier", 1f);
        base.ExposeData();
    }

    public void DoWindowContents(Rect inRect)
    {
        Listing_Standard listingStandard = new Listing_Standard();
        listingStandard.Begin(inRect);

        listingStandard.CheckboxLabeled("ShowMessageAtArrival".Translate(), ref ShowMessageAtArrival, "ShowMessageAtArrivalTooltip".Translate());
        listingStandard.CheckboxLabeled("PreventIncapacitatedArrivals".Translate(), ref PreventIncapacitatedArrivals, "PreventIncapacitatedArrivalsTooltip".Translate());
        listingStandard.CheckboxLabeled("EnableLandingSpotVariants".Translate(), ref EnableLandingSpotVariants, "EnableLandingSpotVariantsTooltip".Translate());

        listingStandard.GapLine();
        Text.Font = GameFont.Medium;
        listingStandard.Label("DifficultySettings".Translate());
        Text.Font = GameFont.Small;

        listingStandard.SliderLabeled("SilverMultiplier".Translate(), ref SilverMultiplier, SilverMultiplier.ToString("0%"), 0f, 2f,
            "SilverMultiplierTooltip".Translate());
        listingStandard.SliderLabeled("GoodwillGainMultiplier".Translate(), ref GoodwillGainMultiplier, GoodwillGainMultiplier.ToString("0%"), 0f, 3f,
            "GoodwillGainMultiplierTooltip".Translate());

        float deathPenalty = DeathGoodwillPenalty;
        listingStandard.SliderLabeled("DeathGoodwillPenalty".Translate(), ref deathPenalty, deathPenalty.ToString("0"), 0f, 50f,
            "DeathGoodwillPenaltyTooltip".Translate());
        DeathGoodwillPenalty = Mathf.RoundToInt(deathPenalty);
        listingStandard.CheckboxLabeled("PenalizeNonPlayerDeaths".Translate(), ref PenalizeNonPlayerDeaths, "PenalizeNonPlayerDeathsTooltip".Translate());

        float surgeryFailPenalty = SurgeryFailGoodwillPenalty;
        listingStandard.SliderLabeled("SurgeryFailGoodwillPenalty".Translate(), ref surgeryFailPenalty, surgeryFailPenalty.ToString("0"), 0f, 50f,
            "SurgeryFailGoodwillPenaltyTooltip".Translate());
        SurgeryFailGoodwillPenalty = Mathf.RoundToInt(surgeryFailPenalty);

        listingStandard.SliderLabeled("SedationHours".Translate(), ref SedationHours, SedationHours.ToString("0.#") + "h", 2f, 24f,
            "SedationHoursTooltip".Translate());
        SedationHours = Mathf.Clamp(Mathf.Round(SedationHours * 2f) / 2f, 2f, 24f);

        listingStandard.SliderLabeled("DiseaseSeverityMultiplier".Translate(), ref DiseaseSeverityMultiplier, DiseaseSeverityMultiplier.ToString("0%"), 0.25f, 2f,
            "DiseaseSeverityMultiplierTooltip".Translate());
        listingStandard.SliderLabeled("WoundSeverityMultiplier".Translate(), ref WoundSeverityMultiplier, WoundSeverityMultiplier.ToString("0%"), 0.25f, 2f,
            "WoundSeverityMultiplierTooltip".Translate());

        if (listingStandard.ButtonText("ResetToDefaults".Translate(), widthPct: 0.35f))
        {
            ResetToDefaults();
        }

        listingStandard.End();
    }

    private void ResetToDefaults()
    {
        PreventIncapacitatedArrivals = true;
        EnableLandingSpotVariants = true;
        SilverMultiplier = 1f;
        GoodwillGainMultiplier = 1f;
        DeathGoodwillPenalty = 10;
        PenalizeNonPlayerDeaths = false;
        SurgeryFailGoodwillPenalty = 1;
        SedationHours = 8f;
        DiseaseSeverityMultiplier = 1f;
        WoundSeverityMultiplier = 1f;
    }
}
