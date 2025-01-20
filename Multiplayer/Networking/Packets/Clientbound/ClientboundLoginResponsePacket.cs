using System;
using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundLoginResponsePacket
{
    public bool Accepted { get; set; }
    public string ReasonKey { get; set; }
    public string[] ReasonArgs { get; set; }
    public ModInfo[] Missing { get; set; } = Array.Empty<ModInfo>();
    public ModInfo[] Extra { get; set; } = Array.Empty<ModInfo>();
}
