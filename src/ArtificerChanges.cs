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
    private CharacterBody? artificerBody;
    private float originalAcceleration;
    private UnlockableDef? snapfreezeUnlock;
    private string? originalUnlockNameToken;

    internal ArtificerChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Artificer", "Stats")) Add(() => On.RoR2.BodyCatalog.SetBodyPrefabs += Bodies,
                () => On.RoR2.BodyCatalog.SetBodyPrefabs -= Bodies);
            if (ChangeGroups.Enabled("Artificer", "Utility")) Add(() => IL.EntityStates.Mage.Weapon.FireEarthShards.PlayAttackEffect += TectonicPush,
                () => IL.EntityStates.Mage.Weapon.FireEarthShards.PlayAttackEffect -= TectonicPush);
            if (ChangeGroups.Enabled("Artificer", "Secondary") || ChangeGroups.Enabled("Artificer", "Special")) cleanup.Add(SharedFreezeChanges.Acquire().Dispose);
            if (ChangeGroups.SwapArtificer) Add(() => IL.EntityStates.Mage.Weapon.PrepWall.FixedUpdate += WallInput,
                () => IL.EntityStates.Mage.Weapon.PrepWall.FixedUpdate -= WallInput);
            if (ChangeGroups.Enabled("Artificer", "Special")) Add(() => IL.EntityStates.Mage.Weapon.PrepWall.OnExit += WallRefund,
                () => IL.EntityStates.Mage.Weapon.PrepWall.OnExit -= WallRefund);
            if (ChangeGroups.Enabled("Artificer", "Utility")) Add(() => IL.EntityStates.Mage.FlyUpState.HandleMovements += LaunchSpeed,
                () => IL.EntityStates.Mage.FlyUpState.HandleMovements -= LaunchSpeed);
            if (ChangeGroups.SwapArtificer) Add(() => IL.EntityStates.Mage.FlyUpState.OnEnter += LaunchSource,
                () => IL.EntityStates.Mage.FlyUpState.OnEnter -= LaunchSource);
            if (ChangeGroups.Enabled("Artificer", "Special")) Add(() => IL.EntityStates.Mage.Weapon.Flamethrower.FixedUpdate += FlameVisuals,
                () => IL.EntityStates.Mage.Weapon.Flamethrower.FixedUpdate -= FlameVisuals);
            if (ChangeGroups.Enabled("Artificer", "Special")) Add(() => On.EntityStates.Mage.Weapon.Flamethrower.OnEnter += FlameEnter,
                () => On.EntityStates.Mage.Weapon.Flamethrower.OnEnter -= FlameEnter);
            if (ChangeGroups.Enabled("Artificer", "Primary")) Add(() => On.EntityStates.Mage.Weapon.FireLightningBolt.ModifyProjectileInfo += Plasma,
                () => On.EntityStates.Mage.Weapon.FireLightningBolt.ModifyProjectileInfo -= Plasma);
            if (ChangeGroups.Enabled("Artificer", "Primary")) Add(() => GlobalEventManager.onServerDamageDealt += Slow,
                () => GlobalEventManager.onServerDamageDealt -= Slow);
            if (ChangeGroups.SwapArtificer) Add(() => On.RoR2.Skills.SkillCatalog.SetSkillFamilies += SwapSkills,
                () => On.RoR2.Skills.SkillCatalog.SetSkillFamilies -= SwapSkills);
            if (ChangeGroups.Enabled("Artificer", "Special")) Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += WallDamageSource,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= WallDamageSource);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Artificer changes ENABLED: acceleration 60; Plasma Bolt 310%/30% slow for 2s, Snapfreeze Special/300% damage, Ion Surge Utility/move-speed scaling capped at 2x base distance, Flamethrower 30m, aim-scaled Tectonic Blast push, Mithrix/False Son boss freeze execution 10%.");
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
        if (artificerBody) artificerBody!.baseAcceleration = originalAcceleration;
        artificerBody = null;
        if (utility != null && originalUtility != null) utility.variants = originalUtility;
        if (special != null && originalSpecial != null) special.variants = originalSpecial;
        if (snapfreezeUnlock != null) snapfreezeUnlock.nameToken = originalUnlockNameToken;
        snapfreezeUnlock = null;
        if (wallDamage != null) wallDamage.damageType = originalWallDamageType;
        wallDamage = null;
        originalUtility = originalSpecial = null;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
    }

    private static void WallInput(ILContext il) => ArtificerBalance.PatchWallInput(il.Body);
    private static void WallRefund(ILContext il)
    {
        if (ChangeGroups.SwapArtificer) ArtificerBalance.PatchWallRefund(il.Body);
        ArtificerBalance.PatchWallDamage(il.Body);
    }
    private static void LaunchSpeed(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(ArtificerBalance.LaunchSpeedSite(il.Body), MoveType.After);
        cursor.EmitDelegate<Func<float, float>>(ArtificerBalance.CappedLaunchSpeed);
    }
    private static void LaunchSource(ILContext il) => ArtificerBalance.PatchLaunchSource(il.Body);

    private static void TectonicPush(ILContext il)
    {
        var aim = il.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().First(f =>
            f.DeclaringType.FullName == "EntityStates.Mage.Weapon.FireEarthShards" && f.Name == "aimVector");
        var cursor = new ILCursor(il);
        cursor.Goto(ArtificerBalance.TectonicPushSite(il.Body), MoveType.After);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldfld, aim);
        cursor.EmitDelegate<Func<float, Vector3, float>>((speed, direction) =>
            speed * ArtificerBalance.TectonicPushMultiplier(direction.y));
    }

    private void Bodies(On.RoR2.BodyCatalog.orig_SetBodyPrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!artificerBody)
            {
                var body = prefabs.Single(p => p.name == "MageBody").GetComponent<CharacterBody>();
                if (!body) throw new InvalidOperationException("Artificer's body prefab was not found.");
                originalAcceleration = body.baseAcceleration;
                artificerBody = body;
                body.baseAcceleration = ArtificerBalance.BaseAcceleration;
            }
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Artificer stat patch failed; disabled Artificer changes. {error}");
        }
        orig(prefabs);
    }

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
                var unlock = s.variants[ion].unlockableDef;
                if (!unlock || unlock.cachedName != "Skills.Mage.FlyUp")
                    throw new InvalidOperationException("Orbital Bombardment's unlock requirement was not found.");
                newU[ice].unlockableDef = null;
                newS[ion].unlockableDef = unlock;
                snapfreezeUnlock = unlock;
                originalUnlockNameToken = unlock.nameToken;
                unlock.nameToken = "T4YLOR_SNAPFREEZE_UNLOCK_NAME";
                utility = u; special = s;
                originalUtility = u.variants; originalSpecial = s.variants;
                u.variants = newU; s.variants = newS;
                log.LogInfo("Artificer skill slots swapped; Ion Surge unlocked by default, Orbital Bombardment unlocks Snapfreeze; other variants preserved.");
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
                if (ChangeGroups.SwapArtificer) damage.damageType.damageSource = DamageSource.Special;
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

    private static string? ArtificerDescription(string token)
    {
        if (ChangeGroups.SwapArtificer)
        {
            if (token == "T4YLOR_SNAPFREEZE_UNLOCK_NAME") return "Artificer: Snapfreeze";
            if (token == "ACHIEVEMENT_MAGEAIRBORNEMULTIKILL_DESCRIPTION")
                return "As Artificer, kill 15 enemies before touching the ground. <color=#C586FF><b>Unlocks Snapfreeze.</b></color>";
        }
        var text = ArtificerBalance.GetDescription(token);
        if (!ChangeGroups.SwapArtificer && text != null)
            text = text.Replace("Special slot.", "").Replace("Utility slot. ", "");
        return text;
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Artificer", token) ? ArtificerDescription(token) : null) ?? orig(self, token);
}
