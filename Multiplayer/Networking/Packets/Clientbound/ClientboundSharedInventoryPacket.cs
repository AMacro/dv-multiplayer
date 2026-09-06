namespace Multiplayer.Networking.Packets.Clientbound;

/// <summary>
///     Full contents of the shared (co-op) inventory. Sent when a player joins.
/// </summary>
public class ClientboundSharedInventoryPacket
{
    public ushort[] EntryIds { get; set; }
    public string[] PrefabNames { get; set; }

    /// <summary>Serialized per-item save state, empty string when the item has none.</summary>
    public string[] States { get; set; }
}
