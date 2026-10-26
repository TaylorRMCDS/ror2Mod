using System;

namespace TaylorsBalancePatch;

internal static class CaptainBalance
{
    internal const float HealingBeaconBarrierFraction = 0.2f;
    internal const float TaserSlowDuration = 5f;
    internal static bool IsHealingWardName(string name) =>
        name == "CaptainHealingWard" || name == "CaptainHealingWard(Clone)";

    internal static float GetBarrierGrant(float currentBarrier, float fullHealth, float healingTick)
    {
        var remaining = Math.Max(0f, fullHealth * HealingBeaconBarrierFraction - currentBarrier);
        return Math.Min(Math.Max(0f, healingTick), remaining);
    }

    internal static string? GetDescription(string token) => token switch
    {
        "CAPTAIN_SECONDARY_DESCRIPTION" => "<style=cIsDamage>Shocking</style>. Fire a fast tazer that deals <style=cIsDamage>100% damage</style>. Travels farther if bounced. <color=#C586FF><b>Enemies that cannot be shocked are slowed by 50% for 5 seconds instead.</b></color>",
        "CAPTAIN_SUPPLY_HEAL_DESCRIPTION" => "<style=cIsHealing>Heal</style> all nearby allies for <style=cIsHealing>10%</style> of their <style=cIsHealing>maximum health</style> every second. <color=#C586FF><b>Allies already at full combined health instead gain each healing tick as barrier, up to 20% maximum health. Existing barrier is never removed.</b></color>",
        "CAPTAIN_UTILITY_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Request up to <style=cIsDamage>3</style> Orbital Probes from the <style=cIsDamage>UES Safe Travels</style>. Each probe deals <style=cIsDamage>1000% damage</style>. <color=#C586FF><b>Available in hidden realms.</b></color>",
        "CAPTAIN_UTILITY_ALT1_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Request a <style=cIsDamage>kinetic strike</style> from the <style=cIsDamage>UES Safe Travels</style>. After <style=cIsUtility>20 seconds</style>, it deals <style=cIsDamage>40,000% damage</style> to ALL characters. <color=#C586FF><b>Available in hidden realms.</b></color>",
        "CAPTAIN_SPECIAL_DESCRIPTION" => "Request a <style=cIsUtility>permanent</style> Supply Beacon. Can only be requested <style=cIsUtility>twice per stage</style>. <color=#C586FF><b>Available in hidden realms.</b></color>",
        _ => null
    };
}
