using System;
using System.Collections;
using System.Text.RegularExpressions;
using DV.Localization;
using DV.UI;
using DV.UIFramework;
using DV.Util;
using DV.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Linq;
using Multiplayer.Networking.Data;
using DV;
using Multiplayer.Components.Networking.UI;



namespace Multiplayer.Components.MainMenu
{
    public class ServerBrowserPane : MonoBehaviour
    {
        // Regular expressions for IP and port validation
        // @formatter:off
        // Patterns from https://ihateregex.io/
        private static readonly Regex IPv4Regex = new Regex(@"(\b25[0-5]|\b2[0-4][0-9]|\b[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}");
        private static readonly Regex IPv6Regex = new Regex(@"(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])|([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9]))");
        private static readonly Regex PortRegex = new Regex(@"^((6553[0-5])|(655[0-2][0-9])|(65[0-4][0-9]{2})|(6[0-4][0-9]{3})|([1-5][0-9]{4})|([0-5]{0,5})|([0-9]{1,4}))$");
        // @formatter:on

        private const int MAX_PORT_LEN = 5;
        private const int MIN_PORT = 1024;
        private const int MAX_PORT = 49151;

        //Gridview variables
        private ObservableCollectionExt<IServerBrowserGameDetails> gridViewModel = new ObservableCollectionExt<IServerBrowserGameDetails>();
        private ServerBrowserGridView gridView;
        private ScrollRect parentScroller;
        private string serverIDOnRefresh;
        private IServerBrowserGameDetails selectedServer;

        //Button variables
        private ButtonDV buttonJoin;
        private ButtonDV buttonRefresh;
        private ButtonDV buttonDirectIP;

        //Misc GUI Elements
        private TextMeshProUGUI serverName;
        private TextMeshProUGUI detailsPane;
        private ScrollRect serverInfo;


        private bool serverRefreshing = false;
        private bool autoRefresh = false;
        private float timePassed = 0f; //time since last refresh
        private const int AUTO_REFRESH_TIME = 30; //how often to refresh in auto
        private const int REFRESH_MIN_TIME = 10; //Stop refresh spam

        //connection parameters
        private string ipAddress;
        private int portNumber;
        string password = null;
        bool direct = false;

        private string[] testNames = new string[] { "ChooChooExpress", "RailwayRascals", "FreightFrenzy", "SteamDream", "DieselDynasty", "CargoKings", "TrackMasters", "RailwayRevolution", "ExpressElders", "IronHorseHeroes", "LocomotiveLegends", "TrainTitans", "HeavyHaulers", "RapidRails", "TimberlineTransport", "CoalCountry", "SilverRailway", "GoldenGauge", "SteelStream", "MountainMoguls", "RailRiders", "TrackTrailblazers", "FreightFanatics", "SteamSensation", "DieselDaredevils", "CargoChampions", "TrackTacticians", "RailwayRoyals", "ExpressExperts", "IronHorseInnovators", "LocomotiveLeaders", "TrainTacticians", "HeavyHitters", "RapidRunners", "TimberlineTrains", "CoalCrushers", "SilverStreamliners", "GoldenGears", "SteelSurge", "MountainMovers", "RailwayWarriors", "TrackTerminators", "FreightFighters", "SteamStreak", "DieselDynamos", "CargoCommanders", "TrackTrailblazers", "RailwayRangers", "ExpressEngineers", "IronHorseInnovators", "LocomotiveLovers", "TrainTrailblazers", "HeavyHaulersHub", "RapidRailsRacers", "TimberlineTrackers", "CoalCountryCarriers", "SilverSpeedsters", "GoldenGaugeGang", "SteelStalwarts", "MountainMoversClub", "RailRunners", "TrackTitans", "FreightFalcons", "SteamSprinters", "DieselDukes", "CargoCommandos", "TrackTracers", "RailwayRebels", "ExpressElite", "IronHorseIcons", "LocomotiveLunatics", "TrainTornadoes", "HeavyHaulersCrew", "RapidRailsRunners", "TimberlineTrackMasters", "CoalCountryCrew", "SilverSprinters", "GoldenGale", "SteelSpeedsters", "MountainMarauders", "RailwayRiders", "TrackTactics", "FreightFury", "SteamSquires", "DieselDefenders", "CargoCrusaders", "TrackTechnicians", "RailwayRaiders", "ExpressEnthusiasts", "IronHorseIlluminati", "LocomotiveLoyalists", "TrainTurbulence", "HeavyHaulersHeroes", "RapidRailsRiders", "TimberlineTrackTitans", "CoalCountryCaravans", "SilverSpeedRacers", "GoldenGaugeGangsters", "SteelStorm", "MountainMasters", "RailwayRoadrunners", "TrackTerror", "FreightFleets", "SteamSurgeons", "DieselDragons", "CargoCrushers", "TrackTaskmasters", "RailwayRevolutionaries", "ExpressExplorers", "IronHorseInquisitors", "LocomotiveLegion", "TrainTriumph", "HeavyHaulersHorde", "RapidRailsRenegades", "TimberlineTrackTeam", "CoalCountryCrusade", "SilverSprintersSquad", "GoldenGaugeGroup", "SteelStrike", "MountainMonarchs", "RailwayRaid", "TrackTacticiansTeam", "FreightForce", "SteamSquad", "DieselDynastyClan", "CargoCrew", "TrackTeam", "RailwayRalliers", "ExpressExpedition", "IronHorseInitiative", "LocomotiveLeague", "TrainTribe", "HeavyHaulersHustle", "RapidRailsRevolution", "TimberlineTrackersTeam", "CoalCountryConvoy", "SilverSprint", "GoldenGaugeGuild", "SteelSpirits", "MountainMayhem", "RailwayRaidersCrew", "TrackTrailblazersTribe", "FreightFleetForce", "SteamStalwarts", "DieselDragonsDen", "CargoCaptains", "TrackTrailblazersTeam", "RailwayRidersRevolution", "ExpressEliteExpedition", "IronHorseInsiders", "LocomotiveLords", "TrainTacticiansTribe", "HeavyHaulersHeroesHorde", "RapidRailsRacersTeam", "TimberlineTrackMastersTeam", "CoalCountryCarriersCrew", "SilverSpeedstersSprint", "GoldenGaugeGangGuild", "SteelSurgeStrike", "MountainMoversMonarchs" };

        #region setup

        private void Awake()
        {
            Multiplayer.Log("MultiplayerPane Awake()");
            /*
             * 
             * Temp testing code
             * 
             */

            //GameObject chat = new GameObject("ChatUI", typeof(ChatGUI));
            //chat.transform.SetParent(GameObject.Find("MenuOpeningScene").transform,false);

            //////////Debug.Log("Instantiating Overlay");
            //////////GameObject overlay = new GameObject("Overlay", typeof(ChatGUI));
            //////////GameObject parent = GameObject.Find("MenuOpeningScene");
            //////////if (parent != null)
            //////////{
            //////////    overlay.transform.SetParent(parent.transform, false);
            //////////    Debug.Log("Overlay parent set to MenuOpeningScene");
            //////////}
            //////////else
            //////////{
            //////////    Debug.LogError("MenuOpeningScene not found");
            //////////}

            //////////Debug.Log("Overlay instantiated with components:");
            //////////foreach (Transform child in overlay.transform)
            //////////{
            //////////    Debug.Log("Child: " + child.name);
            //////////    foreach (Transform grandChild in child)
            //////////    {
            //////////        Debug.Log("GrandChild: " + grandChild.name);
            //////////    }
            //////////}

            /*
             * 
             * End Temp testing code
             * 
             */
            CleanUI();
            BuildUI();

            SetupServerBrowser();
            FillDummyServers();

        }

        private void OnEnable()
        {
            Multiplayer.Log("MultiplayerPane OnEnable()");
            if (!this.parentScroller)
            {
                Multiplayer.Log("Find ScrollRect");
                this.parentScroller = this.gridView.GetComponentInParent<ScrollRect>();
                Multiplayer.Log("Found ScrollRect");
            }
            this.SetupListeners(true);
            this.serverIDOnRefresh = "";

            buttonDirectIP.ToggleInteractable(true);
            buttonRefresh.ToggleInteractable(true);
        }

        // Disable listeners
        private void OnDisable()
        {
            this.SetupListeners(false);
        }

        private void Update()
        {
            
            timePassed += Time.deltaTime;

            if (autoRefresh && !serverRefreshing)
            {              
                if (timePassed >= AUTO_REFRESH_TIME)
                {
                    RefreshAction();
                }
                else if(timePassed >= REFRESH_MIN_TIME)
                {
                    buttonRefresh.ToggleInteractable(true);
                }
            }
        }

        private void CleanUI()
        {
            GameObject.Destroy(this.FindChildByName("Text Content"));

            GameObject.Destroy(this.FindChildByName("HardcoreSavingBanner"));
            GameObject.Destroy(this.FindChildByName("TutorialSavingBanner"));

            GameObject.Destroy(this.FindChildByName("Thumbnail"));

            GameObject.Destroy(this.FindChildByName("ButtonIcon OpenFolder"));
            GameObject.Destroy(this.FindChildByName("ButtonIcon Rename"));
            GameObject.Destroy(this.FindChildByName("ButtonTextIcon Load"));

        }
        private void BuildUI()
        {

            // Update title
            GameObject titleObj = this.FindChildByName("Title");
            GameObject.Destroy(titleObj.GetComponentInChildren<I2.Loc.Localize>());
            titleObj.GetComponentInChildren<Localize>().key = Locale.SERVER_BROWSER__TITLE_KEY;
            titleObj.GetComponentInChildren<Localize>().UpdateLocalization();

            //Rebuild the save description pane
            GameObject serverWindowGO = this.FindChildByName("Save Description");
            GameObject serverNameGO = serverWindowGO.FindChildByName("text list [noloc]");
            GameObject scrollViewGO = this.FindChildByName("Scroll View");

            //Create new objects
            GameObject serverScroll = Instantiate(scrollViewGO, serverNameGO.transform.position, Quaternion.identity, serverWindowGO.transform);
            

            /* 
             * Setup server name 
             */
            serverNameGO.name = "Server Title";

            //Positioning
            RectTransform serverNameRT = serverNameGO.GetComponent<RectTransform>();
            serverNameRT.pivot = new Vector2(1f, 1f);
            serverNameRT.anchorMin = new Vector2(0f, 1f);
            serverNameRT.anchorMax = new Vector2(1f, 1f);
            serverNameRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0, 54);

            //Text
            serverName = serverNameGO.GetComponentInChildren<TextMeshProUGUI>();
            serverName.alignment = TextAlignmentOptions.Center;
            serverName.textWrappingMode = TextWrappingModes.Normal;
            serverName.fontSize = 22;
            serverName.text = "Server Browser Info";

            /* 
             * Setup server details
             */

            // Create new ScrollRect object
            GameObject viewport = serverScroll.FindChildByName("Viewport");
            serverScroll.transform.SetParent(serverWindowGO.transform, false);

            // Positioning ScrollRect
            RectTransform serverScrollRT = serverScroll.GetComponent<RectTransform>();
            serverScrollRT.pivot = new Vector2(1f, 1f);
            serverScrollRT.anchorMin = new Vector2(0f, 1f);
            serverScrollRT.anchorMax = new Vector2(1f, 1f);
            serverScrollRT.localEulerAngles = Vector3.zero;
            serverScrollRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 54, 400);
            serverScrollRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, serverNameGO.GetComponent<RectTransform>().rect.width);

            RectTransform viewportRT = viewport.GetComponent<RectTransform>();

            // Assign Viewport to ScrollRect
            ScrollRect scrollRect = serverScroll.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRT;

            // Create Content
            GameObject.Destroy(serverScroll.FindChildByName("GRID VIEW").gameObject);
            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            content.transform.SetParent(viewport.transform, false);
            ContentSizeFitter contentSF = content.GetComponent<ContentSizeFitter>();
            contentSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            VerticalLayoutGroup contentVLG = content.GetComponent<VerticalLayoutGroup>();
            contentVLG.childControlWidth = true;
            contentVLG.childControlHeight = true;
            RectTransform contentRT = content.GetComponent<RectTransform>();
            contentRT.pivot = new Vector2(0f, 1f);
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            scrollRect.content = contentRT;

            // Create TextMeshProUGUI object
            GameObject textContainerGO = new GameObject("Details Container", typeof(HorizontalLayoutGroup));
            textContainerGO.transform.SetParent(content.transform, false);
            contentRT.localPosition = new Vector3(contentRT.localPosition.x + 10, contentRT.localPosition.y, contentRT.localPosition.z);


            GameObject textGO = new GameObject("Details Text", typeof(TextMeshProUGUI));
            textGO.transform.SetParent(textContainerGO.transform, false);
            HorizontalLayoutGroup textHLG = textGO.GetComponent<HorizontalLayoutGroup>();
            detailsPane = textGO.GetComponent<TextMeshProUGUI>();
            detailsPane.textWrappingMode = TextWrappingModes.Normal;
            detailsPane.fontSize = 18;
            detailsPane.text = "Dummy servers are shown for demonstration purposes only.<br><br>Press refresh to attempt loading real servers.<br>After pressing refresh, auto refresh will occur every 30 seconds.";

            // Adjust text RectTransform to fit content
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.pivot = new Vector2(0.5f, 1f);
            textRT.anchorMin = new Vector2(0, 1);
            textRT.anchorMax = new Vector2(1, 1);
            textRT.offsetMin = new Vector2(0, -detailsPane.preferredHeight);
            textRT.offsetMax = new Vector2(0, 0);

            // Set content size to fit text
            contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x -50, detailsPane.preferredHeight);

            // Update buttons on the multiplayer pane
            GameObject goDirectIP = this.gameObject.UpdateButton("ButtonTextIcon Overwrite", "ButtonTextIcon Manual", Locale.SERVER_BROWSER__MANUAL_CONNECT_KEY, null, Multiplayer.AssetIndex.multiplayerIcon);
            GameObject goJoin = this.gameObject.UpdateButton("ButtonTextIcon Save", "ButtonTextIcon Join", Locale.SERVER_BROWSER__JOIN_KEY, null, Multiplayer.AssetIndex.connectIcon);
            GameObject goRefresh = this.gameObject.UpdateButton("ButtonIcon Delete", "ButtonIcon Refresh", Locale.SERVER_BROWSER__REFRESH_KEY, null, Multiplayer.AssetIndex.refreshIcon);


            if (goDirectIP == null || goJoin == null || goRefresh == null)
            {
                Multiplayer.LogError("One or more buttons not found.");
                return;
            }

            // Set up event listeners
            buttonDirectIP = goDirectIP.GetComponent<ButtonDV>();
            buttonDirectIP.onClick.AddListener(DirectAction);

            buttonJoin = goJoin.GetComponent<ButtonDV>();
            buttonJoin.onClick.AddListener(JoinAction);

            buttonRefresh = goRefresh.GetComponent<ButtonDV>();
            buttonRefresh.onClick.AddListener(RefreshAction);

            //Lock out the join button until a server has been selected
            buttonJoin.ToggleInteractable(false);
        }
        private void SetupServerBrowser()
        {
            GameObject GridviewGO = this.FindChildByName("Scroll View").FindChildByName("GRID VIEW");
            SaveLoadGridView slgv = GridviewGO.GetComponent<SaveLoadGridView>();

            GridviewGO.SetActive(false);

            gridView = GridviewGO.AddComponent<ServerBrowserGridView>();
            slgv.viewElementPrefab.SetActive(false);
            gridView.viewElementPrefab = Instantiate(slgv.viewElementPrefab);
            

            GameObject.Destroy(slgv);

            GridviewGO.SetActive(true);
        }
        private void SetupListeners(bool on)
        {
            if (on)
            {
                this.gridView.SelectedIndexChanged += this.IndexChanged;
            }
            else
            {
                this.gridView.SelectedIndexChanged -= this.IndexChanged;
            }

        }

        #endregion

        #region UI callbacks
        private void RefreshAction()
        {
            if (serverRefreshing)
                return;

            

            if (selectedServer != null)
            {
                serverIDOnRefresh = selectedServer.id;
            }

            serverRefreshing = true;
            autoRefresh = true;
            buttonJoin.ToggleInteractable(false);
            buttonRefresh.ToggleInteractable(false);

            StartCoroutine(GetRequest($"{Multiplayer.Settings.LobbyServerAddress}/list_game_servers"));

        }
        private void JoinAction()
        {
            if (selectedServer != null)
            {
                buttonDirectIP.ToggleInteractable(false);
                buttonJoin.ToggleInteractable(false);

                if (selectedServer.HasPassword)
                {
                    //not making a direct connection
                    direct = false;
                    ipAddress = selectedServer.ip;
                    portNumber = selectedServer.port;

                    ShowPasswordPopup();

                    return;
                }

                //No password, just connect
                SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(selectedServer.ip, selectedServer.port, null, false);
            }
        }

        private void DirectAction()
        {
            Debug.Log($"DirectAction()");
            buttonDirectIP.ToggleInteractable(false);
            buttonJoin.ToggleInteractable(false)    ;

            //making a direct connection
            direct = true;

            ShowIpPopup();
        }

        private void IndexChanged(AGridView<IServerBrowserGameDetails> gridView)
        {
            Debug.Log($"Index: {gridView.SelectedModelIndex}");
            if (serverRefreshing)
                return;

            if (gridView.SelectedModelIndex >= 0)
            {
                Debug.Log($"Selected server: {gridViewModel[gridView.SelectedModelIndex].Name}");

                selectedServer = gridViewModel[gridView.SelectedModelIndex];
                
                UpdateDetailsPane();

                //Check if we can connect to this server

                Debug.Log($"server: \"{selectedServer.GameVersion}\" \"{selectedServer.MultiplayerVersion}\"");
                Debug.Log($"client: \"{BuildInfo.BUILD_VERSION_MAJOR.ToString()}\" \"{Multiplayer.ModEntry.Version.ToString()}\"");
                Debug.Log($"result: \"{selectedServer.GameVersion == BuildInfo.BUILD_VERSION_MAJOR.ToString()}\" \"{selectedServer.MultiplayerVersion == Multiplayer.ModEntry.Version.ToString()}\"");

                bool canConnect = selectedServer.GameVersion == BuildInfo.BUILD_VERSION_MAJOR.ToString() &&
                                  selectedServer.MultiplayerVersion == Multiplayer.ModEntry.Version.ToString();

                buttonJoin.ToggleInteractable(canConnect);
            }
            else
            {
                buttonJoin.ToggleInteractable(false);
            }
        }

        #endregion

        private void UpdateDetailsPane()
        {
            string details="";

            if (selectedServer != null)
            {
                Debug.Log("Prepping Data");
                serverName.text = selectedServer.Name;

                //note: built-in localisations have a trailing colon e.g. 'Game mode:'

                details  = "<alpha=#50>" + LocalizationAPI.L("launcher/game_mode", Array.Empty<string>()) + "</color> " + LobbyServerData.GetGameModeFromInt(selectedServer.GameMode) + "<br>";
                details += "<alpha=#50>" + LocalizationAPI.L("launcher/difficulty", Array.Empty<string>()) + "</color> " + LobbyServerData.GetDifficultyFromInt(selectedServer.Difficulty) + "<br>";
                details += "<alpha=#50>" + LocalizationAPI.L("launcher/in_game_time_passed", Array.Empty<string>()) + "</color> " + selectedServer.TimePassed + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__PLAYERS + ":</color> " + selectedServer.CurrentPlayers + '/' + selectedServer.MaxPlayers + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__PASSWORD_REQUIRED + ":</color> " + (selectedServer.HasPassword ? Locale.SERVER_BROWSER__YES : Locale.SERVER_BROWSER__NO) + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__MODS_REQUIRED + ":</color> " + (selectedServer.RequiredMods != null? Locale.SERVER_BROWSER__YES : Locale.SERVER_BROWSER__NO) + "<br>";
                details += "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__GAME_VERSION + ":</color> " + (selectedServer.GameVersion != BuildInfo.BUILD_VERSION_MAJOR.ToString() ? "<color=\"red\">" : "") + selectedServer.GameVersion + "</color><br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__MOD_VERSION + ":</color> " + (selectedServer.MultiplayerVersion != Multiplayer.ModEntry.Version.ToString() ? "<color=\"red\">" : "") + selectedServer.MultiplayerVersion + "</color><br>";
                details += "<br>";
                details += selectedServer.ServerDetails;

                Debug.Log("Finished Prepping Data");
                detailsPane.text = details;
            }
        }

        private void ShowIpPopup()
        {
            Debug.Log("In ShowIpPpopup");
            var popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
            if (popup == null)
            {
                Multiplayer.LogError("Popup not found.");
                return;
            }

            popup.labelTMPro.text = Locale.SERVER_BROWSER__IP;
            popup.GetComponentInChildren<TMP_InputField>().text = Multiplayer.Settings.LastRemoteIP;

            popup.Closed += result =>
            {
                if (result.closedBy == PopupClosedByAction.Abortion)
                {
                    buttonDirectIP.ToggleInteractable(true);
                    IndexChanged(gridView); //re-enable the join button if a valid gridview item is selected
                    return;
                }

                if (!IPv4Regex.IsMatch(result.data) && !IPv6Regex.IsMatch(result.data))
                {
                    ShowOkPopup(Locale.SERVER_BROWSER__IP_INVALID, ShowIpPopup);
                }
                else
                {
                    ipAddress = result.data;
                    ShowPortPopup();
                }
            };
        }

        private void ShowPortPopup()
        {

            var popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
            if (popup == null)
            {
                Multiplayer.LogError("Popup not found.");
                return;
            }

            popup.labelTMPro.text = Locale.SERVER_BROWSER__PORT;
            popup.GetComponentInChildren<TMP_InputField>().text = $"{Multiplayer.Settings.LastRemotePort}";
            popup.GetComponentInChildren<TMP_InputField>().contentType = TMP_InputField.ContentType.IntegerNumber;
            popup.GetComponentInChildren<TMP_InputField>().characterLimit = MAX_PORT_LEN;

            popup.Closed += result =>
            {
                if (result.closedBy == PopupClosedByAction.Abortion)
                {
                    buttonDirectIP.ToggleInteractable(true);
                    return;
                }

                if (!PortRegex.IsMatch(result.data))
                {
                    ShowOkPopup(Locale.SERVER_BROWSER__PORT_INVALID, ShowIpPopup);
                }
                else
                {
                    portNumber = ushort.Parse(result.data);
                    ShowPasswordPopup();
                }
            };

        }

        private void ShowPasswordPopup()
        {
            var popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
            if (popup == null)
            {
                Multiplayer.LogError("Popup not found.");
                return;
            }

            popup.labelTMPro.text = Locale.SERVER_BROWSER__PASSWORD;

            //direct IP connection
            if (direct)
            {
                //Prefill with stored password
                popup.GetComponentInChildren<TMP_InputField>().text = Multiplayer.Settings.LastRemotePassword;

                //Set us up to allow a blank password
                DestroyImmediate(popup.GetComponentInChildren<PopupTextInputFieldController>());
                popup.GetOrAddComponent<PopupTextInputFieldControllerNoValidation>();
             }

            popup.Closed += result =>
            {
                if (result.closedBy == PopupClosedByAction.Abortion)
                {
                    buttonDirectIP.ToggleInteractable(true);
                    return;
                }

                if (direct)
                {
                    //store params for later
                    Multiplayer.Settings.LastRemoteIP = ipAddress;
                    Multiplayer.Settings.LastRemotePort = portNumber;
                    Multiplayer.Settings.LastRemotePassword = result.data;

                }

                SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(ipAddress, portNumber, result.data, false);

                //ShowConnectingPopup(); // Show a connecting message
                //SingletonBehaviour<NetworkLifecycle>.Instance.ConnectionFailed += HandleConnectionFailed;
                //SingletonBehaviour<NetworkLifecycle>.Instance.ConnectionEstablished += HandleConnectionEstablished;
            };
        }

        // Example of handling connection success
        private void HandleConnectionEstablished()
        {
            // Connection established, handle the UI or game state accordingly
            Debug.Log("Connection established!");
            // HideConnectingPopup(); // Hide the connecting message
        }

        // Example of handling connection failure
        private void HandleConnectionFailed()
        {
            // Connection failed, show an error message or handle the failure scenario
            Debug.LogError("Connection failed!");
            // ShowConnectionFailedPopup();
        }

        IEnumerator GetRequest(string uri)
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                // Request and wait for the desired page.
                yield return webRequest.SendWebRequest();

                string[] pages = uri.Split('/');
                int page = pages.Length - 1;

                if (webRequest.isNetworkError)
                {
                    Debug.Log(pages[page] + ": Error: " + webRequest.error);
                }
                else
                {
                    Debug.Log(pages[page] + ":\nReceived: " + webRequest.downloadHandler.text);

                    LobbyServerData[] response;

                    response = Newtonsoft.Json.JsonConvert.DeserializeObject<LobbyServerData[]>(webRequest.downloadHandler.text);

                    Debug.Log($"servers: {response.Length}");

                    foreach (LobbyServerData server in response)
                    {
                        Debug.Log($"Name: {server.Name}\tIP: {server.ip}");
                    }

                    if (response.Length == 0)
                    {
                        gridView.showDummyElement = true;
                        buttonJoin.ToggleInteractable(false);
                    }
                    else
                    {
                        gridView.showDummyElement = false;
                    }
                    gridViewModel.Clear();
                    gridView.SetModel(gridViewModel);
                    gridViewModel.AddRange(response);

                    //if we have a server selected, we need to re-select it after refresh
                    if (serverIDOnRefresh != null)
                    {
                        int selID = Array.FindIndex(gridViewModel.ToArray(), server => server.id == serverIDOnRefresh);
                        if (selID >= 0)
                        {
                            gridView.SetSelected(selID);

                            if (this.parentScroller)
                            {
                                this.parentScroller.verticalNormalizedPosition = 1f - (float)selID / (float)gridView.Model.Count;
                            }
                        }

                        serverIDOnRefresh = null;
                    }

                    
                }
            }

            serverRefreshing = false;
            timePassed = 0;
        }

        private static void ShowOkPopup(string text, Action onClick)
        {
            var popup = MainMenuThingsAndStuff.Instance.ShowOkPopup();
            if (popup == null) return;

            popup.labelTMPro.text = text;
            popup.Closed += _ => onClick();
        }

        private void SetButtonsActive(params GameObject[] buttons)
        {
            foreach (var button in buttons)
            {
                button.SetActive(true);
            }
        }

        private void FillDummyServers()
        {
            gridView.showDummyElement = false;
            gridViewModel.Clear();


            IServerBrowserGameDetails item = null;

            for (int i = 0; i < UnityEngine.Random.Range(1, 50); i++)
            {

                item = new LobbyServerData();
                item.Name = testNames[UnityEngine.Random.Range(0, testNames.Length - 1)];
                item.MaxPlayers = UnityEngine.Random.Range(1, 10);
                item.CurrentPlayers = UnityEngine.Random.Range(1, item.MaxPlayers);
                item.Ping = UnityEngine.Random.Range(5, 1500);
                item.HasPassword = UnityEngine.Random.Range(0, 10) > 5;

                item.GameVersion = UnityEngine.Random.Range(1, 10) > 3 ? BuildInfo.BUILD_VERSION_MAJOR.ToString() : "97";
                item.MultiplayerVersion = UnityEngine.Random.Range(1, 10) > 3 ? Multiplayer.ModEntry.Version.ToString() : "0.1.0";


                Debug.Log(item.HasPassword);
                gridViewModel.Add(item);
            }

            gridView.SetModel(gridViewModel);
        }
    }

   
}
