using Multiplayer.Networking.Data.Train;
using System.Collections.Generic;

namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundSpawnTrainSetPacket
{
    public TrainsetSpawnPart[] SpawnParts { get; set; }
    public bool AutoCouple { get; set; }

    //public static ClientboundSpawnTrainSetPacket FromTrainSet(Trainset trainset, bool autoCouple)
    //{
    //    return new ClientboundSpawnTrainSetPacket {
    //        SpawnParts = TrainsetSpawnPart.FromTrainSet(trainset),
    //        AutoCouple = autoCouple
    //    };
    //}

    public static ClientboundSpawnTrainSetPacket FromTrainSet(List<TrainCar> trainset, bool autoCouple)
    {
        return new ClientboundSpawnTrainSetPacket {
            SpawnParts = TrainsetSpawnPart.FromTrainSet(trainset),
            AutoCouple = autoCouple

        };
    }
}
