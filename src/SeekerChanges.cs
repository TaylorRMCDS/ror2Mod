using System;
using System.Collections.Generic;
using BepInEx.Logging;
using MonoMod.Cil;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class SeekerChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    internal SeekerChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Seeker", "Utility")) Add(() => IL.RoR2.ReprieveVehicle.OnPassengerEnter += ReprieveEnter,
                () => IL.RoR2.ReprieveVehicle.OnPassengerEnter -= ReprieveEnter);
            if (ChangeGroups.Enabled("Seeker", "Utility")) Add(() => IL.RoR2.ReprieveVehicle.MyFixedUpdate += ReprieveSpeed,
                () => IL.RoR2.ReprieveVehicle.MyFixedUpdate -= ReprieveSpeed);
            if (ChangeGroups.Enabled("Seeker", "Utility")) Add(() => IL.RoR2.ReprieveVehicle.FixedUpdateMovement += ReprieveTurn,
                () => IL.RoR2.ReprieveVehicle.FixedUpdateMovement -= ReprieveTurn);
            if (ChangeGroups.Enabled("Seeker", "Special")) Add(() => IL.RoR2.SeekerController.UnlockGateEffects += MeditateGrace,
                () => IL.RoR2.SeekerController.UnlockGateEffects -= MeditateGrace);
            if (ChangeGroups.Enabled("Seeker", "Special")) Add(() => On.RoR2.SeekerController.UnlockPalmBlastEffects += PalmGrace,
                () => On.RoR2.SeekerController.UnlockPalmBlastEffects -= PalmGrace);
            if (ChangeGroups.Enabled("Seeker", "Special")) Add(() => On.RoR2.HealthComponent.UpdateLastHitTime += LowHealthGrace,
                () => On.RoR2.HealthComponent.UpdateLastHitTime -= LowHealthGrace);
            if (ChangeGroups.Enabled("Seeker", "Special")) Add(() => On.RoR2.Skills.SkillCatalog.SetSkillDefs += SkillDefs,
                () => On.RoR2.Skills.SkillCatalog.SetSkillDefs -= SkillDefs);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Seeker changes ENABLED: Reprieve speed -10%, turning scales linearly from normal at starting speed to -80% at maximum speed; Saving Grace replaces Seeker self-revive with native low-health potion heal; Palm Blast grants Saving Grace at 7 Tranquility. Meditate ally revives and Grace stats preserved.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Seeker setup failed; removed hooks and restored skill keywords. {error}");
        }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }
    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
    }
    private static void ReprieveEnter(ILContext il) => SeekerBalance.PatchReprieveSpeed(il.Body, true);
    private static void ReprieveSpeed(ILContext il) => SeekerBalance.PatchReprieveSpeed(il.Body, false);
    private static void ReprieveTurn(ILContext il) => SeekerBalance.PatchReprieveTurn(il.Body);
    private static void MeditateGrace(ILContext il) => SeekerBalance.PatchSelfReviveGrant(il.Body);

    private static void PalmGrace(On.RoR2.SeekerController.orig_UnlockPalmBlastEffects orig, SeekerController self, byte gate)
    {
        orig(self, gate);
        if (!NetworkServer.active || gate < SeekerBalance.MaxTranquility) return;
        // Match Meditate's living-player branch only; Palm Blast never invokes the ally respawn path.
        foreach (var player in PlayerCharacterMasterController.instances)
        {
            if (!player || !player.isConnected) continue;
            var master = player.master;
            var body = master ? master.GetBody() : null;
            if (!body || !body!.healthComponent || !body.healthComponent.alive || master!.seekerRevivedOnce ||
                body.HasBuff(DLC2Content.Buffs.RevitalizeBuff) || body.HasBuff(DLC2Content.Buffs.SeekerRevivedOnce)) continue;
            master!.seekerRevitalize = true;
            body.AddUnmanagedBuff(DLC2Content.Buffs.RevitalizeBuff.buffIndex, 1);
        }
    }

    private static void LowHealthGrace(On.RoR2.HealthComponent.orig_UpdateLastHitTime orig, HealthComponent self,
        float damage, Vector3 position, bool silent, GameObject attacker, bool delayedDamage, bool firstHitOfDelayedDamage)
    {
        if (NetworkServer.active && damage > 0f && self.body && self.body.master)
        {
            var body = self.body;
            var master = body.master;
            if (SeekerBalance.CanConsumeGrace(self.alive, self.isHealthLow, body.GetComponent<SeekerController>(),
                body.HasBuff(DLC2Content.Buffs.RevitalizeBuff), master.seekerRevivedOnce,
                body.HasBuff(DLC4Content.Buffs.DisableDefenses)))
            {
                // Consume before healing, so heal-triggered effects cannot re-enter and spend it twice.
                master.seekerSelfRevive = false;
                master.seekerRevitalize = false;
                master.seekerRevivedOnce = true;
                body.RemoveUnmanagedBuff(DLC2Content.Buffs.RevitalizeBuff, 1);
                if (!body.HasBuff(DLC2Content.Buffs.SeekerRevivedOnce)) body.AddBuff(DLC2Content.Buffs.SeekerRevivedOnce);
                HealthComponent.TriggerHealthPotion(self);
            }
        }
        orig(self, damage, position, silent, attacker, delayedDamage, firstHitOfDelayedDamage);
    }

    private void SkillDefs(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] defs)
    {
        foreach (var def in defs)
        {
            if (def.skillNameToken != "SEEKER_SPECIAL_ALT1_NAME") continue;
            string[] original = def.keywordTokens;
            if (Array.IndexOf(original, "KEYWORD_LOWHEALTHPROTECTION") >= 0) continue;
            var keywords = new List<string>(original) { "KEYWORD_LOWHEALTHPROTECTION" };
            cleanup.Add(() => { if (def) def.keywordTokens = original; });
            def.keywordTokens = keywords.ToArray();
        }
        orig(defs);
    }
    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Seeker", token) ? SeekerBalance.GetDescription(token) : null) ?? orig(self, token);
}
