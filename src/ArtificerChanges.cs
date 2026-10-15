using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EntityStates.Mage.Weapon;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class ArtificerChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private SkillFamily? utility;
    private SkillFamily? special;
    private SkillFamily.Variant[]? originalUtility;
    private SkillFamily.Variant[]? originalSpecial;
    private ProjectileDamage? wallDamage;
    private DamageTypeCombo originalWallDamageType;

    internal ArtificerChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            Add(() => IL.EntityStates.Mage.Weapon.PrepWall.FixedUpdate += WallInput,
                () => IL.EntityStates.Mage.Weapon.PrepWall.FixedUpdate -= WallInput);
            Add(() => IL.EntityStates.Mage.Weapon.PrepWall.OnExit += WallRefund,
                () => IL.EntityStates.Mage.Weapon.PrepWall.OnExit -= WallRefund);
            Add(() => IL.EntityStates.Mage.FlyUpState.HandleMovements += LaunchSpeed,
                () => IL.EntityStates.Mage.FlyUpState.HandleMovements -= LaunchSpeed);
            Add(() => IL.EntityStates.Mage.FlyUpState.OnEnter += LaunchSource,
                () => IL.EntityStates.Mage.FlyUpState.OnEnter -= LaunchSource);
            Add(() => IL.EntityStates.Mage.Weapon.Flamethrower.FixedUpdate += FlameVisuals,
                () => IL.EntityStates.Mage.Weapon.Flamethrower.FixedUpdate -= FlameVisuals);
            Add(() => On.EntityStates.Mage.Weapon.Flamethrower.OnEnter += FlameEnter,
                () => On.EntityStates.Mage.Weapon.Flamethrower.OnEnter -= FlameEnter);
            Add(() => On.EntityStates.Mage.Weapon.FireLightningBolt.ModifyProjectileInfo += Plasma,
                () => On.EntityStates.Mage.Weapon.FireLightningBolt.ModifyProjectileInfo -= Plasma);
            Add(() => GlobalEventManager.onServerDamageDealt += Slow,
                () => GlobalEventManager.onServerDamageDealt -= Slow);
            Add(() => On.RoR2.Skills.SkillCatalog.SetSkillFamilies += SwapSkills,
                () => On.RoR2.Skills.SkillCatalog.SetSkillFamilies -= SwapSkills);
            Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += WallDamageSource,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= WallDamageSource);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Artificer changes ENABLED: Plasma Bolt 310%/30% slow for 2s, Snapfreeze Special, Ion Surge Utility/fixed launch speed, Flamethrower 30m.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Could not enable the full Artificer patch; removed its hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }
    public void Dispose()
    {
        if (utility != null && originalUtility != null) utility.variants = originalUtility;
        if (special != null && originalSpecial != null) special.variants = originalSpecial;
        if (wallDamage != null) wallDamage.damageType = originalWallDamageType;
        wallDamage = null;
        originalUtility = originalSpecial = null;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
    }

    private static void WallInput(ILContext il) => ArtificerBalance.PatchWallInput(il.Body);
    private static void WallRefund(ILContext il) => ArtificerBalance.PatchWallRefund(il.Body);
    private static void LaunchSpeed(ILContext il) => ArtificerBalance.PatchLaunchSpeed(il.Body);
    private static void LaunchSource(ILContext il) => ArtificerBalance.PatchLaunchSource(il.Body);

    private void SwapSkills(On.RoR2.Skills.SkillCatalog.orig_SetSkillFamilies orig, SkillFamily[] families)
    {
        try
        {
            if (originalUtility == null)
            {
                var u = families.Single(f => ((ScriptableObject)f).name == "MageBodyUtilityFamily");
                var s = families.Single(f => ((ScriptableObject)f).name == "MageBodySpecialFamily");
                int ice = Array.FindIndex(u.variants, v => v.skillDef.skillNameToken == "MAGE_UTILITY_ICE_NAME");
                int ion = Array.FindIndex(s.variants, v => v.skillDef.skillNameToken == "MAGE_SPECIAL_LIGHTNING_NAME");
                if (ice < 0 || ion < 0) throw new InvalidOperationException("Artificer's original skill variants were not found.");
                var newU = (SkillFamily.Variant[])u.variants.Clone();
                var newS = (SkillFamily.Variant[])s.variants.Clone();
                newU[ice] = s.variants[ion];
                newS[ion] = u.variants[ice];
                utility = u; special = s;
                originalUtility = u.variants; originalSpecial = s.variants;
                u.variants = newU; s.variants = newS;
                log.LogInfo("Artificer skill slots swapped; other variants and unlock requirements preserved.");
            }
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Artificer skill swap failed; disabled Artificer changes. {error}");
        }
        orig(families);
    }

    private void WallDamageSource(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (wallDamage == null)
            {
                var damage = prefabs.Single(p => p.name == "MageIcewallPillarProjectile").GetComponent<ProjectileDamage>();
                if (!damage) throw new InvalidOperationException("Snapfreeze projectile damage was not found.");
                originalWallDamageType = damage.damageType;
                wallDamage = damage;
                damage.damageType.damageSource = DamageSource.Special;
            }
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Artificer projectile patch failed; disabled Artificer changes. {error}");
        }
        orig(prefabs);
    }

    private static void Plasma(On.EntityStates.Mage.Weapon.FireLightningBolt.orig_ModifyProjectileInfo orig,
        FireLightningBolt self, ref FireProjectileInfo info)
    {
        orig(self, ref info);
        var body = self.outer.GetComponent<CharacterBody>();
        if (body) info.damage = body.damage * ArtificerBalance.PlasmaDamage;
    }

    private static void Slow(DamageReport report)
    {
        if (!NetworkServer.active || !report.victimBody || report.damageInfo.rejected || !report.damageInfo.inflictor) return;
        var controller = report.damageInfo.inflictor.GetComponent<ProjectileController>();
        if (!controller || controller.catalogIndex < 0) return;
        var prefab = ProjectileCatalog.GetProjectilePrefab(controller.catalogIndex);
        if (prefab && prefab.name == "MageLightningboltBasic")
            report.victimBody.AddTimedBuff(JunkContent.Buffs.Slow30, ArtificerBalance.SlowDuration);
    }

    private static void FlameEnter(On.EntityStates.Mage.Weapon.Flamethrower.orig_OnEnter orig, Flamethrower self)
    {
        self.maxDistance = ArtificerBalance.FlameRange;
        orig(self);
    }

    private static void FlameVisuals(ILContext il)
    {
        var sites = il.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f &&
            (f.Name == "leftFlamethrowerEffectInstance" || f.Name == "rightFlamethrowerEffectInstance")).ToArray();
        if (sites.Length != 2 || sites.Any(i => !(i.Previous.Operand is MethodReference m) || m.Name != "Instantiate"))
            throw new InvalidOperationException("Flamethrower effect creation layout changed.");
        foreach (var site in sites)
        {
            var cursor = new ILCursor(il);
            cursor.Goto(site, MoveType.Before);
            cursor.EmitDelegate<Func<GameObject, GameObject>>(ExtendFlame);
        }
    }

    private static GameObject ExtendFlame(GameObject effect)
    {
        if (!effect) return effect;
        var scale = effect.transform.localScale;
        scale.z *= ArtificerBalance.FlameLengthScale;
        effect.transform.localScale = scale;
        foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particle.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
        return effect;
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => ArtificerBalance.GetDescription(token) ?? orig(self, token);
}
