using System;
using System.Collections.Generic;
using BepInEx.Logging;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class CaptainChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();

    internal CaptainChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Captain", "Secondary")) Add(() => GlobalEventManager.onServerDamageDealt += TaserSlow,
                () => GlobalEventManager.onServerDamageDealt -= TaserSlow);
            Add(() => On.RoR2.Skills.CaptainOrbitalSkillDef.GetCurrentIcon += OrbitalIcon,
                () => On.RoR2.Skills.CaptainOrbitalSkillDef.GetCurrentIcon -= OrbitalIcon);
            Add(() => On.RoR2.Skills.CaptainOrbitalSkillDef.GetCurrentNameToken += OrbitalName,
                () => On.RoR2.Skills.CaptainOrbitalSkillDef.GetCurrentNameToken -= OrbitalName);
            Add(() => On.RoR2.Skills.CaptainOrbitalSkillDef.GetCurrentDescriptionToken += OrbitalDescription,
                () => On.RoR2.Skills.CaptainOrbitalSkillDef.GetCurrentDescriptionToken -= OrbitalDescription);
            Add(() => On.RoR2.Skills.CaptainOrbitalSkillDef.CanExecute += OrbitalCanExecute,
                () => On.RoR2.Skills.CaptainOrbitalSkillDef.CanExecute -= OrbitalCanExecute);
            Add(() => On.RoR2.Skills.CaptainOrbitalSkillDef.IsReady += OrbitalIsReady,
                () => On.RoR2.Skills.CaptainOrbitalSkillDef.IsReady -= OrbitalIsReady);
            if (ChangeGroups.Enabled("Captain", "Special")) Add(() => On.RoR2.HealingWard.HealOccupants += HealingBeacon,
                () => On.RoR2.HealingWard.HealOccupants -= HealingBeacon);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Captain changes ENABLED: taser slows shock-immune enemies by 50% for 5s; orbital Utility/Special available in hidden realms; Healing Beacon converts full-health ticks into barrier up to 20% maximum health.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Could not enable the full Captain patch; removed its hooks. {error}");
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
            catch (Exception error) { log.LogError($"Captain hook cleanup failed: {error}"); }
        cleanup.Clear();
    }

    private static bool OrbitalEnabled(GenericSkill slot) => slot && slot.characterBody && slot.characterBody.skillLocator &&
        ChangeGroups.Enabled("Captain", slot == slot.characterBody.skillLocator.special ? "Special" : "Utility");

    private static T WithoutOrbitalBlock<T>(GenericSkill slot, Func<T> action)
    {
        var scene = SceneCatalog.mostRecentSceneDef;
        if (!OrbitalEnabled(slot) || !scene || !scene.blockOrbitalSkills) return action();
        scene.blockOrbitalSkills = false;
        try { return action(); }
        finally { scene.blockOrbitalSkills = true; }
    }

    private static Sprite OrbitalIcon(On.RoR2.Skills.CaptainOrbitalSkillDef.orig_GetCurrentIcon orig,
        CaptainOrbitalSkillDef self, GenericSkill slot) => WithoutOrbitalBlock(slot, () => orig(self, slot));

    private static string OrbitalName(On.RoR2.Skills.CaptainOrbitalSkillDef.orig_GetCurrentNameToken orig,
        CaptainOrbitalSkillDef self, GenericSkill slot) => WithoutOrbitalBlock(slot, () => orig(self, slot));

    private static string OrbitalDescription(On.RoR2.Skills.CaptainOrbitalSkillDef.orig_GetCurrentDescriptionToken orig,
        CaptainOrbitalSkillDef self, GenericSkill slot) => WithoutOrbitalBlock(slot, () => orig(self, slot));

    private static bool OrbitalCanExecute(On.RoR2.Skills.CaptainOrbitalSkillDef.orig_CanExecute orig,
        CaptainOrbitalSkillDef self, GenericSkill slot) => WithoutOrbitalBlock(slot, () => orig(self, slot));

    private static bool OrbitalIsReady(On.RoR2.Skills.CaptainOrbitalSkillDef.orig_IsReady orig,
        CaptainOrbitalSkillDef self, GenericSkill slot) => WithoutOrbitalBlock(slot, () => orig(self, slot));

    private static bool IsCaptainHealingBeacon(HealingWard ward)
    {
        // The ward is spawned separately from the supply drop carrying the name token.
        return CaptainBalance.IsHealingWardName(ward.gameObject.name);
    }

    private static void TaserSlow(DamageReport report)
    {
        if (!NetworkServer.active || !report.attackerBody || !report.victimBody ||
            !report.victim.alive || report.damageInfo.rejected ||
            report.attackerTeamIndex == report.victimTeamIndex) return;
        var info = report.damageInfo;
        if (report.attackerBody.baseNameToken != "CAPTAIN_BODY_NAME" ||
            info.damageType.damageSource != DamageSource.Secondary ||
            (info.damageType.damageType & DamageType.Shock5s) == 0 ||
            info.dotIndex != DotController.DotIndex.None || info.procCoefficient <= 0f ||
            !info.procChainMask.Equals(default(ProcChainMask))) return;
        var hurt = report.victimBody.GetComponent<SetStateOnHurt>();
        if (hurt && hurt.canBeStunned && hurt.targetStateMachine) return;
        report.victimBody.AddTimedBuff(RoR2Content.Buffs.Slow50, CaptainBalance.TaserSlowDuration);
    }

    private static void HealingBeacon(On.RoR2.HealingWard.orig_HealOccupants orig, HealingWard self)
    {
        if (!NetworkServer.active || !IsCaptainHealingBeacon(self))
        {
            orig(self);
            return;
        }

        var recipients = new List<HealthComponent>();
        var teamFilter = self.GetComponent<TeamFilter>();
        if (teamFilter)
        {
            var radiusSquared = self.radius * self.radius;
            var center = self.transform.position;
            foreach (var member in TeamComponent.GetTeamMembers(teamFilter.teamIndex))
            {
                var health = member ? member.GetComponent<HealthComponent>() : null;
                if (!health || !health!.alive || (member!.transform.position - center).sqrMagnitude > radiusSquared)
                    continue;
                if (health!.combinedHealth >= health.fullCombinedHealth)
                    recipients.Add(health);
            }
        }

        orig(self);

        foreach (var health in recipients)
        {
            if (!health || !health.alive) continue;
            var healingTick = self.healPoints + health.fullHealth * self.healFraction;
            var grant = CaptainBalance.GetBarrierGrant(health.barrier, health.fullHealth, healingTick);
            if (grant > 0f) health.AddBarrier(grant);
        }
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Captain", token) ? CaptainBalance.GetDescription(token) : null) ?? orig(self, token);
}
