namespace Multiplayer.Networking.Packets.Clientbound;

/// <summary>
///     A single item entering or leaving the shared (co-op) inventory.
/// </summary>
public class ClientboundSharedInventoryChangePacket
{
    public ushort EntryId { get; set; }
    public string PrefabName { get; set; }
    public bool Added { get; set; }

    /// <summary>
    ///     For additions: the item's own save state (battery charge, coal load, ...) so the
    ///     copy each player receives carries it over. Empty when the item has no state.
    /// </summary>
    public string State { get; set; }

    /// <summary>
    ///     For additions: the world item this entry came from. The player who picked it up is
    ///     already holding that exact object and keeps it, rather than having it swapped for a
    ///     fresh copy; everyone else builds their own.
    /// </summary>
    public ushort OriginItemNetId { get; set; }
}
