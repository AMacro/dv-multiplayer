using DV.Common;
using DV.Localization;
using DV.Platform.Steam;
using DV.UI.PresetEditors;
using DV.UI;
using DV.UIFramework;
using DV;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Util;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using System.Linq;
using System.Reflection;
using System;
using TMPro;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;
using Multiplayer.API;

namespace Multiplayer.Components.MainMenu;

public class HostGamePane : MonoBehaviour
{
    private const int MAX_SERVER_NAME_LEN = 25;
    private const int MAX_PORT_LEN = 5;
    private const int MAX_DETAILS_LEN = 500;

    private const int MIN_PORT = 1024;
    private const int MAX_PORT = 49151;
    private const int MIN_PLAYERS = 2;
    private const int MAX_PLAYERS = 10;

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

        GameObject dividerPrefab = goMMC.FindChildByName("Divider");
        if (dividerPrefab == null)
        {
            Multiplayer.LogError("Divider not found!");
            return;
        }

        GameObject cbPrefab = goMMC.FindChildByName("CheckboxFreeCam");
        if (cbPrefab == null)
        {
            Multiplayer.LogError("CheckboxFreeCam not found!");
            return;
        }

        GameObject selectorPrefab = goMMC.FindChildByName("Crosshair").gameObject;
        if (selectorPrefab == null)
        {
            Multiplayer.LogError("selectorPrefab not found!");
            return;
        }

        GameObject sliderPrefab = goMMC.FindChildByName("Field Of View").gameObject;
        if (sliderPrefab == null)
        {
            Multiplayer.LogError("Field Of View not found!");
            return;
        }

        GameObject inputPrefab = MainMenuThingsAndStuff.Instance.references.popupTextInput.gameObject.FindChildByName("TextFieldTextIcon");
        if (inputPrefab == null)
        {
            Multiplayer.LogError("TextFieldTextIcon not found!");
            return;
        }


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
        GameObject go = GameObject.Instantiate(inputPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta).transform, false);
        go.name = "Server Name";
        serverName = go.GetComponent<TMP_InputField>();
        serverName.text = Multiplayer.Settings.ServerName?.Trim().Substring(0, Mathf.Min(Multiplayer.Settings.ServerName.Trim().Length, MAX_SERVER_NAME_LEN));
        serverName.placeholder.GetComponent<TMP_Text>().text = Locale.SERVER_HOST_NAME;
        serverName.characterLimit = MAX_SERVER_NAME_LEN;
        go.AddComponent<UIElementTooltip>();
        go.ResetTooltip();

        /*
         *  Server password field 
         */
        go = GameObject.Instantiate(inputPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta).transform, false);
        go.name = "Password";
        password = go.GetComponent<TMP_InputField>();
        password.text = Multiplayer.Settings.Password;
        //password.contentType = TMP_InputField.ContentType.Password; //re-introduce later when code for toggling has been implemented
        password.placeholder.GetComponent<TMP_Text>().text = Locale.SERVER_HOST_PASSWORD;
        go.AddComponent<UIElementTooltip>();//.enabledKey = Locale.SERVER_HOST_PASSWORD__TOOLTIP_KEY;
        go.ResetTooltip();

        /*
         *  Server visibility field 
         */
        selectorPrefab.SetActive(false);
        go = GameObject.Instantiate(selectorPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta).transform, false);
        selectorPrefab.SetActive(true);
        gameVisibility = go.GetOrAddComponent<Selector>();

        //clean-up

        if (gameVisibility.labelTMPro?.gameObject.TryGetComponent<I2.Loc.Localize>(out var loc) ?? false)
            GameObject.DestroyImmediate(loc);
        if (gameVisibility.labelTMPro?.gameObject.TryGetComponent<DV.Localization.Localize>(out var loc2) ?? false)
            GameObject.DestroyImmediate(loc2);

        DestroyImmediate(go.GetComponent<SettingChangeSource>());

        go.name = "Visibility";
        gameVisibility.initialized = false;

        gameVisibility.LocalizedLabel = true;
        gameVisibility.SetLabel(Locale.SERVER_HOST_VISIBILITY_KEY);
        gameVisibility.labelTMPro.GetComponent<Localize>().key = Locale.SERVER_HOST_VISIBILITY_KEY;

        gameVisibility.LocalizedValues = true;
        gameVisibility.SetValues(Locale.SERVER_HOST_VISIBILITY_MODES.ToList());
        gameVisibility.SetSelectedIndex(3);

        go.SetActive(true);
        go.ResetTooltip();

        gameVisibility.ToggleInteractable(true);

        /*
         *  Server details field 
         */
        go = GameObject.Instantiate(inputPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta, 106).transform, false);
        go.name = "Details";
        go.transform.GetComponent<RectTransform>().sizeDelta = new Vector2(go.transform.GetComponent<RectTransform>().sizeDelta.x, 106);
        details = go.GetComponent<TMP_InputField>();
        details.characterLimit = MAX_DETAILS_LEN;
        details.lineType = TMP_InputField.LineType.MultiLineNewline;
        details.FindChildByName("text [noloc]").GetComponent<TMP_Text>().alignment = TextAlignmentOptions.TopLeft;
        details.placeholder.GetComponent<TMP_Text>().text = Locale.SERVER_HOST_DETAILS;

        //Divider
        go = GameObject.Instantiate(dividerPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta).transform, false);
        go.name = "Divider";

        /*
         *  Server max players field 
         */
        sliderPrefab.SetActive(false);
        go = GameObject.Instantiate(sliderPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta).transform, false);
        sliderPrefab.SetActive(true);
        maxPlayers = go.GetComponent<SliderDV>();

        go.name = "Max Players";
        var labelGo = go.FindChildByName("[text label]");

        if (labelGo?.gameObject.TryGetComponent<I2.Loc.Localize>(out loc) ?? false)
            GameObject.DestroyImmediate(loc);

        DestroyImmediate(go.GetComponent<SettingChangeSource>());

        labelGo.GetComponent<Localize>().key = Locale.SERVER_HOST_MAX_PLAYERS_KEY;
        go.ResetTooltip();
        //labelGo.GetComponent<Localize>().UpdateLocalization();

        maxPlayers.stepIncrement = 1;
        maxPlayers.minValue = MIN_PLAYERS;
        maxPlayers.maxValue = MAX_PLAYERS;
        maxPlayers.value = Mathf.Clamp(Multiplayer.Settings.MaxPlayers, MIN_PLAYERS, MAX_PLAYERS);
        go.SetActive(true);
        maxPlayers.interactable = true;

        /*
         *  Server port field 
         */
        go = GameObject.Instantiate(inputPrefab, NewContentGroup(controls, scroller.viewport.sizeDelta).transform, false);
        go.name = "Port";
        port = go.GetComponent<TMP_InputField>();
        port.characterValidation = TMP_InputField.CharacterValidation.Integer;
        port.characterLimit = MAX_PORT_LEN;
        port.placeholder.GetComponent<TMP_Text>().text = DEFAULT_PORT.ToString();
        port.text = (Multiplayer.Settings.Port >= MIN_PORT && Multiplayer.Settings.Port <= MAX_PORT) ? Multiplayer.Settings.Port.ToString() : DEFAULT_PORT.ToString();

        /*
         *  Start Game button
         */
        go = this.gameObject.UpdateButton("ButtonTextIcon Save", "ButtonTextIcon Start", Locale.SERVER_HOST_START_KEY, null, playSprite);
        go.FindChildByName("[text]").GetComponent<Localize>().UpdateLocalization();

        startButton = go.GetComponent<ButtonDV>();
        startButton.onClick.RemoveAllListeners();
        startButton.onClick.AddListener(OnStartClick);
    }

    private GameObject NewContentGroup(GameObject parent, Vector2 sizeDelta, int cellMaxHeight = 53)
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

        return contentGroup;
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
            string requiredMods = ModCompatibilityManager.Instance.GetRequiredMods();
            if (requiredMods == null)
            {
                
                incompatibleMods = true;
                ValidateInputs(null);
                return;
            }

            serverData.RequiredMods = requiredMods;
            serverData.GameVersion = Multiplayer.LocalBuildInfo;
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


            //Pass the server data to the NetworkLifecycle manager
            NetworkLifecycle.Instance.serverData = serverData;
        }
        //Mark it as a real multiplayer game
        NetworkLifecycle.Instance.IsSinglePlayer = false;


        var ContinueGameRequested = lcInstance.GetType().GetMethod("OnRunClicked", BindingFlags.NonPublic | BindingFlags.Instance);

        //Multiplayer.Log($"OnRunClicked exists: {ContinueGameRequested != null}");
        ContinueGameRequested?.Invoke(lcInstance, null);
    }
    #endregion


}
