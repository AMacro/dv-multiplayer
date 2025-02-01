using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundPitStopStationLookupPacket
{
    public PitStopStationMappingData[] PitStops {  get; set; }

    public ClientboundPitStopStationLookupPacket() { }

    public ClientboundPitStopStationLookupPacket(NetworkedPitStopStation[] netStations)
    {
        PitStops = new PitStopStationMappingData[netStations.Count()];

        for (int i = 0; i < netStations.Count(); i++)
            PitStops[i] = PitStopStationMappingData.From(netStations[i]);
    }

}
