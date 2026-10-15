using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates.Bandit2.Weapon;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class BanditChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private readonly ConditionalWeakTable<CrosshairController, Transform[]> stockPipLayouts =
        new ConditionalWeakTable<CrosshairController, Transform[]>();
    private SkillDef? rifleSkill;
    private int originalMaxStock;

    internal BanditChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            Add(() => On.RoR2.Skills.SkillCatalog.SetSkillDefs += SkillDefs,
                () => On.RoR2.Skills.SkillCatalog.SetSkillDefs -= SkillDefs);
            Add(() => On.EntityStates.Bandit2.Weapon.Bandit2FirePrimaryBase.OnEnter += FireInterval,
                () => On.EntityStates.Bandit2.Weapon.Bandit2FirePrimaryBase.OnEnter -= FireInterval);
            Add(() => On.EntityStates.Bandit2.Weapon.Bandit2FireRifle.ModifyBullet += ReduceSpread,
                () => On.EntityStates.Bandit2.Weapon.Bandit2FireRifle.ModifyBullet -= ReduceSpread);
            Add(() => On.RoR2.UI.CrosshairController.Awake += ExpandBanditStockDisplay,
                () => On.RoR2.UI.CrosshairController.Awake -= ExpandBanditStockDisplay);
            Add(() => On.RoR2.UI.CrosshairController.LateUpdate += CenterBanditStockPips,
                () => On.RoR2.UI.CrosshairController.LateUpdate -= CenterBanditStockPips);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Bandit changes ENABLED: Blast has 6 rounds, a 0.2s base firing interval, and 50% lower spread and bloom.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Could not enable the full Bandit patch; removed its hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }

    public void Dispose()
    {
        if (rifleSkill != null) rifleSkill.baseMaxStock = originalMaxStock;
        rifleSkill = null;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
    }

    private void SkillDefs(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] skillDefs)
    {
        try
        {
            if (rifleSkill == null)
            {
                var skill = Array.Find(skillDefs, s => s.skillNameToken == "BANDIT2_PRIMARY_ALT_NAME");
                if (!skill) throw new InvalidOperationException("Bandit Blast's rifle SkillDef was not found.");
                rifleSkill = skill;
                originalMaxStock = skill.baseMaxStock;
                skill.baseMaxStock = BanditBalance.BlastBaseCharges;
            }
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Bandit stock patch failed; disabled Bandit changes. {error}");
        }
        orig(skillDefs);
    }

    private static void FireInterval(On.EntityStates.Bandit2.Weapon.Bandit2FirePrimaryBase.orig_OnEnter orig,
        Bandit2FirePrimaryBase self)
    {
        if (self.GetType() == typeof(Bandit2FireRifle))
        {
            // The base state divides this by attack speed, so the interval
            // remains 0.2 seconds at normal speed and scales normally with AS.
            self.minimumBaseDuration = BanditBalance.BlastMinimumBaseInterval;
            self.spreadBloomValue *= BanditBalance.BlastBloomMultiplier;
        }
        orig(self);
    }

    private static void ReduceSpread(On.EntityStates.Bandit2.Weapon.Bandit2FireRifle.orig_ModifyBullet orig,
        Bandit2FireRifle self, BulletAttack attack)
    {
        orig(self, attack);
        attack.spreadPitchScale *= BanditBalance.BlastSpreadMultiplier;
        attack.spreadYawScale *= BanditBalance.BlastSpreadMultiplier;
    }

    private void ExpandBanditStockDisplay(On.RoR2.UI.CrosshairController.orig_Awake orig, CrosshairController self)
    {
        orig(self);
        if (!self || !self.name.StartsWith("Bandit2Crosshair", StringComparison.Ordinal)) return;

        try
        {
            var displays = self.skillStockSpriteDisplays;
            var stockIndexes = new int[4];
            for (int stock = 1; stock <= 4; stock++)
            {
                int index = Array.FindIndex(displays, display =>
                    display.target && display.target.name == $"Stock, {stock}");
                if (index < 0) throw new InvalidOperationException($"Reticle stock pip {stock} was not found.");
                stockIndexes[stock - 1] = index;
            }
            int centerIndex = Array.FindIndex(displays, display =>
                display.target && display.target.name == "Center, Available");
            if (centerIndex < 0) throw new InvalidOperationException("Reticle center marker was not found.");
            int fourthStock = stockIndexes[3];

            for (int i = 0; i < displays.Length; i++)
            {
                var display = displays[i];
                if (!display.target) continue;
                if (Array.IndexOf(stockIndexes, i) >= 0 || i == centerIndex)
                {
                    // A negative maximum means the display has no upper stock limit.
                    // The six stock pips and center marker still have their own minimums.
                    display.maximumStockCountToBeValid = -1;
                    displays[i] = display;
                }
            }

            var sourceDisplay = displays[fourthStock];
            var sourceTransform = sourceDisplay.target.transform;
            var row = sourceTransform.parent;

            Array.Resize(ref self.skillStockSpriteDisplays, displays.Length + 2);
            for (int stock = 5; stock <= 6; stock++)
            {
                var target = UnityEngine.Object.Instantiate(sourceDisplay.target, row, false);
                target.name = $"Stock, {stock}";
                target.SetActive(false);

                var display = sourceDisplay;
                display.target = target;
                display.minimumStockCountToBeValid = stock;
                display.maximumStockCountToBeValid = -1;
                self.skillStockSpriteDisplays[displays.Length + stock - 5] = display;
            }

            var pipTransforms = new Transform[BanditBalance.BlastBaseCharges];
            for (int i = 0; i < stockIndexes.Length; i++)
                pipTransforms[i] = displays[stockIndexes[i]].target.transform;
            for (int stock = 5; stock <= BanditBalance.BlastBaseCharges; stock++)
                pipTransforms[stock - 1] = self.skillStockSpriteDisplays[displays.Length + stock - 5].target.transform;
            stockPipLayouts.Add(self, pipTransforms);
        }
        catch (Exception error)
        {
            log.LogError($"Could not extend Bandit's Blast reticle to six stock pips. {error}");
        }
    }

    private void CenterBanditStockPips(On.RoR2.UI.CrosshairController.orig_LateUpdate orig,
        CrosshairController self)
    {
        orig(self);
        if (!self || !stockPipLayouts.TryGetValue(self, out var pips)) return;

        if (!self.hudElement) return;
        var body = self.hudElement.targetCharacterBody;
        if (!body) return;
        var locator = body.skillLocator;
        if (!locator) return;
        var primary = locator.primary;
        if (!primary) return;

        int stockCount = primary.maxStock;
        for (int i = 0; i < pips.Length; i++)
        {
            if (!pips[i]) continue;
            var position = pips[i].localPosition;
            position.x = BanditBalance.GetStockPipX(i, stockCount);
            pips[i].localPosition = position;
        }
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => BanditBalance.GetDescription(token) ?? orig(self, token);
}
