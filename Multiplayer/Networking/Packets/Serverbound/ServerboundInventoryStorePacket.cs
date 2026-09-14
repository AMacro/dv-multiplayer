namespace Multiplayer.Networking.Packets.Serverbound;

/// <summary>
///     A player took a world item into their inventory; it should become a shared entry.
/// </summary>
public class ServerboundInventoryStorePacket
{
    public ushort ItemNetId { get; set; }
}
