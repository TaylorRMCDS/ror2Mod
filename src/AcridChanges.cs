using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates.Croco;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class AcridChanges : IDisposable
{
    [ThreadStatic]
    private static int breathProjectileSetupDepth;

    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private readonly Dictionary<Spawn, CharacterBody> protectedSpawns = new();
    private SkillDef? neurotoxin;
    private SkillDef? frenziedLeap;
    private float originalNeurotoxinCooldown;
    private int originalFrenziedLeapStock;
    private float originalFrenziedLeapCooldown;
    private GameObject? breathProjectilePrefab;
    private ConditionalWeakTable<ProjectileController, ConditionalWeakTable<HealthComponent, BreathTickSequence>> breathTicks = new();
    private FieldInfo? dotStacksField;
    private FieldInfo? stackIndexField, stackAttackerField;
    [ThreadStatic]
    private static BlightTickContext? activeBlightTick;

    internal AcridChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            dotStacksField = typeof(DotController).GetField("dotStackList", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("DotController.dotStackList");
            var stackType = dotStacksField.FieldType.GetGenericArguments()[0];
            stackIndexField = stackType.GetField("dotIndex") ?? throw new MissingFieldException("DotStack.dotIndex");
            stackAttackerField = stackType.GetField("attackerObject") ?? throw new MissingFieldException("DotStack.attackerObject");
            Add(() => On.RoR2.Skills.SkillCatalog.SetSkillDefs += SkillDefs,
                () => On.RoR2.Skills.SkillCatalog.SetSkillDefs -= SkillDefs);
            if (ChangeGroups.Enabled("Acrid", "Special")) Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += Projectiles,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= Projectiles);
            if (ChangeGroups.Enabled("Acrid", "Passive")) Add(() => On.RoR2.DotController.InflictDot_refInflictDotInfo += BlightDuration,
                () => On.RoR2.DotController.InflictDot_refInflictDotInfo -= BlightDuration);
            if (ChangeGroups.Enabled("Acrid", "Passive")) Add(() => On.RoR2.DotController.EvaluateDotStacksForType += BlightTick,
                () => On.RoR2.DotController.EvaluateDotStacksForType -= BlightTick);
            if (ChangeGroups.Enabled("Acrid", "Passive")) Add(() => On.RoR2.HealthComponent.TakeDamage += BlightDamage,
                () => On.RoR2.HealthComponent.TakeDamage -= BlightDamage);
            if (ChangeGroups.Enabled("Acrid", "Stats")) Add(() => On.EntityStates.Croco.Spawn.OnEnter += ProtectSpawn,
                () => On.EntityStates.Croco.Spawn.OnEnter -= ProtectSpawn);
            if (ChangeGroups.Enabled("Acrid", "Stats")) Add(() => On.EntityStates.Croco.Spawn.OnExit += EndSpawnProtection,
                () => On.EntityStates.Croco.Spawn.OnExit -= EndSpawnProtection);
            if (ChangeGroups.Enabled("Acrid", "Utility")) Add(() => On.EntityStates.Croco.ChainableLeap.GetBlastDamageType += FrenziedLeapDamageType,
                () => On.EntityStates.Croco.ChainableLeap.GetBlastDamageType -= FrenziedLeapDamageType);
            if (ChangeGroups.Enabled("Acrid", "Special")) Add(() => On.EntityStates.Croco.Breath.FireProjectile += BreathProjectile,
                () => On.EntityStates.Croco.Breath.FireProjectile -= BreathProjectile);
            if (ChangeGroups.Enabled("Acrid", "Special")) Add(() => On.RoR2.CrocoDamageTypeController.GetDamageType += CrocoDamageType,
                () => On.RoR2.CrocoDamageTypeController.GetDamageType -= CrocoDamageType);
            if (ChangeGroups.Enabled("Acrid", "Special")) Add(() => GlobalEventManager.onServerDamageDealt += DamageDealt,
                () => GlobalEventManager.onServerDamageDealt -= DamageDealt);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Acrid changes ENABLED: spawn protection, non-Poisonous Vicious Wounds finisher, 8s Blight with 0.1 proc per stack/capped 1.0 per tick, Neurotoxin cooldown 2.5s, one-charge Poisonous Frenzied Leap, and Cloying Breath Poison every tick or Blight on ticks 1/5/9 with no bonus roll.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void Add(Action install, Action remove)
    {
        install();
        cleanup.Add(remove);
    }

    private void Fail(Exception error)
    {
        Dispose();
        log.LogError($"Acrid setup failed; restored its values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        if (NetworkServer.active)
            foreach (var body in protectedSpawns.Values)
                if (body) body.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
        protectedSpawns.Clear();
        if (neurotoxin != null) neurotoxin.baseRechargeInterval = originalNeurotoxinCooldown;
        if (frenziedLeap != null)
        {
            frenziedLeap.baseMaxStock = originalFrenziedLeapStock;
            frenziedLeap.baseRechargeInterval = originalFrenziedLeapCooldown;
        }
        neurotoxin = null;
        frenziedLeap = null;
        breathProjectilePrefab = null;
        breathProjectileSetupDepth = 0;
        breathTicks = new();
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"Acrid hook cleanup failed: {error}"); }
        cleanup.Clear();
    }

    private void SkillDefs(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] skillDefs)
    {
        try
        {
            if (neurotoxin == null || frenziedLeap == null)
            {
                var neurotoxinSkill = skillDefs.Single(s => s.skillNameToken == "CROCO_SECONDARY_NAME");
                var frenziedLeapSkill = skillDefs.Single(s => s.skillNameToken == "CROCO_UTILITY_ALT1_NAME");
                originalNeurotoxinCooldown = neurotoxinSkill.baseRechargeInterval;
                originalFrenziedLeapStock = frenziedLeapSkill.baseMaxStock;
                originalFrenziedLeapCooldown = frenziedLeapSkill.baseRechargeInterval;
                neurotoxin = neurotoxinSkill;
                frenziedLeap = frenziedLeapSkill;
                if (ChangeGroups.Enabled("Acrid", "Secondary")) neurotoxinSkill.baseRechargeInterval = AcridBalance.NeurotoxinCooldown;
                if (ChangeGroups.Enabled("Acrid", "Utility"))
                {
                    frenziedLeapSkill.baseMaxStock = AcridBalance.FrenziedLeapStock;
                    frenziedLeapSkill.baseRechargeInterval = AcridBalance.FrenziedLeapCooldown;
                }
            }
        }
        catch (Exception error) { Fail(error); }
        orig(skillDefs);
    }

    private void Projectiles(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!breathProjectilePrefab)
            {
                var prefab = prefabs.Single(p => p.name == "CrocoBreathProjectile");
                if (!prefab.GetComponent<ProjectileController>() || !prefab.GetComponent<ProjectileDotZone>())
                    throw new InvalidOperationException("Cloying Breath's projectile layout changed.");
                breathProjectilePrefab = prefab;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private static void BlightDuration(On.RoR2.DotController.orig_InflictDot_refInflictDotInfo orig,
        ref InflictDotInfo info)
    {
        if (info.dotIndex == DotController.DotIndex.Blight) info.duration = AcridBalance.BlightBaseDuration;
        orig(ref info);
    }

    private void BlightTick(On.RoR2.DotController.orig_EvaluateDotStacksForType orig,
        DotController self, DotController.DotIndex index, float deltaTime, out int remainingActive)
    {
        if (!NetworkServer.active || index != DotController.DotIndex.Blight)
        {
            orig(self, index, deltaTime, out remainingActive);
            return;
        }
        // Capture before native evaluation removes stacks on their final damage tick.
        var stacks = (System.Collections.IEnumerable)dotStacksField!.GetValue(self);
        var context = new BlightTickContext(self.gameObject);
        foreach (var stack in stacks)
        {
            if ((DotController.DotIndex)stackIndexField!.GetValue(stack) != DotController.DotIndex.Blight) continue;
            var attacker = (GameObject)stackAttackerField!.GetValue(stack);
            if (attacker) context.budget.AddStack(attacker);
            else context.budget.AddUnattributedStack();
        }
        var previous = activeBlightTick;
        activeBlightTick = context;
        try { orig(self, index, deltaTime, out remainingActive); }
        finally { activeBlightTick = previous; }
    }

    private static void BlightDamage(On.RoR2.HealthComponent.orig_TakeDamage orig,
        HealthComponent self, DamageInfo info)
    {
        var tick = activeBlightTick;
        bool isTick = NetworkServer.active && tick != null &&
            info.dotIndex == DotController.DotIndex.Blight && info.inflictor == tick.controllerObject;
        if (isTick && info.attacker)
        {
            info.procCoefficient = tick!.budget.TakeProc(info.attacker);
        }
        orig(self, info);
        // Native DOT evaluation only calls TakeDamage; dispatch ordinary hit
        // processing once per native Blight packet, after native death handling.
        if (isTick && !info.rejected && info.attacker && info.procCoefficient > 0f && GlobalEventManager.instance)
        {
            GlobalEventManager.instance.OnHitEnemy(info, self.gameObject);
            GlobalEventManager.instance.OnHitAll(info, self.gameObject);
        }
    }

    private sealed class BlightTickContext
    {
        internal readonly GameObject controllerObject;
        internal readonly BlightProcBudget<GameObject> budget = new();
        internal BlightTickContext(GameObject controllerObject) => this.controllerObject = controllerObject;
    }

    private void ProtectSpawn(On.EntityStates.Croco.Spawn.orig_OnEnter orig, Spawn self)
    {
        orig(self);
        var body = self.outer ? self.outer!.GetComponent<CharacterBody>() : null;
        if (NetworkServer.active && body && !protectedSpawns.ContainsKey(self))
        {
            body!.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
            protectedSpawns.Add(self, body);
        }
    }

    private void EndSpawnProtection(On.EntityStates.Croco.Spawn.orig_OnExit orig, Spawn self)
    {
        if (protectedSpawns.TryGetValue(self, out var body))
        {
            protectedSpawns.Remove(self);
            if (NetworkServer.active && body)
            {
                body.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
                if (body.healthComponent && body.healthComponent.alive)
                    body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, AcridBalance.SpawnProtectionTail);
            }
        }
        orig(self);
    }

    private static DamageTypeCombo FrenziedLeapDamageType(
        On.EntityStates.Croco.ChainableLeap.orig_GetBlastDamageType orig, ChainableLeap self)
    {
        var result = orig(self);
        var controller = self.outer.GetComponent<CrocoDamageTypeController>();
        if (controller) result |= controller.GetDamageType();
        return result;
    }

    private static void BreathProjectile(
        On.EntityStates.Croco.Breath.orig_FireProjectile orig, Breath self)
    {
        breathProjectileSetupDepth++;
        try { orig(self); }
        finally { breathProjectileSetupDepth--; }
    }

    private static DamageTypeCombo CrocoDamageType(
        On.RoR2.CrocoDamageTypeController.orig_GetDamageType orig, CrocoDamageTypeController self) =>
        breathProjectileSetupDepth > 0 ? (DamageTypeCombo)DamageType.Generic : orig(self);

    private void DamageDealt(DamageReport report)
    {
        if (!NetworkServer.active || !breathProjectilePrefab || report.damageDealt <= 0f ||
            report.damageInfo.rejected || !report.victimBody || !report.attacker)
            return;

        var controller = report.damageInfo.inflictor
            ? report.damageInfo.inflictor.GetComponent<ProjectileController>()
            : null;
        var breathPrefab = breathProjectilePrefab;
        if (controller is null || !controller ||
            ProjectileCatalog.GetProjectilePrefab(controller.catalogIndex) != breathPrefab)
            return;

        var passive = report.attacker.GetComponent<CrocoDamageTypeController>();
        if (!passive || !passive.passiveSkillSlot) return;
        bool usesBlight = passive.passiveSkillSlot.skillDef == passive.blightSkillDef;
        if (usesBlight && !breathTicks.GetValue(controller, _ => new())
            .GetValue(report.victim, _ => new()).NextAppliesBlight()) return;
        var poison = new InflictDotInfo
        {
            victimObject = report.victimBody.gameObject,
            attackerObject = report.attacker,
            dotIndex = usesBlight ? DotController.DotIndex.Blight : DotController.DotIndex.Poison,
            duration = usesBlight && ChangeGroups.Enabled("Acrid", "Passive") ? AcridBalance.BlightBaseDuration : AcridBalance.BreathPoisonDuration,
            damageMultiplier = 1f
        };
        DotController.InflictDot(ref poison);
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Acrid", token) ? AcridBalance.GetDescription(token) : null) ?? orig(self, token);
}
