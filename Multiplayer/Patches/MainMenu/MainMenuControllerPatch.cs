using DV.Localization;
using DV.UI;
using HarmonyLib;
using Multiplayer.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Patches.MainMenu
{
    /// <summary>
    /// Harmony patch for the Awake method of MainMenuController to add a Multiplayer button.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuController), "Awake")]
    public static class MainMenuController_Awake_Patch
    {
        public static GameObject multiplayerButton;

        /// <summary>
        /// Prefix method to run before MainMenuController's Awake method.
        /// </summary>
        /// <param name="__instance">The instance of MainMenuController.</param>
        private static void Prefix(MainMenuController __instance)
        {
            // Find the Sessions button to base the Multiplayer button on
            GameObject sessionsButton = __instance.FindChildByName("ButtonSelectable Sessions");
            if (sessionsButton == null)
            {
                Multiplayer.LogError("Failed to find Sessions button!");
                return;
            }

            // Deactivate the sessions button temporarily to duplicate it
            sessionsButton.SetActive(false);
            multiplayerButton = Object.Instantiate(sessionsButton, sessionsButton.transform.parent);
            sessionsButton.SetActive(true);

            // Configure the new Multiplayer button
            multiplayerButton.transform.SetSiblingIndex(sessionsButton.transform.GetSiblingIndex() + 1);
            multiplayerButton.name = "ButtonSelectable Multiplayer";

            // Set the localization key for the new button
            Localize localize = multiplayerButton.GetComponentInChildren<Localize>();
            localize.key = Locale.MAIN_MENU__JOIN_SERVER_KEY;

            // Remove existing localization components to reset them
            Object.Destroy(multiplayerButton.GetComponentInChildren<I2.Loc.Localize>());
            multiplayerButton.ResetTooltip();

            // Set the icon for the new Multiplayer button
            SetButtonIcon(multiplayerButton);
        }

        /// <summary>
        /// Resets the tooltip for a given button.
        /// </summary>
        /// <param name="button">The button to reset the tooltip for.</param>
        //private static void ResetTooltip(GameObject button)
        //{
        //    UIElementTooltip tooltip = button.GetComponent<UIElementTooltip>();
        //    tooltip.disabledKey = null;
        //    tooltip.enabledKey = null;
        //}

        /// <summary>
        /// Sets the icon for the Multiplayer button.
        /// </summary>
        /// <param name="button">The button to set the icon for.</param>
        private static void SetButtonIcon(GameObject button)
        {
            GameObject icon = button.FindChildByName("icon");
            if (icon == null)
            {
                Multiplayer.LogError("Failed to find icon on Sessions button, destroying the Multiplayer button!");
                Object.Destroy(multiplayerButton);
                return;
            }

            icon.GetComponent<Image>().sprite = Multiplayer.AssetIndex.multiplayerIcon;
        }
    }
}
