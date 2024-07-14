using DV.Localization;
using DV.UI;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Utils;
using System.Reflection;
using TMPro;
using UnityEngine;



namespace Multiplayer.Patches.MainMenu
{
    [HarmonyPatch(typeof(RightPaneController), "OnEnable")]
    public static class RightPaneController_OnEnable_Patch
    {
        public static int hostMenuIndex;
        public static UIMenuController uIMenuController;
        public static HostGamePane hgpInstance;
        private static void Prefix(RightPaneController __instance)
        {
            uIMenuController = __instance.menuController;
            // Check if the multiplayer pane already exists
            if (__instance.HasChildWithName("PaneRight Multiplayer"))
                return;

            // Find the base pane for Load/Save
            GameObject basePane = __instance.FindChildByName("PaneRight Load/Save");
            if (basePane == null)
            { 
                Multiplayer.LogError("Failed to find Launcher pane!");
                return;
            }

            // Create a new multiplayer pane based on the base pane
            basePane.SetActive(false);
            GameObject multiplayerPane = GameObject.Instantiate(basePane, basePane.transform.parent);
            basePane.SetActive(true);
            multiplayerPane.name = "PaneRight Multiplayer";

            // Add the multiplayer pane to the menu controller
            __instance.menuController.controlledMenus.Add(multiplayerPane.GetComponent<UIMenu>());
            MainMenuController_Awake_Patch.multiplayerButton.GetComponent<UIMenuRequester>().requestedMenuIndex = __instance.menuController.controlledMenus.Count - 1;

            // Clean up unnecessary components and child objects
            GameObject.Destroy(multiplayerPane.GetComponent<SaveLoadController>());
            GameObject.Destroy(multiplayerPane.GetComponent<PlatformSpecificElements>());
            multiplayerPane.AddComponent<ServerBrowserPane>();

            // Create and initialize MainMenuThingsAndStuff
            MainMenuThingsAndStuff.Create(manager =>
            {
                PopupManager popupManager = null;
                __instance.FindPopupManager(ref popupManager);
                manager.popupManager = popupManager;
                manager.renamePopupPrefab = __instance.continueLoadNewController.career.renamePopupPrefab;
                manager.okPopupPrefab = __instance.continueLoadNewController.career.okPopupPrefab;
                manager.uiMenuController = __instance.menuController;
            });

            // Activate the multiplayer button
            MainMenuController_Awake_Patch.multiplayerButton.SetActive(true);
            Multiplayer.LogError("At end!");



            // Check if the host pane already exists
            if (__instance.HasChildWithName("PaneRight Host"))
                return;

            if (basePane == null)
            {
                Multiplayer.LogError("Failed to find Load/Save pane!");
                return;
            }

            // Create a new host pane based on the base pane
            basePane.SetActive(false);
            GameObject hostPane = GameObject.Instantiate(basePane, basePane.transform.parent);
            basePane.SetActive(true);
            hostPane.name = "PaneRight Host";

            GameObject.Destroy(hostPane.GetComponent<SaveLoadController>());
            GameObject.Destroy(hostPane.GetComponent<PlatformSpecificElements>());
            hgpInstance = hostPane.GetOrAddComponent<HostGamePane>();

            // Add the host pane to the menu controller
            __instance.menuController.controlledMenus.Add(hostPane.GetComponent<UIMenu>());
            hostMenuIndex = __instance.menuController.controlledMenus.Count - 1;
            //MainMenuController_Awake_Patch.multiplayerButton.GetComponent<UIMenuRequester>().requestedMenuIndex = __instance.menuController.controlledMenus.Count - 1;
        }
    }
}
