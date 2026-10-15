# Taylor's Balance Patch — 0.4.2

A configurable Commando, Artificer and Bandit balance patch for Risk of Rain 2. No R2API modules or new game assets required. Needs BepInExPack and HookGenPatcher, including their dependencies.

## Changes

| Ability | Change |
| --- | --- |
| Double Tap | Back to vanilla. The 10,000% test and custom title are removed. |
| Phase Round | 320% initial damage; +80% of initial damage for each enemy pierced, using the game's existing additive ramp. |
| Phase Blast | 0.7 proc coefficient on each pellet. Still two blasts of four pellets, at 200% damage each. |
| Tactical Dive | +200 armor for 0.5 seconds, using the standard visible ArmorBoost buff. Tactical Slide is unchanged. |
| Suppressive Fire | 8 base shots, scaled by attack speed as normal, at 115% damage per bullet. |
| Frag Grenade | 50% lower outgoing speed after enemy collisions. Ground and wall bounces, damage, fuse and blast radius are unchanged. |
| Plasma Bolt | 310% damage. Direct and splash hits apply the standard 30% movement slow for 2 seconds. |
| Snapfreeze | Moved from Utility to Special, including release input and cancelled-placement stock refund. |
| Ion Surge | Moved from Special to Utility. Launch uses the original curve with a fixed base-speed factor of 7; movement-speed bonuses and sprint do not increase it. |
| Flamethrower | 30m range. Both hand effects extend forward by 50%, including their particle hierarchy. |
| Bandit Blast | 6 ammo charges with six matching reticle pips; a 0.2s minimum shot interval that scales with attack speed; 50% lower spread and bloom buildup. The pips center dynamically for Burst's four-stock display and Blast's six-stock display. |

Changed damage/range abilities have updated English descriptions. All skill names are unchanged. Descriptions appear in English regardless of the selected language. ArmorBoost is a standard nonstacking buff; an existing ArmorBoost does not stack a second +200 bonus. Artificer's other skill choices and the original unlock requirements remain intact. Re-select your loadout after enabling or disabling the slot swap; saved variant indices refer to the new occupants of those slots.

## Import into r2modman

1. Close Risk of Rain 2.
2. Use your existing mod profile. Keep only one installed copy of Taylor's Balance Patch.
3. Select **ModDev** in r2modman. Open **Settings**, search **Import local mod**, and select the ZIP file `Taylor-TaylorsBalancePatch-0.4.2.zip` from this project's `dist` folder.
4. If asked for an author, enter **Taylor**. Keep the name **TaylorsBalancePatch** and version **0.4.2**. Accept required dependencies if prompted. They are the same BepInExPack/HookGenPatcher packages already used by ModDev.
5. Verify it appears in **Installed**, enabled. Use **Start modded**.

Do not copy this build into the plugins folder manually: importing through the manager is what creates its managed entry. The ZIP contains a manifest, icon, README, changelog and plugin binaries; it contains no game DLLs and no config file that could overwrite your choices.

## Config editor

Start the game once with this version, reach the main menu, then quit. BepInEx creates or updates:

`BepInEx\config\com.taylor.ror2.balancepatch.cfg`

In r2modman, open **Config editor** (refresh if necessary), search for `taylor`, select that file and choose **Edit Config**.

Select the entry ending in **com.taylor.ror2.balancepatch.cfg**, not **mm_v2_manifest.json** or **manifest.json**. Those JSON files are package metadata managed by r2modman. The actual settings file uses BepInEx's standard format: a `[Commando]` section with `EnableCommandoChanges = true` or `false`. The verified 0.2.0 config already has the correct syntax; no replacement or conversion is needed. Re-importing an update preserves this setting.

Under **Commando**, the single setting is **EnableCommandoChanges**:

- **true** (default): enables all five changes and descriptions.
- **false**: leaves Commando unchanged by this mod. This does not restore the old Double Tap test.

Under **Artificer**, **EnableArtificerChanges** independently enables/disables all Artificer changes, including the skill slot swap. It defaults to **true**. Disabling one survivor's switch does not disable the other. The new setting appears after launching the updated version once.

Under **Bandit**, **EnableBanditChanges** independently enables/disables the Blast changes and description. It defaults to **true**. Burst remains unchanged.

Save, then **restart the game** after changing the setting. It is intentionally an all-or-nothing switch; no per-ability toggles. The config file remains available when this switch is false. r2modman's separate enable/disable control for the whole package also works after restarting.

## First tests

1. With the switch true, check that the primary is simply Double Tap again and deals normal damage. Check the five updated descriptions and unchanged ability names.
2. Phase Round should begin at 320% and grow by 80% of that initial damage for each prior enemy hit (320%, 576%, 832%, before other modifiers). Line up separate enemies to test it.
3. Phase Blast keeps eight pellets total. Its 0.7 proc coefficient affects eligible on-hit effects; it is not a flat 70% proc chance. A short firing test cannot reliably prove a proc chance change.
4. Tactical Dive should briefly show the standard armor icon for 0.5 seconds. Tactical Slide should not gain this buff.
5. Suppressive Fire should fire eight shots at base attack speed. At a character damage stat of 12, each bullet has 13.8 raw damage before armor, critical hits and other modifiers.
6. Compare grenades thrown directly into enemies with ground bounces. Enemy rebounds should be gentler; normal floor bounces should feel the same.
7. Quit, set **EnableCommandoChanges = false**, save and relaunch. Descriptions should return to vanilla, Suppressive Fire to six base shots, and Tactical Dive should no longer add this armor buff.

For Artificer, test Plasma Bolt's damage and slowing splash hits, then hold/release Snapfreeze using the Special button. Try cancelling placement without a valid surface and check that Special stock is refunded. Compare Ion Surge launch height before/after movement-speed items. Test Flamethrower against targets between 20m and 30m and check both flame visuals while aiming. Finally, disable only Artificer's switch, restart, and confirm the vanilla slots return while Commando remains patched.

For Bandit, verify Blast has six rifle shots and six centered reticle pips before reloading, and that the center reticle marker remains visible. Confirm the pips empty in order and return after reloading. Select Burst and verify its four pips are centered, then switch back to Blast and verify all six remain centered. Check the 0.2s minimum interval at base speed and cadence with attack-speed items, and compare spread and bloom to vanilla. Toggle only **EnableBanditChanges** and restart to confirm its stock, reticle, cadence, spread and description return to vanilla.

Successful startup logs each survivor's `changes ENABLED` or `changes DISABLED` in the profile's `BepInEx\LogOutput.log`; successful Artificer catalog setup also logs `Artificer skill slots swapped`. If something fails, provide this log and the behavior you observed.

Built and checked against the local installed game. Offline checks verify the method patches, asset configuration and package contents. In-game behavior and the config-editor interaction still need testing. Multiplayer is not yet tested: all participants should use the same mod version and config.

## Development

Open `src\TaylorsBalancePatch.csproj` in Visual Studio. `Directory.Build.props` points at the installed game and `ModDev` profile. Run `build.ps1` to build, run offline assembly validation and package the ZIP. Optional arguments: `-GameDirectory` and `-ModProfileDirectory`. The build does not import the package or launch the game.

`validation/inspect-assets.py` verifies the local Unity asset assumptions when run with UnityPy available. UnityPy is a development inspection tool only; players do not need it. Original game files are read only and are never bundled in the mod.

Future releases should increase both the project/plugin version and `manifest.json` version. Re-import the new ZIP through r2modman; avoid keeping multiple installed versions of this plugin. Other mods that change the same skills may conflict.
