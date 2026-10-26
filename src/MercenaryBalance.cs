using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class MercenaryBalance
{
    internal const string ConfigSection = "Mercenary";
    internal const string ConfigKey = "EnableMercenaryChanges";
    internal const string ConfigDescription =
        "Enable Expose Ready, Rising Thunder tuning, and attack-speed-independent travel for Whirlwind, both utilities and Eviscerate's entry dash. Restart after changing this setting.";

    internal const float RisingThunderDamage = 6f;
    internal const float RisingThunderDurationMultiplier = 0.75f;

    internal static void PatchUppercutEnter(MethodBody body)
    {
        var duration = body.Instructions.Single(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f &&
            f.DeclaringType.FullName == "EntityStates.Merc.Uppercut" && f.Name == "baseDuration");
        var damage = body.Instructions.Single(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f &&
            f.DeclaringType.FullName == "EntityStates.Merc.Uppercut" && f.Name == "baseDamageCoefficient");
        if (duration.Next.OpCode != OpCodes.Ldarg_0 || !(duration.Next.Next.Operand is FieldReference speed) ||
            speed.Name != "attackSpeedStat" || duration.Next.Next.Next.OpCode != OpCodes.Div)
            throw new InvalidOperationException("Rising Thunder duration layout changed.");
        var processor = body.GetILProcessor();
        var scale = Instruction.Create(OpCodes.Ldc_R4, RisingThunderDurationMultiplier);
        processor.InsertAfter(duration, scale);
        processor.InsertAfter(scale, Instruction.Create(OpCodes.Mul));
        damage.OpCode = OpCodes.Ldc_R4;
        damage.Operand = RisingThunderDamage;
    }

    internal static Instruction UppercutMovementSite(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
            f.FullName == "UnityEngine.Vector3 RoR2.CharacterMotor::velocity" && i.Previous.OpCode == OpCodes.Ldloc_1).ToArray();
        if (sites.Length != 1 || !body.Instructions.Any(i => i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "UnityEngine.AnimationCurve" && m.Name == "Evaluate"))
            throw new InvalidOperationException("Rising Thunder movement curve layout changed.");
        return sites[0];
    }

    internal enum HitboxStage { Waiting, Primary, Ability, Finished }

    internal sealed class HitboxLifetime
    {
        internal HitboxStage Stage { get; private set; }

        internal void ObservePrimary(bool hitboxActive)
        {
            if (Stage == HitboxStage.Waiting && hitboxActive) Stage = HitboxStage.Primary;
            else if (Stage == HitboxStage.Primary && !hitboxActive) Finish();
        }

        internal bool Extend(bool hitboxStillActive)
        {
            if (Stage != HitboxStage.Primary || !hitboxStillActive) return false;
            Stage = HitboxStage.Ability;
            return true;
        }

        internal void Finish() => Stage = HitboxStage.Finished;
    }

    internal static bool ContinuesAbility(string from, string to) => (from, to) switch
    {
        ("EntityStates.Merc.WhirlwindEntry", "EntityStates.Merc.WhirlwindGround") or
        ("EntityStates.Merc.WhirlwindEntry", "EntityStates.Merc.WhirlwindAir") or
        ("EntityStates.Merc.PrepAssaulter2", "EntityStates.Merc.Assaulter2") or
        ("EntityStates.Merc.FocusedAssaultPrep", "EntityStates.Merc.FocusedAssaultDash") or
        ("EntityStates.Merc.EvisDash", "EntityStates.Merc.Evis") => true,
        _ => false
    };

    internal static string? GetDescription(string token) => token switch
    {
        "MERC_PRIMARY_DESCRIPTION" => "<style=cIsUtility>Agile</style>. Slice in front for <style=cIsDamage>130% damage</style>. Every 3rd hit strikes in a greater area and <style=cIsUtility>Exposes</style> enemies. <color=#C586FF><b>Gain Expose Ready while the third swing's hitbox is active. Using a skill during this window keeps that sword hitbox and buff active until the skill finishes or is interrupted. The sword follows you, retaining its original damage, Expose and already-hit enemies; it cannot repeatedly hit the same enemy.</b></color>",
        "MERC_SECONDARY_ALT1_DESCRIPTION" => "Unleash a slicing uppercut, dealing <style=cIsDamage><color=#C586FF><b>600%</b></color> damage</style> and sending you airborne. <color=#C586FF><b>Base duration is 25% shorter. Attack speed shortens the movement animation without reducing travel distance.</b></color>",
        "MERC_SECONDARY_DESCRIPTION" => "Quickly slice horizontally twice, dealing <style=cIsDamage>2x200% damage</style>. If airborne, slice vertically instead. <color=#C586FF><b>Attack speed shortens the movement animation without reducing travel distance.</b></color>",
        "MERC_UTILITY_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Dash forward, dealing <style=cIsDamage>300% damage</style>. If you hit an enemy, <style=cIsDamage>you can dash again</style>, up to <style=cIsDamage>3</style> total. <color=#C586FF><b>Attack speed shortens the movement animation without reducing travel distance.</b></color>",
        "MERC_UTILITY_ALT1_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Dash forward, dealing <style=cIsDamage>700% damage</style> and <style=cIsUtility>Exposing</style> enemies after <style=cIsUtility>1 second</style>. <color=#C586FF><b>Attack speed shortens the movement animation without reducing travel distance.</b></color>",
        "MERC_SPECIAL_DESCRIPTION" => "Target the nearest enemy, attacking them for <style=cIsDamage>110% damage</style> repeatedly. <style=cIsUtility>You cannot be hit for the duration</style>. <color=#C586FF><b>Attack speed shortens the preparation and entry dash without reducing base dash distance; the target-searching and damage phase remain unchanged.</b></color>",
        _ => null
    };
}

// Integrate the movement curve first, then sample displacement over normalized
// phase time. Each fixed tick consumes a bounded slice, including the last tick.
internal sealed class MovementIntegral
{
    private readonly float[] area = new float[1025];
    internal MovementIntegral(Func<float, float> curve)
    {
        float previous = curve(0f);
        for (int i = 1; i < area.Length; i++)
        {
            float current = curve(i / 1024f);
            area[i] = area[i - 1] + (previous + current) / 2048f;
            previous = current;
        }
    }
    internal float At(float time)
    {
        float t = Math.Max(0f, Math.Min(1f, time)) * 1024f;
        int i = Math.Min(1023, (int)t);
        return area[i] + (area[i + 1] - area[i]) * (t - i);
    }
}

internal sealed class MovementWindow
{
    private float elapsed;
    internal float Take(float duration, float deltaTime)
    {
        float step = Math.Max(0f, Math.Min(deltaTime, duration - elapsed));
        elapsed += step;
        return step;
    }
}
