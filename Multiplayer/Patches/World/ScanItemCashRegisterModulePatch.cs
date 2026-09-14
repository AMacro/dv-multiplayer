using DV.Shops;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(ScanItemCashRegisterModule))]
public static class ScanItemCashRegisterModulePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ScanItemCashRegisterModule.AddItemsToBuy))]
    private static bool AddItemsToBuy_Prefix(ScanItemCashRegisterModule __instance, ref bool __result)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        if (!TryGetRegisterAndIndex(__instance, out var netCashRegister, out byte moduleIndex))
        {
            Multiplayer.LogWarning($"ScanItemCashRegisterModule.AddItemsToBuy() NetworkedCashRegisterWithModules not found for module {__instance.name}");
            __result = false;
            return false;
        }

        // Validate against the local (server-synced) stock so the scanner gives immediate feedback;
        // the server is authoritative and echoes the accepted basket state back via SetBasket
        ShopItemData shopItemData = SingletonBehaviour<GlobalShopController>.Instance.GetShopItemData(__instance.sellingItemSpec);
        int itemsInStock = shopItemData?.ItemsInStock ?? 0;
        __result = __instance.sellingItemSpec != null && itemsInStock > 0 && __instance.Data.unitsToBuy < (float)itemsInStock;

        if (__result)
            NetworkLifecycle.Instance.Client.SendCashRegisterAction(netCashRegister.NetId, CashRegisterAction.ScanItem, 0, moduleIndex);

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ScanItemCashRegisterModule.AddItemsToBuy))]
    private static void AddItemsToBuy_Postfix(ScanItemCashRegisterModule __instance, bool __result)
    {
        // Covers both the host player scanning locally and the server processing a client's
        // ScanItem request - either way the new basket state is broadcast to all clients
        if (!NetworkLifecycle.Instance.IsHost() || !__result)
            return;

        if (!TryGetRegisterAndIndex(__instance, out var netCashRegister, out byte moduleIndex))
        {
            Multiplayer.LogWarning($"ScanItemCashRegisterModule.AddItemsToBuy() NetworkedCashRegisterWithModules not found for module {__instance.name}");
            return;
        }

        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = netCashRegister.NetId,
            Action = CashRegisterAction.SetBasket,
            Amount = __instance.Data.unitsToBuy,
            ModuleIndex = moduleIndex
        });
    }

    private static bool TryGetRegisterAndIndex(ScanItemCashRegisterModule module, out NetworkedCashRegisterWithModules netCashRegister, out byte moduleIndex)
    {
        netCashRegister = null;
        moduleIndex = 0;

        Shop shop = module.GetComponentInParent<Shop>();
        if (shop == null || shop.cashRegister == null)
            return false;

        return NetworkedCashRegisterWithModules.TryGet(shop.cashRegister, out netCashRegister)
            && netCashRegister.TryGetModuleIndex(module, out moduleIndex);
    }
}
