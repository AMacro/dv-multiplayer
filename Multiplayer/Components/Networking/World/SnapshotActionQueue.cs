using System;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Components.Networking.World;

// Tracks snapshot supersession independently of Unity's coroutine scheduling.
internal sealed class SnapshotActionQueue<T>
{
    private readonly Func<T, uint> originId;
    private readonly Func<T, bool> skipLocalReplay;
    private readonly Func<T, T> copy;
    private readonly List<T> localActions = [];
    private readonly HashSet<uint> acknowledged = [];
    public IEnumerable<T> LocalActions => localActions;
    public List<T> Pending { get; } = [];
    public int Generation { get; private set; }

    public SnapshotActionQueue(Func<T, uint> originId, Func<T, bool> skipLocalReplay, Func<T, T> copy)
    {
        this.originId = originId;
        this.skipLocalReplay = skipLocalReplay;
        this.copy = copy;
    }

    public void Reset()
    {
        Generation++;
        Pending.Clear();
        localActions.Clear();
        acknowledged.Clear();
    }

    public void RecordLocal(T action)
    {
        localActions.Add(copy(action));
    }

    public void Receive(T action) => Pending.Add(copy(action));

    public void Acknowledge(IEnumerable<uint> ids, bool applyingSnapshot)
    {
        acknowledged.UnionWith(ids);
        // An older baseline may still need these local edits replayed over it.
        if (!applyingSnapshot)
            PruneAcknowledged();
    }

    private void PruneAcknowledged()
    {
        localActions.RemoveAll(action => acknowledged.Contains(originId(action)));
        acknowledged.Clear();
    }

    public void ReceiveSnapshot(IEnumerable<uint> processedOriginIds)
    {
        Generation++;
        // Consume acknowledgments on receipt, even if this baseline is superseded
        // before application. The server clears its acknowledgment set after send.
        // Earlier reliable acknowledgments are also included in this newer
        // baseline, even if its predecessor was still being applied.
        acknowledged.UnionWith(processedOriginIds);
        PruneAcknowledged();
        // Origin acknowledgments bind identity on receipt. For other peers the
        // server supplies current replacement objects before this baseline, so
        // no earlier physical mutation should be replayed over its newer state.
        Pending.Clear();
    }

    public bool CompleteSnapshot(int generation)
    {
        if (generation != Generation)
            return false;

        Pending.InsertRange(0, localActions.Where(action => !skipLocalReplay(action)).Select(copy));
        PruneAcknowledged();
        return true;
    }
}
