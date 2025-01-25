using DV.Localization;
using DV.UI;
using HarmonyLib;
using Multiplayer.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Patches.MainMenu;

[HarmonyPatch(typeof(MainMenuController), "Awake")]
public static class MainMenuController_Awake_Patch
{
    public static GameObject MultiplayerButton;

    private static void Prefix(MainMenuController __instance)
    {
        GameObject button = __instance.FindChildByName("ButtonSelectable Sessions");
        if (button == null)
        {
            Multiplayer.LogError("Failed to find Sessions button!");
            return;
        }

        button.SetActive(false);
        MultiplayerButton = Object.Instantiate(button, button.transform.parent);
        button.SetActive(true);

        MultiplayerButton.transform.SetSiblingIndex(button.transform.GetSiblingIndex() + 1);
        MultiplayerButton.name = "ButtonSelectable Multiplayer";

        Localize localize = MultiplayerButton.GetComponentInChildren<Localize>();
        localize.key = Locale.MAIN_MENU__JOIN_SERVER_KEY;

        // Reset existing localization components that were added when the Sessions button was initialized.
        Object.Destroy(MultiplayerButton.GetComponentInChildren<I2.Loc.Localize>());
        UIElementTooltip tooltip = MultiplayerButton.GetComponent<UIElementTooltip>();
        tooltip.disabledKey = null;
        tooltip.enabledKey = null;

        GameObject icon = MultiplayerButton.FindChildByName("icon");
        if (icon == null)
        {
            Multiplayer.LogError("Failed to find icon on Sessions button, destroying the Multiplayer button!");
            Object.Destroy(MultiplayerButton);
            return;
        }

        icon.GetComponent<Image>().sprite = Multiplayer.AssetIndex.multiplayerIcon;
    }
}
