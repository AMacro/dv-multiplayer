using System;
using DV.Customization.Gadgets;
using DV.JObjectExtstensions;
using Multiplayer.Components.Networking.World;
using Newtonsoft.Json.Linq;

namespace Multiplayer.Components.SaveGame;

// The native event supplies the actual subscribers, including other mods and
// nested snap behaviours. Publicized APIs provide all component access.
internal static class NativeItemPersistence
{
    [ThreadStatic] private static CaptureContext currentCapture;

    private sealed class CaptureContext
    {
        public string Description;
    }

    internal static JObject Capture(NetworkedItem item)
    {
        var save = item.GetComponent<ItemSaveData>();
        if (save == null)
            return new JObject();

        JObject previous = (JObject)(save.saveData?.DeepClone() ?? new JObject());
        var context = new CaptureContext { Description = $"{item.NetId} ({item.name})" };
        CaptureContext outerCapture = currentCapture;
        currentCapture = context;
        save.saveData = (JObject)previous.DeepClone();
        try
        {
            save.SaveItemData();
        }
        catch (Exception exception)
        {
            // Covers failures outside the intercepted delegate call as well.
            save.saveData = previous;
            ReportFailure(context, "ItemSaveData", exception);
        }
        finally
        {
            currentCapture = outerCapture;
        }

        return (JObject)save.saveData.DeepClone();
    }

    // Called from the patched native delegate invocation. Outside an inventory
    // capture preserve native multicast semantics, including exception propagation.
    internal static JObject InvokeSaveListeners(ItemSaveData.ItemSaveDataRequestedDelegate listeners, JObject state)
    {
        CaptureContext context = currentCapture;
        if (context == null)
            return listeners(state);

        foreach (ItemSaveData.ItemSaveDataRequestedDelegate listener in listeners.GetInvocationList())
        {
            GadgetItem gadget = listener.Target as GadgetItem;
            bool nativeGadgetWriter = gadget != null && listener ==
                (ItemSaveData.ItemSaveDataRequestedDelegate)gadget.OnItemSaveDataRequestedInternal;
            if (nativeGadgetWriter && state["gadgetData"] == null && gadget.gadgetData.HasValues)
                state["gadgetData"] = gadget.gadgetData.DeepClone();

            bool succeeded = ItemStateTransaction.TryApply(state, candidate =>
            {
                if (nativeGadgetWriter)
                    SaveGadget(gadget, candidate);
                else
                    listener(candidate); // Native SaveItemData ignores the return value.
            }, out Exception failure);

            if (!succeeded)
            {
                ReportFailure(context, listener.Target?.GetType().Name ?? "static listener", failure);
                // These native handlers write charge after consulting controls.
                // Preserve current charge independently when those controls fail.
                Battery battery = listener.Target switch
                {
                    FlashlightItem flashlight => flashlight.battery,
                    LocomotiveRemoteController remote => remote.battery,
                    _ => null,
                };
                if (battery != null && !ItemStateTransaction.TryApply(state, battery.SaveState, out Exception batteryFailure))
                    ReportFailure(context, "Battery", batteryFailure);
            }
        }
        return state;
    }

    private static void SaveGadget(GadgetItem item, JObject state)
    {
        // The game's wrapper clears gadgetData and swallows serialization errors.
        // Call its publicized operations directly so a failed write can be rejected.
        JObject previous = (JObject)item.gadgetData.DeepClone();
        var candidate = new JObject();
        bool succeeded = false;
        try
        {
            item.Gadget.SaveDataRequested(candidate);
            state.SetJObject("gadgetData", candidate);
            item.OnItemSaveDataRequested(state);
            var target = item.Gadget.Custom;
            state.SetString("placedOn", target != null ? target.GetIdentificationKey() : string.Empty);
            if (target != null)
            {
                state.SetVector3("position", item.Gadget.transform.localPosition);
                state.SetVector3("rotation", item.Gadget.transform.localEulerAngles);
            }
            ItemStateTransaction.Replace(item.gadgetData, candidate);
            succeeded = true;
        }
        finally
        {
            if (!succeeded)
                ItemStateTransaction.Replace(item.gadgetData, previous);
        }
    }

    private static void ReportFailure(CaptureContext context, string component, Exception exception)
    {
        Multiplayer.LogWarning($"Could not save {component} state for {context.Description}; " +
            $"retaining the previous state for this callback: {exception.Message}");
    }
}
