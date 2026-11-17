using System.Collections.Generic;
using System.Linq;
using Hospital.Utilities;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Hospital;

public static class IncidentHelper
{
    // Upper bound for attempts to generate a pawn that can be a patient (see TryGenerateAcceptedPawn).
    public const int MaxPawnGenerationAttempts = 20;

    public static Pawn GeneratePawn(Faction faction)
    {
        PawnKindDef kind = faction.RandomPawnKind();
        if (kind == null) return null; // faction without any pawn kind
        Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction,
            PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
            canGeneratePawnRelations: true, false, 1f,
            forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: true, allowFood: true,
            allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false,
            forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f,
            null, null, null, null, null, null, null, null, null, null, null, null));
        return p;
    }
    
    // Pawns that cannot be patients: androids (VRE Android) and anything that cannot bleed.
    public static bool IsSuitablePatient(Pawn pawn)
    {
        return pawn.kindDef != null && !Compat.ModDefFilters.IsAndroidDef(pawn.kindDef) && pawn.health.CanBleed;
    }

    // Generates a pawn from a random one of the factions that passes `accept`. Gives up after maxAttempts
    // (returns false, pawn null) instead of looping forever. Rejected pawns are discarded so they do not
    // pile up in the world pawn pool.
    public static bool TryGenerateAcceptedPawn(IList<Faction> factions, System.Func<Pawn, bool> accept, int maxAttempts,
        out Pawn pawn, out Faction faction, System.Func<Faction, Pawn> generate = null)
    {
        generate = generate ?? GeneratePawn;
        pawn = null;
        faction = null;
        if (factions == null || factions.Count == 0) return false;
        for (int i = 0; i < maxAttempts; i++)
        {
            Faction f = factions.RandomElement();
            Pawn candidate = generate(f);
            if (candidate == null) continue;
            if (accept(candidate))
            {
                pawn = candidate;
                faction = f;
                return true;
            }
            DiscardGeneratedPawn(candidate);
        }
        return false;
    }

    public static void DiscardGeneratedPawn(Pawn pawn)
    {
        if (pawn == null || pawn.Discarded) return;
        if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
    }

    public static IEnumerable<Pawn> GetKnownPawns(IncidentParms parms)
    {
        return Find.WorldPawns.AllPawnsAlive.Where(pawn => ValidPatient(pawn, parms.faction));
    }

    private static bool ValidPatient(Pawn pawn, Faction faction)
    {
        var validGuest = !pawn.Discarded && !pawn.Dead && !pawn.Spawned && !pawn.NonHumanlikeOrWildMan() && !pawn.Downed && pawn.Faction == faction;
        if (!validGuest) return false;
        // Leader only comes when relations are good
        if (faction.leader == pawn && faction.PlayerGoodwill < 80) return false;
        if (pawn.kindDef == PawnKindDefOf.Empire_Royal_Bestower) return false;
        if (QuestUtility.IsReservedByQuestOrQuestBeingGenerated(pawn)) return false;
        // No patient-roster check needed: candidates are unspawned world pawns, and registered patients are always spawned on a map.
        return true;
    }        

    // Patient Landing Spot first; the (costly, fallible) drop cell finder only when there is none.
    public static bool TryFindPatientLandingCell(Map map, out IntVec3 loc)
    {
        var spot = map.listerBuildings.AllBuildingsColonistOfDef(ThingDef.Named("PatientLandingSpot")).Where(b => b.Spawned && b.Map == map).RandomElementWithFallback();
        loc = spot != null ? spot.Position : DropCellFinder.TryFindSafeLandingSpotCloseToColony(map, IntVec2.Two);
        return loc.IsValid && loc.InBounds(map);
    }

    // True when a mass casualty event should be skipped because of dangers on the map: the MCE danger setting
    // is off, the normal danger override is off and an active pawn is in an aggressive mental state (manhunter).
    public static bool MapTooDangerousForMce(Map map, HospitalMapComponent hospital)
    {
        if (hospital.MceAcceptDanger || hospital.AcceptDanger) return false;
        return map.mapPawns.AllPawnsSpawned.Any(p => !p.Dead && !p.IsPrisoner && !p.Downed && !PatientUtility.IsFogged(p) && !p.InContainerEnclosed && p.InAggroMentalState);
    }

    public static bool CanSpawnPatient(Map map)
    {
        var hospital = map.GetComponent<HospitalMapComponent>();
        if (!hospital.IsOpen()) return false;
        if (hospital.IsFull()) return false;
        if (map.GameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout) && !hospital.AcceptDanger) return false;
        if (map.GameConditionManager.ConditionIsActive(GameConditionDefOf.VolcanicWinter) && !hospital.AcceptDanger) return false;
        //Log.Message((int)HospitalMod.Settings.PatientLimit + " - " + map.GetComponent<HospitalMapComponent>().Patients.Count);
        IntVec3 cell;
        return TryFindEntryCell(map, out cell);
    }
            
    private static bool TryFindEntryCell(Map map, out IntVec3 cell)
    {
        return CellFinder.TryFindRandomEdgeCellWith(
            (IntVec3 c) => map.reachability.CanReachColony(c) && !c.Fogged(map), map,
            CellFinder.EdgeRoadChance_Neutral, out cell);
    }

    public static void SetUpNewPatient(Pawn pawn)
    {
        pawn.guest.SetGuestStatus(Faction.OfPlayer); // mark as guest otherwise the pawn just wanders off again
        pawn.playerSettings.selfTend = false; // otherwise they will start self tending :p

        pawn.equipment.DestroyAllEquipment(); // avoid exploits
        pawn.inventory.DestroyAll();
    }
}