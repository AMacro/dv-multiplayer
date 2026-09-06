using DV.CabControls;
using DV.Shops;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(GlobalShopController))]
public static class GlobalShopControllerPatch
{
    // Player executing the current purchase, so spawned items can be stamped with their owner.
    // Set around Buy() by the cash register code; 0 means unknown.
    public static byte PurchasingPlayerId;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.AddItemToInstantiationQueue))]
    private static bool AddItemToInstantiationQueue(GlobalShopController __instance)
    {
        // Purchases are executed on the host only; clients receive the spawned
        // items through the item sync and the stock levels through ClientboundShopStockPacket
        return NetworkLifecycle.Instance.IsHost();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GlobalShopController.AddItemToInstantiationQueue))]
    private static void AddItemToInstantiationQueue_Postfix(GlobalShopController __instance)
    {
        // Vanilla does not raise GlobalShopDataChanged on purchase; raising it here keeps
        // host-side shop displays current and lets the server broadcast the new stock levels
        if (NetworkLifecycle.Instance.IsHost())
            __instance.Fire_GlobalShopDataChanged();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.Restock))]
    private static bool Restock(GlobalShopController __instance)
    {
        return NetworkLifecycle.Instance.IsHost();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.UpdateItemStocksOnGameLoad))]
    private static bool UpdateItemStocksOnGameLoad()
    {
        // Clients receive authoritative stock levels from the server on join
        return NetworkLifecycle.Instance.IsHost();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.InstantiatePurchasedItems))]
    private static bool InstantiatePurchasedItems(GlobalShopController __instance, ref IEnumerator __result)
    {
        // Reimplementation of the vanilla coroutine with one change: items are placed at the
        // shop when ANY player is near it, not just the host's player - the buyer may be a remote client
        __result = InstantiatePurchasedItems_AnyPlayer(__instance);
        return false;
    }

    private static IEnumerator InstantiatePurchasedItems_AnyPlayer(GlobalShopController gsc)
    {
        gsc.isInstantiatingItems = true;
        gsc.SetShopProcessingTransactionState(true);
        gsc.staggeredItemActivationCollection.Clear();

        Vector3 zero = Vector3.zero;
        Vector3 right = Vector3.right;
        int offset = 0;

        foreach (ShoppingCartEntry entry in gsc.itemInstantiationQueue)
        {
            for (int i = 0; i < entry.desiredAmount; i++)
            {
                Vector3 position = zero + right * offset;
                string itemPrefabName = entry.specs.ItemPrefabName;
                GameObject obj = Object.Instantiate(Resources.Load(itemPrefabName) as GameObject, position, Quaternion.identity);
                Transform itemTransform = obj.transform;
                obj.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;

                ShopRestocker restocker = obj.GetComponent<ShopRestocker>();
                if (restocker == null)
                    Multiplayer.LogError($"Missing ShopRestocker component on item prefab {itemPrefabName}. This should not happen.");
                else
                    restocker.restockOnItemDestroyed = true;

                ItemBase itemBase = itemTransform.GetComponent<ItemBase>();

                if (PurchasingPlayerId != 0 && NetworkedItem.TryGetNetworkedItem(itemBase, out var netItem))
                    netItem.SetLastOwner(PurchasingPlayerId);

                gsc.staggeredItemActivationCollection.Add((itemTransform, itemBase, entry.shop));
                offset++;
            }
        }

        PurchasingPlayerId = 0;

        yield return null;

        for (int i = gsc.staggeredItemActivationCollection.Count - 1; i >= 0; i--)
        {
            ItemBase item = gsc.staggeredItemActivationCollection[i].itemBase;
            SingletonBehaviour<StorageController>.Instance.AddItemToLostAndFound(item, updateTransformData: false);
            item.gameObject.SetActive(false);
        }

        foreach (var (itemTransform, itemBase, shop) in gsc.staggeredItemActivationCollection)
        {
            Vector3 spawnPosition = shop.itemSpawnTransform.position;

            RespawnOnDrop respawnOnDrop = itemBase.GetComponent<RespawnOnDrop>();
            if (respawnOnDrop != null && spawnPosition.AnyPlayerSqrMag() >= respawnOnDrop.maxDistance * respawnOnDrop.maxDistance)
            {
                itemBase.ItemRigidbody.velocity = Vector3.zero;
                itemBase.ItemRigidbody.angularVelocity = Vector3.zero;
                continue;
            }

            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(itemBase);
            itemTransform.position = spawnPosition;
            itemBase.ItemRigidbody.velocity = Vector3.zero;
            itemBase.ItemRigidbody.angularVelocity = Vector3.zero;
            itemBase.gameObject.SetActive(true);

            APurchaseTrigger purchaseTrigger = itemBase.GetComponent<APurchaseTrigger>();
            if (purchaseTrigger != null)
                purchaseTrigger.OnPurchased(itemBase.gameObject);

            yield return WaitFor.Seconds(0.5f);
        }

        gsc.itemInstantiationQueue.Clear();
        gsc.isInstantiatingItems = false;
        gsc.SetShopProcessingTransactionState(false);
    }
}
