using DV.Utils;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StartingItemsController;
using UnityEngine;
using DV.JObjectExtstensions;
using Multiplayer.Components.SaveGame;

namespace Multiplayer.Components.Networking.World;

public class NetworkedStorage:SingletonBehaviour<NetworkedStorage>
{

    public new static string AllowAutoCreate()
    {
        return $"[{nameof(NetworkedStorage)}]";
    }

    public static void LoadInventory(ServerPlayer player, SaveGameData playerData, bool firstTime)
    {
        Multiplayer.LogDebug(()=> "Player data:\r\n" + playerData?.dataObject?.ToString(Newtonsoft.Json.Formatting.Indented));

        HashSet<Rigidbody> itemRigidBodiesToToggleOffKinematicState = new HashSet<Rigidbody>();
        HashSet<(ItemSaveData itemSaveData, JObject itemState)> itemSaveDataCollection = new HashSet<(ItemSaveData, JObject)>();
        Dictionary<GameObject, StorageItemData> inventoryBelongingData = new Dictionary<GameObject, StorageItemData>();


        //load inventory and unique lost and found
        List<StorageItemData> inventory = StorageSerializer.LoadStorageData(StorageType.Inventory, playerData); //NetworkedSaveGameManager.Instance.Server_GetPlayerInventory(playerData);
        List<StorageItemData> lost = StorageSerializer.LoadStorageData(StorageType.LostAndFound, playerData); //NetworkedSaveGameManager.Instance.Server_GetPlayerLostAndFound(playerData);


        //clean up items and ensure essentials are available
        List<StorageItemData> combinedSavedItems = StartingItemsController.Instance.CombineAndResolveIllegalDuplicates(inventory, lost, new List<StorageItemData>());
        StartingItemsController.Instance.StartingItemsSafeguard(inventory, combinedSavedItems, playerData, firstTime);

        //foreach (StorageItemData item in inventory)
        //{
        //    if (item == null)
        //        continue;

        //    NetworkedItem networkedItem = new NetworkedItem();
        //    networkedItem.StorageData = item;

        //    player.inventory.Add(networkedItem);
        //}

        //foreach (StorageItemData item in lost)
        //{
        //    if (item == null)
        //        continue;

        //    NetworkedItem networkedItem = new NetworkedItem();
        //    networkedItem.StorageData = item;

        //    player.lostAndFound.Add(networkedItem);
        //}
        List<GameObject> newItems = StartingItemsController.Instance.InstantiateStorageItems(inventory, ignoreNulls: true, ref inventoryBelongingData, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);

        
        foreach (GameObject item in newItems)
        {
            NetworkedItem nwItem = item.GetComponentInChildren<NetworkedItem>();

            if (nwItem != null)
            {
                nwItem.StorageData = inventoryBelongingData[item];
                player.inventory.Add(nwItem);
                nwItem.transform.SetParent(NetworkedStorage.Instance.transform, false);
                Multiplayer.Log($"Item: {item.name}, NetId: {nwItem.NetId}");
            }
        }

        //Transform worldParent;
        //Dictionary<GameObject, (Vector3, Quaternion, Rigidbody, Transform, bool)> transformRelatedWorldItemStates = InstantiateStorageItemsWorld(list3, inventoryBelongingData, list2, out worldParent, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);
        //List<GameObject> instantiatedItemsWorld = new List<GameObject>(transformRelatedWorldItemStates.Keys);
        //List<GameObject> instantiatedItemsLostAndFound = InstantiateStorageItems(list2, ignoreNulls: true, ref inventoryBelongingData, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);

        //StartingItemsController.Instance.AddStartingItems(saveGameData, true);
        //List<ushort> netIds = GetStartingItems();
        //}

        /*
        if (saveGameData.GetString(SaveGameKeys.Game_mode) == "FreeRoam")
            LicenseManager.Instance.GrabAllGameModeSpecificUnlockables(SaveGameKeys.Game_mode);
        else

        */
    }

    //Generate starting items and add them to a game object for the player
    //Return the list of netIDs that can be passed back to the server
    //todo: do we need item types or something so we can spawn them client side?
    //private static List<ushort> GetStartingItems()
    //{

    //    //List<StorageItemData> list = StorageSerializer.LoadStorageData(StorageType.Inventory, saveGameData);
    //    //List<StorageItemData> list2 = StorageSerializer.LoadStorageData(StorageType.LostAndFound, saveGameData);
    //    //List<StorageItemData> list3 = StorageSerializer.LoadStorageData(StorageType.World, saveGameData);
    //    Dictionary<GameObject, StorageItemData> inventoryBelongingData = new Dictionary<GameObject, StorageItemData>();
    //    List<StorageItemData> combinedSavedItems = CombineAndResolveIllegalDuplicates(list, list2, list3);
    //    StartingItemsSafeguard(list, combinedSavedItems, saveGameData, firstTime);

    //    LicensesSafeguard(list2, combinedSavedItems, saveGameData, firstTime);

    //    HashSet<(ItemSaveData itemSaveData, JObject itemState)> itemSaveDataCollection = new HashSet<(ItemSaveData, JObject)>();
    //    InstantiateStorageItems(list, ignoreNulls: true, ref inventoryBelongingData, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);
    //    Transform worldParent;
    //    Dictionary<GameObject, (Vector3, Quaternion, Rigidbody, Transform, bool)> transformRelatedWorldItemStates = InstantiateStorageItemsWorld(list3, inventoryBelongingData, list2, out worldParent, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);
    //    List<GameObject> instantiatedItemsWorld = new List<GameObject>(transformRelatedWorldItemStates.Keys);
    //    List<GameObject> instantiatedItemsLostAndFound = InstantiateStorageItems(list2, ignoreNulls: true, ref inventoryBelongingData, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);
    //    yield return null;
    //    yield return FinalizeItemStateLoading(transformRelatedWorldItemStates, worldParent, itemSaveDataCollection, itemRigidBodiesToToggleOffKinematicState);
    //    List<InventoryItemData> inventoryBelongingData2 = CreateInventoryData(inventoryBelongingData);
    //    InitializeInventory(inventoryBelongingData2);
    //    foreach (GameObject item in instantiatedItemsLostAndFound)
    //    {
    //        SingletonBehaviour<StorageController>.Instance.AddItemToStorageItemList(SingletonBehaviour<StorageController>.Instance.StorageLostAndFound, item);
    //    }

    //    foreach (GameObject item2 in instantiatedItemsWorld)
    //    {
    //        SingletonBehaviour<StorageController>.Instance.AddItemToStorageItemList(SingletonBehaviour<StorageController>.Instance.StorageWorld, item2);
    //    }

    //    itemsLoaded = true;
    //}
}
