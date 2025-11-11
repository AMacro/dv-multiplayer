using Multiplayer.Networking.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundPitStopBulkUpdatePacket
{
    public ushort NetId { get; set; }
    public int CarCount { get; set; }
    public int CarSelection {  get; set; }
    public int FaucetNotch { get; set; }
    public LocoResourceModuleData[] ResourceData { get; set; }
    public PitStopPlugData[] PlugData { get; set; }
}
