using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EntityStates.Engi.EngiWeapon;
using EntityStates.Engi.Mine;
using RoR2;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class EngineerChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private CharacterBody? engineerBody;
    private CharacterBody? carbonizerBody;
    private float originalBaseDamage;
    private float originalLevelDamage;
    private float originalCarbonizerRegen;
    private float originalGrenadeTotalDuration;
    private float originalGrenadeMaxChargeTime;
    private bool grenadeChanged;

    internal EngineerChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            Add(() => On.RoR2.BodyCatalog.SetBodyPrefabs += Bodies,
                () => On.RoR2.BodyCatalog.SetBodyPrefabs -= Bodies);
            if (ChangeGroups.Enabled("Engineer", "Primary")) Add(() => RoR2Application.onLoad += ApplyGrenadeCharge,
                () => RoR2Application.onLoad -= ApplyGrenadeCharge);
            if (ChangeGroups.Enabled("Engineer", "Secondary")) Add(() => On.EntityStates.Engi.Mine.BaseMineArmingState.OnEnter += FullMine,
                () => On.EntityStates.Engi.Mine.BaseMineArmingState.OnEnter -= FullMine);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Engineer changes ENABLED: 13 (+2.6/level) damage, 10% faster grenade charge, 1000% fully armed Pressure Mines and TR58 base regeneration 2.");
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
        log.LogError($"Engineer setup failed; restored its values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        if (engineerBody)
        {
            engineerBody!.baseDamage = originalBaseDamage;
            engineerBody.levelDamage = originalLevelDamage;
        }
        if (carbonizerBody) carbonizerBody!.baseRegen = originalCarbonizerRegen;
        if (grenadeChanged)
        {
            ChargeGrenades.baseTotalDuration = originalGrenadeTotalDuration;
            ChargeGrenades.baseMaxChargeTime = originalGrenadeMaxChargeTime;
        }
        engineerBody = null;
        carbonizerBody = null;
        grenadeChanged = false;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"Engineer hook cleanup failed: {error}"); }
        cleanup.Clear();
    }

    private void Bodies(On.RoR2.BodyCatalog.orig_SetBodyPrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!engineerBody)
            {
                var engineer = prefabs.Single(p => p.name == "EngiBody").GetComponent<CharacterBody>();
                var carbonizer = prefabs.Single(p => p.name == "EngiWalkerTurretBody").GetComponent<CharacterBody>();
                if (!engineer || !carbonizer)
                    throw new InvalidOperationException("Engineer body prefab layout changed.");
                engineerBody = engineer;
                carbonizerBody = carbonizer;
                originalBaseDamage = engineer.baseDamage;
                originalLevelDamage = engineer.levelDamage;
                originalCarbonizerRegen = carbonizer.baseRegen;
                if (ChangeGroups.Enabled("Engineer", "Stats"))
                {
                    engineer.baseDamage = EngineerBalance.BaseDamage;
                    engineer.levelDamage = EngineerBalance.LevelDamage;
                }
                if (ChangeGroups.Enabled("Engineer", "Special")) carbonizer.baseRegen = EngineerBalance.CarbonizerBaseRegen;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void ApplyGrenadeCharge()
    {
        try
        {
            if (grenadeChanged) return;
            if (ChargeGrenades.baseTotalDuration <= 0f || ChargeGrenades.baseMaxChargeTime <= 0f)
                throw new InvalidOperationException("Bouncing Grenades' charge timing was not initialized.");
            originalGrenadeTotalDuration = ChargeGrenades.baseTotalDuration;
            originalGrenadeMaxChargeTime = ChargeGrenades.baseMaxChargeTime;
            ChargeGrenades.baseTotalDuration *= EngineerBalance.GrenadeChargeDurationMultiplier;
            ChargeGrenades.baseMaxChargeTime *= EngineerBalance.GrenadeChargeDurationMultiplier;
            grenadeChanged = true;
        }
        catch (Exception error) { Fail(error); }
    }

    private static void FullMine(On.EntityStates.Engi.Mine.BaseMineArmingState.orig_OnEnter orig,
        BaseMineArmingState self)
    {
        if (self.GetType() == typeof(MineArmingFull))
            self.damageScale = EngineerBalance.PressureMineFullDamageScale;
        orig(self);
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Engineer", token) ? EngineerBalance.GetDescription(token) : null) ?? orig(self, token);
}
