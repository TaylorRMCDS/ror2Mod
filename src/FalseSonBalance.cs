using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TaylorsBalancePatch;

internal static class FalseSonBalance
{
    internal const string ConfigSection = "False Son";
    internal const string ConfigKey = "EnableFalseSonChanges";
    internal const string ConfigDescription = "Enable False Son growth tuning, laser Lunar Ruin and bug fixes. Restart after changing this setting.";
    internal const float SpeedPerGrowth = 0.04f;
    internal const float ArmorAndRegenPerGrowth = 2f;
    internal const int TicksPerRuin = 24;
    internal const float RuinDuration = 5f;

    internal static void PatchGrowth(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f &&
            f.DeclaringType.FullName == "RoR2.FalseSonController" && f.Name.StartsWith("tamperedHeart")).ToArray();
        if (sites.Length != 4) throw new InvalidOperationException("False Son growth bonus layout changed.");
        foreach (var site in sites)
        {
            string name = ((FieldReference)site.Operand).Name;
            site.OpCode = OpCodes.Ldc_R4;
            site.Operand = name == "tamperedHeartMoveSpeed" || name == "tamperedHeartAttackSpeed" ? SpeedPerGrowth : ArmorAndRegenPerGrowth;
        }
    }

    internal static Instruction BulletFireSite(MethodBody body) => body.Instructions.Single(i =>
        i.Operand is MethodReference m && m.DeclaringType.FullName == "RoR2.BulletAttack" && m.Name == "Fire");

    internal static Instruction BurstRoundSite(MethodBody body)
    {
        var field = body.Instructions.Single(i => i.Operand is FieldReference f && f.Name == "secondaryRefillPercentage");
        if (field.Next.OpCode != OpCodes.Mul || field.Next.Next.OpCode != OpCodes.Conv_I4)
            throw new InvalidOperationException("Laser Burst refill layout changed.");
        return field.Next.Next;
    }

    internal static Instruction DeathMarkDotSite(MethodBody body) => body.Instructions.Single(i =>
        i.Operand is MethodReference m && m.Name == "HasDotActive");

    internal static void PatchLaserVisuals(MethodBody body)
    {
        var sites = body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f &&
            f.Name == "laserEffect" && i.Next.Operand is MethodReference m && m.Name == "op_Implicit").ToArray();
        if (sites.Length != 2 || sites.Any(i => i.Previous.OpCode != OpCodes.Ldarg_0))
            throw new InvalidOperationException("Laser visual update layout changed.");
        // Skip only the two visual blocks. Aim calculation, damage and timers stay in FixedUpdate.
        foreach (var site in sites)
        {
            site.Previous.OpCode = OpCodes.Ldnull;
            site.OpCode = OpCodes.Nop;
            site.Operand = null;
        }
    }

    internal static void PatchRuinDamage(MethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        int start = Array.FindIndex(instructions, i => i.Operand is FieldReference f && f.Name == "lunarruin");
        int store = Array.FindIndex(instructions, start, i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.DeclaringType.FullName == "RoR2.DamageInfo" && f.Name == "damage");
        int incoming = Array.FindIndex(instructions, store, i => i.OpCode == OpCodes.Stloc_S &&
            i.Previous.Operand is FieldReference f && f.DeclaringType.FullName == "RoR2.DamageInfo" && f.Name == "damage");
        if (start < 2 || store < start || incoming < store || instructions[store - 6].OpCode != OpCodes.Ldloc_0 ||
            instructions[store - 2].OpCode != OpCodes.Ldloc_S || instructions[incoming - 3].OpCode != OpCodes.Ldloc_0)
            throw new InvalidOperationException("Lunar Ruin incoming-damage layout changed.");
        var local = (VariableDefinition)instructions[incoming].Operand;
        var bonus = (VariableDefinition)instructions[store - 2].Operand;
        var il = body.GetILProcessor();
        // Initialize the native incomingDamage local before Ruin, preserving the existing exclusion guards.
        for (int n = incoming - 3; n <= incoming; n++)
        {
            var clone = Instruction.Create(OpCodes.Nop);
            clone.OpCode = instructions[n].OpCode;
            clone.Operand = instructions[n].Operand;
            il.InsertBefore(instructions[start - 2], clone);
        }
        for (int n = store - 6; n <= store; n++) { instructions[n].OpCode = OpCodes.Nop; instructions[n].Operand = null; }
        instructions[store - 6].OpCode = OpCodes.Ldloc; instructions[store - 6].Operand = local;
        instructions[store - 5].OpCode = OpCodes.Ldloc; instructions[store - 5].Operand = bonus;
        instructions[store - 4].OpCode = OpCodes.Add;
        instructions[store - 3].OpCode = OpCodes.Stloc; instructions[store - 3].Operand = local;
        for (int n = incoming - 3; n <= incoming; n++) { instructions[n].OpCode = OpCodes.Nop; instructions[n].Operand = null; }
    }

    internal static string? GetDescription(string token) => token switch
    {
        "FALSESON_PASSIVE_DESCRIPTION" => "Unused Lunar Spikes give <style=cIsUtility><color=#C586FF><b>+4% attack speed</b></color></style> and <style=cIsUtility><color=#C586FF><b>+2 armor</b></color></style>. Used spikes give <style=cIsUtility><color=#C586FF><b>+4% movement speed</b></color></style> and <style=cIshealing><color=#C586FF><b>+2 health regeneration</b></color></style>.",
        "FALSESON_SPECIAL_DESCRIPTION" => "<style=cIsUtility>Charge</style> a rapidly hitting laser dealing <style=cIsDamage>240%-1400% damage</style> for <style=cIsUtility>4s</style> and <style=cIsUtility>refill</style> Lunar Spikes. Increases in <style=cIsUtility>duration</style> through <style=cIsHealing>Growth</style>. <color=#C586FF><b>Hits refresh existing Lunar Ruin to 5 seconds and inflict another stack every 24 consecutive ticks per enemy (about 3 seconds at base attack speed). Misses and new casts reset streaks. Beam visuals update every rendered frame.</b></color>",
        "FALSESON_SPECIAL_ALT1_DESCRIPTION" => "Fire a piercing laser for <style=cIsDamage>1250% damage</style>, refilling <style=cIsUtility>50%</style> Lunar Spikes. Increases in stock by <style=cIsHealing>Growth</style>. <color=#C586FF><b>Lunar Spike refill rounds up, including one-stock loadouts.</b></color>",
        "FALSESON_UTILITY_DESCRIPTION" => "Dash a short distance, generating an explosion of <style=cIsDamage>200% damage</style> for every enemy hit. <style=cIsUtility>Two uses</style>. <color=#C586FF><b>The zero-damage detection sweep cannot proc items; explosions retain their normal proc coefficient.</b></color>",
        "KEYWORD_LUNARRUIN" => "<style=cKeywordName>Lunar Ruin</style><style=cSub>Enemies hit take <style=cIsDamage>+10%</style> more damage from all sources and reduces healing for <style=cIsUtility>5 seconds.</style> <i>Lunar Ruin can stack.</i></style> <color=#C586FF><b>Bonus damage no longer multiplies the shared damage packet. Lunar Ruin counts only once toward Death Mark.</b></color>",
        _ => null
    };
}

// Owned by one channel instance. Missing a tick or starting a new channel breaks a target's streak.
internal sealed class LaserRuinCounter
{
    private Dictionary<int, int> previous = new Dictionary<int, int>();
    private Dictionary<int, int> current = new Dictionary<int, int>();
    internal void BeginTick() { var spare = previous; previous = current; current = spare; current.Clear(); }
    internal bool Hit(int target)
    {
        if (current.ContainsKey(target)) return false;
        int count = previous.TryGetValue(target, out int old) ? old + 1 : 1;
        bool apply = count >= FalseSonBalance.TicksPerRuin;
        current[target] = apply ? 0 : count;
        return apply;
    }
}
