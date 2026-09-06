using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace Multiplayer.Patches.SaveGame;

[HarmonyPatch(typeof(StorageSerializer), nameof(StorageSerializer.SaveStorage))]
internal static class StorageSerializerPatch
{
    // Native container membership is needed even for remote carried items. Their
    // persistence belongs to the player's saved inventory, not the world storage.
    private static List<ItemBase> ExcludeRemoteCarriedContents(List<ItemBase> items)
    {
        if (items == null || NetworkLifecycle.Instance?.IsHost() != true)
            return items;
        return items.Where(item => !IsRemoteCarriedContent(item)).ToList();
    }

    private static bool IsRemoteCarriedContent(ItemBase item)
    {
        var root = item;
        var visited = new HashSet<ItemBase>();
        while (root != null && root.InContainer != null && visited.Add(root))
            root = root.InContainer.GetComponent<ItemBase>();
        return root != null && root != item &&
            NetworkedItem.TryGetNetworkedItem(root, out var networkedRoot) &&
            networkedRoot.OwnerPlayerId != 0 &&
            networkedRoot.OwnerPlayerId != NetworkLifecycle.Instance.Server.SelfId &&
            networkedRoot.CurrentState is ItemState.InHand or ItemState.InInventory;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getItems = AccessTools.Method(typeof(StorageBase), nameof(StorageBase.GetStorageItemList));
        var filter = AccessTools.Method(typeof(StorageSerializerPatch), nameof(ExcludeRemoteCarriedContents));
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(getItems))
                yield return new CodeInstruction(OpCodes.Call, filter);
        }
    }
}
