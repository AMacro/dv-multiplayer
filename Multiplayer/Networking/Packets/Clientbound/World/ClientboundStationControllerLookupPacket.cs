using System;
using System.Collections.Generic;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientBoundStationControllerLookupPacket
{
    public ushort[] NetID { get; set; }
    public string[] StationID { get; set; }

    public ClientBoundStationControllerLookupPacket() { }

    public ClientBoundStationControllerLookupPacket(ushort[] netID, string[] stationID)
    {
        if (netID == null) throw new ArgumentNullException(nameof(netID));
        if (stationID == null) throw new ArgumentNullException(nameof(stationID));
        if (netID.Length != stationID.Length) throw new ArgumentException("Arrays must have the same length");

        NetID = netID;
        StationID = stationID;
    }

    public ClientBoundStationControllerLookupPacket(KeyValuePair<ushort, string>[] NetIDtoStationID)
    {
        if (NetIDtoStationID == null)
            throw new ArgumentNullException(nameof(NetIDtoStationID));

        NetID = new ushort[NetIDtoStationID.Length];
        StationID = new string[NetIDtoStationID.Length];

        for (int i = 0; i < NetIDtoStationID.Length; i++)
        {
            NetID[i] = NetIDtoStationID[i].Key;
            StationID[i] = NetIDtoStationID[i].Value;
        }
    }
}
