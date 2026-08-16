using DV.Customization;
using DV.Customization.Gadgets;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch]
public static class CustomizationPatch
{
    [HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.Place)), HarmonyPostfix]
    private static void Placed(GadgetBase __result, Customization destination, Vector3 localPos, Quaternion localRot, GadgetItem gadgetItem)
    {
        if (__result == null || gadgetItem == null || CustomizationSyncScope.IsApplyingRemote || !NetworkedItem.TryGetNetId(gadgetItem.Item, out var id) || !CustomizationRef.TryCreate(destination, out var target)) return;
        CustomizationStateManager.SendAction(new CommonCustomizationPacket { Action = CustomizationAction.PlaceGadget, ItemNetId = id,
            TargetKey = target.IdentificationKey, Position = localPos, Rotation = localRot });
    }

    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove)), HarmonyPrefix]
    private static void BeforeRemoveGadget(GadgetBase __instance, bool reparentToTrainCar, out RemoveGadgetState __state)
    {
        __state = default;
        if (CustomizationSyncScope.IsApplyingRemote || __instance?.GadgetItem?.Item == null ||
            !NetworkedItem.TryGetNetId(__instance.GadgetItem.Item, out var id)) return;
        __state = new RemoveGadgetState { ShouldSend = true, ItemNetId = id, ReparentToTrainCar = reparentToTrainCar };
    }

    [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove)), HarmonyPostfix]
    private static void RemovedGadget(GadgetItem __result, bool reparentToTrainCar, RemoveGadgetState __state)
    {
        if (__result == null) return;

        if (reparentToTrainCar)
            StabilizeRemovedGadget(__result);

        if (!__state.ShouldSend) return;
        CustomizationStateManager.SendAction(new CommonCustomizationPacket { Action = CustomizationAction.RemoveGadget,
            ItemNetId = __state.ItemNetId, Flag = __state.ReparentToTrainCar });
    }

    private static void StabilizeRemovedGadget(GadgetItem gadgetItem)
    {
        var rigidbody = gadgetItem?.Item?.ItemRigidbody;
        if (rigidbody == null)
            return;

        var trainCar = gadgetItem.GetComponentInParent<TrainCar>();
        if (trainCar?.rb != null)
        {
            // The backing item was inactive while installed and can retain its old
            // hand/drop velocity. Start it with the train's motion at this exact
            // point so re-enabling physics does not launch it across the interior.
            rigidbody.velocity = trainCar.rb.GetPointVelocity(rigidbody.worldCenterOfMass);
            rigidbody.angularVelocity = trainCar.rb.angularVelocity;
        }
        else
        {
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }
    }

    [HarmonyPatch(typeof(Customization), nameof(Customization.AddHole)), HarmonyPostfix]
    private static void AddedHole(Customization __instance, Vector3 localPosition, Vector3 localNormal, Collider __result)
    { if (__result != null) SendHole(__instance, CustomizationAction.AddHole, localPosition, default, localNormal); }

    [HarmonyPatch(typeof(Customization), nameof(Customization.MoveHole)), HarmonyPrefix]
    private static void BeforeMove(Collider hole, out HoleMoveState __state) => __state = new HoleMoveState
    { IsValid = hole != null, PreviousPosition = hole != null ? hole.transform.localPosition : default };
    [HarmonyPatch(typeof(Customization), nameof(Customization.MoveHole)), HarmonyPostfix]
    private static void MovedHole(Customization __instance, Vector3 localPosition, Vector3 localNormal, HoleMoveState __state)
    { if (__state.IsValid) SendHole(__instance, CustomizationAction.MoveHole, localPosition, __state.PreviousPosition, localNormal); }

    [HarmonyPatch(typeof(Customization), nameof(Customization.RemoveHole)), HarmonyPrefix]
    private static void BeforeRemove(Collider holeCollider, out Vector3 __state) => __state = holeCollider != null ? holeCollider.transform.localPosition : default;
    [HarmonyPatch(typeof(Customization), nameof(Customization.RemoveHole)), HarmonyPostfix]
    private static void RemovedHole(Customization __instance, bool __result, Vector3 __state)
    { if (__result) SendHole(__instance, CustomizationAction.RemoveHole, default, __state, default); }

    private static void SendHole(Customization c, CustomizationAction action, Vector3 pos, Vector3 old, Vector3 normal)
    {
        if (CustomizationSyncScope.IsApplyingRemote || !CustomizationRef.TryCreate(c, out var target)) return;
        CustomizationStateManager.SendAction(new CommonCustomizationPacket { Action = action, TargetKey = target.IdentificationKey,
            Position = pos, PreviousPosition = old, Normal = normal });
    }

    private struct RemoveGadgetState
    {
        public bool ShouldSend;
        public ushort ItemNetId;
        public bool ReparentToTrainCar;
    }

    private struct HoleMoveState
    {
        public bool IsValid;
        public Vector3 PreviousPosition;
    }
}
