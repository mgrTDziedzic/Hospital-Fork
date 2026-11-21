# Hospital (Fork)

> **Fork notice:** This is a fork of [Hospital](https://github.com/tomvd/Hospital) by
> Adamas ([tomvd](https://github.com/tomvd)), licensed under the GNU GPLv3.
> Modified by [mgrTDziedzic](https://github.com/mgrTDziedzic) since November 2026.
> All credit for the original mod goes to its author. Changes made in this fork are listed in
> [CHANGELOG.md](CHANGELOG.md).
>
> Original mod on Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=2992224079
> If you enjoy Hospital, please consider supporting its original author; details are on the
> original mod's Steam Workshop page.

> [!WARNING]
> **Early stage.** This fork is a work in progress and may contain bugs. I strongly
> recommend backing up your save before trying it. The fork is available on the
> [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3843639021).

A RimWorld mod that triggers events to receive patients you have to care for.

The goal of this fork is to build on the great Hospital mod by tomvd: new features, wider
compatibility with other mods, bug fixes and performance improvements. The work is done with
the original author's permission, but he is not involved in its development. Please
report any problems with this fork here, not to the original author.

- [OVERVIEW.md](OVERVIEW.md) - everything the mod currently offers
- [NEW_FEATURES.md](NEW_FEATURES.md) - an overview of what the fork adds and changes
- [RECOMMENDED_AND_COMPATIBLE_MODS.md](RECOMMENDED_AND_COMPATIBLE_MODS.md) - mods that pair well with Hospital, including the ones the fork explicitly supports
- [PLANNED_FEATURES.md](PLANNED_FEATURES.md) - features and ideas planned for the future

## Ideas and suggestions

Have an idea for the mod? Feel free to suggest it on
[GitHub Issues](https://github.com/mgrTDziedzic/Hospital-Fork/issues). One caveat: I don't
own any of the official DLCs, so ideas involving them won't be implemented for now.

## AI use

This fork is being developed with the help of AI coding tools (Claude). I'm a professional
software developer (ERP systems), though new to C# and RimWorld modding. Most of the code
is written by AI; I decide what to change and test every change in-game.

To reduce risk, the fork adds:
- automated in-game tests covering core hospital logic ([list of tests](Tests/TESTS.md)),
- debug actions for spawning and inspecting patients and mass casualty events ([description](Tests/DEBUG_ACTIONS.md)),

and is playtested manually. If you run into a bug, please report it on GitHub Issues.

The art for the Patient Landing Spot variants (the 3x3 and 5x5 landing pads) was also made
with AI. These pads are optional eye candy: the default landing spot is always available, and
you can hide the variants from the build menu with the "Show patient landing pads"
option in the mod settings.

## Credits

- Original mod: Adamas (tomvd).
- Contributors to the original: Average Otter, MCOfficer, Sakura-TA, Spagles.
- Settings UI helper code: AlexTD and Why_is_that (as credited in `SettingsHelper.cs`).

## License

Copyright (C) 2023–2026 Adamas (tomvd) and contributors.
Fork modifications Copyright (C) 2026 mgrTDziedzic (Niejeden).

This project is licensed under the GNU General Public License v3.0, the same as the original — see [LICENSE](LICENSE).

Third-party: `0MultiplayerAPI.dll` is the RimWorld Multiplayer API (MIT, © 2018 Zetrith) — see [LICENSES/MultiplayerAPI-LICENSE.txt](LICENSES/MultiplayerAPI-LICENSE.txt).
