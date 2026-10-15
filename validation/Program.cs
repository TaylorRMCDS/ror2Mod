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
Check(!pluginInstructions.Any(i => i.Operand is MemberReference m && m.FullName.Contains("FirePistol2")),
    "The plugin has no Double Tap damage hook.");
Check(CommandoBalance.GetDescription("COMMANDO_PRIMARY_NAME") == null &&
    CommandoBalance.GetDescription("COMMANDO_PRIMARY_DESCRIPTION") == null &&
    CommandoBalance.GetDescription("COMMANDO_UTILITY_ALT_DESCRIPTION") == null,
    "Double Tap and Tactical Slide text are untouched.");
Check(!pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions
    .Where(i => i.OpCode == OpCodes.Ldstr).Any(i => ((string)i.Operand).Contains("10000%")), "Old test initialization is gone.");
Check(pluginInstructions.Any(i => i.Operand is MethodReference m && m.Name == "Bind") &&
    pluginType.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Ldstr && ((string)i.Operand).Contains("Commando changes DISABLED")),
    "The plugin binds its config and includes the disabled startup path.");
Check(pluginInstructions.Any(i => i.Operand is MethodReference m && m.Name == "AddTimedBuff"), "Dive uses the timed buff system.");
Check(pluginInstructions.Any(i => i.Operand is MethodReference m && m.Name == "get_active" && m.DeclaringType.Name == "NetworkServer"),
    "Server authority guards the buff and grenade physics.");

var wall = game.MainModule.GetType("EntityStates.Mage.Weapon.PrepWall");
CheckFieldPatch(wall.Methods.Single(m => m.Name == "FixedUpdate"), ArtificerBalance.PatchWallInput, "RoR2.InputBankTest", "skill4");
CheckFieldPatch(wall.Methods.Single(m => m.Name == "OnExit"), ArtificerBalance.PatchWallRefund, "RoR2.SkillLocator", "special");
var fly = game.MainModule.GetType("EntityStates.Mage.FlyUpState");
var movement = fly.Methods.Single(m => m.Name == "HandleMovements");
ArtificerBalance.PatchLaunchSpeed(movement.Body);
Check(!movement.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "moveSpeedStat") &&
    movement.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 7f),
    "Ion Surge uses the base 7 m/s factor without reading movement speed.");
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
Check(banditBase.Methods.Single(m => m.Name == "OnEnter").Body.Instructions.Any(i =>
    i.Operand is FieldReference f && f.Name == "minimumBaseDuration"),
    "Bandit's attack-speed-scaled minimum shot interval is patched before state entry.");
var rifle = game.MainModule.GetType("EntityStates.Bandit2.Weapon.Bandit2FireRifle");
Check(rifle.BaseType.FullName == "EntityStates.Bandit2.Weapon.Bandit2FirePrimaryBase" &&
    rifle.Methods.Single(m => m.Name == "ModifyBullet").Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.Name == "ModifyBullet"),
    "Blast is isolated through its rifle-specific state while Burst keeps the shared base state.");
var skillDef = game.MainModule.GetType("RoR2.Skills.SkillDef");
Check(skillDef.Fields.Any(f => f.Name == "baseMaxStock" && f.FieldType.FullName == "System.Int32") &&
    skillDef.Fields.Any(f => f.Name == "skillNameToken" && f.FieldType.FullName == "System.String"),
    "Bandit Blast stock can be changed by locating its SkillDef token.");
Check(BanditBalance.BlastBaseCharges == 6 && BanditBalance.BlastMinimumBaseInterval == 0.2f &&
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
var fruitInstructions = rexHandler.Body.Instructions;
var healingWrite = fruitInstructions.Single(i => i.OpCode == OpCodes.Stfld &&
    i.Operand is FieldReference f && f.FullName == "System.Single RoR2.HealthPickup::fractionalHealing");
var fruitSpawn = fruitInstructions.Single(i => i.Operand is MethodReference m &&
    m.DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && m.Name == "Spawn");
Check(fruitInstructions.IndexOf(healingWrite) < fruitInstructions.IndexOf(fruitSpawn) &&
    game.MainModule.GetType("RoR2.HealthPickup").Methods.Single(m => m.Name == "OnTriggerStay")
        .Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "fractionalHealing"),
    "Spawned Fruiting pickup healing is configured before network spawn and consumed by the game's pickup handler.");
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
