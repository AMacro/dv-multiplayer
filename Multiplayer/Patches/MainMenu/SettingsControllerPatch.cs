using DV.Localization;
using DV.UI;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Utils;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Patches.MainMenu;

[HarmonyPatch(typeof(SettingsController))]
public static class SettingsControllerPatch
{
    [HarmonyPatch(typeof(SettingsController), nameof(SettingsController.Awake))]
    [HarmonyPrefix]
    private static void Awake(SettingsController __instance)
    {
        var goPane = __instance.FindChildByName("Gameplay");

        if (goPane == null)
        {
            Multiplayer.LogError("Failed to find Gameplay settings panel!");
            return;
        }

        goPane.SetActive(false);
        var mpSettingsPaneGO = GameObject.Instantiate(goPane, goPane.transform.parent);

        for (int i = 0; i < mpSettingsPaneGO.transform.childCount; i++)
            GameObject.DestroyImmediate(mpSettingsPaneGO.transform.GetChild(i).gameObject);

        var mpCharSelectPaneGO = GameObject.Instantiate(goPane, goPane.transform.parent);

        for (int i = 0; i < mpCharSelectPaneGO.transform.childCount; i++)
            GameObject.DestroyImmediate(mpCharSelectPaneGO.transform.GetChild(i).gameObject);

        goPane.SetActive(true);

        mpSettingsPaneGO.name = "Multiplayer";
        mpSettingsPaneGO.GetComponent<SettingsCategoryMarker>().categoryName = "Multiplayer";
        var mpSettingsPane = mpSettingsPaneGO.AddComponent<MultiplayerSettingsMenu>();

        mpCharSelectPaneGO.name = "Character";
        mpCharSelectPaneGO.GetComponent<SettingsCategoryMarker>().categoryName = "MultiplayerCharacter";
        var mpCharSelectPane = mpCharSelectPaneGO.AddComponent<CharacterSelectorMenu>();

        // Add Multiplayer button
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
        GameObject.Destroy(mpButton.GetComponentInChildren<I2.Loc.Localize>());
        mpButton.ResetTooltip();

        // Wire up the button
        __instance.menuController.controlledMenus.Add(mpSettingsPaneGO.GetComponent<UIMenu>());
        var mpMenuIndex = __instance.menuController.controlledMenus.Count - 1;
        UIMenuRequester mpButtonReq = mpButton.GetComponent<UIMenuRequester>();
        mpButtonReq.requestedMenuIndex = mpMenuIndex;

        GameObject icon = mpButton.FindChildByName("[icon]");
        if (icon != null)
        {
            icon.GetComponent<Image>().sprite = Multiplayer.AssetIndex.multiplayerIcon;
        }

        mpButton.SetActive(true);

        // Setup the character select pane to be accessible from the multiplayer settings pane
        mpSettingsPane.MenuController = __instance.menuController;
        __instance.menuController.controlledMenus.Add(mpCharSelectPaneGO.GetComponent<UIMenu>());
        mpSettingsPane.CharacterSelectorMenuIndex = __instance.menuController.controlledMenus.Count - 1;

        // Set up Apply and Discard buttons
        var buttons = CreateBottomButtons(__instance);
        mpSettingsPane.BottomButtons = buttons.Item1;
        mpSettingsPane.ApplyButton = buttons.Item2;
        mpSettingsPane.DiscardButton = buttons.Item3;

        mpCharSelectPane.MenuController = __instance.menuController;
        mpCharSelectPane.CharacterSelectorMenuIndex = mpMenuIndex;
        mpCharSelectPane.BottomButtons = buttons.Item1;
        mpCharSelectPane.ApplyButton = buttons.Item2;
        mpCharSelectPane.DiscardButton = buttons.Item3;

        MoveMenuToScrollView(__instance.gameObject);
    }

    private static void MoveMenuToScrollView(GameObject parent)
    {
        // Move all menu buttons into a scroll view to prevent them from overflowing in VR mode and to fix
        // the changes list blocking the Advanced settings button
        var originalScrollGo = parent.transform.FindChildByName("Scroll View").gameObject;
        Multiplayer.LogDebug(() => $"Found existing scroll view: {originalScrollGo.name}");
        var scrollViewGO = UIHelpers.MakePrefab(originalScrollGo);
        scrollViewGO.name = "Scroll View Left Buttons";
        scrollViewGO.transform.SetParent(parent.transform, false);

        var leftButtons = parent.transform.FindChildByName("Left Buttons");
        scrollViewGO.transform.SetSiblingIndex(leftButtons.GetSiblingIndex());

        var scrollRect = scrollViewGO.GetComponent<ScrollRect>();
        var scrollRT = scrollViewGO.GetComponent<RectTransform>();
        var leftRT = leftButtons.GetComponent<RectTransform>();
        scrollRT.pivot = leftRT.pivot;
        scrollRT.anchorMin = leftRT.anchorMin;
        scrollRT.anchorMax = leftRT.anchorMax;
        scrollRT.anchoredPosition = leftRT.anchoredPosition;

        // Reset the width accounting for the scrollbar width and position
        var scrollbarRT = scrollRect.verticalScrollbar.GetComponent<RectTransform>();
        float scrollbarExtra = scrollbarRT.sizeDelta.x + Mathf.Abs(scrollbarRT.anchoredPosition.x);
        scrollRT.sizeDelta = new Vector2(leftRT.sizeDelta.x + scrollbarExtra, leftRT.sizeDelta.y);

        var viewportRT = scrollRect.viewport;
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.anchoredPosition = Vector2.zero;
        viewportRT.sizeDelta = Vector2.zero;
        viewportRT.pivot = new Vector2(0f, 1f);

        GameObject.Destroy(scrollRect.content.gameObject);
        leftButtons.SetParent(viewportRT, false);
        scrollRect.content = leftRT;


        leftRT.anchorMin = new Vector2(0f, 1f);
        leftRT.anchorMax = new Vector2(1f, 1f);
        leftRT.pivot = new Vector2(0f, 1f);
        leftRT.anchoredPosition = Vector2.zero;
        leftRT.sizeDelta = Vector2.zero;

        var csf = leftButtons.GetOrAddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        //leftButtons.SetParent(content, false);

        scrollViewGO.SetActive(true);
    }

    private static Tuple<GameObject, ButtonDV, ButtonDV> CreateBottomButtons(SettingsController controller)
    {
        var buttonsGO = controller.transform.FindChildByName("buttons bottom-right").gameObject;
        var BottomButtons = UIHelpers.MakePrefab(buttonsGO);
        BottomButtons.transform.SetParent(buttonsGO.transform.parent, false);
        BottomButtons.name = "buttons bottom-right Multiplayer";

        var selectorGo = BottomButtons.transform.FindChildByName("Selector Preset").gameObject;
        GameObject.Destroy(selectorGo);

        var discardGO = BottomButtons.FindChildByName("ButtonTextIcon Discard");
        var discardButton = discardGO.GetComponent<ButtonDV>();
        
        discardButton.ToggleInteractable(false);
        discardButton.gameObject.SetActive(true);

        var applyGO = BottomButtons.FindChildByName("ButtonTextIcon Apply");
        var applyButton = applyGO.GetComponent<ButtonDV>();
        
        applyButton.ToggleInteractable(false);
        applyButton.gameObject.SetActive(true);

        return new Tuple<GameObject, ButtonDV, ButtonDV>(BottomButtons, applyButton, discardButton);
    }

    /// <summary>
    /// Recursively logs the full UI hierarchy from <paramref name="root"/> downward, including
    /// RectTransform layout data and key UI components on each GameObject.
    /// Output is sent through <see cref="Multiplayer.LogDebug"/>.
    /// </summary>
    public static void LogUIHierarchy(this GameObject root, string label = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== UI Hierarchy: {label ?? root.name} ===");
        AppendUINode(sb, root.transform, 0);
        Multiplayer.LogDebug(() => sb.ToString());
    }

    /// <inheritdoc cref="LogUIHierarchy(GameObject, string)"/>
    public static void LogUIHierarchy(this Component root, string label = null)
    {
        root.gameObject.LogUIHierarchy(label);
    }

    private static void AppendUINode(StringBuilder sb, Transform t, int depth)
    {
        string indent = new string(' ', depth * 2);
        var go = t.gameObject;
        var rt = go.GetComponent<RectTransform>();

        sb.Append($"{indent}[{(go.activeSelf ? "ON" : "OFF")}] {go.name}");

        if (rt != null)
        {
            sb.Append($"  RT(anchorMin={rt.anchorMin}, anchorMax={rt.anchorMax}," +
                      $" pivot={rt.pivot}," +
                      $" anchoredPos={rt.anchoredPosition}," +
                      $" sizeDelta={rt.sizeDelta}," +
                      $" rect={rt.rect})");
        }

        // Key layout / scroll components
        AppendComponentInfo<LayoutGroup>(sb, go);
        AppendComponentInfo<ContentSizeFitter>(sb, go);
        AppendComponentInfo<LayoutElement>(sb, go);
        AppendComponentInfo<ScrollRect>(sb, go);
        AppendComponentInfo<Mask>(sb, go);
        AppendComponentInfo<RectMask2D>(sb, go);
        AppendComponentInfo<Canvas>(sb, go);
        AppendComponentInfo<CanvasGroup>(sb, go);
        AppendComponentInfo<Image>(sb, go);

        sb.AppendLine();

        for (int i = 0; i < t.childCount; i++)
            AppendUINode(sb, t.GetChild(i), depth + 1);
    }

    private static void AppendComponentInfo<T>(StringBuilder sb, GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null)
            return;

        sb.Append($"  [{typeof(T).Name}");

        switch (c)
        {
            case VerticalLayoutGroup vlg:
                sb.Append($": spacing={vlg.spacing}, padding={vlg.padding.left}/{vlg.padding.right}/{vlg.padding.top}/{vlg.padding.bottom}" +
                          $", childForceExpandW={vlg.childForceExpandWidth}, childForceExpandH={vlg.childForceExpandHeight}" +
                          $", controlChildSizeW={vlg.childControlWidth}, controlChildSizeH={vlg.childControlHeight}");
                break;
            case HorizontalLayoutGroup hlg:
                sb.Append($": spacing={hlg.spacing}, padding={hlg.padding.left}/{hlg.padding.right}/{hlg.padding.top}/{hlg.padding.bottom}" +
                          $", childForceExpandW={hlg.childForceExpandWidth}, childForceExpandH={hlg.childForceExpandHeight}");
                break;
            case GridLayoutGroup glg:
                sb.Append($": cellSize={glg.cellSize}, spacing={glg.spacing}");
                break;
            case ContentSizeFitter csf:
                sb.Append($": horizontal={csf.horizontalFit}, vertical={csf.verticalFit}");
                break;
            case LayoutElement le:
                sb.Append($": minW={le.minWidth}, minH={le.minHeight}" +
                          $", preferredW={le.preferredWidth}, preferredH={le.preferredHeight}" +
                          $", flexibleW={le.flexibleWidth}, flexibleH={le.flexibleHeight}" +
                          $", ignoreLayout={le.ignoreLayout}");
                break;
            case ScrollRect sr:
                sb.Append($": content={(sr.content != null ? sr.content.name : "null")}" +
                          $", viewport={(sr.viewport != null ? sr.viewport.name : "null")}" +
                          $", horizontal={sr.horizontal}, vertical={sr.vertical}" +
                          $", movementType={sr.movementType}");
                break;
            case CanvasGroup cg:
                sb.Append($": alpha={cg.alpha}, interactable={cg.interactable}, blocksRaycasts={cg.blocksRaycasts}");
                break;
        }

        sb.Append("]");
    }
}

