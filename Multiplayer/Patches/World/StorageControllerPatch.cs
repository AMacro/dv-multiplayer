using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
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
}

[HarmonyPatch(typeof(StorageItemTransformController), nameof(StorageItemTransformController.ActivateItems))]
internal static class ActivateLostAndFoundItemsPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        LostAndFoundOwnership.FilterStorageList(instructions);
}
