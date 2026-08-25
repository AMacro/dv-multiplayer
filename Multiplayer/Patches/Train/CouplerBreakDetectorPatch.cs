using DV.Simulation.Brake;
using HarmonyLib;
using Multiplayer.Components.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CouplerBreakDetector))]
public static class CouplerBreakDetectorPatch
{
    [HarmonyPatch(nameof(CouplerBreakDetector.OnJointBreak))]
    [HarmonyPrefix]
    private static bool OnCouplerBreak_Prefix(CouplerBreakDetector __instance, float breakForce)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        Multiplayer.LogDebug(() => $"OnCouplerBreak_Prefix({__instance?.motherCoupler?.train?.ID}, breakForce: {breakForce})\r\n{new System.Diagnostics.StackTrace()}");
        return false;
    }
}
