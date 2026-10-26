# Railgunner

T4ylorSurvivors 1.1.0 survivor changes.

Config section: `[Railgunner]`. Available toggles: `EnablePassiveChanges`, `EnableSecondaryChanges`, `EnableUtilityChanges`, `EnableSpecialChanges`. Restart after editing; use matching settings across multiplayer peers.

## Passive


- Magnetic Accelerator: Highlighted Weak Points and matching hit-acceptance radius are 70% of vanilla linear size (49% squared-radius threshold). Physical enemy hurtboxes and critical-strike conversion are unchanged.

## Secondary


- M99 Sniper and HH44 Marksman: Stationary grounded shots no longer apply physical self-push, preserving Bustling Fungus while firing.
- M99 Sniper and HH44 Marksman: Shooter volume remains normal; other players' listeners hear 50% firing volume on clients running the mod. Unknown ownership and Wwise failures preserve native volume. Headshots/special sounds are unchanged.

## Utility


- Polar Field Device: Initial burst radius increased from 10 to 15 meters, matching the lasting field's existing 15-meter radius.
- Polar Field Device: Replaces vanilla's inward pull with 1500 outward force; does not hit the owner. Deployment damage and proc coefficient remain zero; native enemy slow and projectile slowdown remain unchanged.
- Polar Field Device: Post-stick deployment fuse reduced from 1s to 0.1s.
- Polar Field Device: Railgunner and allies jump 100% higher when jumping inside an active allied bubble; overlapping fields do not stack.
- Polar Field Device: Allied landings inside the active bubble take no fall damage. Fall speed, ordinary enemy damage and landings outside remain normal.

## Special


- Cryocharge: Piercing shot radius increased by 20%, from 2 to 2.4 meters.
- Cryocharge: Shared freeze executes ordinary enemies below 30% health, and Mithrix/False Son boss forms below 10%, with matching health-bar markers. These rules remain enabled when Artificer is disabled. Freeze duration, eligibility, shot damage and piercing remain native.

## Configuration notes


- The boss freeze rule is shared with Artificer. Disabling Railgunner Special alone does not remove it while Artificer Secondary or Special remains enabled.
