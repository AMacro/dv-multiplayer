using System.Collections;
using DV.ThingTypes;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(JobValidator))]
public static class JobValidator_Patch
{
    [HarmonyPatch(nameof(JobValidator.Start))]
    [HarmonyPostfix]
    private static void Start(JobValidator __instance)
    {
        //Multiplayer.Log($"JobValidator Awake!");
        NetworkedStationController.QueueJobValidator(__instance);
    }


    [HarmonyPatch(nameof(JobValidator.ProcessJobOverview))]
    [HarmonyPrefix]
    private static bool ProcessJobOverview_Prefix(JobValidator __instance, JobOverview jobOverview)
    {

        if(__instance.bookletPrinter.IsOnCooldown)
        {
            __instance.bookletPrinter.PlayErrorSound();
            return false;
        }

        if(!NetworkedJob.TryGetFromJob(jobOverview.job, out NetworkedJob networkedJob) || jobOverview.job.State != JobState.Available)
        {
            NetworkLifecycle.Instance.Client.LogWarning($"ProcessJobOverview_Prefix({jobOverview?.job?.ID}) NetworkedJob found: {networkedJob != null}, Job state: {jobOverview?.job?.State}");
            __instance.bookletPrinter.PlayErrorSound();
            jobOverview.DestroyJobOverview();
            return false;
        }

        if (NetworkLifecycle.Instance.IsHost())
        {
            Multiplayer.Log($"ProcessJobOverview_Prefix({jobOverview?.job?.ID}) IsHost");
            networkedJob.JobValidator = __instance;
            return true;
        }

        if (!networkedJob.ValidatorRequestSent)
        //    return (networkedJob.ValidatorResponseReceived && networkedJob.ValidationAccepted);         
        //else
            SendValidationRequest(__instance, networkedJob, ValidationType.JobOverview);

        return false;
    }


    [HarmonyPatch(nameof(JobValidator.ValidateJob))]
    [HarmonyPrefix]
    private static bool ValidateJob_Prefix(JobValidator __instance, JobBooklet jobBooklet)
    {
        if (__instance.bookletPrinter.IsOnCooldown)
        {
            __instance.bookletPrinter.PlayErrorSound();
            return false;
        }

        if (!NetworkedJob.TryGetFromJob(jobBooklet.job, out NetworkedJob networkedJob) || jobBooklet.job.State != JobState.InProgress)
        {
            NetworkLifecycle.Instance.Client.LogWarning($"ValidateJob({jobBooklet?.job?.ID}) NetworkedJob found: {networkedJob != null}, Job state: {jobBooklet?.job?.State}");
            __instance.bookletPrinter.PlayErrorSound();
            jobBooklet.DestroyJobBooklet();
            return false;
        }

        if (NetworkLifecycle.Instance.IsHost())
        {
            networkedJob.JobValidator = __instance;
            return true;
        }

        if (networkedJob.ValidatorRequestSent)
            return (networkedJob.ValidatorResponseReceived && networkedJob.ValidationAccepted);
        else
            SendValidationRequest(__instance, networkedJob, ValidationType.JobBooklet);

        return false;
    }

    private static void SendValidationRequest(JobValidator validator,NetworkedJob netJob, ValidationType type)
    {
        //find the current station we're at
        if (NetworkedStationController.GetFromJobValidator(validator, out NetworkedStationController networkedStation))
        {
            //Set initial job state parameters
            netJob.ValidatorRequestSent = true;
            netJob.ValidatorResponseReceived = false;
            netJob.ValidationAccepted = false;
            netJob.JobValidator = validator;
            netJob.ValidationType = type;

            NetworkLifecycle.Instance.Client.SendJobValidateRequest(netJob, networkedStation);
            CoroutineManager.Instance.StartCoroutine(AwaitResponse(validator, netJob));
        }
        else
        {
            NetworkLifecycle.Instance.Client.LogError($"SendValidation({netJob?.Job?.ID}, {type}) Failed to find NetworkedStation");
            validator.bookletPrinter.PlayErrorSound();
        }
    }
    private static IEnumerator AwaitResponse(JobValidator validator, NetworkedJob networkedJob)
    {
        yield return new WaitForSecondsRealtime((NetworkLifecycle.Instance.Client.Ping * 3f)/1000);

        NetworkLifecycle.Instance.Client.Log($"JobValidator_Patch.AwaitResponse() ResponseReceived: {networkedJob?.ValidatorResponseReceived}, Accepted: {networkedJob?.ValidationAccepted}");

        if (networkedJob == null || (!networkedJob.ValidatorResponseReceived || !networkedJob.ValidationAccepted))
        {
            validator.bookletPrinter.PlayErrorSound();
            yield break;
        }
    }
}
