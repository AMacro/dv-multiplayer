using DV.ThingTypes;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Serverbound.Train;

public class ServerboundTrainCarSpawnRequest
{
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; set; }
    public bool Derailed { get; set; }
    public string LiveryID { get; set; }

    public static ServerboundTrainCarSpawnRequest FromTrainCar(TrainCar trainCar)
    {
        return new ServerboundTrainCarSpawnRequest
        {
            Position = trainCar.transform.position,
            Forward = trainCar.transform.forward,
            Derailed = trainCar.derailed,
            LiveryID = trainCar.carLivery.id,
        };
    }
}
