using System;
using System.Collections.Generic;

namespace TaylorsBalancePatch;

// Damage reports run inside TakeDamageProcess, before its death handling.
// Drain bonus hits only after the outermost original damage call returns.
internal sealed class AfterDamageQueue<T>
{
    private readonly Queue<T> pending = new Queue<T>();
    private int depth;
    private bool draining;

    internal void Enter() => depth++;
    internal void Enqueue(T hit) => pending.Enqueue(hit);
    internal void Clear() => pending.Clear();

    internal void Exit(Action<T> apply)
    {
        if (--depth != 0 || draining) return;
        draining = true;
        try
        {
            while (pending.Count > 0) apply(pending.Dequeue());
        }
        finally { draining = false; }
    }
}
