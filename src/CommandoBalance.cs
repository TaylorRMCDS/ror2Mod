using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class CommandoBalance
{
    internal const float PhaseRoundDamage = 3.2f;
    internal const float PhaseRoundPierceBonus = 0.8f;
    internal const float PhaseBlastProc = 0.7f;
    internal const float DiveArmorDuration = 0.5f;
    internal const int BarrageBaseShots = 8;
    internal const float BarrageDamage = 1.15f;
    internal const float GrenadeEnemyBounceSpeedMultiplier = 0.5f;
    internal const string PhaseRoundProjectile = "FMJRamping";
    internal const string GrenadeProjectile = "CommandoGrenadeProjectile";
    internal const string ConfigSection = "Commando";
    internal const string ConfigKey = "EnableCommandoChanges";
    internal const string ConfigDescription =
        "Enable all Commando balance changes and their descriptions. " +
        "False leaves every Commando ability unchanged by this mod. Restart the game after changing this setting.";

    internal static string? GetDescription(string token) => token switch
    {
        "COMMANDO_SECONDARY_DESCRIPTION" => "Fire a <style=cIsDamage>piercing</style> bullet for <style=cIsDamage><color=#C586FF><b>320%</b></color> damage</style>. Deals <style=cIsDamage><color=#C586FF><b>80%</b></color></style> more damage every time it passes through an enemy.",
        "COMMANDO_SECONDARY_ALT1_DESCRIPTION" => "Fire two close-range blasts that deal <style=cIsDamage>8x200% damage</style> total. <color=#C586FF><b>Each pellet has a 0.7 proc coefficient.</b></color>",
        "COMMANDO_UTILITY_DESCRIPTION" => "<style=cIsUtility>Roll</style> a short distance. <color=#C586FF><b>Gain 200 armor for 0.5 seconds.</b></color>",
        "COMMANDO_SPECIAL_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Fire repeatedly for <style=cIsDamage><color=#C586FF><b>115%</b></color> damage</style> per bullet. The number of shots increases with attack speed. <color=#C586FF><b>Fires 8 shots at base attack speed.</b></color>",
        "COMMANDO_SPECIAL_ALT1_DESCRIPTION" => "Throw a grenade that explodes for <style=cIsDamage>700% damage</style>. Can hold up to 2. <color=#C586FF><b>Bounces off enemies with 50% less speed.</b></color>",
        _ => null
    };

    internal static void PatchBarrageDamage(MethodBody body) => ReplaceStaticField(
        body, "EntityStates.Commando.CommandoWeapon.FireBarrage", "damageCoefficient", OpCodes.Ldc_R4, BarrageDamage);

    internal static void PatchBarrageShots(MethodBody body) => ReplaceStaticField(
        body, "EntityStates.Commando.CommandoWeapon.FireBarrage", "baseBulletCount", OpCodes.Ldc_I4, BarrageBaseShots);

    private static void ReplaceStaticField(MethodBody body, string type, string name, OpCode opcode, object value)
    {
        Instruction? match = null;
        int count = 0;
        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field
                && field.DeclaringType.FullName == type && field.Name == name)
            {
                match = instruction;
                count++;
            }
        }
        if (count != 1 || match == null)
            throw new InvalidOperationException($"Expected one {type}.{name} read, found {count}. No changes applied to this method.");
        match.OpCode = opcode;
        match.Operand = value;
    }
}
