# Changes in this fork

An overview of the changes this fork makes to [Hospital](https://github.com/tomvd/Hospital) by tomvd,
which remains the basis for everything below. For the detailed list see [CHANGELOG.md](CHANGELOG.md).

Supported game versions: RimWorld 1.6 and 1.7.

---

## Additions

### Patient drug policies
Lets you choose which drugs patients receive while in your care.

- **Hospital policies → Patient drug policies** has a drug policy for each kind of patient:
  - **All patients**
  - **Infection** – sick patients who are not yet immune
  - **Surgery** – patients waiting for an operation
  - **Injuries** – patients with fresh wounds or blood loss
  - **Scars** – patients with permanent injuries
- A patient gets the drugs of every list that applies to them.
- Only drugs marked **Scheduled** in the policy are given. Doctors administer them as
  medical bills, following the policy's frequency and mood/joy limits.
- **Include teetotalers** – per list, whether teetotaler patients also get those drugs.
- Drugs are billed to the patient like other treatment. Billing for drugs comes from the original
  Hospital; what is new is that the drugs are given automatically.
- **Manage drug policies** creates policies for the hospital. Examples:
  - **All patients** ("Include teetotalers" off): psychite tea for mood.
  - **Infection**: Masala Chai from Vanilla Brewing Expanded.
  - **Scars**: Neoderm from Remedies (Continued). A scar alone does not keep a patient in the
    hospital, so the drug only applies while they are being treated for something else.

  The base game has few suitable drugs, so most options come from other mods.

### Optimal bed choice
Patients pick a hospital bed based on what they need:

- Surgery patients: the bed with the highest surgery success chance (bed quality, cleanliness,
  light, vitals monitor).
- Sick patients: the bed with the fastest immunity gain.
- Others: the bed with the best tend quality.

### Mass casualties during danger
New hospital policy, "Allow mass casualties during danger". When unticked, mass casualty events wait until there are no manhunters
or other aggressive animals on the map. Ticked (the default) keeps the original behaviour.

### Sedated aggressive patients (optional)
Patients no longer arrive with scaria. **Allow aggressive patients** in Hospital policies (requires
"Allow surgery patients") re-enables this as an optional challenge: the patient arrives sedated by their faction
and lies where the pod landed. They need to be brought to a bed and cured before the sedation wears off;
the arrival letter shows the remaining time. If it runs out, they wake up berserk. A death costs goodwill
as for any patient. The sedation time (default 8 hours) is set in the mod settings.

### Death penalty only for deaths you cause
New mod setting "Penalize deaths not caused by me", under the death goodwill slider (default off). Off: a patient
death costs goodwill only if it has no killer (illness, blood loss, failed surgery) or was caused by your faction.
Deaths caused by raiders, wild or manhunting animals, mechs or the environment cost nothing and show a message.
On: every patient death costs goodwill. A patient who bleeds out after an animal attack has no killer attached
and still counts.

### Patient landing pad variants
Besides the default 1x1 patient landing spot, Architect > Misc has a **patient landing pads** group with larger,
purely cosmetic variants: 3x3 and 5x5 in nine designs (tribal, industrial, spacer, field tarp, mechanoid, royal,
tundra, painted marking, medevac). They work like the default spot; patients land on the centre cell. A pad
blocks its whole area for walls and furniture, and cannot be rotated. The mod setting "Enable Patient Landing Spot
variants" (default on) hides them from the build menu. The default 1x1 spot is always available, and pads
already placed keep working when the setting is off.

### Patient backstories (optional)
Wounded and sick patients arrive with a short generated story of how they got hurt or ill, e.g.
"Name was settling an argument about practicing dice with pistols when the thicket turned out to be
steeper than it looked, and the result was nasty injuries." or "Name caught gut worms while sharing
a spoon with the owl on a bet with the archaeologist."
The story matches the kind of wound or disease (modded diseases are named too) and can mention the
patient's faction or title. It appears in the arrival letter and as a tooltip in the Hospital tab
and the patient's Bio tab. When a wounded or sick patient leaves, the message adds a line that refers back
to the story. Surgery and mass casualty patients get none. The mod setting "Show patient backstories"
(default on) hides them.

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

- Disease, plague and wound patients are generated without freezing the game.
- Patients arrive while visitors (e.g. from Hospitality) from factions hostile to each other are on the map.
- Patients are assigned only surgeries the game can perform on them.
- Wound and infection patients arrive with the condition they came for.
- Payment: silver arrives in valid stacks; large payments are split by the stack limit.
- Mass casualty events respect the refused factions list, don't fire without free beds,
  and the letter shows the actual number of patients.
- Arrested patients are removed from the hospital list.
- A patient's arrival leaves no blood, "Headshot" text or pain sounds on the landing spot.
- Modded surgeries with "gr" in their name (e.g. upgrades, grafts) can be requested by patients.
- Beds from other mods with "Pod" in their name work as regular hospital beds; MedPod handling applies only to MedPod beds.
- Deafness surgeries target an ear.
- Feeding a patient is billed only when the meal is completed.
- Saves from earlier versions of the mod load correctly.
- Performance improvements with many pawns on the map and when generating mass casualty events.
