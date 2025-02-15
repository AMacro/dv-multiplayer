using Multiplayer.Components.Networking.Train;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking;

public abstract class TickedQueue<T> : MonoBehaviour
{
    private const float WARNING_THRESHOLD_SECONDS = 1.0f;
    private const uint QUEUE_LENGTH_WARNING = (uint)(NetworkLifecycle.TICK_RATE * WARNING_THRESHOLD_SECONDS);
    private const uint SNAPSHOT_GAP_WARNING = (uint)(NetworkLifecycle.TICK_RATE * WARNING_THRESHOLD_SECONDS);

    private uint lastTick;
    private uint lastReceivedTick;
    private readonly Queue<(uint, T)> snapshots = new();
    private string identifier;

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
        identifier = string.Empty;
    }

    public void ReceiveSnapshot(T snapshot, uint tick)
    {
        if (tick <= lastTick)
            return;

        if (snapshots.Count >= QUEUE_LENGTH_WARNING)
            Multiplayer.LogWarning($"[{GetID()}] Snapshot queue exceeds {QUEUE_LENGTH_WARNING} items. Current size: {snapshots.Count}");

        if (lastReceivedTick > 0 && tick - lastReceivedTick > SNAPSHOT_GAP_WARNING)
            Multiplayer.LogWarning($"[{GetID()}] Large gap between snapshots: {tick - lastReceivedTick} ticks.");

        lastReceivedTick = tick;
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

    private string GetID()
    {
        if (identifier != string.Empty)
            return identifier;

        TrainCar car;
        int bogie = 0;

        if (car = TrainCar.Resolve(this.gameObject))
            if (this is NetworkedBogie netBogie)
                bogie = (car.Bogies[0] == netBogie.Bogie) ? 1 : 2;

        if (car.logicCar != null)
            identifier = $"{car?.ID ?? gameObject.GetPath()}{(bogie > 0 ? $" Bogie {bogie}" : "")}";

        return identifier;
    }
}
