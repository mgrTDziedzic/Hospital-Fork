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

    // Upper bound when a forced surgery needs a pawn that fits it (debug).
    public const int MaxForcedSurgeryAttempts = 60;

    // forSurgery != null (debug, forced surgery): optional per-mod hints shape the request (PawnGenHints).
    public static Pawn GeneratePawn(Faction faction, RecipeDef forSurgery = null)
    {
        PawnKindDef kind = faction.RandomPawnKind();
        if (kind == null) return null; // faction without any pawn kind
        var request = new PawnGenerationRequest(kind, faction,
            PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
            canGeneratePawnRelations: true, false, 1f,
            forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: true, allowFood: true,
            allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false,
            forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f,
            null, null, null, null, null, null, null, null, null, null, null, null);
        if (forSurgery != null) Compat.PawnGenHints.Apply(forSurgery, ref request);
        return PawnGenerator.GeneratePawn(request);
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
        generate = generate ?? (f => GeneratePawn(f));
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

    // silent: vanilla's silentlyRemoveReferences (as used by the world pawn GC): battle log entries, play log
    // entries and tales that mention the pawn are removed without a warning. Used by the debug sampler.
    // The silent path uses vanilla's GC route (RemoveAndDiscardPawnViaGC): it destroys and discards the pawn inside
    // WorldPawns' "being discarded" guard. Calling Destroy() directly would pass the pawn back to the world pool as dead
    // (Pawn.Destroy does that when no guard is set), and Discard would then refuse to discard a world pawn.
    public static void DiscardGeneratedPawn(Pawn pawn, bool silent = false)
    {
        if (pawn == null || pawn.Discarded) return;
        if (silent)
        {
            if (pawn.Spawned) pawn.DeSpawn(); // PassToWorld errors on spawned pawns; it also removes leftover spawned components
            if (!Find.WorldPawns.Contains(pawn)) Find.WorldPawns.PassToWorld(pawn); // Decide mode: just adds the pawn
            Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
            return;
        }
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
        // Any marked landing spot def (default 1x1 and variants); invalid variant defs are ignored. Position is the centre cell for odd, non-rotatable sizes.
        var spot = map.listerBuildings.allBuildingsColonist.Where(b => b.Spawned && b.Map == map && Utilities.LandingSpotUtility.IsLandingSpotDef(b.def)
            && (!Utilities.LandingSpotUtility.IsVariantDef(b.def) || Utilities.LandingSpotUtility.IsValidLandingDef(b.def))).RandomElementWithFallback();
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

    // Factions a patient can come from right now (normal play rules). manhunterOnMap: an active pawn on the map is in an
    // aggressive mental state; the incident then skips the arrival unless AcceptDanger is on (caller decides).
    public static List<Faction> GetPotentialPatientFactions(Map map, HospitalMapComponent hospital, out bool manhunterOnMap)
    {
        List<Faction> potentialPatientFactions = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden && f.AllyOrNeutralTo(Faction.OfPlayer) && f.def.humanlikeFaction && !Compat.ModDefFilters.IsAndroidDef(f.def) && hospital.IsFactionAllowed(f.def)).ToList();

        var activePawns = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.IsPrisoner && !p.Downed && !PatientUtility.IsFogged(p) && !p.InContainerEnclosed).ToArray();
        manhunterOnMap = activePawns.Any(p => p.InAggroMentalState);

        // remove all factions that could get hostile with a pawn on the map - or if the player is currently has hostile pawns visiting
        potentialPatientFactions.RemoveAll(faction =>
            activePawns.Where(p => p.Faction != null).Select(p => p.Faction).Any(f => f.HostileTo(Faction.OfPlayer) || f.HostileTo(faction)));
        return potentialPatientFactions;
    }

    // Normal-play roll of the patient type: surgery only when the hospital accepts it.
    public static PatientType RollPatientType(HospitalMapComponent hospital)
    {
        return (PatientType)Rand.Range(1, hospital.AcceptSurgery ? 4 : 3);
    }

    // Shared by the arrival incident and the debug sampler: set up the pawn, pick the type (spec?.Type overrides the
    // roll), spawn it so it has a Map and apply the arrival wounds/disease/surgery. Leaves the pawn spawned at loc.
    public static PatientData PrepareAndInjure(Map map, HospitalMapComponent hospital, Pawn pawn, IntVec3 loc, PatientSpec spec, out PatientType rolledType)
    {
        SetUpNewPatient(pawn);
        // A forced type ignores the AcceptSurgery setting. Without a spec the roll is unchanged.
        PatientType type = spec?.Type ?? RollPatientType(hospital);
        rolledType = type;
        PatientData data = new PatientData(GenDate.TicksGame, pawn.MarketValue, pawn.needs.mood.curLevelInt, type);
        // Spawn the pawn on the map first so it has a valid Map while we apply damage/disease.
        // Damaging an unspawned pawn (pawn.Map == null) collides with hediff comps and other
        // mods' patches that dereference pawn.Map (sound makers, FleckMaker, CompPostPostAdd, ...).
        GenSpawn.Spawn(pawn, loc, map);
        PatientUtility.DamagePawn(pawn, data, hospital, spec);
        return data;
    }

    public static void SetUpNewPatient(Pawn pawn)
    {
        pawn.guest.SetGuestStatus(Faction.OfPlayer); // mark as guest otherwise the pawn just wanders off again
        pawn.playerSettings.selfTend = false; // otherwise they will start self tending :p

        pawn.equipment.DestroyAllEquipment(); // avoid exploits
        pawn.inventory.DestroyAll();
    }
}