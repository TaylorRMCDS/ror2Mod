using System;

namespace TaylorsBalancePatch;

internal static class HuntressBalance
{
    internal const float BlinkCooldown = 6f;
    internal const float ArrowRainDamageMultiplier = 1.05f;
    internal const float ArrowRainVerticalReach = 100f;

    internal static string? GetDescription(string token) => token switch
    {
        "HUNTRESS_UTILITY_DESCRIPTION" => "<style=cIsUtility>Disappear</style> and <style=cIsUtility>teleport</style> forward. <color=#C586FF><b>Base cooldown: 6 seconds.</b></color>",
        "HUNTRESS_SPECIAL_DESCRIPTION" => "<style=cIsUtility>Teleport</style> into the sky. Target an area to rain arrows, <style=cIsUtility>slowing</style> all enemies and dealing <style=cIsDamage><color=#C586FF><b>236.25%</b></color> damage per second</style>. <color=#C586FF><b>Deals 5% more damage than normal. Hits enemies up to 100m above its horizontal area, including through platforms; falling-arrow visuals fill the column.</b></color>",
        _ => null
    };
}
