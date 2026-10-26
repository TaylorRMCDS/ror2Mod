using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates.Loader;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class LoaderChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private CharacterBody? loaderBody;
    private float originalHealth;
    private float originalArmor;
    private sealed class GrappleTime
    {
        internal ProjectileStickOnImpact? stick;
        internal bool attached;
        internal float seconds;
    }
    private readonly ConditionalWeakTable<FireHook, GrappleTime> grappleTimes = new ConditionalWeakTable<FireHook, GrappleTime>();

    internal LoaderChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Loader", "Stats")) Add(() => On.RoR2.BodyCatalog.SetBodyPrefabs += Bodies,
                () => On.RoR2.BodyCatalog.SetBodyPrefabs -= Bodies);
            if (ChangeGroups.Enabled("Loader", "Secondary")) Add(() => On.EntityStates.Loader.FireHook.FixedUpdate += GrappleUpdate,
                () => On.EntityStates.Loader.FireHook.FixedUpdate -= GrappleUpdate);
            if (ChangeGroups.Enabled("Loader", "Secondary")) Add(() => On.EntityStates.Loader.FireHook.SetHookReference += GrappleReference,
                () => On.EntityStates.Loader.FireHook.SetHookReference -= GrappleReference);
            if (ChangeGroups.Enabled("Loader", "Secondary")) Add(() => On.EntityStates.Loader.FireHook.OnExit += GrappleExit,
                () => On.EntityStates.Loader.FireHook.OnExit -= GrappleExit);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Loader changes ENABLED: 130 health, 10 armor and Grapple Fist refunds half its cooldown after attachments of 1s or less.");
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
        log.LogError($"Loader setup failed; restored its values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        if (loaderBody)
        {
            loaderBody!.baseMaxHealth = originalHealth;
            loaderBody.baseArmor = originalArmor;
        }

        loaderBody = null;

        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"Loader hook cleanup failed: {error}"); }
        cleanup.Clear();
    }

    private void Bodies(On.RoR2.BodyCatalog.orig_SetBodyPrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!loaderBody)
            {
                loaderBody = prefabs.Single(p => p.name == "LoaderBody").GetComponent<CharacterBody>();
                if (!loaderBody) throw new InvalidOperationException("Loader body prefab layout changed.");
                originalHealth = loaderBody!.baseMaxHealth;
                originalArmor = loaderBody.baseArmor;
                loaderBody.baseMaxHealth = LoaderBalance.BaseHealth;
                loaderBody.baseArmor = LoaderBalance.BaseArmor;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void GrappleReference(On.EntityStates.Loader.FireHook.orig_SetHookReference orig,
        FireHook self, GameObject hook)
    {
        orig(self, hook);
        if (self.GetType() == typeof(FireHook) && hook)
            grappleTimes.GetValue(self, _ => new GrappleTime()).stick = hook.GetComponent<ProjectileStickOnImpact>();
    }

    private void GrappleUpdate(On.EntityStates.Loader.FireHook.orig_FixedUpdate orig, FireHook self)
    {
        // FireYankHook derives from FireHook; only the base skill earns a refund.
        if (self.GetType() == typeof(FireHook) && Util.HasEffectiveAuthority(self.outer.networkIdentity) &&
            grappleTimes.TryGetValue(self, out var time) && time.stick && time.stick!.stuck)
        {
            time.attached = true;
            time.seconds += Time.fixedDeltaTime;
        }
        orig(self);
    }

    private void GrappleExit(On.EntityStates.Loader.FireHook.orig_OnExit orig, FireHook self)
    {
        bool refund = self.GetType() == typeof(FireHook) && Util.HasEffectiveAuthority(self.outer.networkIdentity) &&
            grappleTimes.TryGetValue(self, out var time) &&
            LoaderBalance.QuickGrapple(time.attached, time.seconds);
        var skill = self.activatorSkillSlot;
        orig(self);
        grappleTimes.Remove(self);
        // Direct credit also works while native begin-on-skill-end blocks RunRecharge.
        if (refund && skill && skill.stock < skill.maxStock)
            skill.rechargeStopwatch += skill.GetFinalRechargeInterval() * 0.5f;
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Loader", token) ? LoaderBalance.GetDescription(token) : null) ?? orig(self, token);
}
