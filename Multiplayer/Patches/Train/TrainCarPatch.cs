using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(TrainCar))]
public static class TrainCarPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(TrainCar.Awake))]
    private static void Awake_Prefix(TrainCar __instance)
    {
        InitNetworkedTrainCar(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(TrainCar.AwakeForPooledCar))]
    private static void AwakeForPooledCar_Prefix(TrainCar __instance)
    {
        InitNetworkedTrainCar(__instance);
    }

    private static void InitNetworkedTrainCar(TrainCar __instance)
    {
        if (CarSpawner.Instance.PoolSetupInProgress)
            return;
        __instance.GetOrAddComponent<NetworkedTrainCar>();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(TrainCar.Rerail))]
    private static void Rerail_Prefix(TrainCar __instance, RailTrack rerailTrack, Vector3 worldPos, Vector3 forward)
    {
        if (!NetworkLifecycle.Instance.IsHost() || NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        if (!__instance.derailed || !__instance.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return;
        NetworkLifecycle.Instance.Server.SendRerailTrainCar(networkedTrainCar.NetId, NetworkedRailTrack.GetFromRailTrack(rerailTrack).NetId, worldPos - WorldMover.currentMove, forward);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(TrainCar.UpdateCouplerJoints))]
    private static bool UpdateCouplerJoints(TrainCar __instance)
    {
        return NetworkLifecycle.Instance.IsHost();
    }
}
