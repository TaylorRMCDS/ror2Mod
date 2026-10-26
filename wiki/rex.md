# REX

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[REX]`. Available toggles: `EnablePrimaryChanges`, `EnableUtilityChanges`, `EnableSpecialChanges`. Restart after editing; use matching settings across multiplayer peers.

## Primary


- DIRECTIVE: Inject: Final syringe proc coefficient increased from 0.5 to 1.0. The first two syringes retain their 0.5 proc coefficient.

## Utility


- DIRECTIVE: Disperse: Replaces the forward-facing sonic boom with a blast directly beneath REX, affecting enemies within 13 meters of his feet and applying Weaken for 4 seconds.
- DIRECTIVE: Disperse: Pushes enemies up and away with 20 outward speed and 8 upward lift before normal mass resistance. Clay Dunestriders receive 1.5x the resulting knockback, including both outward and upward components.
- DIRECTIVE: Disperse: Adds an upward impulse to REX's existing momentum on the ground or in the air, at 90% of vanilla base airborne recoil strength. The movement-speed contribution to launch scaling is halved. Horizontal momentum is preserved; fast falls are slowed rather than automatically reversed.
- DIRECTIVE: Disperse: Displays a pale-green horizontal range ring that expands to 13 meters, briefly holds and fades over 0.65 seconds at the cast location.

## Special


- DIRECTIVE: Harvest: Damage reduced from 330% to 100%; recovery shortened from 1 second to 0.25 seconds at base attack speed. Adds Weaken for 4 seconds.
- DIRECTIVE: Harvest: Adds 5 shared Fruiting stacks to the enemy and refreshes the whole stack pool to 10 seconds. Reapplication adds to remaining stacks rather than resetting or capping the count.
- Fruiting: Replaces vanilla's fruit-on-death effect. Allied direct skill hits, including minion attacks, have a flat 20% chance to consume 1 stack, drop 1 fruit and deal bonus damage equal to 400% of the triggering attacker's damage stat. DOT and item-proc damage cannot trigger this effect.
- Fruiting: Bonus damage cannot crit or trigger on-hit effects. Normal kill rewards still apply.
- Fruiting: Fruits heal 15% of maximum health, retaining normal team pickup rules and a 20-second lifetime. Enemies use the existing fruit-cluster visual indicator. No additional fruit drops occur on death.
