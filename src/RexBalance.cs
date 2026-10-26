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
    internal const float EnemyLiftSpeed = 8f;
    internal const float ClayDunestriderPushMultiplier = 1.5f;
    internal const float LaunchMultiplier = 0.9f;
    internal static float LaunchAcceleration(float acceleration, float moveSpeed, float baseMoveSpeed)
    {
        float ratio = baseMoveSpeed > 0f ? moveSpeed / baseMoveSpeed : 1f;
        return ratio > 0f ? acceleration * (1f + (ratio - 1f) * 0.5f) / ratio : acceleration;
    }
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
        "TREEBOT_PRIMARY_DESCRIPTION" => "Fire 3 syringes for <style=cIsDamage>3x80% damage</style>. The last syringe <style=cIsDamage>Weakens</style> and <style=cIsHealing>heals for 60% of damage dealt</style>. <color=#C586FF><b>Final syringe proc coefficient is 1.0; the first two remain 0.5.</b></color>",
        "TREEBOT_UTILITY_DESCRIPTION" => "Fire a <style=cIsUtility>Sonic Boom</style> that <style=cIsDamage>Weakens</style> all enemies hit. <color=#C586FF><b>The sonic boom fires directly underneath you in a 13m radius, pushing enemies up and away with 20 outward speed and 8 upward lift before mass resistance, and applying Weaken for 4 seconds. Clay Dunestriders receive 1.5x knockback. Add an upward impulse to your current momentum, from ground or air, at 90% of the original base airborne push strength, with half movement-speed scaling. A pale-green range ring expands to 13m and fades over 0.65 seconds.</b></color>",
        "TREEBOT_SPECIAL_ALT1_DESCRIPTION" => "Fire a bolt that deals <style=cIsDamage><color=#C586FF><b>100%</b></color> damage</style> and <style=cIsDamage>injects</style> an enemy. <color=#C586FF><b>Apply Weaken for 4 seconds and add 5 shared Fruiting stacks, refreshing the entire pool to 10 seconds. Allied direct skill hits, including minions, have a flat 20% chance to consume one stack, deal 400% of the triggering attacker's damage stat and drop a fruit that heals 15% HP. DOT and item-proc hits cannot trigger it; bonus damage cannot crit or trigger on-hit effects. No extra fruit drops on death.</b></color> <color=#C586FF><b>Recovery: 0.25 seconds at base attack speed.</b></color>",
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
