using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class MultChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private ProjectileExplosion? scrapExplosion;
    private BlastAttack.FalloffModel originalScrapFalloff;
    private CharacterBody? multBody;
    private float originalAcceleration;
    private SkillDef? scrapSkill;
    private bool originalAttackSpeedRestock;
    private float originalRestockMultiplier;

    internal MultChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("MUL-T", "Primary")) Add(() => On.RoR2.Skills.SkillCatalog.SetSkillDefs += Skills,
                () => On.RoR2.Skills.SkillCatalog.SetSkillDefs -= Skills);
            if (ChangeGroups.Enabled("MUL-T", "Stats")) Add(() => On.RoR2.BodyCatalog.SetBodyPrefabs += Bodies,
                () => On.RoR2.BodyCatalog.SetBodyPrefabs -= Bodies);
            if (ChangeGroups.Enabled("MUL-T", "Primary")) Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += Projectiles,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= Projectiles);
            if (ChangeGroups.Enabled("MUL-T", "Primary")) Add(() => GlobalEventManager.onCharacterDeathServer += SawKill,
                () => GlobalEventManager.onCharacterDeathServer -= SawKill);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("MUL-T changes ENABLED: acceleration 60; Scrap Launcher has no blast falloff and reloads with attack speed; Power-Saw kills grant 0.5s Regenerative.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void Add(Action install, Action remove)
    {
        cleanup.Add(remove);
        install();
    }

    private void Fail(Exception error)
    {
        Dispose();
        log.LogError($"MUL-T setup failed; restored its values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        if (scrapSkill)
        {
            scrapSkill!.attackSpeedBuffsRestockSpeed = originalAttackSpeedRestock;
            scrapSkill.attackSpeedBuffsRestockSpeed_Multiplier = originalRestockMultiplier;
        }
        scrapSkill = null;
        if (multBody) multBody!.baseAcceleration = originalAcceleration;
        multBody = null;
        if (scrapExplosion) scrapExplosion!.falloffModel = originalScrapFalloff;
        scrapExplosion = null;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"MUL-T hook cleanup failed: {error}"); }
        cleanup.Clear();
    }

    private void Skills(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] skills)
    {
        try
        {
            if (!scrapSkill)
            {
                var skill = skills.Single(s => s.skillNameToken == "TOOLBOT_PRIMARY_ALT2_NAME");
                originalAttackSpeedRestock = skill.attackSpeedBuffsRestockSpeed;
                originalRestockMultiplier = skill.attackSpeedBuffsRestockSpeed_Multiplier;
                scrapSkill = skill;
                skill.attackSpeedBuffsRestockSpeed = true;
                skill.attackSpeedBuffsRestockSpeed_Multiplier = 1f;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(skills);
    }

    private void Bodies(On.RoR2.BodyCatalog.orig_SetBodyPrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!multBody)
            {
                var body = prefabs.Single(p => p.name == "ToolbotBody").GetComponent<CharacterBody>();
                if (!body) throw new InvalidOperationException("MUL-T's body prefab was not found.");
                originalAcceleration = body.baseAcceleration;
                multBody = body;
                body.baseAcceleration = MultBalance.BaseAcceleration;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void Projectiles(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!scrapExplosion)
            {
                var prefab = prefabs.Single(p => p.name == "ToolbotGrenadeLauncherProjectile");
                var explosion = prefab.GetComponent<ProjectileImpactExplosion>();
                if (!explosion || explosion.blastRadius <= 0f)
                    throw new InvalidOperationException("Scrap Launcher's explosion prefab layout changed.");
                scrapExplosion = explosion;
                originalScrapFalloff = explosion.falloffModel;
                explosion.falloffModel = BlastAttack.FalloffModel.None;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private static void SawKill(DamageReport report)
    {
        if (!NetworkServer.active || !report.attackerBody || !report.victimBody ||
            report.attackerTeamIndex == report.victimTeamIndex || report.damageInfo.rejected) return;
        var body = report.attackerBody!;
        var info = report.damageInfo;
        if (body.baseNameToken != "TOOLBOT_BODY_NAME" || info.inflictor != body.gameObject ||
            info.dotIndex != DotController.DotIndex.None || !info.procChainMask.Equals(default(ProcChainMask))) return;
        // The native saw tags its actual weapon slot, including Power Mode's
        // second weapon. The server receives that tag in client overlap reports.
        var slots = body.skillLocator;
        if (!slots) return;
        var slot = info.damageType.damageSource == DamageSource.Primary ? slots!.primary :
            info.damageType.damageSource == DamageSource.Secondary ? slots!.secondary : null;
        if (slot && slot!.skillDef && slot.skillDef.skillNameToken == "TOOLBOT_PRIMARY_ALT3_NAME")
            body.AddTimedBuff(RoR2Content.Buffs.CrocoRegen, MultBalance.PowerSawRegenDuration);
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("MUL-T", token) ? MultBalance.GetDescription(token) : null) ?? orig(self, token);
}
