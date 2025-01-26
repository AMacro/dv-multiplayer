using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundPitStopStationLookupPacket
{
    public ushort[] NetIds {  get; set; }
    public Vector3[] Locations { get; set; }


    public ClientboundPitStopStationLookupPacket() { }

    public ClientboundPitStopStationLookupPacket(KeyValuePair<ushort, Vector3>[] NetIDtoLocation)
    {
        if (NetIDtoLocation == null)
            throw new ArgumentNullException(nameof(NetIDtoLocation));

        NetIds = new ushort[NetIDtoLocation.Length];
        Locations = new Vector3[NetIDtoLocation.Length];

        for (int i = 0; i < NetIDtoLocation.Length; i++)
        {
            NetIds[i] = NetIDtoLocation[i].Key;
            Locations[i] = NetIDtoLocation[i].Value;
        }
    }

}
