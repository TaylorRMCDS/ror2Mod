using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using BepInEx.Logging;
using EntityStates.FalseSon;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class FalseSonChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private ConditionalWeakTable<LaserFatherCharged, LaserRuinCounter> counters = new ConditionalWeakTable<LaserFatherCharged, LaserRuinCounter>();
    internal FalseSonChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("False Son", "Passive")) Add(() => IL.RoR2.CharacterBody.ComputeTamperedHeartBonus += Growth,
                () => IL.RoR2.CharacterBody.ComputeTamperedHeartBonus -= Growth);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => IL.RoR2.HealthComponent.TakeDamageProcess += RuinDamage,
                () => IL.RoR2.HealthComponent.TakeDamageProcess -= RuinDamage);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => IL.RoR2.GlobalEventManager.ProcDeathMark += DeathMark,
                () => IL.RoR2.GlobalEventManager.ProcDeathMark -= DeathMark);
            if (ChangeGroups.Enabled("False Son", "Utility")) Add(() => IL.EntityStates.FalseSon.StepBrothers.OnEnter += Step,
                () => IL.EntityStates.FalseSon.StepBrothers.OnEnter -= Step);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => IL.EntityStates.FalseSon.LaserFatherBurstPrep.OnEnter += BurstRefill,
                () => IL.EntityStates.FalseSon.LaserFatherBurstPrep.OnEnter -= BurstRefill);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => IL.EntityStates.FalseSon.LaserFatherCharged.FireBullet += LaserBullet,
                () => IL.EntityStates.FalseSon.LaserFatherCharged.FireBullet -= LaserBullet);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => On.EntityStates.FalseSon.LaserFatherCharged.OnEnter += LaserEnter,
                () => On.EntityStates.FalseSon.LaserFatherCharged.OnEnter -= LaserEnter);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => On.EntityStates.FalseSon.LaserFatherCharged.FireBullet += LaserTick,
                () => On.EntityStates.FalseSon.LaserFatherCharged.FireBullet -= LaserTick);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => On.RoR2.GlobalEventManager.ProcessHitEnemy += LaserHit,
                () => On.RoR2.GlobalEventManager.ProcessHitEnemy -= LaserHit);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => IL.EntityStates.FalseSon.LaserFatherCharged.FixedUpdate += LaserFixed,
                () => IL.EntityStates.FalseSon.LaserFatherCharged.FixedUpdate -= LaserFixed);
            if (ChangeGroups.Enabled("False Son", "Special")) Add(() => On.EntityStates.EntityState.Update += StateUpdate,
                () => On.EntityStates.EntityState.Update -= StateUpdate);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("False Son changes ENABLED: growth 4% speed/2 armor/2 regen; laser refreshes Ruin and applies it every 24 consecutive ticks; Ruin damage/Death Mark, dash procs, Burst refill and laser visuals fixed.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"False Son setup failed; restored values and removed hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }
    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
        counters = new ConditionalWeakTable<LaserFatherCharged, LaserRuinCounter>();
    }

    private static void RuinDamage(ILContext il) => FalseSonBalance.PatchRuinDamage(il.Body);
    private static void LaserFixed(ILContext il) => FalseSonBalance.PatchLaserVisuals(il.Body);
    private static void BurstRefill(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(FalseSonBalance.BurstRoundSite(il.Body), MoveType.Before);
        cursor.EmitDelegate<Func<float, float>>(value => Mathf.Ceil(value));
    }
    private static void DeathMark(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(FalseSonBalance.DeathMarkDotSite(il.Body), MoveType.Before);
        cursor.Remove();
        // Ruin already contributes via its associated buff; omit only its DOT entry.
        cursor.EmitDelegate<Func<DotController, DotController.DotIndex, bool>>((dot, index) =>
            index != DotController.DotIndex.LunarRuin && dot.HasDotActive(index));
    }
    private static void Growth(ILContext il) => FalseSonBalance.PatchGrowth(il.Body);
    private static void Step(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "InitMeleeOverlap"))
            throw new InvalidOperationException("Step of the Brothers overlap creation changed.");
        cursor.EmitDelegate<Func<OverlapAttack, OverlapAttack>>(attack => { attack.procCoefficient = 0f; return attack; });
    }
    private void LaserTick(On.EntityStates.FalseSon.LaserFatherCharged.orig_FireBullet orig,
        LaserFatherCharged self, Transform model, Ray ray, string muzzle, float distance)
    {
        counters.GetValue(self, _ => new LaserRuinCounter()).BeginTick();
        orig(self, model, ray, muzzle, distance);
    }
    private void LaserEnter(On.EntityStates.FalseSon.LaserFatherCharged.orig_OnEnter orig, LaserFatherCharged self)
    {
        counters.Remove(self); // Entity states can be pooled and reused for a later cast.
        orig(self);
    }
    private void LaserBullet(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(FalseSonBalance.BulletFireSite(il.Body), MoveType.Before);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<BulletAttack, LaserFatherCharged, BulletAttack>>((bullet, state) =>
        {
            var counter = counters.GetValue(state, _ => new LaserRuinCounter());
            bullet.modifyOutgoingDamageCallback += (BulletAttack attack, ref BulletAttack.BulletHit hit, DamageInfo info) =>
            {
                var health = hit.hitHurtBox ? hit.hitHurtBox.healthComponent : null;
                if (health && !info.rejected && counter.Hit(health!.GetInstanceID()))
                    info.damageType |= DamageType.LunarRuin;
            };
            return bullet;
        });
    }

    private static void LaserHit(On.RoR2.GlobalEventManager.orig_ProcessHitEnemy orig,
        GlobalEventManager self, DamageInfo info, GameObject victim)
    {
        // The native bullet damage message preserves these fields for remote-authority players.
        // Item procs carry a proc-chain mask or a different inflictor, source or coefficient.
        if (NetworkServer.active && !info.rejected && info.attacker && info.inflictor == info.attacker &&
            info.damageType.damageSource == DamageSource.Special && (info.procChainMask.mask & ~(1u << (int)ProcType.SureProc)) == 0 &&
            info.procCoefficient == LaserFatherCharged.procCoefficientPerTick && info.dotIndex == DotController.DotIndex.None &&
            info.attacker.GetComponent<FalseSonController>())
        {
            var body = victim ? victim.GetComponent<CharacterBody>() : null;
            if (body)
            {
                foreach (var timed in (System.Collections.IEnumerable)TimedBuffs.GetValue(body))
                    if ((BuffIndex)TimedIndex.GetValue(timed) == DLC2Content.Buffs.lunarruin.buffIndex)
                        TimedTimer.SetValue(timed, Mathf.Max((float)TimedTimer.GetValue(timed), (float)TimedDuration.GetValue(timed)));
                var dots = DotController.FindDotController(victim);
                if (dots)
                    foreach (var stack in (System.Collections.IEnumerable)DotStacks.GetValue(dots))
                        if ((DotController.DotIndex)DotIndex.GetValue(stack) == DotController.DotIndex.LunarRuin)
                            DotTimer.SetValue(stack, Mathf.Max((float)DotTimer.GetValue(stack), (float)DotDuration.GetValue(stack)));
            }
        }
        orig(self, info, victim);
    }

    private static readonly FieldInfo DotStacks = typeof(DotController).GetField("dotStackList", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Type DotStackType = typeof(DotController).GetNestedType("DotStack", BindingFlags.NonPublic)!;
    private static readonly FieldInfo DotIndex = DotStackType.GetField("dotIndex")!;
    private static readonly FieldInfo DotTimer = DotStackType.GetField("timer")!;
    private static readonly FieldInfo DotDuration = DotStackType.GetField("totalDuration")!;
    private static readonly FieldInfo TimedBuffs = typeof(CharacterBody).GetField("timedBuffs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly Type TimedBuffType = typeof(CharacterBody).GetNestedType("TimedBuff", BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly FieldInfo TimedIndex = TimedBuffType.GetField("buffIndex")!;
    private static readonly FieldInfo TimedTimer = TimedBuffType.GetField("timer")!;
    private static readonly FieldInfo TimedDuration = TimedBuffType.GetField("totalDuration")!;
    private static readonly Dictionary<string, FieldInfo> VisualFields = BuildVisualFields();
    private static Dictionary<string, FieldInfo> BuildVisualFields()
    {
        var fields = new Dictionary<string, FieldInfo>();
        foreach (string name in new[] { "laserEffect", "laserOriginTransformSprinting", "laserOriginTransformStanding", "laserEffectEnd", "muzzleTransform", "lockedOnHurtBox" })
            fields.Add(name, typeof(LaserFatherCharged).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(LaserFatherCharged).FullName, name));
        return fields;
    }
    private static T VisualField<T>(LaserFatherCharged state, string name) => (T)VisualFields[name].GetValue(state);
    private static void StateUpdate(On.EntityStates.EntityState.orig_Update orig, EntityStates.EntityState self)
    {
        orig(self);
        if (!(self is LaserFatherCharged laser)) return;
        var effect = VisualField<GameObject>(laser, "laserEffect");
        if (!effect || !laser.outer) return;
        var body = laser.outer.GetComponent<CharacterBody>();
        if (!body) return;
        var originTransform = VisualField<Transform>(laser, body.isSprinting ? "laserOriginTransformSprinting" : "laserOriginTransformStanding");
        var effectEnd = VisualField<Transform>(laser, "laserEffectEnd");
        if (!originTransform || !effectEnd) return;
        var visual = effect.transform;
        visual.SetParent(originTransform);
        visual.position = originTransform.position;
        var input = body.inputBank;
        Ray aim = input ? new Ray(input.aimOrigin, input.aimDirection) : new Ray(body.transform.position, body.transform.forward);
        var muzzle = VisualField<Transform>(laser, "muzzleTransform");
        var locked = VisualField<HurtBox>(laser, "lockedOnHurtBox");
        Vector3 origin = muzzle ? muzzle.position : aim.origin;
        Vector3 end;
        int mask = LayerIndex.world.mask | LayerIndex.entityPrecise.mask;
        if (locked) end = locked.transform.position;
        else
        {
            TrajectoryAimAssist.ApplyTrajectoryAimAssist(ref aim, LaserFatherCharged.maxDistance, body.gameObject, 1f);
            end = Util.CharacterRaycast(body.gameObject, aim, out var hit, LaserFatherCharged.maxDistance, mask,
                QueryTriggerInteraction.Ignore) ? hit.point : aim.GetPoint(LaserFatherCharged.maxDistance);
        }
        Ray beam = new Ray(origin, end - origin);
        if (Util.CharacterRaycast(body.gameObject, beam, out var obstruction, (end - origin).magnitude, mask,
            QueryTriggerInteraction.UseGlobal)) end = obstruction.point;
        visual.rotation = Util.QuaternionSafeLookRotation(end - origin);
        effectEnd.position = end;
    }    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("False Son", token) ? FalseSonBalance.GetDescription(token) : null) ?? orig(self, token);
}
