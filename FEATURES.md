# What's new in Hospital (Fork)

A player-friendly overview of what this fork adds to [Hospital](https://github.com/tomvd/Hospital).
For the full, detailed list of every change see [CHANGELOG.md](CHANGELOG.md).

Supported game versions: RimWorld 1.6 and 1.7.

---

## New features

### Patient drugs
You can now decide which drugs your patients receive while they are in your care.

- Open **Hospital Policies → Patient drugs** and pick a drug policy for each kind of patient:
  - **All patients**
  - **Infection** – sick patients who are not yet immune
  - **Surgery** – patients waiting for an operation
  - **Injuries** – patients with fresh wounds or blood loss
  - **Scars** – patients with permanent injuries
- A patient gets the drugs of **every** list that applies to them.
- Only drugs marked **Scheduled** in the policy are given. Your doctors administer them as
  medical bills, respecting the policy's frequency and mood/joy limits.
- **Teetotalers too** – choose per list whether teetotaler patients should also get those drugs.
- Drugs are billed to the patient like any other treatment.
- Use **Manage drug policies...** to create policies just for your hospital. For example:
  - **All patients** (with "Teetotalers too" off): psychite tea to keep them in a good mood.
  - **Infection**: Masala Chai from Vanilla Brewing Expanded for patients fighting an infection.
  - **Scars**: Neoderm from Remedies (Continued) for patients with scars. For now this is a
    small bonus: a scar alone does not keep a patient in your hospital, so the drug only works
    while they are being treated for something else. In practice the scars usually heal before
    the patient leaves.

  The base game has only a few drugs that fit, so most useful ones come from other mods.

### Smarter bed choice
Patients now pick the hospital bed that suits them best, not just the first one you built:

- Surgery patients go to the bed with the best surgery success chance (bed quality, cleanliness,
  light, vitals monitor).
- Sick patients go to the bed where they gain immunity fastest.
- Everyone else gets the bed with the best tend quality.

So it is now worth building one really good operating room.

### Mass casualties during danger
New hospital setting. Untick it and mass casualty events will wait until there are no manhunters
or other aggressive animals on your map. Ticked (the default) keeps the old behaviour.

### Sedated aggressive patients (optional challenge)
Patients no longer arrive with scaria and attack your colonists (a bug in earlier versions). If you want
the risk, tick **Allow aggressive patients** in Hospital Policies (needs "Accept surgery"). Such a patient
arrives sedated by their faction and lies helpless where the pod landed. Get them to a bed and cure them
before the sedation wears off; the arrival letter tells you how long you have. Too late, and they wake up
berserk. If they die, you lose goodwill as for any dead patient. The sedation time (default 8 hours) is a
slider in the mod settings.

### Failed surgery is not the end
If an operation fails, the patient now stays to recover instead of walking away. You can add the
surgery again and give your doctor another try.

---

## Compatibility with other mods

| Mod | What you get |
|---|---|
| **MedPod** | Patients healed in a MedPod are recognised as cured and pay for the treatment. Other mods' beds with "Pod" in the name now work as normal hospital beds. |
| **Multi-Doctor Surgery** | Patients no longer leave right after arriving. To have a whole team operate on patients, turn on its **"(Beta) Fast Operation"** option and set a default surgery team. Patient surgeries then get that team automatically. Without Fast Operation, or if the team's lead surgeon can't do that operation, a single doctor operates as usual. |
| **Plasteel Surgery (Continued)** | Cosmetic and body-shape surgeries are blocked for patients by default on new maps (you can change this in the surgery blacklist). Patients are never assigned a surgery they cannot actually receive. |

All of these are optional. Nothing changes if you don't use them.

---

## Fewer bugs, smoother games

- **No more freezes** when the game creates certain disease, plague or wound patients.
- **No more stuck patients** – patients are no longer given surgeries that the game silently
  cancels, which used to leave them waiting forever.
- **Patients arrive with what they came for** – wound patients really have wounds, and infection
  patients are not healthy on arrival.
- **Fair payment** – no more empty or broken silver stacks; large payments arrive in proper stacks.
- **Mass casualty events** respect your faction blacklist, never fire when you have no free beds,
  and the letter shows the real number of patients.
- **Arrested patients** are removed from the hospital list.
- **Old saves load cleanly**, even from earlier versions of the mod.
- **Better performance** with many pawns on the map.
