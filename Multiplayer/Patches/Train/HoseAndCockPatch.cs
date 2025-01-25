using DV.Simulation.Brake;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(HoseAndCock), nameof(HoseAndCock.SetCock))]
public static class HoseAndCock_SetCock_Patch
{
    private static void Prefix(HoseAndCock __instance, bool open)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        Coupler coupler = NetworkedTrainCar.GetCoupler(__instance);
        NetworkedTrainCar networkedTrainCar = coupler.train.Networked();
        if (networkedTrainCar.IsDestroying)
            return;
        NetworkLifecycle.Instance.Client?.SendCockState(networkedTrainCar.NetId, coupler, open);
    }
}
