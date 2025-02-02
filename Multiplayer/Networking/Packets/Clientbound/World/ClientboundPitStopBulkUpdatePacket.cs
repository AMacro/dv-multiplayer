using Multiplayer.Networking.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundPitStopBulkUpdatePacket
{
    public ushort NetId { get; set; }
    public PitStopStationData[] PitStopData { get; set; }
    public PitStopPlugData[] PlugData { get; set; }
}
