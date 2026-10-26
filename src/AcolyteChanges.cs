using System;
using BepInEx.Logging;

namespace TaylorsBalancePatch;

internal sealed class AcolyteChanges : IDisposable
{
    internal AcolyteChanges(ManualLogSource log) { }

    internal void Enable()
    {
        // No changes specified. Not registered in Plugin yet.
    }

    public void Dispose() { }
}
