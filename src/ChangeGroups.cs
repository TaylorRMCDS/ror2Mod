using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace TaylorsBalancePatch;

internal static class ChangeGroups
{
    private static readonly Dictionary<string, bool> values = new();
    internal static readonly Dictionary<string, string[]> Groups = new()
    {
        ["Commando"] = new[] { "Secondary", "Utility", "Special" },
        ["Artificer"] = new[] { "Stats", "Primary", "Secondary", "Utility", "Special" },
        ["Bandit"] = new[] { "Primary" },
        ["Captain"] = new[] { "Secondary", "Utility", "Special" },
        ["Huntress"] = new[] { "Utility", "Special" },
        ["REX"] = new[] { "Primary", "Utility", "Special" },
        ["MUL-T"] = new[] { "Stats", "Primary" },
        ["Engineer"] = new[] { "Stats", "Primary", "Secondary", "Special" },
        ["Loader"] = new[] { "Stats", "Secondary" },
        ["Acrid"] = new[] { "Stats", "Passive", "Secondary", "Utility", "Special" },
        ["Mercenary"] = new[] { "Primary", "Secondary", "Utility", "Special" },
        ["Railgunner"] = new[] { "Passive", "Secondary", "Utility", "Special" },
        ["Void Fiend"] = new[] { "Passive", "Primary", "Secondary", "Special" },
        ["False Son"] = new[] { "Passive", "Utility", "Special" },
        ["Seeker"] = new[] { "Utility", "Special" },
        ["CHEF"] = new[] { "Secondary", "Utility", "Special" },
        ["Drifter"] = new[] { "Utility" },
        ["Operator"] = new[] { "Utility" }
    };

    internal static void Bind(ConfigFile config)
    {
        values.Clear();
        foreach (var survivor in Groups)
        {
            // Read old all-or-nothing settings once to preserve disabled survivors.
            string oldName = survivor.Key.Replace(" ", "").Replace("-", "");
            var legacy = config.Bind(survivor.Key, "Enable" + oldName + "Changes", true);
            bool enabled = legacy.Value;
            config.Remove(legacy.Definition);
            foreach (string group in survivor.Value)
                values[survivor.Key + "/" + group] = config.Bind(survivor.Key, "Enable" + group + "Changes", enabled,
                    "Enable " + survivor.Key + " " + group + " changes and descriptions. Restart the game after changing this setting.").Value;
        }
        config.Save();
    }

    internal static bool Enabled(string survivor, string group) =>
        !values.TryGetValue(survivor + "/" + group, out bool enabled) || enabled;
    internal static bool Any(string survivor) => Groups[survivor].Any(group => Enabled(survivor, group));
    internal static bool SwapArtificer => Enabled("Artificer", "Utility") && Enabled("Artificer", "Special");

    internal static bool DescriptionEnabled(string survivor, string token)
    {
        if (token == "KEYWORD_FREEZING") return SharedFreezeChanges.Active;
        if (token == "KEYWORD_LUNARRUIN") return Enabled(survivor, "Special");
        if (token == "KEYWORD_LOWHEALTHPROTECTION") return Enabled(survivor, "Special");
        if (token.Contains("CAPTAIN_SUPPLY")) return Enabled(survivor, "Special");
        if (token == "KEYWORD_FRUITING") return Enabled(survivor, "Special");
        if (survivor == "Artificer")
        {
            if (token == "MAGE_UTILITY_ICE_DESCRIPTION") return Enabled(survivor, "Special");
            if (token == "MAGE_SPECIAL_LIGHTNING_DESCRIPTION") return Enabled(survivor, "Utility");
        }
        foreach (string group in new[] { "Passive", "Primary", "Secondary", "Utility", "Special" })
            if (token.Contains("_" + group.ToUpperInvariant())) return Enabled(survivor, group);
        return true;
    }
}
