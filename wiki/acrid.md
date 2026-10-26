# Acrid

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[Acrid]`. Available toggles: `EnableStatsChanges`, `EnablePassiveChanges`, `EnableSecondaryChanges`, `EnableUtilityChanges`, `EnableSpecialChanges`. Restart after editing; use matching settings across multiplayer peers.

## Passive


- Blight: Each stack lasts a fixed 8 seconds. Each damage tick has 0.1 proc coefficient per stack, capped at 1.0 total per victim per tick. The cap is shared across attackers. Native damage, stack scaling and tick frequency remain; Blight ticks can trigger ordinary on-hit items.

## Secondary


- Neurotoxin: Base cooldown increased from 2 seconds to 2.5 seconds.

## Utility


- Frenzied Leap: Base charges reduced from 2 to 1.
- Frenzied Leap: Base cooldown reduced from 10 seconds to 8 seconds.
- Frenzied Leap: Impact is now Poisonous, following the equipped passive. The existing 2-second cooldown refund per enemy hit is retained.

## Special


- Cloying Breath: With Poison equipped, applies Poison every accepted tick. With Blight equipped, applies Blight on ticks 1, 5, 9, etc. for each cloud/enemy pair. The former extra 40% Blight chance is removed. Cloud damage, tick frequency and other native properties remain.

## Stats


- Added spawn immunity throughout Spawn and for 2 seconds after exiting it. The tail is granted only if Acrid is still alive.
