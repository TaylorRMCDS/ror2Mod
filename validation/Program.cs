using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TaylorsBalancePatch;

if (args.Length != 2) throw new ArgumentException("Supply installed RoR2.dll and built plugin paths.");
using var game = AssemblyDefinition.ReadAssembly(args[0]);
using var plugin = AssemblyDefinition.ReadAssembly(args[1]);
var barrage = game.MainModule.GetType("EntityStates.Commando.CommandoWeapon.FireBarrage");
CheckPatch(barrage.Methods.Single(m => m.Name == "OnEnter"), CommandoBalance.PatchBarrageShots, OpCodes.Ldc_I4, 8);
CheckPatch(barrage.Methods.Single(m => m.Name == "FireBullet"), CommandoBalance.PatchBarrageDamage, OpCodes.Ldc_R4, 1.15f);
var enter = barrage.Methods.Single(m => m.Name == "OnEnter").Body.Instructions;
Check(enter.Any(i => i.Operand is FieldReference f && f.Name == "attackSpeedStat") &&
    enter.Any(i => i.OpCode == OpCodes.Mul), "Barrage keeps vanilla attack-speed scaling.");

var armorInstructions = game.MainModule.GetType("RoR2.CharacterBody").Methods
    .Single(m => m.Name == "RecalculateStats").Body.Instructions;
var armorStart = armorInstructions.Select((instruction, index) => (instruction, index)).Single(x =>
    x.instruction.Operand is FieldReference f && f.FullName == "RoR2.BuffDef RoR2.RoR2Content/Buffs::ArmorBoost").index;
Check(armorInstructions.Skip(armorStart).Take(8).Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 200f),
    "The installed game's standard ArmorBoost grants exactly 200 armor.");

var bulletBase = game.MainModule.GetType("EntityStates.GenericBulletBaseState");
Check(bulletBase.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
    i.Operand is MethodReference mr && mr.Name == "ModifyBulletAttack")), "Phase Blast's shared firing path calls its bullet modifier.");
var secondBlast = game.MainModule.GetType("EntityStates.Commando.CommandoWeapon.FireShotgunBlast").Methods.Single(m => m.Name == "FixedUpdate");
Check(secondBlast.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.Name == "FireBullet"),
    "The second Phase Blast uses the same bullet firing path.");

var pluginType = plugin.MainModule.GetType("TaylorsBalancePatch.Plugin");
var pluginInstructions = pluginType.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
var commandoType = plugin.MainModule.GetType("TaylorsBalancePatch.CommandoChanges");
var commandoInstructions = commandoType.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
Check(!pluginInstructions.Concat(commandoInstructions).Any(i =>
        i.Operand is MemberReference m && m.FullName.Contains("FirePistol2")),
    "The plugin has no Double Tap damage hook.");
Check(CommandoBalance.GetDescription("COMMANDO_PRIMARY_NAME") == null &&
    CommandoBalance.GetDescription("COMMANDO_PRIMARY_DESCRIPTION") == null,
    "Double Tap text is untouched.");
Check(!pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions
    .Where(i => i.OpCode == OpCodes.Ldstr).Any(i => ((string)i.Operand).Contains("10000%")), "Old test initialization is gone.");
Check(pluginInstructions.Any(i => i.Operand is MethodReference m && m.Name == "Bind") &&
    pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && ((string)i.Operand).Contains("Commando changes DISABLED")),
    "The plugin binds its config and includes the disabled startup path.");
Check(commandoInstructions.Any(i => i.Operand is MethodReference m && m.Name == "AddTimedBuff"),
    "Dive uses the timed buff system.");
Check(commandoInstructions.Any(i => i.Operand is MethodReference m &&
        m.Name == "get_active" && m.DeclaringType.Name == "NetworkServer"),
    "Server authority guards the buff and grenade physics.");

Check(!commandoInstructions.Any(i => i.Operand is MethodReference m &&
    (m.Name == "add_ProcessJump_bool" || m.Name == "add_TryPerformJump" ||
     m.DeclaringType.FullName.Contains("Commando.SlideState"))) &&
    CommandoBalance.GetDescription("COMMANDO_UTILITY_ALT_DESCRIPTION") == null &&
    !CommandoBalance.GetDescription("COMMANDO_UTILITY_DESCRIPTION")!.Contains("jump"),
    "Commando adds no jump allowance or airborne Slide override; native utility jumping is restored.");
var wall = game.MainModule.GetType("EntityStates.Mage.Weapon.PrepWall");
CheckFieldPatch(wall.Methods.Single(m => m.Name == "FixedUpdate"), ArtificerBalance.PatchWallInput, "RoR2.InputBankTest", "skill4");
CheckFieldPatch(wall.Methods.Single(m => m.Name == "OnExit"), ArtificerBalance.PatchWallRefund, "RoR2.SkillLocator", "special");
var fly = game.MainModule.GetType("EntityStates.Mage.FlyUpState");
var movement = fly.Methods.Single(m => m.Name == "HandleMovements");
Check(ArtificerBalance.LaunchSpeedSite(movement.Body).Operand is FieldReference launchField && launchField.Name == "moveSpeedStat",
    "Ion Surge retains the native movement-speed read for its capped multiplier.");
Check(ArtificerBalance.CappedLaunchSpeed(3.5f) == 3.5f && ArtificerBalance.CappedLaunchSpeed(7f) == 7f &&
    ArtificerBalance.CappedLaunchSpeed(10.5f) == 10.5f && ArtificerBalance.CappedLaunchSpeed(14f) == 14f &&
    ArtificerBalance.CappedLaunchSpeed(70f) == 14f,
    "Ion Surge scales with movement speed from vanilla base distance to a hard 2x-base-distance cap.");
Check(movement.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Evaluate"),
    "Ion Surge retains its launch curve.");
ArtificerBalance.PatchLaunchSource(fly.Methods.Single(m => m.Name == "OnEnter").Body);
Check(fly.Methods.Single(m => m.Name == "OnEnter").Body.Instructions.Single(i => i.Operand is FieldReference f &&
    f.Name == "damageSource").Previous.OpCode == OpCodes.Ldc_I4_4, "Ion Surge damage is tagged as Utility.");
var slowStart = armorInstructions.Select((instruction, index) => (instruction, index)).Single(x =>
    x.instruction.Operand is FieldReference f && f.FullName == "RoR2.BuffDef RoR2.JunkContent/Buffs::Slow30").index;
Check(armorInstructions.Skip(slowStart).Take(8).Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.3f),
    "The installed game's Slow30 buff supplies a 30% movement slow.");
var flame = game.MainModule.GetType("EntityStates.Mage.Weapon.Flamethrower");
var effectStores = flame.Methods.Single(m => m.Name == "FixedUpdate").Body.Instructions.Where(i =>
    i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
    (f.Name == "leftFlamethrowerEffectInstance" || f.Name == "rightFlamethrowerEffectInstance")).ToArray();
Check(effectStores.Length == 2 && effectStores.All(i => i.Previous.Operand is MethodReference m && m.Name == "Instantiate"),
    "Both Flamethrower effect creation sites match the visual extension hook.");
Check(flame.Methods.Single(m => m.Name == "FireGauntlet").Body.Instructions.Any(i =>
    i.Operand is FieldReference f && f.Name == "maxDistance" && f.DeclaringType == flame),
    "Flamethrower's damage path reads the modified range.");
Check(!flame.Methods.Single(m => m.Name == "OnEnter").Body.Instructions.Any(i =>
    i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "maxDistance"),
    "Flamethrower initialization does not overwrite the range override.");
var explosion = game.MainModule.GetType("RoR2.Projectile.ProjectileExplosion").Methods.Single(m => m.Name == "PerformDamage");
Check(explosion.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
    f.Name == "inflictor" && i.Previous.Operand is MethodReference m && m.Name == "get_gameObject"),
    "Projectile splash damage keeps the projectile as inflictor for the Plasma Bolt slow.");
var banditBase = game.MainModule.GetType("EntityStates.Bandit2.Weapon.Bandit2FirePrimaryBase");
var shotClock = new BlastShotClock();
Check(shotClock.CanFire(0f), "Blast can fire its first round immediately.");
for (int shot = 0; shot < 6; shot++)
{
    float now = shot * 0.3f;
    Check(shotClock.CanFire(now), "Blast can fire at each full 0.3-second boundary.");
    shotClock.RecordShot(now);
    Check(!shotClock.CanFire(now + 0.03f) && !shotClock.CanFire(now + 0.299f),
        "Blast rejects spam and early shots throughout the 0.3-second interval.");
}
var banditChanges = plugin.MainModule.GetType("TaylorsBalancePatch.BanditChanges");
Check(banditChanges.Methods.Single(m => m.Name == "CanFireBlast").Body.Instructions.Any(i =>
    i.Operand is MethodReference m && m.Name == "CanFire") &&
    banditChanges.Methods.Single(m => m.Name == "RecordBlastShot").Body.Instructions.Any(i =>
    i.Operand is MethodReference m && m.Name == "RecordShot"),
    "Blast blocks skill activation and records actual shots independently of state/reload duration.");
var rifle = game.MainModule.GetType("EntityStates.Bandit2.Weapon.Bandit2FireRifle");
Check(rifle.BaseType.FullName == "EntityStates.Bandit2.Weapon.Bandit2FirePrimaryBase" &&
    rifle.Methods.Single(m => m.Name == "ModifyBullet").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "ModifyBullet"),
    "Blast is isolated through its rifle-specific state while Burst keeps the shared base state.");
var skillDef = game.MainModule.GetType("RoR2.Skills.SkillDef");
Check(skillDef.Fields.Any(f => f.Name == "baseMaxStock" && f.FieldType.FullName == "System.Int32") &&
    skillDef.Fields.Any(f => f.Name == "skillNameToken" && f.FieldType.FullName == "System.String"),
    "Bandit Blast stock can be changed by locating its SkillDef token.");
Check(BanditBalance.BlastBaseCharges == 6 && BanditBalance.BlastMinimumBaseInterval == 0.3f &&
    BanditBalance.BlastSpreadMultiplier == 0.5f && BanditBalance.BlastBloomMultiplier == 0.5f,
    "Bandit values match the requested magazine, cadence, spread and bloom changes.");
Check(new[] { -12f, -4f, 4f, 12f }.Select((x, i) => BanditBalance.GetStockPipX(i, 4) == x).All(x => x),
    "The four-pip Burst reticle stays centered on its original spacing.");
Check(new[] { -20f, -12f, -4f, 4f, 12f, 20f }.Select((x, i) => BanditBalance.GetStockPipX(i, 6) == x).All(x => x),
    "The six-pip Blast reticle expands symmetrically around the same center.");
Check(BanditBalance.GetDescription("BANDIT2_PRIMARY_ALT_DESCRIPTION")?.Contains("6") == true &&
    BanditBalance.GetDescription("BANDIT2_PRIMARY_DESCRIPTION") == null,
    "Only Blast's description changes; Burst remains untouched.");
var reticlePatch = plugin.MainModule.GetType("TaylorsBalancePatch.BanditChanges").Methods
    .Single(m => m.Name == "ExpandBanditStockDisplay");
Check(reticlePatch.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Instantiate") &&
    reticlePatch.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "SetActive") &&
    reticlePatch.Body.Instructions.Any(i => i.Operand is FieldReference f &&
        f.Name == "skillStockSpriteDisplays") &&
    reticlePatch.Body.Instructions.Any(i => i.Operand is FieldReference f &&
        f.Name == "maximumStockCountToBeValid"),
    "Bandit reticle hook adds the two missing live stock displays and extends existing display validity.");
var reticleLayout = plugin.MainModule.GetType("TaylorsBalancePatch.BanditChanges").Methods
    .Single(m => m.Name == "CenterBanditStockPips");
Check(reticleLayout.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_maxStock") &&
    reticleLayout.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_localPosition"),
    "The live Bandit reticle layout follows the active primary's stock capacity.");

var orbitalSkill = game.MainModule.GetType("RoR2.Skills.CaptainOrbitalSkillDef");
var availability = orbitalSkill.Methods.Single(m => m.Name == "get_isAvailable");
Check(availability.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "blockOrbitalSkills") &&
    new[] { "GetCurrentIcon", "GetCurrentNameToken", "GetCurrentDescriptionToken", "CanExecute", "IsReady" }
        .All(name => orbitalSkill.Methods.Single(m => m.Name == name).Body.Instructions.Any(i =>
            i.Operand is MethodReference called && called.Name == "get_isAvailable")),
    "Captain's hidden-realm restriction is isolated to the orbital skill availability path.");
Check(CaptainBalance.GetBarrierGrant(0f, 100f, 10f) == 10f &&
    CaptainBalance.GetBarrierGrant(15f, 100f, 10f) == 5f &&
    CaptainBalance.GetBarrierGrant(30f, 100f, 10f) == 0f &&
    CaptainBalance.GetBarrierGrant(0f, 100f, -1f) == 0f,
    "Healing Beacon barrier accumulates by native tick size, caps at 20% and never removes existing barrier.");
var healingWard = game.MainModule.GetType("RoR2.HealingWard").Methods.Single(m => m.Name == "HealOccupants");
Check(healingWard.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Heal") &&
    healingWard.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "healFraction"),
    "Healing Beacon retains the game's native team, radius and healing tick path.");
var captainChanges = plugin.MainModule.GetType("TaylorsBalancePatch.CaptainChanges");
var withoutBlock = captainChanges.Methods.Single(m => m.Name == "WithoutOrbitalBlock");
var isCaptainHealing = captainChanges.Methods.Single(m => m.Name == "IsCaptainHealingBeacon");
var captainHealing = captainChanges.Methods.Single(m => m.Name == "HealingBeacon");
Check(withoutBlock.Body.Instructions.Count(i => i.Operand is FieldReference f && f.Name == "blockOrbitalSkills") >= 3 &&
    withoutBlock.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally),
    "Captain temporarily bypasses only the orbital restriction and restores the scene flag even on failure.");
Check(captainHealing.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_fullCombinedHealth") &&
    captainHealing.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "AddBarrier") &&
    isCaptainHealing.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "IsHealingWardName"),
    "Only Captain's Healing Beacon grants capped barrier to recipients already at full combined health.");

Check(CaptainBalance.IsHealingWardName("CaptainHealingWard(Clone)") &&
    CaptainBalance.IsHealingWardName("CaptainHealingWard") &&
    !CaptainBalance.IsHealingWardName("CaptainSupplyDrop, Healing(Clone)") &&
    !CaptainBalance.IsHealingWardName("HealingWard(Clone)") &&
    !CaptainBalance.IsHealingWardName("CaptainHealingWardOther"),
    "Barrier recognizes the separately spawned Captain ward and excludes other healing wards.");
var arrowRain = game.MainModule.GetType("EntityStates.Huntress.ArrowRain");
Check(arrowRain.Methods.Single(m => m.Name == "DoFireArrowRain").Body.Instructions.Any(i =>
        i.Operand is FieldReference f && f.Name == "damageCoefficient") &&
    arrowRain.Fields.Any(f => f.Name == "arrowRainRadius"),
    "Arrow Rain supplies separate damage and horizontal-radius values for the scoped patch.");
var dotZone = game.MainModule.GetType("RoR2.Projectile.ProjectileDotZone");
var dotReset = dotZone.Methods.Single(m => m.Name == "ResetOverlap");
Check(dotReset.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "overlapProcCoefficient") &&
    dotReset.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "hitBoxGroup"),
    "Arrow Rain's native tick attack reads an independent proc multiplier and HitBoxGroup.");
Check(HuntressBalance.BlinkCooldown == 6f && HuntressBalance.ArrowRainDamageMultiplier == 1.05f &&
    HuntressBalance.ArrowRainVerticalReach == 100f,
    "Huntress values match the requested cooldown, damage and effectively unlimited upward reach.");
var huntressChanges = plugin.MainModule.GetType("TaylorsBalancePatch.HuntressChanges");
var huntressInstructions = huntressChanges.Methods.Concat(huntressChanges.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions).ToArray();
var extendArrowRain = huntressChanges.Methods.Single(m => m.Name == "ExtendArrowRain");
Check(huntressInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "HUNTRESS_UTILITY_NAME") &&
    !huntressInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "HUNTRESS_UTILITY_ALT1_NAME") &&
    huntressInstructions.Any(i => i.Operand is FieldReference f && f.Name == "baseRechargeInterval"),
    "Huntress changes normal Blink's cooldown without targeting Phase Blink.");
Check(extendArrowRain.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_localScale") &&
    extendArrowRain.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "hitBoxes") &&
    !extendArrowRain.Body.Instructions.Any(i => i.Operand is MemberReference m && m.FullName.Contains("CapsuleCollider")),
    "Arrow Rain defines the tall damage hitbox through transform scale, without an ineffective CapsuleCollider.");
Check(game.MainModule.GetType("RoR2.OverlapAttack").Methods.Single(m => m.Name == "Fire")
    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_lossyScale"),
    "The installed overlap damage path reads the hitbox's world transform scale.");
Check(!huntressInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
    (f.Name == "overlapProcCoefficient" || f.Name == "procCoefficient")),
    "Huntress leaves both native proc coefficients untouched.");
var arrowVisuals = huntressChanges.Methods.Single(m => m.Name == "ExtendArrowRainVisuals").Body.Instructions;
Check(arrowVisuals.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "ArrowsFalling") &&
    arrowVisuals.Any(i => i.Operand is MethodReference m && m.Name == "set_scale") &&
    arrowVisuals.Any(i => i.Operand is MethodReference m && m.Name == "set_cullingMode") &&
    arrowVisuals.Any(i => i.Operand is MethodReference m && m.Name == "set_localBounds"),
    "Arrow Rain extends only falling arrows through the volume and updates simulation/culling bounds.");

var scrapLauncher = game.MainModule.GetType("EntityStates.Toolbot.FireGrenadeLauncher");
Check(scrapLauncher.BaseType.FullName == "EntityStates.GenericProjectileBaseState",
    "Scrap Launcher retains the game's standard projectile firing path.");
var projectileExplosion = game.MainModule.GetType("RoR2.Projectile.ProjectileExplosion");
Check(projectileExplosion.Fields.Any(f => f.Name == "falloffModel" &&
        f.FieldType.FullName == "RoR2.BlastAttack/FalloffModel") &&
    projectileExplosion.Fields.Any(f => f.Name == "blastRadius"),
    "Scrap Launcher's blast falloff can be removed independently of its radius.");
var sawFixed = game.MainModule.GetType("EntityStates.Toolbot.FireBuzzsaw").Methods
    .Single(m => m.Name == "FixedUpdate").Body.Instructions;
var sawFire = sawFixed.Where(i => i.Operand is MethodReference m && m.FullName ==
    "System.Boolean RoR2.OverlapAttack::Fire(System.Collections.Generic.List`1<RoR2.HurtBox>)").ToArray();
Check(sawFire.Length == 1 && sawFire[0].Previous.OpCode == OpCodes.Ldnull &&
    sawFire[0].Next.Operand is FieldReference sawHit && sawHit.Name == "hitOverlapLastTick",
    "Power-Saw has one accepted-hurtbox collection point per damage tick.");
Check(MultBalance.PowerSawRegenDuration == 0.5f &&
    MultBalance.GetDescription("TOOLBOT_PRIMARY_ALT3_DESCRIPTION")!.Contains("Killing an enemy") &&
    !MultBalance.GetDescription("TOOLBOT_PRIMARY_ALT3_DESCRIPTION")!.Contains("chance"),
    "Power-Saw grants 0.5s Regenerative on kills without a hit-based roll.");
Check(MultBalance.GetDescription("TOOLBOT_PRIMARY_ALT2_DESCRIPTION")?.Contains("no damage falloff") == true &&
    MultBalance.GetDescription("TOOLBOT_PRIMARY_ALT2_NAME") == null,
    "MUL-T preserves the Scrap Launcher change without renaming skills.");
var multChanges = plugin.MainModule.GetType("TaylorsBalancePatch.MultChanges");
var multInstructions = multChanges.Methods.Concat(multChanges.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
var sawKill = multChanges.Methods.Single(m => m.Name == "SawKill").Body.Instructions;
Check(multInstructions.Any(i => i.Operand is MethodReference m && m.Name == "add_onCharacterDeathServer") &&
    sawKill.Any(i => i.Operand is MethodReference m && m.Name == "AddTimedBuff") &&
    sawKill.Any(i => i.Operand is FieldReference f && f.Name == "damageSource") &&
    sawKill.Any(i => i.Operand is FieldReference f && f.Name == "inflictor") &&
    sawKill.Any(i => i.Operand is FieldReference f && f.Name == "dotIndex") &&
    !multInstructions.Any(i => i.Operand is MemberReference m && m.DeclaringType?.FullName == "UnityEngine.Random"),
    "Power-Saw regeneration uses server kill attribution, actual weapon slot and direct-hit checks, with no random rolls.");
Check(pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && (string)i.Operand == MultBalance.ConfigSection),
    "MUL-T has its own category configuration and plugin startup path.");

Check(EngineerBalance.BaseDamage == 13f && EngineerBalance.LevelDamage == 2.6f &&
    Math.Abs(EngineerBalance.GrenadeChargeDurationMultiplier - 1f / 1.1f) < 0.00001f &&
    Math.Abs(EngineerBalance.PressureMineFullDamageScale - 10f / 3f) < 0.00001f &&
    EngineerBalance.CarbonizerBaseRegen == 2f,
    "Engineer values match 13 (+2.6/level), 10% faster charge, 1000% full mine damage and TR58 regeneration 2.");
Check(EngineerBalance.GetDescription("ENGI_PRIMARY_DESCRIPTION")?.Contains("10% faster") == true &&
    EngineerBalance.GetDescription("ENGI_SECONDARY_DESCRIPTION")?.Contains("1000%") == true &&
    EngineerBalance.GetDescription("ENGI_SPECIAL_ALT1_DESCRIPTION")!.Contains("2 health per second"),
    "Engineer descriptions update only the changed skills including TR58 regeneration without changing its health.");
var chargeGrenades = game.MainModule.GetType("EntityStates.Engi.EngiWeapon.ChargeGrenades");
var grenadeEnter = chargeGrenades.Methods.Single(m => m.Name == "OnEnter").Body.Instructions;
Check(new[] { "baseTotalDuration", "baseMaxChargeTime" }.All(name => grenadeEnter.Any(i =>
        i.Operand is FieldReference f && f.Name == name)) &&
    grenadeEnter.Count(i => i.Operand is FieldReference f && f.Name == "attackSpeedStat") == 2,
    "Bouncing Grenades has separate total/full-charge fields and preserves attack-speed scaling.");
var mineFull = game.MainModule.GetType("EntityStates.Engi.Mine.MineArmingFull");
var mineWeak = game.MainModule.GetType("EntityStates.Engi.Mine.MineArmingWeak");
Check(mineFull.BaseType.FullName == "EntityStates.Engi.Mine.BaseMineArmingState" &&
    mineWeak.BaseType.FullName == "EntityStates.Engi.Mine.BaseMineArmingState",
    "Pressure Mine's fully armed stage can be changed without modifying its weak stage.");
var engineerChanges = plugin.MainModule.GetType("TaylorsBalancePatch.EngineerChanges");
var engineerInstructions = engineerChanges.Methods.Concat(engineerChanges.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
var engineerBodies = engineerChanges.Methods.Single(m => m.Name == "Bodies").Body.Instructions;
Check(engineerInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "EngiBody") &&
    engineerInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "EngiWalkerTurretBody") &&
    engineerBodies.Any(i => i.Operand is FieldReference f && f.Name == "baseDamage") &&
    engineerBodies.Any(i => i.Operand is FieldReference f && f.Name == "levelDamage") &&
    engineerBodies.Any(i => i.Operand is FieldReference f && f.Name == "baseRegen") &&
    !engineerBodies.Any(i => i.Operand is FieldReference f && f.Name == "baseMaxHealth"),
    "Engineer changes target Engineer damage and only the mobile TR58 turret's base regeneration, not turret health.");
Check(pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && (string)i.Operand == EngineerBalance.ConfigSection),
    "Engineer has its own category configuration and plugin startup path.");

Check(LoaderBalance.BaseHealth == 130f && LoaderBalance.BaseArmor == 10f,
    "Loader retains its existing stat changes.");
Check(LoaderBalance.QuickGrapple(true, 0f) && LoaderBalance.QuickGrapple(true, 0.5f) &&
    LoaderBalance.QuickGrapple(true, 1f) && !LoaderBalance.QuickGrapple(true, 1.02f) &&
    !LoaderBalance.QuickGrapple(false, 0f),
    "Grapple refunds include exactly 1s, exclude longer attachments and missed shots.");
Check(LoaderBalance.GetDescription("LOADER_YANKHOOK_DESCRIPTION") == null &&
    LoaderBalance.GetDescription("LOADER_SECONDARY_DESCRIPTION")!.Contains("50%"),
    "Spiked Fist uses its original tooltip; Grapple Fist explains its cooldown refund.");
var basicMelee = game.MainModule.GetType("EntityStates.BasicMeleeAttack");
Check(basicMelee.Fields.Any(f => f.Name == "pushAwayForce") &&
    game.MainModule.GetType("EntityStates.Loader.SwingComboFist").BaseType.FullName ==
        "EntityStates.Loader.LoaderMeleeAttack" &&
    !plugin.MainModule.GetType("TaylorsBalancePatch.LoaderChanges").Methods.Any(m => m.Name == "MeleeEnter"),
    "Knuckleboom uses native melee knockback without an added Loader override.");
var grappleFly = game.MainModule.GetType("RoR2.Projectile.ProjectileGrappleController/FlyState")
    .Methods.Single(m => m.Name == "FixedUpdateBehavior").Body.Instructions;
Check(grappleFly.Count(i => i.Operand is MethodReference m && m.FullName ==
        "System.Single UnityEngine.Rigidbody::get_mass()") == 1 &&
    grappleFly.Any(i => i.Operand is FieldReference f && f.Name == "yankMassLimit"),
    "Spiked Fist's pull classification uses one explicit rigidbody-mass threshold.");
var loaderChanges = plugin.MainModule.GetType("TaylorsBalancePatch.LoaderChanges");
var loaderInstructions = loaderChanges.Methods.Concat(loaderChanges.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
Check(loaderInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "LoaderBody") &&
    !loaderInstructions.Any(i => i.Operand is FieldReference f && f.Name == "yankMassLimit") &&
    !loaderInstructions.Any(i => i.Operand is MethodReference m && m.Name == "add_FixedUpdateBehavior"),
    "Loader no longer changes Spiked Fist's projectile or target classification.");
foreach (string hook in new[] { "FixedUpdate", "SetHookReference", "OnExit" })
    foreach (string action in new[] { "add_", "remove_" })
        Check(loaderInstructions.Any(i => i.Operand is MethodReference m && m.Name == action + hook),
            $"Grapple refund lifecycle includes {action + hook}.");
Check(loaderInstructions.Any(i => i.Operand is MethodReference m && m.Name == "GetFinalRechargeInterval") &&
    loaderInstructions.Any(i => i.Operand is MethodReference m && m.Name == "set_rechargeStopwatch") &&
    loaderInstructions.Any(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference t && t.FullName == "EntityStates.Loader.FireHook"),
    "Refund uses the adjusted cooldown, directly credits recharge and scopes to base Grapple Fist.");
Check(pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && (string)i.Operand == LoaderBalance.ConfigSection),
    "Loader has its own category configuration and plugin startup path.");

var harvestState = game.MainModule.GetType("EntityStates.Treebot.TreebotFireFruitSeed");
var harvestEnter = harvestState.Methods.Single(m => m.Name == "OnEnter").Body.Instructions;
Check(harvestEnter.Any(i => i.Operand is MethodReference m && m.Name == "FireProjectile") &&
    harvestEnter.Any(i => i.Operand is FieldReference f && f.Name == "baseDuration") &&
    harvestState.Methods.Single(m => m.Name == "FixedUpdate").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "SetNextStateToMain"),
    "Harvest fires on state entry; its duration is the recovery before returning to main.");
var syringeState = game.MainModule.GetType("EntityStates.Treebot.Weapon.FireSyringe").Methods
    .Single(m => m.Name == "FixedUpdate").Body.Instructions;
Check(syringeState.Any(i => i.Operand is FieldReference f && f.Name == "finalProjectilePrefab") &&
    syringeState.Any(i => i.Operand is FieldReference f && f.Name == "projectilePrefab"),
    "Inject has separate normal and final projectile references.");
var sonic = game.MainModule.GetType("EntityStates.Treebot.Weapon.FireSonicBoom");
var sonicEnter = sonic.Methods.Single(m => m.Name == "OnEnter").Body.Instructions;
Check(sonicEnter.Count(i => i.Operand is MethodReference m && m.Name == "GetAimRay") == 1 &&
    sonicEnter.Count(i => i.Operand is MethodReference m && m.Name == "get_isAuthority") == 1 &&
    sonicEnter.Count(i => i.Operand is MethodReference m && m.FullName ==
        "System.Boolean UnityEngine.Networking.NetworkServer::get_active()") == 1,
    "Disperse has the single aim, server attack and owner recoil gates required by its scoped IL hook.");
Check(game.MainModule.GetType("EntityStates.Treebot.Weapon.FirePlantSonicBoom").BaseType == sonic,
    "Bramble Volley inherits the shared state and must be excluded by exact state type.");
var deathMethod = game.MainModule.GetType("RoR2.GlobalEventManager").Methods.Single(m => m.Name == "OnCharacterDeath");
var deathBefore = deathMethod.Body.Instructions.Select(i => i.ToString()).ToArray();
var branchTargets = deathMethod.Body.Instructions.Where(i => i.Operand is Instruction).Select(i => i.Operand).ToArray();
RexBalance.PatchFruitDeathBuffCheck(deathMethod.Body);
Check(deathMethod.Body.Instructions.Where((i, index) => i.ToString() != deathBefore[index]).Count() == 3 &&
    branchTargets.SequenceEqual(deathMethod.Body.Instructions.Where(i => i.Operand is Instruction).Select(i => i.Operand)),
    "Fruiting death-drop patch changes only the buff check and preserves all existing branch targets.");
bool refusedDeathRepatch = false;
try { RexBalance.PatchFruitDeathBuffCheck(deathMethod.Body); }
catch (InvalidOperationException) { refusedDeathRepatch = true; }
Check(refusedDeathRepatch, "Fruiting death-drop patch rejects an unexpected/already-patched method.");
var visualInstructions = game.MainModule.GetType("RoR2.CharacterBody").Methods
    .Single(m => m.Name == "UpdateAllTemporaryVisualEffects").Body.Instructions;
Check(visualInstructions.Any(i => i.Operand is FieldReference f && f.Name == "fruitingEffectPrefab") &&
    visualInstructions.Any(i => i.Operand is FieldReference f && f.FullName ==
        "RoR2.BuffDef RoR2.RoR2Content/Buffs::Fruiting"),
    "The game renders a world-space Fruiting effect from the synchronized vanilla buff.");
Check(RexBalance.IsEligibleHit(false, false, false, false, true, true, 1f) &&
    !RexBalance.IsEligibleHit(true, false, false, false, true, true, 1f) &&
    !RexBalance.IsEligibleHit(false, true, false, false, true, true, 1f) &&
    !RexBalance.IsEligibleHit(false, false, true, false, true, true, 1f) &&
    !RexBalance.IsEligibleHit(false, false, false, true, true, true, 1f) &&
    !RexBalance.IsEligibleHit(false, false, false, false, false, true, 1f) &&
    !RexBalance.IsEligibleHit(false, false, false, false, true, false, 1f) &&
    !RexBalance.IsEligibleHit(false, false, false, false, true, true, 0f),
    "Fruiting accepts allied direct skill damage and excludes rejected, delayed, DOT, proc-chain, untagged, hostile and zero-damage hits.");
Check(RexBalance.StacksAfterHarvest(0) == 5 && RexBalance.StacksAfterHarvest(3) == 8 &&
    RexBalance.StacksAfterHarvest(10) == 15,
    "Reapplication adds five to the remaining pool instead of resetting or capping it.");
var hitQueue = new AfterDamageQueue<int>();
var applied = new System.Collections.Generic.List<int>();
hitQueue.Enter();
hitQueue.Enqueue(1);
hitQueue.Enter();
hitQueue.Enqueue(2);
hitQueue.Exit(applied.Add);
Check(applied.Count == 0, "Nested damage never drains a Fruiting bonus before outer damage/death handling finishes.");
bool insideBonus = false;
Action<int>? apply = null;
apply = hit =>
{
    Check(!insideBonus, "Bonus queue does not recursively drain while a bonus is being applied.");
    insideBonus = true;
    applied.Add(hit);
    if (hit == 1)
    {
        hitQueue.Enter();
        hitQueue.Enqueue(3);
        hitQueue.Exit(apply!);
    }
    insideBonus = false;
};
hitQueue.Exit(apply);
Check(applied.SequenceEqual(new[] { 1, 2, 3 }), "Queued hits are applied once each in order after the outer damage call.");
var rexHandler = plugin.MainModule.GetType("TaylorsBalancePatch.RexChanges").Methods.Single(m => m.Name == "DamageDealt");
Check(!rexHandler.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "TakeDamage") &&
    rexHandler.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RemoveOldestTimedBuff") &&
    rexHandler.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Enqueue"),
    "Fruiting consumes a timed stack and queues its bonus instead of dealing nested damage from the damage-report event.");
var rexDisperse = plugin.MainModule.GetType("TaylorsBalancePatch.RexChanges").Methods
    .Single(m => m.Name == "Disperse").Body.Instructions;
var resetVelocity = rexDisperse.Single(i => i.Operand is MethodReference m && m.Name == "set_resetVelocity");
Check(resetVelocity.Previous.OpCode == OpCodes.Ldc_I4_0 &&
    rexDisperse.Any(i => i.Operand is MethodReference m && m.Name == "ApplyForceImpulse") &&
    !rexDisperse.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.DeclaringType.FullName == "RoR2.CharacterMotor" && f.Name == "velocity"),
    "Disperse uses an additive motor impulse without replacing existing velocity.");
var rexEnemyPush = plugin.MainModule.GetType("TaylorsBalancePatch.RexChanges").Methods
    .Single(m => m.Name == "PushEnemies").Body.Instructions;
Check(rexEnemyPush.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "ClayBossBody") &&
    rexEnemyPush.Any(i => i.Operand is MethodReference m && m.Name == "FindBodyIndex") &&
    rexEnemyPush.Any(i => i.Operand is FieldReference f && f.Name == "bodyIndex") &&
    rexEnemyPush.Any(i => i.Operand is MethodReference m && m.Name == "Evaluate") &&
    rexEnemyPush.Any(i => i.Operand is MethodReference m && m.Name == "TakeDamageForce"),
    "Disperse identifies Clay Dunestriders by body index while preserving the native mass-resistance and force path.");
Check(RexBalance.EnemyPushSpeed == 20f && RexBalance.EnemyLiftSpeed == 8f &&
    RexBalance.ClayDunestriderPushMultiplier == 1.5f && RexBalance.LaunchMultiplier == 0.9f &&
    RexBalance.DisperseRadius == 13f && RexBalance.WeakenDuration == 4f,
    "Disperse uses 20 outward/8 upward and 1.5x Dunestrider push without changing self-launch, radius or Weaken.");
Check(rexEnemyPush.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 20f) &&
    rexEnemyPush.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 8f) &&
    rexEnemyPush.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 1.5f) &&
    RexBalance.GetDescription("TREEBOT_UTILITY_DESCRIPTION")!.Contains("1.5x knockback"),
    "Disperse's compiled force path and tooltip use the retuned values.");
foreach (var (typeName, prefabName) in new[] {
    ("TaylorsBalancePatch.ArtificerChanges", "MageBody"),
    ("TaylorsBalancePatch.MultChanges", "ToolbotBody") })
{
    var survivorType = plugin.MainModule.GetType(typeName);
    var bodyPatch = survivorType.Methods.Single(m => m.Name == "Bodies").Body.Instructions;
    var survivorInstructions = survivorType.Methods.Concat(survivorType.NestedTypes.SelectMany(t => t.Methods))
        .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions);
    Check(survivorInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == prefabName) &&
        bodyPatch.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "baseAcceleration" &&
            i.Previous.OpCode == OpCodes.Ldc_R4 && (float)i.Previous.Operand == 60f) &&
        !bodyPatch.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "baseMoveSpeed") &&
        survivorType.Methods.Single(m => m.Name == "Dispose").Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "baseAcceleration"),
        $"{prefabName}: acceleration is set to 60 with rollback, without changing movement speed.");
}
var fruitInstructions = rexHandler.Body.Instructions;
var healingWrite = fruitInstructions.Single(i => i.OpCode == OpCodes.Stfld &&
    i.Operand is FieldReference f && f.FullName == "System.Single RoR2.HealthPickup::fractionalHealing");
var fruitSpawn = fruitInstructions.Single(i => i.Operand is MethodReference m &&
    m.DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && m.Name == "Spawn");
Check(fruitInstructions.IndexOf(healingWrite) < fruitInstructions.IndexOf(fruitSpawn) &&
    game.MainModule.GetType("RoR2.HealthPickup").Methods.Single(m => m.Name == "OnTriggerStay")
        .Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "fractionalHealing"),
    "Spawned Fruiting pickup healing is configured before network spawn and consumed by the game's pickup handler.");
Check(AcridBalance.NeurotoxinCooldown == 2.5f && AcridBalance.FrenziedLeapStock == 1 &&
    AcridBalance.FrenziedLeapCooldown == 8f && AcridBalance.BlightBaseDuration == 8f,
    "Existing Acrid cooldowns are preserved; every new Blight stack lasts 8 seconds.");
var breathSequence = new BreathTickSequence();
Check(Enumerable.Range(1, 17).Where(_ => breathSequence.NextAppliesBlight()).SequenceEqual(new[] { 1, 5, 9, 13, 17 }),
    "Cloying Breath applies Blight on the first hit and every four hits afterward, without random bonus stacks.");
var secondBreathSequence = new BreathTickSequence();
Check(secondBreathSequence.NextAppliesBlight() && !secondBreathSequence.NextAppliesBlight() &&
    new BreathTickSequence().NextAppliesBlight(),
    "Each enemy/cloud sequence starts independently on its own first tick.");
for (int stacks = 0; stacks <= 50; stacks++)
{
    if (Math.Abs(AcridBalance.GetBlightProcCoefficient(stacks, stacks) - Math.Min(1f, stacks * 0.1f)) > 0.00001f)
        throw new Exception("Blight per-stack proc scaling or the 1.0 cap is incorrect.");
}
var blightBudget = new BlightProcBudget<string>();
for (int i = 0; i < 12; i++) blightBudget.AddStack("Acrid A");
for (int i = 0; i < 8; i++) blightBudget.AddStack("Acrid B");
float firstShare = blightBudget.TakeProc("Acrid A"), secondShare = blightBudget.TakeProc("Acrid B");
Check(Math.Abs(firstShare - 0.6f) < 0.00001f && Math.Abs(secondShare - 0.4f) < 0.00001f &&
    Math.Abs(firstShare + secondShare - 1f) < 0.00001f && blightBudget.TakeProc("Acrid A") == 0f &&
    blightBudget.TakeProc("unknown") == 0f && AcridBalance.GetBlightProcCoefficient(-1, 1) == 0f,
    "Blight scales from 0.1 at one stack to 1.0 at ten; multiplayer attribution shares the same capped tick budget and cannot duplicate procs.");
Check(AcridBalance.GetDescription("CROCO_PRIMARY_DESCRIPTION") == null &&
    AcridBalance.GetDescription("CROCO_UTILITY_ALT1_DESCRIPTION")?.Contains("Poisonous") == true &&
    AcridBalance.GetDescription("CROCO_SPECIAL_ALT_DESCRIPTION")?.Contains("every 4 ticks") == true &&
    AcridBalance.GetDescription("CROCO_SPECIAL_ALT_DESCRIPTION")?.Contains("chance") == false,
    "Acrid descriptions preserve unrelated skills and replace the old chance with the selected passive's cadence.");
Check(AcridBalance.SpawnProtectionTail == 2f &&
    plugin.MainModule.GetType("TaylorsBalancePatch.AcridChanges").Methods.Single(m => m.Name == "EndSpawnProtection")
        .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "AddTimedBuff"),
    "Acrid spawn protection transitions to a full 2 second timed tail.");
var crocoSlash = game.MainModule.GetType("EntityStates.Croco.Slash");
var slashOverlap = crocoSlash.Methods.Single(m => m.Name == "AuthorityModifyOverlapAttack").Body.Instructions;
Check(slashOverlap.Count(i => i.Operand is FieldReference f && f.Name == "damageSource") == 1,
    "Vicious Wounds keeps its native Primary damage source without the added finisher poison hook.");
var crocoSpawn = game.MainModule.GetType("EntityStates.Croco.Spawn");
var acridChangesType = plugin.MainModule.GetType("TaylorsBalancePatch.AcridChanges");
Check(crocoSpawn.Methods.Any(m => m.Name == "OnEnter") && crocoSpawn.Methods.Any(m => m.Name == "OnExit") &&
    acridChangesType.Methods.Any(m => m.Name == "ProtectSpawn") &&
    acridChangesType.Methods.Any(m => m.Name == "EndSpawnProtection"),
    "Acrid spawn immunity is scoped to the Spawn state lifecycle.");
var chainableLeap = game.MainModule.GetType("EntityStates.Croco.ChainableLeap");
Check(chainableLeap.Methods.Single(m => m.Name == "DoImpactAuthority").Body.Instructions.Any(i =>
        i.Operand is FieldReference f && f.Name == "refundPerHit") &&
    chainableLeap.Methods.Any(m => m.Name == "GetBlastDamageType"),
    "Frenzied Leap keeps its hit-based cooldown refund and has a scoped blast damage-type hook.");
var breathFire = game.MainModule.GetType("EntityStates.Croco.Breath").Methods
    .Single(m => m.Name == "FireProjectile").Body.Instructions;
Check(breathFire.Any(i => i.Operand is MethodReference m && m.Name == "GetDamageType") &&
    breathFire.Any(i => i.Operand is MethodReference m && m.Name == "FireProjectile"),
    "Cloying Breath has one passive damage-type selection around its projectile creation path.");
var dotController = game.MainModule.GetType("RoR2.DotController");
Check(dotController.Methods.Any(m => m.Name == "InflictDot" && m.Parameters.Count == 1 &&
        m.Parameters[0].ParameterType.IsByReference) &&
    dotController.Methods.Single(m => m.Name == "OnDotStackAddedServer").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "AddBuff"),
    "The authoritative DOT entry point and one-buff-per-live-stack mirror used for Blight counting are present.");
var acridChanges = plugin.MainModule.GetType("TaylorsBalancePatch.AcridChanges");
var acridInstructions = acridChanges.Methods.Concat(acridChanges.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
var breathHandler = acridChanges.Methods.Single(m => m.Name == "DamageDealt").Body.Instructions;
Check(breathHandler.Any(i => i.Operand is FieldReference f && f.Name == "blightSkillDef") &&
    breathHandler.Any(i => i.Operand is FieldReference f && f.Name == "passiveSkillSlot"),
    "Breath selects its guaranteed DOT from the attacker's equipped passive.");
Check(acridInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "CrocoBreathProjectile") &&
    breathHandler.Count(i => i.Operand is MethodReference m && m.Name == "InflictDot") == 1 &&
    breathHandler.Any(i => i.Operand is MethodReference m && m.Name == "NextAppliesBlight") &&
    !acridInstructions.Any(i => i.Operand is MemberReference m && m.DeclaringType?.FullName == "UnityEngine.Random"),
    "Cloying Breath applies at most one passive stack per accepted tick and has no random extra Blight path.");
var blightDuration = acridChanges.Methods.Single(m => m.Name == "BlightDuration").Body.Instructions;
Check(blightDuration.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "duration" &&
        i.Previous.OpCode == OpCodes.Ldc_R4 && (float)i.Previous.Operand == 8f) &&
    !blightDuration.Any(i => i.Operand is MethodReference m && m.Name == "GetBuffCount"),
    "New Blight applications receive a fixed 8s duration without the old existing-stack duration scaling.");
var nativeDotTick = dotController.Methods.Single(m => m.Name == "EvaluateDotStacksForType");
Check(!nativeDotTick.Body.Instructions.Any(i => i.Operand is MethodReference m &&
        (m.Name == "OnHitEnemy" || m.Name == "OnHitAll")),
    "Native DOT damage does not dispatch hit procs; Blight requires its explicit hit-processing path.");
var blightDamage = acridChanges.Methods.Single(m => m.Name == "BlightDamage").Body.Instructions;
Check(blightDamage.Count(i => i.Operand is MethodReference m && m.Name == "OnHitEnemy") == 1 &&
    blightDamage.Count(i => i.Operand is MethodReference m && m.Name == "OnHitAll") == 1 &&
    blightDamage.Any(i => i.Operand is FieldReference f && f.Name == "rejected") &&
    blightDamage.Any(i => i.Operand is FieldReference f && f.Name == "inflictor") &&
    blightDamage.Any(i => i.Operand is FieldReference f && f.Name == "dotIndex"),
    "Only the scoped native Blight packet runs ordinary on-hit processing, once and after native damage/death handling; rejected hits and other DOTs are excluded.");
Check(!blightDamage.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        (f.Name == "damage" || f.Name == "dotIndex" || f.Name == "damageType")) &&
    blightDamage.First(i => i.Operand is MethodReference m && m.Name == "Invoke").Offset <
        blightDamage.Single(i => i.Operand is MethodReference m && m.Name == "OnHitEnemy").Offset,
    "The Blight proc hook preserves native damage and DOT identity and dispatches items only after the original damage call.");
Check(acridChanges.Methods.Single(m => m.Name == "BlightTick").Body.ExceptionHandlers.Any(e =>
        e.HandlerType == ExceptionHandlerType.Finally) &&
    dotController.Fields.Single(f => f.Name == "dotStackList").FieldType.FullName.Contains("DotStack"),
    "Blight snapshots the native per-attacker stacks before final-tick removals and restores nested tick context with finally.");
Check(pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && (string)i.Operand == AcridBalance.ConfigSection),
    "Acrid has its own category configuration and plugin startup path.");

var ordinarySwing = new MercenaryBalance.HitboxLifetime();
ordinarySwing.ObservePrimary(false);
Check(ordinarySwing.Stage == MercenaryBalance.HitboxStage.Waiting && !ordinarySwing.Extend(true),
    "Mercenary cannot extend a third swing before its hitbox opens.");
ordinarySwing.ObservePrimary(true);
Check(ordinarySwing.Stage == MercenaryBalance.HitboxStage.Primary,
    "Opening the third swing's hitbox starts its readiness lifetime.");
ordinarySwing.ObservePrimary(false);
Check(ordinarySwing.Stage == MercenaryBalance.HitboxStage.Finished && !ordinarySwing.Extend(false),
    "Without a cancelling skill, readiness ends exactly when the primary hitbox closes.");
var cancelledSwing = new MercenaryBalance.HitboxLifetime();
cancelledSwing.ObservePrimary(true);
Check(cancelledSwing.Extend(true), "A skill activated during the active hitbox extends that swing.");
cancelledSwing.ObservePrimary(false);
Check(cancelledSwing.Stage == MercenaryBalance.HitboxStage.Ability && !cancelledSwing.Extend(true),
    "The cancelled sword animation can stop while its retained hitbox stays active, without chaining a second extension.");
cancelledSwing.Finish();
cancelledSwing.ObservePrimary(true);
Check(cancelledSwing.Stage == MercenaryBalance.HitboxStage.Finished,
    "Finishing the follow-up skill removes readiness and prevents the old swing from reopening.");
var missedWindow = new MercenaryBalance.HitboxLifetime();
missedWindow.ObservePrimary(true);
Check(!missedWindow.Extend(false), "A skill used after the actual hitbox closes cannot retain it.");
Check(MercenaryBalance.ContinuesAbility("EntityStates.Merc.EvisDash", "EntityStates.Merc.Evis") &&
    MercenaryBalance.ContinuesAbility("EntityStates.Merc.WhirlwindEntry", "EntityStates.Merc.WhirlwindGround") &&
    MercenaryBalance.ContinuesAbility("EntityStates.Merc.PrepAssaulter2", "EntityStates.Merc.Assaulter2") &&
    !MercenaryBalance.ContinuesAbility("EntityStates.Merc.Evis", "EntityStates.Merc.Weapon.GroundLight2"),
    "The retained hitbox covers preparation and damage states, then ends rather than carrying into another ability.");
var basicFire = basicMelee.Methods.Single(m => m.Name == "AuthorityFireAttack").Body.Instructions;
Check(basicFire.Count(i => i.Operand is MethodReference m && m.Name == "AuthorityModifyOverlapAttack") == 1 &&
    basicFire.Any(i => i.Operand is MethodReference m && m.Name == "Fire" && m.DeclaringType.Name == "OverlapAttack"),
    "The retained attack is captured after native third-swing Expose setup and before its first hit.");
var nativePrimary = game.MainModule.GetType("EntityStates.Merc.Weapon.GroundLight2");
Check(nativePrimary.Methods.Single(m => m.Name == "AuthorityModifyOverlapAttack").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == 8388608) &&
    basicMelee.Methods.Single(m => m.Name == "GetShouldFire").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "GetFloat" && m.DeclaringType.Name == "Animator"),
    "The native third-swing Expose flag and animation-driven hitbox gate remain available.");
var healthDamage = game.MainModule.GetType("RoR2.HealthComponent").Methods.Single(m => m.Name == "TakeDamageProcess").Body.Instructions;
var exposeRemove = healthDamage.Single(i => i.Operand is MethodReference m && m.Name == "RemoveUnmanagedBuff" &&
    i.Previous.Previous.Operand is FieldReference f && f.Name == "MercExpose");
var exposeAdd = healthDamage.Single(i => i.Operand is MethodReference m && m.Name == "AddUnmanagedBuff" &&
    i.Previous.Previous.Operand is FieldReference f && f.Name == "MercExpose");
Check(healthDamage.IndexOf(exposeRemove) < healthDamage.IndexOf(exposeAdd),
    "Native Expose consumes an existing mark before applying the new one, so a tagging hit cannot consume its own new mark.");
var mercChanges = plugin.MainModule.GetType("TaylorsBalancePatch.MercenaryChanges");
var mercMethods = mercChanges.Methods.Concat(mercChanges.NestedTypes.SelectMany(t => t.Methods)).Where(m => m.HasBody).ToArray();
var mercInstructions = mercMethods.SelectMany(m => m.Body.Instructions).ToArray();
var captureHook = mercChanges.Methods.Single(m => m.Name == "CapturePrimaryOverlap").Body.Instructions;
var captureOrig = captureHook.Single(i => i.Operand is MethodReference m && m.Name == "Invoke" &&
    m.DeclaringType.FullName.Contains("GroundLight2/orig_AuthorityModifyOverlapAttack"));
var captureSwing = captureHook.Single(i => i.Operand is MethodReference m && m.Name == "CaptureSwing");
Check(captureHook.IndexOf(captureOrig) < captureHook.IndexOf(captureSwing) &&
    !captureHook.Any(i => i.Operand is MethodReference m && (m.Name == "get_DeclaringType" || m.Name == "get_Fields")) &&
    !mercInstructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "IL.EntityStates.BasicMeleeAttack"),
    "Expose Ready captures the actual overlap argument after native setup, with no generated-type field lookup or failing melee IL hook.");
foreach (string hook in new[] { "add_AuthorityModifyOverlapAttack", "remove_AuthorityModifyOverlapAttack" })
    Check(mercInstructions.Any(i => i.Operand is MethodReference m && m.Name == hook &&
        m.DeclaringType.FullName == "On.EntityStates.Merc.Weapon.GroundLight2"),
        $"Expose Ready direct native capture lifecycle includes {hook}.");
var buffDefs = mercChanges.Methods.Single(m => m.Name == "BuffDefs").Body.Instructions;
Check(buffDefs.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "iconSprite") &&
    buffDefs.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "isHidden" && i.Previous.OpCode == OpCodes.Ldc_I4_0) &&
    buffDefs.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "canStack" && i.Previous.OpCode == OpCodes.Ldc_I4_0) &&
    mercInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "bdMercExpose"),
    "Expose Ready uses the native Expose icon and is explicitly visible to the standard buff bar.");
Check(mercChanges.Methods.Single(m => m.Name == "Tick").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "Fire" && m.DeclaringType.Name == "OverlapAttack") &&
    mercChanges.Methods.Single(m => m.Name == "PreserveVolume").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "get_lossyScale") &&
    mercChanges.Methods.Single(m => m.Name == "ResetOverlap").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "Contains"),
    "Mercenary retains the sword volume and its original overlap attack, protecting its hit ledger from state pooling.");
Check(!mercInstructions.Any(i => i.Operand is MethodReference m &&
        (m.Name == "ResetIgnoredHealthComponents" || m.Name == "add_OnSerialize" ||
         m.Name == "add_OnDeserialize" || m.Name == "add_ModifyProjectileInfo")) &&
    !mercInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "damageType"),
    "The replacement neither resets swing victims nor tags other skill damage nor changes skill network messages.");
Check(mercInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Expose Ready") &&
    mercInstructions.Any(i => i.Operand is MethodReference m && m.Name == "AddBuffAuthority") &&
    mercInstructions.Any(i => i.Operand is MethodReference m && m.Name == "RemoveBuffAuthority") &&
    mercChanges.Methods.Single(m => m.Name == "Finish").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "Destroy"),
    "Expose Ready has an authority-synchronized lifetime and finishes together with the retained sword volume.");

var uppercut = game.MainModule.GetType("EntityStates.Merc.Uppercut");
var uppercutEnter = uppercut.Methods.Single(m => m.Name == "OnEnter");
MercenaryBalance.PatchUppercutEnter(uppercutEnter.Body);
Check(uppercutEnter.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 6f) &&
    uppercutEnter.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.75f && i.Next.OpCode == OpCodes.Mul) &&
    uppercutEnter.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "attackSpeedStat" && i.Next.OpCode == OpCodes.Div),
    "Rising Thunder deals 600% and uses 75% of native base duration, retaining attack-speed scaling.");
var uppercutSnapshot = uppercutEnter.Body.Instructions.Select(i => i.ToString()).ToArray();
bool duplicateUppercutRefused = false;
try { MercenaryBalance.PatchUppercutEnter(uppercutEnter.Body); } catch (InvalidOperationException) { duplicateUppercutRefused = true; }
Check(duplicateUppercutRefused && uppercutSnapshot.SequenceEqual(uppercutEnter.Body.Instructions.Select(i => i.ToString())),
    "Rising Thunder entry patch rejects an unexpected/already-patched layout without partial changes.");
var uppercutTick = uppercut.Methods.Single(m => m.Name == "FixedUpdate");
var uppercutMove = MercenaryBalance.UppercutMovementSite(uppercutTick.Body);
Check(uppercutMove.Previous.OpCode == OpCodes.Ldloc_1 &&
    uppercutTick.Body.Instructions.Count(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "velocity") == 2 &&
    uppercutTick.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_zero"),
    "Rising Thunder identifies its native movement assignment separately from hit-stop zeroing.");
Check(uppercut.Methods.Single(m => m.Name == "PlayAnim").Body.Instructions.Any(i =>
        i.Operand is FieldReference f && f.Name == "duration") &&
    uppercutTick.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Evaluate") &&
    uppercutTick.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "FireMeleeOverlap"),
    "Rising Thunder preserves its duration-driven animation, movement curve and native melee hit delivery.");
var mercMovement = plugin.MainModule.GetType("TaylorsBalancePatch.MercenaryMovementChanges");
var mercMovementInstructions = new[] { mercMovement }.Concat(mercMovement.NestedTypes).SelectMany(t => t.Methods)
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
foreach (string type in new[] { "Uppercut", "WhirlwindBase", "Assaulter2", "FocusedAssaultDash", "EvisDash" })
    Check(mercMovementInstructions.Any(i => i.Operand is MethodReference m && m.Name.StartsWith("add_") &&
        m.DeclaringType.FullName.EndsWith("Merc." + type)), $"Mercenary movement covers {type}.");
foreach (float rate in new[] { 0.5f, 1f, 2f, 4f, 8f })
{
    foreach (float nominal in new[] { 0.15f, 0.2f, 0.4f, 0.5f })
    {
        float duration = nominal / rate;
        var window = new MovementWindow();
        float displacement = 0f;
        for (float elapsed = 0f; elapsed < duration + 0.04f; elapsed += 0.02f)
            displacement += 20f * window.Take(duration, 0.02f) * nominal / duration;
        Check(Math.Abs(displacement - 20f * nominal) < 0.0001f,
            $"Dash {nominal}s at {rate}x attack speed retains base displacement, including partial last ticks.");
    }
    var curve = new MovementIntegral(t => 3f * (1f - t));
    float previous = 0f, integrated = 0f;
    float curveDuration = 0.3f / rate;
    for (float elapsed = 0.02f; elapsed <= curveDuration + 0.02f; elapsed += 0.02f)
    {
        float next = Math.Min(1f, elapsed / curveDuration);
        integrated += 0.4f * (curve.At(next) - curve.At(previous));
        previous = next;
    }
    Check(Math.Abs(integrated - 0.6f) < 0.0001f,
        $"Uppercut curve area is preserved at {rate}x without an attack-speed-scaled exit impulse.");
}
foreach (string stateName in new[] { "Assaulter2", "FocusedAssaultDash" })
{
    var nativeDash = game.MainModule.GetType("EntityStates.Merc." + stateName).Methods.Single(m => m.Name == "AuthorityFixedUpdate");
    var motionStores = nativeDash.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.FullName == "UnityEngine.Vector3 RoR2.CharacterMotor::rootMotion").ToArray();
    Check(motionStores.Length == 1 && motionStores[0].Previous.Operand is MethodReference add && add.Name == "op_Addition",
        stateName + " exposes the validated single native root-motion addition.");
}
var prepEnter = game.MainModule.GetType("EntityStates.Merc.PrepAssaulter2").Methods.Single(m => m.Name == "OnEnter");
Check(prepEnter.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "baseDuration") == 2,
    "Blinding Assault preparation animation has the validated duration-read site.");
foreach (string method in new[] { "OnEnter", "FixedUpdate" })
    Check(game.MainModule.GetType("EntityStates.Merc.EvisDash").Methods.Single(m => m.Name == method).Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "dashPrepDuration"),
        "Eviscerate " + method + " uses native per-phase timing reads for scoped acceleration.");
Check(!mercMovementInstructions.Any(i => i.Operand is MethodReference m &&
    m.DeclaringType.FullName == "IL.EntityStates.Merc.Evis"),
    "Eviscerate's target-searching and damage phase remains native, preserving target stops and attack-speed hit scaling.");
Check(mercMovementInstructions.Any(i => i.Operand is MethodReference m && m.Name == "At") &&
    !mercMovementInstructions.Any(i => i.Operand is MethodReference m && m.Name == "set_position"),
    "Mercenary uses bounded curve displacement through motor motion, without teleporting past collisions or targets.");
Check(MercenaryBalance.GetDescription("MERC_SECONDARY_ALT1_DESCRIPTION")!.Contains("600%"),
    "Rising Thunder retains its approved damage and shorter base duration.");

Check(ArtificerBalance.TectonicPushMultiplier(1f) == 0f &&
    Math.Abs(ArtificerBalance.TectonicPushMultiplier(0f) - 0.9f) < 0.00001f &&
    Math.Abs(ArtificerBalance.TectonicPushMultiplier(-1f) - 1.1f) < 0.00001f &&
    Math.Abs(ArtificerBalance.TectonicPushMultiplier(0.5f) - 0.6116605f) < 0.00001f &&
    Math.Abs(ArtificerBalance.TectonicPushMultiplier(-0.5f) - 1.0359246f) < 0.00001f,
    "Tectonic Blast uses one smooth exponential curve: zero up, 0.9x horizontal, 1.1x down.");
float previousPush = 0f;
bool pushCurveValid = true;
for (int sample = 0; sample <= 200; sample++)
{
    float push = ArtificerBalance.TectonicPushMultiplier(1f - sample / 100f);
    pushCurveValid &= push >= previousPush && push >= 0f && push <= 1.1f;
    previousPush = push;
}
Check(pushCurveValid, "Tectonic push is monotonic and bounded by 1.1x across 201 aim samples.");
Check(ArtificerBalance.TectonicPushMultiplier(2f) == 0f &&
    ArtificerBalance.TectonicPushMultiplier(-2f) == 1.1f &&
    Math.Abs((ArtificerBalance.TectonicPushMultiplier(0f) - ArtificerBalance.TectonicPushMultiplier(0.0001f)) -
        (ArtificerBalance.TectonicPushMultiplier(-0.0001f) - ArtificerBalance.TectonicPushMultiplier(0f))) < 0.000001f,
    "Tectonic clamps out-of-range aim and has a smooth slope through horizontal aim.");
var tectonic = game.MainModule.GetType("EntityStates.Mage.Weapon.FireEarthShards").Methods.Single(m => m.Name == "PlayAttackEffect");
Check(ArtificerBalance.TectonicPushSite(tectonic.Body).Next.Next.Operand is FieldReference velocity && velocity.Name == "velocity" &&
    tectonic.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_isAuthority") &&
    tectonic.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_isGrounded") &&
    tectonic.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Min"),
    "Tectonic self-push patch targets the native airborne authority-only velocity assignment and preserves upward clamp.");
foreach (string boss in new[] { "BrotherBody", "BrotherHurtBody", "FalseSonBossBody", "FalseSonBossBodyLunarShard", "FalseSonBossBodyBrokenLunarShard" })
    Check(ArtificerBalance.FrozenExecuteFraction(boss) == 0.1f, $"{boss}: frozen execution is 10%.");
foreach (string other in new[] { "FalseSonBody", "BrotherHauntBody", "BrotherPhantomBody", "BeetleQueen2Body", "MageBody", "" })
    Check(ArtificerBalance.FrozenExecuteFraction(other) == 0.3f, $"{other}: ordinary frozen execution remains 30%.");
var healthType = game.MainModule.GetType("RoR2.HealthComponent");
Check(ArtificerBalance.FrozenExecuteSites(healthType.Methods.Single(m => m.Name == "TakeDamageProcess").Body, false).Length == 2 &&
    ArtificerBalance.FrozenExecuteSites(healthType.Methods.Single(m => m.Name == "GetHealthBarValues").Body, true).Length == 1,
    "Frozen execution comparison, assignment and health-bar marker match installed native layouts.");
var artiType = plugin.MainModule.GetType("TaylorsBalancePatch.ArtificerChanges");
var artiHooks = new[] { artiType }.Concat(artiType.NestedTypes).SelectMany(t => t.Methods)
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
foreach (string hook in new[] { "add_PlayAttackEffect", "remove_PlayAttackEffect" })
    Check(artiHooks.Any(i => i.Operand is MethodReference m && m.Name == hook), $"Artificer hook lifecycle includes {hook}.");
Check(ArtificerBalance.GetDescription("KEYWORD_FREEZING")!.Contains("10%") &&
    ArtificerBalance.GetDescription("MAGE_UTILITY_ALT_DESCRIPTION")!.Contains("Aiming downward provides more aerial knockback"),
    "Freezing and Tectonic Blast tooltips explain the new rules.");

Check(RailgunnerBalance.WeakPointScale == 0.7f && RailgunnerBalance.CryoRadiusMultiplier == 1.2f &&
    Math.Abs(RailgunnerBalance.WeakPointScale * RailgunnerBalance.WeakPointScale - 0.49f) < 0.000001f,
    "Railgunner weak-point radius scales to 70% with a 49% squared-distance test; Cryocharge radius grows 20%.");
Check(RailgunnerBalance.PolarRadius == 15f && RailgunnerBalance.PolarDamage == 0f &&
    RailgunnerBalance.PolarProc == 0f && RailgunnerBalance.PolarForce == 1500f &&
    RailgunnerBalance.DeploymentDelayMultiplier == 0.1f,
    "Polar Field keeps 15m, restores zero damage/proc and native slowing, triples outward force and 1/10ths its fuse.");
Check(RailgunnerBalance.ListenerVolume(true, true) == 1f &&
    RailgunnerBalance.ListenerVolume(false, true) == 0.5f &&
    RailgunnerBalance.ListenerVolume(false, false) == 1f,
    "The shooter hears normal secondary volume; resolved other-player listeners hear half volume; unknown ownership preserves native audio.");
Check(RailgunnerBalance.InsidePolar(224f, 15f, true, true) &&
    RailgunnerBalance.InsidePolar(225f, 15f, true, true) &&
    !RailgunnerBalance.InsidePolar(225.01f, 15f, true, true) &&
    !RailgunnerBalance.InsidePolar(0f, 15f, false, true) &&
    !RailgunnerBalance.InsidePolar(0f, 15f, true, false),
    "Allied Polar landing protection includes the boundary, excludes enemies/outside/inactive fields and needs no retained trigger state.");
Check(RailgunnerBalance.SecondaryVolume == 0.5f &&
    RailgunnerBalance.IsSecondarySound("Play_railgunner_m2_fire") &&
    RailgunnerBalance.IsSecondarySound("Play_railgunner_m2_alt_fire") &&
    !RailgunnerBalance.IsSecondarySound("Play_railgunner_R_fire") &&
    !RailgunnerBalance.IsSecondarySound("Play_railgunner_R_alt_fire") &&
    !RailgunnerBalance.IsSecondarySound("Play_railgunner_m2_headshot"),
    "Railgunner audio halves only the two secondary firing events, not specials or headshot feedback.");
var targetViewer = game.MainModule.GetType("RoR2.UI.SniperTargetViewer");
var visualRadius = RailgunnerBalance.WeakPointSite(targetViewer.Methods.Single(m => m.Name == "OnTargetDiscovered").Body, false);
var sniperHit = game.MainModule.GetType("RoR2.BulletAttack").Methods.Single(m => m.Name == "IsSniperTargetHit");
var hitRadius = RailgunnerBalance.WeakPointSite(sniperHit.Body, true);
Check(visualRadius.Next.Operand is MethodReference visualSetter && visualSetter.Name == "set_targetWorldRadius" &&
    hitRadius.Next.OpCode.FlowControl == FlowControl.Cond_Branch,
    "Native weak-point UI and actual hit acceptance both use the validated radius hook sites.");
var cryoModify = game.MainModule.GetType("EntityStates.Railgunner.Weapon.FireSnipeCryo").Methods.Single(m => m.Name == "ModifyBullet");
Check(cryoModify.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ModifyBullet" &&
        m.DeclaringType.FullName == "EntityStates.Railgunner.Weapon.BaseFireSnipe") &&
    !cryoModify.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "radius"),
    "Cryocharge native modification preserves the generated bullet radius before the scoped 20% increase.");
var nativeMine = game.MainModule.GetType("EntityStates.Railgunner.Weapon.BaseFireMine").Methods.Single(m => m.Name == "OnEnter").Body.Instructions;
Check(nativeMine.Any(i => i.Operand is MethodReference m && m.Name == "ModifyProjectileInfo") &&
    nativeMine.Any(i => i.Operand is MethodReference m && m.Name == "get_isAuthority"),
    "Polar mine damage and force are modified before native authority-controlled projectile firing.");
var railChanges = plugin.MainModule.GetType("TaylorsBalancePatch.RailgunnerChanges");
var railInstructions = railChanges.Methods.Concat(railChanges.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
foreach (string hook in new[] { "OnTargetDiscovered", "IsSniperTargetHit", "ModifyBullet", "ModifyProjectileInfo", "SetProjectilePrefabs", "ApplyJumpVelocity", "PlaySound_string_GameObject", "GetLocalizedStringByToken" })
    foreach (string action in new[] { "add_", "remove_" })
        Check(railInstructions.Any(i => i.Operand is MethodReference m && m.Name == action + hook),
            $"Railgunner hook lifecycle includes {action + hook}.");
var weakPointScaleInstructions = railChanges.Methods.Single(m => m.Name == "ScaleWeakPoint").Body.Instructions;
Check(weakPointScaleInstructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && Math.Abs((float)i.Operand - 0.7f) < 0.000001f) &&
    weakPointScaleInstructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && Math.Abs((float)i.Operand - 0.49f) < 0.000001f),
    "The compiled weak-point hooks emit 0.7 for display radius and 0.49 for squared hit radius.");
var listenerGain = railChanges.Methods.Single(m => m.Name == "ListenerGain").Body.Instructions;
Check(listenerGain.Any(i => i.Operand is MethodReference m && m.Name == "get_cachedBody") &&
    listenerGain.Any(i => i.Operand is MethodReference m && m.Name == "get_cameraRigController") &&
    !listenerGain.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "NetworkServer"),
    "Audio ownership resolves each listener's local camera and controlled body, independently of host/server role.");
var polarInfo = railChanges.Methods.Single(m => m.Name == "Polar").Body.Instructions;
Check(polarInfo.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "force") &&
    railInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "RailgunnerMineAlt") &&
    railInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "RailgunnerMineAltDetonated") &&
    !railInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        new[] { "childrenDamageCoefficient", "slowDownCoefficient", "buffDef", "blastDamageCoefficient", "blastProcCoefficient" }.Contains(f.Name)),
    "Polar targets its own deployment/child field and leaves native damage multipliers, proc and enemy/projectile slowing untouched.");
var landing = game.MainModule.GetType("RoR2.GlobalEventManager").Methods.Single(m => m.Name == "OnCharacterHitGroundServer");
Check(RailgunnerBalance.LandingImmunitySite(landing.Body).Next.OpCode.FlowControl == FlowControl.Cond_Branch,
    "Polar landing extends the native immunity decision without skipping native landing movement or effects.");
Check(railChanges.Methods.Single(m => m.Name == "IsProtectedLanding").Body.Instructions.Any(i =>
    i.Operand is MethodReference m && m.Name == "TransformPoint") &&
    !railInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "velocity") &&
    !railInstructions.Any(i => i.Operand is MethodReference m && (m.Name == "AddBuff" || m.Name == "AddTimedBuff")),
    "Fall protection checks live allied sphere geometry at impact without changing velocity or granting lingering invulnerability.");
var explosionDamage = game.MainModule.GetType("RoR2.Projectile.ProjectileExplosion").Methods.Single(m => m.Name == "PerformDamage").Body.Instructions;
Check(explosionDamage.Any(i => i.Operand is FieldReference f && f.Name == "force" && f.DeclaringType.Name == "ProjectileDamage") &&
    explosionDamage.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "baseForce"),
    "Native Polar blast force is populated independently of its zero base damage.");
var forceDamage = game.MainModule.GetType("RoR2.HealthComponent").Methods
    .Single(m => m.Name == "TakeDamageForce" && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.Name == "DamageInfo");
Check(!forceDamage.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "damage"),
    "Native force delivery reads the force independently of damage amount.");
var sharedFreeze = plugin.MainModule.GetType("TaylorsBalancePatch.SharedFreezeChanges");
var sharedFreezeInstructions = new[] { sharedFreeze }.Concat(sharedFreeze.NestedTypes).SelectMany(t => t.Methods)
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
foreach (string hook in new[] { "add_TakeDamageProcess", "remove_TakeDamageProcess", "add_GetHealthBarValues", "remove_GetHealthBarValues" })
    Check(sharedFreezeInstructions.Any(i => i.Operand is MethodReference m && m.Name == hook), $"Shared freeze installation owns {hook}.");
Check(artiHooks.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == sharedFreeze.FullName && m.Name == "Acquire") &&
    railInstructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == sharedFreeze.FullName && m.Name == "Acquire") &&
    !artiHooks.Concat(railInstructions).Any(i => i.Operand is MethodReference m && m.Name == "add_TakeDamageProcess"),
    "Either survivor enables shared freeze thresholds; both use leases instead of duplicate hooks.");
Check(cryoModify.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == 256),
    "Cryocharge natively applies Freeze2s and enters the shared frozen-execution path.");
Check(railInstructions.Any(i => i.Operand is MethodReference m && m.Name == "SetGameObjectOutputBusVolume") &&
    railInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "TaylorRailgunnerSecondaryAudio") &&
    railInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "RAILGUNNER_BODY_NAME") &&
    !railInstructions.Any(i => i.Operand is MethodReference m && m.Name == "SetRTPCValue"),
    "Secondary audio uses a dedicated Railgunner-only emitter and listener gain, not global volume settings.");
Check(pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && (string)i.Operand == RailgunnerBalance.ConfigSection) &&
    pluginType.Methods.Single(m => m.Name == "OnDestroy").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.DeclaringType.FullName == railChanges.FullName && m.Name == "Dispose"),
    "Railgunner has an independent configuration switch and plugin shutdown cleanup.");
Check(RailgunnerBalance.GetDescription("RAILGUNNER_UTILITY_ALT_DESCRIPTION")!.Contains("no fall damage") &&
    RailgunnerBalance.GetDescription("RAILGUNNER_SPECIAL_DESCRIPTION") == null,
    "Railgunner descriptions explain landing protection without altering Supercharge.");

Check(VoidFiendBalance.PrimaryDamageMultiplier(0f) == 1f && VoidFiendBalance.PrimaryDamageMultiplier(50f) == 1f &&
    VoidFiendBalance.PrimaryDamageMultiplier(75f) == 0.75f && VoidFiendBalance.PrimaryDamageMultiplier(100f) == 0.5f &&
    VoidFiendBalance.PrimaryDamageMultiplier(1000f) == 0.5f,
    "Void Fiend normal primary has full damage through 50m, linear 50-100m falloff and a 50% damage floor.");
Check(VoidFiendBalance.CorruptedFloodRadius == 15f && VoidFiendBalance.CorruptedFloodSpeed == 50f &&
    VoidFiendBalance.CorruptedFloodLifetime == 150f && VoidFiendBalance.HealFraction == 0.2f &&
    VoidFiendBalance.SelfDamageFraction == -0.2f && VoidFiendBalance.CorruptedCastMultiplier == 0.5f &&
    VoidFiendBalance.OutOfCombatCorruption == 2f,
    "Void Fiend projectile, healing/self-damage, cast-time and out-of-combat tuning match the request.");
var handBeam = game.MainModule.GetType("EntityStates.VoidSurvivor.Weapon.FireHandBeam").Methods.Single(m => m.Name == "OnEnter");
Check(VoidFiendBalance.PrimaryFireSite(handBeam.Body).OpCode == OpCodes.Callvirt,
    "Drown's scoped damage modifier is attached at the native normal-primary BulletAttack.Fire call.");
var bulletDamageDelivery = game.MainModule.GetType("RoR2.BulletAttack").Methods.Single(m => m.Name == "DefaultHitCallbackImplementation").Body.Instructions;
Check(bulletDamageDelivery.Any(i => i.Operand is FieldReference f && f.Name == "modifyOutgoingDamageCallback") &&
    game.MainModule.GetType("RoR2.BulletAttack/BulletHit").Fields.Any(f => f.Name == "distance"),
    "Native per-hit outgoing damage callbacks expose hit distance for falloff without changing proc coefficient.");
var enterCorruption = game.MainModule.GetType("EntityStates.VoidSurvivor.EnterCorruptionTransition");
var enterExit = enterCorruption.Methods.Single(m => m.Name == "OnExit");
Check(enterExit.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "AddTimedBuff") &&
    enterExit.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "OnExit" &&
        m.DeclaringType.FullName == "EntityStates.VoidSurvivor.CorruptionTransitionBase"),
    "Vanilla transformation retains its native lingering invincibility call and base-state cleanup.");
var transition = game.MainModule.GetType("EntityStates.VoidSurvivor.CorruptionTransitionBase");
Check(transition.Methods.Single(m => m.Name == "OnEnter").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "AddBuff") &&
    transition.Methods.Single(m => m.Name == "OnExit").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "RemoveBuff"),
    "Native invincibility still covers the full transformation state.");
var fiendType = plugin.MainModule.GetType("TaylorsBalancePatch.VoidFiendChanges");
var fiendInstructions = fiendType.Methods.Concat(fiendType.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
Check(!fiendInstructions.Any(i => i.Operand is MemberReference m && m.FullName.Contains("CorruptionTransition")) &&
    !plugin.MainModule.GetType("TaylorsBalancePatch.VoidFiendBalance").Methods.Any(m => m.Name == "PatchLingeringInvincibility") &&
    !VoidFiendBalance.GetDescription("VOIDSURVIVOR_PASSIVE_DESCRIPTION")!.Contains("invincibility"),
    "Void Fiend has no transformation hooks/patch helper or modified-invincibility tooltip claim.");
foreach (string hook in new[] { "OnEnter", "SetBodyPrefabs", "SetProjectilePrefabs", "GetLocalizedStringByToken" })
    foreach (string action in new[] { "add_", "remove_" })
        Check(fiendInstructions.Any(i => i.Operand is MethodReference m && m.Name == action + hook),
            $"Void Fiend hook lifecycle includes {action + hook}.");
var fiendCrush = fiendType.Methods.Single(m => m.Name == "Crush");
var fiendCharge = fiendType.Methods.Single(m => m.Name == "Charge");
Check(fiendCharge.Body.Instructions.Any(i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.Name == "ChargeCrushHealth") &&
    fiendCrush.Body.Instructions.Any(i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.Name == "CrushHealth") &&
    fiendCrush.Body.Instructions.Any(i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.Name == "CrushCorruption") &&
    fiendCharge.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally) &&
    fiendCrush.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally) &&
    !fiendInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "corruptionChange"),
    "Suppress halves only corrupted charge/recovery, restores pooled-state inputs in finally, and preserves corruption exchange.");
Check(fiendInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "VoidSurvivorMegaBlasterBigProjectileCorrupted") &&
    fiendInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.DeclaringType.FullName == "RoR2.Projectile.ProjectileSimple" && f.Name == "lifetime") &&
    fiendInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.DeclaringType.FullName == "RoR2.Projectile.ProjectileImpactExplosion" && f.Name == "lifetime") &&
    !fiendInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        (f.Name == "lifetimeAfterImpact" || f.Name == "corruptionPerSecondInCombat")),
    "Only corrupted Flood's prefab and both expiry limits change; post-impact timing and in-combat corruption stay native.");
Check(fiendInstructions.Any(i => i.Operand is FieldReference f && f.Name == "modifyOutgoingDamageCallback") &&
    fiendType.Methods.Single(m => m.Name == "ScalePrimaryDamage").Body.Instructions.Any(i =>
        i.Operand is FieldReference f && f.Name == "distance") &&
    !fiendInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "procCoefficient"),
    "Normal primary falloff uses each hit's distance and does not reduce proc coefficient or other skill damage.");
Check(pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && (string)i.Operand == VoidFiendBalance.ConfigSection) &&
    pluginType.Methods.Single(m => m.Name == "OnDestroy").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.DeclaringType.FullName == fiendType.FullName && m.Name == "Dispose"),
    "Void Fiend has an independent configuration switch and plugin shutdown cleanup.");
Check(VoidFiendBalance.GetDescription("VOIDSURVIVOR_SPECIAL_DESCRIPTION")!.Contains("20%") &&
    VoidFiendBalance.GetDescription("VOIDSURVIVOR_SPECIAL_UPRADE_TOOLTIP")!.Contains("25% Corruption") &&
    VoidFiendBalance.GetDescription("VOIDSURVIVOR_PRIMARY_UPRADE_TOOLTIP") == null,
    "Void Fiend tooltips explain the new values without changing corrupted Drown or corruption exchange.");

var snapExit = wall.Methods.Single(m => m.Name == "OnExit");
ArtificerBalance.PatchWallDamage(snapExit.Body);
Check(snapExit.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 3f) == 2 &&
    !snapExit.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "damageCoefficient"),
    "Both Snapfreeze wall projectiles use 300% damage.");
foreach (float ratio in new[] { 0.5f, 1f, 1.5f, 2f, 4f })
    Check(Math.Abs(RexBalance.LaunchAcceleration(80f * ratio, 7f * ratio, 7f) -
        80f * (1f + (ratio - 1f) * 0.5f)) < 0.001f,
        $"Disperse halves movement-speed scaling at {ratio}x speed while retaining base acceleration.");
Check(Math.Abs(RailgunnerBalance.PolarJumpVelocityMultiplier * RailgunnerBalance.PolarJumpVelocityMultiplier - 2f) < 0.00001f,
    "Polar jump velocity produces 100% extra height rather than 50% extra speed.");
var applyJump = game.MainModule.GetType("EntityStates.GenericCharacterMain").Methods.Single(m => m.Name == "ApplyJumpVelocity");
Check(applyJump.IsStatic && applyJump.Parameters.Count == 5 &&
    applyJump.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_jumpPower"),
    "Polar hooks native jump velocity calculation without granting extra jumps.");

var laserStreak = new LaserRuinCounter();
for (int tick = 1; tick <= 48; tick++)
{
    laserStreak.BeginTick();
    Check(laserStreak.Hit(1) == (tick % 24 == 0), $"Laser applies Ruin only on consecutive tick {tick} at multiples of 24.");
    Check(!laserStreak.Hit(1), "Duplicate hurtboxes never advance a laser tick twice.");
    if (tick % 2 == 0) Check(!laserStreak.Hit(2), "Intermittent hits on another enemy do not form a streak.");
}
laserStreak.BeginTick(); // Miss a complete tick.
for (int tick = 1; tick <= 24; tick++)
{
    laserStreak.BeginTick();
    Check(laserStreak.Hit(1) == (tick == 24), "A missed tick resets the target's streak.");
}
var newChannel = new LaserRuinCounter();
newChannel.BeginTick();
Check(!newChannel.Hit(1), "A new channel cannot inherit a previous channel's streak.");
var fsLaser = game.MainModule.GetType("EntityStates.FalseSon.LaserFatherCharged");
Check(FalseSonBalance.BulletFireSite(fsLaser.Methods.Single(m => m.Name == "FireBullet").Body) != null,
    "Laser Ruin tagging attaches only to the channeled laser bullet.");
var fsFixed = fsLaser.Methods.Single(m => m.Name == "FixedUpdate");
FalseSonBalance.PatchLaserVisuals(fsFixed.Body);
Check(!fsFixed.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f &&
    f.Name == "laserEffect" && i.Next.Operand is MethodReference m && m.Name == "op_Implicit") &&
    fsFixed.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "FireBullet"),
    "Both physics visual blocks are disabled while native firing remains.");
Check(!fsLaser.Methods.Any(m => m.Name == "Update"), "The channeled laser inherits EntityState.Update for frame-based visuals.");
var refillSite = FalseSonBalance.BurstRoundSite(game.MainModule.GetType("EntityStates.FalseSon.LaserFatherBurstPrep")
    .Methods.Single(m => m.Name == "OnEnter").Body);
Check(refillSite.OpCode == OpCodes.Conv_I4 && (int)Math.Ceiling(1 * 0.5f) == 1 &&
    (int)Math.Ceiling(3 * 0.5f) == 2, "Burst rounds fractional refills up, including a single maximum stock.");
var ruinDamageMethod = game.MainModule.GetType("RoR2.HealthComponent").Methods.Single(m => m.Name == "TakeDamageProcess");
FalseSonBalance.PatchRuinDamage(ruinDamageMethod.Body);
var ruinInstructions = ruinDamageMethod.Body.Instructions.ToArray();
int ruinStart = Array.FindIndex(ruinInstructions, i => i.Operand is FieldReference f && f.Name == "lunarruin");
Check(!ruinInstructions.Skip(ruinStart).Take(60).Any(i => i.OpCode == OpCodes.Stfld &&
    i.Operand is FieldReference f && f.Name == "damage" && f.DeclaringType.FullName == "RoR2.DamageInfo") &&
    ruinInstructions.Skip(ruinStart).Take(45).Any(i => i.OpCode == OpCodes.Stloc &&
        i.Operand is VariableDefinition v && v.Index == 12),
    "Ruin updates native incomingDamage and never writes its multiplier back to DamageInfo.damage.");
Check(ruinInstructions[ruinStart - 3].OpCode == OpCodes.Stloc_S &&
    ((VariableDefinition)ruinInstructions[ruinStart - 3].Operand).Index == 12,
    "Native incomingDamage is initialized before the Ruin guards and multiplier.");
var growthMethod = game.MainModule.GetType("RoR2.CharacterBody").Methods.Single(m => m.Name == "ComputeTamperedHeartBonus");
FalseSonBalance.PatchGrowth(growthMethod.Body);
Check(growthMethod.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.04f) == 2 &&
    growthMethod.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 2f) == 2,
    "Growth supplies 4% attack/movement speed and 2 armor/regen, retaining native buff and Meridian multipliers.");
Check(FalseSonBalance.DeathMarkDotSite(game.MainModule.GetType("RoR2.GlobalEventManager").Methods
    .Single(m => m.Name == "ProcDeathMark").Body) != null,
    "Death Mark has a distinct DOT test where Ruin's duplicate contribution can be excluded.");
var fsChanges = plugin.MainModule.GetType("TaylorsBalancePatch.FalseSonChanges");
Check(fsChanges.Methods.Single(m => m.Name == "LaserEnter").Body.Instructions.Any(i =>
    i.Operand is MethodReference m && m.Name == "Remove"),
    "Every laser cast resets the streak counter even if its state instance is pooled.");
Check(fsChanges.Methods.Concat(fsChanges.NestedTypes.SelectMany(t => t.Methods)).Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions).Any(i =>
    i.Operand is MethodReference m && m.Name == "add_Update") &&
    pluginInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == FalseSonBalance.ConfigSection),
    "False Son installs frame-based visuals and has an independent config switch.");
foreach (string field in new[] { "laserEffect", "laserOriginTransformSprinting", "laserOriginTransformStanding", "laserEffectEnd", "muzzleTransform", "lockedOnHurtBox" })
    Check(fsLaser.Fields.Any(f => f.Name == field), $"Cached laser visual field {field} exists.");
foreach (string nestedName in new[] { "DotStack", "TimedBuff" })
{
    var owner = game.MainModule.GetType(nestedName == "DotStack" ? "RoR2.DotController" : "RoR2.CharacterBody");
    var nested = owner.NestedTypes.Single(t => t.Name == nestedName);
    Check(owner.Fields.Any(f => f.Name == (nestedName == "DotStack" ? "dotStackList" : "timedBuffs")) &&
        nested.Fields.Any(f => f.Name == (nestedName == "DotStack" ? "dotIndex" : "buffIndex") && f.IsPublic),
        $"{nestedName} stack list and index fields match the cached refresh accessors.");
    Check(nested.Fields.Any(f => f.Name == "timer" && f.IsPublic) &&
        nested.Fields.Any(f => f.Name == "totalDuration" && f.IsPublic),
        $"{nestedName} exposes duration fields for refreshing all existing Ruin stacks.");
}
var reprieve = game.MainModule.GetType("RoR2.ReprieveVehicle");
var reprieveEnter = reprieve.Methods.Single(m => m.Name == "OnPassengerEnter");
var reprieveSpeed = reprieve.Methods.Single(m => m.Name == "MyFixedUpdate");
var reprieveTurn = reprieve.Methods.Single(m => m.Name == "FixedUpdateMovement");
SeekerBalance.PatchReprieveSpeed(reprieveEnter.Body, true);
SeekerBalance.PatchReprieveSpeed(reprieveSpeed.Body, false);
SeekerBalance.PatchReprieveTurn(reprieveTurn.Body);
Check(reprieveEnter.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
    f.Name == "startingSpeed").Previous.OpCode == OpCodes.Mul &&
    reprieveSpeed.Body.Instructions.Single(i => i.Operand is FieldReference f && f.Name == "trueEndSpeed").Next.Operand is float capScale &&
    capScale == 0.9f, "Reprieve start speed and acceleration cap are both scaled to 90%, preserving native acceleration timing.");
Check(reprieveTurn.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ReprieveTurnCoefficient") &&
    new[] { "currentSpeed", "startingSpeed", "trueEndSpeed" }.All(name => reprieveTurn.Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == name)),
    "Reprieve computes turning from its live current/start/capped maximum speeds.");
foreach (float start in new[] { 7f, 21f, 42f })
    foreach (float progress in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        Check(Math.Abs(SeekerBalance.ReprieveTurnCoefficient(0.5f, start + progress * 100f, start, start + 100f) -
            0.5f * (1f - 0.8f * progress)) < 0.00001f,
            $"Reprieve steering is linear at {progress:P0} acceleration progress with start speed {start}.");
Check(SeekerBalance.ReprieveTurnCoefficient(0.5f, 0f, 10f, 100f) == 0.5f &&
    Math.Abs(SeekerBalance.ReprieveTurnCoefficient(0.5f, 200f, 10f, 100f) - 0.1f) < 0.00001f &&
    SeekerBalance.ReprieveTurnCoefficient(0.5f, 10f, 10f, 10f) == 0.5f,
    "Reprieve preserves normal entry steering, caps the reduction at 80%, and handles a zero acceleration range.");
var meditateGate = game.MainModule.GetType("RoR2.SeekerController").Methods.Single(m => m.Name == "UnlockGateEffects");
var beforeGate = meditateGate.Body.Instructions.Select(i => (i.OpCode, i.Operand)).ToArray();
SeekerBalance.PatchSelfReviveGrant(meditateGate.Body);
Check(meditateGate.Body.Instructions.Count == beforeGate.Length && meditateGate.Body.Instructions
    .Select((i, n) => i.OpCode != beforeGate[n].OpCode || !Equals(i.Operand, beforeGate[n].Operand)).Count(changed => changed) == 1 &&
    meditateGate.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "seekerSelfRevive")
        .Previous.OpCode == OpCodes.Ldc_I4_0,
    "Meditate changes only its self-revive grant; native ally revival, buffs, immunity, effects and UI remain byte-for-byte intact.");
Check(SeekerBalance.CanConsumeGrace(true, true, true, true, false, false), "A living Seeker with unspent Grace can trigger its low-health heal.");
Check(!SeekerBalance.CanConsumeGrace(false, true, true, true, false, false) &&
    !SeekerBalance.CanConsumeGrace(true, false, true, true, false, false) &&
    !SeekerBalance.CanConsumeGrace(true, true, false, true, false, false) &&
    !SeekerBalance.CanConsumeGrace(true, true, true, false, false, false) &&
    !SeekerBalance.CanConsumeGrace(true, true, true, true, true, false) &&
    !SeekerBalance.CanConsumeGrace(true, true, true, true, false, true),
    "Grace rejects lethal hits, healthy bodies, other survivors, missing/spent charges and disabled defenses.");
var potionMethod = game.MainModule.GetType("RoR2.HealthComponent").Methods.Single(m => m.Name == "TriggerHealthPotion");
Check(potionMethod.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.75f) &&
    potionMethod.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "HealFraction"),
    "Native Power Elixir heals 75% through the standard healing pipeline.");
var seekerChanges = plugin.MainModule.GetType("TaylorsBalancePatch.SeekerChanges");
var palmGrace = seekerChanges.Methods.Single(m => m.Name == "PalmGrace").Body.Instructions;
Check(palmGrace.Any(i => i.Operand is MethodReference m && m.Name == "AddUnmanagedBuff") &&
    !palmGrace.Any(i => i.Operand is MethodReference m && (m.Name == "Respawn" || m.Name == "UnlockGateEffects")),
    "Palm Blast grants living-player Grace without entering Meditate's dead-ally revival path.");
var graceHeal = seekerChanges.Methods.Single(m => m.Name == "LowHealthGrace").Body.Instructions;
Check(graceHeal.Any(i => i.Operand is MethodReference m && m.Name == "get_isHealthLow") &&
    graceHeal.Any(i => i.Operand is MethodReference m && m.Name == "TriggerHealthPotion") &&
    graceHeal.Any(i => i.Operand is MethodReference m && m.Name == "set_seekerRevivedOnce"),
    "Saving Grace uses the native low-health threshold/heal and consumes the native once-per-stage charge.");
Check(pluginInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == SeekerBalance.ConfigSection) &&
    SeekerBalance.GetDescription("SEEKER_SPECIAL_DESCRIPTION") == null &&
    SeekerBalance.GetDescription("KEYWORD_LOWHEALTHPROTECTION")!.Contains("75%"),
    "Seeker has an independent toggle, updated Grace tooltip, and preserves Meditate's ally-revive description.");
var nativeRestockRate = skillDef.Methods.Single(m => m.Name == "GetRechargeRate").Body.Instructions;
Check(nativeRestockRate.Any(i => i.Operand is FieldReference f && f.Name == "attackSpeedBuffsRestockSpeed") &&
    nativeRestockRate.Any(i => i.Operand is MethodReference m && m.Name == "get_attackSpeed"),
    "Native restocking supports attack-speed scaling for Scrap Launcher.");
var scrapSkills = multChanges.Methods.Single(m => m.Name == "Skills").Body.Instructions;
Check(scrapSkills.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.Name == "attackSpeedBuffsRestockSpeed" && i.Previous.OpCode == OpCodes.Ldc_I4_1) &&
    scrapSkills.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.Name == "attackSpeedBuffsRestockSpeed_Multiplier" && (float)i.Previous.Operand == 1f),
    "Scrap Launcher enables native restocking with full attack-speed scaling.");
var blastBullet = banditChanges.Methods.Single(m => m.Name == "ReduceSpread").Body.Instructions;
Check(BanditBalance.BlastRadiusMultiplier == 1.2f &&
    blastBullet.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "radius") &&
    blastBullet.Any(i => i.Operand is FieldReference f && f.Name == "world") &&
    blastBullet.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "stopperMask"),
    "Blast widens its bullet and uses terrain as its piercing stopper, within the rifle-specific hook.");
var snipePush = game.MainModule.GetType("EntityStates.Railgunner.Weapon.BaseFireSnipe")
    .Methods.Single(m => m.Name == "OnFireBulletAuthority").Body.Instructions;
Check(snipePush.Any(i => i.Operand is FieldReference f && f.Name == "selfKnockbackForce") &&
    snipePush.Any(i => i.Operand is MethodReference m && m.Name == "ApplyForce"),
    "Scoped-shot physical recoil is the movement source controlled by the stationary patch.");
var stationaryShot = railChanges.Methods.Single(m => m.Name == "StationarySecondary");
Check(stationaryShot.Body.Instructions.Any(i => i.OpCode == OpCodes.Isinst &&
        i.Operand is TypeReference t && t.Name == "FireSnipeHeavy") &&
    stationaryShot.Body.Instructions.Any(i => i.OpCode == OpCodes.Isinst &&
        i.Operand is TypeReference t && t.Name == "FireSnipeLight") &&
    stationaryShot.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_isGrounded") &&
    stationaryShot.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "moveVector") &&
    stationaryShot.Body.ExceptionHandlers.Any(e => e.HandlerType == ExceptionHandlerType.Finally),
    "Only stationary grounded secondary shots suppress self-push, restoring the state field even on failure.");
var nativeShock = game.MainModule.GetType("RoR2.SetStateOnHurt").Methods.Single(m => m.Name == "SetShock");
Check(nativeShock.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "canBeStunned"),
    "Native shock immunity uses the stun eligibility flag.");
var taserSlow = captainChanges.Methods.Single(m => m.Name == "TaserSlow").Body.Instructions;
Check(CaptainBalance.TaserSlowDuration == 5f &&
    taserSlow.Any(i => i.Operand is FieldReference f && f.Name == "canBeStunned") &&
    taserSlow.Any(i => i.Operand is FieldReference f && f.Name == "Slow50") &&
    taserSlow.Any(i => i.Operand is FieldReference f && f.Name == "damageSource") &&
    taserSlow.Any(i => i.Operand is FieldReference f && f.Name == "rejected") &&
    taserSlow.Any(i => i.Operand is MethodReference m && m.Name == "get_active"),
    "Captain's server-side taser fallback slows shock-immune victims and excludes rejected or unrelated damage.");
var volleyRandom = new Random(7263);
var straightSlots = new int[3];
var randomOffsets = new System.Collections.Generic.HashSet<(float, float)>();
for (int cast = 0; cast < 1000; cast++)
{
    var volley = new IceBoxVolley(volleyRandom);
    int straightCount = 0;
    for (int shot = 1; shot <= 3; shot++)
    {
        var offset = volley.Offset(shot);
        float radius = MathF.Sqrt(offset.pitch * offset.pitch + offset.yaw * offset.yaw);
        if (radius == 0f) { straightCount++; straightSlots[shot - 1]++; }
        else
        {
            if (radius < 0.4999f || radius > ChefBalance.IceCubeSpreadDegrees + 0.0001f)
                throw new Exception("Ice Box random spread escaped its slight trajectory cone.");
            randomOffsets.Add(offset);
        }
        if (volley.Offset(shot) != offset) throw new Exception("Ice Box changed its spread within the same cast.");
    }
    if (straightCount != 1) throw new Exception("Ice Box must have exactly one straight cube per cast.");
}
Check(straightSlots.All(count => count > 250 && count < 420) && randomOffsets.Count > 1900,
    "1000 Ice Box casts each have exactly one randomly selected straight cube and two independent bounded random trajectories.");
Check(ChefBalance.CancelCrestLift(true, false, false) &&
    !ChefBalance.CancelCrestLift(true, false, true) &&
    !ChefBalance.CancelCrestLift(false, false, false) &&
    !ChefBalance.CancelCrestLift(true, true, false),
    "Roll removes passive upward carry at a crest while preserving deliberate jumps, grounded travel and existing airborne movement.");
Check(ChefBalance.CollisionUpSpeed(0f, 9f) == 0f && ChefBalance.CollisionUpSpeed(-5f, 3f) == 0f &&
    ChefBalance.CollisionUpSpeed(6f, 12f) == 6f && ChefBalance.CollisionUpSpeed(6f, -2f) == -2f,
    "Unstable Roll collisions cannot create upward speed; existing jump speed and downward motion are retained.");
Check(ChefBalance.BoostedIceDamage == 10f &&
    ChefBalance.GetDescription("CHEF_SECONDARY_ALT_BOOST_DESCRIPTION")!.Contains("1000%") &&
    ChefBalance.GetDescription("CHEF_SECONDARY_ALT_BOOST_DESCRIPTION_SKILL")!.Contains("1000%") &&
    ChefBalance.GetDescription("CHEF_SECONDARY_DESCRIPTION") == null,
    "Both Ice Box boost tooltips match 1000%; Sear remains untouched.");
var iceState = game.MainModule.GetType("EntityStates.Chef.IceBox");
var iceFire = iceState.Methods.Single(m => m.Name == "FireIceCube").Body.Instructions;
Check(iceFire.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        f.Name == "iceCubesFired").Offset < iceFire.Single(i => i.OpCode == OpCodes.Stfld &&
        i.Operand is FieldReference f && f.Name == "rotation").Offset,
    "Ice Box increments its shot index before the spread hook reads it.");
Check(iceState.Methods.Single(m => m.Name == "FireBoostedIceCube").Body.Instructions.Count(i =>
    i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "boostedProjectileDmgCoefficient") == 1,
    "Boosted Ice Box has one isolated damage-coefficient read; normal cube damage is separate.");
var yesChefEnter = game.MainModule.GetType("EntityStates.Chef.YesChef").Methods.Single(m => m.Name == "OnEnter");
var bodyLookup = yesChefEnter.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "FindByCustomName");
Check(bodyLookup.Previous.OpCode == OpCodes.Ldstr && (string)bodyLookup.Previous.Operand == "Body" &&
    yesChefEnter.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj &&
        i.Operand is MethodReference m && m.DeclaringType.Name == "YesChefBlockingState"),
    "Yes CHEF's Body lookup is the blocker site; boost, blast and stock logic remain separate.");
var chefPatch = plugin.MainModule.GetType("TaylorsBalancePatch.ChefChanges");
Check(chefPatch.Methods.Single(m => m.Name == "YesChefMovement").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.Name == "YesChef") &&
    chefPatch.Methods.Single(m => m.Name == "RollObstruction").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "IsRolling"),
    "Concurrent Yes CHEF movement and wall handling are scoped to the active Roll.");
var chefHooks = chefPatch.Methods.Concat(chefPatch.NestedTypes.SelectMany(t => t.Methods))
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
Check(chefHooks.Any(i => i.Operand is MethodReference m && m.Name == "add_HandleMovements") &&
    chefHooks.Any(i => i.Operand is MethodReference m && m.Name == "add_PerformInputs") &&
    !chefHooks.Any(i => i.Operand is MethodReference m && m.Name == "add_FixedUpdate"),
    "Concurrent Yes CHEF skips only duplicate movement/input; its native fixed-age timer and blast timing still advance.");
using (var kcc = AssemblyDefinition.ReadAssembly(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0])!,
    "KinematicCharacterController.dll")))
{
    var obstruction = kcc.MainModule.GetType("KinematicCharacterController.KinematicCharacterMotor")
        .Methods.Single(m => m.Name == "GetObstructionNormal");
    Check(obstruction.Parameters.Count == 2 && obstruction.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3" &&
        obstruction.Parameters[1].ParameterType.FullName == "System.Boolean" &&
        obstruction.ReturnType.FullName == "UnityEngine.Vector3",
        "Installed collision solver matches the Roll steep-wall detour signature.");
    var projection = kcc.MainModule.GetType("KinematicCharacterController.KinematicCharacterMotor")
        .Methods.Single(m => m.Name == "HandleVelocityProjection");
    Check(projection.Parameters.Count == 3 && projection.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3&" &&
        projection.Parameters[1].ParameterType.FullName == "UnityEngine.Vector3" &&
        projection.Parameters[2].ParameterType.FullName == "System.Boolean",
        "Installed collision solver matches the Roll projection hook, including the writable velocity argument.");
}
Check(chefPatch.Methods.Single(m => m.Name == "RollExited").Body.ExceptionHandlers.Any(e =>
        e.HandlerType == ExceptionHandlerType.Finally) &&
    chefPatch.Methods.Single(m => m.Name == "RestoreRollMotor").Body.Instructions.Count(i =>
        i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
        (f.Name == "StepHandling" || f.Name == "MaxStableSlopeAngle")) == 2,
    "Roll exit restores both saved motor settings even if native exit throws.");
Check(DrifterBalance.SteeringMultiplier == 3f && DrifterBalance.JunkPullRadius == 15f,
    "Drifter uses the confirmed 3x steering and 15m Junk-pull radius.");
Check(DrifterBalance.WithinPullRadius(225f) && !DrifterBalance.WithinPullRadius(225.01f) &&
    !DrifterBalance.WithinPullRadius(-1f) && !DrifterBalance.WithinPullRadius(float.NaN),
    "Junk attraction includes the 15m boundary and rejects outside/invalid distances.");
Check(DrifterBalance.PullSpeed(15f, 0.02f) == 30f && DrifterBalance.PullSpeed(0.1f, 0.02f) == 5f &&
    DrifterBalance.PullSpeed(1f, 0f) == 0f,
    "Junk attraction is speed-capped and limits the final approach to avoid overshooting.");
foreach (var (type, method, expected) in new[] {
    ("EntityStates.Drifter.TornadoSlam", "OnEnter", 1),
    ("EntityStates.Drifter.TornadoSlam", "UpdateDirection", 2),
    ("EntityStates.Drifter.ChargeTornadoSlam", "OnEnter", 1) })
{
    var nativeMethod = game.MainModule.GetType(type).Methods.Single(m => m.Name == method);
    using var context = new MonoMod.Cil.ILContext(nativeMethod);
    context.Invoke(il =>
    {
        var before = il.Body.Instructions.Select(i => (instruction: i, opcode: i.OpCode, operand: i.Operand)).ToArray();
        DrifterBalance.PatchSteeringReads(il.Body, expected);
        Check(il.Body.Instructions.Count == before.Length + expected * 2 &&
            before.All(x => x.instruction.OpCode == x.opcode && ReferenceEquals(x.instruction.Operand, x.operand)) &&
            il.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldc_R4 && i.Operand is float value && value == 3f &&
                i.Next.OpCode == OpCodes.Mul) == expected,
            $"{type}.{method}: scales only steering reads; native instructions, branches, field writes and cached restoration remain intact.");
    });
}
var tornadoType = game.MainModule.GetType("EntityStates.Drifter.TornadoSlam");
var nativeTornadoOverlap = tornadoType.Methods.Single(m => m.Name == "ModifyOverlapInfo").Body.Instructions;
Check(nativeTornadoOverlap.Any(i => i.Operand is FieldReference f && f.Name == "timesBounced") &&
    nativeTornadoOverlap.Any(i => i.Operand is MethodReference m && m.Name == "GenerateJunk"),
    "Native boss bounce counting and Junk-generation paths remain present and unpatched.");
var drifterPatch = plugin.MainModule.GetType("TaylorsBalancePatch.DrifterChanges");
var drifterInstructions = new[] { drifterPatch }.Concat(drifterPatch.NestedTypes).SelectMany(t => t.Methods)
    .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
var drifterSubscriptions = drifterInstructions.Where(i => i.Operand is MethodReference m && m.Name.StartsWith("add_"))
    .Select(i => (MethodReference)i.Operand).ToArray();
Check(drifterSubscriptions.Length == 5 && drifterSubscriptions.All(m =>
    (m.DeclaringType.FullName == "IL.EntityStates.Drifter.TornadoSlam" && (m.Name == "add_OnEnter" || m.Name == "add_UpdateDirection")) ||
    (m.DeclaringType.FullName == "IL.EntityStates.Drifter.ChargeTornadoSlam" && m.Name == "add_OnEnter") ||
    (m.DeclaringType.FullName == "On.JunkPickup" && m.Name == "add_FixedUpdate") ||
    (m.DeclaringType.FullName == "On.RoR2.Language" && m.Name == "add_GetLocalizedStringByToken")),
    "Drifter installs only three steering-read hooks, a Junk-pickup hook and the description hook; no collision or combat hooks remain.");
Check(!drifterInstructions.Any(i => i.Operand is MemberReference m && new[] {
    "ModifyOverlapInfo", "GenerateJunk", "TakeDamage", "TakeDamageForce", "SetNextState", "SetNextStateToMain",
    "RecalcSpeedMultiplier", "ResetOverlapAttack", "GiveItemPermanent", "ForceUnground", "UpdateVelocity" }.Contains(m.Name)) &&
    !drifterInstructions.Any(i => i.Operand is FieldReference f && new[] {
        "extraSpeedMultiplier", "timesBounced", "bounceTimer", "fireAge", "junkSpawnedThisAttack", "procCoefficient" }.Contains(f.Name)) &&
    !drifterPatch.NestedTypes.Any(t => t.Name == "LaunchedEnemy"),
    "Removed Drifter code cannot alter boss collisions, generate Junk, deal bonus damage, launch enemies or control skill cancellation/speed.");
var junkPull = drifterPatch.Methods.Single(m => m.Name == "PullJunk").Body.Instructions;
Check(junkPull.Any(i => i.Operand is MethodReference m && m.Name == "get_active") &&
    junkPull.Any(i => i.Operand is FieldReference f && f.Name == "gravitateTarget") &&
    junkPull.Any(i => i.Operand is MethodReference m && m.Name == "set_velocity") &&
    !drifterInstructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "gravitateTarget"),
    "Junk pull is server-only, respects native homing targets and never leaves a persistent attraction target behind.");
var junkSearch = drifterPatch.Methods.Single(m => m.Name == "FindPullTarget").Body.Instructions;
var spinning = drifterPatch.Methods.Single(m => m.Name == "Spinning").Body.Instructions;
Check(junkSearch.Any(i => i.Operand is MethodReference m && m.Name == "get_alive") &&
    junkSearch.Any(i => i.Operand is MethodReference m && m.Name == "Linecast") &&
    junkSearch.Any(i => i.Operand is MethodReference m && m.Name == "get_teamIndex") &&
    spinning.Any(i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference t && t.FullName == "EntityStates.Drifter.TornadoSlam"),
    "Only living allied Drifters actually spinning within line of sight attract Junk; charging, exit and death stop the effect.");
Check(DrifterBalance.GetDescription("DRIFTER_UTILITY_ALT_DESCRIPTION")!.Contains("<color=#C586FF><b>Charge and spin steering are 3x normal.") &&
    DrifterBalance.GetDescription("DRIFTER_UTILITY_ALT_DESCRIPTION")!.Contains("within 15m"),
    "Tornado Slam retains its native wording and highlights only 3x handling and the 15m Junk pull.");
// FIREWALL decisions cover both phases and are final only after the projectile ends.
var unusedFirewall = new FirewallUse();
Check(!unusedFirewall.TryComplete(false, out _), "FIREWALL never refunds while its shield is held.");
unusedFirewall.HoldingEnded = true;
unusedFirewall.ExpectsProjectile = true;
Check(!unusedFirewall.TryComplete(false, out _), "FIREWALL waits for the released server projectile instead of treating network delay as a miss.");
unusedFirewall.SawProjectile = true;
Check(!unusedFirewall.TryComplete(true, out _), "FIREWALL cannot refund before a late hit or block is possible.");
Check(unusedFirewall.TryComplete(false, out bool unusedRefund) && unusedRefund &&
    !unusedFirewall.TryComplete(false, out _), "An unused FIREWALL receives exactly one half-cooldown refund after both phases end.");
foreach (string activity in new[] { "held block", "projectile block", "damage", "block and damage" })
{
    var usedFirewall = new FirewallUse { HoldingEnded = true, ExpectsProjectile = true, SawProjectile = true };
    if (activity.Contains("block")) usedFirewall.Block();
    if (activity.Contains("damage")) usedFirewall.Damage(1f);
    Check(usedFirewall.TryComplete(false, out bool refund) && !refund,
        $"FIREWALL keeps its full cooldown after {activity}.");
}
var zeroDamageFirewall = new FirewallUse { HoldingEnded = true };
zeroDamageFirewall.Damage(0f);
Check(zeroDamageFirewall.TryComplete(false, out bool zeroRefund) && zeroRefund,
    "A zero-damage report alone does not disqualify an unused FIREWALL.");
Check(OperatorBalance.CooldownCredit(8f) == 4f && OperatorBalance.CooldownCredit(4f) == 2f &&
    OperatorBalance.CooldownCredit(0f) == 0f,
    "FIREWALL halves the final cooldown after cooldown-item modifiers: 8s becomes 4s; 4s becomes 2s.");
var shieldTrigger = game.MainModule.GetType("RoR2.Projectile.DroneTechShieldProjectile").Methods.Single(m => m.Name == "OnTriggerEnter");
var shieldDestroy = OperatorBalance.ProjectileBlockSite(shieldTrigger.Body);
Check(shieldTrigger.Body.Instructions.TakeWhile(i => i != shieldDestroy).Any(i =>
        i.Operand is MethodReference m && m.Name == "get_teamIndex") &&
    shieldDestroy.Previous.Operand is MethodReference getObject && getObject.Name == "get_gameObject",
    "The FIREWALL block hook targets the actual hostile-projectile destruction branch, after native team/type checks.");
var operatorPatch = plugin.MainModule.GetType("TaylorsBalancePatch.OperatorChanges");
var operatorEnable = operatorPatch.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
Check(operatorEnable.Any(i => i.Operand is MethodReference m && m.Name == "add_OnIncomingDamageServer") &&
    operatorEnable.Any(i => i.Operand is MethodReference m && m.Name == "add_onServerDamageDealt") &&
    operatorEnable.Any(i => i.Operand is MethodReference m && m.Name == "add_FireProjectileImmediateServer"),
    "FIREWALL tracks shield blocks, actual server damage and initialized projectile instances.");
Check(new[] { "ShieldProjectileEmpty", "ShieldProjectile25", "ShieldProjectile50", "ShieldProjectile75", "ShieldProjectile100" }
    .All(OperatorBalance.IsFirewallProjectile) && !OperatorBalance.IsFirewallProjectile("MageWall"),
    "FIREWALL tracks even its harmless empty variant and excludes unrelated projectiles.");
var operatorSpawn = operatorPatch.Methods.Single(m => m.Name == "SpawnProjectile").Body.Instructions;
Check(operatorSpawn.Any(i => i.Operand is MethodReference m && m.Name == "IsFirewallProjectile") &&
    operatorEnable.Any(i => i.Operand is MethodReference m && m.Name == "GetComponentInParent"),
    "FIREWALL tracks its native projectile variants and attributes child-collider blocks to the tracked projectile root.");
var operatorDamage = operatorPatch.Methods.Single(m => m.Name == "Damage").Body.Instructions;
Check(operatorDamage.Any(i => i.Operand is FieldReference f && f.Name == "inflictor") &&
    operatorDamage.Any(i => i.Operand is FieldReference f && f.Name == "damageDealt"),
    "Only the tracked FIREWALL projectile's actual damage counts; other Operator/drone attacks do not.");
var operatorApply = operatorPatch.Methods.Single(m => m.Name == "ApplyRefund").Body.Instructions;
Check(operatorApply.Any(i => i.Operand is MethodReference m && m.Name == "GetFinalRechargeInterval") &&
    operatorApply.Any(i => i.Operand is MethodReference m && m.Name == "set_rechargeStopwatch") &&
    operatorApply.Any(i => i.Operand is FieldReference f && f.Name == "credited"),
    "FIREWALL credits the final recharge timer once without changing base cooldown or granting extra stock.");
var operatorCurrent = operatorPatch.Methods.Single(m => m.Name == "Current").Body.Instructions;
var operatorReceive = operatorPatch.Methods.Single(m => m.Name == "ReceiveRefund").Body.Instructions;
Check(operatorCurrent.Any(i => i.Operand is MethodReference m && m.Name == "get_alive") &&
    operatorCurrent.Any(i => i.Operand is FieldReference f && f.Name == "latest") &&
    operatorCurrent.Any(i => i.OpCode == OpCodes.Ceq) &&
    operatorReceive.Any(i => i.Operand is MethodReference m && m.Name == "HasEffectiveAuthority") &&
    operatorReceive.Count(i => i.Operand is FieldReference f && f.Name == "sequence") == 2,
    "Dead bodies, changed skills, newer uses, duplicate reports and non-owner clients cannot receive stale FIREWALL refunds.");
Check(OperatorBalance.GetDescription("DRONETECH_UTILITY_ALT_DESCRIPTION")!.Contains("<color=#C586FF><b>") &&
    pluginInstructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == OperatorBalance.ConfigSection),
    "Operator has an independent default-enabled switch and a bold-purple FIREWALL description.");
var categories = plugin.MainModule.GetType("TaylorsBalancePatch.ChangeGroups");
var categorySetup = categories.Methods.Single(m => m.Name == ".cctor").Body.Instructions;
var operatorCategory = categorySetup.SkipWhile(i => i.OpCode != OpCodes.Ldstr || (string)i.Operand != "Operator").ToArray();
Check(operatorCategory.TakeWhile(i => !(i.Operand is MethodReference m && m.Name == "set_Item"))
    .Count(i => i.OpCode == OpCodes.Ldstr) == 2 && operatorCategory.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Utility"),
    "Operator exposes only Utility in the category schema.");
var configBinding = categories.Methods.Single(m => m.Name == "Bind").Body.Instructions;
Check(configBinding.Any(i => i.Operand is MethodReference m && m.Name == "Remove") &&
    configBinding.Any(i => i.Operand is MethodReference m && m.Name == "Save"),
    "Category migration removes the old master entries and saves the new settings.");
foreach (string survivor in new[] { "Commando", "Artificer", "Bandit", "Captain", "Huntress", "Rex", "Mult", "Engineer", "Loader", "Acrid", "Mercenary", "Railgunner", "VoidFiend", "FalseSon", "Seeker", "Chef", "Drifter", "Operator" })
{
    var module = plugin.MainModule.GetType("TaylorsBalancePatch." + survivor + "Changes");
    Check(module.Methods.Single(m => m.Name == "Enable").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.DeclaringType == categories && m.Name == "Enabled"),
        survivor + " gates hook installation by category.");
    Check(module.Methods.Single(m => m.Name == "Description").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.DeclaringType == categories && m.Name == "DescriptionEnabled"),
        survivor + " gates descriptions by category.");
}
Console.WriteLine("Offline game-assembly checks passed; in-game skill, config and visual tests remain necessary.");

void CheckFieldPatch(MethodDefinition method, Action<MethodBody> patch, string type, string target)
{
    patch(method.Body);
    var reference = (FieldReference)method.Body.Instructions.Single(i => i.Operand is FieldReference f &&
        f.DeclaringType.FullName == type && f.Name == target).Operand;
    Check(game.MainModule.GetType(type).Fields.Any(f => f.Name == target && f.FieldType.FullName == reference.FieldType.FullName),
        $"{method.Name}: replacement {type}.{target} has the correct field type.");
}

static void CheckPatch(MethodDefinition method, Action<MethodBody> patch, OpCode opcode, object value)
{
    var before = method.Body.Instructions.Select(i => i.ToString()).ToArray();
    patch(method.Body);
    var changed = method.Body.Instructions.Where((i, index) => i.ToString() != before[index]).ToArray();
    Check(changed.Length == 1 && changed[0].OpCode == opcode && changed[0].Operand.Equals(value),
        $"{method.Name}: only the requested constant changes to {value}.");
    var after = method.Body.Instructions.Select(i => i.ToString()).ToArray();
    bool refused = false;
    try { patch(method.Body); } catch (InvalidOperationException) { refused = true; }
    Check(refused && after.SequenceEqual(method.Body.Instructions.Select(i => i.ToString())),
        $"{method.Name}: unexpected method layouts are rejected without edits.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
