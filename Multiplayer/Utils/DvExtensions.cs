using System;
using DV.UI;
using DV.UIFramework;
using DV.Localization;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;



namespace Multiplayer.Utils;

public static class DvExtensions
{
    #region TrainCar

    public static ushort GetNetId(this TrainCar car)
    {
        ushort netId = 0;

        if (car != null && car.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            netId = networkedTrainCar.NetId;
/*
        if (netId == 0)
            Multiplayer.LogWarning($"NetId for {car.carLivery.id} ({car.ID}) isn't initialized!\r\n" + (Multiplayer.Settings.DebugLogging ? new System.Diagnostics.StackTrace() : ""));*/
            //throw new InvalidOperationException($"NetId for {car.carLivery.id} ({car.ID}) isn't initialized!");
        return netId;
    }

    //public static NetworkedTrainCar Networked(this TrainCar trainCar)
    //{
    //    return NetworkedTrainCar.GetFromTrainCar(trainCar);
    //}

    public static bool TryNetworked(this TrainCar trainCar, out NetworkedTrainCar networkedTrainCar)
    {
        return NetworkedTrainCar.TryGetFromTrainCar(trainCar, out networkedTrainCar);
    }

    #endregion

    #region RailTrack

    public static NetworkedRailTrack Networked(this RailTrack railTrack)
    {
        return NetworkedRailTrack.GetFromRailTrack(railTrack);
    }

    #endregion

    #region UI
    public static GameObject UpdateButton(this GameObject pane, string oldButtonName, string newButtonName, string localeKey, string toolTipKey, Sprite icon)
    {
        // Find and rename the button
        GameObject button = pane.FindChildByName(oldButtonName);
        button.name = newButtonName;

        // Update localization and tooltip
        if (button.GetComponentInChildren<Localize>() != null)
        {
            button.GetComponentInChildren<Localize>().key = localeKey;
            foreach(var child in button.GetComponentsInChildren<I2.Loc.Localize>())
            {
                GameObject.Destroy(child);
            }
            ResetTooltip(button);
            button.GetComponentInChildren<Localize>().UpdateLocalization();
        }else if(button.GetComponentInChildren<UIElementTooltip>() != null)
        {
            button.GetComponentInChildren<UIElementTooltip>().enabledKey = localeKey + "__tooltip";
            button.GetComponentInChildren<UIElementTooltip>().disabledKey = localeKey + "__tooltip_disabled";
        }

        // Set the button icon if provided
        if (icon != null)
        {
            SetButtonIcon(button, icon);
        }

        // Enable button interaction
        button.GetComponentInChildren<ButtonDV>().ToggleInteractable(true);

        return button;
    }

    private static void SetButtonIcon(this GameObject button, Sprite icon)
    {
        // Find and set the icon for the button
        GameObject goIcon = button.FindChildByName("[icon]");
        if (goIcon == null)
        {
            Multiplayer.LogError("Failed to find icon!");
            return;
        }

        goIcon.GetComponent<Image>().sprite = icon;
    }

    public static void ResetTooltip(this GameObject button)
    {
        // Reset the tooltip keys for the button
        UIElementTooltip tooltip = button.GetComponent<UIElementTooltip>();
        tooltip.disabledKey = null;
        tooltip.enabledKey = null;

    }

    #endregion

    #region Utils

    public static float AnyPlayerSqrMag(this GameObject item)
    {
        return AnyPlayerSqrMag(item.transform.position);
    }

    public static float AnyPlayerSqrMag(this Vector3 anchor)
    {
        float result = float.MaxValue;
        //string origin = new StackTrace().GetFrame(1).GetMethod().Name;

        //Loop through all of the players and return the one thats closest to the anchor
        foreach (ServerPlayer serverPlayer in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            float sqDist = (serverPlayer.WorldPosition - anchor).sqrMagnitude;
            /*
            if(origin == "UnusedTrainCarDeleter.AreDeleteConditionsFulfilled_Patch0")
                Multiplayer.LogDebug(() => $"AnyPlayerSqrMag(): car: {UnusedTrainCarDeleterPatch.current?.ID}, player: {serverPlayer.Username}, result: {sqDist}");
            */
            if (sqDist < result)
                result = sqDist;
        }

        /*
        if (origin == "UnusedTrainCarDeleter.AreDeleteConditionsFulfilled_Patch0")
            Multiplayer.LogDebug(() => $"AnyPlayerSqrMag(): player: result: {result}");
        */
        return result;
    }

    public static Vector3 GetWorldAbsolutePosition(this GameObject go)
    {
        return go.transform.GetWorldAbsolutePosition();
    }
    public static Vector3 GetWorldAbsolutePosition(this Transform transform)
    {
        return transform.position - WorldMover.currentMove;
    }
    #endregion
}
