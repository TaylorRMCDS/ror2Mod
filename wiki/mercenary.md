# Mercenary

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[Mercenary]`. Available toggles: `EnablePrimaryChanges`, `EnableSecondaryChanges`, `EnableUtilityChanges`, `EnableSpecialChanges`. Restart after editing; use matching settings across multiplayer peers.

## Primary


- Laser Sword: The third swing grants a visible Expose Ready buff while its hitbox is active. The buff uses the existing Expose icon and color.
- Laser Sword: Using a skill during that active window cancels the sword animation but preserves the third-swing hitbox and Expose Ready throughout the follow-up skill's preparation and execution. Both end when that skill finishes or is interrupted; a later skill does not chain another extension.
- Laser Sword: The retained sword volume follows Mercenary and preserves the original swing's damage, native Expose and already-hit enemy history. It does not repeatedly damage an enemy already hit by that swing or add Expose to the follow-up skill's own attacks.

## Secondary


- Rising Thunder: Damage increased from 550% to 600%.
- Rising Thunder: Base duration shortened by 25%. Integrated movement curves preserve base-distance travel without scaling the remaining upward velocity.
- Whirlwind (ground/air): Faster attack speed shortens the movement animation without reducing its base movement-phase distance.

## Utility


- Blinding Assault and Focused Assault: Dash duration scales with attack speed while preserving base dash distance. Native walls, hit-stop and interrupts remain.

## Special


- Eviscerate: Preparation/entry dash accelerates with attack speed without losing base dash distance. Target-searching/damage phase and target stopping remain native.
