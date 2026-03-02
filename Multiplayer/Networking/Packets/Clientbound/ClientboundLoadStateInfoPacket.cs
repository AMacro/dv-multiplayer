
using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundLoadStateInfoPacket
{

    public PlayerLoadingState LoadingState { get; set; }
    public uint ItemsToLoad { get; set; }

}
