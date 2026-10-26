using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EntityStates;
using EntityStates.Merc;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class MercenaryMovementChanges : IDisposable
{
    private sealed class Phase
    {
        internal float nominalDuration, duration, previousTime, moveSpeed;
        internal MovementIntegral? horizontal, vertical;
        internal readonly MovementWindow window = new MovementWindow();
    }

    private readonly List<Action> cleanup = new List<Action>();
    private ConditionalWeakTable<EntityState, Phase> phases = new();
    private static readonly Dictionary<(Type, string), FieldInfo> fields = new();

    // Look up original game types, never the generated IL hook's declaring type.
    private static FieldInfo Field(Type type, string name)
    {
        if (fields.TryGetValue((type, name), out var cached)) return cached;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field != null) { fields.Add((type, name), field); return field; }
        }
        throw new InvalidOperationException($"Movement field {type.FullName}.{name} was not found.");
    }

    private static float Read(EntityState state, string name) => (float)Field(state.GetType(), name).GetValue(state);

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }

    internal void Enable()
    {
        if (ChangeGroups.Enabled("Mercenary", "Secondary")) Add(() => On.EntityStates.Merc.Uppercut.OnEnter += UppercutEnter,
            () => On.EntityStates.Merc.Uppercut.OnEnter -= UppercutEnter);
        if (ChangeGroups.Enabled("Mercenary", "Secondary")) Add(() => IL.EntityStates.Merc.Uppercut.FixedUpdate += UppercutMovement,
            () => IL.EntityStates.Merc.Uppercut.FixedUpdate -= UppercutMovement);
        if (ChangeGroups.Enabled("Mercenary", "Secondary")) Add(() => On.EntityStates.Merc.WhirlwindBase.OnEnter += WhirlwindEnter,
            () => On.EntityStates.Merc.WhirlwindBase.OnEnter -= WhirlwindEnter);
        if (ChangeGroups.Enabled("Mercenary", "Secondary")) Add(() => IL.EntityStates.Merc.WhirlwindBase.FixedUpdate += WhirlwindMovement,
            () => IL.EntityStates.Merc.WhirlwindBase.FixedUpdate -= WhirlwindMovement);
        if (ChangeGroups.Enabled("Mercenary", "Utility")) Add(() => On.EntityStates.Merc.Assaulter2.OnEnter += AssaulterEnter,
            () => On.EntityStates.Merc.Assaulter2.OnEnter -= AssaulterEnter);
        if (ChangeGroups.Enabled("Mercenary", "Utility")) Add(() => On.EntityStates.Merc.FocusedAssaultDash.OnEnter += FocusedEnter,
            () => On.EntityStates.Merc.FocusedAssaultDash.OnEnter -= FocusedEnter);
        if (ChangeGroups.Enabled("Mercenary", "Utility")) Add(() => IL.EntityStates.Merc.Assaulter2.AuthorityFixedUpdate += DashMovement,
            () => IL.EntityStates.Merc.Assaulter2.AuthorityFixedUpdate -= DashMovement);
        if (ChangeGroups.Enabled("Mercenary", "Utility")) Add(() => IL.EntityStates.Merc.FocusedAssaultDash.AuthorityFixedUpdate += DashMovement,
            () => IL.EntityStates.Merc.FocusedAssaultDash.AuthorityFixedUpdate -= DashMovement);
        if (ChangeGroups.Enabled("Mercenary", "Utility")) Add(() => IL.EntityStates.Merc.PrepAssaulter2.OnEnter += PrepAnimation,
            () => IL.EntityStates.Merc.PrepAssaulter2.OnEnter -= PrepAnimation);
        if (ChangeGroups.Enabled("Mercenary", "Special")) Add(() => On.EntityStates.Merc.EvisDash.OnEnter += EvisEnter,
            () => On.EntityStates.Merc.EvisDash.OnEnter -= EvisEnter);
        if (ChangeGroups.Enabled("Mercenary", "Special")) Add(() => IL.EntityStates.Merc.EvisDash.OnEnter += EvisTiming,
            () => IL.EntityStates.Merc.EvisDash.OnEnter -= EvisTiming);
        if (ChangeGroups.Enabled("Mercenary", "Special")) Add(() => IL.EntityStates.Merc.EvisDash.FixedUpdate += EvisMovement,
            () => IL.EntityStates.Merc.EvisDash.FixedUpdate -= EvisMovement);
        Add(() => On.EntityStates.EntityState.Reset += Reset,
            () => On.EntityStates.EntityState.Reset -= Reset);
    }

    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--) cleanup[i]();
        cleanup.Clear();
        phases = new();
    }

    private Phase Begin(EntityState state, float nominal, float duration)
    {
        phases.Remove(state);
        var phase = new Phase { nominalDuration = nominal, duration = duration, moveSpeed = Read(state, "moveSpeedStat") };
        phases.Add(state, phase);
        return phase;
    }

    private void UppercutEnter(On.EntityStates.Merc.Uppercut.orig_OnEnter orig, Uppercut self)
    {
        orig(self);
        var phase = Begin(self, Read(self, "baseDuration"), Read(self, "duration"));
        float coefficient = Read(self, "moveSpeedBonusCoefficient");
        phase.horizontal = new MovementIntegral(t => coefficient * (1f - t));
        var curve = (AnimationCurve)Field(typeof(Uppercut), "yVelocityCurve").GetValue(null);
        phase.vertical = new MovementIntegral(curve.Evaluate);
    }

    private void WhirlwindEnter(On.EntityStates.Merc.WhirlwindBase.orig_OnEnter orig, WhirlwindBase self)
    {
        orig(self);
        var phase = Begin(self, Read(self, "baseDuration"), Read(self, "duration"));
        float coefficient = Read(self, "moveSpeedBonusCoefficient");
        phase.horizontal = new MovementIntegral(t => coefficient + (1f - coefficient) * t);
    }

    private static void EmitAge(ILCursor cursor, ILContext il)
    {
        var age = il.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
            .First(m => m.DeclaringType.FullName == "EntityStates.EntityState" && m.Name == "get_fixedAge");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Call, age);
    }

    private void UppercutMovement(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(MercenaryBalance.UppercutMovementSite(il.Body), MoveType.Before);
        cursor.Emit(OpCodes.Ldarg_0);
        EmitAge(cursor, il);
        cursor.EmitDelegate<Func<Vector3, EntityState, float, Vector3>>(CurveMovement);
    }

    private void WhirlwindMovement(ILContext il)
    {
        var sites = il.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
            f.FullName == "UnityEngine.Vector3 RoR2.CharacterMotor::velocity" && i.Previous.OpCode == OpCodes.Ldloc_0).ToArray();
        if (sites.Length != 1) throw new InvalidOperationException("Whirlwind movement site changed.");
        var cursor = new ILCursor(il);
        cursor.Goto(sites[0], MoveType.Before);
        cursor.Emit(OpCodes.Ldarg_0);
        EmitAge(cursor, il);
        cursor.EmitDelegate<Func<Vector3, EntityState, float, Vector3>>(CurveMovement);
    }

    private Vector3 CurveMovement(Vector3 native, EntityState state, float age)
    {
        if (!phases.TryGetValue(state, out var phase) || phase.duration <= 0f) return native;
        var body = state.outer.GetComponent<CharacterBody>();
        if (!body || !body.hasEffectiveAuthority || !body.characterMotor || !body.characterDirection) return native;
        float previous = phase.previousTime;
        float t = Mathf.Max(previous, Mathf.Clamp01(age / phase.duration));
        phase.previousTime = Mathf.Max(previous, t);
        float horizontal = phase.nominalDuration * phase.moveSpeed *
            (phase.horizontal!.At(t) - phase.horizontal.At(previous));
        var direction = body.characterDirection.forward;
        direction.y = 0f;
        var desired = direction * horizontal;
        var controlled = new Vector3(native.x, 0f, native.z);
        if (phase.vertical != null)
        {
            desired.y = phase.nominalDuration * (phase.vertical.At(t) - phase.vertical.At(previous));
            controlled.y = native.y;
        }
        // Root motion carries only the correction; leave the unscaled native
        // velocity for gravity, collisions, hit-stop and subsequent airborne motion.
        body.characterMotor.rootMotion += desired - controlled * Time.fixedDeltaTime;
        return native;
    }

    private void BeginDash(EntityState self) => Begin(self, Read(self, "baseDuration"), Read(self, "duration"));

    private void AssaulterEnter(On.EntityStates.Merc.Assaulter2.orig_OnEnter orig, Assaulter2 self)
    {
        var ignore = Field(typeof(BasicMeleeAttack), "ignoreAttackSpeed");
        var original = ignore.GetValue(self);
        ignore.SetValue(self, false);
        try { orig(self); BeginDash(self); }
        finally { ignore.SetValue(self, original); }
    }

    private void FocusedEnter(On.EntityStates.Merc.FocusedAssaultDash.orig_OnEnter orig, FocusedAssaultDash self)
    {
        var ignore = Field(typeof(BasicMeleeAttack), "ignoreAttackSpeed");
        var original = ignore.GetValue(self);
        ignore.SetValue(self, false);
        try { orig(self); BeginDash(self); }
        finally { ignore.SetValue(self, original); }
    }

    private void DashMovement(ILContext il)
    {
        var stores = il.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
            f.FullName == "UnityEngine.Vector3 RoR2.CharacterMotor::rootMotion").ToArray();
        if (stores.Length != 1 || !(stores[0].Previous.Operand is MethodReference add) || add.Name != "op_Addition")
            throw new InvalidOperationException("Mercenary dash root-motion site changed.");
        var cursor = new ILCursor(il);
        cursor.Goto(stores[0].Previous, MoveType.Before);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<Vector3, EntityState, Vector3>>(DashStep);
    }

    private Vector3 DashStep(Vector3 native, EntityState self)
    {
        if (!phases.TryGetValue(self, out var phase) || phase.duration <= 0f) return native;
        float step = phase.window.Take(phase.duration, Time.fixedDeltaTime);
        return native * (phase.nominalDuration / phase.duration) * (step / Time.fixedDeltaTime);
    }

    private static void PrepAnimation(ILContext il)
    {
        var reads = il.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference f && f.Name == "baseDuration").ToArray();
        if (reads.Length != 2) throw new InvalidOperationException("Blinding Assault preparation animation changed.");
        var cursor = new ILCursor(il);
        cursor.Goto(reads[1], MoveType.After);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<float, EntityState, float>>((duration, state) =>
            duration / Mathf.Max(0.01f, Read(state, "attackSpeedStat")));
    }

    private void EvisEnter(On.EntityStates.Merc.EvisDash.orig_OnEnter orig, EvisDash self)
    {
        phases.Remove(self);
        orig(self);
        Begin(self, Read(self, "dashDuration"), Read(self, "dashDuration") /
            Mathf.Max(0.01f, Read(self, "attackSpeedStat")));
    }

    private static void EvisTiming(ILContext il)
    {
        var reads = il.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f &&
            f.DeclaringType.FullName == "EntityStates.Merc.EvisDash" &&
            (f.Name == "dashDuration" || f.Name == "dashPrepDuration")).ToArray();
        if (reads.Length == 0) throw new InvalidOperationException("Eviscerate dash timing reads changed.");
        foreach (var read in reads)
        {
            var cursor = new ILCursor(il);
            cursor.Goto(read, MoveType.After);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<float, EntityState, float>>((duration, state) =>
                duration / Mathf.Max(0.01f, Read(state, "attackSpeedStat")));
        }
    }

    private void EvisMovement(ILContext il) { EvisTiming(il); DashMovement(il); }

    private void Reset(On.EntityStates.EntityState.orig_Reset orig, EntityState self)
    {
        phases.Remove(self);
        orig(self);
    }
}
