using DV.LocoRestoration;
using HarmonyLib;
using Multiplayer.Components.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(LocoRestorationController))]
public static class LocoRestorationControllerStartPatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {

        //We're targeting an 'IEnumerable'; this gets compiled as a state machine with
        //a method per state.
        //Find all of the resultant states that are a 'MoveNext', these are the methods we need to patch.
        //Doing this dynamically reduces the chance a game update breaks the transpiler

        var methods = typeof(LocoRestorationController)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(t => t.Name.StartsWith("<Start>"))
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(m => m.Name == "MoveNext");

        // Multiplayer.LogDebug(() => $"LocoRestorationControllerPatch.TargetMethods found {methods.Count()} methods to patch:\r\n{string.Join("\r\n\t", methods.Select(m => m.Name))}");

        return methods;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Start(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codeMatcher = new CodeMatcher(instructions, generator);

        // Find access to AStartGameData.carsAndJobsLoadingFinished and replace it with a call to IsWorldSyncComplete()
        codeMatcher
            .MatchStartForward
            (
                CodeMatch.LoadsField(AccessTools.DeclaredField(typeof(AStartGameData), nameof(AStartGameData.carsAndJobsLoadingFinished)))
            )
            .ThrowIfNotMatch("Failed to find references to AStartGameData.carsAndJobsLoadingFinished")
            .Set(OpCodes.Call, AccessTools.DeclaredMethod(typeof(LocoRestorationControllerStartPatch),nameof(IsWorldSyncComplete)));

        return codeMatcher.Instructions();
    }

    private static bool IsWorldSyncComplete()
    {
        if (NetworkLifecycle.Instance.IsHost())
            return AStartGameData.carsAndJobsLoadingFinished;

        return NetworkLifecycle.Instance.Client.LoadingState == Networking.Data.PlayerLoadingState.Complete;
    }
}

[HarmonyPatch(typeof(LocoRestorationController))]
public static class LocoRestorationControllerInitCarForRestorationPatch
{
    [HarmonyPatch(nameof(LocoRestorationController.InitCarForRestoration))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> InitCarForRestoration(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codeMatcher = new CodeMatcher(instructions, generator);

        // Replace GetComponentInChildren<LocoZoneBlocker>() with GetLocoZoneBlockerForCar()
        codeMatcher
            .MatchStartForward
            (
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Component), nameof(Component.GetComponentInChildren), null, [typeof(LocoZoneBlocker)]))
            )
            .ThrowIfNotMatch("Failed to find GetComponentInChildren<LocoZoneBlocker>() call")
            .SetInstruction(CodeInstruction.Call(typeof(LocoRestorationControllerInitCarForRestorationPatch), nameof(GetLocoZoneBlockerForCar)));

        // Find the additionalLicenseRequired update and insert a call to LocoZoneBlocker.Start() after it
        codeMatcher
            .MatchStartForward
            (
                new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(LocoZoneBlocker), nameof(LocoZoneBlocker.additionalLicenseRequired)))
            )
            .ThrowIfNotMatch("Failed to find LocoZoneBlocker.additionalLicenseRequired update")
            .Advance(1)
            .Insert
            (
                new CodeInstruction(OpCodes.Ldloc_0), // Load locoZoneBlocker instance
                CodeInstruction.Call(typeof(LocoZoneBlocker), nameof(LocoZoneBlocker.Start))
            );

        return codeMatcher.Instructions();
    }

    private static LocoZoneBlocker GetLocoZoneBlockerForCar(TrainCar car)
    {
        var locoZoneBlocker = car.GetComponentInChildren<LocoZoneBlocker>(true) ?? (car?.interior?.GetComponentInChildren<LocoZoneBlocker>(true));

        if (locoZoneBlocker == null)
             Multiplayer.LogWarning(() => $"LocoZoneBlocker not found for car {car.ID}");

        return locoZoneBlocker;
    }

}
