using System;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Utils;

public static class DvExtensions
{
    #region TrainCar

    public static ushort GetNetId(this TrainCar car)
    {
        ushort netId = car.Networked().NetId;
        if (netId == 0)
            throw new InvalidOperationException($"NetId for {car.carLivery.id} ({car.ID}) isn't initialized!");
        return netId;
    }

    public static NetworkedTrainCar Networked(this TrainCar trainCar)
    {
        return NetworkedTrainCar.GetFromTrainCar(trainCar);
    }

    public static bool TryNetworked(this TrainCar trainCar, out NetworkedTrainCar networkedTrainCar)
    {
        return NetworkedTrainCar.TryGetFromTrainCar(trainCar, out networkedTrainCar);
    }

    #endregion

    #region RailTrack

    public static NetworkedRailTrack Networked(this RailTrack railTrack)
    {
        return NetworkedRailTrack.GetFromRailTrack(railTrack);
    }

    #endregion
}
