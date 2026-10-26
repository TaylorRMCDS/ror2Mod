using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class ArtificerBalance
{
    internal const float BaseAcceleration = 60f;
    internal const float PlasmaDamage = 3.1f;
    internal const float SnapfreezeDamage = 3f;

    internal static void PatchWallDamage(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference f && f.DeclaringType.FullName == "EntityStates.Mage.Weapon.PrepWall" &&
            f.Name == "damageCoefficient").ToArray();
        if (sites.Length != 2) throw new InvalidOperationException("Snapfreeze's two wall damage reads changed.");
        foreach (var site in sites) { site.OpCode = OpCodes.Ldc_R4; site.Operand = SnapfreezeDamage; }
    }
    internal const float SlowDuration = 2f;
    internal const float LaunchSpeed = 7f;
    internal static float CappedLaunchSpeed(float moveSpeed) => Math.Max(0f, Math.Min(LaunchSpeed * 2f, moveSpeed));
    internal const float FlameRange = 30f;
    internal const float FlameLengthScale = 1.5f;

    // A single ease-out exponential across the whole aim range: up 0x,
    // horizontal 0.9x, down 1.1x, with no join at the horizon.
    internal static float TectonicPushMultiplier(float aimY)
    {
        double t = (1d - Math.Max(-1f, Math.Min(1f, aimY))) / 2d;
        return (float)((81d / 70d) * (1d - Math.Pow(81d / 4d, -t)));
    }

    internal static float FrozenExecuteFraction(string bodyName) => bodyName switch
    {
        "BrotherBody" or "BrotherHurtBody" or "FalseSonBossBody" or
        "FalseSonBossBodyLunarShard" or "FalseSonBossBodyBrokenLunarShard" => 0.1f,
        _ => 0.3f
    };

    internal static Instruction TectonicPushSite(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference f && f.DeclaringType.FullName == "EntityStates.Mage.Weapon.FireEarthShards" &&
            f.Name == "selfKnockbackSpeed").ToArray();
        if (sites.Length != 1 || !(sites[0].Next.Operand is MethodReference m) || m.Name != "op_Multiply")
            throw new InvalidOperationException("Tectonic Blast self-knockback layout changed.");
        return sites[0];
    }

    internal static Instruction[] FrozenExecuteSites(MethodBody body, bool healthBar)
    {
        var instructions = body.Instructions.ToArray();
        int start = Array.FindIndex(instructions, i => i.Operand is MethodReference m && m.Name == "get_isInFrozenState");
        int end = Array.FindIndex(instructions, Math.Max(0, start), i => i.Operand is FieldReference f &&
            (healthBar ? f.Name == "cullFraction" : f.DeclaringType.FullName == "EntityStates.FrozenState" && f.Name == "executeEffectPrefab"));
        if (start < 0 || end <= start) throw new InvalidOperationException("Frozen execution block was not found.");
        var sites = instructions.Skip(start).Take(end - start).Where(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.3f).ToArray();
        if (sites.Length != (healthBar ? 1 : 2)) throw new InvalidOperationException("Frozen execution threshold layout changed.");
        return sites;
    }

    internal static string? GetDescription(string token) => token switch
    {
        "MAGE_PRIMARY_LIGHTNING_DESCRIPTION" => "<style=cIsUtility>Rapid</style>. Fire a bolt for <style=cIsDamage><color=#C586FF><b>310%</b></color> damage</style> that <style=cIsDamage>explodes</style> in a small area. Hold up to 4. <color=#C586FF><b>Direct and splash hits slow enemies by 30% for 2 seconds.</b></color>",
        "MAGE_UTILITY_ICE_DESCRIPTION" => "<style=cIsUtility>Freezing</style>. Create a barrier that hurts enemies for <style=cIsDamage><color=#C586FF><b>300%</b></color> damage</style>. <color=#C586FF><b>Special slot.</b></color>",
        "MAGE_SPECIAL_LIGHTNING_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Burst into the sky, dealing <style=cIsDamage>800% damage</style>. <color=#C586FF><b>Utility slot. Launch distance scales with movement speed, capped at twice the vanilla base distance.</b></color>",
        "MAGE_SPECIAL_FIRE_DESCRIPTION" => "<style=cIsDamage>Ignite</style>. Burn all enemies in front of you for <style=cIsDamage>2000% damage</style>. <color=#C586FF><b>Reaches 30 meters; hand effects extend forward by 50%.</b></color>",
        "MAGE_UTILITY_ALT_DESCRIPTION" => "Fire a spread of shards that deal <style=cIsDamage>8x100% damage</style> and <style=cIsUtility>oil</style> enemies. <color=#C586FF><b>Aiming downward provides more aerial knockback.</b></color>",
        "MAGE_SECONDARY_ICE_DESCRIPTION" => "<style=cIsUtility>Freezing</style>. Charge up a <style=cIsDamage>piercing</style> nano-spear that deals <style=cIsDamage>400%-1200%</style> damage. <color=#C586FF><b>Frozen Mithrix and False Son bosses execute below 10% health; other enemies retain the 30% threshold.</b></color>",
        "KEYWORD_FREEZING" => "<style=cKeywordName>Freezing</style><style=cSub>Freeze enemies in place. Frozen enemies are <style=cIsHealth>instantly killed</style> if below <style=cIsHealth>30%</style> health.</style> <color=#C586FF><b>Frozen Mithrix and False Son bosses execute below 10% health; other enemies retain the 30% threshold.</b></color>",
        _ => null
    };

    internal static void PatchWallInput(MethodBody body) => ReplaceField(body,
        "RoR2.InputBankTest", "skill3", "skill4");
    internal static void PatchWallRefund(MethodBody body) => ReplaceField(body,
        "RoR2.SkillLocator", "utility", "special");

    private static void ReplaceField(MethodBody body, string type, string from, string to)
    {
        var matches = body.Instructions.Where(i => i.Operand is FieldReference f &&
            f.DeclaringType.FullName == type && f.Name == from).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected one {type}.{from}, found {matches.Length}.");
        var field = (FieldReference)matches[0].Operand;
        matches[0].Operand = new FieldReference(to, field.FieldType, field.DeclaringType);
    }

    internal static Instruction LaunchSpeedSite(MethodBody body)
    {
        var matches = body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld &&
            i.Operand is FieldReference f && f.Name == "moveSpeedStat").ToArray();
        if (matches.Length != 1 || matches[0].Previous.OpCode != OpCodes.Ldarg_0)
            throw new InvalidOperationException("Ion Surge movement layout changed.");
        return matches[0];
    }

    internal static void PatchLaunchSource(MethodBody body)
    {
        var matches = body.Instructions.Where(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.FullName == "RoR2.DamageSource RoR2.DamageTypeCombo::damageSource").ToArray();
        if (matches.Length != 1 || matches[0].Previous.OpCode != OpCodes.Ldc_I4_8)
            throw new InvalidOperationException("Ion Surge damage source layout changed.");
        matches[0].Previous.OpCode = OpCodes.Ldc_I4_4;
    }
}
