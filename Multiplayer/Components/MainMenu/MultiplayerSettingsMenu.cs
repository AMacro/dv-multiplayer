using DV.Localization;
using DV.UI;
using DV.UIFramework;
using Multiplayer.Components.Networking.UI;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu;

public class MultiplayerSettingsMenu : MonoBehaviour
{
    GameObject selectorPrefab;
    GameObject togglePrefab;
    GameObject sliderPrefab;
    GameObject buttonPrefab;
    GameObject inputPrefab;
    GameObject dividerPrefab;
    GameObject scrollViewPrefab;

    public int CharacterSelectorMenuIndex;
    public UIMenuController MenuController;
    public GameObject BottomButtons;
    public ButtonDV ApplyButton;
    public ButtonDV DiscardButton;

    int changeCounter = 0;
    bool showingCharacterSelector = false;

    Color disabledInputColor;
    Color enabledInputColor;


    protected void Awake()
    {
        // Grab UI elements to make prefabs
        selectorPrefab = UIHelpers.MakePrefab<Selector>(transform);
        togglePrefab = UIHelpers.MakePrefab<ToggleDV>(transform);
        sliderPrefab = UIHelpers.MakePrefab<SliderDV>(transform);

        var buttonGO = transform.parent.FindChildByName("Open Bindings");
        buttonPrefab = UIHelpers.MakePrefab<ButtonDV>(buttonGO);

        var scrollGO = transform.parent.FindChildByName("Scroll View").gameObject;
        scrollViewPrefab = UIHelpers.MakePrefab(scrollGO);

        GameObject goMMC = GameObject.FindObjectOfType<MainMenuController>().gameObject;
        var divider = goMMC.FindChildByName("Divider");
        dividerPrefab = UIHelpers.MakePrefab(divider);

        var inputGo = MainMenuThingsAndStuff.Instance.references.popupTextInput.gameObject.FindChildByName("TextFieldTextIcon");
        inputPrefab = UIHelpers.MakePrefab(inputGo);

        // Clean up child objects on this menu
        for (int i = 0; i < transform.childCount; i++)
            Destroy(transform.GetChild(i).gameObject);

        // Remove layout components
        var existingGrid = GetComponent<GridLayoutGroup>();
        if (existingGrid != null)
            DestroyImmediate(existingGrid);

        BuildUI();
    }

    protected void OnEnable()
    {
        // Re-enable the bottom buttons and their events
        if (BottomButtons != null)
        {
            BottomButtons.SetActive(true);
            ApplyButton.Clicked += ApplyChanges;
            DiscardButton.Clicked += DiscardChanges;
        }

        // Don't rebuild the UI if we're returning from the character selector
        if (showingCharacterSelector)
        {
            showingCharacterSelector = false;
            return;
        }
    }

    protected void OnDisable()
    {
        if (BottomButtons != null)
        {
            BottomButtons.SetActive(false);
            ApplyButton.Clicked -= ApplyChanges;
            DiscardButton.Clicked -= DiscardChanges;
        }

        // Don't wipe the UI if we're going to the character selector
        if (showingCharacterSelector)
            return;
    }

    private void BuildUI()
    {
        // Setup ScrollView
        var scrollView = Instantiate(scrollViewPrefab, transform);
        scrollView.name = "Scroll View";
        var scrollRect = scrollView.GetComponent<ScrollRect>();
        var content = scrollRect.content;

        for (int i = 0; i < content.childCount; i++)
            Destroy(content.GetChild(i).gameObject);

        var gridLayout = content.GetComponent<GridLayoutGroup>();
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 1;
        gridLayout.cellSize = new Vector2(gridLayout.cellSize.x * 2, 53f);
        gridLayout.padding = new RectOffset(0, 0, 0, 0);

        BuildPlayerPrefs(content);
        CreateDivider(content);
        BuildAdvancedPrefs(content);

        scrollView.SetActive(true);

        BottomButtons.SetActive(true);
    }

    private void BuildPlayerPrefs(RectTransform content)
    {
        // Use steam name
        var useSteamName = CreateToggle(content, "Use Steam Name", Locale.SETTINGS_USE_STEAM_NAME_KEY, Multiplayer.Settings.UseSteamName);

        // Alternate player name
        var usernameInput = CreateInputField(content, "Username", "Player", Multiplayer.Settings.Username, Settings.MAX_USERNAME_LENGTH);
        var hoverImage = useSteamName.FindChildByName("[image hover]");
        Instantiate(hoverImage, usernameInput.transform);

        usernameInput.readOnly = Multiplayer.Settings.UseSteamName; // Initial read-only state
        var hoverable = usernameInput.GetOrAddComponent<SimpleHoverable>();
        hoverable.addEffects = true;
        var usernameTooltip = usernameInput.GetOrAddComponent<UIElementTooltip>();
        usernameTooltip.hoverable = hoverable;
        usernameTooltip.enabledKey = Locale.SETTINGS_PLAYER_NAME_KEY + "__tooltip";

        // Store colours and set initial styling
        enabledInputColor = usernameInput.textComponent.color;
        disabledInputColor = usernameInput.placeholder.color;
        usernameInput.textComponent.color = Multiplayer.Settings.UseSteamName ? disabledInputColor : enabledInputColor;

        // Button to open character selector
        var characterSelectorButton = CreateButton(content, "Character Selector Button", Locale.SETTINGS_CHOOSE_CHARACTER_KEY);

        // Listen for changes to the "Use Steam Name" toggle
        useSteamName.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.UseSteamName;
            MarkChanged(changed);

            usernameInput.readOnly = value;
            usernameInput.textComponent.color = value ? disabledInputColor : enabledInputColor;
        });

        // Listen for changes to the username input field
        usernameInput.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.GetUserName();
            MarkChanged(changed);
        });

        // Click event for the character selector button
        characterSelectorButton.Clicked += (clickable) =>
        {
            showingCharacterSelector = true;
            // request submenu

            MenuController.SwitchMenu(CharacterSelectorMenuIndex);
        };

        CreateDivider(content);

        // Show Name Tags
        var showNameTags = CreateToggle(content, "Show Name Tags", Locale.SETTINGS_SHOW_NAME_TAGS_KEY, Multiplayer.Settings.ShowNameTags);

        var showPings = CreateToggle(content, "Show Pings", Locale.SETTINGS_SHOW_PINGS_KEY, Multiplayer.Settings.ShowPingInNameTags);

        var showPlayerList = CreateToggle(content, "Show Player List", Locale.SETTINGS_SHOW_PLAYER_LIST_KEY, Multiplayer.Settings.ShowPlayerListInAltMouseMode);

        var positions = new List<string>(Enum.GetNames(typeof(PlayerListGUI.PlayerListPosition)).Select(name => Locale.SETTINGS_POSITION_KEY + name));
        var playerListPositionSelector = CreateSelector(content, "Player List Position", Locale.SETTINGS_PLAYER_LIST_POSITION_KEY, true, true, positions, (int)Multiplayer.Settings.PlayerListPosition);

        var showChatMessages = CreateToggle(content, "Show Chat Messages", Locale.SETTINGS_SHOW_CHAT_KEY, !Multiplayer.Settings.HideChatMessages);

        // Multiplayer.Settings.ChatKey
        var chatKeyBinding = CreateButton(content, "Chat Key Binding", Locale.SETTINGS_CHAT_KEY_BINDING_KEY);
        var loc = chatKeyBinding.GetComponentInChildren<Localize>();
        if (loc != null)
            DestroyImmediate(loc);
        var tooltip = chatKeyBinding.GetComponent<UIElementTooltip>();
        if (tooltip != null)
        {
            tooltip.enabledKey = Locale.SETTINGS_CHAT_KEY_BINDING_TOOLTIP_ENABLED_KEY;
            tooltip.disabledKey = Locale.SETTINGS_CHAT_KEY_BINDING_TOOLTIP_DISABLED_KEY;
        }

        var chatKeyBindingLabel = chatKeyBinding.GetComponentInChildren<TMP_Text>();
        chatKeyBindingLabel.text = String.Format(Locale.SETTINGS_CHAT_KEY_BINDING, Multiplayer.Settings.ChatKey.ToString());
    }

    private void BuildAdvancedPrefs(RectTransform content)
    {
        // Enable debug logging
        var enableDebugLogging = CreateToggle(content, "Enable Debug Logging", Locale.SETTINGS_DEBUG_LOGGING_KEY, Multiplayer.Settings.DebugLogging);
    }

    private void MarkChanged(bool changed)
    {
        if (changed)
            changeCounter++;
        else
            changeCounter--;

        if (changeCounter <= 0)
        {
            changeCounter = 0;

            ApplyButton.ToggleInteractable(false);
            DiscardButton.ToggleInteractable(false);
        }
        else
        {
            ApplyButton.ToggleInteractable(true);
            DiscardButton.ToggleInteractable(true);
        }
    }

    private void DiscardChanges(IClickable clickable)
    {
        // Reset all settings to their current values
    }

    private void ApplyChanges(IClickable clickable)
    {
        // Save all settings to disk and apply them
    }

    #region Control Factories

    private ToggleDV CreateToggle(RectTransform parent, string name, string label_key, bool initialValue)
    {
        var go = Instantiate(togglePrefab, parent);
        go.name = name;

        var toggle = go.GetComponent<ToggleDV>();
        toggle.isOn = initialValue;

        var labelGo = go.FindChildByName("text");
        labelGo.GetComponent<Localize>().key = label_key;
        go.gameObject.ResetTooltip();

        go.SetActive(true);

        return toggle;
    }

    private TMP_InputField CreateInputField(RectTransform parent, string name, string placeholder, string initialValue, int characterLimit = 0)
    {
        var go = Instantiate(inputPrefab, parent);
        go.name = name;

        var input = go.GetComponent<TMP_InputField>();
        input.text = initialValue ?? string.Empty;

        if (characterLimit > 0)
            input.characterLimit = characterLimit;

        var placeholderText = input.placeholder?.GetComponent<TMP_Text>();
        if (placeholderText != null)
            placeholderText.text = placeholder;

        var icon = go.FindChildByName("icon");
        if (icon != null)
            icon.transform.localPosition = new Vector3(icon.transform.localPosition.x - 15, icon.transform.localPosition.y, icon.transform.localPosition.z);

        go.SetActive(true);

        return input;
    }

    private Selector CreateSelector(RectTransform parent, string objectName, string label, bool localisedLabel, bool localisedValues, List<string> values, int selectedIndex)
    {
        selectorPrefab.SetActive(false);
        var go = Instantiate(selectorPrefab, parent);
        selectorPrefab.SetActive(true);
        go.name = objectName;

        var selector = go.GetOrAddComponent<Selector>();

        // Strip any existing localization so we can set values directly
        if (selector.labelTMPro?.gameObject.TryGetComponent<I2.Loc.Localize>(out var i2loc) ?? false)
            DestroyImmediate(i2loc);
        if (selector.labelTMPro?.gameObject.TryGetComponent<Localize>(out var dvloc) ?? false)
            DestroyImmediate(dvloc);

        if (go.TryGetComponent<SettingChangeSource>(out var scs))
            DestroyImmediate(scs);

        selector.initialized = false;
        selector.LocalizedLabel = localisedLabel;
        selector.SetLabel(label);
        if (localisedLabel)
            selector.labelTMPro.GetComponent<Localize>().key = label;

        selector.LocalizedValues = localisedValues;
        selector.SetValues(values);
        selector.SetSelectedIndex(selectedIndex);

        go.ResetTooltip();

        go.SetActive(true);
        selector.ToggleInteractable(true);

        return selector;
    }

    private ButtonDV CreateButton(RectTransform parent, string name, string label_key)
    {
        var go = Instantiate(buttonPrefab, parent);
        go.name = name;

        var button = go.GetComponent<ButtonDV>();
        var inteff = go.GetComponent<InteractableEffect>();
        DestroyImmediate(inteff);
        DestroyImmediate(button);
        button = go.AddComponent<ButtonDV>();

        button.GetComponentInChildren<Localize>().key = label_key;
        go.gameObject.ResetTooltip();

        go.SetActive(true);

        return button;
    }

    private void CreateDivider(RectTransform parent)
    {
        var go = Instantiate(dividerPrefab, parent);
        go.name = "Divider";
        go.SetActive(true);
    }

    #endregion

}
