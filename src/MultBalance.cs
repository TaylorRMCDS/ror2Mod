namespace TaylorsBalancePatch;

internal static class MultBalance
{
    internal const string ConfigSection = "MUL-T";
    internal const string ConfigKey = "EnableMULTChanges";
    internal const string ConfigDescription =
        "Enable all MUL-T acceleration, Scrap Launcher and Power-Saw changes and descriptions. Restart the game after changing this setting.";

    internal const float BaseAcceleration = 60f;

    internal const float PowerSawRegenDuration = 0.5f;

    internal static string? GetDescription(string token) => token switch
    {
        "TOOLBOT_PRIMARY_ALT2_DESCRIPTION" => "Fire a rocket that explodes for <style=cIsDamage>360% damage</style>. Hold up to 4. <color=#C586FF><b>Explosions deal full damage throughout their blast radius, with no damage falloff. Reload speed scales with attack speed.</b></color>",
        "TOOLBOT_PRIMARY_ALT3_DESCRIPTION" => "Saw nearby enemies for <style=cIsDamage>1000% damage per second</style>. <color=#C586FF><b>Killing an enemy directly with the saw grants Regenerative for 0.5 seconds. DOT and item-proc kills do not trigger it.</b></color>",
        _ => null
    };
}
