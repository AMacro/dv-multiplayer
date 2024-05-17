using DV.Localization;
using DV.UI;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Utils;
using TMPro;
using UnityEngine;

namespace Multiplayer.Patches.MainMenu;

[HarmonyPatch(typeof(RightPaneController), "OnEnable")]
public static class RightPaneController_OnEnable_Patch
{
    private static void Prefix(RightPaneController __instance)
    {
        if (__instance.HasChildWithName("PaneRight Multiplayer"))
            return;
        GameObject launcher = __instance.FindChildByName("PaneRight Launcher");
        if (launcher == null)
        {
            Multiplayer.LogError("Failed to find Launcher pane!");
            return;
        }

        launcher.SetActive(false);
        GameObject multiplayerPane = Object.Instantiate(launcher, launcher.transform.parent);
        launcher.SetActive(true);

        multiplayerPane.name = "PaneRight Multiplayer";
        __instance.menuController.controlledMenus.Add(multiplayerPane.GetComponent<UIMenu>());
        MainMenuController_Awake_Patch.MultiplayerButton.GetComponent<UIMenuRequester>().requestedMenuIndex = __instance.menuController.controlledMenus.Count - 1;

        Object.Destroy(multiplayerPane.GetComponent<LauncherController>());
        Object.Destroy(multiplayerPane.FindChildByName("Thumb Background"));
        Object.Destroy(multiplayerPane.FindChildByName("Thumbnail"));
        Object.Destroy(multiplayerPane.FindChildByName("Savegame Details Background"));
        Object.Destroy(multiplayerPane.FindChildByName("ButtonTextIcon Run"));

        GameObject titleObj = multiplayerPane.FindChildByName("Title");
        if (titleObj == null)
        {
            Multiplayer.LogError("Failed to find title object!");
            return;
        }

        GameObject content =  multiplayerPane.FindChildByName("text header");
        content.GetComponentInChildren<TextMeshProUGUI>().text = "Server browser not yet implemented.";

        titleObj.GetComponentInChildren<Localize>().key = Locale.SERVER_BROWSER__TITLE_KEY;
        Object.Destroy(titleObj.GetComponentInChildren<I2.Loc.Localize>());

        multiplayerPane.AddComponent<MultiplayerPane>();

        MainMenuThingsAndStuff.Create(manager =>
        {
            PopupManager popupManager = null;
            __instance.FindPopupManager(ref popupManager);
            manager.popupManager = popupManager;
            manager.renamePopupPrefab = __instance.continueLoadNewController.career.renamePopupPrefab;
            manager.okPopupPrefab = __instance.continueLoadNewController.career.okPopupPrefab;
            manager.uiMenuController = __instance.menuController;
        });

        multiplayerPane.SetActive(true);
        MainMenuController_Awake_Patch.MultiplayerButton.SetActive(true);
    }
}
