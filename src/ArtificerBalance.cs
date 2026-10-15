using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class ArtificerBalance
{
    internal const float PlasmaDamage = 3.1f;
    internal const float SlowDuration = 2f;
    internal const float LaunchSpeed = 7f;
    internal const float FlameRange = 30f;
    internal const float FlameLengthScale = 1.5f;

    internal static string? GetDescription(string token) => token switch
    {
        "MAGE_PRIMARY_LIGHTNING_DESCRIPTION" => "<style=cIsDamage>Rapid</style>. Fire a bolt for <style=cIsDamage>310% damage</style> that explodes in a small area and <style=cIsUtility>slows enemies by 30% for 2 seconds</style>. Hold up to 4.",
        "MAGE_SPECIAL_FIRE_DESCRIPTION" => "<style=cIsDamage>Ignite</style>. Burn all enemies in front of you for <style=cIsDamage>2000% damage</style> over 3 seconds. Reaches <style=cIsUtility>30 meters</style>.",
        "MAGE_SPECIAL_LIGHTNING_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Burst into the sky, dealing <style=cIsDamage>800% damage</style>. Launch speed is unaffected by movement speed.",
        _ => null
    };

    internal static void PatchWallInput(MethodBody body) => ReplaceField(body,
        "RoR2.InputBankTest", "skill3", "skill4");
    internal static void PatchWallRefund(MethodBody body) => ReplaceField(body,
        "RoR2.SkillLocator", "utility", "special");

    private static void ReplaceField(MethodBody body, string type, string from, string to)
    {
        var matches = body.Instructions.Where(i => i.Operand is FieldReference f &&
            f.DeclaringType.FullName == type && f.Name == from).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one {type}.{from}, found {matches.Length}.");
        var field = (FieldReference)matches[0].Operand;
        matches[0].Operand = new FieldReference(to, field.FieldType, field.DeclaringType);
    }

    internal static void PatchLaunchSpeed(MethodBody body)
    {
        var matches = body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld &&
            i.Operand is FieldReference f && f.Name == "moveSpeedStat").ToArray();
        if (matches.Length != 1 || matches[0].Previous.OpCode != OpCodes.Ldarg_0)
            throw new InvalidOperationException("Ion Surge movement layout changed.");
        // Remove the receiver as well as the field read, keeping the stack balanced.
        matches[0].Previous.OpCode = OpCodes.Nop;
        matches[0].Previous.Operand = null;
        matches[0].OpCode = OpCodes.Ldc_R4;
        matches[0].Operand = LaunchSpeed;
    }

    internal static void PatchLaunchSource(MethodBody body)
    {
        var matches = body.Instructions.Where(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.FullName == "RoR2.DamageSource RoR2.DamageTypeCombo::damageSource").ToArray();
        if (matches.Length != 1 || matches[0].Previous.OpCode != OpCodes.Ldc_I4_8)
            throw new InvalidOperationException("Ion Surge damage source layout changed.");
        matches[0].Previous.OpCode = OpCodes.Ldc_I4_4;
    }
}
