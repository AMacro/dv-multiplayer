using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundPitStopStationLookupPacket
{
    public ushort[] NetIds {  get; set; }
    public Vector3[] Locations { get; set; }
    public int[] SelectedCars { get; set; }


    public ClientboundPitStopStationLookupPacket() { }

    public ClientboundPitStopStationLookupPacket(Tuple<ushort, Vector3, int>[] data)
    {
        NetIds = new ushort[data.Length];
        Locations = new Vector3[data.Length];
        SelectedCars = new int[data.Length];

        for (int i = 0; i < data.Length; i++)
        {
            var (netId, location, selection) = data[i];
            NetIds[i] = netId;
            Locations[i] = location;
            SelectedCars[i] = selection;
        }
    }

}
