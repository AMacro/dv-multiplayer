using DV.Interaction;
using DV.KeyboardInput;
using DV.Localization;
using DV.UI;
using DV.UIFramework;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;



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
        tooltip.initialized = false;
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

            if (sqDist < result)
                result = sqDist;
        }

        return result;
    }

    public static bool PlayerCanReach(this GameObject item, ServerPlayer player, float extraRange = 0f)
    {
        return PlayerCanReach (item.transform, player, extraRange);
    }

    public static bool PlayerCanReach(this Transform item, ServerPlayer player, float extraRange = 0f)
    {
        float reachRange = AKeyboardInput.XZ_SQR_REACH_RANGE + GrabberRaycasterDV.FPS_INTERACTION_RANGE_SQR + (extraRange * extraRange);

        var delta = player.WorldPosition - item.transform.position;

        if (Mathf.Abs(delta.y) > AKeyboardInput.Y_REACH_RANGE)
            return false;

        delta.y = 0f;

        float sqrMag = (delta).sqrMagnitude;

        return sqrMag <= reachRange;
    }

    public static Vector3 GetWorldAbsolutePosition(this GameObject go)
    {
        return go.transform.GetWorldAbsolutePosition();
    }

    public static Vector3 GetWorldAbsolutePosition(this Transform transform)
    {
        return transform.position - WorldMover.currentMove;
    }

    public static bool AllowPause()
    {
        return NetworkLifecycle.Instance.IsHost() &&
            (NetworkLifecycle.Instance.Server.IsSinglePlayer ||
            (NetworkLifecycle.Instance.Server.PlayerCount == 1 && NetworkLifecycle.Instance.IsClientRunning));
    }
    #endregion

    #region GenericExtensions

    public static int Replace<T>(this IList<T> source, T oldValue, T newValue)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        var index = source.IndexOf(oldValue);
        if (index != -1)
            source[index] = newValue;
        return index;
    }

    public static void ReplaceAll<T>(this IList<T> source, T oldValue, T newValue)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        int index = -1;
        do
        {
            index = source.IndexOf(oldValue);
            if (index != -1)
                source[index] = newValue;
        } while (index != -1);
    }


    public static IEnumerable<T> Replace<T>(this IEnumerable<T> source, T oldValue, T newValue)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return source.Select(x => EqualityComparer<T>.Default.Equals(x, oldValue) ? newValue : x);
    }

    #endregion
}
