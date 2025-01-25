using DV.Interaction;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(GrabHandlerItem))]
public static class GrabHandlerItem_Patch
{
    [HarmonyPatch(nameof(GrabHandlerItem.Throw))]
    [HarmonyPrefix]
    private static void Throw(GrabHandlerItem __instance, Vector3 direction)
    {
        __instance.TryGetComponent<NetworkedItem>(out NetworkedItem netItem);

        if (netItem != null)
        {
            netItem.OnThrow(direction);
        }

    }


    /** 
     * Patch below methods to get attach/detach events
     */

    //public void AttachToAttachPoint(Transform attachPoint, bool positionStays)
    //{
    //    this.TogglePhysics(false);
    //    base.transform.SetParent(attachPoint, positionStays);
    //}

    //// Token: 0x060000A7 RID: 167 RVA: 0x000042EC File Offset: 0x000024EC
    //public override void EndInteraction()
    //{
    //    base.transform.parent = null;
    //    base.EndInteraction();
    //    this.TogglePhysics(true);
    //}



}
