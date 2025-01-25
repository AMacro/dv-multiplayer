using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(Coupler), nameof(Coupler.CoupleTo))]
public static class Coupler_CoupleTo_Patch
{
    private static void Postfix(Coupler __instance, Coupler other, bool playAudio, bool viaChainInteraction)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        NetworkLifecycle.Instance.Client?.SendTrainCouple(__instance, other, playAudio, viaChainInteraction);
    }
}

[HarmonyPatch(typeof(Coupler), nameof(Coupler.Uncouple))]
public static class Coupler_Uncouple_Patch
{
    private static void Postfix(Coupler __instance, bool playAudio, bool calledOnOtherCoupler, bool dueToBrokenCouple, bool viaChainInteraction)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket || calledOnOtherCoupler)
            return;
        if (!__instance.train.TryNetworked(out NetworkedTrainCar networkedTrainCar) || networkedTrainCar.IsDestroying)
            return;
        NetworkLifecycle.Instance.Client?.SendTrainUncouple(__instance, playAudio, dueToBrokenCouple, viaChainInteraction);
    }
}

[HarmonyPatch(typeof(Coupler), nameof(Coupler.ConnectAirHose))]
public static class Coupler_ConnectAirHose_Patch
{
    private static void Postfix(Coupler __instance, Coupler other, bool playAudio)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        NetworkLifecycle.Instance.Client?.SendHoseConnected(__instance, other, playAudio);
    }
}

[HarmonyPatch(typeof(Coupler), nameof(Coupler.DisconnectAirHose))]
public static class Coupler_DisconnectAirHose_Patch
{
    private static void Postfix(Coupler __instance, bool playAudio)
    {
        if (UnloadWatcher.isUnloading || NetworkLifecycle.Instance.IsProcessingPacket)
            return;
        NetworkLifecycle.Instance.Client?.SendHoseDisconnected(__instance, playAudio);
    }
}
