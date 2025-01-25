using System.Linq;
using DV;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedTurntable : IdMonoBehaviour<byte, NetworkedTurntable>
{
    private static NetworkedTurntable[] _indexedTurntables;
    public static NetworkedTurntable[] IndexedTurntables => _indexedTurntables ??= WorldData.Instance.TrackRootParent.GetComponentsInChildren<NetworkedTurntable>().OrderBy(nj => nj.NetId).ToArray();

    protected override bool IsIdServerAuthoritative => false;

    public TurntableRailTrack TurntableRailTrack;
    private float lastYRotation;

    protected override void Awake()
    {
        base.Awake();
        TurntableRailTrack = GetComponent<TurntableRailTrack>();
        NetworkLifecycle.Instance.OnTick += OnTick;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (UnloadWatcher.isQuitting)
            return;
        NetworkLifecycle.Instance.OnTick -= OnTick;
    }

    private void OnTick(uint tick)
    {
        if (Mathf.Approximately(lastYRotation, TurntableRailTrack.targetYRotation))
            return;

        lastYRotation = TurntableRailTrack.targetYRotation;
        NetworkLifecycle.Instance.Client.SendTurntableRotation(NetId, lastYRotation);
    }

    public void SetRotation(float rotation, bool forceConnectionRefresh = false)
    {
        lastYRotation = rotation;
        TurntableRailTrack.targetYRotation = rotation;
        TurntableRailTrack.RotateToTargetRotation(forceConnectionRefresh);
    }

    public static bool Get(byte netId, out NetworkedTurntable obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<byte, NetworkedTurntable> rawObj);
        obj = (NetworkedTurntable)rawObj;
        return b;
    }
}
