using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking;

public abstract class TickedQueue<T> : MonoBehaviour
{
    private uint lastTick;
    private readonly Queue<(uint, T)> snapshots = new();

    protected virtual void OnEnable()
    {
        NetworkLifecycle.Instance.OnTick += OnTick;
    }

    protected virtual void OnDisable()
    {
        if (UnloadWatcher.isQuitting)
            return;
        NetworkLifecycle.Instance.OnTick -= OnTick;
        lastTick = 0;
        snapshots.Clear();
    }

    public void ReceiveSnapshot(T snapshot, uint tick)
    {
        if (tick <= lastTick)
            return;
        lastTick = tick;
        snapshots.Enqueue((tick, snapshot));
    }

    private void OnTick(uint tick)
    {
        if (snapshots.Count == 0 || UnloadWatcher.isUnloading)
            return;
        while (snapshots.Count > 0)
        {
            (uint snapshotTick, T snapshot) = snapshots.Dequeue();
            Process(snapshot, snapshotTick);
        }
    }

    public void Clear()
    {
        snapshots.Clear();
    }

    protected abstract void Process(T snapshot, uint snapshotTick);
}
