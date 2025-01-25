using DV;
using DV.Interaction;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Utils;
using System.Collections;
using Unity.Jobs;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(JobOverview))]
public static class JobOverview_Patch
{
    //[HarmonyPatch(nameof(JobOverview.Start))]
    //[HarmonyPostfix]
    //private static void Start(JobOverview __instance)
    //{
    //    if (!NetworkedJob.TryGetFromJob(__instance.job, out NetworkedJob networkedJob))
    //    {
    //        Multiplayer.LogError($"JobOverview.Start() NetworkedJob not found for Job ID: {__instance.job?.ID}");
    //        __instance.DestroyJobOverview();
    //        return;
    //    }

    //    networkedJob.JobOverview = __instance;
    //}


    [HarmonyPatch(nameof(JobOverview.DestroyJobOverview))]
    [HarmonyPrefix]
    private static void DestroyJobOverview(JobOverview __instance)
    {
        if (!NetworkedJob.TryGetFromJob(__instance.job, out NetworkedJob networkedJob))
            Multiplayer.LogError($"JobOverview.DestroyJobOverview() NetworkedJob not found for Job ID: {__instance.job}");
        else
            networkedJob.JobOverview = null;
    }
}
