using System;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;

namespace TaylorsBalancePatch;

// One installation, retained while either survivor module owns a lease.
internal static class SharedFreezeChanges
{
    private static int owners;
    internal static bool Active => owners > 0;

    internal static IDisposable Acquire()
    {
        if (owners == 0)
        {
            try
            {
                IL.RoR2.HealthComponent.TakeDamageProcess += FrozenExecution;
                IL.RoR2.HealthComponent.GetHealthBarValues += FrozenHealthBar;
            }
            catch
            {
                IL.RoR2.HealthComponent.TakeDamageProcess -= FrozenExecution;
                IL.RoR2.HealthComponent.GetHealthBarValues -= FrozenHealthBar;
                throw;
            }
        }
        owners++;
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (--owners != 0) return;
            IL.RoR2.HealthComponent.TakeDamageProcess -= FrozenExecution;
            IL.RoR2.HealthComponent.GetHealthBarValues -= FrozenHealthBar;
        }
    }

    private static void FrozenExecution(ILContext il) => PatchFrozenThreshold(il, false);
    private static void FrozenHealthBar(ILContext il) => PatchFrozenThreshold(il, true);

    private static void PatchFrozenThreshold(ILContext il, bool healthBar)
    {
        // Change only the frozen-state contribution, not Guillotine or other execute mechanics.
        foreach (var site in ArtificerBalance.FrozenExecuteSites(il.Body, healthBar))
        {
            var cursor = new ILCursor(il);
            cursor.Goto(site, MoveType.After);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<float, HealthComponent, float>>((vanilla, health) =>
                health.body ? ArtificerBalance.FrozenExecuteFraction(BodyCatalog.GetBodyName(health.body.bodyIndex)) : vanilla);
        }
    }

}
