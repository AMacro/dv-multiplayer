using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV.Logic.Job;
using DV.Utils;

namespace Multiplayer.Patches.Train;
[HarmonyPatch(typeof(TrainsOptimizer))]
public static class TrainsOptimizerPatch
{
    [HarmonyPatch(nameof(TrainsOptimizer.ForceOptimizationStateOnCars))]
    [HarmonyFinalizer]
    public static void ForceOptimizationStateOnCars(TrainsOptimizer __instance, Exception __exception, HashSet<Car> carsToProcess, bool forceSleep, bool forceStateOnCloseStationaryCars)
    {
        if (__exception == null)
            return;

        Multiplayer.LogError(() =>
        {
            Dictionary<Car, TrainCar> logicCarToTrainCar = SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar;

            if (carsToProcess == null)
                return $"TrainsOptimizer.ForceOptimizationStateOnCars() carstToProcess is null!";

            StringBuilder sb = new StringBuilder();
            sb.Append($"TrainsOptimizer.ForceOptimizationStateOnCars() iterating over {carsToProcess?.Count} cars:\r\n");

            int i=0 ;   
            foreach (Car car in carsToProcess)
            {
                if (car == null)
                    sb.AppendLine($"\tCar {i} is null!");
                else
                {
                    bool result = logicCarToTrainCar.TryGetValue(car, out TrainCar trainCar);

                    sb.AppendLine($"\tCar {i} id {car.ID} found TrainCar: {result}, TC ID: {trainCar?.ID}");
                }
            }

            i++;

            return sb.ToString();
        }
        );
    }
}

