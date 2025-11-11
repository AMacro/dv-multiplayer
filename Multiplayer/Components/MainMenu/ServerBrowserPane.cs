using DV;
using DV.Localization;
using DV.Platform.Steam;
using DV.UI;
using DV.UIFramework;
using DV.Utils;
using LiteNetLib;
using Multiplayer.API;
using Multiplayer.Components.MainMenu.ServerBrowser;
using Multiplayer.Components.Networking;
using Multiplayer.Components.UI.Controls;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu
{
    public class ServerBrowserPane : MonoBehaviour
    {
        private enum ConnectionState
        {
            NotConnected,
            JoiningLobby,
            AwaitingPassword,
            AttemptingSteamRelay,
            AttemptingIPv6,
            AttemptingIPv6Punch,
            AttemptingIPv4,
            AttemptingIPv4Punch,
            Connected,
            Failed,
            Aborted
        }

        private const int MAX_PORT_LEN = 5;
        private const int MIN_PORT = 1024;
        private const int MAX_PORT = 49151;

        //Gridview variables
        private ServerBrowserGridView serverGridView;
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

        private bool incompatibleMods = true;

        #region setup

        public void Awake()
        {
            Multiplayer.Log("MultiplayerPane Awake()");
            joinedLobby?.Leave();
            joinedLobby = null;

            CleanUI();
            BuildUI();

            SetupServerBrowser();
        }

        public void OnEnable()
        {
            //ensure no incompatible mods are loaded
            incompatibleMods = ModCompatibilityManager.Instance.CheckModCompatibility();

            this.SetupListeners(true);

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
                else if (timePassed >= REFRESH_MIN_TIME)
                {
                    buttonRefresh.ToggleInteractable(true);
                }
            }
            else if (remoteRefreshComplete)
            {
                RefreshGridView();
                OnSelectedIndexChanged(serverGridView); //Revalidate any selected servers
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

            if (lobbyToJoin != null && connectionState == ConnectionState.NotConnected)
            {
                //For invites/requests
                Multiplayer.Log($"Player invite initiated/request");

                if (lobbyToJoin.Value.Id.IsValid)
                {
                    direct = false;
                    var _ = JoinLobby((Lobby)lobbyToJoin);
                }
                else
                {
                    Multiplayer.LogWarning("Received invalid lobby invite");
                    lobbyToJoin = null;
                }
            }
        }

        public void Start()
        {
            if (DVSteamworks.Success)
                return;

            Multiplayer.Log($"Steam not detected, prompt for restart.");
            MainMenuThingsAndStuff.Instance.ShowOkPopup("Steam not detected. Please restart the game with Steam running", () => { });
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
            GameObject textContainerGO = new("Details Container", typeof(HorizontalLayoutGroup));
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
            contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x - 50, detailsPane.preferredHeight);

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
            serverGridView = GridviewGO.AddComponent<ServerBrowserGridView>();

            //grab the original prefab
            slgv.viewElementPrefab.SetActive(false);
            serverGridView.viewElementPrefab = Instantiate(slgv.viewElementPrefab);
            slgv.viewElementPrefab.SetActive(true);

            //Remove original controller
            GameObject.Destroy(slgv);

            //Don't forget to re-enable!
            GridviewGO.SetActive(true);
            serverGridView.Clear();
        }
        private void SetupListeners(bool on)
        {
            if (on)
            {
                serverGridView.SelectedIndexChanged += this.OnSelectedIndexChanged;
            }
            else
            {
                serverGridView.SelectedIndexChanged -= this.OnSelectedIndexChanged;
            }
        }
        #endregion

        #region UI callbacks
        private void RefreshAction()
        {
            if (serverRefreshing)
                return;

            remoteServers.Clear();

            serverRefreshing = true;
            //buttonJoin.ToggleInteractable(false);
            buttonRefresh.ToggleInteractable(false);

            if (DVSteamworks.Success)
                ListActiveLobbies();

        }
        private void JoinAction()
        {
            if (selectedServer == null || connectionState != ConnectionState.NotConnected)
                return;

            buttonDirectIP.ToggleInteractable(false);
            buttonJoin.ToggleInteractable(false);

            //not making a direct connection
            direct = false;
            portNumber = -1;

            var lobby = GetLobbyFromServer(selectedServer);
            if (lobby != null)
            {
                selectedLobby = (Lobby)lobby;
                _ = JoinLobby((Lobby)selectedLobby);
            }
            else
            {
                Multiplayer.LogWarning($"JoinAction called but lobby is null");
                AttemptFail();
            }
        }

        private void DirectAction()
        {
            if(connectionState != ConnectionState.NotConnected)
                return;

            buttonDirectIP.ToggleInteractable(false);
            buttonJoin.ToggleInteractable(false);

            //making a direct connection
            direct = true;
            password = null;

            ShowIpPopup();
        }

        private void OnSelectedIndexChanged(MPGridView<IServerBrowserGameDetails> gridView)
        {
            if (serverRefreshing)
                return;

            selectedServer = gridView.SelectedItem;
            if (selectedServer != null && incompatibleMods == false)
            {
                UpdateDetailsPane();

                //Check if we can connect to this server
                Multiplayer.Log($"Server: \"{selectedServer.GameVersion}\" \"{selectedServer.MultiplayerVersion}\"");
                Multiplayer.Log($"Client: \"{Multiplayer.LocalBuildInfo}\" \"{Multiplayer.Ver}\"");
                Multiplayer.Log($"Result: \"{selectedServer.GameVersion == Multiplayer.LocalBuildInfo}\" \"{selectedServer.MultiplayerVersion == Multiplayer.Ver}\"");

                bool canConnect = selectedServer.GameVersion == Multiplayer.LocalBuildInfo &&
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
            int index = serverGridView.IndexOf(element);

            if (index >= 0)
            {
                var viewElement = serverGridView.GetElementAt(index) as ServerBrowserElement;
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

                details = "<alpha=#50>" + LocalizationAPI.L("launcher/game_mode", []) + "</color> " + LobbyServerData.GetGameModeFromInt(selectedServer.GameMode) + "<br>";
                details += "<alpha=#50>" + LocalizationAPI.L("launcher/difficulty", []) + "</color> " + LobbyServerData.GetDifficultyFromInt(selectedServer.Difficulty) + "<br>";
                details += "<alpha=#50>" + LocalizationAPI.L("launcher/in_game_time_passed", []) + "</color> " + selectedServer.TimePassed + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__PLAYERS + ":</color> " + selectedServer.CurrentPlayers + '/' + selectedServer.MaxPlayers + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__PASSWORD_REQUIRED + ":</color> " + (selectedServer.HasPassword ? Locale.SERVER_BROWSER__YES : Locale.SERVER_BROWSER__NO) + "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__MODS_REQUIRED + ":</color> " + (string.IsNullOrEmpty(selectedServer.RequiredMods) ? Locale.SERVER_BROWSER__NO : Locale.SERVER_BROWSER__YES) + "<br>";
                details += "<br>";
                details += "<alpha=#50>" + Locale.SERVER_BROWSER__GAME_VERSION + ":</color> " + (selectedServer.GameVersion != Multiplayer.LocalBuildInfo ? "<color=\"red\">" : "") + selectedServer.GameVersion + "</color><br>";
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
                    OnSelectedIndexChanged(serverGridView); //re-enable the join button if a valid gridview item is selected
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
                    AttemptFail();
                    return;
                }

                password = result.data;

                if (direct)
                {
                    //store params for later
                    Multiplayer.Settings.LastRemoteIP = address;
                    Multiplayer.Settings.LastRemotePort = portNumber;
                    Multiplayer.Settings.LastRemotePassword = result.data;
                }

                InitiateConnection();
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
            loc.key = "cancel";
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

        private void InitiateConnection()
        {

            Multiplayer.Log($"Initiating connection. Direct: {direct}, Address: {address}, Lobby: {selectedLobby?.Id.ToString()}");

            attempt = 0;
            ShowConnectingPopup();

            if (!direct && joinedLobby != null)
            {
                connectionState = ConnectionState.AttemptingSteamRelay;
                string hostId = ((Lobby)joinedLobby).Owner.Id.Value.ToString();
                NetworkLifecycle.Instance.StartClient(hostId, -1, password, false, OnDisconnect);
                return;
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
            }
            else
            {
                Multiplayer.LogError($"IP address invalid: {address}");
                AttemptFail();
            }
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

            joinedLobby?.Leave();
            joinedLobby = null;

            if (gameObject != null && gameObject.activeInHierarchy)
            {
                if (serverGridView != null)
                    OnSelectedIndexChanged(serverGridView);

                if (buttonDirectIP != null && buttonDirectIP.gameObject != null)
                    buttonDirectIP.ToggleInteractable(true);
            }

            StartCoroutine(ResetConnectionState());
        }

        private IEnumerator ResetConnectionState()
        {
            yield return new WaitForSeconds(1.0f);
            connectionState = ConnectionState.NotConnected;
        }

        private void OnDisconnect(DisconnectReason reason, string message)
        {
            Multiplayer.Log($"Disconnected due to: {reason}, \"{message}\"");

            string displayMessage = !string.IsNullOrEmpty(message)
                ? message
                : GetDisplayMessageForDisconnect(reason);

            Multiplayer.LogDebug(() => "OnDisconnect() Leaving Lobby");
            joinedLobby?.Leave();
            joinedLobby = null;

            connectionState = ConnectionState.NotConnected;
            AttemptFail();

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
                                                      .FilterDistanceWorldwide()
                                                      .WithSlotsAvailable(-1)
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
            foreach (var server in serverGridView.Items)
            {
                if (server is LobbyServerData lobbyServer)
                {
                    if (ulong.TryParse(server.id, out ulong id))
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
            // Get all active IDs
            List<string> activeIDs = remoteServers.Select(s => s.id).Distinct().ToList();

            // Remove servers that no longer exist
            for (int i = serverGridView.Items.Count - 1; i >= 0; i--)
            {
                if (!activeIDs.Contains(serverGridView.Items[i].id))
                {
                    serverGridView.RemoveItemAt(i);
                }
            }

            Multiplayer.LogDebug(() => $"RefreshGridView() prepare to update/add, remoteServers count: {remoteServers.Count}");
            // Update existing servers and add new ones
            foreach (var server in remoteServers)
            {
                var existingServer = serverGridView.Items.FirstOrDefault(gv => gv.id == server.id);
                if (existingServer != null)
                {
                    Multiplayer.LogDebug(() => $"RefreshGridView() updating server");
                    // Update existing server
                    existingServer.TimePassed = server.TimePassed;
                    existingServer.CurrentPlayers = server.CurrentPlayers;
                    existingServer.LocalIPv4 = server.LocalIPv4;
                    existingServer.LastSeen = server.LastSeen;
                }
                else
                {
                    Multiplayer.LogDebug(() => $"RefreshGridView() adding server");
                    // Add new server
                    serverGridView.AddItem(server);
                }
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

        private async Task<bool> JoinLobby(Lobby lobby)
        {

            if (connectionState != ConnectionState.NotConnected)
            {
                Multiplayer.LogWarning($"Cannot join lobby while in state: {connectionState}");
                return false;
            }

            connectionState = ConnectionState.JoiningLobby;
            Multiplayer.Log($"Attempting to join lobby ({lobby.Id})");

            var joinResult = await lobby.Join();

            if (joinResult == RoomEnter.Success)
            {
                Multiplayer.Log($"Lobby joined ({lobby.Id})");
                
                joinedLobby = lobby;
                lobbyToJoin = null;

                string hasPass = lobby.GetData(SteamworksUtils.LOBBY_HAS_PASSWORD);
                Multiplayer.Log($"Lobby ({lobby.Id}) has password: {hasPass}");

                if (string.IsNullOrEmpty(hasPass) || hasPass == "False")
                {
                    Multiplayer.Log($"Attempting connection...");
                    InitiateConnection();
                }
                else
                {
                    connectionState = ConnectionState.AwaitingPassword;
                    Multiplayer.Log($"Prompting for password...");
                    ShowPasswordPopup();
                }

                return true;
            }
            else
            {
                Multiplayer.LogDebug(() => "JoinLobby() Leaving Lobby");
                lobby.Leave();
                joinedLobby = null;
                Multiplayer.Log($"Failed to join lobby: {joinResult}");
                AttemptFail();
            }

            return false;
        }
    }
}
