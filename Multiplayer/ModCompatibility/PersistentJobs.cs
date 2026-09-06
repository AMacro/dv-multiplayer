using DV.Logic.Job;
using DV.Utils;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace Multiplayer.ModCompatibility;

internal class PersistentJobs
{
    public static bool Active { get; private set; } = false;
    public static Action<Job> OnTrackChanged;
    public static Action<(Job, Car)> OnCarChanged;

    public static bool ResumeCoroRunning
    {
        get
        {
            if (resumeCoroRunningField == null)
                return false;
            return (bool)resumeCoroRunningField.GetValue(null);
        }
    }

    private static EventInfo trackChangedEvent;
    private static EventInfo carChangedEvent;
    private static FieldInfo resumeCoroRunningField;

    public static void TryLoadPersistentJobs()
    {
        UnityModManager.ModEntry persistentJobs = UnityModManager.FindMod("PersistentJobsMod");
        if (persistentJobs?.Enabled == true)
        {
            Multiplayer.Log("Persistent Jobs mod found...");
            CoroutineManager.Instance.Run(WaitForPersistentJobsAndLoad(persistentJobs));
        }
    }

    private static IEnumerator WaitForPersistentJobsAndLoad(UnityModManager.ModEntry persistentJobs)
    {
        float timeout = 1000f;
        float start = Time.realtimeSinceStartup;

        yield return new WaitUntil(() => persistentJobs?.Loaded == true || Time.realtimeSinceStartup - start > timeout);

        if (!persistentJobs.Loaded)
        {
            Multiplayer.LogWarning("Timed out waiting for PersistentJobs.");
            yield break;
        }

        try
        {
            Multiplayer.LogDebug(() => "Loading Persistent Jobs integration");

            Type features = AccessTools.TypeByName("PersistentJobsMod.ModInteraction.PersistentJobsModInteractionFeatures");
            Type farCarOpt = AccessTools.TypeByName("PersistentJobsMod.Optimization.FarCarOpt");

            trackChangedEvent = features.GetEvent("JobTracksChanged");
            if (trackChangedEvent == null)
            {
                Multiplayer.LogWarning("Persistent Jobs integration failed. JobTracksChanged event not found");
                Active = false;
                yield break;
            }

            carChangedEvent = features.GetEvent("JobCarsChanged");
            if (carChangedEvent == null)
            {
                Multiplayer.LogWarning("Persistent Jobs integration failed. JobCarsChanged event not found");
                Active = false;
                yield break;
            }

            resumeCoroRunningField = AccessTools.Field(farCarOpt, "ResumeCoroRunning");
            if (resumeCoroRunningField == null)
            {
                Multiplayer.LogWarning("Persistent Jobs integration failed. ResumeCoroRunning field not found");
                Active = false;
                yield break;
            }

            trackChangedEvent.AddEventHandler(null, new Action<Job>(TrackChangedEventHandler));
            carChangedEvent.AddEventHandler(null, new Action<(Job, Car)>(CarChangedEventHandler));

            Active = true;
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"Persistent Jobs integration failed\r\n{ex.Message}");
            Active = false;
        }
    }

    private static void TrackChangedEventHandler(Job job)
    {
        OnTrackChanged?.Invoke(job);
    }

    private static void CarChangedEventHandler((Job, Car) args)
    {
        OnCarChanged?.Invoke(args);
    }
}
