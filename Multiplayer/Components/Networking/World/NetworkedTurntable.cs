using DV;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedTurntable : IdMonoBehaviour<byte, NetworkedTurntable>
{
    #region Lookup Cache
    private static NetworkedTurntable[] _indexedTurntables;
    private static readonly Dictionary<TurntableRailTrack, NetworkedTurntable> turntableToNetworkedTurntable = [];
    public static NetworkedTurntable[] IndexedTurntables => _indexedTurntables ??= RailTrackRegistry.Instance.TrackRootParent.GetComponentsInChildren<NetworkedTurntable>().OrderBy(nj => nj.NetId).ToArray();


    public static bool TryGet(ushort netId, out TurntableRailTrack turntable)
    {
        if (Get((byte)netId, out var networkedTurntable))
        {
            turntable = networkedTurntable.TurntableRailTrack;
            return true;
        }

        turntable = null;
        return false;
    }

    public static bool TryGetNetId(TurntableRailTrack turntable, out ushort netId)
    {
        if (turntableToNetworkedTurntable.TryGetValue(turntable, out var networkedTurntable))
        {
            netId = networkedTurntable.NetId;
            return true;
        }

        netId = 0;
        return false;
    }

    #endregion

    protected override bool IsIdServerAuthoritative => false;

    public TurntableRailTrack TurntableRailTrack;
    private float lastYRotation;
    private bool initialised = false;

    protected override void Awake()
    {
        base.Awake();
        TurntableRailTrack = GetComponent<TurntableRailTrack>();
        turntableToNetworkedTurntable[TurntableRailTrack] = this;

        NetworkLifecycle.Instance.OnTick += OnTick;

        initialised = NetworkLifecycle.Instance.IsHost();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isQuitting)
            return;

        turntableToNetworkedTurntable.Remove(TurntableRailTrack);

        NetworkLifecycle.Instance.OnTick -= OnTick;
    }

    private void OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading || !initialised || Mathf.Approximately(lastYRotation, TurntableRailTrack.targetYRotation))
            return;

        lastYRotation = TurntableRailTrack.targetYRotation;
        NetworkLifecycle.Instance.Client.SendTurntableRotation(NetId, lastYRotation);
    }

    public void SetRotation(float rotation, bool forceConnectionRefresh = false, bool initialising = false)
    {
        lastYRotation = rotation;
        TurntableRailTrack.targetYRotation = rotation;
        TurntableRailTrack.RotateToTargetRotation(forceConnectionRefresh);

        if (!initialised && initialising)
            initialised = true;
    }

    public static bool Get(byte netId, out NetworkedTurntable obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<byte, NetworkedTurntable> rawObj);
        obj = (NetworkedTurntable)rawObj;
        return b;
    }
}
