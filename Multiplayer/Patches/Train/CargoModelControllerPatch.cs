using System.Collections;
using DV.ThingTypes.TransitionHelpers;
using HarmonyLib;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CargoModelController), nameof(CargoModelController.OnCargoLoaded))]
public static class CargoModelControllerPatch
{
    private static bool Prefix(CargoModelController __instance)
    {
        __instance.StartCoroutine(AddCargoOnceInitialized(__instance));
        return false;
    }

    private static IEnumerator AddCargoOnceInitialized(CargoModelController controller)
    {
        NetworkedTrainCar networkedTrainCar;
        while (!controller.trainCar.TryNetworked(out networkedTrainCar))
            yield return null;

        AddCargo(controller, networkedTrainCar);
    }

    private static void AddCargo(CargoModelController controller, NetworkedTrainCar networkedTrainCar)
    {
        TrainCar trainCar = networkedTrainCar.TrainCar;
        if (AudioManager.Instance.cargoLoadUnload != null && trainCar.IsCargoLoadedUnloadedByMachine)
        {
            Transform transform = trainCar.transform;
            AudioManager.Instance.cargoLoadUnload.Play(transform.position, minDistance: 10f, parent: transform);
        }

        GameObject[] prefabsForCarType = trainCar.LoadedCargo.ToV2().GetCargoPrefabsForCarType(trainCar.carLivery.parentType);
        if (prefabsForCarType == null || prefabsForCarType.Length == 0)
            return;

        byte modelIndex = networkedTrainCar.CargoModelIndex;
        if (modelIndex == byte.MaxValue || modelIndex >= prefabsForCarType.Length)
        {
            modelIndex = (byte)Random.Range(0, prefabsForCarType.Length);
            networkedTrainCar.CargoModelIndex = modelIndex;
        }

        controller.currentCargoModel = Object.Instantiate(prefabsForCarType[modelIndex], trainCar.interior.transform, false);
        controller.currentCargoModel.transform.localPosition = Vector3.zero;
        controller.currentCargoModel.transform.localRotation = Quaternion.identity;
        controller.trainColliders.SetupCargo(controller.currentCargoModel);
    }
}
