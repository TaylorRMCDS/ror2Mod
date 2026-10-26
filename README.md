# T4ylorSurvivors

T4ylorSurvivors is a configurable survivor balance mod for Risk of Rain 2. Its goal is to bring skills closer to their alternatives: give overlooked choices a stronger reason to be picked, tone down choices that overshadow their counterparts, and make more loadouts feel worthwhile.

The changes include buffs, nerfs, and adjustments to how skills work. Choose the changes you enjoy with separate config toggles for each survivor's modified ability groups.

## A few examples

- **Commando — Tactical Dive:** Gain 200 armor for 0.5 seconds when rolling, giving Dive a defensive benefit alongside its mobility.
- **Acrid — Blight:** Stacks last 8 seconds and their ticks can trigger on-hit items, with a shared proc coefficient cap.
- **Railgunner — Polar Field Device:** Deploys faster, pushes enemies outward, doubles allied jump height inside the bubble, and protects allied landings inside it from fall damage.
- **Operator — FIREWALL:** Halve the cooldown if the shield and released projectile block nothing and deal no damage.

Changed ability values and effects are highlighted in **purple** in game. Skill descriptions are in English.

## Installation

Install through r2modman or Thunderstore Mod Manager. The manager installs the required **BepInExPack** and **HookGenPatcher** dependencies.

Use one installed copy of T4ylorSurvivors. Launch with **Start modded**.

## Configuration

1. Launch the modded game once, reach the main menu, then quit.
2. In your mod manager, open **Config editor** and search for `t4ylor`.
3. Open `com.t4ylor.ror2.balancepatch.cfg`.
4. Find the survivor section and set the desired entries to `true` or `false`.
5. Save and restart the game.

The file is in your profile's `BepInEx/config` folder if you prefer editing it directly.

Version **1.1.0** provides separate toggles for **Stats, Passive, Primary, Secondary, Utility, and Special**, wherever that survivor has changes. Groups with no changes do not get a toggle. New installs enable all available groups by default.

For example, keep Commando's Utility changes while disabling his Secondary changes:

```ini
[Commando]
EnableSecondaryChanges = false
EnableUtilityChanges = true
EnableSpecialChanges = true
```

Operator has only one entry:

```ini
[Operator]
EnableUtilityChanges = true
```

Each toggle controls the group's gameplay changes and descriptions. When upgrading from the old survivor-wide toggles, disabled survivors become disabled category defaults; existing category choices are preserved.

### Shared changes

- **Artificer:** Utility controls Ion Surge tuning and Tectonic Blast; Special controls Snapfreeze and Flamethrower. The Ion Surge/Snapfreeze slot swap requires both Utility and Special enabled. With only one group enabled, those abilities retain their original slots. Re-select your loadout after changing the slot swap.
- **Freeze execution:** The shared boss freeze threshold stays active while Artificer's Secondary or Special changes, or Railgunner's Special changes, are enabled.
- **Acrid:** Spawn protection is included under Stats.
- **CHEF:** Utility controls Roll's terrain handling; Special controls using Yes CHEF during Roll.

## Multiplayer and compatibility

All players should use the same mod version and config settings. Changes may conflict with other mods that adjust the same skills or stats. DLC survivors and skills still require their normal game access and unlocks.

## Detailed survivor changes

The individual survivor reference pages are prepared for the mod's **Wiki** section. Once published there, use those pages for the full changes and the config groups that control them.

Version 1.1.0 adds the granular configuration options. Build and offline checks have passed; in-game testing of mixed configurations and multiplayer remains pending.
