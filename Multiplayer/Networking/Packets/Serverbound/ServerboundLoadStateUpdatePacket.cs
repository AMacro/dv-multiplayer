using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Serverbound
{
    internal class ServerboundLoadStateUpdatePacket
    {
        public PlayerLoadingState LoadState { get; set; }
    }
}
