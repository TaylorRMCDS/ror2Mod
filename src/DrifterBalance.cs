using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class DrifterBalance
{
    internal const float SteeringMultiplier = 3f;
    internal const float JunkPullRadius = 15f;
    internal const float JunkPullSpeed = 30f;
    internal const float JunkPullAcceleration = 60f;

    // Scale reads, never the reusable state's fields or native boss-collision writes.
    internal static void PatchSteeringReads(MethodBody body, int expected)
    {
        var reads = body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f &&
            f.Name == "turnSpeed" && (f.DeclaringType.FullName == "EntityStates.Drifter.TornadoSlam" ||
            f.DeclaringType.FullName == "EntityStates.Drifter.ChargeTornadoSlam")).ToArray();
        if (reads.Length != expected) throw new InvalidOperationException("Tornado Slam steering layout changed.");
        var il = body.GetILProcessor();
        foreach (var read in reads)
        {
            var scale = Instruction.Create(OpCodes.Ldc_R4, SteeringMultiplier);
            il.InsertAfter(read, scale);
            il.InsertAfter(scale, Instruction.Create(OpCodes.Mul));
        }
    }

    internal static bool WithinPullRadius(float squaredDistance) =>
        squaredDistance >= 0f && squaredDistance <= JunkPullRadius * JunkPullRadius;

    internal static float PullSpeed(float distance, float deltaTime) =>
        deltaTime > 0f ? Math.Min(JunkPullSpeed, Math.Max(0f, distance) / deltaTime) : 0f;

    internal static string? GetDescription(string token) => token switch
    {
        "DRIFTER_UTILITY_ALT_DESCRIPTION" => "Spin forwards, smacking enemies for <style=cIsDamage>220% damage</style> per hit with a chance to drop <style=cIsUtility>Junk</style>, and then slam for <style=cIsDamage>600%</style>. <style=cIsUtility>Ricochets</style> off of heavy enemies. Cancel with Primary Skill or by jumping. <color=#C586FF><b>Charge and spin steering are 3x normal. While spinning, pull unclaimed allied Junk within 15m toward you, with line of sight.</b></color>",
        _ => null
    };
}
