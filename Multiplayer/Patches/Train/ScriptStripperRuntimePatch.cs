using DV.Optimizers;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using UnityEngine;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(ScriptStripperRuntime))]
public static class ScriptStripperRuntimePatch
{
    [HarmonyPatch(nameof(ScriptStripperRuntime.Strip))]
    [HarmonyPrefix]
    public static bool Strip(GameObject goToStrip)
    {
        if(!NetworkLifecycle.Instance.IsHost())
            return true;

        var trainCar = TrainCar.Resolve(goToStrip);

        if (trainCar == null)
            return true;

        MonoBehaviour[] scripts = goToStrip.GetComponentsInChildren<MonoBehaviour>();
        Joint[] joints = goToStrip.GetComponentsInChildren<Joint>();
        Rigidbody[] rigidBodies = goToStrip.GetComponentsInChildren<Rigidbody>();
        Collider[] colliders = goToStrip.GetComponentsInChildren<Collider>();

        for (int i = 0; i < joints.Length; i++)
        {
            Object.Destroy(joints[i]);
        }

        for (int i = 0; i < rigidBodies.Length; i++)
        {
            Object.Destroy(rigidBodies[i]);
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if(!colliders[i].TryGetComponent<LocoResourceReceiver>(out _))
                Object.Destroy(colliders[i]);
            else
            {
                Multiplayer.LogDebug(() => $"ScriptStripperRuntimePatch.Strip() Keeping collider {colliders[i].gameObject.GetPath()} for {trainCar.ID}, has LocoResourceReceiver component.");
            }
        }

        for (int i = 0; i < scripts.Length; i++)
        {
            if (!scripts[i].GetType().Equals(typeof(LocoResourceReceiver)))
                Object.Destroy(scripts[i]);
            else
            {
                Multiplayer.LogDebug(() => $"ScriptStripperRuntimePatch.Strip() Keeping script {scripts[i].gameObject.GetPath()} for {trainCar.ID}, is LocoResourceReceiver component.");
            }
        }

        return false;
    }
}
