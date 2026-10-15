# 0.5.1

- Disperse: increased the nearby push and Weaken radius from 10m to 20m. Jump height and push strength are unchanged.
- Harvest: reduced initial damage from 400% to 100%. Fruiting's stack-consumption bonus remains 150% pending a separate balance decision.
- Updated ability descriptions to match.

# 0.5.0

- REX Inject: final syringe proc coefficient 1.0; earlier syringes unchanged.
- Disperse: downward sonic boom, 10m nearby push, 4s Weaken, vertical launch from ground or air at 90% of vanilla airborne recoil strength.
- Harvest: 400% damage, 0.25s recovery, 4s Weaken; each application adds five shared Fruiting stacks and refreshes all stacks to 10s.
- Fruiting: allied direct skill hits have a flat 20% chance to consume a stack, spawn one vanilla healing fruit and deal 150% attacker-base-damage bonus. Excludes DOT/item procs; bonus cannot crit, trigger on-hit procs or recurse. Existing fruit cluster remains visible while marked. Removed old fruit-on-death drops.
- Added independent REX / EnableREXChanges config toggle. No additional mod dependencies or assets.

# 0.4.2

- Center Bandit's stock pips based on the active primary: four pips for Burst and six for Blast.

# 0.4.1

- Fixed Bandit Blast's reticle for six rounds: added stock pips five and six, kept the center marker visible, and spaced all six pips evenly.

# 0.4.0

- Bandit Blast: six rounds, a 0.2-second minimum interval that scales with attack speed, and 50% lower firing spread and bloom buildup.
- Updated the Blast description; Burst is unchanged.
- Added Bandit / EnableBanditChanges (default true, restart required), independent of other survivor switches.

# 0.3.0

- Plasma Bolt: 310% damage and the standard 30% movement slow for 2 seconds, including splash victims.
- Swapped Snapfreeze into Special and Ion Surge into Utility, preserving other options and unlock requirements. Updated Snapfreeze's release input, cancelled-placement stock refund and damage source, plus Ion Surge's damage source.
- Ion Surge uses a fixed launch curve with Artificer's base movement speed (7); movement-speed items and sprint no longer increase the launch.
- Flamethrower: 30m damage range, with both hand effects stretched forward by 50% and particle hierarchy scaling enabled.
- Added Artificer / EnableArtificerChanges (default true, restart required), independent of Commando's switch. Updated ability descriptions; names unchanged.
- Preserved Taylor's manual Frag Grenade adjustment: 50% lower outgoing speed on enemy collisions.

# 0.2.1

- Frag Grenade now loses 90% of its outgoing speed after enemy collisions (retains 10%), up from a 20% reduction.
- Updated the grenade description and startup log.
- Clarified which file to open in r2modman's Config editor; the existing BepInEx config format is correct.

# 0.2.0

- Removed the Double Tap test damage and custom title entirely.
- Phase Round: 320% initial damage; +80% additive damage per enemy pierced.
- Phase Blast: 0.7 proc coefficient per pellet; two four-pellet blasts unchanged.
- Tactical Dive: vanilla ArmorBoost (+200 armor and normal icon) for 0.5 seconds.
- Suppressive Fire: 8 base shots, still scaled by attack speed; 115% damage per bullet.
- Frag Grenade: outgoing velocity reduced by 20% after an enemy collision. Ground/wall bounces unchanged.
- Updated the five descriptions; kept all original skill names.
- Added Commando / EnableCommandoChanges (default true, restart required).
- Packaged for r2modman's Import local mod with a manifest and icon.

# 0.1.0

- Initial Double Tap test. Superseded by 0.2.0; do not install alongside it.

