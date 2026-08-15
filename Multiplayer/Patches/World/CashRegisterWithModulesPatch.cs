using DV.CashRegister;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;
using System.Linq;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CashRegisterWithModules))]
public class CashRegisterWithModulesPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnDisable))]
    private static bool OnDisable(CashRegisterWithModules __instance)
    {
        //Multiplayer.LogDebug(() => $"CashRegisterWithModules.OnDisable({__instance.GetObjectPath()})");
        if (__instance == null)
            return true;

        if (ShopPurchaseCoordinator.IsShopRegister(__instance))
            return true;

        __instance?.StopAllCoroutines();
        __instance?.textController?.Clear();
        __instance?.SetupListeners(false);

        // Prevent clients from cancelling/returning cash on cash registers when loading the game or leaving the area
        return NetworkLifecycle.Instance.IsHost();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnBuyPressed))]
    private static bool OnBuyPressed(CashRegisterWithModules __instance)
    {
        var player = PlayerManager.PlayerTransform.position;
        var reg = __instance.transform.position;
        var sqrMag = (player - reg).sqrMagnitude;
        Multiplayer.LogDebug(() => $"CashRegisterWithModules.OnBuyPressed() player pos: {player} register pos: {reg}, sqrMag: {sqrMag}");
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.OnBuyPressed({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return false;
        }

        if (netCashRegister.IsShopRegister)
        {
            CoroutineManager.Instance.StartCoroutine(netCashRegister.BuyShop());
            return false;
        }

        CoroutineManager.Instance.StartCoroutine(netCashRegister.Buy());

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnBuyPressed))]
    private static void OnBuyPressed_Postfix(CashRegisterWithModules __instance)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.OnBuyPressed_Postfix({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return;
        }

        if (netCashRegister.IsShopRegister)
            return;

        // Send buy action to all clients
        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = netCashRegister.NetId,
            Action = CashRegisterAction.Buy,
            Amount = __instance.DepositedCash
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Buy))]
    private static void Buy_Prefix(CashRegisterWithModules __instance, out ShopPurchase[] __state)
    {
        __state = NetworkLifecycle.Instance.IsHost() &&
            NetworkedCashRegisterWithModules.TryGet(__instance, out var networkedRegister) &&
            networkedRegister.IsShopRegister
                ? ShopPurchaseCoordinator.Capture(__instance)
                : [];
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Buy))]
    private static void Buy_Postfix(CashRegisterWithModules __instance, bool __result, ShopPurchase[] __state)
    {
        if (!__result || __state == null || __state.Length == 0 || !NetworkLifecycle.Instance.IsHost() ||
            !NetworkedCashRegisterWithModules.TryGet(__instance, out var networkedRegister) ||
            !networkedRegister.IsShopRegister ||
            !NetworkLifecycle.Instance.Server.TryGetServerPlayer(NetworkLifecycle.Instance.Server.SelfId, out var buyer))
            return;

        ShopPurchaseCoordinator.QueueOwnership(__state, buyer);
        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = networkedRegister.NetId,
            Action = CashRegisterAction.Approve,
            BuyerPlayerId = buyer.PlayerId,
            ItemPrefabNames = __state.Select(item => item.PrefabName).ToArray(),
            ItemAmounts = __state.Select(item => item.Amount).ToArray(),
            ItemUnitPrices = __state.Select(item => item.UnitPrice).ToArray()
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Cancel))]
    private static bool Cancel(CashRegisterWithModules __instance)
    {

        //Multiplayer.LogDebug(()=>$"CashRegisterWithModules.Cancel({__instance.GetObjectPath()})\r\n{Environment.StackTrace}");

        if (NetworkLifecycle.Instance?.IsHost() != false)
            return true;

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.Cancel({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return false;
        }

        if (netCashRegister.IsShopRegister)
            return true;

        CoroutineManager.Instance.StartCoroutine(netCashRegister.Cancel());

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Cancel))]
    private static void Cancel_Postfix(CashRegisterWithModules __instance)
    {
        //Multiplayer.LogWarning($"CashRegisterWithModules.Cancel_Postfix({__instance.GetObjectPath()})");
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.Cancel_Postfix({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return;
        }

        if (netCashRegister.IsShopRegister)
            return;

        // Send cancel action to all clients
        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = netCashRegister.NetId,
            Action = CashRegisterAction.Cancel,
            Amount = __instance.DepositedCash
        });
    }
}
