using HarmonyLib;
using System;
using System.Collections.Generic;
using Multiplayer.Utils;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.Code;
using Multiplayer.Networking.Data;
using DV.ThingTypes;
using DV.Logic.Job;
using DV.Utils;
using Multiplayer.Components.Networking;


namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(UnusedTrainCarDeleter))]
public static class UnusedTrainCarDeleterPatch
{
    private const int TARGET_LDARG_1 = 4;
    private const int TARGET_SKIPS = 5;
    public static TrainCar current;

    [HarmonyPatch("AreDeleteConditionsFulfilled")]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int ldarg_1_Counter = 0;
        int skipCtr = 0;
        bool foundEntry = false;
        bool complete = false;

        foreach (CodeInstruction instruction in instructions)
        {
            //Multiplayer.LogDebug(() => $"Transpiling: {instruction.ToString()} - ldarg_1_Counter: {ldarg_1_Counter},  found: {foundEntry}, complete: {complete}, skip: {skipCtr}, len: {instruction.opcode.Size} + {instruction.operand}");
            if (instruction.opcode == OpCodes.Ldarg_1 && !foundEntry)
            {
                ldarg_1_Counter++;
                foundEntry = ldarg_1_Counter == TARGET_LDARG_1;
            }
            else if (foundEntry && !complete)
            {
                if(instruction.opcode == OpCodes.Callvirt)
                {
                    //allow IL_0083: callvirt and IL_0088: callvirt
                    yield return instruction;
                    continue;
                }

                if (instruction.opcode == OpCodes.Call)
                {
                    complete = true;
                    yield return CodeInstruction.Call(typeof(DvExtensions), "AnyPlayerSqrMag", [typeof(Vector3)], null); //inject our method
                    continue;
                }
            }else if (complete && skipCtr < TARGET_SKIPS)
            {
                //skip IL_0092: callvirt 
                //skip IL_0097: call
                //skip IL_009C: stloc.s
                //skip IL_009E: ldloca.s
                //skip IL_00A0: call

                skipCtr++;
                yield return new CodeInstruction(OpCodes.Nop);
                continue;
            }

            yield return instruction;
        }
    }

    [HarmonyPatch("InstantConditionalDeleteOfUnusedCars")]
    [HarmonyPrefix]
    public static bool InstantConditionalDeleteOfUnusedCars(UnusedTrainCarDeleter __instance)
    {
        if(NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;

        return false;
    }
/*
    [HarmonyPatch("AreDeleteConditionsFulfilled")]
    [HarmonyPrefix]
    public static void Prefix(UnusedTrainCarDeleter __instance, TrainCar trainCar)
    {
        string job="";

        if (trainCar.IsLoco)
        {
            foreach (TrainCar car in trainCar.trainset.cars)
            {
                job += $"{car.ID} {SingletonBehaviour<JobsManager>.Instance.GetJobOfCar(car, onlyActiveJobs: true)?.ID}, " ;
            }
        }

        //Multiplayer.LogDebug(() => $"AreDeleteConditionsFulfilled_Prefix({trainCar?.ID}) Visit Checker: {trainCar?.visitChecker?.IsRecentlyVisited}, Livery: {CarTypes.IsAnyLocomotiveOrTender(trainCar?.carLivery)}, Player Spawned: {trainCar?.playerSpawnedCar} jobs: {job}");

        current = trainCar;
    }

    
    [HarmonyPatch("AreDeleteConditionsFulfilled")]
    [HarmonyPostfix]
    public static void Postfix(UnusedTrainCarDeleter __instance, TrainCar trainCar, bool __result)
    {
        //Multiplayer.LogDebug(() => $"AreDeleteConditionsFulfilled_Postfix({trainCar?.ID}) = {__result}");
    }
    */

}
