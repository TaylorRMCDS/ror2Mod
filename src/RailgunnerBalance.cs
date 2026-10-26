using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class RailgunnerBalance
{
    internal const string ConfigSection = "Railgunner";
    internal const string ConfigKey = "EnableRailgunnerChanges";
    internal const string ConfigDescription = "Enable Railgunner weak-point, secondary sound, Polar Field Device and Cryocharge changes. Restart after changing this setting.";
    internal const float WeakPointScale = 0.7f;
    internal const float SecondaryVolume = 0.5f;
    internal const float PolarRadius = 15f;
    internal const float PolarDamage = 0f;
    internal const float PolarProc = 0f;
    internal const float PolarForce = 1500f;
    internal const float DeploymentDelayMultiplier = 0.1f;
    internal const float CryoRadiusMultiplier = 1.2f;
    internal const float PolarJumpHeightMultiplier = 2f;
    internal static float PolarJumpVelocityMultiplier => (float)Math.Sqrt(PolarJumpHeightMultiplier);

    internal static bool IsSecondarySound(string name) =>
        name == "Play_railgunner_m2_fire" || name == "Play_railgunner_m2_alt_fire";

    internal static float ListenerVolume(bool isShooter, bool resolved) =>
        !resolved || isShooter ? 1f : SecondaryVolume;

    internal static bool InsidePolar(float distanceSquared, float radius, bool allied, bool active) =>
        active && allied && radius > 0f && distanceSquared >= 0f && distanceSquared <= radius * radius;

    internal static Instruction LandingImmunitySite(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "RoR2.GlobalEventManager" && m.Name == "IsImmuneToFallDamage").ToArray();
        if (sites.Length != 1) throw new InvalidOperationException($"Expected one native landing immunity check; found {sites.Length}.");
        return sites[0];
    }

    internal static Instruction WeakPointSite(MethodBody body, bool squared)
    {
        string name = squared ? "sniperTargetRadiusSqr" : "sniperTargetRadius";
        var sites = body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference f && f.DeclaringType.FullName == "RoR2.HurtBox" && f.Name == name).ToArray();
        if (sites.Length != 1) throw new InvalidOperationException($"Expected one Railgunner {name} read; found {sites.Length}.");
        return sites[0];
    }

    internal static string? GetDescription(string token) => token switch
    {
        "KEYWORD_FREEZING" => ArtificerBalance.GetDescription(token),
        "RAILGUNNER_PASSIVE_DESCRIPTION" => "All <style=cIsDamage>Critical Strike Chance</style> is converted into <style=cIsDamage>Critical Strike Damage</style>. <color=#C586FF><b>Highlighted Weak Points and their hit-acceptance radius are 70% of normal size.</b></color>",
        "RAILGUNNER_SECONDARY_DESCRIPTION" => "Activate your <style=cIsUtility>long-range scope</style>, highlighting <style=cIsDamage>Weak Points</style> and transforming your weapon into a piercing <style=cIsDamage>1000% damage</style> railgun. <color=#C586FF><b>Stationary grounded shots apply no physical self-push. Other players hear firing at 50% volume on clients running the mod; shooter volume remains normal.</b></color>",
        "RAILGUNNER_SECONDARY_ALT_DESCRIPTION" => "Activate your <style=cIsUtility>short-range scope</style>, highlighting <style=cIsDamage>Weak Points</style> and transforming your weapon into a quick <style=cIsDamage>400% damage</style> railgun. <color=#C586FF><b>Stationary grounded shots apply no physical self-push. Other players hear firing at 50% volume on clients running the mod; shooter volume remains normal.</b></color>",
        "RAILGUNNER_UTILITY_ALT_DESCRIPTION" => "Throw out a device that <style=cIsUtility>slows down</style> all nearby <style=cIsUtility>enemies and projectiles</style>. <color=#C586FF><b>Initial burst radius: 15m, matching the lasting field. Deploys 0.1 seconds after sticking and pushes enemies away with 1500 force. You and allies jump 100% higher inside the active bubble. Allied landings inside it take no fall damage; fall speed is unchanged.</b></color>",
        "RAILGUNNER_SPECIAL_ALT_DESCRIPTION" => "<style=cIsUtility>Freezing</style>.  Fire <style=cIsDamage>piercing</style> round for <style=cIsDamage>2000% damage</style>. <color=#C586FF><b>Shot radius is 20% larger (2.4m). Frozen Mithrix and False Son bosses execute below 10% health; other enemies retain the 30% threshold.</b></color>",
        _ => null
    };
}
