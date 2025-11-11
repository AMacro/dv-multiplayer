using System.Collections.Generic;

namespace Multiplayer.Components.Networking.World;

public class NetworkedRailTrack : IdMonoBehaviour<ushort, NetworkedRailTrack>
{
    #region Lookup Cache
    private static readonly Dictionary<RailTrack, NetworkedRailTrack> railTracksToNetworkedRailTracks = [];

    public static bool TryGet(ushort netId, out NetworkedRailTrack networkedRailTrack)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedRailTrack> rawObj);
        networkedRailTrack = (NetworkedRailTrack)rawObj;
        return b;
    }

    public static bool TryGet(ushort netId, out RailTrack railTrack)
    {
        if (TryGet(netId, out NetworkedRailTrack networkedRailTrack))
        {
            railTrack = networkedRailTrack.RailTrack;
            return true;
        }

        railTrack = null;
        return false;
    }

    public static bool TryGetNetId(RailTrack track, out ushort netId)
    {
        if (railTracksToNetworkedRailTracks.TryGetValue(track, out var networkedRailTrack))
        {
            netId = networkedRailTrack.NetId;
            return true;
        }

        netId = 0;
        return false;
    }

    public static NetworkedRailTrack GetFromRailTrack(RailTrack railTrack)
    {
        return railTracksToNetworkedRailTracks[railTrack];
    }

    #endregion

    protected override bool IsIdServerAuthoritative => false;

    public RailTrack RailTrack;

    protected override void Awake()
    {
        base.Awake();
        RailTrack = GetComponent<RailTrack>();
        railTracksToNetworkedRailTracks[RailTrack] = this;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        railTracksToNetworkedRailTracks.Remove(RailTrack);
    }
}
