using System;
using System.Collections;
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
using System.Linq;
using Multiplayer.Networking.Data;
using DV;
using System.Net;
using LiteNetLib;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;

namespace Multiplayer.Components.MainMenu
{
    public class ServerBrowserPane : MonoBehaviour
    {
        private enum ConnectionState
        {
            NotConnected,
            AttemptingSteamRelay,
            AttemptingIPv6,
            AttemptingIPv6Punch,
            AttemptingIPv4,
            AttemptingIPv4Punch,
            Failed,
            Aborted
        }

        private const int MAX_PORT_LEN = 5;
        private const int MIN_PORT = 1024;
        private const int MAX_PORT = 49151;

        //Gridview variables
        private readonly ObservableCollectionExt<IServerBrowserGameDetails> gridViewModel = [];
        private ServerBrowserGridView gridView;
        private ScrollRect parentScroller;
        private string serverIDOnRefresh;
        private IServerBrowserGameDetails selectedServer;


        //ping tracking
        private float pingTimer = 0f;
        private const float PING_INTERVAL = 2f; // base interval to refresh all pings

        //Button variables
        private ButtonDV buttonJoin;
        private ButtonDV buttonRefresh;
        private ButtonDV buttonDirectIP;

        //Misc GUI Elements
        private TextMeshProUGUI serverName;
        private TextMeshProUGUI detailsPane;

        //Remote server tracking
        private readonly List<IServerBrowserGameDetails> remoteServers = [];
        private bool serverRefreshing = false;
        private float timePassed = 0f; //time since last refresh
        private const int AUTO_REFRESH_TIME = 30; //how often to refresh in auto
        private const int REFRESH_MIN_TIME = 10; //Stop refresh spam
        private bool remoteRefreshComplete;

        //connection parameters
        private string address;
        private int portNumber;
        private Lobby? selectedLobby;
        private static Lobby? joinedLobby;
        public static Lobby? lobbyToJoin;
        string password = null;
        bool direct = false;

        private ConnectionState connectionState = ConnectionState.NotConnected;
        private Popup connectingPopup;
        private int attempt;

        private Lobby[] lobbies;


        #region setup

        public void Awake()
        {
            Multiplayer.Log("MultiplayerPane Awake()");
            joinedLobby?.Leave();
            joinedLobby = null;

            CleanUI();
            BuildUI();

            SetupServerBrowser();
            RefreshGridView();
        }

        public void OnEnable()
        {
            //Multiplayer.Log("MultiplayerPane OnEnable()");
            if (!this.parentScroller)
            {
                //Multiplayer.Log("Find ScrollRect");
                this.parentScroller = this.gridView.GetComponentInParent<ScrollRect>();
                //Multiplayer.Log("Found ScrollRect");
            }
            this.SetupListeners(true);
            this.serverIDOnRefresh = "";

            buttonDirectIP.ToggleInteractable(true);
            buttonRefresh.ToggleInteractable(true);

            RefreshAction();
        }

        // Disable listeners
        public void OnDisable()
        {
            this.SetupListeners(false);
        }

        public void Update()
        {
            SteamClient.RunCallbacks();

            //Handle server refresh interval
            timePassed += Time.deltaTime;

            if (!serverRefreshing)
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
            else if(remoteRefreshComplete)
            {
                RefreshGridView();
                IndexChanged(gridView); //Revalidate any selected servers
                remoteRefreshComplete = false;
                serverRefreshing = false;
                timePassed = 0;
            }

            //Handle pinging servers
            pingTimer += Time.deltaTime;

            if (pingTimer >= PING_INTERVAL)
            {
                UpdatePings();
                pingTimer = 0f;
            }

            if (lobbyToJoin != null && lobbyToJoin?.Data?.Count() > 0)
            {
                //For invites
                Multiplayer.Log($"Player Invite initiated");
                if (lobbyToJoin != null)
                {
                    direct = false;
                    selectedLobby = lobbyToJoin;
                    lobbyToJoin = null;

                    string hasPass = selectedLobby?.GetData(SteamworksUtils.LOBBY_HAS_PASSWORD);
                    Multiplayer.Log($"Player Invite ({selectedLobby?.Id}) Has Password: {hasPass}");

                    if (string.IsNullOrEmpty(hasPass))
                    {
                        Multiplayer.Log($"Player Invite ({selectedLobby?.Id}) Attempting connection...");
                        AttemptConnection();
                    }
                    else
                    {
                        Multiplayer.Log($"Player Invite ({selectedLobby?.Id}) Ask Password...");
                        ShowPasswordPopup();
                    }
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
            serverName.text = Locale.SERVER_BROWSER__INFO_TITLE;// "Server Browser Info";

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
            GameObject content = new("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
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
            GameObject textContainerGO = new ("Details Container", typeof(HorizontalLayoutGroup));
            textContainerGO.transform.SetParent(content.transform, false);
            contentRT.localPosition = new Vector3(contentRT.localPosition.x + 10, contentRT.localPosition.y, contentRT.localPosition.z);


            GameObject textGO = new("Details Text", typeof(TextMeshProUGUI));
            textGO.transform.SetParent(textContainerGO.transform, false);
            HorizontalLayoutGroup textHLG = textGO.GetComponent<HorizontalLayoutGroup>();
            detailsPane = textGO.GetComponent<TextMeshProUGUI>();
            detailsPane.textWrappingMode = TextWrappingModes.Normal;
            detailsPane.fontSize = 18;
            detailsPane.text = Locale.Get(Locale.SERVER_BROWSER__INFO_CONTENT_KEY, [AUTO_REFRESH_TIME, REFRESH_MIN_TIME]);// "Welcome to Derail Valley Multiplayer Mod!<br><br>The server list refreshes automatically every 30 seconds, but you can refresh manually once every 10 seconds.";

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

            //Disable before we make any changes
            GridviewGO.SetActive(false);


            //load our custom controller
            SaveLoadGridView slgv = GridviewGO.GetComponent<SaveLoadGridView>();
            gridView = GridviewGO.AddComponent<ServerBrowserGridView>();

            //grab the original prefab
            slgv.viewElementPrefab.SetActive(false);
            gridView.viewElementPrefab = Instantiate(slgv.viewElementPrefab);
            slgv.viewElementPrefab.SetActive(true);

            //Remove original controller
            GameObject.Destroy(slgv);

            //Don't forget to re-enable!
            GridviewGO.SetActive(true);

            gridView.showDummyElement = true;
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
                serverIDOnRefresh = selectedServer.id;

            remoteServers.Clear();

            serverRefreshing = true;
            buttonJoin.ToggleInteractable(false);
            buttonRefresh.ToggleInteractable(false);

            if (DVSteamworks.Success)
                ListActiveLobbies();

        }
        private void JoinAction()
        {
            if (selectedServer != null)
            {
                buttonDirectIP.ToggleInteractable(false);
                buttonJoin.ToggleInteractable(false);

                //not making a direct connection
                direct = false;
                portNumber = -1;
                var lobby = GetLobbyFromServer(selectedServer);
                if (lobby != null)
                {
                    selectedLobby = (Lobby)lobby;
                    password = null; //clear the password

                    if (selectedServer.HasPassword)
                    {
                        ShowPasswordPopup();
                        return;
                    }

                    AttemptConnection();

                }
            }
        }

        private void DirectAction()
        {
            //Debug.Log($"DirectAction()");
            buttonDirectIP.ToggleInteractable(false);
            buttonJoin.ToggleInteractable(false)    ;

            //making a direct connection
            direct = true;
            password = null;

            //ShowSteamID();
            ShowIpPopup();
        }

        private void IndexChanged(AGridView<IServerBrowserGameDetails> gridView)
        {
            if (serverRefreshing)
                return;

            if (gridView.SelectedModelIndex >= 0)
            {
                selectedServer = gridViewModel[gridView.SelectedModelIndex];
                
                UpdateDetailsPane();

                //Check if we can connect to this server
                Multiplayer.Log($"Server: \"{selectedServer.GameVersion}\" \"{selectedServer.MultiplayerVersion}\"");
                Multiplayer.Log($"Client: \"{BuildInfo.BUILD_VERSION_MAJOR}\" \"{Multiplayer.Ver}\"");
                Multiplayer.Log($"Result: \"{selectedServer.GameVersion == BuildInfo.BUILD_VERSION_MAJOR.ToString()}\" \"{selectedServer.MultiplayerVersion == Multiplayer.Ver}\"");

                bool canConnect = selectedServer.GameVersion == BuildInfo.BUILD_VERSION_MAJOR.ToString() &&
                                  selectedServer.MultiplayerVersion == Multiplayer.Ver;

                buttonJoin.ToggleInteractable(canConnect);
            }
            else
            {
                buttonJoin.ToggleInteractable(false);
            }
        }

        private void UpdateElement(IServerBrowserGameDetails element)
        {
            int index = gridViewModel.IndexOf(element);

            if (index >= 0)
            {
                var viewElement = gridView.GetElementAt(index);
                viewElement?.UpdateView();
            }
        }
        #endregion

        private void UpdateDetailsPane()
        {
            string details;

            if (selectedServer != null)
            {
                serverName.text = selectedServer.Name;

                //note: built-in localisations have a trailing colon e.g. 'Game mode:'

                details  = "<alpha=#50>" + LocalizationAPI.L("launcher/game_mode", []) + "</color> " + LobbyServerData.GetGameModeFromInt(selectedServer.GameMode) + "<br>";
                details += "<alpha=#50>" + LocalizationAPI.L("launcher/difficulty", []) + "</color> " + LobbyServerData.GetDifficultyFromInt(selectedServer.Difficulty) + "<br>";
                details += "<alpha=#50>" + LocalizationAPI.L("launcher/in_game_time_passed", []) + "</color> " + selectedServer.TimePassed + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__PLAYERS + ":</color> " + selectedServer.CurrentPlayers + '/' + selectedServer.MaxPlayers + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__PASSWORD_REQUIRED + ":</color> " + (selectedServer.HasPassword ? Locale.SERVER_BROWSER__YES : Locale.SERVER_BROWSER__NO) + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__MODS_REQUIRED + ":</color> " + (string.IsNullOrEmpty(selectedServer.RequiredMods) ? Locale.SERVER_BROWSER__NO : Locale.SERVER_BROWSER__YES) + "<br>";
                details += "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__GAME_VERSION + ":</color> " + (selectedServer.GameVersion != BuildInfo.BUILD_VERSION_MAJOR.ToString() ? "<color=\"red\">" : "") + selectedServer.GameVersion + "</color><br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__MOD_VERSION + ":</color> " + (selectedServer.MultiplayerVersion != Multiplayer.Ver ? "<color=\"red\">" : "") + selectedServer.MultiplayerVersion + "</color><br>";
                details += "<br>";
                details += selectedServer.ServerDetails;

                //Multiplayer.Log("Finished Prepping Data");
                detailsPane.text = details;
            }
        }

        private void ShowIpPopup()
        {
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

                if (!IPAddress.TryParse(result.data, out IPAddress parsedAddress))
                {
                    string inputUrl = result.data;

                    if (!inputUrl.StartsWith("http://") && !inputUrl.StartsWith("https://"))
                    {
                        inputUrl = "http://" + inputUrl;
                    }

                    bool isValidURL = Uri.TryCreate(inputUrl, UriKind.Absolute, out Uri uriResult)
                      && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

                    if (isValidURL)
                    {
                        string domainName = ExtractDomainName(result.data);
                        try
                        {
                            IPHostEntry hostEntry = Dns.GetHostEntry(domainName);
                            IPAddress[] addresses = hostEntry.AddressList;

                            if (addresses.Length > 0)
                            {
                                string address2 = addresses[0].ToString();

                                address = address2;
                                Multiplayer.Log(address);

                                ShowPortPopup();
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Multiplayer.LogError($"An error occurred: {ex.Message}");
                        }
                    }

                    MainMenuThingsAndStuff.Instance.ShowOkPopup(Locale.SERVER_BROWSER__IP_INVALID, ShowIpPopup);
                }
                else
                {
                    if (parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        connectionState = ConnectionState.AttemptingIPv4;
                    else
                        connectionState = ConnectionState.AttemptingIPv6;

                    address = result.data;
                    ShowPortPopup();
                }
            };
        }

        //private void ShowSteamID()
        //{
        //    var popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
        //    if (popup == null)
        //    {
        //        Multiplayer.LogError("Popup not found.");
        //        return;
        //    }

        //    popup.labelTMPro.text = "SteamID";
        //    //popup.GetComponentInChildren<TMP_InputField>().text = Multiplayer.Settings.LastRemoteIP;

        //    popup.Closed += result =>
        //    {
        //        if (result.closedBy == PopupClosedByAction.Abortion)
        //        {
        //            buttonDirectIP.ToggleInteractable(true);
        //            IndexChanged(gridView); //re-enable the join button if a valid gridview item is selected
        //            return;
        //        }

        //        steamId = popup.GetComponentInChildren<TMP_InputField>().text;
        //        Multiplayer.LogDebug(() => $"Attempting to connecto SteamID: {steamId}");

        //        ShowPasswordPopup();
        //    };
        //}

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

                if (!int.TryParse(result.data, out portNumber) || portNumber < MIN_PORT || portNumber > MAX_PORT)
                {
                    MainMenuThingsAndStuff.Instance.ShowOkPopup(Locale.SERVER_BROWSER__PORT_INVALID, ShowIpPopup);
                }
                else
                {
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
                    Multiplayer.Settings.LastRemoteIP = address;
                    Multiplayer.Settings.LastRemotePort = portNumber;
                    Multiplayer.Settings.LastRemotePassword = result.data;
                }

                password = result.data;

                AttemptConnection();
            };
        }

        public void ShowConnectingPopup()
        {
            var popup = MainMenuThingsAndStuff.Instance.ShowOkPopup();

            if (popup == null)
            {
                Multiplayer.LogError("ShowConnectingPopup() Popup not found.");
                return;
            }

            connectingPopup = popup;

            Localize loc = popup.positiveButton.GetComponentInChildren<Localize>();
            loc.key ="cancel";
            loc.UpdateLocalization();


            popup.labelTMPro.text = $"Connecting, please wait..."; //to be localised

            popup.Closed += (PopupResult result) =>
            {
                connectionState = ConnectionState.Aborted;
            };
            
        }
        
        #region workflow
        private void UpdatePings()
        {
            UpdatePingsSteam();
        }

        private async void AttemptConnection()
        {

            Multiplayer.Log($"AttemptConnection Direct: {direct}, Address: {address}, Lobby: {selectedLobby?.Id.ToString()}");

            attempt = 0;
            connectionState = ConnectionState.NotConnected;
            ShowConnectingPopup();

            if (!direct)
            {
                if(selectedLobby != null)
                {
                    joinedLobby = selectedLobby; //store the lobby for when we disconnect

                    connectionState = ConnectionState.AttemptingSteamRelay;

                    var joinResult = await joinedLobby?.Join();
                    if (joinResult == RoomEnter.Success)
                    {
                        string hostId = ((Lobby)joinedLobby).Owner.Id.Value.ToString();
                        NetworkLifecycle.Instance.StartClient(hostId, -1, password, false, OnDisconnect);
                    }
                    else
                    {
                        Multiplayer.LogDebug(() => "AttemptConnection() Leaving Lobby");
                        joinedLobby?.Leave();
                        joinedLobby = null;
                        Multiplayer.Log($"Failed to join lobby: {joinResult}");
                        AttemptFail();
                    }

                    return;
                }
            }

            Multiplayer.Log($"AttemptConnection address: {address}");

            if (IPAddress.TryParse(address, out IPAddress IPaddress))
            {
                Multiplayer.Log($"AttemptConnection tryParse: {IPaddress.AddressFamily}");

                if (IPaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    AttemptIPv4();
                }
                else if (IPaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    AttemptIPv6();
                }

                return;
            }

            Multiplayer.LogError($"IP address invalid: {address}");

            AttemptFail();
        }

        private void AttemptIPv6()
        {
            Multiplayer.Log($"AttemptIPv6() {address}");

            if (connectionState == ConnectionState.Aborted)
                return;

            attempt++;
            if (connectingPopup != null)
                connectingPopup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}";

            Multiplayer.Log($"AttemptIPv6() starting attempt");
            connectionState = ConnectionState.AttemptingIPv6;
            SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, portNumber, password, false, OnDisconnect);

        }

        //private void AttemptIPv6Punch()
        //{
        //    Multiplayer.Log($"AttemptIPv6Punch() {address}");

        //    if (connectionState == ConnectionState.Aborted)
        //        return;

        //    attempt++;
        //    if (connectingPopup != null)
        //        connectingPopup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}";

        //    //punching not implemented we'll just try again for now
        //    connectionState = ConnectionState.AttemptingIPv6Punch;
        //    SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, portNumber, password, false, OnDisconnect);

        //}
        private void AttemptIPv4()
        {
            Multiplayer.Log($"AttemptIPv4() {address}, {connectionState}");

            if (connectionState == ConnectionState.Aborted)
                return;

            attempt++;
            if (connectingPopup != null)
                connectingPopup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}";

            if (!direct)
            {
                if (selectedServer.ipv4 == null || selectedServer.ipv4 == string.Empty)
                {
                    AttemptFail();
                    return;
                }

                address = selectedServer.ipv4;
            }

            Multiplayer.Log($"AttemptIPv4() {address}");

            if (IPAddress.TryParse(address, out IPAddress IPaddress))
            {
                Multiplayer.Log($"AttemptIPv4() TryParse passed");
                if (IPaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    Multiplayer.Log($"AttemptIPv4() starting attempt");
                    connectionState = ConnectionState.AttemptingIPv4;
                    SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, portNumber, password, false, OnDisconnect);
                    return;
                }
            }

            Multiplayer.Log($"AttemptIPv4() TryParse failed");
            AttemptFail();
            string message = "Host Unreachable";
            MainMenuThingsAndStuff.Instance.ShowOkPopup(message, () => { });
        }

        //private void AttemptIPv4Punch()
        //{
        //    Multiplayer.Log($"AttemptIPv4Punch() {address}");

        //    if (connectionState == ConnectionState.Aborted)
        //        return;

        //    attempt++;
        //    if (connectingPopup != null)
        //        connectingPopup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}";

        //    //punching not implemented we'll just try again for now
        //    connectionState = ConnectionState.AttemptingIPv4Punch;
        //    SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, portNumber, password, false, OnDisconnect);
        //}

        private void AttemptFail()
        {
            connectionState = ConnectionState.Failed;

            if (connectingPopup != null)
            {
                connectingPopup.RequestClose(PopupClosedByAction.Abortion, null);
                connectingPopup = null;  // Clear the reference
            }

            if (gameObject != null && gameObject.activeInHierarchy)
            {
                if (gridView != null)
                    IndexChanged(gridView);

                if (buttonDirectIP != null && buttonDirectIP.gameObject != null)
                    buttonDirectIP.ToggleInteractable(true);
            }
        }

        private void OnDisconnect(DisconnectReason reason, string message)
        {
            Multiplayer.Log($"Disconnected due to: {reason}, \"{message}\"");

            string displayMessage = message;

            Multiplayer.LogDebug(() => "OnDisconnect() Leaving Lobby");
            joinedLobby?.Leave();
            joinedLobby = null;

            if (string.IsNullOrEmpty(message))
            {
                //fallback for no message (server initiated disconnects should have a message)               
                //if (reason == DisconnectReason.ConnectionFailed)
                //{
                //    switch (connectionState)
                //    {
                //        case ConnectionState.AttemptingIPv6:
                //            if (Multiplayer.Settings.EnableNatPunch)
                //                AttemptIPv6Punch();
                //            else
                //                AttemptIPv4();
                //            return;
                //        case ConnectionState.AttemptingIPv6Punch:
                //            AttemptIPv4();
                //            return;
                //        case ConnectionState.AttemptingIPv4:
                //            if (Multiplayer.Settings.EnableNatPunch)
                //            {
                //                AttemptIPv4Punch();
                //                return;
                //            }
                //            break;
                //    }
                //}

                displayMessage = GetDisplayMessageForDisconnect(reason);
                AttemptFail();
            }
            else
            {
                connectionState = ConnectionState.NotConnected;
            }

            NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
            {
                Multiplayer.LogDebug(() => "OnDisconnect() Queuing");
                MainMenuThingsAndStuff.Instance?.ShowOkPopup(displayMessage, () => { });
            });
        }

        private string GetDisplayMessageForDisconnect(DisconnectReason reason)
        {
            return reason switch
            {
                DisconnectReason.UnknownHost => "Unknown Host",
                DisconnectReason.DisconnectPeerCalled => "Player Kicked",
                DisconnectReason.ConnectionFailed => "Host Unreachable",
                DisconnectReason.ConnectionRejected => "Rejected!",
                DisconnectReason.RemoteConnectionClose => "Server Shutting Down",
                DisconnectReason.Timeout => "Server Timed Out",
                _ => "Connection Failed"
            };
        }
        #endregion


        #region steam lobby
        private async void ListActiveLobbies()
        {
            lobbies = await SteamMatchmaking.LobbyList.WithMaxResults(100)
                                                      //.WithKeyValue(SteamworksUtils.MP_MOD_KEY, string.Empty)
                                                      .RequestAsync();

            Multiplayer.LogDebug(() => $"ListActiveLobbies() lobbies found: {lobbies?.Count()}");

            remoteServers.Clear();

            if (lobbies != null)
            {
                var myLoc = SteamNetworkingUtils.LocalPingLocation;

                foreach (var lobby in lobbies)
                {
                    LobbyServerData server = SteamworksUtils.GetLobbyData(lobby);

                    server.id = lobby.Id.ToString();

                    server.CurrentPlayers = lobby.MemberCount;
                    server.MaxPlayers = lobby.MaxMembers;

                    remoteServers.Add(server);

                    Multiplayer.LogDebug(() => $"ListActiveLobbies() lobby {server.Name}, {lobby.MemberCount}/{lobby.MaxMembers}");
                    
                }
            }
            remoteRefreshComplete = true;
        }

        private void UpdatePingsSteam()
        {
            foreach (var server in gridViewModel)
            {
                if (server is LobbyServerData lobbyServer)
                {
                    if (ulong.TryParse(server.id,out ulong id))
                    {
                        Lobby? lobby = lobbies.FirstOrDefault(l => l.Id.Value == id);
                        if (lobby != null)
                        {
                            string strLoc = ((Lobby)lobby).GetData(SteamworksUtils.LOBBY_NET_LOCATION_KEY);
                            NetPingLocation? location = NetPingLocation.TryParseFromString(strLoc);

                            if (location != null)
                                server.Ping = SteamNetworkingUtils.EstimatePingTo((NetPingLocation)location) / 2; //normalise to one way ping
                        }
                    }

                    UpdateElement(lobbyServer);
                }
            }
        }

        private Lobby? GetLobbyFromServer(IServerBrowserGameDetails server)
        {
            if (ulong.TryParse(server.id, out ulong id))
                return lobbies.FirstOrDefault(l => l.Id.Value == id);

            return null;
        }
        #endregion
        private void RefreshGridView()
        {

            var allServers = new List<IServerBrowserGameDetails>();
            allServers.AddRange(remoteServers);

            // Get all active IDs
            List<string> activeIDs = allServers.Select(s => s.id).Distinct().ToList();

            // Find servers to remove
            List<IServerBrowserGameDetails> removeList = gridViewModel.Where(gv => !activeIDs.Contains(gv.id)).ToList();

            // Remove expired servers
            foreach (var remove in removeList)
            {
                gridViewModel.Remove(remove);
            }

            // Update existing servers and add new ones
            foreach (var server in allServers)
            {
                var existingServer = gridViewModel.FirstOrDefault(gv => gv.id == server.id);
                if (existingServer != null)
                {
                    // Update existing server
                    existingServer.TimePassed = server.TimePassed;
                    existingServer.CurrentPlayers = server.CurrentPlayers;
                    existingServer.LocalIPv4 = server.LocalIPv4;
                    existingServer.LastSeen = server.LastSeen;
                }
                else
                {
                    // Add new server
                    gridViewModel.Add(server);
                }
            }

            if (gridViewModel.Count() == 0)
            {
                gridView.showDummyElement = true;
                buttonJoin.ToggleInteractable(false);
            }
            else
            {
                gridView.showDummyElement = false;
            }

            //Update the gridview rendering
            gridView.SetModel(gridViewModel);

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

        private string ExtractDomainName(string input)
        {
            if (input.StartsWith("http://"))
            {
                input = input.Substring(7);
            }
            else if (input.StartsWith("https://"))
            {
                input = input.Substring(8);
            }

            int portIndex = input.IndexOf(':');
            if (portIndex != -1)
            {
                input = input.Substring(0, portIndex);
            }

            return input;
        }
    }
}
