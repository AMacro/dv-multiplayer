using System.Collections.Generic;

namespace Multiplayer.Components.Networking.World;

public class NetworkedRailTrack : IdMonoBehaviour<ushort, NetworkedRailTrack>
{
    private static readonly Dictionary<RailTrack, NetworkedRailTrack> railTracksToNetworkedRailTracks = new();

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

    public static bool Get(ushort netId, out NetworkedRailTrack obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedRailTrack> rawObj);
        obj = (NetworkedRailTrack)rawObj;
        return b;
    }

    public static NetworkedRailTrack GetFromRailTrack(RailTrack railTrack)
    {
        return railTracksToNetworkedRailTracks[railTrack];
    }

    public static bool TryGetFromRailTrack(RailTrack railTrack, out NetworkedRailTrack networkedRailTrack)
    {
        return railTracksToNetworkedRailTracks.TryGetValue(railTrack, out networkedRailTrack);
    }
}
