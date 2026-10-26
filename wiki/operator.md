# Operator

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[Operator]`. Available toggles: `EnableUtilityChanges`. Restart after editing; use matching settings across multiplayer peers.

## Utility


- FIREWALL: If the entire use blocks nothing and deals no damage, halve its cooldown.
- The credit is granted after the held shield and released projectile end. Cooldown items apply before the 50% credit.
- Other Operator attacks, drone damage and item-proc damage do not affect the decision. Normal shield behavior, charge, damage, lifetime and stock remain.
- Refund decisions run on the server and are sent to the owning client. All multiplayer peers need this version with Operator changes enabled. Dead bodies, replaced skills and superseded casts receive no late refund.
