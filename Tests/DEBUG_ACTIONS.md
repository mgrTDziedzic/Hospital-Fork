# Hospital debug actions

Debug actions provided by the developer test mod **Hospital Tests (dev)** (`Tests/HospitalTests`, RimWorld 1.6).
They are not part of the released Hospital mod.

Intended readers:
- players and testers who want to trigger hospital situations on demand or see how the mod is tested,
- agents working on the code, who need the list of available actions and what each one does.

Sources: `Tests/HospitalTests/Source/DebugActions.cs`, `PatientSampler.cs`, `UI.cs`.

## Setup

1. Install the test mod (junction into the game's `Mods` folder) and build it, see [README.md](README.md#install).
2. Enable **Hospital Tests (dev)** in the mod list, after Hospital.
3. Enable Development mode (Options -> Development mode).
4. Load a map. All actions require a game with a map loaded (`PlayingOnMap`) and work on the currently viewed map.
5. Open the debug actions menu (the dev toolbar button) and look for the categories **Hospital Debug** and **Hospital Tests**.

A map with at least one free hospital bed (medical bed with the Hospital toggle on) and a doctor is needed for the
patient actions to fire. A Patient Landing Spot is optional.

## Category "Hospital Debug"

### Common behaviour of all incident actions

For the duration of a single incident, the action temporarily sets:
- hospital open for business, all 24 opening hours on,
- danger accepted,
- for mass casualty actions also: mass casualties enabled on this map and mass casualty danger accepted.

The previous values are restored right after the incident, also when it fails.

What is **not** bypassed:
- a free hospital bed is still required,
- an eligible patient faction (allowed, neutral or allied, humanlike) and a reachable map edge or landing cell are still required,
- pawn validity checks stay (a pawn that cannot get the requested condition is not used).

If the incident does not fire, a red message names the likely reason (no free hospital bed, or no faction / edge /
suitable pawn). Details are in the log.

### Random incidents

| Action | Effect |
|---|---|
| `Patient arrives (forced)` | One patient arrives now. Type and condition are rolled by the normal rules and current settings. |
| `Mass casualty event (forced)` | A mass casualty event now. Random type, size based on the number of hospital beds. |

### Specific patients

These actions also ignore the per-map patient settings: the surgery switch, the aggressive surgery switch and the
surgery / disease blacklists. For surgeries the colonist skill and ingredient checks are skipped as well.

| Action | Submenu | Effect |
|---|---|---|
| `Patient (specific): Wounds...` | `Wounds: Random`, `Gunshot`, `Bruises`, `Burns` | One wounded patient with the chosen kind of injuries. |
| `Patient (specific): Disease...` | one entry per candidate disease, `Label (defName)`, sorted by label | One patient with the chosen disease. |
| `Patient (specific): Surgery...` | `Random surgery`, then one entry per candidate surgery, `Label (defName)` | One patient with a bill for the chosen surgery. |

Notes:
- The disease list is every hediff the mod may inflict (tendable, makes the pawn sick, has a lethal severity),
  including diseases added by other mods. It is the same list as the disease blacklist dialog.
- The surgery list is every humanlike surgery the mod may assign, aggressive ones included. With more than 40 entries
  it is grouped by the first letter of the label (`A (12)...`, `B (5)...`).
- If no generated pawn fits the chosen surgery, the message `Hospital Debug: no pawn found that surgery <defName> can be done on`
  is shown and logged.

### Specific mass casualty events

`Mass casualty (specific)...` -> event type -> size:

| Event type | Size options |
|---|---|
| `Pandemic...` | `Default count`, `1 pawn`, `3 pawns` |
| `Raid...` | same |
| `Crash...` | same |
| `Fire...` | same |

`Default count` uses the normal bed-based size. The common bypass rules above apply.

### Patient distribution sample

`Patient distribution sample...` -> `100 patients` / `500 patients`

Simulates N patient arrivals with the normal-play rules and the current settings (no forced type, nothing ignored) and
writes a report to the log. Nothing arrives on the map: no letter, no lord, no drop pod. Every generated pawn, and any
relative generated with it, is discarded afterwards. 500 patients can take a noticeable amount of time.

Requirements: an eligible patient faction and a landing cell. Without them a red message is shown and nothing is sampled.

The log report contains:
- requested / recorded count, generation failures, exceptions, run time,
- current settings (`AcceptSurgery`, `AcceptAggressive`) and whether a manhunter is on the map,
- world pawn cleanup result (discarded relatives, pawns left in the world pool; expected 0),
- rolled patient type and final type after fallbacks (for example Surgery -> Wounds when no surgery fits), with percentages,
- per type a breakdown: surgeries by recipe, diseases by hediff, wounds by worst injury,
- surgery filter breakdown (checked on up to 100 sampled pawns before injury): which filter rejects which recipe, in the
  same order as the real eligibility check: aggressive not accepted, research / not available, blacklisted,
  `isViolation`, does not fit the pawn, not assignable by name, implausible request, no colonist with the skill,
  missing ingredients; plus the number of recipes eligible for at least one sampled pawn.

Use it to check how often a given condition appears with the current mod list and settings, or why a surgery never shows up.

## Category "Hospital Tests"

| Action | Effect |
|---|---|
| `Run all` | Runs every in-game test and opens a results window. |
| `Run: <Class>.<Method>` | Runs a single test and opens a results window. One entry per test. |

Tests spawn and remove their own throwaway pawns and beds. The list of tests with their Given/When/Then descriptions is
in [TESTS.md](TESTS.md); running and writing tests is described in [README.md](README.md).

## Notes for agents

- Actions are declared in `Tests/HospitalTests/Source/DebugActions.cs` (`[DebugAction]` for fixed entries,
  `[DebugActionYielder]` returning `DebugActionNode` trees for submenus) and in `UI.cs` for the test runner.
- A specific patient is described by `Hospital.PatientSpec` (`Source/Hospital/Hospital/PatientSpec.cs`): `Type`,
  `Wounds`, `Disease`, `Surgery`, `IgnoreRestrictions`. Unset fields mean random. Normal arrivals pass no spec.
- `FireForced` wraps every incident action: it saves and restores the hospital map settings around the call.
  New incident actions should go through it.
- Debug actions are run by the user in game. Agents do not launch RimWorld; after a change, build and list the actions
  to try.
- Keep this file in sync when an action is added, renamed or removed.
