using DV.CashRegister;
using DV.Interaction;
using DV.InventorySystem;
using DV.Shops;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedCashRegisterWithModules : IdMonoBehaviour<ushort, NetworkedCashRegisterWithModules>
{
    #region Lookup Cache
    private static readonly Dictionary<CashRegisterWithModules, NetworkedCashRegisterWithModules> cashRegisterToNetworkedCashRegister = [];

    public static bool Get(ushort netId, out NetworkedCashRegisterWithModules obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedCashRegisterWithModules> rawObj);
        obj = (NetworkedCashRegisterWithModules)rawObj;
        return b;
    }

    public static bool TryGet(CashRegisterWithModules cashRegister, out NetworkedCashRegisterWithModules networkedCashRegisterWithModules)
    {
        return cashRegisterToNetworkedCashRegister.TryGetValue(cashRegister, out networkedCashRegisterWithModules);
    }

    public static void InitialiseCashRegisters()
    {
        // Find all shop cash registers
        var shopRegisters = GlobalShopController.Instance.globalShopList
            .Select(shop => shop.cashRegister)
            .ToArray();

        //Find all CashRegistersWithModules that are placed on the map
        //sort them by their hierarchy path for consistent ordering
        var registers = CashRegisterBase.allCashRegisters
            .OfType<CashRegisterWithModules>()
            .OrderBy(r => r.transform.position.x)
            .ThenBy(r => r.transform.position.y)
            .ThenBy(r => r.transform.position.z)
            .ToArray();

        //Multiplayer.LogDebug(() => $"InitialiseCashRegisters() Found: {registers?.Length}");

        foreach (var register in registers)
        {
            var netRegister = register.GetOrAddComponent<NetworkedCashRegisterWithModules>();
            netRegister.CashRegister = register;
            netRegister.IsShopRegister = shopRegisters.Contains(register);

            if (netRegister.NetId == 0)
                netRegister.Awake();

            cashRegisterToNetworkedCashRegister[register] = netRegister;

            //Multiplayer.LogDebug(() => $"InitialiseCashRegisters() Register: {register?.GetObjectPath()}, netId: {netRegister.NetId}");
        }
    }

    #endregion

    protected override bool IsIdServerAuthoritative => false;

    #region Server Variables
    bool processingAction = false;
    #endregion

    #region Client Variables
    bool isBuying;
    bool isCancelling;
    public bool IsShopRegister { get; set; } = false;
    #endregion

    #region Common Variables
    CashRegisterWithModules CashRegister; 
    #endregion

    #region Unity

    protected override void Awake()
    {
        //Multiplayer.LogDebug(()=>$"CashRegisterWithModules.Awake() {transform.GetObjectPath()}, {transform.position}, netId: {NetId}");

        if (NetId == 0)
            base.Awake();
    }

    protected override void OnDestroy()
    {
        cashRegisterToNetworkedCashRegister.Remove(CashRegister);
        base.OnDestroy();
    }
    #endregion

    #region Server
    public void Server_ProcessCashRegisterAction(ServerPlayer player, CommonCashRegisterWithModulesActionPacket packet)
    {
        float sqrDistance = (player.WorldPosition - transform.position).sqrMagnitude;
        bool success = false;
        CashRegisterAction response = CashRegisterAction.RejectGeneric;

        NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({player.Username}, {packet.Action}, {packet.Amount})");

        if (sqrDistance > GrabberRaycasterDV.FPS_INTERACTION_RANGE_SQR * 2) //need to find the real distance, likely related to player capsual size
        {
            NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({player.Username}, {packet.Action}, {packet.Amount}) {CashRegister.GetObjectPath()}. Player too far! Player pos: {player.WorldPosition}, register pos: {transform.position}, sqrMag: {sqrDistance}");
            return;
        }

        processingAction = true;
        switch (packet.Action)
        {
            case CashRegisterAction.Cancel:
                    CashRegister?.Cancel();
                    success = true;

                break;

            case CashRegisterAction.Buy:

                Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({packet.Action}) Player Money: {Inventory.Instance.PlayerMoney}, TotalCost: {CashRegister.GetTotalCost()}, TotalUnitsInBasket: {CashRegister.TotalUnitsInBasket()}");

                if (CashRegister.TotalUnitsInBasket() <= 0)
                {
                    response = CashRegisterAction.RejectedNoItems;
                }
                else if (Inventory.Instance.PlayerMoney <= CashRegister.GetTotalCost())
                {
                    response = CashRegisterAction.RejectFunds;
                }
                else
                {
                   success = CashRegister?.Buy() ?? false;
                }

                Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({packet.Action}, {packet.Amount}) Response: {response}, Buy success: {success}, Player Money: {Inventory.Instance.PlayerMoney}, TotalCost: {CashRegister.GetTotalCost()}, TotalUnitsInBasket: {CashRegister.TotalUnitsInBasket()}");

                break;
                
            case CashRegisterAction.SetFunds:
                double spend = 0;

                NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({player.Username}, {packet.Action}, {packet.Amount}) Wallet: {Inventory.Instance.PlayerMoney}");
                if (packet.Amount > 0)
                {
                    if (Inventory.Instance.PlayerMoney >= packet.Amount)
                        spend = packet.Amount;
                    else
                        spend = Inventory.Instance.PlayerMoney;

                    success = Inventory.Instance.RemoveMoney(spend);

                    if(success && player.PlayerId != NetworkLifecycle.Instance.Server.SelfId)
                        CashRegister?.AddCash(spend);
                }
                else
                {
                    NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({player.Username}, {packet.Action}, {packet.Amount}) amount negative!");
                }
                break;
        }

        if (success)
            NetworkLifecycle.Instance.Server.SendCashRegisterAction(packet);
        else
            NetworkLifecycle.Instance.Server.SendCashRegisterAction
                (
                    new CommonCashRegisterWithModulesActionPacket
                    {
                        NetId = NetId,
                        Action = response,
                        Amount = CashRegister.DepositedCash
                    },
                    player.Peer
                );

        processingAction = false;
    }

    #endregion

    #region Client

    public void Client_ProcessCashRegisterAction(CashRegisterAction action, double amount)
    {
        NetworkLifecycle.Instance.Client?.LogDebug(() => $"NetworkedCashRegisterWithModules.Client_ProcessCashRegisterAction({action}, {amount}) isBuying: {isBuying}, isCancelling: {isCancelling}");
        switch (action)
        {
            case CashRegisterAction.Cancel:

                isCancelling = false;
                isBuying = false;

                foreach (var module in CashRegister.registerModules)
                    module.ResetData();

                CashRegister.OnUnitsToBuyChanged();

                if (CashRegister.DepositedCash > 0)
                {
                    CashRegister?.cancelAudio?.Play(CashRegister.transform.position, 1f, 1f, 0f, 1f, 500f, default, null, CashRegister.transform, false, 0f, null);
                    CashRegister.DepositedCash = 0;
                    CashRegister?.OnDepositedUpdated();
                }

                //CashRegister?.Cancel();

                break;

            case CashRegisterAction.Buy:

                isCancelling = false;
                isBuying = false;

                CashRegister?.buyAudio?.Play(CashRegister.transform.position, 1f, 1f, 0f, 1f, 500f, default, null, CashRegister.transform, false, 0f, null);

                foreach(var module in CashRegister.registerModules)
                    module.ResetData();

                CashRegister?.OnUnitsToBuyChanged();

                CashRegister.DepositedCash = 0;
                CashRegister?.OnDepositedUpdated();

                CashRegister.IsProcessingTransaction = false;

                break;

            case CashRegisterAction.SetFunds:
                CashRegister?.SetCash(amount);

                break;

            case CashRegisterAction.RejectGeneric:
                isBuying = false;
                isCancelling = false;
               
                break;

            case CashRegisterAction.RejectFunds:
                isBuying = false;
                isCancelling = false;

                CashRegister?.notEnoughMoneyAudio?.Play(CashRegister.transform.position, 1f, 1f, 0f, 1f, 500f, default, null, CashRegister.transform, false, 0f, null);

                break;

            case CashRegisterAction.RejectedNoItems:
                isBuying = false;
                isCancelling = false;

                foreach (var module in CashRegister.registerModules)
                    module.ResetData();

                CashRegister?.OnUnitsToBuyChanged();

                CashRegister.DepositedCash = 0;
                CashRegister?.OnDepositedUpdated();

                CashRegister?.buyAudio?.Play(CashRegister.transform.position, 1f, 1f, 0f, 1f, 500f, default, null, CashRegister.transform, false, 0f, null);
                break;
        }
    }

    public IEnumerator Buy()
    {
        if (isBuying || isCancelling || NetworkLifecycle.Instance.IsProcessingPacket)
            yield break;

        DisableInteraction();
        CashRegister.IsProcessingTransaction = true;

        NetworkLifecycle.Instance.Client.SendCashRegisterAction(NetId, CashRegisterAction.Buy);

        isBuying = true;
        float timeOut = Time.time + NetworkLifecycle.Instance.Client.RPC_Timeout;

        yield return new WaitUntil(() => Time.time >= timeOut || isBuying == false);

        isBuying = false;

        CashRegister.IsProcessingTransaction = false;
        EnableInteraction();
    }

    public IEnumerator Cancel()
    {
        if (isBuying || isCancelling || NetworkLifecycle.Instance.IsProcessingPacket)
            yield break;

        DisableInteraction();

        NetworkLifecycle.Instance.Client.SendCashRegisterAction(NetId, CashRegisterAction.Cancel);
        isCancelling = true;
        float timeOut = Time.time + NetworkLifecycle.Instance.Client.RPC_Timeout;

        yield return new WaitUntil(() => Time.time >= timeOut || isCancelling == false);

        isCancelling = false;

        EnableInteraction();
    }

    public void SetCash(double amount)
    {
        if (isBuying || isCancelling || processingAction || NetworkLifecycle.Instance.IsProcessingPacket)
            return;

        NetworkLifecycle.Instance.Client.SendCashRegisterAction(NetId, CashRegisterAction.SetFunds, amount);
    }

    private void DisableInteraction()
    {
        CashRegister.buyButton.InteractionAllowed = false;
        CashRegister.cancelButton.InteractionAllowed = false;
    }

    private void EnableInteraction()
    {
        CashRegister.buyButton.InteractionAllowed = true;
        CashRegister.cancelButton.InteractionAllowed = true;
    }

    #endregion

    #region Common

    #endregion
}
