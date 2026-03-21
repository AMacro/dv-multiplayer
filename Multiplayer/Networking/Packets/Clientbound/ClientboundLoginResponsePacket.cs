using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundLoginResponsePacket
{
    public bool Accepted { get; set; }
    public byte PlayerId { get; set; }
    public string OverrideUsername { get; set; }
    public string ReasonKey { get; set; }
    public string[] ReasonArgs { get; set; }
    public ModInfo[] Missing { get; set; } = [];
    public ModInfo[] Extra { get; set; } = [];
}
