using DV.Localization;
using DV.UI;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Patches.PauseMenu;



[HarmonyPatch(typeof(PauseMenuController))]
public static class PauseMenuController_Patch
{
    private static readonly PopupLocalizationKeys popupQuitLocalizationKeys = new PopupLocalizationKeys
    {
        positiveKey = "yes",
        negativeKey = "no",
        labelKey = Locale.PAUSE_MENU_QUIT_KEY
    };
    private static readonly PopupLocalizationKeys popupDisconnectLocalizationKeys = new PopupLocalizationKeys
    {
        positiveKey = "yes",
        negativeKey = "no",
        labelKey = Locale.PAUSE_MENU_DISCONNECT_KEY
    };


    [HarmonyPatch(nameof(PauseMenuController.Start))]
    [HarmonyPostfix]
    private static void Start(PauseMenuController __instance)
    {
        if(NetworkLifecycle.Instance.IsHost())
            return;

        __instance.loadSaveButton.gameObject.SetActive(false);
        __instance.tutorialsButton.gameObject.SetActive(false);
    }

    [HarmonyPatch(nameof(PauseMenuController.OnExitLevelClicked))]
    [HarmonyPrefix]
    private static bool OnExitLevelClicked(PauseMenuController __instance)
    {
        if(NetworkLifecycle.Instance.IsHost())
            return true;


        if (!__instance.popupManager.CanShowPopup())
        {
            Multiplayer.LogWarning("PauseMenuController.OnExitLevelClicked() PopupManager can't show popups at this moment");
            return false;
        }
        Popup popupPrefab = __instance.yesNoPopupPrefab;
        PopupLocalizationKeys locKeys = popupDisconnectLocalizationKeys;

        __instance.popupManager.ShowPopup(popupPrefab, locKeys).Closed += (PopupResult result) =>
            {
                //Negative = 'No', so we're aborting the disconnect
                if (result.closedBy == PopupClosedByAction.Negative)
                    return;

                //Negative = 'No', so we're aborting the disconnect
                if (result.closedBy == PopupClosedByAction.Negative)
                    return;

                FieldInfo eventField = __instance.GetType().GetField("ExitLevelRequested", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (eventField != null)
                {
                    Delegate eventDelegate = (Delegate)eventField.GetValue(__instance);
                    if (eventDelegate != null)
                        eventDelegate.DynamicInvoke();
                }
            };

        return false;
    }

    [HarmonyPatch("OnQuitClicked")]
    [HarmonyPrefix]
    private static bool OnQuitClicked(PauseMenuController __instance)
    {
        if(NetworkLifecycle.Instance.IsHost())
            return true;


        if (!__instance.popupManager.CanShowPopup())
        {
            Multiplayer.LogWarning("PauseMenuController.OnQuitClicked() PopupManager can't show popups at this moment");
            return false;
        }
        Popup popupPrefab = __instance.yesNoPopupPrefab;
        PopupLocalizationKeys locKeys = popupDisconnectLocalizationKeys;

        __instance.popupManager.ShowPopup(popupPrefab, locKeys).Closed += (PopupResult result) =>
            {
                //Negative = 'No', so we're aborting the disconnect
                if (result.closedBy == PopupClosedByAction.Negative)
                    return;

                FieldInfo eventField = __instance.GetType().GetField("QuitGameRequested", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (eventField != null)
                {
                    Delegate eventDelegate = (Delegate)eventField.GetValue(__instance);
                    if (eventDelegate != null)
                        eventDelegate.DynamicInvoke();
                }
            };

        return false;
    }


}
