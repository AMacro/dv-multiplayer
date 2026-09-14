using DV.CashRegister;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;

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

        __instance?.StopAllCoroutines();
        __instance?.textController?.Clear();
        __instance?.SetupListeners(false);

        // Prevent clients from cancelling/returning cash on cash registers when loading the game or leaving the area
        return NetworkLifecycle.Instance.IsHost();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnBuyPressed))]
    private static bool OnBuyPressed(CashRegisterWithModules __instance, out bool __state)
    {
        // Record whether there was anything in the basket so the postfix can tell if the buy succeeded
        __state = NetworkLifecycle.Instance.IsHost() && __instance.TotalUnitsInBasket() > 0f;

        var player = PlayerManager.PlayerTransform.position;
        var reg = __instance.transform.position;
        var sqrMag = (player - reg).sqrMagnitude;
        Multiplayer.LogDebug(() => $"CashRegisterWithModules.OnBuyPressed() player pos: {player} register pos: {reg}, sqrMag: {sqrMag}");
        if (NetworkLifecycle.Instance.IsHost())
        {
            // Record the host player as buyer so spawned items can be stamped with their owner
            GlobalShopControllerPatch.PurchasingPlayerId = NetworkLifecycle.Instance.Client?.PlayerId ?? 0;
            return true;
        }

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.OnBuyPressed({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return false;
        }

        CoroutineManager.Instance.StartCoroutine(netCashRegister.Buy());

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CashRegisterWithModules.OnBuyPressed))]
    private static void OnBuyPressed_Postfix(CashRegisterWithModules __instance, bool __state)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        // Only broadcast if the buy succeeded (a successful Buy resets the basket to zero);
        // broadcasting a failed buy would wrongly reset the clients' baskets
        if (!__state || __instance.TotalUnitsInBasket() > 0f)
            return;

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.OnBuyPressed_Postfix({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return;
        }

        // Send buy action to all clients
        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = netCashRegister.NetId,
            Action = CashRegisterAction.Buy,
            Amount = __instance.DepositedCash
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CashRegisterWithModules.Cancel))]
    private static bool Cancel(CashRegisterWithModules __instance)
    {

        //Multiplayer.LogDebug(()=>$"CashRegisterWithModules.Cancel({__instance.GetObjectPath()})\r\n{Environment.StackTrace}");

        if (NetworkLifecycle.Instance.IsHost())
            return true;

        if (!NetworkedCashRegisterWithModules.TryGet(__instance, out var netCashRegister))
        {
            Multiplayer.LogWarning($"CashRegisterWithModules.Cancel({__instance.GetObjectPath()}) NetworkedCashRegisterWithModules not found!");
            return false;
        }

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

        // Send cancel action to all clients
        NetworkLifecycle.Instance.Server.SendCashRegisterAction(new CommonCashRegisterWithModulesActionPacket
        {
            NetId = netCashRegister.NetId,
            Action = CashRegisterAction.Cancel,
            Amount = __instance.DepositedCash
        });
    }
}
