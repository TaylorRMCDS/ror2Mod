using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates.Treebot;
using EntityStates.Treebot.Weapon;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class RexChanges : IDisposable
{
    private sealed class FruitingTeam { internal TeamIndex team; }
    private sealed class BonusHit
    {
        internal HealthComponent victim = null!;
        internal DamageInfo info = null!;
    }
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private readonly ConditionalWeakTable<CharacterBody, FruitingTeam> fruitTeams =
        new ConditionalWeakTable<CharacterBody, FruitingTeam>();
    private readonly HashSet<DamageInfo> bonusHits = new HashSet<DamageInfo>();
    private readonly AfterDamageQueue<BonusHit> pendingBonuses = new AfterDamageQueue<BonusHit>();
    private readonly Stack<GameObject> untaggedDroneShots = new Stack<GameObject>();
    private ProjectileController? finalSyringe;
    private float originalProc;
    private ProjectileDamage? harvestProjectile;
    private DamageTypeCombo originalHarvestType;
    private BuffDef? fruitBuff;
    private bool originalStackable;
    private GameObject? fruitPrefab;
    private bool reportedVisualFailure;

    internal RexChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            Add(() => IL.RoR2.GlobalEventManager.OnCharacterDeath += FruitDeath,
                () => IL.RoR2.GlobalEventManager.OnCharacterDeath -= FruitDeath);
            Add(() => IL.EntityStates.Treebot.Weapon.FireSonicBoom.OnEnter += DisperseOriginal,
                () => IL.EntityStates.Treebot.Weapon.FireSonicBoom.OnEnter -= DisperseOriginal);
            Add(() => On.EntityStates.Treebot.Weapon.FireSonicBoom.OnEnter += Disperse,
                () => On.EntityStates.Treebot.Weapon.FireSonicBoom.OnEnter -= Disperse);
            Add(() => On.EntityStates.Treebot.TreebotFireFruitSeed.OnEnter += Harvest,
                () => On.EntityStates.Treebot.TreebotFireFruitSeed.OnEnter -= Harvest);
            Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += Projectiles,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= Projectiles);
            Add(() => On.RoR2.BuffCatalog.SetBuffDefs += Buffs,
                () => On.RoR2.BuffCatalog.SetBuffDefs -= Buffs);
            Add(() => RoR2Application.onLoad += LoadFruit,
                () => RoR2Application.onLoad -= LoadFruit);
            Add(() => GlobalEventManager.onServerDamageDealt += DamageDealt,
                () => GlobalEventManager.onServerDamageDealt -= DamageDealt);
            Add(() => On.RoR2.HealthComponent.TakeDamageProcess += FinishDamage,
                () => On.RoR2.HealthComponent.TakeDamageProcess -= FinishDamage);
            Add(() => On.EntityStates.Drone.DroneWeapon.FireGatling.OnEnter += DroneShot,
                () => On.EntityStates.Drone.DroneWeapon.FireGatling.OnEnter -= DroneShot);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("REX changes ENABLED: final Inject proc 1.0; 13m downward Disperse; Harvest 100%/0.25s recovery; stacking Fruiting on-hit redesign.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }
    private void Fail(Exception error)
    {
        Dispose();
        log.LogError($"REX setup failed; restored its prefab values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        if (finalSyringe != null) finalSyringe.procCoefficient = originalProc;
        if (harvestProjectile != null) harvestProjectile.damageType = originalHarvestType;
        if (fruitBuff != null) fruitBuff.canStack = originalStackable;
        finalSyringe = null; harvestProjectile = null; fruitBuff = null; fruitPrefab = null;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
        pendingBonuses.Clear();
    }

    private void Projectiles(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (finalSyringe == null)
            {
                var syringe = prefabs.Single(p => p.name == "SyringeProjectileHealing").GetComponent<ProjectileController>();
                var seed = prefabs.Single(p => p.name == "TreebotFruitSeedProjectile").GetComponent<ProjectileDamage>();
                if (!syringe || !seed) throw new InvalidOperationException("REX projectile components were not found.");
                finalSyringe = syringe;
                originalProc = syringe.procCoefficient;
                harvestProjectile = seed;
                originalHarvestType = seed.damageType;
                syringe.procCoefficient = 1f;
                var damageType = seed.damageType;
                damageType.damageType &= ~DamageType.FruitOnHit;
                seed.damageType = damageType;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void Buffs(On.RoR2.BuffCatalog.orig_SetBuffDefs orig, BuffDef[] defs)
    {
        try
        {
            if (fruitBuff == null)
            {
                fruitBuff = defs.Single(b => b.name == "bdFruiting");
                originalStackable = fruitBuff.canStack;
                fruitBuff.canStack = true;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(defs);
    }

    private void LoadFruit()
    {
        try
        {
            fruitPrefab = RoR2.ContentManagement.ContentManager.networkedObjectPrefabs
                .Single(p => p.name == "TreebotFruitPack");
            if (!fruitPrefab || !fruitPrefab.GetComponent<NetworkIdentity>() ||
                !fruitPrefab.GetComponent<TeamFilter>() || !fruitPrefab.GetComponentInChildren<HealthPickup>() ||
                finalSyringe == null || harvestProjectile == null || fruitBuff == null)
                throw new InvalidOperationException("REX fruit pickup or catalog setup was incomplete.");
            log.LogInfo("REX fruit pickup and stackable buff ready; using the game's networked fruit and Fruiting visual.");
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Harvest(On.EntityStates.Treebot.TreebotFireFruitSeed.orig_OnEnter orig, TreebotFireFruitSeed self)
    {
        // The projectile fires immediately in this state. PrepFruitSeed is a
        // separate state; only the post-shot recovery and its animation speed change.
        self.damageCoefficient = RexBalance.HarvestDamage;
        self.baseDuration = RexBalance.HarvestRecovery;
        orig(self);
    }

    private static void FruitDeath(ILContext il) => RexBalance.PatchFruitDeathBuffCheck(il.Body);

    private static void DisperseOriginal(ILContext il)
    {
        var methods = il.Body.Instructions.Where(i => i.Operand is MethodReference).ToArray();
        var aim = methods.Where(i => ((MethodReference)i.Operand).Name == "GetAimRay").ToArray();
        var server = methods.Where(i => ((MethodReference)i.Operand).FullName ==
            "System.Boolean UnityEngine.Networking.NetworkServer::get_active()").ToArray();
        var authority = methods.Where(i => ((MethodReference)i.Operand).Name == "get_isAuthority").ToArray();
        if (aim.Length != 1 || server.Length != 1 || authority.Length != 1)
            throw new InvalidOperationException("Disperse's aim/server/recoil layout changed.");

        var cursor = new ILCursor(il);
        cursor.Goto(aim[0], MoveType.After);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<Ray, FireSonicBoom, Ray>>((ray, state) =>
        {
            if (state.GetType() != typeof(FireSonicBoom)) return ray;
            return new Ray(state.outer.GetComponent<CharacterBody>().footPosition, Vector3.down);
        });
        // Keep vanilla initialization, animation, audio and the downward effect.
        // Replace gameplay only for Disperse, leaving Bramble Volley untouched.
        foreach (var call in new[] { server[0], authority[0] })
        {
            cursor.Goto(call, MoveType.After);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<bool, FireSonicBoom, bool>>((allowed, state) =>
                allowed && state.GetType() != typeof(FireSonicBoom));
        }
    }

    private void Disperse(On.EntityStates.Treebot.Weapon.FireSonicBoom.orig_OnEnter orig, FireSonicBoom self)
    {
        orig(self);
        if (self.GetType() != typeof(FireSonicBoom)) return;
        var body = self.outer.GetComponent<CharacterBody>();
        if (!body) return;
        try { RexDisperseVisual.Spawn(self.fireEffectPrefab, body.footPosition); }
        catch (Exception error)
        {
            if (!reportedVisualFailure)
            {
                reportedVisualFailure = true;
                log.LogWarning($"Disperse range ring unavailable; skill gameplay is unaffected. {error}");
            }
        }
        if (NetworkServer.active) PushEnemies(body);
        if (!Util.HasEffectiveAuthority(self.outer.networkIdentity) || !body.characterMotor) return;

        float speed = Trajectory.CalculateInitialYSpeedForHeight(self.airKnockbackDistance, -body.acceleration)
            * RexBalance.LaunchMultiplier;
        var force = PhysForceInfo.Create();
        force.force = Vector3.up * speed;
        force.massIsOne = true;
        // Add the impulse to existing motion, including downward velocity.
        force.resetVelocity = false;
        force.ignoreGroundStick = true;
        body.characterMotor.ApplyForceImpulse(in force);
    }

    private static void PushEnemies(CharacterBody source)
    {
        var search = new BullseyeSearch
        {
            teamMaskFilter = TeamMask.all,
            searchOrigin = source.footPosition,
            searchDirection = Vector3.down,
            maxAngleFilter = 180f,
            maxDistanceFilter = RexBalance.DisperseRadius,
            filterByLoS = false,
            sortMode = BullseyeSearch.SortMode.None
        };
        search.RefreshCandidates();
        search.FilterOutGameObject(source.gameObject);
        foreach (var hurtBox in search.GetResults().Where(h => h).Distinct(new HurtBox.EntityEqualityComparer()))
        {
            var health = hurtBox.healthComponent;
            if (!health || !health.alive || !health.body ||
                !FriendlyFireManager.ShouldSplashHitProceed(health, source.teamComponent.teamIndex)) continue;
            var victim = health.body;
            victim.AddTimedBuff(RoR2Content.Buffs.Weak, RexBalance.WeakenDuration);
            var hurtState = victim.GetComponent<SetStateOnHurt>();
            if (hurtState) hurtState.SetStun(-1f);
            var direction = victim.corePosition - source.footPosition;
            direction.y = 0f;
            var velocity = direction.normalized * RexBalance.EnemyPushSpeed + Vector3.up * RexBalance.EnemyLiftSpeed;
            var rigidbody = victim.GetComponent<Rigidbody>();
            float mass = victim.characterMotor ? victim.characterMotor.mass : rigidbody ? rigidbody.mass : 1f;
            float suitability = FireSonicBoom.shoveSuitabilityCurve.Evaluate(mass);
            health.TakeDamageForce(velocity * mass * suitability, true, true);
            var damage = new DamageInfo
            {
                attacker = source.gameObject, damage = 0f, procCoefficient = 0f,
                position = hurtBox.transform.position, inflictedHurtbox = hurtBox,
                damageType = new DamageTypeCombo { damageSource = DamageSource.Utility }
            };
            health.TakeDamage(damage);
            GlobalEventManager.instance.OnHitEnemy(damage, victim.gameObject);
        }
    }

    private static bool IsHarvest(DamageInfo info)
    {
        if (!info.inflictor) return false;
        var controller = info.inflictor.GetComponent<ProjectileController>();
        if (!controller || controller.catalogIndex < 0) return false;
        var prefab = ProjectileCatalog.GetProjectilePrefab(controller.catalogIndex);
        return prefab && prefab.name == "TreebotFruitSeedProjectile";
    }

    private void DamageDealt(DamageReport report)
    {
        if (!NetworkServer.active || fruitPrefab == null || fruitBuff == null ||
            !report.victimBody || !report.attackerBody || report.damageInfo.rejected || bonusHits.Contains(report.damageInfo)) return;
        var victim = report.victimBody;
        var attacker = report.attackerBody;
        var info = report.damageInfo;

        // The hit sees the stacks already on the victim. A subsequent Harvest
        // then adds five and refreshes the entire remaining pool, including on reapply.
        if (victim.GetBuffCount(fruitBuff) > 0 && fruitTeams.TryGetValue(victim, out var owner) &&
            RexBalance.IsEligibleHit(info.rejected, info.delayedDamageSecondHalf,
                info.dotIndex != DotController.DotIndex.None || (info.damageType.damageSource & DamageSource.DOT) != 0,
                !info.procChainMask.Equals(default(ProcChainMask)),
                (info.damageType.damageSource & DamageSource.SkillMask) != 0 ||
                    (untaggedDroneShots.Count > 0 && untaggedDroneShots.Peek() == attacker.gameObject &&
                        info.inflictor == attacker.gameObject),
                attacker.teamComponent && attacker.teamComponent.teamIndex == owner.team, report.damageDealt) &&
            UnityEngine.Random.value < RexBalance.FruitChance)
        {
            victim.RemoveOldestTimedBuff(fruitBuff);
            var fruit = UnityEngine.Object.Instantiate(fruitPrefab, victim.corePosition, UnityEngine.Random.rotation);
            // Collection heals on the server. Change only this spawned fruit,
            // preserving the shared prefab and other sources of healing pickups.
            var healing = fruit.GetComponentInChildren<HealthPickup>();
            healing.flatHealing = 0f;
            healing.fractionalHealing = RexBalance.FruitHealing;
            fruit.GetComponent<TeamFilter>().teamIndex = owner.team;
            NetworkServer.Spawn(fruit);

            if (report.victim.alive)
            {
                var bonus = new DamageInfo
                {
                    attacker = attacker.gameObject, damage = attacker.damage * RexBalance.FruitBonusDamage,
                    crit = false, procCoefficient = 0f, position = victim.corePosition,
                    dotIndex = DotController.DotIndex.None, damageType = default(DamageTypeCombo)
                };
                pendingBonuses.Enqueue(new BonusHit { victim = report.victim, info = bonus });
            }
        }

        if (IsHarvest(info) && !info.delayedDamageSecondHalf && info.dotIndex == DotController.DotIndex.None &&
            report.damageDealt > 0f && report.victim.alive && attacker.teamComponent)
        {
            int count = RexBalance.StacksAfterHarvest(victim.GetBuffCount(fruitBuff));
            victim.ClearTimedBuffs(fruitBuff);
            for (int i = 0; i < count; i++) victim.AddTimedBuff(fruitBuff, RexBalance.FruitDuration);
            fruitTeams.GetValue(victim, _ => new FruitingTeam()).team = attacker.teamComponent.teamIndex;
            victim.AddTimedBuff(RoR2Content.Buffs.Weak, RexBalance.WeakenDuration);
        }
    }

    private void FinishDamage(On.RoR2.HealthComponent.orig_TakeDamageProcess orig, HealthComponent self, DamageInfo info)
    {
        pendingBonuses.Enter();
        try { orig(self, info); }
        finally { pendingBonuses.Exit(ApplyBonus); }
    }

    private void DroneShot(On.EntityStates.Drone.DroneWeapon.FireGatling.orig_OnEnter orig,
        EntityStates.Drone.DroneWeapon.FireGatling self)
    {
        // The installed gunner-drone state leaves DamageSource unspecified.
        // Recognize its synchronous bullet hits without changing their damage tags.
        untaggedDroneShots.Push(self.outer.gameObject);
        try { orig(self); }
        finally { untaggedDroneShots.Pop(); }
    }

    private void ApplyBonus(BonusHit hit)
    {
        if (!NetworkServer.active || !hit.victim || !hit.victim.alive) return;
        bonusHits.Add(hit.info);
        try { hit.victim.TakeDamage(hit.info); }
        finally { bonusHits.Remove(hit.info); }
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig, Language self, string token) =>
        RexBalance.GetDescription(token) ?? orig(self, token);
}
