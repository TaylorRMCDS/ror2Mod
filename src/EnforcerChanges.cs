using System;
using BepInEx.Logging;

namespace TaylorsBalancePatch;

internal sealed class EnforcerChanges : IDisposable
{
    internal EnforcerChanges(ManualLogSource log) { }

    internal void Enable()
    {
        // No changes specified. Not registered in Plugin yet.
    }

    public void Dispose() { }
}
