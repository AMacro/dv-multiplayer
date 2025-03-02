using DV.Simulation.Brake;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(HoseAndCock), nameof(HoseAndCock.SetCock))]
public static class HoseAndCock_SetCock_Patch
{
    private static void Prefix(HoseAndCock __instance, bool open)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        if (!NetworkedTrainCar.TryGetCoupler(__instance, out Coupler coupler) || !coupler.train.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return;

        if (networkedTrainCar.IsDestroying)
            return;

        NetworkLifecycle.Instance.Client?.SendCockState(networkedTrainCar.NetId, coupler, open);
    }
}
