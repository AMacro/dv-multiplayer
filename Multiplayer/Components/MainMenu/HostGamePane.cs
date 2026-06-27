using DV;
using DV.Common;
using DV.Localization;
using DV.Platform.Steam;
using DV.UI;
using DV.UI.PresetEditors;
using DV.UIFramework;
using Multiplayer.API;
using Multiplayer.Components.Networking;
using Multiplayer.Components.UI.ServerBrowser;
using Multiplayer.Components.Util;
using Multiplayer.Networking.Data;
using Multiplayer.Patches.MainMenu;
using Multiplayer.Utils;
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu;

public class HostGamePane : MonoBehaviour
{
    private const int MAX_SERVER_NAME_LEN = 25;
    private const int MAX_PORT_LEN = 5;
    private const int MAX_DETAILS_LEN = 500;

    private const int MIN_PORT = 1024;
    private const int MAX_PORT = 49151;
    private const int MIN_PLAYERS = 2;
    private const int MAX_PLAYERS = 20;

    private const int DEFAULT_PORT = 7777;

    TMP_InputField serverName;
    TMP_InputField password;
    TMP_InputField port;
    TMP_InputField details;
    TextMeshProUGUI serverDetails;

    SliderDV maxPlayers;
    Selector gameVisibility;
    ButtonDV startButton;

    public ISaveGame saveGame;
    public UIStartGameData startGameData;
    public AUserProfileProvider userProvider;
    public AScenarioProvider scenarioProvider;
    LauncherController lcInstance;

    public Action<ISaveGame> continueCareerRequested;

    private bool incompatibleMods = true;
    #region setup

    public void Awake()
    {
        Multiplayer.Log("HostGamePane Awake()");

        CleanUI();
        BuildUI();
        ValidateInputs(null);
    }

    public void Start()
    {
        Multiplayer.Log("HostGamePane Started");

        if (DVSteamworks.Success)
            return;

        Multiplayer.Log($"Steam not detected, prompt for restart.");
        MainMenuThingsAndStuff.Instance.ShowOkPopup("Steam not detected. Please restart the game with Steam running", () => { });
    }

    public void OnEnable()
    {
        this.SetupListeners(true);

        incompatibleMods = ModCompatibilityManager.Instance.CheckModCompatibility();
        ValidateInputs(null);
    }

    // Disable listeners
    public void OnDisable()
    {
        this.SetupListeners(false);
    }

    private void CleanUI()
    {
        //top elements
        GameObject.Destroy(this.FindChildByName("Text Content"));

        //body elements
        GameObject.Destroy(this.FindChildByName("GRID VIEW"));
        GameObject.Destroy(this.FindChildByName("HardcoreSavingBanner"));
        GameObject.Destroy(this.FindChildByName("TutorialSavingBanner"));

        //footer elements
        GameObject.Destroy(this.FindChildByName("ButtonIcon OpenFolder"));
        GameObject.Destroy(this.FindChildByName("ButtonIcon Rename"));
        GameObject.Destroy(this.FindChildByName("ButtonIcon Delete"));
        GameObject.Destroy(this.FindChildByName("ButtonTextIcon Load"));
        GameObject.Destroy(this.FindChildByName("ButtonTextIcon Overwrite"));

    }
    private void BuildUI()
    {
        //Create Prefabs
        GameObject goMMC = GameObject.FindObjectOfType<MainMenuController>().gameObject;

        lcInstance = goMMC.FindChildByName("PaneRight Launcher").GetComponent<LauncherController>();
        if (lcInstance == null)
        {
            Multiplayer.LogError("No Run Button");
            return;
        }
        Sprite playSprite = lcInstance.runButton.FindChildByName("[icon]").GetComponent<Image>().sprite;


        //update title
        GameObject titleObj = this.FindChildByName("Title");
        GameObject.Destroy(titleObj.GetComponentInChildren<I2.Loc.Localize>());
        titleObj.GetComponentInChildren<Localize>().key = Locale.SERVER_HOST__TITLE_KEY;
        titleObj.GetComponentInChildren<Localize>().UpdateLocalization();

        //update right hand info pane (this will be used later for more settings or information
        GameObject serverWindowGO = this.FindChildByName("Save Description");
        GameObject serverDetailsGO = serverWindowGO.FindChildByName("text list [noloc]");
        HyperlinkHandler hyperLinks = serverDetailsGO.GetOrAddComponent<HyperlinkHandler>();

        hyperLinks.linkColor = new Color(0.302f, 0.651f, 1f); // #4DA6FF
        hyperLinks.linkHoverColor = new Color(0.498f, 0.749f, 1f); // #7FBFFF

        serverWindowGO.name = "Host Details";
        serverDetails = serverDetailsGO.GetComponent<TextMeshProUGUI>();
        serverDetails.textWrappingMode = TextWrappingModes.Normal;
        serverDetails.text = Locale.Get(Locale.SERVER_HOST__INSTRUCTIONS_FIRST_KEY, ["<link=\"https://github.com/AMacro/dv-multiplayer/wiki/Hosting\">", "</link>"]) + "<br><br><br>" +
                             Locale.Get(Locale.SERVER_HOST__MOD_WARNING_KEY, ["<link=\"https://github.com/AMacro/dv-multiplayer/wiki/Mod-Compatibility\">", "</link>"]) + "<br><br>" +
                             Locale.SERVER_HOST__RECOMMEND + "<br><br>" +
                             Locale.SERVER_HOST__SIGNOFF;
        /*"First time hosts, please see the <link=\"https://github.com/AMacro/dv-multiplayer/wiki/Hosting\">Hosting</link> section of our Wiki.<br><br><br>" +

        "Using other mods may cause unexpected behaviour including de-syncs. See <link=\"https://github.com/AMacro/dv-multiplayer/wiki/Mod-Compatibility\">Mod Compatibility</link> for more info.<br><br>" +
        "It is recommended that other mods are disabled and Derail Valley restarted prior to playing in multiplayer.<br><br>" +

        "We hope to have your favourite mods compatible with multiplayer in the future.";*/


        //Find scrolling viewport
        ScrollRect scroller = this.FindChildByName("Scroll View").GetComponent<ScrollRect>();
        RectTransform scrollerRT = scroller.transform.GetComponent<RectTransform>();
        scrollerRT.sizeDelta = new Vector2(scrollerRT.sizeDelta.x, 504);

        // Create the content object
        GameObject controls = new("Controls");
        controls.SetLayersRecursive(Layers.UI);
        controls.transform.SetParent(scroller.viewport.transform, false);

        // Assign the content object to the ScrollRect
        RectTransform contentRect = controls.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0f, 1);
        contentRect.anchoredPosition = new Vector2(0, 21);
        contentRect.sizeDelta = scroller.viewport.sizeDelta;
        scroller.content = contentRect;

        // Add VerticalLayoutGroup and ContentSizeFitter
        VerticalLayoutGroup layoutGroup = controls.AddComponent<VerticalLayoutGroup>();
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = false;
        layoutGroup.childScaleWidth = false;
        layoutGroup.childScaleHeight = false;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = true;

        layoutGroup.spacing = 0; // Adjust the spacing as needed
        layoutGroup.padding = new RectOffset(0, 0, 0, 0);

        ContentSizeFitter sizeFitter = controls.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        /*
         *  Server name field 
         */

        string initServerName = Multiplayer.Settings.ServerName?.Trim().Substring(0, Mathf.Min(Multiplayer.Settings.ServerName.Trim().Length, MAX_SERVER_NAME_LEN));
        serverName = UIHelpers.CreateInputField(NewContentGroup(controls, scroller.viewport.sizeDelta), "Server Name", Locale.SERVER_HOST_NAME, initServerName, true,MAX_SERVER_NAME_LEN);

        var tooltip = serverName.GetComponent<UIElementTooltip>();
        tooltip.enabledKey = Locale.SERVER_HOST_NAME_TOOLTIP_KEY;
        tooltip.disabledKey = Locale.SERVER_HOST_NAME_TOOLTIP_DISABLED_KEY;

        /*
         *  Server password field 
         */
        password = UIHelpers.CreateInputField
        (
            NewContentGroup(controls, scroller.viewport.sizeDelta),
            "Password",
            Locale.SERVER_HOST_PASSWORD,
            Multiplayer.Settings.Password,
            true
        );

        tooltip = password.GetComponent<UIElementTooltip>();
        tooltip.enabledKey = Locale.SERVER_HOST_PASSWORD_TOOLTIP_KEY;
        tooltip.disabledKey = Locale.SERVER_HOST_PASSWORD_TOOLTIP_DISABLED_KEY;


        /*
         *  Server visibility field 
         */
        gameVisibility = UIHelpers.CreateSelector
        (
            NewContentGroup(controls, scroller.viewport.sizeDelta),
            "Visibility",
            Locale.SERVER_HOST_VISIBILITY_KEY,
            true,
            true,
            Locale.SERVER_HOST_VISIBILITY_MODES.ToList(),
            3
        );


        /*
         *  Server details field 
         */
        details = UIHelpers.CreateInputFieldMultiline
        (
            NewContentGroup(controls, scroller.viewport.sizeDelta, 106),
            "Details",
            Locale.SERVER_HOST_DETAILS,
            Multiplayer.Settings.Details,
            true,
            MAX_DETAILS_LEN,
            -1,
            106
        );

        tooltip = details.GetComponent<UIElementTooltip>();
        tooltip.enabledKey = Locale.SERVER_HOST_DETAILS_TOOLTIP_KEY;
        tooltip.disabledKey = Locale.SERVER_HOST_DETAILS_TOOLTIP_DISABLED_KEY;


        // Divider
        UIHelpers.CreateDivider(NewContentGroup(controls, scroller.viewport.sizeDelta));


        /*
         *  Server max players field 
         */

        maxPlayers = UIHelpers.CreateSlider
        (
            NewContentGroup(controls, scroller.viewport.sizeDelta),
            "Max Players",
            Locale.SERVER_HOST_MAX_PLAYERS_KEY,
            true,
            null,
            Multiplayer.Settings.MaxPlayers,
            1,
            MIN_PLAYERS,
            MAX_PLAYERS
        );


        /*
         *  Server port field 
         */
        var initPort = (Multiplayer.Settings.Port >= MIN_PORT && Multiplayer.Settings.Port <= MAX_PORT) ? Multiplayer.Settings.Port.ToString() : DEFAULT_PORT.ToString();
        port = UIHelpers.CreateInputField
        (
            NewContentGroup(controls, scroller.viewport.sizeDelta),
            "Port",
            DEFAULT_PORT.ToString(),
            initPort,
            true,
            MAX_PORT_LEN
        );

        port.characterValidation = TMP_InputField.CharacterValidation.Integer;

        tooltip = port.GetComponent<UIElementTooltip>();
        tooltip.enabledKey = Locale.SERVER_HOST_PORT_TOOLTIP_KEY;
        tooltip.disabledKey = Locale.SERVER_HOST_PORT_TOOLTIP_DISABLED_KEY;


        /*
         *  Start Game button
         */
        var go = this.gameObject.UpdateButton("ButtonTextIcon Save", "ButtonTextIcon Start", Locale.SERVER_HOST_START_KEY, null, playSprite);
        go.FindChildByName("[text]").GetComponent<Localize>().UpdateLocalization();

        startButton = go.GetComponent<ButtonDV>();
        startButton.onClick.RemoveAllListeners();
        startButton.onClick.AddListener(OnStartClick);
    }

    private RectTransform NewContentGroup(GameObject parent, Vector2 sizeDelta, int cellMaxHeight = 53)
    {
        // Create a content group
        GameObject contentGroup = new("ContentGroup");
        contentGroup.SetLayersRecursive(Layers.UI);
        RectTransform groupRect = contentGroup.AddComponent<RectTransform>();
        contentGroup.transform.SetParent(parent.transform, false);
        groupRect.sizeDelta = sizeDelta;

        ContentSizeFitter sizeFitter = contentGroup.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Add VerticalLayoutGroup and ContentSizeFitter
        GridLayoutGroup glayoutGroup = contentGroup.AddComponent<GridLayoutGroup>();
        glayoutGroup.startCorner = GridLayoutGroup.Corner.LowerLeft;
        glayoutGroup.startAxis = GridLayoutGroup.Axis.Vertical;
        glayoutGroup.cellSize = new Vector2(617.5f, cellMaxHeight);
        glayoutGroup.spacing = new Vector2(0, 0);
        glayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glayoutGroup.constraintCount = 1;
        glayoutGroup.padding = new RectOffset(10, 0, 0, 10);

        return groupRect;
    }

    private void SetupListeners(bool on)
    {
        if (on)
        {
            serverName.onValueChanged.RemoveAllListeners();
            serverName.onValueChanged.AddListener(new UnityAction<string>(ValidateInputs));

            port.onValueChanged.RemoveAllListeners();
            port.onValueChanged.AddListener(new UnityAction<string>(ValidateInputs));
        }
        else
        {
            this.serverName.onValueChanged.RemoveAllListeners();
        }

    }

    #endregion

    #region UI callbacks
    private void ValidateInputs(string _)
    {
        bool valid = true;

        if (incompatibleMods)
            valid = false;

        if (!DVSteamworks.Success)
            valid = false;

        if (serverName.text.Trim() == "" || serverName.text.Length > MAX_SERVER_NAME_LEN)
            valid = false;

        if (port.text != "")
        {
            if (!int.TryParse(port.text, out int portNum) || portNum < MIN_PORT || portNum > MAX_PORT)
                valid = false;
        }

        if (port.text == "" && (Multiplayer.Settings.Port < MIN_PORT || Multiplayer.Settings.Port > MAX_PORT))
            valid = false;

        startButton.ToggleInteractable(valid);
    }

    private void OnStartClick()
    {

        using (LobbyServerData serverData = new())
        {
            serverData.port = (port.text == "") ? Multiplayer.Settings.Port : int.Parse(port.text); ;
            serverData.Name = serverName.text.Trim();
            serverData.HasPassword = password.text != "";
            serverData.Visibility = (ServerVisibility)gameVisibility.SelectedIndex;

            serverData.GameMode = 0; //replaced with details from save / new game
            serverData.Difficulty = 0; //replaced with details from save / new game
            serverData.TimePassed = "N/A"; //replaced with details from save, or persisted if new game (will be updated in lobby server update cycle)

            serverData.CurrentPlayers = 0;
            serverData.MaxPlayers = (int)maxPlayers.value;

            // final check before we start the server
            var requiredMods = ModCompatibilityManager.Instance.GetLocalMods();
            if (requiredMods == null)
            {
                incompatibleMods = true;
                ValidateInputs(null);
                return;
            }

            serverData.RequiredMods = requiredMods;
            serverData.GameVersion = MainMenuControllerPatch.MenuProvider.BuildVersionString;
            serverData.MultiplayerVersion = Multiplayer.Ver;

            serverData.ServerDetails = details.text.Trim();

            if (saveGame != null)
            {
                ISaveGameplayInfo saveGameplayInfo = this.userProvider.GetSaveGameplayInfo(this.saveGame);
                if (!saveGameplayInfo.IsCorrupt)
                {
                    serverData.TimePassed = (saveGameplayInfo.InGameDate != DateTime.MinValue) ? saveGameplayInfo.InGameTimePassed.ToString("d\\d\\ hh\\h\\ mm\\m\\ ss\\s") : "N/A";
                    serverData.Difficulty = LobbyServerData.GetDifficultyFromString(this.userProvider.GetSessionDifficulty(saveGame.ParentSession).Name);
                    serverData.GameMode = LobbyServerData.GetGameModeFromString(saveGame.GameMode);
                }
            }
            else if (startGameData != null)
            {
                serverData.Difficulty = LobbyServerData.GetDifficultyFromString(this.startGameData.difficulty.Name);
                serverData.GameMode = LobbyServerData.GetGameModeFromString(startGameData.session.GameMode);
            }

            Multiplayer.Settings.ServerName = serverData.Name;
            Multiplayer.Settings.Password = password.text;
            Multiplayer.Settings.Visibility = serverData.Visibility;
            Multiplayer.Settings.Port = serverData.port;
            Multiplayer.Settings.MaxPlayers = serverData.MaxPlayers;
            Multiplayer.Settings.Details = serverData.ServerDetails;

            // Pass the server data to the NetworkLifecycle manager
            NetworkLifecycle.Instance.serverData = serverData;
        }

        // Mark it as a real multiplayer game
        NetworkLifecycle.Instance.IsSinglePlayer = false;

        var ContinueGameRequested = lcInstance.GetType().GetMethod("OnRunClicked", BindingFlags.NonPublic | BindingFlags.Instance);

        //Multiplayer.Log($"OnRunClicked exists: {ContinueGameRequested != null}");
        ContinueGameRequested?.Invoke(lcInstance, null);
    }
    #endregion


}
