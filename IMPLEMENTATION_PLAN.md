# Taylor's Balance Patch — base survivor implementation plan

Document revision: 1 · Created: 2026-10-12 · Target project: Taylor's Balance Patch

This is a handoff specification for an AI coding agent. It records Taylor's requested changes for the 11 listed base-game survivors, including newer skills on those survivors. It is a plan, not a claim that the changes have been coded or tested. No new gameplay code was created while preparing this document.

## 1. Start here

Implement one survivor at a time and deliver an r2modman-importable package for Taylor to test before proceeding to another survivor. Preserve previously completed changes. Do not reinterpret unspecified numbers or mechanics as approved balance decisions.

Each survivor section separates:

- **Required behavior:** Taylor's requested outcome.
- **Implementation guidance:** a proposed technical approach; inspect the installed game before committing to hooks, fields or assets.
- **Decisions / user work:** unresolved design choices or concrete testing/assets needed from Taylor. Proposed answers are not approved values.
- **Acceptance checks:** evidence needed before calling that survivor complete.

The decision register in section 5 is the consolidated list of open design questions. A question blocks only its dependent behavior; continue independent work. Technical facts that can be resolved from the game should be investigated by the agent, not sent back to Taylor as homework.

### Existing project and environment

| Resource | Location / state |
| --- | --- |
| Project root | `D:\Modding\RoR2Mods\TaylorsBalancePatch` |
| Source | `src\Plugin.cs`, `src\CommandoBalance.cs`, `src\ArtificerBalance.cs`, `src\ArtificerChanges.cs` beneath the project root |
| Build and package | `build.ps1`; builds Release, runs offline assembly validation, writes a ZIP under `dist` |
| Existing validation | `validation\Program.cs`, `validation\inspect-assets.py` |
| Game | `D:\SteamLibrary\steamapps\common\Risk of Rain 2` |
| Game assembly | `Risk of Rain 2_Data\Managed\RoR2.dll` beneath the game directory |
| Mod profile | `C:\Users\Taylor\AppData\Roaming\r2modmanPlus-local\RiskOfRain2\profiles\ModDev` |
| Config | `BepInEx\config\com.taylor.ror2.balancepatch.cfg` beneath that profile |
| Runtime prerequisites | BepInExPack and HookGenPatcher, with their package dependencies; already installed |
| Current source/package version | 0.5.5 |

Paths above describe the current machine. Recheck `Directory.Build.props`, package manifest and installed profile when picking up this work; do not assume these remain current on another machine.

### Implementation status

| Survivor | Status at handoff |
| --- | --- |
| Commando | Implemented. Taylor reported the earlier changes working. Taylor subsequently edited enemy grenade bounce velocity to 50%; current source and 0.3.0 preserve that value. Keep regression coverage. |
| Artificer | Implemented and packaged in 0.3.0; build and offline checks passed. No explicit in-game acceptance of 0.3.0 has been recorded. Treat visual and gameplay acceptance as pending. |
| Bandit | Implemented in 0.4.0; reticle fixes in 0.4.1–0.4.2. The pips now center from the active primary's stock capacity (four for Burst, six for Blast). Taylor selected a 0.2s minimum interval that scales with attack speed. Build/offline checks and in-game acceptance of 0.4.2 pending. |
| REX | Implemented in 0.5.0 with decisions R1–R5 resolved below. Build, focused offline tests and installed-asset checks passed. In-game acceptance pending. |
| Acrid, Captain, Engineer, Huntress, Loader, MUL-T, Mercenary | Planned only. |

### Shared implementation rules

1. Keep the existing plugin identity `com.taylor.ror2.balancepatch`. Extend the same mod instead of shipping conflicting copies.
2. Follow the existing independent, all-or-nothing survivor toggles. Proposed new settings: `EnableAcridChanges`, `EnableBanditChanges`, `EnableCaptainChanges`, `EnableEngineerChanges`, `EnableHuntressChanges`, `EnableLoaderChanges`, `EnableMULTChanges`, `EnableMercenaryChanges`, `EnableREXChanges`. Each belongs in its survivor's section, defaults to true, and requires a restart. Existing keys remain unchanged. This extends the established config pattern; it does not introduce requested per-ability sliders.
3. Disabling a survivor restores that survivor's vanilla behavior and text as far as this mod is concerned. Keep other survivor toggles independent. Shared handlers must check which feature is enabled.
4. Update ability descriptions and affected keyword explanations; preserve names unless Taylor later requests otherwise. Do not leave text promising a change whose hook failed. Include charge count, damage, duration and slot changes where relevant.
5. Preserve unspecified damage, cooldowns, proc coefficients, unlocks, level growth, animations and variants. Exceptions require an explicit decision below. Old values in Taylor's notes describe intent and are not a substitute for inspecting the installed build.
6. Apply gameplay state on the appropriate authority. Roll random effects once on the server; synchronize buffs, stock, fruit pickups and indicators. Client effects must not duplicate damage, healing or drops.
7. Validate hook assumptions. Avoid silent partial survivor patches; log failures and undo that survivor's hooks and prefab edits where possible. Register any custom content before catalogs finalize. Static content registration may remain while disabled, but it must have no active gameplay effect.
8. Scope shared-state hooks by survivor, skill, projectile or explicit attack identity. Do not modify another survivor's use of a shared buff, damage type or attack class by accident.
9. Preserve original values for cleanup. Do not repeatedly multiply shared prefab values each run or scene load. Use the project's patch-editing workflow so source edits are visible in the chat's edited-files summary.
10. Package updates with matching plugin, project and manifest versions. Update README/changelog. Do not bundle game DLLs or overwrite the user's config. Import through r2modman; do not introduce a second manually copied plugin.

## 2. Dependencies and extra work

**No additional download is confirmed necessary for every planned change.** Existing hooks can implement many of these changes. Custom content and multiplayer requirements may justify small R2API modules. Select the implementation first, then name exact required packages and tested versions in the release instructions. Do not install the entire R2API collection or an unrelated balance mod preemptively.

R2API is modular; dependency choices should correspond to modules actually used. See the [official R2API repository](https://github.com/risk-of-thunder/R2API).

| Candidate dependency | When it may be useful | Current classification |
| --- | --- | --- |
| R2API.ContentManagement | Registering Mercenary's custom readiness buff and any new Fruiting content/effect definitions. It supplies content-pack and content-addition support. | Recommended candidate for new content, not mandatory if native content registration is implemented correctly. [Documentation](https://github.com/risk-of-thunder/R2API/blob/master/R2API.ContentManagement/README.md) |
| R2API.DamageType | Marking a Mercenary attack so all its hits expose, or marking REX bonus damage so it cannot trigger itself. It supports custom damage tags on attack types. | Conditional implementation aid; explicit per-attack state is an alternative. [Documentation](https://github.com/risk-of-thunder/R2API/blob/master/R2API.DamageType/README.md) |
| R2API.Prefab | Cloning/registering a new networked fruit pickup or another networked prefab. | Conditional. Reuse the existing game pickup if suitable; a client-only indicator may not need a new networked prefab. [Documentation](https://github.com/risk-of-thunder/R2API/blob/master/R2API.Prefab/README.md) |
| R2API.RecalculateStats | Applying scoped stat or conditional regeneration changes through shared stat hooks. | Optional; fixed body-stat edits and the existing vanilla regeneration buff may suffice. [Documentation](https://github.com/risk-of-thunder/R2API/blob/master/R2API.RecalculateStats/README.md) |

Other modules may become justified during implementation. Do not call one “required” until the code actually depends on it. Adding a BepInEx hard dependency can make that module necessary to load the entire plugin, even when a survivor toggle is off; document this honestly and include transitive dependencies through r2modman.

### What Taylor may need to do

- **Every survivor:** import the new ZIP, launch modded, test the changes and restart when toggling config. The agent supplies a short survivor-specific checklist and log location.
- **New dependencies:** install only the exact packages named in that release, including r2modman's required dependencies. No new dependencies are requested by this planning document.
- **Movement/feel:** approve Loader's knockback/pull targets and REX's jump/sonic-boom feel after the agent proposes concrete values.
- **Design choices:** resolve the numbered decisions in section 5 when the relevant survivor is being developed.
- **Mercenary and REX visuals:** review an agent-created/reused readiness icon or Fruiting effect. Custom art is optional; no asset file or Unity installation is currently required from Taylor. If bespoke assets become necessary, the agent must first explain the missing asset and provide a usable placeholder.
- **Multiplayer:** another player/session is needed for final host/client acceptance. Until tested, label multiplayer unverified.
- **Skill availability:** the installed localization includes Cloying Breath in `DLC4.json`. The agent must inspect its actual skill/expansion requirements. If Taylor cannot select it, determine the required installed content/unlock; do not buy content, bypass unlocks or remove expansion checks automatically.

## 3. Survivor specifications

### Acrid

**Status:** not implemented. **Complexity:** high for Blight timing and Breath hit rules; low to medium for the other edits.

#### Required behavior

| Target | Requested change |
| --- | --- |
| Blight passive | Stack duration on an enemy follows `3 seconds + (0.5 seconds × number of existing stacks)`. Taylor describes every stack's duration as based on current stacks on that enemy. Timer semantics require A1. |
| Vicious Wounds | Third hit becomes **Poisonous**. Preserve its existing damage and Regenerative effect. |
| Neurotoxin | Base cooldown 2s → 3s. |
| Frenzied Leap | Base charges 2 → 1; base cooldown 10s → 6s; becomes **Poisonous**. Preserve unrelated leap behavior, including any existing hit-based recharge behavior. |
| Cloying Breath | Applies poison every hit; only a 40% chance to apply Blight each hit. How this interacts with the selected passive requires A2. |

#### Implementation guidance

- Inspect the actual DOT stack collection and duration/refresh path. A visible debuff count is not necessarily the authoritative count of live DOT stacks. Count only Blight, not poison or unrelated DOTs.
- Under an application-time interpretation, count existing stacks before adding the new stack: zero existing → 3.0s; one → 3.5s; four → 5.0s. Do not change Blight damage/tick interval while changing duration. Do not silently impose a stack/duration cap; report performance concerns if needed.
- For Vicious Wounds and Frenzied Leap, use the game's **Poisonous** passive-aware routing unless Taylor decides otherwise. Check both Poison and Blight loadouts. Breath's explicit poison/Blight wording may need a separate path.
- Inspect `EntityStates.Croco.Slash`, `ChainableLeap`, `FireSpit`, `Breath`, the associated SkillDefs and DOT processing. These are navigation leads verified as existing types, not guaranteed hook signatures.
- Prevent applying Blight twice via the original poison routing plus the new chance roll. A hit roll must correspond to one accepted damage event on one victim, not each hurtbox or both client/server callbacks.

**Dependencies / user work:** no required extra module established. Custom damage tags may help isolate Breath. Taylor must settle A1–A3 and test both passive loadouts and a sustained multi-target cloud. Verify Cloying Breath availability first.

**Acceptance checks:** durations match the chosen formula at 0/1/4 existing stacks; expiration and refresh behavior match A1; two Acrids behave according to the chosen ownership rule. Only the third primary hit adds Poisonous. Frenzied Leap has one base charge and a 6s recharge, with extra-stock items still working. Neurotoxin uses 3s. Breath's poison and Blight rolls match A2–A3 with no doubled DOT application; use controlled counts to assess the 40% rule.

### Artificer

**Status:** existing 0.3.0 implementation; gameplay acceptance pending. **Task:** verify and repair if needed, do not add a second implementation.

| Target | Required behavior |
| --- | --- |
| Plasma Bolt | 280% → 310% damage; slows enemy movement by 30% for 2s. Existing implementation applies the slow to direct and splash victims. |
| Snapfreeze | Move Utility → Special. |
| Ion Surge | Move Special → Utility; launch velocity must not scale with movement speed. |
| Flamethrower | Damage range 20m → 30m; visuals extend to match. This visual requirement carries forward from Taylor's earlier request. |

**Existing implementation to preserve:** `ArtificerBalance.cs` and `ArtificerChanges.cs`; `JunkContent.Buffs.Slow30`; whole skill-variant swap preserving other options and unlocks; Snapfreeze release input and cancelled-placement stock refund redirected to Special; damage-source tags updated to the new slots. Ion Surge's launch uses the existing curve with a fixed 7 m/s factor, including while sprinting. Flamethrower uses a 30m range and forward scaling of both hand effects with particle hierarchy scaling.

**Important:** the existing effect scaling is an implementation attempt, not proof that the visible flame endpoint matches the damage boundary in motion. Verify meshes, particles, aiming and available skins in-game. Existing skill-selection indices may now refer to a different skill; Taylor should reselect the loadout after toggling the swap.

**Dependencies / user work:** no additional modules in 0.3.0. Taylor tests movement, new buttons, cancelled wall placement and visible flame reach. No new asset is currently required.

**Acceptance checks:** Plasma hits do 3.1× attacker damage before normal modifiers and slow for 2s; misses do not slow. Other primary variants remain unchanged. Snapfreeze responds to Special press/release and refunds Special stock on cancellation. Ion Surge uses Utility and reaches the same launch height with and without movement bonuses under comparable conditions. Both flame effects and damage reach approximately the intended 30m boundary. Disabling Artificer restores its vanilla slots and values while Commando stays enabled.

### Bandit

**Status:** not implemented. **Complexity:** low to medium.

| Target | Required behavior |
| --- | --- |
| Blast | Bullets 4 → 6; spread/bloom reduced by 50%; 0.2s between shots. |

**Verified interpretation:** installed localization identifies **Blast** as the alternate rifle primary, with four ammo charges; **Burst** is the shotgun primary with five pellets. Therefore change Blast's magazine/stock to six, not six projectiles per trigger pull. Leave Burst alone.

**Implementation:** the rifle SkillDef token `BANDIT2_PRIMARY_ALT_NAME` has base stock 4 and no recharge; 0.4.0 changes only this SkillDef's base stock to 6. Blast uses `Bandit2FireRifle`, which inherits `Bandit2FirePrimaryBase`. Its base minimum duration is 0.1s divided by attack speed. The patch sets its base minimum duration to 0.2s before original state initialization, so the minimum interval scales with attack speed. On that same state, spread pitch/yaw scales and `spreadBloomValue` are multiplied by 0.5. The shared primary base hook is scoped to the exact rifle state. Burst shares the base, but remains unmodified.

**Reticle fixes (0.4.1–0.4.2):** the installed `Bandit2Crosshair` prefab's controller has only four stock-pip displays (1–4), each capped at stock 4. Its center-available marker is also capped at 4, while the empty center marker applies at 0 stock. The `CrosshairController.Awake` hook extends only Bandit2Crosshair instances: it removes the upper-stock cap from existing pips/center marker, clones two existing pip images for counts 5 and 6, and adds corresponding display entries. A `LateUpdate` hook centers the live pip row according to the active primary's capacity: four pips for Burst and six for Blast. The center-available marker therefore persists for positive stock, and the empty marker remains for 0. This operates on the live UI instance; it does not alter the original asset bundle.

**Preserved:** one rifle projectile per shot, existing 330% damage, proc coefficient, reload rules, Burst's five pellets and all other abilities. The description now states the six-round magazine, 0.2s base-speed interval and 50% spread/bloom reduction.

**Decision resolved:** B1 — Taylor chose the 0.2s interval to scale with attack speed. No user install work or extra assets are needed.

**Acceptance checks:** six normal shots before depletion at base stock; six visible pips empty in order; center marker stays visible while stock is positive and switches at zero; pips refill after reload. One rifle bullet per shot; firing cone/bloom is half the original angular values at comparable firing history; minimum cadence scales with attack speed; Burst is unchanged. Taylor still needs to test these in game.

### Captain

**Status:** not implemented. **Complexity:** medium to high.

| Target | Required behavior |
| --- | --- |
| Utility | Usable in hidden realms. |
| Special | Usable in hidden realms. |
| Healing Beacon | While a recipient is at full health, supplies barrier up to 20% of that recipient's maximum health. Exact grant semantics require C1. |

**Implementation guidance:** remove the Captain-specific hidden-realm skill restriction while preserving targeting validity, charge limits and real geometry. Treat this as applying to available variants in those slots unless Taylor narrows it. Do not globally change a scene's classification merely to enable these skills: that can change unrelated game systems. Confirm the calls actually spawn and function rather than only enabling the HUD icon.

Inspect the Captain setup/call states, SkillDefs and scene restriction path. For Healing Beacon, scope barrier handling to this beacon's recipients/ticks rather than changing all healing. Preserve normal healing below full health. Never subtract pre-existing barrier when imposing the beacon's contribution limit. Retain normal barrier decay unless Taylor asks otherwise.

**Dependencies / user work:** no additional module established. C1–C2 define barrier accumulation and the full-health condition. Taylor tests actual utility strikes and at least one beacon in available hidden realms, including an enclosed area. No new beacon artwork is required.

**Acceptance checks:** skills can be selected, activated and successfully delivered in representative restricted scenes; normal scenes and per-stage beacon stock remain correct. Injured recipients heal normally. Full-health recipients receive barrier according to C1; repeated ticks never forcibly reduce barrier from another source. Test shields, curse-reduced health and two overlapping Healing Beacons using C2's rules.

### Commando

**Status:** implemented; retain and regression-test.

| Target | Required behavior |
| --- | --- |
| Phase Round | Initial damage 300% → 320%; damage bonus per pierced enemy 40% → 80%. |
| Phase Blast | Proc coefficient 0.5 → 0.7 on each of eight projectiles; preserve two four-pellet blasts. |
| Tactical Dive | +200 armor for 0.5s with the standard visible armor buff. |
| Suppressive Fire | Six → eight base shots; 100% → 115% damage per bullet. Preserve attack-speed shot scaling. |
| Frag Grenade | After hitting an enemy, reduce outgoing velocity by 50%. |

**Existing implementation:** `Plugin.cs` and `CommandoBalance.cs`. Phase Round keeps the game's additive ramp: 320%, 576%, 832% for the first three enemies before other modifiers. It is not an exponential ×1.8 multiplier per hit. Tactical Dive uses vanilla ArmorBoost and excludes Tactical Slide. Grenade enemy-collision velocity is multiplied by 0.5, with floor/wall bounces unchanged. Double Tap is vanilla; never restore the original 10,000% test or custom name.

**Dependencies / user work:** none beyond existing prerequisites. Preserve Taylor's manual 50% edit. Do not use old 0.2.1 changelog entries describing 90% as the current requirement.

**Acceptance checks:** existing checks still pass; brief standard armor icon appears; eight base barrage shots at normal attack speed; Phase Blast still has eight total pellets; enemy grenade rebounds retain half their outgoing speed. Turning off Commando restores its vanilla values without disabling Artificer or future survivors.

### Engineer

**Status:** not implemented. **Complexity:** low to medium.

| Target | Required behavior |
| --- | --- |
| Survivor damage | Base 14 (+2.8 per level) → 12 (+2.4 per level). |
| Bouncing Grenades | Base charge speed increased by 25%. |
| Pressure Mines | 900% → 1000% damage. |
| TR58 Carbonizer Turrets | Base health 130 → 140; base health regeneration 1.5 → 2. |

**Implementation guidance:** change Engineer's base and level damage fields. At level L, unmodified damage becomes `12 + 2.4 × (L − 1)`. Faster charging means multiplying the charge rate by 1.25, equivalently multiplying the base full-charge time by 0.8; it does not mean subtracting 25% from the time. Preserve attack-speed scaling, maximum grenade count and grenade damage.

Inspect Pressure Mines' arming stages: 1000% targets the listed fully armed damage. Preserve partial-arming damage unless Taylor expands the request; do not scale all stages by accident. Change the mobile TR58 body, not the stationary turret body. Preserve turret level-growth fields because only base values were requested. Inspect whether Engineer's changed damage influences turret spawning; do not assume turret base damage is inherited directly from Engineer's body stat.

**Dependencies / user work:** no additional module expected. Taylor can compare charge feel and mobile turret survivability; no custom assets needed.

**Acceptance checks:** level 1 damage 12 and level 2 damage 14.4 without modifiers; full-charge time is 80% of baseline at the same attack speed; fully armed Pressure Mine coefficient is 10.0; partial stages are unchanged; TR58 has the requested base health/regen and unchanged growth; stationary turrets remain unchanged.

### Huntress

**Status:** not implemented. **Complexity:** medium to high for the vertical hit area.

| Target | Required behavior |
| --- | --- |
| Blink | Cooldown 7s → 6s. |
| Arrow Rain | Proc coefficient 0.2 → 0.5; hits all enemies above its horizontal radius; increase current damage by 5%. |

**Damage baseline warning:** the installed English description says 225% damage per second, but the inspected `EntityStates.Huntress.ArrowRain` asset contains a `damageCoefficient` value of 2.2. Neither alone establishes actual DPS. Follow projectile damage, tick interval and any secondary multipliers before computing the result. Multiply verified actual damage by **1.05**, not add five percentage points. If actual DPS is 225%, the result is 236.25%; that is an example, not a verified implementation constant.

**Implementation guidance:** keep normal Blink separate from Phase Blink. For Arrow Rain, preserve the horizontal radius and duration. Change damage, proc coefficient and hit volume independently; enlarging a sphere would also hit enemies outside the requested footprint. A vertical cylinder test can use horizontal distance from the cast center plus height above the area. Dedupe hurtboxes to one damage event per target per original tick. Keep the original tick cadence.

**Dependencies / user work:** no additional module established. H1 determines upper reach and obstruction rules. Taylor tests flying targets and stacked terrain. A new visual may be needed if the enlarged vertical reach is otherwise misleading; first adapt the existing effect rather than requesting new art.

**Acceptance checks:** normal Blink recharges in 6s at base modifiers; Phase Blink stays unchanged. Arrow Rain's measured damage is exactly baseline ×1.05 over equal duration/ticks; each eligible hit has 0.5 proc coefficient. Flying enemies inside the horizontal footprint are hit according to H1; enemies just outside it are not. Multi-hurtbox enemies receive one event per intended tick.

### Loader

**Status:** not implemented. **Complexity:** medium; requires feel tuning.

| Target | Required behavior |
| --- | --- |
| Stats | Base maximum health 160 → 130; armor 20 → 10. |
| Knuckleboom | Slightly knocks back lighter enemies. |
| Spiked Fist | Can pull somewhat larger enemies, but not large enemies such as bosses. |

**Implementation guidance:** leave health growth unchanged unless Taylor explicitly requests a new value. Inspect Knuckleboom's existing hit force and add/tune only the missing outward component; do not change damage or grant hard crowd control. Inspect Spiked Fist's target classification and mass threshold, including both Rigidbody and character-motor bodies. Do not implement the exclusion as mass alone if that would pull bosses. Preserve the original behavior for ineligible heavy targets and surfaces.

**Dependencies / user work:** no additional module expected. L1 needs a small/light, newly eligible medium, and excluded boss target set plus a proposed force/threshold. The agent should inspect actual masses, propose values and supply a test build; Taylor judges the feel rather than needing to research engine units.

**Acceptance checks:** base health 130 and armor 10; level growth unchanged. Light targets move a modest distance without being launched excessively, walls/terrain remain stable, and the agreed larger targets can be pulled. Bosses remain ineligible even if their physical mass is unusually low. Grappling Fist and other attacks retain their original behavior.

### MUL-T

**Status:** not implemented. **Complexity:** medium.

| Target | Required behavior |
| --- | --- |
| Scrap Launcher | Remove sweet-spot damage falloff. |
| Power-Saw | Becomes **Regenerative**. If regeneration can stack, only give each hit a chance to grant it. |

**Implementation guidance:** identify Scrap Launcher's explosion falloff path and disable distance-based damage reduction within the existing explosion radius. Keep its radius, center damage, direct-hit handling, stock and recharge unchanged. Do not add an extra explosion to reproduce full damage.

**Regeneration fact found during planning:** `bdCrocoRegen` in the installed Croco asset bundle has `canStack = true`. `CharacterBody.RecalculateStats` uses its stack count multiplied by maximum health and 0.1 as a per-second regeneration contribution. Existing Acrid attacks apply the timed buff for 0.5s. This supports the user's conditional **chance-per-hit** branch; do not apply a guaranteed stack on every saw hit. The keyword description states 5% maximum health over 0.5s, before other modifiers.

Use the existing timed regeneration behavior rather than immediate healing if retaining the Regenerative keyword. Scope it to accepted Power-Saw hits, not all MUL-T damage. The actual probability and multi-target handling still require M1.

**Dependencies / user work:** no new module expected when reusing the vanilla buff. Taylor chooses the healing chance after the agent measures saw hit cadence and estimates healing per second; no art is required for the standard buff.

**Acceptance checks:** equal explosion damage at center/middle/edge within the existing radius before other modifiers; no damage outside it. Saw misses never heal. Chance rolls occur at the chosen event granularity and do not duplicate across peers. Test one target, several targets, attack-speed bonuses and dual-saw loadouts so healing remains understandable. Scrap Launcher and the saw can be selected in either weapon slot.

### Mercenary

**Status:** not implemented. **Complexity:** high; attack identity and multi-hit rules matter.

| Target | Required behavior |
| --- | --- |
| Primary / Expose | After two swings, gain an **Expose Ready** buff lasting 0.5s. The next attack while active applies Expose to every enemy it hits and removes the readiness buff. |

**Implementation guidance:** remove the primary's old automatic third-swing Expose application where it conflicts with the new system; preserve its damage/combo structure. Do not automatically remove Expose from other skills that already have it. Create an identifiable timed readiness buff with a visible icon. Define the trigger using the actual combo/animation events, not every FixedUpdate spent swinging.

Consume readiness once per qualifying **attack**, then keep an attack-level marker long enough to tag all its eligible hits. Removing the buff on the first damage callback and checking only the buff on later callbacks would fail Taylor's all-enemies requirement. Conversely, leaving the buff on the body for the full attack could expose unrelated item procs. Delayed projectiles and multi-tick skills require an explicit policy in ME1–ME2. Preserve normal Expose payoff/cooldown-reduction behavior and ensure the tagging hit does not immediately consume its own newly applied Expose due to event ordering.

**Dependencies / user work:** ContentManagement is a candidate for the readiness BuffDef; DamageType is a candidate for attack tagging. Neither is declared mandatory yet. Taylor resolves ME1–ME2 and reviews the buff icon/0.5s readability. Reusing an existing icon or generating a simple icon avoids needing Taylor-provided art.

**Acceptance checks:** two swings grant one 0.5s window under the chosen combo rule; readiness expires cleanly. The qualifying next attack consumes it once and exposes every intended target. A missed attack, delayed projectile and multi-hit skill behave according to ME1. Item procs and DOT ticks cannot unexpectedly steal readiness. Other Mercenary Expose sources and payoff mechanics remain intact. Readiness is visible to the player and consistent on host/client.

### REX

**Status:** implemented in 0.5.0 and adjusted through 0.5.5; additive momentum and stronger knockback await testing, remaining in-game acceptance pending. **Complexity:** high, particularly the Fruiting redesign.

| Target | Required behavior |
| --- | --- |
| DIRECTIVE: Inject | Final hit proc coefficient 0.5 → 1.0. Earlier hits remain unchanged. |
| DIRECTIVE: Disperse | Sonic boom fires directly underneath REX instead of being aimed. Pushes and Weakens enemies within 13m (20 m/s outward plus 5 m/s lift before mass resistance). Adds an upward impulse to REX's current momentum at 90% of vanilla airborne recoil strength. |
| DIRECTIVE: Harvest | Endlag 1s → 0.25s; damage 330% → 100%; applies Weaken and five Fruiting stacks. |
| Fruiting redesign | Attacks against a Fruiting enemy have a 20% chance to drop a healing fruit, consume one Fruiting stack and deal 400% base damage to that enemy. Add a visible world-space indication beyond the debuff icon/count. |

#### Implementation guidance

- Inject: identify the actual final projectile/hit in the sequence; alter its proc coefficient only. Preserve damage, existing debuffs and healing.
- Disperse: derive direction from world up/down, not camera aim. Separate REX's upward impulse from the enemies' outward knockback. Preserve REX's existing velocity and add only an upward impulse; a fast fall may remain downward. Aim rotation must not introduce lateral impulse. Inspect whether the current force is velocity-based or mass-dependent before choosing a percentage reduction. Preserve the skill's cooldown and unchanged properties; do not alter Bramble Volley accidentally.
- Harvest: isolate recovery/endlag after the shot from startup, aiming and total cast time. Do not set the whole state duration to 0.25s unless the game proves that field is recovery-only. Set direct damage coefficient to 1.0 and add the agreed Weaken duration and five stacks.
- Fruiting: inspect the existing fruit-on-death behavior. This is a redesign, so decide whether it is replaced before retaining it; do not accidentally ship both old death drops and new hit drops.
- On a successful eligible hit, consume exactly one stack atomically on the server, spawn exactly one healing fruit, then apply the specified bonus damage with explicit source/owner attribution. At zero stacks, stop triggering. Rejected hits must not consume a stack.
- Mark bonus damage or otherwise guard it so it cannot recursively trigger Fruiting. Specify its proc coefficient rather than allowing an accidental default. Avoid repeated triggers from multiple hurtboxes or host/client duplication.
- Prefer reusing the game's existing healing fruit pickup, subject to R3. Its collection, healing, lifetime, ownership and networking still need verification.
- Proposed visual: reuse a fruit/plant effect attached above the enemy while Fruiting is active, scaled to remain visible on different body sizes. It may be rendered client-side from the synchronized debuff count; the icon alone is insufficient. Optional count-responsive fruit clusters can communicate the remaining stacks. This is a proposed visual, not an approved art direction.
- Remove the indicator when stacks reach zero, the debuff is cleansed, the enemy dies/despawns or the scene changes. Avoid allocating new effects every frame and avoid covering the target with effects when many enemies are marked.

**Dependencies / user work:** REX may need ContentManagement for custom effect/content registration, DamageType for nonrecursive bonus-hit tagging, and Prefab only if introducing a new networked pickup/prefab. Determine these after inspecting the existing fruit system. Taylor resolves R1–R5 and reviews launch feel and the visible Fruiting marker. No custom model is currently required from Taylor.

**Acceptance checks:** Inject's last hit alone has proc 1.0. Looking in different directions adds the same upward Disperse impulse while preserving existing horizontal and vertical momentum; enemies within 13m receive the stronger push and Weaken. Test standing, rising, fast falling and sideways travel. Harvest does 1.0× damage and its recovery is 0.25s without truncating the firing animation/event. It applies five stacks and Weaken. Controlled eligible hits match the chosen 20% rule; every success removes one stack, spawns one fruit and deals one 4.0× bonus hit attributed correctly. Failures consume nothing. Bonus hits cannot retrigger the system. Test reapplication, multiple attackers, simultaneous hits, death, zero stacks, cleansing, pickup collection and the visual indicator on host/client.

#### Implemented behavior and verified hook targets (0.5.5)

- Disperse emits a pale-green range ring on each observing client from the replicated skill state's OnEnter (no extra network effect transmission). `RexDisperseVisual` reuses `matOmniRing1Generic` from the original effect without modifying the material or prefab. One horizontal mesh particle expands to the gameplay radius, holds at the boundary, then fades over 0.65s. Mesh and effect are destroyed after playback. The ring stays at the cast point plus a 0.15m visual ground offset; it depicts the sphere's horizontal extent, not a terrain-conforming boundary. Gameplay remains immediate. Bramble Volley and dedicated servers do not spawn the ring. Test color, clipping on slopes, low VFX settings, rapid casts and visibility from another player's client.

- `RexChanges.cs` is gated by the independent `[REX] EnableREXChanges` switch, default true and restart required. No R2API additions. The KinematicCharacterController build reference is an existing game DLL and is not bundled.
- `ProjectileCatalog.SetProjectilePrefabs` sets only `SyringeProjectileHealing`'s controller proc coefficient from 0.5 to 1.0. `SyringeProjectile` stays at 0.5. It also removes `FruitOnHit` from `TreebotFruitSeedProjectile` while retaining its Special damage source; Harvest application is handled explicitly on the server.
- `TreebotFireFruitSeed.OnEnter` fires its projectile immediately and then waits `baseDuration / attackSpeedStat`; the source asset is 1 second and 3.3 damage coefficient. The patch sets these instance fields to 0.25 and 1.0 before initialization. The separate preparation state is preserved (its installed base duration is 0).
- `FireSonicBoom.OnEnter` uses exact-type checks to preserve Bramble Volley. Its original initialization, sound, animation and effect remain; its aim ray points down from REX's feet. Original attack/recoil blocks are bypassed for Disperse only, replaced by a server-side 13m sphere search and an owner-authoritative vertical launch. Enemies get 4s Weaken, a 20 m/s outward plus 5 m/s upward push scaled by the existing mass-suitability curve, and the original brief hurt-state interruption. REX's launch uses 90% of the vanilla air-recoil calculation (air distance parameter 8), adds to existing velocity without resetting it and works on ground or in air.
- `BuffCatalog.SetBuffDefs` makes the existing visible `bdFruiting` stackable. Accepted Harvest hits add five to the remaining shared pool and refresh every stack to 10s. Reapplication is additive with no artificial cap. A Harvest hit can trigger an existing stack before adding its five new stacks. Its Weaken lasts 4s.
- `onServerDamageDealt` processes allied direct skill hits with a flat server-side 20% roll, ignoring luck/proc-coefficient scaling. Rejects DOTs, item-proc chains, delayed second halves, rejected hits and zero damage. Skill source tags identify direct hits; gunner-drone `FireGatling.OnEnter` has a scoped fallback for its otherwise untagged bullets. Third-party untagged skills may require compatibility work.
- Each successful roll removes one timed buff immediately, spawns one original networked `TreebotFruitPack` for the attacker's team, and queues a noncritical bonus of 4.0× the triggering attacker's current damage stat with zero proc coefficient. The queue drains after the outermost `HealthComponent.TakeDamageProcess` returns because the game's damage report precedes death handling. This prevents duplicate death processing from a nested lethal bonus. Bonus identities and source/proc filtering prevent recursion. Already-lethal hits can drop their one on-hit fruit but cannot damage the dead target again.
- `GlobalEventManager.OnCharacterDeath` no longer treats the Fruiting buff as a reason for death drops, and Harvest no longer carries the alternate FruitOnHit death-drop flag. Other death rewards remain intact. Spawned stack-consumption fruits heal 15% max health (server-side HealthPickup instance override, flat healing zero); the original 25% prefab is untouched. Pickup lifetime is 20s. The game's synchronized buff drives its existing `FruitingEffect` fruit cluster and native expiry/cleanup.
- Original projectile values and buff stackability are saved for cleanup; setup failures remove REX hooks and restore these values. All peers should use the same version and config. Gameplay, visual readability and host/client correctness remain manual acceptance gates.

## 4. Suggested implementation order and release gates

The order is a recommendation; Taylor may choose another survivor. Do not implement all survivors at once simply because they appear in this plan.

| Phase | Work | Why |
| --- | --- | --- |
| 0 | Confirm current Artificer gameplay and preserve Commando regression baseline | Establish that the existing package works before extending it. |
| 1 | Bandit and Engineer, separately | Mostly stock/stat/coefficient edits; establish the survivor-module pattern. |
| 2 | Captain, Huntress and Loader, separately | Scene restrictions, hit-volume changes and movement/physics testing. |
| 3 | MUL-T and Acrid, separately | Resolve healing probability and DOT timing before adding more stateful effects. |
| 4 | Mercenary | Add readiness content and attack-level tagging with focused tests. |
| 5 | REX | Combine movement, recovery timing, stack ownership, random effects, pickups and visuals. |

For each survivor:

1. Re-read its required behavior and open decisions. Inspect the current source, installed skill definitions, assets and relevant methods. Record actual baselines and exact hook targets.
2. Resolve design choices that affect that survivor. Present concrete proposed values/examples for qualitative requests. Technical inspection should precede requests for user input.
3. Implement behind its independent config switch. Reuse shared helpers only where behavior truly matches; keep rollback and clear startup logging.
4. Run a build and focused offline checks: patch sites, serialized baselines, numerical formulas, stack/ownership logic where testable, and package metadata. Add tests for consequential logic rather than tests that merely repeat constants.
5. Deliver a versioned ZIP and a short in-game checklist. State new dependencies precisely, describe any loadout reselection needed and preserve config choices.
6. Record Taylor's test results. A successful compile or assembly inspection is not in-game acceptance. Fix failures before declaring the survivor complete.

### Shared in-game regression gates

- Config true/false and mixed-survivor settings behave independently after restart; no stale descriptions promise disabled changes.
- Check base values without items, then attack speed, movement speed, cooldown reduction and extra-stock effects where relevant.
- Check alternative skills and unchanged survivors for spillover from shared hooks.
- Check server/client authority for every buff, random roll, pickup, stock refund and movement effect. Report multiplayer untested until it is tested.
- Verify no log exceptions during character select, entering a run, using abilities, dying and changing stages.
- Verify visuals correspond to changed hit areas and debuffs. Use a stable test scene or measured distances rather than judging all range changes by camera perspective.

## 5. Decision register

These are not requests for Taylor to answer everything now. Resolve each entry when implementing the dependent feature, and update this table with the decision and date. Unanswered proposals must not silently become shipped behavior.

| ID | Decision needed | Proposed interpretation / agent preparation |
| --- | --- | --- |
| A1 | Does Blight duration snapshot on application, refresh all stacks on new application, or change continuously with current stack count? Are “existing stacks” all Blight on the victim or only this Acrid's? | Suggested starting interpretation: snapshot each new stack using all currently existing Blight stacks on that enemy; existing timers are not rewritten. This is a proposal, not settled intent. Explain examples before coding. |
| A2 | Does Cloying Breath literally apply poison plus a 40% Blight chance regardless of passive, or use poison for the Poison passive and 40% Blight for the Blight passive? | Wording permits both. Ask explicitly; do not conflate the game's Poisonous keyword with unconditional poison damage. |
| A3 | Is Breath's 40% a flat per-hit roll or affected by proc coefficient/luck? | Proposed: flat server-side 40% per accepted victim hit, no luck scaling. Confirm before implementation. |
| B1 | Resolved 2026-10-12: Blast's 0.2s interval scales down with attack speed. | Confirmed by Taylor; implemented as a 0.2s minimum base duration divided by attack speed. In-game feel check pending. |
| C1 | Does Healing Beacon grant 20% barrier immediately or accumulate it through healing ticks? Does 20% cap total barrier or only this beacon's contribution? | Proposed: convert its would-be healing ticks into barrier at full health, adding only while total barrier is below 20% of max health; never subtract unrelated barrier. Taylor must confirm accumulation/rate semantics. |
| C2 | What counts as full health with shields/curse, and how should the tick that finishes healing behave? | Proposed: use full effective health excluding barrier; explicitly decide shield inclusion and whether only the following tick supplies barrier. Agent should show concrete examples. |
| H1 | Does “all enemies above the radius” mean unlimited height, and should roofs/platforms block the rain? | Preserve the horizontal radius. Ask about upper reach and obstruction rather than selecting an arbitrary cylinder height. Any finite engine/search bound must still satisfy the chosen behavior. |
| L1 | How much primary knockback, and which larger Spiked Fist targets should become eligible? | Agent inspects force/mass and proposes a small test target list and values. Explicit boss exclusion remains required. |
| M1 | What per-hit chance grants Regenerative on Power-Saw, and is it one roll per victim per saw tick or one roll per tick that hits anything? Does proc/luck scale it? | Stacking was verified, so a chance is needed. Agent measures hit cadence and proposes a probability with expected healing; no arbitrary percentage is approved here. |
| ME1 | Does “next attack” consume readiness on activation or first hit? What happens on a miss, delayed projectile, sustained/multi-tick skill or previously launched projectile? | Proposed: consume on the next player skill attack activation; its resulting hits carry readiness, including misses consuming the window. Exclude DOTs/item procs and attacks launched before readiness. Confirm whether the entire sustained skill qualifies. |
| ME2 | Do two swings mean the first two combo steps, any two swings, or two successful hits? When exactly does the 0.5s timer start? | Proposed: after the second normal combo swing's attack event, even on a miss. Verify timing leaves a useful window for the intended follow-up and does not grant readiness on every later frame. |
| R1 | Resolved 2026-10-12: Disperse movement and area. | Taylor requested stronger enemy knockback in 0.5.3 and a 13m radius in 0.5.4. Starting test values: 20 m/s outward plus 5 m/s lift before mass resistance. Keep 4s Weaken and 90% of vanilla airborne upward impulse, now added to current momentum instead of resetting velocity. Knockback feel and fast-fall behavior await testing. |
| R2 | Resolved 2026-10-12: Fruiting trigger eligibility/chance. | Taylor approved all allied direct skill hits including minions, flat 20%, excluding DOTs and item procs. No luck or proc-coefficient scaling; bonus cannot retrigger. |
| R3 | Resolved 2026-10-12: Bonus damage and pickup. | Taylor approved triggering attacker damage stat ×4.0 (increased to 400% in 0.5.2), noncritical bonus with zero proc coefficient, 15% max-health fruit healing (reduced in 0.5.3) and collection by the triggering attacker's team. |
| R4 | Resolved 2026-10-12: Stack pool and death drops. | Taylor explicitly changed the proposal: each application ADDS five shared stacks AND refreshes the full pool to 10s. No artificial cap. Remove the old fruit-on-death drops. |
| R5 | Resolved 2026-10-12: Visual and Harvest Weaken. | Taylor approved the existing fruit-cluster effect and 4s Harvest Weaken. Visibility remains an in-game test. |

## 6. Evidence and limitations

### Verified locally on 2026-10-12

- The current source is version 0.3.0 and contains the Commando and Artificer implementations described above. Read it before changing either survivor.
- `RoR2.dll` SHA-256 at inspection: `A5F7D83C7E6872FDD6099FDC299ADB7D4FA03CBF465E526D6F5EFCC35C2C281D`. This fingerprint identifies the inspected assembly; it is not a marketing version number. Reinspect affected hooks after a game update.
- Installed English localization in `Risk of Rain 2_Data\StreamingAssets\Language\en\CharacterBodies.json` distinguishes Bandit's Blast rifle (four ammo charges) from Burst shotgun (five pellets). It lists Arrow Rain as 225% DPS and Harvest as 330% direct damage with healing fruit on death.
- `Keywords.json` describes Regenerative healing over 0.5s. The Croco asset bundle marks `bdCrocoRegen` stackable, and the local assembly uses stack count for its regeneration contribution. This supports conditional chance-based Power-Saw healing.
- `DLC4.json` contains Cloying Breath's name and description. This verifies its presence in installed data, not ownership, selection availability or unlock completion.
- The Huntress state asset reports Arrow Rain `damageCoefficient = 2.2`; the text/asset mismatch is deliberately left as a runtime investigation rather than a guessed DPS constant.

### Not yet verified

Most new survivors' exact hook signatures, all runtime damage paths, final numerical values for qualitative effects, multiplayer behavior and custom visuals have not been implemented or tested. Type names in this plan are navigation aids. Third-party API documentation supports the dependency options, but does not prove a particular mod design needs those dependencies.

### Completion record for the implementing agent

For each survivor, append the package version, decisions resolved, exact dependency additions, build/validation result, manual test results and remaining limitations here. Keep “implemented,” “offline checked” and “accepted in game” distinct. Do not overwrite the required behavior with an implementation shortcut; amend this document only when Taylor approves a changed design.

#### REX — 0.5.0 — 2026-10-12

Follow-up 0.5.1: Taylor accepted Disperse's jump height and requested a 20m nearby push radius and 100% initial Harvest damage. Follow-up 0.5.2: Taylor selected 400% triggering-attacker damage per consumed stack; enemy maximum-health damage is not used. The current zero-proc bonus bypasses band triggers and attributes damage and lethal-hit credit to the triggering attacker. No new dependencies.

- R1–R5 resolved with Taylor during implementation; additive five-stack reapplication plus refresh replaces the original reset-to-five proposal.
- No additional mod dependencies, artwork or Unity editor work needed. Native fruit pickup and synchronized vanilla buff/fruit-cluster effect reused.
- Release build and existing survivor regression checks passed. The build retains the pre-existing BepInEx architecture-reference warning (MSB3270); no compile errors. REX offline checks cover recovery timing, distinct syringe projectiles, Disperse hook sites, death-drop patch structure, hit eligibility, additive stack arithmetic and nested bonus-queue ordering. Installed asset checks verify the projectile/buff baselines, 4s Weaken, launch reference, 25% fruit healing and 20s pickup lifetime.
- Package: `dist\Taylor-TaylorsBalancePatch-0.5.0.zip`. Import through r2modman, then launch once to expose `[REX] EnableREXChanges`. Existing config choices are preserved.
- Manual tests still required: ground/air launch in different aim directions, knockback feel, Bramble Volley regression, Harvest recovery/damage, stacking and 10s refresh, fruit drops/healing and world indicator, REX toggle false, allies/minions, and host/client synchronization. Third-party skills without native damage-source tags may need compatibility support.
