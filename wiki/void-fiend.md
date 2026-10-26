# Void Fiend

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[Void Fiend]`. Available toggles: `EnablePassiveChanges`, `EnablePrimaryChanges`, `EnableSecondaryChanges`, `EnableSpecialChanges`. Restart after editing; use matching settings across multiplayer peers.

## Passive


- Void Corruption: Out-of-combat corruption gain reduced from 3 to 2 per second. In-combat gain and other corruption sources remain unchanged.

## Primary


- Drown (normal): Adds distance falloff to the vanilla full-damage beam. Damage stays at full strength through 50 meters, then falls linearly to 50% at 100 meters. Damage remains at 50% beyond 100 meters. Proc coefficient, maximum range and slow are preserved; corrupted Drown is unchanged.

## Secondary


- Flood (corrupted): Explosion radius increased from 10 to 15 meters.
- Flood (corrupted): Launch speed reduced from 70 to 50 m/s.
- Flood (corrupted): Maximum flight lifetime increased from 99 to 150 seconds. The separate 4-second explosion-expiry timer is also increased to 150 seconds. Impacts still detonate normally; damage, proc coefficient and normal Flood are unchanged.

## Special


- Suppress (normal): Healing reduced from 25% to 20% maximum health. Still consumes 25% corruption.
- Suppress (corrupted): Self-damage reduced from 25% to 20% maximum health. Still grants 25% corruption.
- Suppress (corrupted): Charge and recovery phases each shortened by 50%, reducing the total cast from 2 seconds to 1 second at base attack speed. Native attack-speed scaling is preserved; normal Suppress timing is unchanged.
