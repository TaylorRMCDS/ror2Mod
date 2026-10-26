using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates;
using EntityStates.Chef;
using KinematicCharacterController;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class ChefChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new();
    private ConditionalWeakTable<IceBox, IceBoxVolley> iceVolleys = new();
    private readonly System.Random spreadRandom = new();
    private readonly Dictionary<KinematicCharacterMotor, (StepHandlingMethod step, float slope)> rollMotors = new();
    internal ChefChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("CHEF", "Utility")) Add(() => On.EntityStates.Chef.RolyPoly.StartRolyPoly += RollStarted,
                () => On.EntityStates.Chef.RolyPoly.StartRolyPoly -= RollStarted);
            if (ChangeGroups.Enabled("CHEF", "Utility")) Add(() => On.EntityStates.Chef.RolyPoly.OnExit += RollExited,
                () => On.EntityStates.Chef.RolyPoly.OnExit -= RollExited);
            if (ChangeGroups.Enabled("CHEF", "Utility")) Add(() => On.RoR2.CharacterMotor.AfterCharacterUpdate += RollAfterMovement,
                () => On.RoR2.CharacterMotor.AfterCharacterUpdate -= RollAfterMovement);
            if (ChangeGroups.Enabled("CHEF", "Secondary")) Add(() => IL.EntityStates.Chef.IceBox.FireIceCube += IceSpread,
                () => IL.EntityStates.Chef.IceBox.FireIceCube -= IceSpread);
            if (ChangeGroups.Enabled("CHEF", "Secondary")) Add(() => IL.EntityStates.Chef.IceBox.FireBoostedIceCube += BoostedDamage,
                () => IL.EntityStates.Chef.IceBox.FireBoostedIceCube -= BoostedDamage);
            if (ChangeGroups.Enabled("CHEF", "Special")) Add(() => IL.EntityStates.Chef.YesChef.OnEnter += PreserveRoll,
                () => IL.EntityStates.Chef.YesChef.OnEnter -= PreserveRoll);
            if (ChangeGroups.Enabled("CHEF", "Special")) Add(() => On.EntityStates.GenericCharacterMain.HandleMovements += YesChefMovement,
                () => On.EntityStates.GenericCharacterMain.HandleMovements -= YesChefMovement);
            if (ChangeGroups.Enabled("CHEF", "Special")) Add(() => On.EntityStates.GenericCharacterMain.PerformInputs += YesChefInputs,
                () => On.EntityStates.GenericCharacterMain.PerformInputs -= YesChefInputs);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            if (ChangeGroups.Enabled("CHEF", "Utility"))
            {
            var method = typeof(KinematicCharacterMotor).GetMethod("GetObstructionNormal",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(Vector3), typeof(bool) }, null)
                ?? throw new MissingMethodException("KCC.GetObstructionNormal");
            var hook = new Hook(method, (ObstructionHook)RollObstruction);
            cleanup.Add(hook.Dispose);
            var projection = typeof(KinematicCharacterMotor).GetMethod("HandleVelocityProjection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException("KCC.HandleVelocityProjection");
            var projectionHook = new Hook(projection, (ProjectionHook)RollProjection);
            cleanup.Add(projectionHook.Dispose);
            }
            log.LogInfo("CHEF changes ENABLED: Ice Box randomly selects one straight cube per cast, with two random trajectories and 1000% boost; Yes CHEF preserves Roll; Roll disables step climbing, caps slopes at 35 degrees and prevents collision/crest launches.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"CHEF setup failed; removed its hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { install(); cleanup.Add(remove); }
    public void Dispose()
    {
        foreach (var motor in rollMotors.Keys.ToArray()) RestoreRollMotor(motor);
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
        iceVolleys = new();
    }

    private void IceSpread(ILContext il)
    {
        var site = il.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is Mono.Cecil.FieldReference f && f.Name == "rotation");
        var cursor = new ILCursor(il);
        cursor.Goto(site, MoveType.Before);
        cursor.Emit(OpCodes.Ldarg_0);
        var count = (Mono.Cecil.FieldReference)il.Body.Instructions.First(i =>
            i.Operand is Mono.Cecil.FieldReference f && f.Name == "iceCubesFired").Operand;
        cursor.Emit(OpCodes.Ldfld, count);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<Quaternion, int, IceBox, Quaternion>>((rotation, fired, state) =>
        {
            var offset = iceVolleys.GetValue(state, _ => new IceBoxVolley(spreadRandom)).Offset(fired);
            return rotation * Quaternion.Euler(offset.pitch, offset.yaw, 0f);
        });
    }

    private static void BoostedDamage(ILContext il)
    {
        var site = il.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldfld &&
            i.Operand is Mono.Cecil.FieldReference f && f.Name == "boostedProjectileDmgCoefficient");
        var cursor = new ILCursor(il);
        cursor.Goto(site, MoveType.After);
        cursor.Emit(OpCodes.Pop);
        cursor.Emit(OpCodes.Ldc_R4, ChefBalance.BoostedIceDamage);
    }

    private static bool IsRolling(GameObject body) => body &&
        EntityStateMachine.FindByCustomName(body, "Body")?.state is RolyPoly;

    private static void PreserveRoll(ILContext il)
    {
        var site = il.Body.Instructions.Single(i => i.Operand is Mono.Cecil.MethodReference m &&
            m.Name == "FindByCustomName");
        var cursor = new ILCursor(il);
        cursor.Goto(site, MoveType.After);
        // The native null branch skips both blockBodySkills and the blocking state.
        cursor.EmitDelegate<Func<EntityStateMachine, EntityStateMachine?>>(machine =>
            machine && machine.state is RolyPoly ? null : machine);
    }

    private static void YesChefMovement(On.EntityStates.GenericCharacterMain.orig_HandleMovements orig,
        GenericCharacterMain self)
    {
        // Yes CHEF lives on Weapon; Roll remains the sole movement controller.
        if (self is YesChef && self.outer && IsRolling(self.outer.gameObject)) return;
        orig(self);
    }

    private static void YesChefInputs(On.EntityStates.GenericCharacterMain.orig_PerformInputs orig,
        GenericCharacterMain self)
    {
        if (self is YesChef && self.outer && IsRolling(self.outer.gameObject)) return;
        orig(self);
    }

    private delegate Vector3 ObstructionOrig(KinematicCharacterMotor self, Vector3 normal, bool stable);
    private delegate Vector3 ObstructionHook(ObstructionOrig orig, KinematicCharacterMotor self, Vector3 normal, bool stable);
    private static Vector3 RollObstruction(ObstructionOrig orig, KinematicCharacterMotor self, Vector3 normal, bool stable)
    {
        var result = orig(self, normal, stable);
        // Treat unwalkable upward-facing terrain as a vertical wall while rolling.
        // This prevents the collision solver from converting forward speed into lift.
        float up = Vector3.Dot(normal, self.CharacterUp);
        if (!stable && up > 0f && up < Mathf.Cos(self.MaxStableSlopeAngle * Mathf.Deg2Rad) &&
            IsRolling(self.gameObject))
        {
            var horizontal = Vector3.ProjectOnPlane(result, self.CharacterUp);
            if (horizontal.sqrMagnitude > 0.0001f) return horizontal.normalized;
        }
        return result;
    }

    private void RollStarted(On.EntityStates.Chef.RolyPoly.orig_StartRolyPoly orig, RolyPoly self)
    {
        var motor = self.outer ? self.outer.GetComponent<CharacterMotor>()?.Motor : null;
        if (motor && !rollMotors.ContainsKey(motor))
        {
            rollMotors.Add(motor, (motor.StepHandling, motor.MaxStableSlopeAngle));
            motor.StepHandling = StepHandlingMethod.None;
            motor.MaxStableSlopeAngle = Mathf.Min(motor.MaxStableSlopeAngle, ChefBalance.RollSlopeLimit);
        }
        try { orig(self); }
        catch { if (motor) RestoreRollMotor(motor); throw; }
    }

    private void RollExited(On.EntityStates.Chef.RolyPoly.orig_OnExit orig, RolyPoly self)
    {
        var motor = self.outer ? self.outer.GetComponent<CharacterMotor>()?.Motor : null;
        try { orig(self); }
        finally { if (motor) RestoreRollMotor(motor); }
    }

    private void RestoreRollMotor(KinematicCharacterMotor motor)
    {
        if (rollMotors.TryGetValue(motor, out var settings))
        {
            if (motor) { motor.StepHandling = settings.step; motor.MaxStableSlopeAngle = settings.slope; }
            rollMotors.Remove(motor);
        }
    }

    private delegate void ProjectionOrig(KinematicCharacterMotor self, ref Vector3 velocity, Vector3 normal, bool stable);
    private delegate void ProjectionHook(ProjectionOrig orig, KinematicCharacterMotor self, ref Vector3 velocity, Vector3 normal, bool stable);
    private static void RollProjection(ProjectionOrig orig, KinematicCharacterMotor self,
        ref Vector3 velocity, Vector3 normal, bool stable)
    {
        float before = Vector3.Dot(velocity, self.CharacterUp);
        orig(self, ref velocity, normal, stable);
        if (!stable && IsRolling(self.gameObject))
        {
            float after = Vector3.Dot(velocity, self.CharacterUp);
            float gained = after - ChefBalance.CollisionUpSpeed(before, after);
            if (gained > 0f) velocity -= self.CharacterUp * gained;
        }
    }

    private void RollAfterMovement(On.RoR2.CharacterMotor.orig_AfterCharacterUpdate orig, CharacterMotor self, float deltaTime)
    {
        var motor = self.Motor;
        if (motor && rollMotors.ContainsKey(motor) && ChefBalance.CancelCrestLift(
            motor.LastGroundingStatus.IsStableOnGround, motor.GroundingStatus.IsStableOnGround, motor.MustUnground()))
        {
            // Ground-slope tangents must not become a launch when crossing an edge.
            float upward = Vector3.Dot(motor.BaseVelocity, motor.CharacterUp);
            if (upward > 0f) motor.BaseVelocity -= motor.CharacterUp * upward;
        }
        orig(self, deltaTime);
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("CHEF", token) ? ChefBalance.GetDescription(token) : null) ?? orig(self, token);
}
