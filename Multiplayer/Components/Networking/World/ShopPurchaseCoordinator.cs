using DV.CabControls;
using DV.CashRegister;
using DV.Shops;
using DV.Utils;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Components.Networking.World;

internal readonly struct ShopPurchase
{
    public readonly string PrefabName;
    public readonly int Amount;
    public readonly float UnitPrice;

    public ShopPurchase(string prefabName, int amount, float unitPrice)
    {
        PrefabName = prefabName;
        Amount = amount;
        UnitPrice = unitPrice;
    }
}

internal static class ShopPurchaseCoordinator
{
    private static readonly Dictionary<string, Queue<ServerPlayer>> pendingOwners = [];
    private static bool applyingClientApproval;

    public static ShopPurchase[] Capture(CashRegisterWithModules register) => register.registerModules
        .OfType<ScanItemCashRegisterModule>()
        .Where(module => module.sellingItemSpec != null && module.Data.unitsToBuy >= 1f)
        .Select(module => new ShopPurchase(module.sellingItemSpec.ItemPrefabName, (int)module.Data.unitsToBuy,
            module.Data.pricePerUnit))
        .ToArray();

    public static bool IsShopRegister(CashRegisterWithModules register)
    {
        if (register == null)
            return false;
        if (NetworkedCashRegisterWithModules.TryGet(register, out var networkedRegister))
            return networkedRegister.IsShopRegister;
        if (register.GetComponentInParent<Shop>() is Shop parentShop && parentShop.cashRegister == register)
            return true;
        return GlobalShopController.Instance?.globalShopList?.Any(shop => shop.cashRegister == register) == true;
    }

    public static void QueueOwnership(IEnumerable<ShopPurchase> purchases, ServerPlayer buyer)
    {
        if (buyer == null)
            return;

        foreach (var purchase in purchases)
        {
            if (!pendingOwners.TryGetValue(purchase.PrefabName, out var owners))
                pendingOwners[purchase.PrefabName] = owners = new Queue<ServerPlayer>();

            for (int i = 0; i < purchase.Amount; i++)
                owners.Enqueue(buyer);
        }
    }

    public static void TryAssignOwner(NetworkedItem item)
    {
        if (!NetworkLifecycle.Instance.IsHost() || item?.Item?.InventorySpecs == null)
            return;

        string prefabName = item.Item.InventorySpecs.ItemPrefabName;
        if (!pendingOwners.TryGetValue(prefabName, out var owners) || owners.Count == 0)
            return;

        ServerPlayer buyer = owners.Dequeue();
        if (owners.Count == 0)
            pendingOwners.Remove(prefabName);

        buyer.AddOwnedItem(item.NetId);
        item.SetOwner(buyer.PlayerId);
        Multiplayer.LogDebug(() => $"Assigned purchased item {item.NetId} ({prefabName}) to {buyer.Username}");
    }

    public static bool SuppressClientInstantiation(InventoryItemSpec itemSpec, int amountBought)
    {
        if (!applyingClientApproval || NetworkLifecycle.Instance.IsHost())
            return false;

        if (itemSpec == null)
            return true;

        ShopItemData data = GlobalShopController.Instance.GetShopItemData(itemSpec);
        if (data != null)
            data.purchasedItems += amountBought;

        SingletonBehaviour<UnlockablesManager>.Instance.UnlockItem(itemSpec.ItemPrefabName, true);
        return true;
    }

    public static void ApplyApproval(NetworkedCashRegisterWithModules networkedRegister,
        CommonCashRegisterWithModulesActionPacket packet)
    {
        bool isBuyer = NetworkLifecycle.Instance.Client?.PlayerId == packet.BuyerPlayerId;
        if (isBuyer)
        {
            ApplyApprovedPrices(networkedRegister.CashRegister, packet.ItemPrefabNames, packet.ItemUnitPrices);
            networkedRegister.CompleteApprovedShopPurchase();
            return;
        }

        ApplyStockDelta(packet.ItemPrefabNames, packet.ItemAmounts);
    }

    private static void ApplyApprovedPrices(CashRegisterWithModules register, string[] prefabNames, float[] prices)
    {
        if (prefabNames == null || prices == null || prefabNames.Length != prices.Length)
            return;

        var pricesByPrefab = prefabNames
            .Select((prefabName, index) => new { prefabName, price = prices[index] })
            .ToDictionary(entry => entry.prefabName, entry => entry.price);
        foreach (ScanItemCashRegisterModule module in register.registerModules.OfType<ScanItemCashRegisterModule>())
            if (module.sellingItemSpec != null &&
                pricesByPrefab.TryGetValue(module.sellingItemSpec.ItemPrefabName, out float price))
                module.Data.pricePerUnit = price;
    }

    public static UnityEngine.Vector3 ClosestPlayerDelta(UnityEngine.Vector3 localPlayerPosition,
        UnityEngine.Vector3 anchor)
    {
        UnityEngine.Vector3 closest = localPlayerPosition - anchor;
        if (!NetworkLifecycle.Instance.IsHost())
            return closest;

        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            UnityEngine.Vector3 delta = player.WorldPosition - anchor;
            if (delta.sqrMagnitude < closest.sqrMagnitude)
                closest = delta;
        }

        return closest;
    }

    public static bool RunApprovedLocalPurchase(CashRegisterWithModules register)
    {
        applyingClientApproval = true;
        try
        {
            register.IsProcessingTransaction = false;
            return register.Buy();
        }
        finally
        {
            applyingClientApproval = false;
        }
    }

    public static void ApplyStockDelta(string[] prefabNames, int[] amounts)
    {
        if (prefabNames == null || amounts == null || prefabNames.Length != amounts.Length)
            return;

        for (int i = 0; i < prefabNames.Length; i++)
        {
            ShopItemData data = GlobalShopController.Instance.GetShopItemData(prefabNames[i]);
            if (data != null && amounts[i] != 0)
                data.purchasedItems = Math.Max(data.purchasedItems + amounts[i], 0);
        }

        GlobalShopController.Instance.Fire_GlobalShopDataChanged();
    }

    public static int MaxPlayers
    {
        get
        {
            NetworkLifecycle lifecycle = NetworkLifecycle.Instance;
            if (lifecycle?.Server != null)
                return lifecycle.Server.IsSinglePlayer ? 1 : Math.Max(Multiplayer.Settings.MaxPlayers, 1);
            return Math.Max(lifecycle?.Client?.MaxPlayers ?? 1, 1);
        }
    }

    public static void ScaleShopLimits(GlobalShopController controller)
    {
        int maxPlayers = MaxPlayers;
        foreach (ShopItemData data in controller.shopItemsData)
        {
            // A zero allowance is meaningful for locked/disabled shop entries.
            if (data.allowedToHaveAmount > 0)
                data.allowedToHaveAmount = checked(data.initialAmount * maxPlayers);
        }
    }

    public static void RecountExistingShopItems(GlobalShopController controller)
    {
        var counts = SingletonBehaviour<StorageController>.Instance.GetAllStorageItems()
            .Select(item => item?.GetComponent<ShopRestocker>())
            .Where(restocker => restocker != null && restocker.restockOnItemDestroyed)
            .Select(restocker => restocker.GetComponent<InventoryItemSpec>()?.ItemPrefabName)
            .Where(prefabName => !string.IsNullOrEmpty(prefabName))
            .GroupBy(prefabName => prefabName)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (ShopItemData data in controller.shopItemsData)
            data.purchasedItems = counts.TryGetValue(data.item.ItemPrefabName, out int count) ? count : 0;

        controller.Fire_GlobalShopDataChanged();
    }
}
