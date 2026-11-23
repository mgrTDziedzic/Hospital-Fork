# Planned Features and Ideas

This file tracks what may come to Hospital in future versions. Nothing here is a promise. Priorities and scope can change.

## Planned Features

Features that are planned for future versions.

### General

- **Staff skill mood modifiers.** Patient mood modifiers that depend on the Medical and Social skills of the doctors and nurses treating them.
- **Expanded patient rating system.** A more detailed system for how patients rate the quality of their care, and a bigger effect of that rating on rewards.
- **More treatment pricing options.** More ways to configure what patients pay for treatment.

### Mod Compatibility

- **MedPod.** Patients who come in for treatments that only a MedPod can provide:
  - limb regeneration, including patients with an existing prosthetic that a doctor removes first,
  - scar removal,
  - addiction cure.
- **Remedies (Continued).** Patients with scars to be removed using Neoderm.

## Ideas

Ideas under consideration. They might not be worth doing, or might not be possible at all.

- **Patient backstories (flavor text).** A short description of what happened to each patient. Approaches to weigh:
  1. **Randomly generated combinations**, in the style of RimWorld's art descriptions. *Started: wounded and sick patients (text in the arrival letter and as a tooltip on Diagnosis in the Hospital tab; activity matches the kind of wound or disease). Surgery patients have none yet.* They don't have to make much sense. Examples: *"X suffered severe injuries while petting a hare."* / *"Y lost a leg in a horrible accident while eating a simple meal."*
  2. **A large pool of hand-written stories.** Example: *"X's tongue was cut out so he could not testify against a corrupt noble. His relatives raised money for a bionic tongue so that justice could be served."*
  3. **A mix of both.** Some patients get a hand-written story, but most get a randomly generated one, so the hand-written ones don't start repeating too soon.
- **Patients stay until fully cured.** Patients with conditions that need repeated tending (e.g. sensory mechanites) would stay until the condition is fully healed, instead of leaving after the first tending. Open problem: what do they do between treatments? They need to eat and sleep somewhere.
- **New mass casualty event: freed captives.** A friendly faction has rescued its people from pirate captivity and sends them to the player's hospital. The freed captives:
  - are severely malnourished,
  - may have hypothermia,
  - have a high chance of infections,
  - have a high chance of scars and missing limbs (from torture),
  - may arrive with poorly tended wounds (self-tended in captivity, or field-tended by the non-doctor warriors who freed them).
- **Mass casualties ignore bed count (optional challenge).** Mass casualty events would not take the number of free beds into account. The player might have to treat patients on the floor, or quickly turn other rooms into wards.
- **Patient recreation (Joy).** Track patients' recreation need. Better recreation means a better rating, so the player would need to provide things like TVs in the hospital. To evaluate: is it feasible, and how would it affect gameplay?
  - A new work type for pawns: entertaining patients, similar to how the Hospitality mod's Entertain works for guests.
- **Animal hospital.** Wealthy people and nobles send their beloved pets for treatment. To consider: should rewards and penalties be higher or lower than for human patients?
  - This would also give a reason to build VetPods from the MedPod mod, which normally see little use. For example, the chief of a friendly tribe might want their elephant permanently cured of asthma.
- **MedPod treatment pricing.** The pricing rules for MedPod treatment still need to be worked out. Options:
  - a fixed price per cured condition or regenerated limb,
  - a fee based on the power the MedPod used.
- **Multiple treatments per patient.** A single patient could request several surgeries or treatments in one visit. Examples:
  1. Wound treatment plus scar removal.
  2. Gender change plus a matching body type change (requires Plasteel Surgery).
  3. Laryngoplasty, immunity boost and beauty increase (requires Plasteel Surgery).
- **Critical cases (optional challenge).** Very hard cases: patients on the verge of death, e.g. with an infection that has progressed very far. They can be saved with luciferium or medicines from other mods.
- **Pain affects the rating.** The less the patient suffers, the higher the rating. This would give a reason to use painkillers from mods or general anesthesia.
- **Food quality affects the rating.** Meal quality would affect the patient's rating directly, on top of the usual mood effects from eating.
- **Hospital environment affects the rating.** The hospital's cleanliness, beauty and wealth would affect the patient's rating directly, on top of the usual mood effects they cause.
- **Per-condition frequency settings.** Mod settings that give more control over how often each condition appears.
- **Patients arriving on foot.** Patients walk to the hospital instead of arriving by transport pod. To decide: should this apply only to lightly injured patients and planned surgeries, or to every kind of condition? Having to send a pawn to fetch a patient who collapsed on the way could add variety, but it could also mean too much micromanagement for the player.
  - Fetching patients who collapsed on the way may be a good task for animals trained in rescue.
- **Patients leaving by transport pod.** Discharged patients leave the hospital in transport pods. Open questions: is it feasible, how should the player be compensated for the resources spent on the pods, and can the process be automated?
- **Drug policies for prisoners.** The patient drug policies could also cover prisoners. Each list would get an "Include prisoners" option, and a new "All prisoners" list would let the player keep separate drug lists for patients and prisoners. The per-condition rules would still apply.
- **Patients bring their own bionics.** Some patients arrive with their own bionic part to be installed and pay only for the surgery itself. Open problem: how to prevent exploits, such as sending the patient away and keeping the part. To check: can the core game's biocoding mechanic be applied to bionic parts?
- **Billing for drugs from automatic devices.** A user mentioned that some mods add devices that give drugs automatically (e.g. auto IV). Hospital doesn't detect drugs given this way, so they're not added to the patient's bill. To check: which mods these are, and whether support for them can be added.
- **Expectations and payment depend on the faction's tech level.** Patients from different factions would expect different things and pay in different ways. For example:
  1. **Tribal:** low expectations. The quality of care and of the hospital matters less. They pay mostly in raw materials (wood, steel, raw meat, vegetables), rarely in silver, and very rarely in gold. If the care is exceptionally good (more than they would expect), they may leave a piece of art as a gift, such as a small sculpture of at least normal quality. The art could sometimes be worth more than the treatment itself. That would reward the player for treating tribal patients well, even though they normally pay little.
  2. **Medieval / settler:** quality of care matters more. They pay mostly in silver, sometimes in gold.
  3. **Industrial / spacer:** quality of care matters a lot. They pay in silver or gold. With a very high rating, they may add a high-tech gift, such as an item normally available only as a quest reward.
- **Patient teleporter (new research).** Once researched and built, discharged patients leave through a teleporter instead of walking to the map edge, which eliminates the risk of them being attacked on the way. Open questions: is it feasible, and does it make sense?
- **Bill breakdown tooltip.** A tooltip on the "bill" column of the Patients tab would list the patient's recent charges with their reasons (tend, meal, surgery, drug), visible without dev mode. This needs a short list of charges stored in the patient's data, which changes the save format. Old saves would start with an empty list.
