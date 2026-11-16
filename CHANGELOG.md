# Changelog

Changes made in this fork ([mgrTDziedzic/Hospital-Fork](https://github.com/mgrTDziedzic/Hospital-Fork))
relative to the original [Hospital](https://github.com/tomvd/Hospital) by Adamas (tomvd).
Original code is © its author, licensed under GPLv3; modifications are © mgrTDziedzic, also GPLv3.

## 2026-11-16
- New: patient drug policies. In Hospital Policies pick a vanilla drug policy for each of five lists: "All patients", "Infection" (immunizable disease not yet immune), "Surgery" (real surgery bill or pending cure), "Injuries" (non-permanent injury or blood loss) and "Scars" (permanent injury). A patient gets the union of every list that applies. Drugs with "Scheduled" ticked in the policy are administered by doctors as "Administer X" medical bills placed at the top of the patient's bill list (checked every 250 ticks). The entry's frequency (counted from the last completed dose, not from a failed attempt), mood limit and joy limit are respected, there is at most one waiting bill per drug, and nothing is added for a patient who would be sent home anyway (same rule as the discharge check, so a scar or blood loss alone never keeps a patient, while a tended but still sick or healing patient keeps getting drugs). Each list has a "Teetotalers too" toggle (defaults: All off, Infection on, Surgery on, Injuries on, Scars off); teetotaler patients skip lists whose toggle is off. Leftover drug bills are removed when the patient leaves. Drugs are billed like any other medical bill. Settings are saved per map and synced in Multiplayer.
- Fix: completing or deleting a medical bill other than the cure surgery (e.g. an administered drug) no longer clears the patient's pending-surgery flag; only the cure recipe does.
- Fix: unfinished "Administer drug" bills no longer keep a patient in the hospital or send them to a surgery bed (new `SurgeryUtility.HasRealSurgeryBills`). Vanilla `ShouldSeekMedicalRest` counts any bill that should be done now, so the discharge check now uses `SurgeryUtility.ShouldSeekMedicalRestIgnoringBills` instead.
- Tests: added `DrugPolicyTests` (Drugs1-17) to the in-game test suite and adjusted a vanilla-compat test to the cure-recipe-only pending flag rule.
- Patients now pick the best hospital bed for their condition instead of the first one built: surgery patients get the highest surgery success chance (bed quality, room cleanliness, light, vitals monitor), patients with a disease they are not yet immune to get the fastest immunity gain, and others get the best tend quality. Beds marked for surgery are still preferred by surgery patients and avoided by the others; on equal stats the nearer bed wins.
- Performance: the hospital map component is cached per map, so the patient check run by every pawn's work search no longer scans all map components.
- Added fork notice and attribution (README, About.xml, this changelog).
- Dropped RimWorld 1.4 and 1.5 support; this fork targets 1.6 (and 1.7).
- 1.6 assembly is now a deterministic Release build; test tooling no longer uses machine-specific paths.
- Fix errors after loading a save that lacks hospital settings (opening hours, refused lists, patients) from an older mod version.
- Fix a possible game freeze when generating a disease or plague patient: attempts are capped, failed hediffs are removed and the patient falls back to wounds; diseases without a lethal severity are no longer picked.
- Fix an arrested patient staying on the hospital roster (they kept being billed and treated as a patient).
- Fix "deaf" surgeries targeting a kidney, and special surgery rules discarding the chosen body part when the pawn lacks the special part.
- Fix infection / lung rot patients arriving healthy with a diagnosis and a bill when no suitable body part exists (they now fall back to wounds).
- Fix interrupted feeding jobs being billed, and a possible error when the food is gone.
- After a failed surgery the patient now stays to recover instead of being sent away; the surgery can be re-added for another attempt.
- Fix a possible game freeze when generating a patient: attempts are capped (20); rejected pawns (androids, non-bleeding) are discarded instead of piling up in the world pawn pool; a faction without pawn kinds is skipped.
- Mass casualty events now respect the faction blacklist and skip androids / non-bleeding pawns; with the plague blacklisted the pandemic type is not chosen. An event with no beds (0 pawns) no longer fires and no longer sends an empty letter; the letter shows the real patient count.
- Fix payment silver: amounts are rounded, a stack of 0 is never created, and large payments are split by the silver stack limit.
- Rename the inverted `includeDismissed` parameter of `PatientUtility.IsPatient` to `activeOnly` (true = only patients not yet discharged); behaviour of every call is unchanged.
- Added an in-game test suite (`Tests/HospitalTests`, a separate dev-only mod that exists only in the repository and is not part of the Hospital mod) covering the fixed bugs; run it with Dev mode -> Debug actions -> "Hospital Tests" (see `Tests/README.md`).
- Fix wound patients arriving without wounds when the wound severity multiplier is small (damage budget is at least 1 and rounded, not truncated).
- Fix an error when a WorkGiverDef without a work type (some mods) is evaluated for a patient.
- New per-map setting "Mass casualties during danger" (default on = unchanged behaviour: mass casualty events ignore manhunters etc.). Switch it off to make mass casualty events wait for a map without aggressive pawns; weather dangers still follow "Allow patients during danger". Translated for French, German and Spanish variants.
- Clarified the log message shown when no disease is eligible for a patient.
- 1.7: added the missing English translation key for the externally-cured condition message.

## 2026-11-15
- Detect conditions cured externally (MedPod compatibility).
- Fix global RNG reseeding and wound generation edge cases.
- Validate patient landing cell before generating pawns.
- Fix inverted owner removal when toggling hospital bed mode.
