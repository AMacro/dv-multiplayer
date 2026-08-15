using DV.CabControls;
using DV.Shops;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        var register = __instance.globalShopList
            .Select(shop => shop.cashRegister)
            .FirstOrDefault(cashRegister => NetworkedCashRegisterWithModules.TryGet(cashRegister, out _));
        if (register == null || !NetworkedCashRegisterWithModules.TryGet(register, out var networkedRegister))
            return;

        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = networkedRegister.NetId,
            Action = CashRegisterAction.ShopStockChanged,
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

        foreach (CodeInstruction instruction in instructions)
        {
            if (!replaced && instruction.Calls(subtraction))
            {
                instruction.operand = closestPlayerDelta;
                replaced = true;
            }

            yield return instruction;
        }

        if (!replaced)
            Multiplayer.LogError("GlobalShopController.InstantiatePurchasedItems distance patch failed");
    }
}
