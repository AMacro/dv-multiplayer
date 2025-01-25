using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundSpawnTrainSetPacket
{
    public TrainsetSpawnPart[] SpawnParts { get; set; }

    public static ClientboundSpawnTrainSetPacket FromTrainSet(Trainset trainset)
    {
        return new ClientboundSpawnTrainSetPacket {
            SpawnParts = TrainsetSpawnPart.FromTrainSet(trainset)
        };
    }
}
