using System;
using System.Collections.Generic;
using BepInEx.Logging;
using EntityStates.Commando;
using EntityStates.Commando.CommandoWeapon;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class CommandoChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();

    internal CommandoChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Commando", "Special")) Add(() => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.OnEnter += PatchBarrageShots,
                () => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.OnEnter -= PatchBarrageShots);
            if (ChangeGroups.Enabled("Commando", "Special")) Add(() => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.FireBullet += PatchBarrageDamage,
                () => IL.EntityStates.Commando.CommandoWeapon.FireBarrage.FireBullet -= PatchBarrageDamage);
            if (ChangeGroups.Enabled("Commando", "Secondary")) Add(() => On.EntityStates.Commando.CommandoWeapon.FireFMJ.ModifyProjectileInfo += ModifyPhaseRound,
                () => On.EntityStates.Commando.CommandoWeapon.FireFMJ.ModifyProjectileInfo -= ModifyPhaseRound);
            if (ChangeGroups.Enabled("Commando", "Secondary")) Add(() => On.RoR2.Projectile.ProjectileOverlapAttack.AddToDamageCoefficient += ModifyPhaseRoundPierce,
                () => On.RoR2.Projectile.ProjectileOverlapAttack.AddToDamageCoefficient -= ModifyPhaseRoundPierce);
            if (ChangeGroups.Enabled("Commando", "Secondary")) Add(() => On.EntityStates.Commando.CommandoWeapon.FireShotgunBlast.ModifyBulletAttack += ModifyPhaseBlast,
                () => On.EntityStates.Commando.CommandoWeapon.FireShotgunBlast.ModifyBulletAttack -= ModifyPhaseBlast);
            if (ChangeGroups.Enabled("Commando", "Utility")) Add(() => On.EntityStates.Commando.DodgeState.OnEnter += BeginTacticalDive,
                () => On.EntityStates.Commando.DodgeState.OnEnter -= BeginTacticalDive);
            if (ChangeGroups.Enabled("Commando", "Special")) Add(() => On.RoR2.Projectile.ProjectileController.OnCollisionEnter += GrenadeCollision,
                () => On.RoR2.Projectile.ProjectileController.OnCollisionEnter -= GrenadeCollision);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Commando changes ENABLED: Phase Round 320%/+80%, Phase Blast proc 0.7, Dive +200 armor/0.5s, Suppressive Fire 8x115%, enemy grenade bounce speed -50%. Double Tap is vanilla.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Could not enable the full Commando patch; removed its hooks. {error}");
        }
    }

    private void Add(Action install, Action remove)
    {
        cleanup.Add(remove);
        install();
    }

    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"Commando hook cleanup failed: {error}"); }
        cleanup.Clear();
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

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Commando", token) ? CommandoBalance.GetDescription(token) : null) ?? orig(self, token);
}
