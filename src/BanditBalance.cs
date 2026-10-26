using System;

namespace TaylorsBalancePatch;

internal static class BanditBalance
{
    internal const int BlastBaseCharges = 6;
    internal const float BlastMinimumBaseInterval = 0.3f;
    internal const float BlastSpreadMultiplier = 0.5f;
    internal const float BlastBloomMultiplier = 0.5f;
    internal const float BlastRadiusMultiplier = 1.2f;
    internal const float StockPipSpacing = 8f;

    internal static float GetStockPipX(int pipIndex, int activeStockCount)
    {
        var count = Math.Max(1, Math.Min(activeStockCount, BlastBaseCharges));
        return (pipIndex - (count - 1) * 0.5f) * StockPipSpacing;
    }

    internal static string? GetDescription(string token) => token switch
    {
        "BANDIT2_PRIMARY_ALT_DESCRIPTION" => "Fire a rifle blast for <style=cIsDamage>330% damage</style>. Can hold up to <color=#C586FF><b>6 bullets</b></color>. <color=#C586FF><b>Pierces enemies, stopping at terrain. Shot radius is 20% larger. Spread and bloom are reduced by 50%. Fires at most once every 0.3 seconds, independent of attack speed, reloads and skill interrupts.</b></color>",
        _ => null
    };
}

internal sealed class BlastShotClock
{
    private float nextShot = float.NegativeInfinity;
    internal bool CanFire(float now) => now >= nextShot;
    internal void RecordShot(float now) => nextShot = now + BanditBalance.BlastMinimumBaseInterval;
}
