using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class VoidFiendBalance
{
    internal const string ConfigSection = "Void Fiend";
    internal const string ConfigKey = "EnableVoidFiendChanges";
    internal const string ConfigDescription = "Enable Void Fiend primary falloff, corrupted Flood, Suppress and corruption changes. Restart after changing this setting.";
    internal const float FalloffStart = 50f;
    internal const float FalloffEnd = 100f;
    internal const float MinimumDamageMultiplier = 0.5f;
    internal const float CorruptedFloodRadius = 15f;
    internal const float CorruptedFloodSpeed = 50f;
    internal const float CorruptedFloodLifetime = 150f;
    internal const float HealFraction = 0.2f;
    internal const float SelfDamageFraction = -0.2f;
    internal const float CorruptedCastMultiplier = 0.5f;
    internal const float OutOfCombatCorruption = 2f;

    internal static float PrimaryDamageMultiplier(float distance)
    {
        float t = Math.Max(0f, Math.Min(1f, (distance - FalloffStart) / (FalloffEnd - FalloffStart)));
        return 1f - (1f - MinimumDamageMultiplier) * t;
    }

    internal static Instruction PrimaryFireSite(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "RoR2.BulletAttack" && m.Name == "Fire" && m.Parameters.Count == 0).ToArray();
        if (sites.Length != 1) throw new InvalidOperationException("Void Fiend normal primary firing layout changed.");
        return sites[0];
    }

    internal static string? GetDescription(string token) => token switch
    {
        "VOIDSURVIVOR_PRIMARY_DESCRIPTION" => "Fire a <style=cIsUtility>slowing</style> long-range beam for <style=cIsDamage>300% damage</style>. <color=#C586FF><b>Damage stays full through 50m, then falls linearly to 50% at 100m and beyond.</b></color>",
        "VOIDSURVIVOR_PASSIVE_DESCRIPTION" => "At full <style=cIsVoid>Corruption</style>, transform your abilities into more aggressive forms. <color=#C586FF><b>Gain 2 Corruption per second out of combat.</b></color>",
        "VOIDSURVIVOR_SECONDARY_UPRADE_TOOLTIP" => "<style=cKeywordName>【Corruption Upgrade】</style><style=cSub>Transform into a 1100% damage arcing bomb with <color=#C586FF><b>a 15m radius</b></color>.</style> <color=#C586FF><b>Launch speed: 50 m/s. Maximum flight lifetime and explosion-expiry timer: 150s. Impacts still detonate normally.</b></color>",
        "VOIDSURVIVOR_SPECIAL_DESCRIPTION" => "Crush <style=cIsVoid>25% Corruption</style> to heal yourself for <style=cIsHealing><color=#C586FF><b>20% maximum health</b></color></style>.",
        "VOIDSURVIVOR_SPECIAL_UPRADE_TOOLTIP" => "<style=cKeywordName>【Corruption Upgrade】</style><style=cSub>Transform to crush <color=#C586FF><b>20% health</b></color> to gain 25% Corruption instead.</style> <color=#C586FF><b>Charge and recovery are each 50% shorter: 1 second total at base attack speed.</b></color>",
        _ => null
    };
}
