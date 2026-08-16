using DV.CabControls;
using DV.Shops;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(GlobalShopController))]
internal static class GlobalShopControllerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("InitializeShopData")]
    private static void InitializeShopData(GlobalShopController __instance) =>
        ShopPurchaseCoordinator.ScaleShopLimits(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.Fire_GlobalShopDataChanged))]
    private static void FireGlobalShopDataChanged(GlobalShopController __instance) =>
        ShopPurchaseCoordinator.ScaleShopLimits(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GlobalShopController.UpdateItemStocksOnGameLoad))]
    private static void UpdateItemStocksOnGameLoad() =>
        ShopPurchaseCoordinator.RecountExistingShopItems(GlobalShopController.Instance);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.AddItemToInstantiationQueue))]
    private static bool AddItemToInstantiationQueue(InventoryItemSpec boughtItemSpec, int amountBought) =>
        !ShopPurchaseCoordinator.SuppressClientInstantiation(boughtItemSpec, amountBought);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GlobalShopController.Restock))]
    private static void Restock_Prefix(GlobalShopController __instance, string itemPrefabName, out int __state) =>
        __state = __instance.GetShopItemData(itemPrefabName)?.purchasedItems ?? 0;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GlobalShopController.Restock))]
    private static void Restock_Postfix(GlobalShopController __instance, string itemPrefabName, int __state)
    {
        ShopItemData data = __instance.GetShopItemData(itemPrefabName);
        if (!NetworkLifecycle.Instance.IsHost() || data == null || data.purchasedItems >= __state)
            return;

        NetworkLifecycle.Instance.Server.SendShopAction(new CommonShopPacket
        {
            Action = ShopAction.StockChanged,
            ItemPrefabNames = [itemPrefabName],
            ItemAmounts = [-1]
        });
    }
}

[HarmonyPatch]
internal static class GlobalShopControllerInstantiatePurchasedItemsPatch
{
    public static IEnumerable<MethodBase> TargetMethods() => typeof(GlobalShopController)
        .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(type => type.Name.StartsWith("<InstantiatePurchasedItems>"))
        .SelectMany(type => type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
        .Where(method => method.Name == "MoveNext");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo subtraction = AccessTools.Method(typeof(Vector3), "op_Subtraction",
            [typeof(Vector3), typeof(Vector3)]);
        MethodInfo closestPlayerDelta = AccessTools.Method(typeof(ShopPurchaseCoordinator),
            nameof(ShopPurchaseCoordinator.ClosestPlayerDelta));
        bool replaced = false;
        bool ownershipInjected = false;

        foreach (CodeInstruction instruction in instructions)
        {
            if (!replaced && instruction.Calls(subtraction))
            {
                instruction.operand = closestPlayerDelta;
                replaced = true;
            }

            yield return instruction;

            if (!ownershipInjected && instruction.operand is MethodInfo called &&
                called.Name == nameof(Object.Instantiate) && called.IsGenericMethod &&
                called.GetGenericArguments().Length == 1 && called.GetGenericArguments()[0] == typeof(GameObject))
            {
                var parameters = called.GetParameters();
                if (parameters.Length == 3 && parameters[1].ParameterType == typeof(Vector3) &&
                    parameters[2].ParameterType == typeof(Quaternion))
                {
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return CodeInstruction.Call(typeof(ShopPurchaseCoordinator),
                        nameof(ShopPurchaseCoordinator.AssignPurchasedObject));
                    ownershipInjected = true;
                }
            }
        }

        if (!replaced)
            Multiplayer.LogError("GlobalShopController.InstantiatePurchasedItems distance patch failed");
        if (!ownershipInjected)
            Multiplayer.LogError("GlobalShopController.InstantiatePurchasedItems ownership patch failed");
    }
}
