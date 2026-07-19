using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using HarmonyLib;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

internal sealed class GadgetSyncState : MonoBehaviour
{
    private const float SerializationInterval = 0.25f;

    public GadgetItem Item;
    private string cachedData = string.Empty;
    private float nextSerializationTime;
    private Coroutine placementRetry;

    public string GetPlacement()
    {
        GadgetBase gadget = Item?.Gadget;
        if (gadget == null || !gadget.IsLinked || gadget.Custom == null)
            return string.Empty;

        // Gadget placement is destination-local, matching GadgetItem.Place and the
        // game's save format. Local coordinates move with the train/customization
        // parent and therefore must not receive the global origin-shift offset.
        Transform transform = gadget.transform;
        ushort trainNetId = 0;
        if (gadget.Custom is TrainCarCustomization trainCustomization)
            NetworkedTrainCar.TryGetNetId(trainCustomization.TrainCar, out trainNetId);

        JObject data = new()
        {
            ["destination"] = gadget.Custom.GetIdentificationKey(),
            ["trainNetId"] = trainNetId,
            ["px"] = transform.localPosition.x,
            ["py"] = transform.localPosition.y,
            ["pz"] = transform.localPosition.z,
            ["rx"] = transform.localRotation.x,
            ["ry"] = transform.localRotation.y,
            ["rz"] = transform.localRotation.z,
            ["rw"] = transform.localRotation.w,
        };
        return data.ToString(Formatting.None);
    }

    public void SetPlacement(string serialized)
    {
        GadgetBase gadget = Item?.Gadget;
        if (gadget == null)
            return;

        if (string.IsNullOrEmpty(serialized))
        {
            if (placementRetry != null)
            {
                StopCoroutine(placementRetry);
                placementRetry = null;
            }
            if (gadget.IsLinked)
                gadget.Remove();
            return;
        }

        if (TryApplyPlacement(serialized))
        {
            // State dictionary enumeration is not an ordering contract. If gadget
            // data arrived first, reapply it now that train/customization links exist.
            SetGadgetData(cachedData);
            return;
        }

        if (placementRetry != null)
            StopCoroutine(placementRetry);
        placementRetry = StartCoroutine(RetryPlacement(serialized));
    }

    private bool TryApplyPlacement(string serialized)
    {
        GadgetBase gadget = Item?.Gadget;
        if (gadget == null)
            return false;

        JObject data = JObject.Parse(serialized);
        if (!TryResolveDestination(data, out Customization destination))
        {
            return false;
        }

        Vector3 position = new(
            data.Value<float>("px"),
            data.Value<float>("py"),
            data.Value<float>("pz"));
        Quaternion rotation = new(
            data.Value<float>("rx"),
            data.Value<float>("ry"),
            data.Value<float>("rz"),
            data.Value<float>("rw"));

        if (gadget.IsLinked)
        {
            if (gadget.Custom == destination)
            {
                gadget.transform.localPosition = position;
                gadget.transform.localRotation = rotation;
                return true;
            }
            gadget.Remove(false);
        }

        GadgetItem.Place(destination, position, rotation, Item);
        Multiplayer.Log($"Applied gadget placement for {name} to {destination.GetIdentificationKey()}.");
        return true;
    }

    private static bool TryResolveDestination(JObject data, out Customization destination)
    {
        destination = null;
        ushort trainNetId = data.Value<ushort?>("trainNetId") ?? 0;
        if (trainNetId != 0 &&
            NetworkedTrainCar.TryGet(trainNetId, out TrainCar trainCar) &&
            trainCar != null &&
            trainCar.Customization != null)
        {
            destination = trainCar.Customization;
            return true;
        }

        string destinationId = data.Value<string>("destination");
        return Customization.TryGetFromIdentificationKey(destinationId, out destination);
    }

    private IEnumerator RetryPlacement(string serialized)
    {
        const int maxFrames = 600;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            if (!TryApplyPlacement(serialized))
                continue;

            placementRetry = null;
            SetGadgetData(cachedData);
            yield break;
        }

        placementRetry = null;
        Multiplayer.LogWarning($"Timed out resolving gadget destination for {name}: {serialized}");
    }

    public string GetGadgetData()
    {
        if (Time.unscaledTime < nextSerializationTime)
            return cachedData;

        nextSerializationTime = Time.unscaledTime + SerializationInterval;
        JObject data = new();
        try
        {
            Item.Gadget.SaveDataRequested(data);
            cachedData = data.ToString(Formatting.None);
        }
        catch (Exception exception)
        {
            Multiplayer.LogWarning($"Could not serialize gadget state for {name}: {exception.Message}");
        }
        return cachedData;
    }

    public void SetGadgetData(string serialized)
    {
        cachedData = serialized ?? string.Empty;
        if (string.IsNullOrEmpty(cachedData) || Item?.Gadget == null)
            return;

        try
        {
            JObject data = JObject.Parse(cachedData);
            Item.Gadget.SaveDataLoaded(data);
            Item.Gadget.AfterSaveDataLoaded(data);
        }
        catch (Exception exception)
        {
            Multiplayer.LogWarning($"Could not apply gadget state for {name}: {exception.Message}");
        }
    }
}

[HarmonyPatch(typeof(GadgetItem))]
internal static class GadgetItemPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(GadgetItem __instance)
    {
        GadgetSyncState state = __instance.gameObject.GetOrAddComponent<GadgetSyncState>();
        state.Item = __instance;

        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "gadget.placement",
            state.GetPlacement,
            state.SetPlacement);
        item.RegisterTrackedValue(
            "gadget.data",
            state.GetGadgetData,
            state.SetGadgetData);
        item.FinaliseTrackedValues();
    }
}

[HarmonyPatch(typeof(GadgetSolderingTool))]
internal static class GadgetSolderingToolPatch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start(GadgetSolderingTool __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "solder.units",
            () => __instance.remainingUnits,
            value =>
            {
                __instance.remainingUnits = value;
                __instance.OnUnitsChanged();
            });
        item.FinaliseTrackedValues();
    }
}

[HarmonyPatch(typeof(DuctTape))]
internal static class DuctTapePatch
{
    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    private static void Awake(DuctTape __instance)
    {
        NetworkedItem item = ItemStateTracking.For(__instance);
        item.RegisterTrackedValue(
            "ductTape.uses",
            () => __instance.usesLeft,
            value =>
            {
                __instance.usesLeft = value;
                __instance.tapeModelUpdater?.UpdateActiveStates(__instance.PercentageUsesLeft);
            });
        item.FinaliseTrackedValues();
    }
}
