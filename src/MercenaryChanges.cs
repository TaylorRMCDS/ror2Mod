using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates;
using EntityStates.Merc.Weapon;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class MercenaryChanges : IDisposable
{
    private sealed class SkillActivation { }

    private sealed class Swing
    {
        internal CharacterBody body = null!;
        internal GroundLight2 primary = null!;
        internal MercenaryBalance.HitboxLifetime lifetime = null!;
        internal OverlapAttack attack = null!;
        internal Animator animator = null!;
        internal HitBoxGroup originalGroup = null!;
        internal GameObject? preservedVolume;
        internal EntityStateMachine? abilityMachine;
        internal EntityState? abilityState;
        internal bool abilityPending;
    }

    private static BuffDef? readyBuff;
    private static bool contentHookInstalled;
    private static ManualLogSource? contentLog;
    private static MercenaryChanges? active;
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private readonly Dictionary<CharacterBody, Swing> swings = new Dictionary<CharacterBody, Swing>();
    private readonly HashSet<OverlapAttack> retainedAttacks = new HashSet<OverlapAttack>();
    private ConditionalWeakTable<EntityState, MercenaryBalance.HitboxLifetime> lifetimes =
        new ConditionalWeakTable<EntityState, MercenaryBalance.HitboxLifetime>();
    private ConditionalWeakTable<EntityState, SkillActivation> activations =
        new ConditionalWeakTable<EntityState, SkillActivation>();

    internal MercenaryChanges(ManualLogSource log) => this.log = log;

    // Keep the buff catalog identical when the survivor switch is off.
    internal static void RegisterContent(ManualLogSource log)
    {
        if (contentHookInstalled) return;
        contentLog = log;
        On.RoR2.BuffCatalog.SetBuffDefs += BuffDefs;
        contentHookInstalled = true;
    }

    private static void BuffDefs(On.RoR2.BuffCatalog.orig_SetBuffDefs orig, BuffDef[] defs)
    {
        try
        {
            if (!readyBuff)
            {
                var expose = defs.Single(b => b.name == "bdMercExpose");
                if (!expose.iconSprite)
                    throw new InvalidOperationException("The native Expose buff icon was not found.");
                readyBuff = ScriptableObject.CreateInstance<BuffDef>();
                readyBuff.name = "Expose Ready";
                readyBuff.iconSprite = expose.iconSprite;
                readyBuff.buffColor = expose.buffColor;
                readyBuff.canStack = false;
                readyBuff.isDebuff = false;
                readyBuff.isHidden = false;
                readyBuff.ignoreGrowthNectar = true;
            }
            defs = defs.Contains(readyBuff!) ? defs : defs.Concat(new[] { readyBuff! }).ToArray();
        }
        catch (Exception error)
        {
            active?.Dispose();
            contentLog?.LogError($"Mercenary buff registration failed; disabled its gameplay changes. {error}");
        }
        orig(defs);
    }

    internal void Enable()
    {
        active = this;
        try
        {
            if (ChangeGroups.Enabled("Mercenary", "Secondary")) Add(() => IL.EntityStates.Merc.Uppercut.OnEnter += RisingThunderEnter,
                () => IL.EntityStates.Merc.Uppercut.OnEnter -= RisingThunderEnter);
            var movement = new MercenaryMovementChanges();
            cleanup.Add(movement.Dispose);
            movement.Enable();
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.EntityStates.BasicMeleeAttack.GetShouldFire += PrimaryHitboxGate,
                () => On.EntityStates.BasicMeleeAttack.GetShouldFire -= PrimaryHitboxGate);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.EntityStates.Merc.Weapon.GroundLight2.AuthorityModifyOverlapAttack += CapturePrimaryOverlap,
                () => On.EntityStates.Merc.Weapon.GroundLight2.AuthorityModifyOverlapAttack -= CapturePrimaryOverlap);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.RoR2.Skills.SkillDef.InstantiateNextState += SkillState,
                () => On.RoR2.Skills.SkillDef.InstantiateNextState -= SkillState);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.RoR2.EntityStateMachine.SetState += Transition,
                () => On.RoR2.EntityStateMachine.SetState -= Transition);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.RoR2.EntityStateMachine.SetNextState += QueueState,
                () => On.RoR2.EntityStateMachine.SetNextState -= QueueState);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.EntityStates.EntityState.Reset += ResetState,
                () => On.EntityStates.EntityState.Reset -= ResetState);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => On.RoR2.OverlapAttack.Reset += ResetOverlap,
                () => On.RoR2.OverlapAttack.Reset -= ResetOverlap);
            if (ChangeGroups.Enabled("Mercenary", "Primary")) Add(() => RoR2Application.onFixedUpdate += Tick,
                () => RoR2Application.onFixedUpdate -= Tick);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Mercenary changes ENABLED: third-swing Expose Ready hitbox persistence; Rising Thunder 600% damage, 25% shorter base duration and bounded travel distance; Whirlwind, both utilities and Eviscerate entry dash preserve travel as attack speed changes.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Mercenary setup failed; removed its gameplay hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }

    private static void RisingThunderEnter(ILContext il) => MercenaryBalance.PatchUppercutEnter(il.Body);

    public void Dispose()
    {
        if (active == this) active = null;
        foreach (var swing in swings.Values.ToArray()) Finish(swing);
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"Mercenary hook cleanup failed: {error}"); }
        cleanup.Clear();
        if (NetworkServer.active && readyBuff)
            foreach (var body in CharacterBody.readOnlyInstancesList)
                if (body && body.HasBuff(readyBuff!)) body.RemoveBuff(readyBuff!);
        lifetimes = new ConditionalWeakTable<EntityState, MercenaryBalance.HitboxLifetime>();
        activations = new ConditionalWeakTable<EntityState, SkillActivation>();
        retainedAttacks.Clear();
    }

    private bool PrimaryHitboxGate(On.EntityStates.BasicMeleeAttack.orig_GetShouldFire orig,
        BasicMeleeAttack self)
    {
        if (!(self is GroundLight2 primary) || primary.step != 2 || !readyBuff) return orig(self);
        var lifetime = lifetimes.GetValue(self, _ => new MercenaryBalance.HitboxLifetime());
        if (lifetime.Stage == MercenaryBalance.HitboxStage.Ability ||
            lifetime.Stage == MercenaryBalance.HitboxStage.Finished) return false;
        bool shouldFire = orig(self);
        lifetime.ObservePrimary(shouldFire);
        if (!shouldFire && lifetime.Stage == MercenaryBalance.HitboxStage.Finished)
        {
            var body = self.outer.GetComponent<CharacterBody>();
            if (body && swings.TryGetValue(body, out var swing) && swing.lifetime == lifetime)
                Finish(swing);
        }
        return shouldFire;
    }

    private void CapturePrimaryOverlap(On.EntityStates.Merc.Weapon.GroundLight2.orig_AuthorityModifyOverlapAttack orig,
        GroundLight2 self, OverlapAttack attack)
    {
        // Use the actual argument after native Expose setup. IL hooks run on a
        // generated method whose declaring type does not own the melee fields.
        orig(self, attack);
        CaptureSwing(self, attack);
    }

    private void CaptureSwing(BasicMeleeAttack state, OverlapAttack attack)
    {
        if (!(state is GroundLight2 primary) || primary.step != 2 || !readyBuff ||
            !lifetimes.TryGetValue(state, out var lifetime) ||
            lifetime.Stage != MercenaryBalance.HitboxStage.Primary) return;
        var body = state.outer.GetComponent<CharacterBody>();
        if (!body || swings.ContainsKey(body) || !attack.hitBoxGroup) return;
        var locator = body.GetComponent<ModelLocator>();
        var animator = locator && locator.modelTransform ? locator.modelTransform.GetComponent<Animator>() : null;
        if (!animator) return;
        var swing = new Swing
        {
            body = body, primary = primary, lifetime = lifetime, attack = attack,
            animator = animator!, originalGroup = attack.hitBoxGroup
        };
        swings.Add(body, swing);
        body.AddBuffAuthority(readyBuff!.buffIndex);
    }

    private EntityState SkillState(On.RoR2.Skills.SkillDef.orig_InstantiateNextState orig,
        SkillDef self, GenericSkill skill)
    {
        var state = orig(self, skill);
        if (state != null && skill.characterBody && swings.ContainsKey(skill.characterBody))
            activations.GetValue(state, _ => new SkillActivation());
        return state!;
    }

    private void Transition(On.RoR2.EntityStateMachine.orig_SetState orig,
        EntityStateMachine machine, EntityState next)
    {
        var body = machine.GetComponent<CharacterBody>();
        if (body && swings.TryGetValue(body, out var swing))
        {
            try
            {
                if (swing.lifetime.Stage == MercenaryBalance.HitboxStage.Ability)
                {
                    if (machine == swing.abilityMachine)
                    {
                        if (swing.abilityPending && next == swing.abilityState)
                            swing.abilityPending = false;
                        // A fresh activation ends the prior ability, even if it
                        // reuses the same preparation/damage state types.
                        else if (!swing.abilityPending && !activations.TryGetValue(next, out _) && swing.abilityState != null &&
                            MercenaryBalance.ContinuesAbility(swing.abilityState.GetType().FullName!, next.GetType().FullName!))
                            swing.abilityState = next;
                        else Finish(swing);
                    }
                }
                else if (activations.TryGetValue(next, out _) && IsPrimaryActive(swing))
                {
                    PreserveVolume(swing);
                    swing.lifetime.Extend(true);
                    swing.abilityMachine = machine;
                    swing.abilityState = next;
                    retainedAttacks.Add(swing.attack);
                }
                else if (machine == swing.primary.outer) Finish(swing);
            }
            catch (Exception error)
            {
                Finish(swing);
                log.LogError($"Could not retain Mercenary's third-swing hitbox; cleared that swing. {error}");
            }
        }
        orig(machine, next);
    }

    private void QueueState(On.RoR2.EntityStateMachine.orig_SetNextState orig,
        EntityStateMachine machine, EntityState next)
    {
        var body = machine.GetComponent<CharacterBody>();
        if (body && swings.TryGetValue(body, out var swing))
        {
            try
            {
                // Skill activation queues its state for a later update. Capture
                // the active sword volume now, before its animation can close.
                if (swing.lifetime.Stage == MercenaryBalance.HitboxStage.Primary &&
                    activations.TryGetValue(next, out _) && IsPrimaryActive(swing))
                {
                    PreserveVolume(swing);
                    swing.lifetime.Extend(true);
                    swing.abilityMachine = machine;
                    swing.abilityState = next;
                    swing.abilityPending = true;
                    retainedAttacks.Add(swing.attack);
                }
                else if (swing.abilityPending && machine == swing.abilityMachine && next != swing.abilityState)
                    Finish(swing);
            }
            catch (Exception error)
            {
                Finish(swing);
                log.LogError($"Could not retain Mercenary's queued skill hitbox; cleared that swing. {error}");
            }
        }
        orig(machine, next);
    }

    private static bool IsPrimaryActive(Swing swing) =>
        swing.primary.outer && swing.primary.outer.state == swing.primary && swing.animator &&
        (string.IsNullOrEmpty(swing.primary.mecanimHitboxActiveParameter) ||
            swing.animator.GetFloat(swing.primary.mecanimHitboxActiveParameter) > 0.5f);

    private static void PreserveVolume(Swing swing)
    {
        var locator = swing.body.GetComponent<ModelLocator>();
        if (!locator || !locator.modelTransform)
            throw new InvalidOperationException("Mercenary's model root was not found.");
        var root = new GameObject("TaylorMercRetainedThirdSwing");
        swing.preservedVolume = root;
        root.transform.SetParent(locator.modelTransform, false);
        var group = root.AddComponent<HitBoxGroup>();
        group.groupName = "TaylorMercRetainedThirdSwing";
        var boxes = new List<HitBox>();
        foreach (var original in swing.originalGroup.hitBoxes)
        {
            if (!original || !original.enabled || !original.gameObject.activeInHierarchy) continue;
            var boxObject = new GameObject("RetainedSwordHitbox");
            boxObject.layer = original.gameObject.layer;
            boxObject.transform.position = original.transform.position;
            boxObject.transform.rotation = original.transform.rotation;
            boxObject.transform.localScale = original.transform.lossyScale;
            // Follow the model root without inheriting the cancelled sword pose.
            boxObject.transform.SetParent(root.transform, true);
            var box = boxObject.AddComponent<HitBox>();
            box.showAsSphere = original.showAsSphere;
            boxes.Add(box);
        }
        if (boxes.Count == 0) throw new InvalidOperationException("The active sword hitbox was not found.");
        group.hitBoxes = boxes.ToArray();
        swing.attack.hitBoxGroup = group;
    }

    private void Tick()
    {
        foreach (var swing in swings.Values.ToArray())
        {
            if (!swings.TryGetValue(swing.body, out var current) || current != swing) continue;
            try
            {
                if (!swing.body || !swing.body.healthComponent || !swing.body.healthComponent.alive ||
                    !Util.HasEffectiveAuthority(swing.body.gameObject)) { Finish(swing); continue; }
                if (swing.lifetime.Stage == MercenaryBalance.HitboxStage.Primary)
                {
                    if (!IsPrimaryActive(swing)) Finish(swing);
                }
                else if (!swing.abilityMachine ||
                    (!swing.abilityPending && swing.abilityMachine!.state != swing.abilityState))
                    Finish(swing);
                else
                    // Preserve the original victim ledger: one hit per enemy
                    // for this swing, even when its lifetime is extended.
                    swing.attack.Fire(null);
            }
            catch (Exception error)
            {
                Finish(swing);
                log.LogError($"Mercenary retained hitbox failed; cleared its hitbox and buff. {error}");
            }
        }
    }

    private void ResetOverlap(On.RoR2.OverlapAttack.orig_Reset orig, OverlapAttack self)
    {
        // Primary-state pooling must not erase a retained attack or its victims.
        if (!retainedAttacks.Contains(self)) orig(self);
    }

    private void ResetState(On.EntityStates.EntityState.orig_Reset orig, EntityState self)
    {
        lifetimes.Remove(self);
        activations.Remove(self);
        orig(self);
    }

    private void Finish(Swing swing)
    {
        if (!swings.TryGetValue(swing.body, out var current) || current != swing) return;
        swings.Remove(swing.body);
        swing.lifetime.Finish();
        retainedAttacks.Remove(swing.attack);
        swing.attack.hitBoxGroup = swing.originalGroup;
        if (swing.preservedVolume) UnityEngine.Object.Destroy(swing.preservedVolume!);
        if (swing.body && readyBuff) swing.body.RemoveBuffAuthority(readyBuff!.buffIndex);
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token)
    {
        return (ChangeGroups.DescriptionEnabled("Mercenary", token) ? MercenaryBalance.GetDescription(token) : null) ?? orig(self, token);
    }
}
