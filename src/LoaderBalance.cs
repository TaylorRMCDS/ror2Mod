namespace TaylorsBalancePatch;

internal static class LoaderBalance
{
    internal const string ConfigSection = "Loader";
    internal const string ConfigKey = "EnableLoaderChanges";
    internal const string ConfigDescription =
        "Enable all Loader stat and Grapple Fist changes and descriptions. Restart the game after changing this setting.";

    internal const float BaseHealth = 130f;
    internal const float BaseArmor = 10f;
    internal static bool QuickGrapple(bool attached, float seconds) =>
        attached && seconds >= 0f && seconds <= 1f + 0.00001f;

    internal static string? GetDescription(string token) => token switch
    {
        "LOADER_SECONDARY_DESCRIPTION" => "Fire your gauntlet forward, <style=cIsUtility>pulling</style> you to the target. <color=#C586FF><b>Grapple for 1 second or less of attached time to reduce the cooldown by 50%. Flight time does not count; misses receive no refund.</b></color>",
        _ => null
    };
}
