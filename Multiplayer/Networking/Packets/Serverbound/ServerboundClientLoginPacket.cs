using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundClientLoginPacket
{
    public string Username { get; set; }

    /// <summary>
    ///     Legacy self-asserted identity. Only honoured when the server does not require authentication,
    ///     since a client can put any value here. Authenticated servers derive the identity from
    ///     <see cref="SteamId" /> once the ticket below has been verified.
    /// </summary>
    public byte[] Guid { get; set; }

    public ulong SteamId { get; set; }

    /// <summary>Steam-minted proof that the sender owns <see cref="SteamId" />. Empty if unavailable.</summary>
    public byte[] AuthTicket { get; set; } = [];

    public string Password { get; set; }
    public string BuildVersion { get; set; }
    public ModInfo[] Mods { get; set; }
}
