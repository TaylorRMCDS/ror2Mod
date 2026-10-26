using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EntityStates.VoidSurvivor.Weapon;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class VoidFiendChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private bool bodyChanged;
    private bool projectileChanged;

    internal VoidFiendChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Void Fiend", "Primary")) Add(() => IL.EntityStates.VoidSurvivor.Weapon.FireHandBeam.OnEnter += PrimaryFalloff,
                () => IL.EntityStates.VoidSurvivor.Weapon.FireHandBeam.OnEnter -= PrimaryFalloff);
            if (ChangeGroups.Enabled("Void Fiend", "Special")) Add(() => On.EntityStates.VoidSurvivor.Weapon.ChargeCrushBase.OnEnter += Charge,
                () => On.EntityStates.VoidSurvivor.Weapon.ChargeCrushBase.OnEnter -= Charge);
            if (ChangeGroups.Enabled("Void Fiend", "Special")) Add(() => On.EntityStates.VoidSurvivor.Weapon.CrushBase.OnEnter += Crush,
                () => On.EntityStates.VoidSurvivor.Weapon.CrushBase.OnEnter -= Crush);
            if (ChangeGroups.Enabled("Void Fiend", "Passive")) Add(() => On.RoR2.BodyCatalog.SetBodyPrefabs += Bodies,
                () => On.RoR2.BodyCatalog.SetBodyPrefabs -= Bodies);
            if (ChangeGroups.Enabled("Void Fiend", "Secondary")) Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += Projectiles,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= Projectiles);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Void Fiend changes ENABLED: normal Drown falloff 50-100m to 50%; corrupted Flood 15m/50mps/150s; Suppress 20% heal/self-damage, corrupted cast 50% faster; OOC corruption 2/s; transformation invincibility unchanged.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }
    private void Fail(Exception error)
    {
        Dispose();
        log.LogError($"Void Fiend setup failed; restored its values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
        bodyChanged = projectileChanged = false;
    }

    private static void PrimaryFalloff(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(VoidFiendBalance.PrimaryFireSite(il.Body), MoveType.Before);
        cursor.EmitDelegate<Func<BulletAttack, BulletAttack>>(bullet =>
        {
            bullet.modifyOutgoingDamageCallback += ScalePrimaryDamage;
            return bullet;
        });
    }

    private static void ScalePrimaryDamage(BulletAttack attack, ref BulletAttack.BulletHit hit, DamageInfo info) =>
        info.damage *= VoidFiendBalance.PrimaryDamageMultiplier(hit.distance);

    private static void Charge(On.EntityStates.VoidSurvivor.Weapon.ChargeCrushBase.orig_OnEnter orig, ChargeCrushBase self)
    {
        float original = self.baseDuration;
        try
        {
            if (self is ChargeCrushHealth) self.baseDuration *= VoidFiendBalance.CorruptedCastMultiplier;
            orig(self);
        }
        finally { self.baseDuration = original; }
    }

    private static void Crush(On.EntityStates.VoidSurvivor.Weapon.CrushBase.orig_OnEnter orig, CrushBase self)
    {
        float originalHeal = self.selfHealFraction, originalDuration = self.baseDuration;
        try
        {
            if (self is CrushHealth)
            {
                self.selfHealFraction = VoidFiendBalance.SelfDamageFraction;
                self.baseDuration *= VoidFiendBalance.CorruptedCastMultiplier;
            }
            else if (self is CrushCorruption) self.selfHealFraction = VoidFiendBalance.HealFraction;
            orig(self);
        }
        finally { self.selfHealFraction = originalHeal; self.baseDuration = originalDuration; }
    }

    private void Bodies(On.RoR2.BodyCatalog.orig_SetBodyPrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!bodyChanged)
            {
                var controller = prefabs.Single(p => p.name == "VoidSurvivorBody").GetComponent<VoidSurvivorController>();
                if (!controller) throw new InvalidOperationException("Void Fiend's body controller was not found.");
                float original = controller.corruptionPerSecondOutOfCombat;
                cleanup.Add(() => { if (controller) controller.corruptionPerSecondOutOfCombat = original; });
                controller.corruptionPerSecondOutOfCombat = VoidFiendBalance.OutOfCombatCorruption;
                bodyChanged = true;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void Projectiles(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!projectileChanged)
            {
                var prefab = prefabs.Single(p => p.name == "VoidSurvivorMegaBlasterBigProjectileCorrupted");
                var simple = prefab.GetComponent<ProjectileSimple>();
                var explosion = prefab.GetComponent<ProjectileImpactExplosion>();
                if (!simple || !explosion) throw new InvalidOperationException("Corrupted Flood projectile layout changed.");
                float oldRadius = explosion.blastRadius, oldSpeed = simple.desiredForwardSpeed,
                    oldFlightLife = simple.lifetime, oldExplosionLife = explosion.lifetime;
                cleanup.Add(() =>
                {
                    if (simple) { simple.desiredForwardSpeed = oldSpeed; simple.lifetime = oldFlightLife; }
                    if (explosion) { explosion.blastRadius = oldRadius; explosion.lifetime = oldExplosionLife; }
                });
                simple.desiredForwardSpeed = VoidFiendBalance.CorruptedFloodSpeed;
                simple.lifetime = explosion.lifetime = VoidFiendBalance.CorruptedFloodLifetime;
                explosion.blastRadius = VoidFiendBalance.CorruptedFloodRadius;
                projectileChanged = true;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Void Fiend", token) ? VoidFiendBalance.GetDescription(token) : null) ?? orig(self, token);
}
