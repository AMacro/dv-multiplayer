using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.PrepareTrainCarForDeleting))]
public static class CarSpawner_PrepareTrainCarForDeleting_Patch
{
    private static void Prefix(TrainCar trainCar)
    {
        if (UnloadWatcher.isUnloading)
            return;
        if (!trainCar.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return;
        networkedTrainCar.IsDestroying = true;
        NetworkLifecycle.Instance.Server?.SendDestroyTrainCar(trainCar);
    }
}
