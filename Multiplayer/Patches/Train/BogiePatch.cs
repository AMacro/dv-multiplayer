using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(Bogie), nameof(Bogie.SetupPhysics))]
public static class Bogie_SetupPhysics_Patch
{
    private static void Postfix(Bogie __instance)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            __instance.gameObject.GetOrAddComponent<NetworkedBogie>();
    }
}

[HarmonyPatch(typeof(Bogie), nameof(Bogie.SwitchJunctionIfNeeded))]
public static class Bogie_SwitchJunctionIfNeeded_Patch
{
    private static bool Prefix()
    {
        return NetworkLifecycle.Instance.IsHost();
    }
}

[HarmonyPatch(typeof(Bogie), nameof(Bogie.SetTrack))]
public static class Bogie_SetTrack_Patch
{
    private static void Prefix(Bogie __instance, int newTrackDirection)
    {
        if (!__instance.Car.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return; // When the car first gets spawned in by CarSpawner#SpawnExistingCar, this method gets called before the NetworkedTrainCar component is added to the car.
        if (__instance.Car.Bogies[0] == __instance)
            networkedTrainCar.Bogie1TrackDirection = newTrackDirection;
        else if (__instance.Car.Bogies[1] == __instance)
            networkedTrainCar.Bogie2TrackDirection = newTrackDirection;
    }
}
