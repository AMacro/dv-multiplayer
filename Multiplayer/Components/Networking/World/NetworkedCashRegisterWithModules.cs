using DV.CashRegister;
using DV.Interaction;
using DV.InventorySystem;
using DV.Shops;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Managers.Server;
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
    CullingManager _cullingManager;
    #endregion

    #region Client Variables
    public bool IsBusy => isBuying || isCancelling || isAddingCash || processingAction;
    bool isBuying;
    bool isCancelling;
    bool isAddingCash;
    public bool IsShopRegister { get; set; } = false;

    double pendingCashToAdd = 0;
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

        if (_cullingManager != null)
            _cullingManager.PlayerEnteredActivationRegion -= CullingManager_PlayerEnteredActivationRegion;

        base.OnDestroy();
    }
    #endregion

    #region Server

    public void Server_InitCashRegister(CullingManager cullingManager)
    {
        if (!NetworkLifecycle.Instance.IsHost() || cullingManager == null)
            return;

        _cullingManager = cullingManager;

        if (_cullingManager != null)
            _cullingManager.PlayerEnteredActivationRegion += CullingManager_PlayerEnteredActivationRegion;
    }

    private void CullingManager_PlayerEnteredActivationRegion(ServerPlayer serverPlayer)
    {
        if (CashRegister.DepositedCash > 0f)
        {
            NetworkLifecycle.Instance.Server.SendCashRegisterAction
                (
                    new CommonCashRegisterWithModulesActionPacket
                    {
                        NetId = NetId,
                        Action = CashRegisterAction.SetFunds,
                        Amount = CashRegister.DepositedCash
                    },
                    [serverPlayer]
                );
        }
    }

    public void Server_ProcessCashRegisterAction(ServerPlayer player, CommonCashRegisterWithModulesActionPacket packet)
    {
        bool success = false;
        CashRegisterAction response = CashRegisterAction.RejectGeneric;

        NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({player.Username}, {packet.Action}, {packet.Amount})");
        if (transform.PlayerCanReach(player, 1))
        {
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
                    else if (CashRegister.DepositedCash < CashRegister.GetTotalCost())
                    {
                        response = CashRegisterAction.RejectFunds;
                    }
                    else
                    {
                        success = CashRegister?.Buy() ?? false;
                    }

                    Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({packet.Action}, {packet.Amount}) Response: {response}, Buy success: {success}, Player Money: {Inventory.Instance.PlayerMoney}, TotalCost: {CashRegister.GetTotalCost()}, TotalUnitsInBasket: {CashRegister.TotalUnitsInBasket()}");

                    break;

                case CashRegisterAction.AddCash:

                    double remainingCost = CashRegister.GetRemainingCost();

                    if (remainingCost <= 0)
                    {
                        Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({packet.Action}) No remaining cost to add cash for.");
                        processingAction = false;
                        // No action needed, no response required
                        return;
                    }
                    else if (CashRegister.TotalUnitsInBasket() <= 0)
                    {
                        Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({packet.Action}) No items in basket to add cash for.");
                        response = CashRegisterAction.RejectedNoItems;
                        success = false;
                    }
                    else
                    {
                        double amountToAdd = Math.Min(remainingCost, Inventory.Instance.PlayerMoney);

                        Inventory.Instance.RemoveMoney(amountToAdd);
                        CashRegister.SetCash(CashRegister.DepositedCash + amountToAdd);

                        NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({packet.Action}) Added cash: {amountToAdd}, New DepositedCash: {CashRegister.DepositedCash}, Player Money: {Inventory.Instance.PlayerMoney}");
                        packet.Action = CashRegisterAction.SetFunds;
                        packet.Amount = CashRegister.DepositedCash;
                        success = true;
                    }

                    break;

                case CashRegisterAction.SetFunds:
                    //NetworkLifecycle.Instance.Server?.LogDebug(() => $"NetworkedCashRegisterWithModules.Server_ProcessAction({player.Username}, {packet.Action}, {packet.Amount}) Wallet: {Inventory.Instance.PlayerMoney}");
                    break;
            }
        }
        else
        {
            NetworkLifecycle.Instance.Server?.LogDebug(() => $"Player \"{player.Username}\" tried to interact with Cash Register , but they are too far away");
        }

        if (success)
            NetworkLifecycle.Instance.Server.SendCashRegisterAction(packet, _cullingManager?.ActivePlayers?.ToArray());
        else
            NetworkLifecycle.Instance.Server.SendCashRegisterAction
                (
                    new CommonCashRegisterWithModulesActionPacket
                    {
                        NetId = NetId,
                        Action = response,
                        Amount = CashRegister.DepositedCash
                    },
                    [player]
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

                foreach (var module in CashRegister.registerModules)
                    module.ResetData();

                CashRegister?.OnUnitsToBuyChanged();

                CashRegister.DepositedCash = 0;
                CashRegister?.OnDepositedUpdated();

                CashRegister.IsProcessingTransaction = false;

                break;

            case CashRegisterAction.AddCash:
                break;

            case CashRegisterAction.SetFunds:
                Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.Client_ProcessCashRegisterAction({action}, {amount}) Setting deposited cash.");
                CashRegister?.SetCash(amount);

                break;

            case CashRegisterAction.RejectGeneric:
                isBuying = false;
                isCancelling = false;

                //if (isAddingCash)
                //{
                    //Inventory.Instance.AddMoney(pendingCashToAdd);
                    pendingCashToAdd = 0;
                //}

                isAddingCash = false;

                break;

            case CashRegisterAction.RejectFunds:
                isBuying = false;
                isCancelling = false;

                //if (isAddingCash)
                //{
                    //Inventory.Instance.AddMoney(pendingCashToAdd);
                    pendingCashToAdd = 0;
                //}

                isAddingCash = false;

                CashRegister?.notEnoughMoneyAudio?.Play(CashRegister.transform.position, 1f, 1f, 0f, 1f, 500f, default, null, CashRegister.transform, false, 0f, null);

                break;

            case CashRegisterAction.RejectedNoItems:
                isBuying = false;
                isCancelling = false;

                //if (isAddingCash)
                //{
                    //Inventory.Instance.AddMoney(pendingCashToAdd);
                    pendingCashToAdd = 0;
                //}

                isAddingCash = false;

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
        if (isBuying || isCancelling || isAddingCash || NetworkLifecycle.Instance.IsProcessingPacket)
            yield break;

        DisableInteraction();

        NetworkLifecycle.Instance.Client.SendCashRegisterAction(NetId, CashRegisterAction.Cancel);

        isCancelling = true;
        float timeOut = Time.time + NetworkLifecycle.Instance.Client.RPC_Timeout;

        yield return new WaitUntil(() => Time.time >= timeOut || isCancelling == false);

        isCancelling = false;

        EnableInteraction();
    }

    public IEnumerator AddCash(double amount)
    {
        if (isBuying || isCancelling || isAddingCash || processingAction || NetworkLifecycle.Instance.IsProcessingPacket)
            yield break;

        DisableInteraction();

        Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.AddCash({amount}) Sending AddCash action.");
        NetworkLifecycle.Instance.Client.SendCashRegisterAction(NetId, CashRegisterAction.AddCash);

        isAddingCash = true;
        pendingCashToAdd = amount;

        float timeOut = Time.time + NetworkLifecycle.Instance.Client.RPC_Timeout;

        Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.AddCash({amount}) Waiting");
        yield return new WaitUntil(() => Time.time >= timeOut || isAddingCash == false);

        Multiplayer.LogDebug(() => $"NetworkedCashRegisterWithModules.AddCash({amount}) Wait complete, time-out: {Time.time >= timeOut}, isAddingCash: {isAddingCash}");

        pendingCashToAdd = 0;
        isAddingCash = false;

        EnableInteraction();
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
