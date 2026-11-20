# Hospital – feature overview

What the mod does in its current version. For what this fork changed compared to the
original mod, see [NEW_FEATURES.md](NEW_FEATURES.md).

Supported game versions: RimWorld 1.6 and 1.7. Requires Harmony.

---

## How it works

Run a hospital for the people of other factions. When you have free medical beds and your
hospital is open, patients from friendly and neutral factions arrive by drop pod and ask for
treatment. You care for them, and when they leave healthy they pay in silver and their faction's
goodwill improves.

### Patients
Each patient comes for one of three reasons:

- **Wounds** – gunshot wounds, bruises or burns that need tending.
- **Disease** – an illness that needs medicine and care until they recover.
- **Surgery** – a problem that needs an operation, or a wish for an implant or prosthetic.

### Payment and goodwill
- The bill grows with the treatment you give: medicine used for tending, meals fed to the
  patient, and surgeries (time and materials).
- Patients pay when they leave. The silver is dropped where they stand.
- Goodwill depends on the patient's mood when they leave: happy patients bring more.
- A patient who dies costs goodwill with their faction, and so does a failed surgery. By default a death only costs goodwill when you caused it or it had no killer (illness, blood loss, failed surgery); a setting makes every death count.
- After a failed surgery the patient stays to recover, so you can try again.

### Mass casualty events
From time to time a disaster floods your hospital with patients at once: **a pandemic**, **a big
raid**, **a large plane crash** or **a large building fire**. The number of patients depends on
how many hospital beds you have. Some of the victims can be people who already exist in the
world, and even the faction's leader if your goodwill with that faction is at least 80.

### Aggressive patients (optional challenge)
When enabled, a patient may arrive with an aggression-causing condition such as scaria. Their
faction sedates them for transport, so they arrive unconscious where the drop pod landed. Bring
them to a bed and cure them before the sedation wears off; the arrival letter shows the time
left. If it runs out, they wake up berserk.

---

## Your hospital

### Beds
- Every medical bed for humans can be a **Hospital Bed** (for patients) – toggle it with the
  bed's button. Hospital beds have their own sheet colour.
- A hospital bed can also be marked as a **Surgery Bed**. Surgery patients prefer these beds,
  other patients avoid them.
- Patients pick the bed that suits them best:
  - surgery patients: the highest surgery success chance (bed quality, cleanliness, light,
    vitals monitor),
  - sick patients: the fastest immunity gain,
  - others: the best tend quality.

### Patient landing spot
Build a **Patient landing spot** to choose where the drop pods land.

### Patients tab
A **Patients** tab lists everyone in your care with their diagnosis, the treatment they need,
their satisfaction, time spent in the hospital and current bill. From here you can:

- **Dismiss** a patient (they leave without paying),
- **Refuse operation** – send the patient away and never accept that operation again,
- open **Hospital Policies**.

### Hospital Policies (per map)
- **Accept patients** – open or close the hospital.
- **Opening hours** – the hours when patients may arrive.
- **Enable mass casualty events**.
- **Accept surgery** – whether surgery patients come at all.
- **Allow aggressive patients** – the challenge described above.
- **Allow patients during danger** – whether patients come during dangers such as toxic
  fallout or manhunters.
- **Mass casualties during danger** – whether mass casualty events may happen while the map
  is dangerous (e.g. manhunters).
- **Medical care** and **food policy** for patients.
- **Patient drugs** – a drug policy for each kind of patient: all patients, infection,
  surgery, injuries and scars. A patient gets the drugs of every list that applies to them.
  Doctors give the drugs marked "Scheduled", following the policy's frequency and mood/joy
  limits, and the drugs are added to the bill. Each list can include or skip teetotalers.
  The base game has few suitable drugs, so most options come from other mods
  (e.g. Vanilla Brewing Expanded, Remedies).
- **Blacklists** for surgeries, diseases and factions you don't want to see.

### Mod settings (global)
- Show a message instead of a letter when a patient arrives.
- Prevent incapacitated arrivals: patients arrive hurt but still able to walk. Turn it off for
  tougher mass casualty events with downed patients you have to rescue.
- Difficulty: silver multiplier, goodwill gain multiplier, goodwill loss on patient death and on
  surgery failure, whether deaths not caused by you also cost goodwill, disease and wound severity multipliers, sedation time of aggressive patients.

### Languages
English. French, German and Spanish translations are partial.

---

## Compatibility with other mods

Supported, recommended and compatible mods are listed in [RECOMMENDED_AND_COMPATIBLE_MODS.md](RECOMMENDED_AND_COMPATIBLE_MODS.md).
