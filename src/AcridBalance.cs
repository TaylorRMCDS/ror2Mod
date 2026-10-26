using System;
using System.Collections.Generic;

namespace TaylorsBalancePatch;

internal static class AcridBalance
{
    internal const string ConfigSection = "Acrid";
    internal const string ConfigKey = "EnableAcridChanges";
    internal const string ConfigDescription =
        "Enable all Acrid passive and skill changes and descriptions. Restart the game after changing this setting.";

    internal const float BlightBaseDuration = 8f;
    internal const float BlightProcPerStack = 0.1f;
    internal const int BreathBlightTickInterval = 4;
    internal const float NeurotoxinCooldown = 2.5f;
    internal const int FrenziedLeapStock = 1;
    internal const float FrenziedLeapCooldown = 8f;
    internal const float BreathPoisonDuration = 10f;
    internal const float SpawnProtectionTail = 2f;
    internal static float GetBlightProcCoefficient(int totalStacks, int attackerStacks)
    {
        if (totalStacks <= 0 || attackerStacks <= 0) return 0f;
        // The game's DOT packets are grouped by attacker. Share one capped
        // victim-wide budget rather than allowing each Acrid another full 1.0.
        return Math.Min(1f, totalStacks * BlightProcPerStack) *
            Math.Min(attackerStacks, totalStacks) / totalStacks;
    }

    internal static string? GetDescription(string token) => token switch
    {
        "CROCO_PASSIVE_ALT_DESCRIPTION" => "Attacks that apply <style=cIsHealing>Poison</style> apply stacking <style=cIsDamage>Blight</style> instead, dealing <style=cIsDamage>60% damage per second</style>. <color=#C586FF><b>Each stack lasts 8 seconds. Each Blight tick has 0.1 proc coefficient per stack, capped at 1.0 total per victim per tick across all attackers; ticks can trigger on-hit items.</b></color>",
        "CROCO_SECONDARY_DESCRIPTION" => "<style=cIsHealing>Poisonous</style>. Spit toxic bile for <style=cIsDamage>240% damage</style>. <color=#C586FF><b>Base cooldown: 2.5 seconds.</b></color>",
        "CROCO_UTILITY_ALT1_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Leap in the air, dealing <style=cIsDamage>550% damage</style>. <style=cIsUtility>Reduce</style> the cooldown by <style=cIsUtility>2s</style> for every enemy hit. <color=#C586FF><b>Poisonous. Holds 1 charge. Base cooldown: 8 seconds.</b></color>",
        "CROCO_SPECIAL_ALT_DESCRIPTION" => "<style=cIsHealing>Poisonous</style>. Emit a lingering cloud of toxic breath for <style=cIsDamage>240% damage per second</style>. <color=#C586FF><b>Uses your equipped passive: Poison applies every accepted tick; Blight applies on the first tick and every 4 ticks after that, per enemy and cloud.</b></color>",
        _ => null
    };
}

internal sealed class BreathTickSequence
{
    private int tick;
    internal bool NextAppliesBlight()
    {
        bool applies = tick == 0;
        tick = (tick + 1) % AcridBalance.BreathBlightTickInterval;
        return applies;
    }
}

internal sealed class BlightProcBudget<T> where T : notnull
{
    private readonly Dictionary<T, int> stacks = new();
    private readonly HashSet<T> consumed = new();
    private int totalStacks;
    internal void AddStack(T attacker)
    {
        totalStacks++;
        stacks.TryGetValue(attacker, out int count);
        stacks[attacker] = count + 1;
    }
    internal void AddUnattributedStack() => totalStacks++;
    internal float TakeProc(T attacker)
    {
        if (!consumed.Add(attacker) || !stacks.TryGetValue(attacker, out int count)) return 0f;
        return AcridBalance.GetBlightProcCoefficient(totalStacks, count);
    }
}
