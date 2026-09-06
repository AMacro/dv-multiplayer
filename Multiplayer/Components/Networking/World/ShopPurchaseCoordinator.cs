using DV.CabControls;
using DV.CashRegister;
using DV.Shops;
using DV.Utils;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Components.SaveGame;
using Multiplayer.Networking.Packets.Common;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;

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
    private static readonly Dictionary<ShopItemData, int> scaledAllowances = [];
    private static bool applyingClientApproval;
    private static bool stockRecountPending;
    private static int sessionGeneration;

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

    public static void TransferPendingOwnership(ServerPlayer from, ServerPlayer to)
    {
        if (from == null)
            return;

        foreach (string prefabName in pendingOwners.Keys.ToArray())
        {
            var owners = pendingOwners[prefabName];
            var replacements = new Queue<ServerPlayer>(owners.Count);
            while (owners.Count > 0)
            {
                ServerPlayer owner = owners.Dequeue();
                if (owner == from)
                    owner = to;
                if (owner != null)
                    replacements.Enqueue(owner);
            }

            if (replacements.Count == 0)
                pendingOwners.Remove(prefabName);
            else
                pendingOwners[prefabName] = replacements;
        }
    }

    public static void ResetSessionState()
    {
        pendingOwners.Clear();
        scaledAllowances.Clear();
        stockRecountPending = false;
        sessionGeneration++;
    }

    public static void AssignPurchasedObject(GameObject purchasedObject)
    {
        var itemBase = purchasedObject?.GetComponent<ItemBase>();
        NetworkedItem.TryGetNetworkedItem(itemBase, out var item);
        if (!NetworkLifecycle.Instance.IsHost() || item?.Item?.InventorySpecs == null)
            return;

        string prefabName = item.Item.InventorySpecs.ItemPrefabName;
        if (!pendingOwners.TryGetValue(prefabName, out var owners) || owners.Count == 0)
            return;

        ServerPlayer buyer = owners.Dequeue();
        if (owners.Count == 0)
            pendingOwners.Remove(prefabName);

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
        CommonShopPacket packet)
    {
        bool isBuyer = NetworkLifecycle.Instance.Client?.PlayerId == packet.BuyerPlayerId;
        if (isBuyer)
        {
            ApplyApprovedBasket(networkedRegister.CashRegister, packet.ItemPrefabNames,
                packet.ItemAmounts, packet.ItemUnitPrices);
            networkedRegister.CompleteApprovedShopPurchase();
            return;
        }

        ApplyStockDelta(packet.ItemPrefabNames, packet.ItemAmounts);
    }

    private static void ApplyApprovedBasket(CashRegisterWithModules register, string[] prefabNames,
        int[] amounts, float[] prices)
    {
        if (prefabNames == null || amounts == null || prices == null ||
            prefabNames.Length != amounts.Length || prefabNames.Length != prices.Length)
            return;

        var approvedByPrefab = prefabNames
            .Select((prefabName, index) => new { prefabName, amount = amounts[index], price = prices[index] })
            .ToDictionary(entry => entry.prefabName, entry => (entry.amount, entry.price));
        foreach (ScanItemCashRegisterModule module in register.registerModules.OfType<ScanItemCashRegisterModule>())
        {
            if (module.sellingItemSpec != null && approvedByPrefab.TryGetValue(
                module.sellingItemSpec.ItemPrefabName, out var approved))
            {
                module.Data.unitsToBuy = approved.amount;
                module.Data.pricePerUnit = approved.price;
            }
            else
            {
                module.Data.unitsToBuy = 0;
            }
        }
        register.OnUnitsToBuyChanged();
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

    public static CommonShopPacket CreateStockSnapshot()
    {
        IReadOnlyList<ShopItemData> items = GlobalShopController.Instance?.shopItemsData ?? [];
        return new CommonShopPacket
        {
            Action = ShopAction.StockSnapshot,
            ItemPrefabNames = items.Select(data => data.item.ItemPrefabName).ToArray(),
            ItemAmounts = items.Select(data => data.purchasedItems).ToArray(),
        };
    }

    public static void ApplyStockSnapshot(string[] prefabNames, int[] purchasedAmounts)
    {
        if (prefabNames == null || purchasedAmounts == null || prefabNames.Length != purchasedAmounts.Length)
            return;

        var purchasedByPrefab = prefabNames
            .Select((prefabName, index) => new { prefabName, amount = purchasedAmounts[index] })
            .Where(entry => !string.IsNullOrEmpty(entry.prefabName))
            .GroupBy(entry => entry.prefabName)
            .ToDictionary(group => group.Key, group => Math.Max(group.Last().amount, 0));
        foreach (ShopItemData data in GlobalShopController.Instance.shopItemsData)
            data.purchasedItems = purchasedByPrefab.TryGetValue(data.item.ItemPrefabName, out int amount)
                ? amount
                : 0;

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
            if (data.allowedToHaveAmount <= 0)
                continue;

            // Only rescale the vanilla allowance or a value previously produced by
            // this method. Preserve positive runtime overrides from the game or
            // another mod just as we preserve the game's zero-valued lockouts.
            if (data.allowedToHaveAmount != data.initialAmount &&
                (!scaledAllowances.TryGetValue(data, out int previous) || data.allowedToHaveAmount != previous))
                continue;

            int scaled = checked(data.initialAmount * maxPlayers);
            data.allowedToHaveAmount = scaled;
            scaledAllowances[data] = scaled;
        }
    }

    public static void RecountExistingShopItems(GlobalShopController controller)
    {
        if (controller == null || !NetworkLifecycle.Instance.IsHost())
            return;

        var players = NetworkLifecycle.Instance.Server.ServerPlayers.ToArray();
        var stored = NetworkedSaveGameManager.Instance.GetSavedPlayerInventories()
            .Where(inventory => !players.Any(player => player.Guid == inventory.Guid &&
                (player.InventoryReconciliationComplete || player.Peer == NetworkLifecycle.Instance.Server.SelfPeer))).ToArray();

        // Until a returning player's complete inventory is reconciled, its saved
        // inventory reserves the stock. Partial live proxies are the same items.
        var partialInventoryItems = players.Where(player => stored.Any(inventory => inventory.Guid == player.Guid))
            .SelectMany(player => player.ReconciledInventoryItems).ToHashSet();
        var networkedItems = NetworkedItem.GetAll().Where(item => item != null && item.NetId != 0).ToArray();
        var ignoredItems = networkedItems.Where(item => item.IsPendingReconciliation || partialInventoryItems.Contains(item))
            .Select(item => item.Item).ToHashSet();
        var physicalItems = SingletonBehaviour<StorageController>.Instance.GetAllStorageItems()
            .Concat(networkedItems.Select(item => item.Item)).Where(item => item != null && !ignoredItems.Contains(item))
            .Distinct().Select(item => item.GetComponent<ShopRestocker>())
            .Where(restocker => restocker != null && restocker.restockOnItemDestroyed)
            .Select(restocker => restocker.GetComponent<InventoryItemSpec>()?.ItemPrefabName);
        var counts = CountStockReservations(physicalItems, stored.SelectMany(inventory => inventory.Items),
            pendingOwners.Select(entry => new KeyValuePair<string, int>(entry.Key, entry.Value.Count)));

        bool changed = false;
        foreach (ShopItemData data in controller.shopItemsData)
        {
            int count = counts.TryGetValue(data.item.ItemPrefabName, out int total) ? total : 0;
            changed |= data.purchasedItems != count;
            data.purchasedItems = count;
        }

        controller.Fire_GlobalShopDataChanged();
        if (changed)
            NetworkLifecycle.Instance.Server.SendShopAction(CreateStockSnapshot());
    }

    internal static Dictionary<string, int> CountStockReservations(IEnumerable<string> physicalItems,
        IEnumerable<PlayerItemSaveData> storedItems, IEnumerable<KeyValuePair<string, int>> pendingPurchases)
    {
        var counts = physicalItems.Concat(storedItems
                .Where(item => item.State?.Value<bool?>(ShopRestocker.RESTOCK_SHOP_KEY) == true)
                .Select(item => item.ItemPrefabName))
            .Where(name => !string.IsNullOrEmpty(name)).GroupBy(name => name)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var purchase in pendingPurchases)
            counts[purchase.Key] = (counts.TryGetValue(purchase.Key, out int count) ? count : 0) + purchase.Value;
        return counts;
    }

    internal static void SuppressStorageTransitionRestock(GameObject item)
    {
        // The saved record retains the native Restock flag. Destroying a proxy
        // while moving it to/from that record is not consumption of the item.
        var restocker = item?.GetComponent<ShopRestocker>();
        if (restocker != null)
            restocker.restockOnItemDestroyed = false;
    }

    internal static void RequestStockRecount()
    {
        if (stockRecountPending || !NetworkLifecycle.Instance.IsHost())
            return;
        stockRecountPending = true;
        CoroutineManager.Instance.StartCoroutine(RecountAfterDestroyedObjects(sessionGeneration));
    }

    private static IEnumerator RecountAfterDestroyedObjects(int generation)
    {
        yield return null;
        if (generation != sessionGeneration)
            yield break;
        stockRecountPending = false;
        if (NetworkLifecycle.Instance.IsHost())
            RecountExistingShopItems(GlobalShopController.Instance);
    }
}
