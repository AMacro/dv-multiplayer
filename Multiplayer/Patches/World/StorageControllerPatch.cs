using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Multiplayer.Patches.World.Items;

internal static class LostAndFoundOwnership
{
    public static List<ItemBase> FilterForLocalPlayer(List<ItemBase> items)
    {
        if (items == null || !IsMultiplayerSession())
            return items;

        byte localPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (localPlayerId == 0)
            return new List<ItemBase>();

        var ownedItems = new List<ItemBase>(items.Count);
        foreach (ItemBase item in items)
        {
            if (item == null || !NetworkedItem.TryGetNetworkedItem(item, out var networkedItem))
                continue;

            if (networkedItem.OwnerPlayerId == localPlayerId)
                ownedItems.Add(item);
        }

        Multiplayer.LogDebug(() =>
            $"Lost and found filtered {items.Count} items to {ownedItems.Count} for player {localPlayerId}");
        return ownedItems;
    }

    public static void MarkLocalItemInLostAndFound(ItemBase item)
    {
        if (item == null || !IsMultiplayerSession() ||
            !NetworkedItem.TryGetNetworkedItem(item, out var networkedItem))
            return;

        byte localPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
        if (localPlayerId != 0 && networkedItem.OwnerPlayerId == localPlayerId)
            networkedItem.MarkItemStateDirty();
    }

    public static void MarkLocalItemsInLostAndFound(StorageController storage)
    {
        if (storage?.StorageLostAndFound == null)
            return;

        foreach (var item in storage.StorageLostAndFound.GetStorageItemList())
            MarkLocalItemInLostAndFound(item);
    }

    public static IEnumerator MarkLocalItemsAfterActivation(StorageController storage)
    {
        int itemCount = storage?.StorageLostAndFound?.GetStorageItemList()?.Count ?? 0;
        for (int i = 0; i <= itemCount; i++)
            yield return null;

        MarkLocalItemsInLostAndFound(storage);
    }

    private static bool IsMultiplayerSession()
    {
        var lifecycle = NetworkLifecycle.Instance;
        if (lifecycle == null || !lifecycle.IsClientRunning)
            return false;

        return lifecycle.IsHost()
            ? lifecycle.Server?.IsSinglePlayer == false
            : lifecycle.Client?.MaxPlayers > 1;
    }

    public static IEnumerable<CodeInstruction> FilterStorageList(IEnumerable<CodeInstruction> instructions)
    {
        var getStorageItems = AccessTools.Method(typeof(StorageBase), nameof(StorageBase.GetStorageItemList));
        var filter = AccessTools.Method(typeof(LostAndFoundOwnership), nameof(FilterForLocalPlayer));

        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(getStorageItems))
                yield return new CodeInstruction(OpCodes.Call, filter);
        }
    }
}

[HarmonyPatch(typeof(StorageController), nameof(StorageController.MoveItemsFromWorldToLostAndFound))]
internal static class MoveItemsFromWorldToLostAndFoundPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        LostAndFoundOwnership.FilterStorageList(instructions);

    [HarmonyPostfix]
    private static void Postfix(StorageController __instance) =>
        LostAndFoundOwnership.MarkLocalItemsInLostAndFound(__instance);
}

[HarmonyPatch(typeof(StorageController), nameof(StorageController.AddItemToLostAndFound))]
internal static class AddItemToLostAndFoundPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemBase item) =>
        LostAndFoundOwnership.MarkLocalItemInLostAndFound(item);
}

[HarmonyPatch(typeof(StorageItemTransformController), nameof(StorageItemTransformController.ActivateItems))]
internal static class ActivateLostAndFoundItemsPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        LostAndFoundOwnership.FilterStorageList(instructions);

    [HarmonyPostfix]
    private static void Postfix() =>
        CoroutineManager.Instance.StartCoroutine(
            LostAndFoundOwnership.MarkLocalItemsAfterActivation(StorageController.Instance));
}

[HarmonyPatch(typeof(StorageItemTransformController), nameof(StorageItemTransformController.DeactivateItems))]
internal static class DeactivateLostAndFoundItemsPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        LostAndFoundOwnership.FilterStorageList(instructions);

    [HarmonyPostfix]
    private static void Postfix() =>
        LostAndFoundOwnership.MarkLocalItemsInLostAndFound(StorageController.Instance);
}
