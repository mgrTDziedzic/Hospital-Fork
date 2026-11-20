# Changes in this fork

An overview of the changes this fork makes to [Hospital](https://github.com/tomvd/Hospital) by tomvd,
which remains the basis for everything below. For the detailed list see [CHANGELOG.md](CHANGELOG.md).

Supported game versions: RimWorld 1.6 and 1.7.

---

## Additions

### Patient drugs
Lets you choose which drugs patients receive while in your care.

- **Hospital Policies → Patient drugs** has a drug policy for each kind of patient:
  - **All patients**
  - **Infection** – sick patients who are not yet immune
  - **Surgery** – patients waiting for an operation
  - **Injuries** – patients with fresh wounds or blood loss
  - **Scars** – patients with permanent injuries
- A patient gets the drugs of every list that applies to them.
- Only drugs marked **Scheduled** in the policy are given. Doctors administer them as
  medical bills, following the policy's frequency and mood/joy limits.
- **Teetotalers too** – per list, whether teetotaler patients also get those drugs.
- Drugs are billed to the patient like other treatment.
- **Manage drug policies...** creates policies for the hospital. Examples:
  - **All patients** ("Teetotalers too" off): psychite tea for mood.
  - **Infection**: Masala Chai from Vanilla Brewing Expanded.
  - **Scars**: Neoderm from Remedies (Continued). A scar alone does not keep a patient in the
    hospital, so the drug only applies while they are being treated for something else.

  The base game has few suitable drugs, so most options come from other mods.

### Bed choice
Patients pick a hospital bed based on what they need:

- Surgery patients: the bed with the highest surgery success chance (bed quality, cleanliness,
  light, vitals monitor).
- Sick patients: the bed with the fastest immunity gain.
- Others: the bed with the best tend quality.

### Mass casualties during danger
New hospital setting. When unticked, mass casualty events wait until there are no manhunters
or other aggressive animals on the map. Ticked (the default) keeps the original behaviour.

### Sedated aggressive patients (optional)
Patients no longer arrive with scaria. **Allow aggressive patients** in Hospital Policies (requires
"Accept surgery") re-enables this as an optional challenge: the patient arrives sedated by their faction
and lies where the pod landed. They need to be brought to a bed and cured before the sedation wears off;
the arrival letter shows the remaining time. If it runs out, they wake up berserk. A death costs goodwill
as for any patient. The sedation time (default 8 hours) is set in the mod settings.

### Death penalty only for deaths you cause
New mod setting "Penalize deaths not caused by me", under the death goodwill slider (default off). Off: a patient
death costs goodwill only if it has no killer (illness, blood loss, failed surgery) or was caused by your faction.
Deaths caused by raiders, wild or manhunting animals, mechs or the environment cost nothing and show a message.
On: every patient death costs goodwill. A patient who bleeds out after an animal attack has no killer attached
and still counts.

### Failed surgery
After a failed operation the patient stays to recover instead of leaving, so the surgery can be
added again.

---

## Compatibility with other mods

Supported, recommended and compatible mods are listed in [RECOMMENDED_AND_COMPATIBLE_MODS.md](RECOMMENDED_AND_COMPATIBLE_MODS.md).
Check the markers next to each mod there to see which support is new in this fork and which
comes from the original Hospital.

---

## Fixes and adjustments

- Fixed freezes when generating certain disease, plague or wound patients.
- Patients are no longer assigned surgeries that the game cancels, which could leave them waiting.
- Wound and infection patients arrive with the condition they came for.
- Payment: empty silver stacks are no longer created; large payments are split into proper stacks.
- Mass casualty events respect the faction blacklist, don't fire without free beds,
  and the letter shows the actual number of patients.
- Arrested patients are removed from the hospital list.
- Saves from earlier versions of the mod load correctly.
- Performance improvements with many pawns on the map.
