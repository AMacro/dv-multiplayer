using System;
using System.Collections.Generic;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components;

[DisallowMultipleComponent]
public abstract class IdMonoBehaviour<T, I> : MonoBehaviour where T : struct where I : MonoBehaviour
{
    private static readonly IdPool<T> idPool = new();
    private static readonly Dictionary<T, IdMonoBehaviour<T, I>> indexToObject = [];

    private T _netId;

    public T NetId {
        get => _netId;
        set {
            if (_netId.Equals(value))
                return;
            Register(value);
        }
    }

    protected abstract bool IsIdServerAuthoritative { get; }

    protected static bool Get(T netId, out IdMonoBehaviour<T, I> obj)
    {
        if (indexToObject.TryGetValue(netId, out obj) && obj != null)
            return true;
        obj = null;
        if ((netId as dynamic).CompareTo(default(T)) != 0)
            Multiplayer.LogDebug(() => $"Got invalid NetId {netId} for {typeof(I).Name}{(NetworkLifecycle.Instance.IsProcessingPacket ? $" while processing packet\r\n{Environment.StackTrace}" : "")}");
        return false;
    }

    protected static bool TryGet(T netId, out IdMonoBehaviour<T, I> obj)
    {
        if (indexToObject.TryGetValue(netId, out obj) && obj != null)
            return true;

        obj = null;
        return false;
    }

    protected virtual void Awake()
    {
        if (IsIdServerAuthoritative && !NetworkLifecycle.Instance.IsHost())
            return;
        Register(idPool.NextId);
    }

    public void Register(T id)
    {
        if (_netId.Equals(id) && indexToObject.TryGetValue(id, out var existing) && ReferenceEquals(existing, this))
            return;
        Unregister();
        _netId = id;
        if (!id.Equals(default(T)))
            indexToObject[id] = this;
    }

    private void Unregister()
    {
        if (!_netId.Equals(default(T)) && indexToObject.TryGetValue(_netId, out var current) && ReferenceEquals(current, this))
        {
            indexToObject.Remove(_netId);
            idPool.ReleaseId(_netId);
        }
    }

    protected virtual void OnDestroy()
    {
        Unregister();
        if (!UnloadWatcher.isUnloading)
            return;
        idPool.Reset();
        indexToObject.Clear();
    }
}
