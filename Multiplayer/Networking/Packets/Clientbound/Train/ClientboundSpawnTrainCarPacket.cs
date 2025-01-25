using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundSpawnTrainCarPacket
{
    public TrainsetSpawnPart SpawnPart { get; set; }

    public static ClientboundSpawnTrainCarPacket FromTrainCar(NetworkedTrainCar networkedTrainCar)
    {
        return new ClientboundSpawnTrainCarPacket {
            SpawnPart = TrainsetSpawnPart.FromTrainCar(networkedTrainCar)
        };
    }
}
