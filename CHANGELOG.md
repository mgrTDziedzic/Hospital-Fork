# Changelog

Changes made in this fork ([mgrTDziedzic/Hospital-Fork](https://github.com/mgrTDziedzic/Hospital-Fork))
relative to the original [Hospital](https://github.com/tomvd/Hospital) by Adamas (tomvd).
Original code is © its author, licensed under GPLv3; modifications are © mgrTDziedzic, also GPLv3.

## 2026-11-16
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
- Clarified the log message shown when no disease is eligible for a patient.
- 1.7: added the missing English translation key for the externally-cured condition message.

## 2026-11-15
- Detect conditions cured externally (MedPod compatibility).
- Fix global RNG reseeding and wound generation edge cases.
- Validate patient landing cell before generating pawns.
- Fix inverted owner removal when toggling hospital bed mode.
