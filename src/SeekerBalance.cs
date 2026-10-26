using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class SeekerBalance
{
    internal const string ConfigSection = "Seeker";
    internal const string ConfigKey = "EnableSeekerChanges";
    internal const string ConfigDescription = "Enable slower Reprieve movement/turning, low-health Saving Grace and Palm Blast Saving Grace. Restart after changing this setting.";
    internal const float ReprieveSpeedMultiplier = 0.9f;
    internal const float MinimumReprieveTurnMultiplier = 0.2f;
    internal const byte MaxTranquility = 7;

    internal static void PatchReprieveSpeed(MethodBody body, bool enter)
    {
        var site = body.Instructions.Single(i => i.OpCode == (enter ? OpCodes.Stfld : OpCodes.Ldfld) &&
            i.Operand is FieldReference f && f.DeclaringType.FullName == "RoR2.SojournVehicleBase" &&
            f.Name == (enter ? "startingSpeed" : "trueEndSpeed"));
        var il = body.GetILProcessor();
        var scale = Instruction.Create(OpCodes.Ldc_R4, ReprieveSpeedMultiplier);
        if (enter) il.InsertBefore(site, scale); else il.InsertAfter(site, scale);
        il.InsertAfter(scale, Instruction.Create(OpCodes.Mul));
    }

    internal static void PatchReprieveTurn(MethodBody body)
    {
        var site = body.Instructions.Single(i => i.OpCode == OpCodes.Ldfld &&
            i.Operand is FieldReference f && f.Name == "turnSpeedCoeficient");
        var current = (FieldReference)body.Instructions.Single(i => i.OpCode == OpCodes.Ldfld &&
            i.Operand is FieldReference f && f.Name == "currentSpeed").Operand;
        var il = body.GetILProcessor();
        var insertion = site;
        foreach (string name in new[] { "currentSpeed", "startingSpeed", "trueEndSpeed" })
        {
            var load = Instruction.Create(OpCodes.Ldarg_0);
            il.InsertAfter(insertion, load);
            insertion = Instruction.Create(OpCodes.Ldfld, new FieldReference(name, current.FieldType, current.DeclaringType));
            il.InsertAfter(load, insertion);
        }
        var scale = Instruction.Create(OpCodes.Ldc_R4, ReprieveSpeedMultiplier);
        il.InsertAfter(insertion, scale);
        var multiply = Instruction.Create(OpCodes.Mul);
        il.InsertAfter(scale, multiply);
        var calculate = typeof(SeekerBalance).GetMethod(nameof(ReprieveTurnCoefficient),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        il.InsertAfter(multiply, Instruction.Create(OpCodes.Call, body.Method.Module.ImportReference(calculate)));
    }

    internal static float ReprieveTurnCoefficient(float nativeCoefficient, float speed, float startingSpeed, float maximumSpeed)
    {
        // A degenerate speed range has no acceleration phase; keep normal steering on entry.
        float progress = maximumSpeed > startingSpeed
            ? Math.Max(0f, Math.Min(1f, (speed - startingSpeed) / (maximumSpeed - startingSpeed)))
            : speed > startingSpeed ? 1f : 0f;
        return nativeCoefficient * (1f - (1f - MinimumReprieveTurnMultiplier) * progress);
    }

    internal static void PatchSelfReviveGrant(MethodBody body)
    {
        var store = body.Instructions.Single(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.Name == "seekerSelfRevive");
        if (store.Previous.OpCode != OpCodes.Ldc_I4_1)
            throw new InvalidOperationException("Meditate self-revive grant layout changed.");
        // Preserve the entire native living-ally buff/dead-ally revival loop.
        store.Previous.OpCode = OpCodes.Ldc_I4_0;
    }

    internal static bool CanConsumeGrace(bool alive, bool lowHealth, bool seeker, bool hasGrace, bool spent, bool defensesDisabled) =>
        alive && lowHealth && seeker && hasGrace && !spent && !defensesDisabled;

    internal static string? GetDescription(string token) => token switch
    {
        "SEEKER_UTILITY_ALT1_DESCRIPTION" => "Gain <style=cIsHealing>barrier</style> and take <style=cIsUtility>flight</style>. Total <style=cIsHealing>barrier</style> loss or impact creates a <style=cIsUtility>cyclone</style>, <style=cIsHealing>healing</style> allies, and dealing <style=cIsDamage>200% damage</style> per second. <color=#C586FF><b>Flight speed is 10% lower. Turning starts at normal strength and decreases linearly with speed to 80% lower at maximum flight speed.</b></color>",
        "KEYWORD_LOWHEALTHPROTECTION" => "<style=cKeywordName>Saving Grace</style><style=cSub>All living Survivors gain +7% to all stats. <color=#C586FF><b>When Seeker survives damage at 25% combined health or lower, Saving Grace is consumed to instantly heal 75% of maximum health instead of self-reviving. Available once per stage; Meditate retains its ally effects.</b></color></style>",
        "SEEKER_SPECIAL_ALT1_DESCRIPTION" => "<style=cIsUtility>Charge</style> a <style=cIsDamage>piercing</style>, spectral hand that deals <style=cIsDamage>400%-700% damage</style> and <style=cIsHealing>heals</style> allies. <style=cIsDamage>Multihit</style> three characters to gain <style=cIsUtility>Tranquility</style> and <style=cIsHealing>barrier</style>. <color=#C586FF><b>Reaching 7 Tranquility grants Saving Grace to living Survivors. Does not revive dead allies.</b></color>",
        _ => null
    };
}
