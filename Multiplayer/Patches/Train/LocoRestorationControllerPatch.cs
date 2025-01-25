using DV.LocoRestoration;
using HarmonyLib;
using Multiplayer.Components.Networking;

namespace Multiplayer.Patches.Train;
[HarmonyPatch(typeof(LocoRestorationController))]
public static class LocoRestorationControllerPatch
{
    [HarmonyPatch(nameof(LocoRestorationController.Start))]
    [HarmonyPrefix]
    private static bool Start(LocoRestorationController __instance)
    {
        if(NetworkLifecycle.Instance.IsHost())
            return true;

        //TrainCar loco = __instance.loco;
        //TrainCar second = __instance.secondCar;

        Multiplayer.LogDebug(() => $"LocoRestorationController.Start()");

        UnityEngine.Object.Destroy(__instance);

        //CarSpawner.Instance.DeleteCar(loco);
        //if(second != null)
        //    CarSpawner.Instance.DeleteCar(second);
        return false;
    }
}
