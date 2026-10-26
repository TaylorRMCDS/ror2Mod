# Artificer

T4ylorSurvivors 1.1.1 survivor changes.

Config section: `[Artificer]`. Available toggles: `EnableStatsChanges`, `EnablePrimaryChanges`, `EnableSecondaryChanges`, `EnableUtilityChanges`, `EnableSpecialChanges`. Restart after editing; use matching settings across multiplayer peers.

## Primary


- Plasma Bolt: Damage increased from 280% to 310%.
- Plasma Bolt: Direct and splash hits slow movement by 30% for 2 seconds.

## Secondary


- Cast Nano-Spear: Frozen Mithrix and False Son bosses execute below 10% health instead of 30%. Includes normal and hurt Mithrix and all three False Son boss forms. Other enemies retain the 30% frozen execution threshold.

## Utility


- Ion Surge: Moved from Special to Utility.
- Ion Surge: Unlocked by default when the slot swap is enabled.
- Ion Surge: Launch scales with movement speed, capped at twice its vanilla base-distance travel. Vanilla movement-speed scaling is retained below the new cap; the movement-speed factor is capped at 14 (base movement speed: 7). Native curve and duration are retained.
- Tectonic Blast: Aiming downward provides more aerial knockback.

## Special


- Snapfreeze: Damage increased from 100% to 300%.
- Snapfreeze: Unlocked by **Artificer: Orbital Bombardment** (kill 15 enemies before touching the ground) when the slot swap is enabled. The challenge reward text now names Snapfreeze.
- Snapfreeze: Moved from Utility to Special. Placement release input and cancelled-placement stock refunds now use the Special slot.
- Snapfreeze: Frozen Mithrix and False Son bosses execute below 10% health instead of 30%, covering the same boss forms as Nano-Spear. Other enemies retain the 30% threshold. Frozen health-bar execution markers match the new threshold; freeze duration and other execution mechanics are unchanged.
- Flamethrower: Damage range increased from 20 to 30 meters. Both hand effects extend forward by 50% to match the longer reach.

## Stats


- Base acceleration increased from 40 to 60. Movement speed is unchanged.

## Configuration notes


- Ion Surge and Snapfreeze swap slots only when both Utility and Special changes are enabled. Otherwise they retain their original slots; their balance changes follow the groups listed on this page. Re-select your loadout after changing the swap.
- Boss freeze execution uses a shared rule. It remains active while Artificer Secondary/Special or Railgunner Special changes are enabled.
