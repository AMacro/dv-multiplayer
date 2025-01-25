using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;
/*
[HarmonyPatch(typeof(StorageController))]
public static class StorageControllerPatch
{
    [HarmonyPatch(nameof(StorageController.AddItemToLostAndFound))]
    [HarmonyPrefix]
    static void AddItemToLostAndFound(StorageController __instance, ItemBase item)
    {

        Multiplayer.LogDebug(() =>
        {
            NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem netItem);
            return $"StorageController.AddItemToLostAndFound({item.name}) netId: {netItem?.NetId}\r\n{new System.Diagnostics.StackTrace()}";
        });
    }

    [HarmonyPatch(nameof(StorageController.RemoveItemFromLostAndFound))]
    [HarmonyPrefix]
    static void RemoveItemFromLostAndFound(StorageController __instance, ItemBase item)
    {

        Multiplayer.LogDebug(() =>
        {
            NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem netItem);
            return $"StorageController.RemoveItemFromLostAndFound({item.name}) netId: {netItem?.NetId}\r\n{new System.Diagnostics.StackTrace()}";
        });
    }

    [HarmonyPatch(nameof(StorageController.RequestLostAndFoundItemActivation))]
    [HarmonyPrefix]
    static void RequestLostAndFoundItemActivation(StorageController __instance)
    {

        Multiplayer.LogDebug(() =>
        {
            return $"StorageController.RequestLostAndFoundItemActivation()\r\n{new System.Diagnostics.StackTrace()}";
        });
    }

    [HarmonyPatch(nameof(StorageController.MoveItemsFromWorldToLostAndFound))]
    [HarmonyPrefix]
    static void MoveItemsFromWorldToLostAndFound(StorageController __instance, bool ignoreItemsWithRespawnParents)
    {

        Multiplayer.LogDebug(() =>
        {
            return $"StorageController.MoveItemsFromWorldToLostAndFound({ignoreItemsWithRespawnParents})\r\n{new System.Diagnostics.StackTrace()}";
        });
    }

    [HarmonyPatch(nameof(StorageController.ForceSummonAllWorldItemsToLostAndFound))]
    [HarmonyPrefix]
    static void ForceSummonAllWorldItemsToLostAndFound(StorageController __instance)
    {

        Multiplayer.LogDebug(() =>
        {
            return $"StorageController.ForceSummonAllWorldItemsToLostAndFound()\r\n{new System.Diagnostics.StackTrace()}";
        });
    }

    [HarmonyPatch(nameof(StorageController.RequestItemActivation))]
    [HarmonyPrefix]
    static void RequestItemActivation(StorageController __instance)
    {

        Multiplayer.LogDebug(() =>
        {
            return $"StorageController.RequestItemActivation()\r\n{new System.Diagnostics.StackTrace()}";
        });
    }

}
*/
