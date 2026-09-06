using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using System.Collections.Generic;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(StorageController))]
public static class StorageControllerPatch
{
    // Reimplementation of the vanilla method with one addition: items belonging to other
    // players are skipped, so a player's Lost & Found (summon button, fast travel) can no
    // longer capture someone else's belongings. (#104)
    [HarmonyPrefix]
    [HarmonyPatch(nameof(StorageController.MoveItemsFromWorldToLostAndFound))]
    private static bool MoveItemsFromWorldToLostAndFound(StorageController __instance, bool includeNonRespawnParents, bool includeRespawnParents, bool includeSnappedItems)
    {
        foreach (ItemBase item in new List<ItemBase>(__instance.StorageWorld.GetStorageItemList()))
        {
            if (item == null || item.IsGrabbed() || !item.GetComponent<InventoryItemSpec>().BelongsToPlayer)
                continue;

            if (BelongsToAnotherPlayer(item))
                continue;

            bool isSnapped = item.IsSnapped;
            if (isSnapped && !includeSnappedItems)
                continue;

            if (item.GetComponent<RespawnOnDrop>().OnValidRespawnParent)
            {
                if (!includeRespawnParents || !__instance.PrepareItemForLostAndFound(item))
                    continue;
            }
            else if (!includeNonRespawnParents)
            {
                if (!includeSnappedItems || !isSnapped || !__instance.PrepareItemForLostAndFound(item))
                    continue;
            }
            else if (isSnapped && !__instance.PrepareItemForLostAndFound(item))
            {
                continue;
            }

            __instance.StorageWorld.RemoveItem(item);
            __instance.StorageLostAndFound.AddItem(item);
        }

        return false;
    }

    // AddItemToStorageItemList ends with an unguarded RespawnOnDrop.UpdateSpawnParams().
    // If anything removed that component the call throws, aborting the game's inventory
    // handling part-way and leaving an unusable item in the slot. Restore it defensively.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(StorageController.AddItemToStorageItemList), typeof(StorageBase), typeof(ItemBase))]
    private static void AddItemToStorageItemList(ItemBase item)
    {
        if (item != null && item.GetComponent<RespawnOnDrop>() == null)
        {
            Multiplayer.LogWarning($"Restoring missing RespawnOnDrop on {item.name} before adding it to storage");
            item.gameObject.AddComponent<RespawnOnDrop>();
        }
    }

    private static bool BelongsToAnotherPlayer(ItemBase item)
    {
        if (!NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem netItem))
            return false; // purely local item -> personal, summon allowed

        // Clients may only summon their own local (un-networked) items;
        // networked items are managed by the server
        if (!NetworkLifecycle.Instance.IsHost())
            return netItem.NetId != 0;

        // Host: skip items last owned by a connected remote player
        return netItem.LastOwnerId != 0
            && NetworkLifecycle.Instance.Server.TryGetServerPlayer(netItem.LastOwnerId, out ServerPlayer owner)
            && !NetworkLifecycle.Instance.IsHost(owner);
    }
}
