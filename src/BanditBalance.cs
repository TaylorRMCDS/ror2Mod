using System;

namespace TaylorsBalancePatch;

internal static class BanditBalance
{
    internal const int BlastBaseCharges = 6;
    internal const float BlastMinimumBaseInterval = 0.2f;
    internal const float BlastSpreadMultiplier = 0.5f;
    internal const float BlastBloomMultiplier = 0.5f;
    internal const float StockPipSpacing = 8f;

    internal static float GetStockPipX(int pipIndex, int activeStockCount)
    {
        var count = Math.Max(1, Math.Min(activeStockCount, BlastBaseCharges));
        return (pipIndex - (count - 1) * 0.5f) * StockPipSpacing;
    }

    internal static string? GetDescription(string token) => token switch
    {
        "BANDIT2_PRIMARY_ALT_DESCRIPTION" => "Fire a rifle blast for <style=cIsDamage>330% damage</style>. Spread and bloom are reduced by <style=cIsUtility>50%</style>. Fires every <style=cIsUtility>0.2 seconds</style> at base attack speed. Can hold up to <style=cIsUtility>6</style> rounds.",
        _ => null
    };
}
