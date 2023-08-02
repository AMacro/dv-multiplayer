using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundClientLoginPacket
{
    public string Username { get; set; }
    public string Password { get; set; }
    public ushort BuildMajorVersion { get; set; }
    public ModInfo[] Mods { get; set; }
}
