using UnityEngine;

namespace Multiplayer.Networking.Packets.Serverbound;

/// <summary>
///     A player removed a shared item from their inventory, either by dropping it into the
///     world or by using it up. Either way it leaves every player's inventory.
/// </summary>
public class ServerboundInventoryRemovePacket
{
    public ushort EntryId { get; set; }

    /// <summary>True if the item was dropped into the world (spawn it), false if consumed.</summary>
    public bool Dropped { get; set; }

    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }

    /// <summary>The item's own save state at the moment it left the inventory.</summary>
    public string State { get; set; }
}
