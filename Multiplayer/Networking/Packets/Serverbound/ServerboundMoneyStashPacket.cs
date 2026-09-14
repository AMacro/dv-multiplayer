namespace Multiplayer.Networking.Packets.Serverbound;

/// <summary>
///     Sent when a client puts a physical money item into their wallet. The wallet is shared
///     and server-authoritative, so the host credits the amount and destroys the item.
/// </summary>
public class ServerboundMoneyStashPacket
{
    public ushort ItemNetId { get; set; }
}
