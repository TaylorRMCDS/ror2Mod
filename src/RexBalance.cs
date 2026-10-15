using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class RexBalance
{
    internal const float HarvestDamage = 1f;
    internal const float HarvestRecovery = 0.25f;
    internal const float DisperseRadius = 13f;
    internal const float EnemyPushSpeed = 20f;
    internal const float EnemyLiftSpeed = 5f;
    internal const float LaunchMultiplier = 0.9f;
    internal const float WeakenDuration = 4f;
    internal const int StacksPerHarvest = 5;
    internal const float FruitDuration = 10f;
    internal const float FruitChance = 0.2f;
    internal const float FruitBonusDamage = 4f;
    internal const float FruitHealing = 0.15f;

    internal static bool IsEligibleHit(bool rejected, bool delayedSecondHalf, bool isDot,
        bool hasProcChain, bool skillSource, bool allied, float damageDealt) =>
        !rejected && !delayedSecondHalf && !isDot && !hasProcChain && skillSource && allied && damageDealt > 0f;

    internal static int StacksAfterHarvest(int currentStacks) => checked(currentStacks + StacksPerHarvest);

    internal static string? GetDescription(string token) => token switch
    {
        "TREEBOT_PRIMARY_DESCRIPTION" => "Fire 3 syringes for <style=cIsDamage>3x80% damage</style>. The last syringe <style=cIsDamage>Weakens</style> and <style=cIsHealing>heals for 60% of damage dealt</style>. Its <style=cIsUtility>proc coefficient is 1.0</style>; the first two remain 0.5.",
        "TREEBOT_UTILITY_DESCRIPTION" => "Fire a <style=cIsUtility>Sonic Boom directly underneath you</style>, pushing enemies within <style=cIsUtility>13m</style> away and <style=cIsDamage>Weakening</style> them for <style=cIsUtility>4 seconds</style>. Add an upward impulse to your current momentum, from ground or air, at <style=cIsUtility>90% of the original airborne push strength</style>.",
        "TREEBOT_SPECIAL_ALT1_DESCRIPTION" => "Fire a bolt for <style=cIsDamage>100% damage</style>, <style=cIsDamage>Weakening</style> enemies for <style=cIsUtility>4 seconds</style> and adding <style=cIsHealing>5 Fruiting stacks</style>. All stacks refresh to <style=cIsUtility>10 seconds</style>. Allied direct skill hits have a flat <style=cIsUtility>20% chance</style> to consume one stack, deal <style=cIsDamage>400% of the attacker's base damage</style> and drop a fruit that heals <style=cIsHealing>15% HP</style>. Bonus damage cannot crit and has no on-hit procs. Recovery: <style=cIsUtility>0.25 seconds</style> at base attack speed. No extra fruit drops on death.",
        _ => null
    };

    internal static void PatchFruitDeathBuffCheck(MethodBody body)
    {
        var matches = body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference f && f.FullName == "RoR2.BuffDef RoR2.RoR2Content/Buffs::Fruiting").ToArray();
        if (matches.Length != 1 || matches[0].Previous.OpCode != OpCodes.Ldloc_3 ||
            !(matches[0].Next.Operand is MethodReference m) || m.Name != "HasBuff" ||
            matches[0].Next.Next.OpCode.FlowControl != FlowControl.Cond_Branch)
            throw new InvalidOperationException("The vanilla Fruiting death-drop condition changed.");

        // Keep every branch target intact. The buff no longer grants death drops;
        // Harvest also loses FruitOnHit so a lethal Harvest cannot bypass this.
        var field = matches[0];
        field.Previous.OpCode = OpCodes.Nop;
        field.Previous.Operand = null;
        field.OpCode = OpCodes.Nop;
        field.Operand = null;
        field.Next.OpCode = OpCodes.Ldc_I4_0;
        field.Next.Operand = null;
    }
}
