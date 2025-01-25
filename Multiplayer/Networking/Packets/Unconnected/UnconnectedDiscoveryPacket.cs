using LiteNetLib.Utils;
using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Unconnected;

public class UnconnectedDiscoveryPacket
{
    public bool IsResponse { get; set; } = false;
    public LobbyServerData Data { get; set; }
}
