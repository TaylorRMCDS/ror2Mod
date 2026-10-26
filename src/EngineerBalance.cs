namespace TaylorsBalancePatch;

internal static class EngineerBalance
{
    internal const string ConfigSection = "Engineer";
    internal const string ConfigKey = "EnableEngineerChanges";
    internal const string ConfigDescription =
        "Enable all Engineer stat, skill and TR58 Carbonizer Turret changes and descriptions. Restart the game after changing this setting.";

    internal const float BaseDamage = 13f;
    internal const float LevelDamage = 2.6f;
    internal const float GrenadeChargeDurationMultiplier = 1f / 1.1f;
    internal const float PressureMineFullDamageScale = 10f / 3f;
    internal const float CarbonizerBaseRegen = 2f;

    internal static string? GetDescription(string token) => token switch
    {
        "ENGI_PRIMARY_DESCRIPTION" => "Charge up to <style=cIsDamage>8</style> grenades that deal <style=cIsDamage>100% damage</style> each. <color=#C586FF><b>Charges 10% faster, retaining attack-speed scaling.</b></color>",
        "ENGI_SECONDARY_DESCRIPTION" => "Place a two-stage mine that deals <style=cIsDamage>300% damage</style>, or <style=cIsDamage><color=#C586FF><b>1000%</b></color> damage</style> if fully armed. Can place up to 4.",
        "ENGI_SPECIAL_ALT1_DESCRIPTION" => "Place a <style=cIsUtility>mobile</style> turret that <style=cIsUtility>inherits all your items.</style> Fires a laser for <style=cIsDamage>200% damage per second</style> that <style=cIsUtility>slows enemies</style>. Can place up to 2. <color=#C586FF><b>Turret base health regeneration: 2 health per second.</b></color>",
        _ => null
    };
}
