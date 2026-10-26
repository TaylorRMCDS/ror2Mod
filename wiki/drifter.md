# Drifter

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[Drifter]`. Available toggles: `EnableUtilityChanges`. Restart after editing; use matching settings across multiplayer peers.

## Utility


- Tornado Slam: Charge and spin steering are now 3x vanilla.  Native charge steering is 180 degrees/s -> 540 degrees/s; spin steering is 90 -> 270 degrees/s. Native zero-turn bounce pauses remain zero; the 30-degree/s recovery setting is read at 90 degrees/s. Steering values are scaled only when read, preventing repeated casts from multiplying fields.
- While actively spinning, attract unclaimed allied Junk within 15 meters and line of sight. Attraction speed is capped at 30m/s, with 60m/s^2 acceleration and an approach limit to avoid overshooting the collector. With multiple spinning Drifters, the nearest eligible teammate attracts it.
- Pulling stops upon cancel, interruption, death, leaving the radius or loss of line of sight. Charging alone does not attract Junk. Existing native homing targets, pickup delay and collection rules remain unchanged.
- Removed ALL custom boss/size ricochet rules, nonboss push suppression, enemy swinging/guided throws, extra collision damage, enemy motor hooks and extra combat callbacks. Boss ricochets, bounce speed/duration limits, damage, ordinary knockback, Junk generation and Primary/jump cancellation now use the game's original behavior.
