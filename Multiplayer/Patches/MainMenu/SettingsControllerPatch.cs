using DV.Localization;
using DV.UI;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Utils;
using UnityEngine.UI;
using UnityEngine;
using Multiplayer.Components.MainMenu;

namespace Multiplayer.Patches.MainMenu;

[HarmonyPatch(typeof(SettingsController))]
public static class SettingsControllerPatch
{
    [HarmonyPatch(typeof(SettingsController), nameof(SettingsController.Awake))]
    [HarmonyPostfix]
    private static void Awake(SettingsController __instance)
    {
        var goPane = __instance.FindChildByName("Gameplay");

        if (goPane == null)
        {
            Multiplayer.LogError("Failed to find Gameplay settings panel!");
            return;
        }

        goPane.SetActive(false);
        var mpSettingsPane = GameObject.Instantiate(goPane, goPane.transform.parent);
        goPane.SetActive(true);

        mpSettingsPane.name = "Multiplayer";
        mpSettingsPane.GetComponent<SettingsCategoryMarker>().categoryName = "Multiplayer";

        mpSettingsPane.AddComponent<CharacterSelectorMenu>();

        // Build button
        var goButton = __instance.FindChildByName("Left Buttons").FindChildByName("Game");
        if (goButton == null)
        {
            Multiplayer.LogError("Failed to find Game settings button!");
            return;
        }

        goButton.SetActive(false);
        var mpButton = GameObject.Instantiate(goButton, goButton.transform.parent);
        goButton.SetActive(true);

        mpButton.name = "Multiplayer";
        mpButton.transform.SetSiblingIndex(goButton.transform.GetSiblingIndex() + 1);

        // Set the localization key for the new button
        Localize localize = mpButton.GetComponentInChildren<Localize>();
        localize.key = Locale.SETTINGS__SETTINGS_KEY;

        // Remove existing localization components to reset them
        Object.Destroy(mpButton.GetComponentInChildren<I2.Loc.Localize>());
        mpButton.ResetTooltip();

        // Wire up the button
        __instance.menuController.controlledMenus.Add(mpSettingsPane.GetComponent<UIMenu>());
        var index = __instance.menuController.controlledMenus.Count - 1;
        UIMenuRequester mpButtonReq = mpButton.GetComponent<UIMenuRequester>();
        mpButtonReq.requestedMenuIndex = index;

        GameObject icon = mpButton.FindChildByName("[icon]");
        if (icon != null)
        {
            icon.GetComponent<Image>().sprite = Multiplayer.AssetIndex.multiplayerIcon;
        }

        mpButton.SetActive(true);
    }
}

