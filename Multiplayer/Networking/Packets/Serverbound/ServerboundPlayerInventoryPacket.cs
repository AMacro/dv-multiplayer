using Multiplayer.Networking.Data.Items;

namespace Multiplayer.Networking.Packets.Serverbound;

public class ServerboundPlayerInventoryPacket
{
    public PlayerItemSaveData[] Items { get; set; }
}
