using System;
using System.Collections.Generic;
using BepInEx;
using EntityStates.Commando;
using EntityStates.Commando.CommandoWeapon;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.taylor.ror2.balancepatch";
    public const string PluginName = "Taylor's Balance Patch";
    public const string PluginVersion = "0.5.5";
    private readonly List<Action> removeHooks = new List<Action>();
    private ArtificerChanges? artificer;
    private BanditChanges? bandit;
    private RexChanges? rex;

    private void Awake()
    {
        var enableCommando = Config.Bind(CommandoBalance.ConfigSection, CommandoBalance.ConfigKey,
            true, CommandoBalance.ConfigDescription);
        var enableArtificer = Config.Bind("Artificer", "EnableArtificerChanges", true,
            "Enable all Artificer changes, skill slot swaps and descriptions. Restart the game after changing this setting.");
        var enableBandit = Config.Bind("Bandit", "EnableBanditChanges", true,
            "Enable all Bandit Blast changes and descriptions. Restart the game after changing this setting.");
        var enableRex = Config.Bind("REX", "EnableREXChanges", true,
            "Enable all REX changes, including the Fruiting redesign. Restart the game after changing this setting.");
        if (enableCommando.Value) EnableCommando();
        else Logger.LogInfo("Commando changes DISABLED.");
        if (enableArtificer.Value)
        {
            artificer = new ArtificerChanges(Logger);
            artificer.Enable();
        }
        else Logger.LogInfo("Artificer changes DISABLED.");
        if (enableBandit.Value)
        {
            bandit = new BanditChanges(Logger);
            bandit.Enable();
        }
        else Logger.LogInfo("Bandit changes DISABLED.");
        if (enableRex.Value)
        {
            rex = new RexChanges(Logger);
            rex.Enable();
        }
        else Logger.LogInfo("REX changes DISABLED.");
    }

    private void EnableCommando()
    {
        try
        {
            AddHook(() => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.OnEnter += PatchBarrageShots,
                () => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.OnEnter -= PatchBarrageShots);
            AddHook(() => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.FireBullet += PatchBarrageDamage,
                () => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.FireBullet -= PatchBarrageDamage);
            AddHook(() => On.EntityStates.Commando.CommandoWeapon.FireFMJ.ModifyProjectileInfo += ModifyPhaseRound,
                () => On.EntityStates.Commando.CommandoWeapon.FireFMJ.ModifyProjectileInfo -= ModifyPhaseRound);
            AddHook(() => On.RoR2.Projectile.ProjectileOverlapAttack.AddToDamageCoefficient += ModifyPhaseRoundPierce,
                () => On.RoR2.Projectile.ProjectileOverlapAttack.AddToDamageCoefficient -= ModifyPhaseRoundPierce);
            AddHook(() => On.EntityStates.Commando.CommandoWeapon.FireShotgunBlast.ModifyBulletAttack += ModifyPhaseBlast,
                () => On.EntityStates.Commando.CommandoWeapon.FireShotgunBlast.ModifyBulletAttack -= ModifyPhaseBlast);
            AddHook(() => On.EntityStates.Commando.DodgeState.OnEnter += BeginTacticalDive,
                () => On.EntityStates.Commando.DodgeState.OnEnter -= BeginTacticalDive);
            AddHook(() => On.RoR2.Projectile.ProjectileController.OnCollisionEnter += GrenadeCollision,
                () => On.RoR2.Projectile.ProjectileController.OnCollisionEnter -= GrenadeCollision);
            AddHook(() => On.RoR2.Language.GetLocalizedStringByToken += OverrideDescriptions,
                () => On.RoR2.Language.GetLocalizedStringByToken -= OverrideDescriptions);
            Logger.LogInfo("Commando changes ENABLED: Phase Round 320%/+80%, Phase Blast proc 0.7, Dive +200 armor/0.5s, Suppressive Fire 8x115%, enemy grenade bounce speed -50%. Double Tap is vanilla.");
        }
        catch (Exception error)
        {
            RemoveHooks();
            Logger.LogError($"Could not enable the full Commando patch; removed its hooks. {error}");
        }
    }

    private void AddHook(Action add, Action remove)
    {
        // Also attempt removal if a registration throws partway through.
        removeHooks.Add(remove);
        add();
    }

    private void RemoveHooks()
    {
        for (int i = removeHooks.Count - 1; i >= 0; i--)
        {
            try { removeHooks[i](); }
            catch (Exception error) { Logger.LogError($"Hook cleanup failed: {error}"); }
        }
        removeHooks.Clear();
    }

    private void OnDestroy()
    {
        artificer?.Dispose();
        bandit?.Dispose();
        rex?.Dispose();
        RemoveHooks();
    }
    private static void PatchBarrageShots(ILContext il) => CommandoBalance.PatchBarrageShots(il.Body);
    private static void PatchBarrageDamage(ILContext il) => CommandoBalance.PatchBarrageDamage(il.Body);

    private static void ModifyPhaseRound(On.EntityStates.Commando.CommandoWeapon.FireFMJ.orig_ModifyProjectileInfo orig,
        FireFMJ self, ref FireProjectileInfo info)
    {
        orig(self, ref info);
        var body = self.outer.GetComponent<CharacterBody>();
        if (body) info.damage = body.damage * CommandoBalance.PhaseRoundDamage;
    }

    private static bool IsProjectile(ProjectileController controller, string prefabName)
    {
        if (!controller || controller.catalogIndex < 0) return false;
        var prefab = ProjectileCatalog.GetProjectilePrefab(controller.catalogIndex);
        return prefab && prefab.name == prefabName;
    }

    private static void ModifyPhaseRoundPierce(On.RoR2.Projectile.ProjectileOverlapAttack.orig_AddToDamageCoefficient orig,
        ProjectileOverlapAttack self, float value)
    {
        // FMJRamping's per-hit UnityEvent passes 0.4. Preserve the existing
        // additive ramp and hit tracking; change only this projectile's increment.
        if (IsProjectile(self.GetComponent<ProjectileController>(), CommandoBalance.PhaseRoundProjectile))
            value = CommandoBalance.PhaseRoundPierceBonus;
        orig(self, value);
    }

    private static void ModifyPhaseBlast(On.EntityStates.Commando.CommandoWeapon.FireShotgunBlast.orig_ModifyBulletAttack orig,
        FireShotgunBlast self, ref BulletAttack attack)
    {
        orig(self, ref attack);
        // Called for both four-pellet blasts. Count and damage stay unchanged.
        attack.procCoefficient = CommandoBalance.PhaseBlastProc;
    }

    private static void BeginTacticalDive(On.EntityStates.Commando.DodgeState.orig_OnEnter orig, DodgeState self)
    {
        orig(self);
        // DodgeState has subclasses; only the original Tactical Dive changes.
        if (NetworkServer.active && self.GetType() == typeof(DodgeState))
        {
            var body = self.outer.GetComponent<CharacterBody>();
            if (body) body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, CommandoBalance.DiveArmorDuration);
        }
    }

    private static void GrenadeCollision(On.RoR2.Projectile.ProjectileController.orig_OnCollisionEnter orig,
        ProjectileController self, Collision collision)
    {
        orig(self, collision);
        if (!self || !(NetworkServer.active || self.isPrediction)
            || !IsProjectile(self, CommandoBalance.GrenadeProjectile) || !collision.collider)
            return;
        var hurtBox = collision.collider.GetComponent<HurtBox>();
        var target = hurtBox ? hurtBox.healthComponent : collision.collider.GetComponentInParent<HealthComponent>();
        if (!target || !target.body || !target.body.teamComponent || !self.teamFilter
            || target.body.teamComponent.teamIndex == self.teamFilter.teamIndex)
            return;
        // Unity has resolved the collision by this callback. Dampen the outgoing
        // velocity after an enemy hit without changing the shared physics material.
        var body = self.GetComponent<Rigidbody>();
        if (body && !body.isKinematic)
            body.velocity *= CommandoBalance.GrenadeEnemyBounceSpeedMultiplier;
    }

    private static string OverrideDescriptions(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => CommandoBalance.GetDescription(token) ?? orig(self, token);
}
