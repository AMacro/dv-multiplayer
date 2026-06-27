using DV.Localization;
using DV.UI;
using DV.UIFramework;
using Multiplayer.Components.Networking.UI;
using Multiplayer.Components.UI.Settings;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu;

public class MultiplayerSettingsMenu : MonoBehaviour
{
    const float ROW_HEIGHT = 53f;

    private static GameObject scrollViewPrefab;

    public int CharacterSelectorMenuIndex;
    public UIMenuController MenuController;
    public GameObject BottomButtons;
    public ButtonDV ApplyButton;
    public ButtonDV DiscardButton;

    readonly Dictionary<object, Action> pendingChanges = [];
    bool showingCharacterSelector = false;

    Color disabledInputColor;
    Color enabledInputColor;


    protected void Awake()
    {
        // Grab UI elements to make prefabs
        var scrollGO = transform.parent.FindChildByName("Scroll View").gameObject;
        scrollViewPrefab = UIHelpers.MakePrefab(scrollGO);

        // Clean up child objects on this menu
        ClearUI();

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

        Settings.OnSettingsUpdated += SettingsChanged;

        // Don't rebuild the UI if we're returning from the character selector
        if (showingCharacterSelector)
        {
            showingCharacterSelector = false;
            return;
        }

        ClearUI();
        BuildUI();
    }

    protected void OnDisable()
    {
        if (BottomButtons != null)
        {
            BottomButtons.SetActive(false);
            ApplyButton.Clicked -= ApplyChanges;
            DiscardButton.Clicked -= DiscardChanges;
        }

        Settings.OnSettingsUpdated -= SettingsChanged;

        // Don't rebuild the UI if we're returning from the character selector
        if (showingCharacterSelector)
            return;

        ClearUI();
    }

    private void ClearUI()
    {
        pendingChanges.Clear();

        for (int i = 0; i < transform.childCount; i++)
            Destroy(transform.GetChild(i).gameObject);
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
        gridLayout.cellSize = new Vector2(gridLayout.cellSize.x * 2, ROW_HEIGHT);
        gridLayout.padding = new RectOffset(0, 0, 0, 0);

        BuildPlayerPrefs(content);
        UIHelpers.CreateDivider(content);
        BuildOtherPrefs(content);
        UIHelpers.CreateDivider(content);
        BuildAdvancedPrefs(content);

        scrollView.SetActive(true);

        BottomButtons.SetActive(true);

        MarkChanged(false);
    }

    private void BuildPlayerPrefs(RectTransform content)
    {
        // Use steam name
        var useSteamName = UIHelpers.CreateToggle(content, "Use Steam Name", Locale.SETTINGS_USE_STEAM_NAME_KEY, Multiplayer.Settings.UseSteamName);

        // Alternate player name
        var usernameInput = UIHelpers.CreateInputField(content, "Username", "Player", Multiplayer.Settings.Username, false, Settings.MAX_USERNAME_LENGTH);
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

        // Listen for changes to the "Use Steam Name" toggle
        useSteamName.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.UseSteamName;
            // update the pending changes
            if (changed)
                pendingChanges[useSteamName] = () => Multiplayer.Settings.UseSteamName = value;
            else
                pendingChanges.Remove(useSteamName);

            usernameInput.readOnly = value;
            usernameInput.textComponent.color = value ? disabledInputColor : enabledInputColor;

            MarkChanged(changed, useSteamName.transform);
        });

        // Listen for changes to the username input field
        usernameInput.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.Username;
            // update the pending changes
            if (changed)
                pendingChanges[usernameInput] = () => Multiplayer.Settings.Username = value;
            else
                pendingChanges.Remove(usernameInput);

            MarkChanged(changed);
        });

        // Button to open character selector
        var characterSelectorButton = UIHelpers.CreateButton(content, "Character Selector Button", Locale.SETTINGS_CHOOSE_CHARACTER_KEY);

        characterSelectorButton.Clicked += (clickable) =>
        {
            // Ensure changes aren't reverted while player is on the character selector submenu
            showingCharacterSelector = true;
            MenuController.SwitchMenu(CharacterSelectorMenuIndex);
        };
    }

    private void BuildOtherPrefs(RectTransform content)
    {
        // Show Name Tags
        var showNameTags = UIHelpers.CreateToggle(content, "Show Name Tags", Locale.SETTINGS_SHOW_NAME_TAGS_KEY, Multiplayer.Settings.ShowNameTags);
        showNameTags.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.ShowNameTags;
            // update the pending changes
            if (changed)
                pendingChanges[showNameTags] = () => Multiplayer.Settings.ShowNameTags = value;
            else
                pendingChanges.Remove(showNameTags);

            MarkChanged(changed, showNameTags.transform);
        });


        // Show Pings
        var showPings = UIHelpers.CreateToggle(content, "Show Pings", Locale.SETTINGS_SHOW_PINGS_KEY, Multiplayer.Settings.ShowPingInNameTags);
        showPings.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.ShowPingInNameTags;
            // update the pending changes
            if (changed)
                pendingChanges[showPings] = () => Multiplayer.Settings.ShowPingInNameTags = value;
            else
                pendingChanges.Remove(showPings);

            MarkChanged(changed, showPings.transform);
        });


        // Show Player List
        var showPlayerList = UIHelpers.CreateToggle(content, "Show Player List", Locale.SETTINGS_SHOW_PLAYER_LIST_KEY, Multiplayer.Settings.ShowPlayerListInAltMouseMode);

        showPlayerList.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.ShowPlayerListInAltMouseMode;
            // update the pending changes
            if (changed)
                pendingChanges[showPlayerList] = () => Multiplayer.Settings.ShowPlayerListInAltMouseMode = value;
            else
                pendingChanges.Remove(showPlayerList);

            MarkChanged(changed, showPlayerList.transform);
        });


        // Player List Position
        var positions = new List<string>(Enum.GetNames(typeof(PlayerListGUI.PlayerListPosition)).Select(name => Locale.SETTINGS_POSITION_KEY + name));
        var playerListPositionSelector = UIHelpers.CreateSelector
        (
            content,
            "Player List Position",
            Locale.SETTINGS_PLAYER_LIST_POSITION_KEY,
            true,
            true,
            positions,
            (int)Multiplayer.Settings.PlayerListPosition
        );

        playerListPositionSelector.SelectionChanged += (_, index) =>
        {
            bool changed = index != (int)Multiplayer.Settings.PlayerListPosition;

            // update the pending changes
            if (changed)
                pendingChanges[playerListPositionSelector] = () => Multiplayer.Settings.PlayerListPosition = (PlayerListGUI.PlayerListPosition)index;
            else
                pendingChanges.Remove(playerListPositionSelector);

            MarkChanged(changed, playerListPositionSelector.transform);
        };


        // Show Chat Messages
        var showChatMessages = UIHelpers.CreateToggle(content, "Show Chat Messages", Locale.SETTINGS_SHOW_CHAT_KEY, !Multiplayer.Settings.HideChatMessages);
        showChatMessages.onValueChanged.AddListener((value) =>
        {
            bool changed = value == Multiplayer.Settings.HideChatMessages;
            // update the pending changes
            if (changed)
                pendingChanges[showChatMessages] = () => Multiplayer.Settings.HideChatMessages = !value;
            else
                pendingChanges.Remove(showChatMessages);

            MarkChanged(changed, showChatMessages.transform);
        });


        // Chat Key Binding
        var chatKeyBinding = UIHelpers.CreateButton(content, "Chat Key Binding", Locale.SETTINGS_CHAT_KEY_BINDING_KEY);
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
        var chatKeyMark = chatKeyBindingLabel.GetOrAddComponent<TMProAddMark>();
        chatKeyBindingLabel.text = String.Format(Locale.SETTINGS_CHAT_KEY_BINDING, Multiplayer.Settings.ChatKey.ToDisplayString());

        chatKeyBinding.Clicked += (_) =>
        {
            KeyBindInterface.Instance.GetKeyBind((newKey) =>
            {
                if (newKey == KeyCode.None)
                    return;

                var changed = newKey != Multiplayer.Settings.ChatKey;

                // update the pending changes
                if (changed)
                    pendingChanges[chatKeyBinding] = () => { Multiplayer.Settings.ChatKey = newKey; };
                else
                    pendingChanges.Remove(chatKeyBinding);

                chatKeyBindingLabel.SetText(String.Format(Locale.SETTINGS_CHAT_KEY_BINDING, newKey));
                chatKeyBindingLabel.ForceMeshUpdate();

                MarkChanged(changed, chatKeyBinding.transform);
            });
        };
    }

    private void BuildAdvancedPrefs(RectTransform content)
    {
        // Enable debug logging
        var enableDebugLogging = UIHelpers.CreateToggle(content, "Enable Debug Logging", Locale.SETTINGS_DEBUG_LOGGING_KEY, Multiplayer.Settings.DebugLogging);
        enableDebugLogging.onValueChanged.AddListener((value) =>
        {
            bool changed = value != Multiplayer.Settings.DebugLogging;
            // update the pending changes
            if (changed)
                pendingChanges[enableDebugLogging] = () => Multiplayer.Settings.DebugLogging = value;
            else
                pendingChanges.Remove(enableDebugLogging);

            MarkChanged(changed, enableDebugLogging.transform);
        });
    }

    private void MarkChanged(bool changed, Transform source = null)
    {
        TMProAddMark mark = null;
        if (source != null)
            mark = source.GetComponentInChildren<TMProAddMark>();

        if (changed)
            mark?.SetMark("*");
        else
            mark?.ClearMark();

        var active = pendingChanges.Count > 0;

        ApplyButton.ToggleInteractable(active);
        DiscardButton.ToggleInteractable(active);

    }

    private void SettingsChanged(Settings settings)
    {
        ClearUI();
        BuildUI();
    }
    private void DiscardChanges(IClickable clickable)
    {
        ClearUI();
        BuildUI();
    }

    private void ApplyChanges(IClickable clickable)
    {
        foreach (var change in pendingChanges)
            change.Value?.Invoke();

        pendingChanges.Clear();

        Multiplayer.Settings.Save(Multiplayer.ModEntry);
    }

}
