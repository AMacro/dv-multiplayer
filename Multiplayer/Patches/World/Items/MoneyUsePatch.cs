using DV.CabControls;
using DV.CashRegister;
using DV.Interaction;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(MoneyUse))]
public static class MoneyUsePatch
{
    // The wallet is shared and server-authoritative, so a client stashing a banknote must ask
    // the host to credit it. The host destroys the item, which replicates back as a normal
    // item update. Cash register payments already route through the register packets.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MoneyUse.HandleUse))]
    private static bool HandleUse(MoneyUse __instance, ItemUseTarget target, ref bool __result)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        // Paying a cash register is handled by the cash register sync
        if (target.GetComponent<CashRegisterBase>() != null)
            return true;

        // Only intercept stashing a destroy-on-use money item (banknotes/coins) into the wallet
        if (!target.TryGetComponent<IMoney>(out var targetMoney) || targetMoney.ShouldDestroyOnUse)
            return true;

        if (!__instance.TryGetComponent<IMoney>(out var heldMoney) || !heldMoney.ShouldDestroyOnUse)
            return true;

        if (!__instance.TryGetComponent<ItemBase>(out var item) || !NetworkedItem.TryGetNetworkedItem(item, out NetworkedItem netItem) || netItem.NetId == 0)
        {
            Multiplayer.LogWarning($"MoneyUse.HandleUse() Money item is not networked; ignoring stash request");
            __result = false;
            return false;
        }

        NetworkLifecycle.Instance.Client.SendMoneyStash(netItem.NetId);

        __result = true;
        return false;
    }
}
