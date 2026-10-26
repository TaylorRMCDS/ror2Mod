using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class OperatorBalance
{
    internal const string ConfigSection = "Operator";
    internal const string ConfigKey = "EnableOperatorChanges";
    internal const string ConfigDescription = "Halve FIREWALL's cooldown if it blocks nothing and deals no damage. Restart after changing this setting.";
    internal const float UnusedCooldownFraction = 0.5f;
    internal const short RefundMessageId = 29873;
    internal const string SkillNameToken = "DRONETECH_UTILITY_ALT_NAME";

    internal static bool IsFirewallProjectile(string name) => name is "ShieldProjectileEmpty" or
        "ShieldProjectile25" or "ShieldProjectile50" or "ShieldProjectile75" or "ShieldProjectile100";

    internal static float CooldownCredit(float finalInterval) => Math.Max(0f, finalInterval) * UnusedCooldownFraction;

    internal static Instruction ProjectileBlockSite(MethodBody body) => body.Instructions.Single(i =>
        i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Object" && m.Name == "Destroy");

    internal static string? GetDescription(string token) => token switch
    {
        "DRONETECH_UTILITY_ALT_DESCRIPTION" => "Command a drone to charge up a <style=cIsUtility>shield</style> that blocks damage. Release to fire it forward for <style=cIsDamage>200%-600% damage</style>. <color=#C586FF><b>If FIREWALL blocks nothing and deals no damage, its cooldown is halved.</b></color>",
        _ => null
    };
}

// One decision per use, spanning both the held shield and the released projectile.
internal sealed class FirewallUse
{
    internal bool Used { get; private set; }
    internal bool HoldingEnded { get; set; }
    internal bool ExpectsProjectile { get; set; }
    internal bool SawProjectile { get; set; }
    private bool completed;
    internal void Block() => Used = true;
    internal void Damage(float dealt) { if (dealt > 0f) Used = true; }
    internal bool TryComplete(bool projectilesAlive, out bool refund)
    {
        refund = false;
        if (completed || !HoldingEnded || projectilesAlive || (ExpectsProjectile && !SawProjectile)) return false;
        completed = true;
        refund = !Used;
        return true;
    }
}
