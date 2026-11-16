# Hospital in-game tests (dev only, RimWorld 1.6)

## Install
RimWorld only scans top-level folders in `Mods`, so the test mod needs its own junction (admin cmd may be needed).
Replace `<RimWorld>` with your game folder (e.g. `...\steamapps\common\RimWorld`) and `<repo>` with this repository's folder:

    mklink /J "<RimWorld>\Mods\Hospital-Tests-dev" "<repo>\Tests\HospitalTests"

Build it: `dotnet build Tests\HospitalTests\Source\HospitalTests.csproj` (output: `Tests\HospitalTests\1.6\Assemblies`).
Enable "Hospital Tests (dev)" in the mod list (after Hospital).

## Test save
Create a small map save named **HospitalTest** with:
- a few Hospital beds (medical beds) and a few normal beds
- a Patient Landing Spot
- one colonist doctor, some medicine, hospital open (no need to wait for patients)
Tests spawn and remove their own throwaway pawns/beds. Note: tests also run fine without a landing spot (one is spawned temporarily).

## Run manually
Dev mode -> debug actions menu -> "Hospital Tests" -> "Run all" (or "Run: <test>"). A results window opens.

## Run automatically
`powershell -File Tests\run-tests.ps1 [-SaveName HospitalTest] [-TimeoutMinutes 10] [-SaveDataFolder <path>] [-GameDir <path>] [-Dotnet <path>]`
- builds both projects, starts the game with `-hospitaltest=<SaveName>`, which loads the save, runs all tests, writes the results file and quits.
- exit code 0 = all passed.
- game folder: `-GameDir`, else the `RIMWORLD_DIR` environment variable, else detected from Steam (all Steam libraries).
- `dotnet`: `-Dotnet`, else PATH, else the default install location.
- Hospital is built in Release and deterministically, so rebuilding unchanged sources leaves the committed `1.6/Assemblies/Hospital.dll` byte-identical.
- `-SaveDataFolder` (off by default) uses a separate data folder; it needs its own ModsConfig.xml (Harmony + Hospital + Hospital Tests enabled) and the save in its Saves folder.

## Results
`HospitalTestResults.txt` in the game data folder (`GenFilePaths.SaveDataFolderPath`, default
`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\`). Last line: `RESULT: N passed, M failed`.
A test also fails if the game logs an error while it runs.

## Writing tests
Add a `public static void` method with `[HospitalTest("description")]` in `Tests/HospitalTests/Source/Tests/`; optional `TestContext ctx` parameter.
Use `ctx.Track(thing)` / `ctx.OnCleanup(action)` for anything you create or change.
