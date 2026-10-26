namespace TaylorsBalancePatch;

internal static class ChefBalance
{
    internal const float IceCubeSpreadDegrees = 2.5f;
    internal const float RollSlopeLimit = 60f;
    internal const float BoostedIceDamage = 10f;
    internal static bool CancelCrestLift(bool wasGrounded, bool grounded, bool forcedUnground) =>
        wasGrounded && !grounded && !forcedUnground;
    internal static float CollisionUpSpeed(float before, float after) =>
        System.Math.Min(after, System.Math.Max(0f, before));

    internal static string? GetDescription(string token) => token switch
    {
        "CHEF_SECONDARY_ALT_DESCRIPTION" => "<style=cIsUtility>Frost</style>. Launch ice cubes for <style=cIsDamage>3x150% damage</style>. <color=#C586FF><b>One randomly selected cube flies straight; the other two each have independent random spread within a 2.5-degree cone.</b></color>",
        "CHEF_SECONDARY_ALT_BOOST_DESCRIPTION" => "<style=cKeywordName>Boost</style><style=cSub><style=cIsUtility>Freezing</style>. Boosting Ice Box launches 1 large ice cube for <style=cIsDamage><color=#C586FF><b>1000%</b></color> damage</style>.</style>",
        "CHEF_SECONDARY_ALT_BOOST_DESCRIPTION_SKILL" => "<style=cSub><style=cIsUtility>Freezing</style>. Launches 1 large ice cube for <style=cIsDamage><color=#C586FF><b>1000%</b></color> damage</style>.</style>",
        "CHEF_UTILITY_DESCRIPTION" => "<style=cIsDamage>Stunning</style>. Charge the rolling pin to <style=cIsUtility>speed</style> forward, dealing <style=cIsDamage>300%-600% damage</style>. <color=#C586FF><b>Automatic step climbing is disabled with a 60-degree walkable slope limit (or the original motor limit if lower) while rolling. Steep collisions and ramp crests cannot create upward launch speed; deliberate jumps remain available.</b></color>",
        "CHEF_SPECIAL_ALT1_DESCRIPTION" => "Emit a blast attack dealing <style=cIsDamage>400%</style> damage, inflicting <style=cIsDamage>Ignite</style> or <style=cIsUtility>Frost</style>. <style=cIsUtility>Boosts</style> the next Skill used. <color=#C586FF><b>Can be used during Roll without ending it.</b></color>",
        _ => null
    };
}

internal sealed class IceBoxVolley
{
    private readonly (float pitch, float yaw)[] offsets = new (float, float)[3];
    internal IceBoxVolley(System.Random random)
    {
        int straight = random.Next(3);
        for (int i = 0; i < offsets.Length; i++)
        {
            if (i == straight) continue;
            double angle = random.NextDouble() * System.Math.PI * 2;
            double radius = 0.5 + random.NextDouble() * (ChefBalance.IceCubeSpreadDegrees - 0.5);
            offsets[i] = ((float)(System.Math.Sin(angle) * radius), (float)(System.Math.Cos(angle) * radius));
        }
    }
    internal (float pitch, float yaw) Offset(int firedCount) => offsets[System.Math.Clamp(firedCount - 1, 0, 2)];
}
