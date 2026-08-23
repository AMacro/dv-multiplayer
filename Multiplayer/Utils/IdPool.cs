using System;
using System.Collections.Generic;

namespace Multiplayer.Utils;

public class IdPool<T> where T : struct
{
    private T currentId;
    private readonly Queue<T> releasedIds;

    public IdPool()
    {
        currentId = default;
        releasedIds = new Queue<T>();
    }

    public T NextId {
        get {
            if (releasedIds.Count > 0)
                return releasedIds.Dequeue();

            dynamic incrementedId = currentId;
            incrementedId++;

            if (incrementedId.CompareTo(default(T)) == 0)
                throw new OverflowException("IdPool has reached the maximum possible id.");

            currentId = incrementedId;
            return currentId;
        }
    }

    public void ReleaseId(T id)
    {
        // NextId increments before returning, so the default value is never handed out and
        // must never be taken back: objects that opt out of networking keep the default id,
        // and releasing it would later hand id 0 to a real object, which then cannot be
        // referenced over the network at all.
        if (EqualityComparer<T>.Default.Equals(id, default))
            return;

        releasedIds.Enqueue(id);
    }

    public void Reset()
    {
        currentId = default;
        releasedIds.Clear();
    }
}
