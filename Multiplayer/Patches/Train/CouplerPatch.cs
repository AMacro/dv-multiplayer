using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;

namespace Multiplayer.Patches.Train;


[HarmonyPatch(typeof(Coupler))]
public static class CouplerPatch
{
    [HarmonyPatch(nameof(Coupler.ConnectAirHose))]
    [HarmonyPostfix]
    private static void ConnectAirHose(Coupler __instance, Coupler other, bool playAudio)
    {
        //Multiplayer.LogDebug(() => $"ConnectAirHose([{__instance?.train?.ID}, isFront: {__instance?.isFrontCoupler}])\r\n{new System.Diagnostics.StackTrace()}");

        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        //Ensure local car has initialised and breaks have been connected on spawn before sending any packets
        if (!NetworkedTrainCar.TryGetFromTrainCar(__instance?.train, out var netTrainCar) || !netTrainCar.Client_Initialized)
        {
            Multiplayer.LogDebug(() => $"ConnectAirHose({__instance?.train?.ID}) netTrainCar found: {netTrainCar != null}, Initialised: {netTrainCar?.Client_Initialized}");
            return;
        }

        NetworkLifecycle.Instance.Client?.SendHoseConnected(__instance, other, playAudio);
    }

    [HarmonyPatch(nameof(Coupler.DisconnectAirHose))]
    [HarmonyPostfix]
    private static void DisconnectAirHose(Coupler __instance, bool playAudio)
    {
        //Multiplayer.LogDebug(() => $"DisconnectAirHose([{__instance?.train?.ID}, isFront: {__instance?.isFrontCoupler}])\r\n{new System.Diagnostics.StackTrace()}");
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        //Ensure local car has initialised and breaks have been connected on spawn before sending any packets
        if (!NetworkedTrainCar.TryGetFromTrainCar(__instance?.train, out var netTrainCar) || !netTrainCar.Client_Initialized)
        {
            Multiplayer.LogDebug(() => $"DisconnectAirHose({__instance?.train?.ID}) netTrainCar found: {netTrainCar != null}, Initialised: {netTrainCar?.Client_Initialized}");
            return;
        }

        NetworkLifecycle.Instance.Client?.SendHoseDisconnected(__instance, playAudio); 
    }

}
