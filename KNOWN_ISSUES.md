# Known issues

Problems in the version currently on the
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3843639021).
An issue stays on this list until the release that fixes it is published on the Workshop.
Fixes may reach this repository earlier; see the [changelog](CHANGELOG.md).

Found something that is not listed here? Please report it on
[GitHub Issues](https://github.com/mgrTDziedzic/Hospital-Fork/issues).

## Fixed, waiting for the Workshop update

### Patients stop arriving while Hospitality visitors are on the map

- **Affects:** 3.0.0 (also the original Hospital).
- **Fixed in:** 3.0.1 (in this repository, not yet on the Workshop).
- **What happens:** when a patient is about to arrive, factions hostile to the faction of any
  pawn on the map are skipped. Visitors from Hospitality and current patients are counted too,
  although as guests of the colony they never fight other non-hostile factions. Most non-player
  factions are hostile to each other, so visitors from one faction limit patients to that faction
  and its friends. Visitors from two factions that are hostile to each other usually stop patient
  arrivals completely.
- **Workaround:** patients arrive again once the visitors have left the map.

## Open

None listed at the moment.
