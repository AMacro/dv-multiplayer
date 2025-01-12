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
using UnityEngine.Networking;
using System.Linq;
using Multiplayer.Networking.Data;
using DV;
using System.Net;
using LiteNetLib;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using Multiplayer.Networking.Managers.Client;


namespace Multiplayer.Components.MainMenu
{
    public class ServerBrowserPane : MonoBehaviour
    {
        private class PingRecord
        {
            int ping1;
            int ping2;
            int received;

            public PingRecord()
            {
                ping1 = -1;
                ping2 = -1;
            }

            public int Avg()
            {
                if (received >= 2 && ping1 >-1 && ping2 > -1)
                    return (ping1 + ping2) / 2;
                else
                    return Math.Max(ping1, ping2);
            }

            public void AddPing(int ping)
            {
                //Multiplayer.Log($"AddPing() ping1 {ping1}, ping2 {ping2}, new {ping}, {received}");
                ping1 = ping2;
                ping2 = ping;

                if(received < 2)
                    received++;
            }
        }

        private enum ConnectionState
        {
            NotConnected,
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
        private readonly List<IServerBrowserGameDetails> serversToPing = [];
        private readonly Dictionary<string, (PingRecord IPv4Ping, PingRecord IPv6Ping)> serverPings = [];

        private float pingTimer = 0f;
        private const float PING_INTERVAL = 2f; // base interval to refresh all pings
        private const float PING_BATCH_INTERVAL = 0.5f; //gap bwetween ping batches
        private const int SERVERS_PER_BATCH = 10;

        //LAN tracking
        private readonly List<IServerBrowserGameDetails> localServers = [];
        private const int LAN_TIMEOUT = 60;         //How long to hold a LAN server without a response
        private const int DISCOVERY_TIMEOUT = 1;    //how long to wait for servers to respond
        private bool localRefreshComplete;
        private float discoveryTimer = 0f;

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

        private ServerBrowserClient serverBrowserClient;

        //connection parameters
        private string address;
        private int portNumber;
        string password = null;
        bool direct = false;

        private ConnectionState connectionState = ConnectionState.NotConnected;
        private Popup connectingPopup;
        private int attempt;

        private Lobby[] lobbies;


        #region setup

        public void Awake()
        {
            //Multiplayer.Log("MultiplayerPane Awake()");
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

            //Start the server browser network client
            serverBrowserClient = new ServerBrowserClient(Multiplayer.Settings);
            serverBrowserClient.OnPing += this.OnPing;
            serverBrowserClient.OnDiscovery += this.OnDiscovery;
            serverBrowserClient.Start();

            RefreshAction();
        }

        // Disable listeners
        public void OnDisable()
        {
            this.SetupListeners(false);

            if (serverBrowserClient != null)
            {
                serverBrowserClient.OnPing -= this.OnPing;
                serverBrowserClient.Stop();
                serverBrowserClient = null;
            }
        }

        public void OnDestroy()
        {
            if (serverBrowserClient == null)
                return;

            serverBrowserClient.OnPing -= this.OnPing;
            serverBrowserClient.Stop();
        }

        public void Update()
        {
            //Poll for any LAN discovery or ping packets
            serverBrowserClient?.PollEvents();

            //Handle server refresh interval
            timePassed += Time.deltaTime;
            discoveryTimer += Time.deltaTime;

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
            else if(localRefreshComplete && remoteRefreshComplete)
            {
                ExpireLocalServers();   //remove any that have not been seen in a while
                RefreshGridView();
                IndexChanged(gridView); //Revalidate any selected servers

                localRefreshComplete = false;
                remoteRefreshComplete = false;
                serverRefreshing = false;
                timePassed = 0;
            }
            else
            {
                if (discoveryTimer >= DISCOVERY_TIMEOUT)
                    localRefreshComplete = true;
            }


            //Handle pinging servers
            pingTimer += Time.deltaTime;

            if (pingTimer >= (serversToPing.Count > 0 ? PING_BATCH_INTERVAL : GetPingInterval()))
            {
                PingNextBatch();
                pingTimer = 0f;
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

            StartCoroutine(GetRequest($"{Multiplayer.Settings.LobbyServerAddress}/list_game_servers"));

            if (DVSteamworks.Success)
                ListActiveLobbies();


            //Send a message to find local peers
            discoveryTimer = 0f;
            serverBrowserClient?.SendDiscoveryRequest();
        }
        private void JoinAction()
        {
            if (selectedServer != null)
            {
                buttonDirectIP.ToggleInteractable(false);
                buttonJoin.ToggleInteractable(false);

                //not making a direct connection
                direct = false;
                portNumber = selectedServer.port;
                password = null; //clear the password

                if (selectedServer.HasPassword)
                {
                    ShowPasswordPopup();
                    return;
                }

                AttemptConnection();
               
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

            ShowIpPopup();
        }

        private void IndexChanged(AGridView<IServerBrowserGameDetails> gridView)
        {
            //Debug.Log($"Index: {gridView.SelectedModelIndex}");
            if (serverRefreshing)
                return;

            if (gridView.SelectedModelIndex >= 0)
            {
                //Multiplayer.Log($"Selected server: {gridViewModel[gridView.SelectedModelIndex].Name}");

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
                //Multiplayer.Log("Prepping Data");
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


            popup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}"; //to be localised

            popup.Closed += _ =>
            {
                connectionState = ConnectionState.Aborted;
            };
            
        }
        private void AttemptConnection()
        {

            Multiplayer.Log($"AttemptConnection Direct: {direct}, Address: {address}");

            attempt = 0;
            connectionState = ConnectionState.NotConnected;
            ShowConnectingPopup();

            if (!direct)
            {
                if (selectedServer.ipv6 != null && selectedServer.ipv6 != string.Empty)
                {
                    address = selectedServer.ipv6;
                }
                else
                {
                    address = selectedServer.ipv4;
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
                else if(IPaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
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
        private void AttemptIPv6Punch()
        {
            Multiplayer.Log($"AttemptIPv6Punch() {address}");

            if (connectionState == ConnectionState.Aborted)
                return;

            attempt++;
            if(connectingPopup != null)
                connectingPopup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}";

            //punching not implemented we'll just try again for now
            connectionState = ConnectionState.AttemptingIPv6Punch;
            SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, portNumber, password, false, OnDisconnect);
 
        }
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
                if(selectedServer.ipv4 == null || selectedServer.ipv4 == string.Empty)
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

        private void AttemptIPv4Punch()
        {
            Multiplayer.Log($"AttemptIPv4Punch() {address}");

            if (connectionState == ConnectionState.Aborted)
                return;

            attempt++;
            if (connectingPopup != null)
                connectingPopup.labelTMPro.text = $"Connecting, please wait...\r\nAttempt: {attempt}";

            //punching not implemented we'll just try again for now
            connectionState = ConnectionState.AttemptingIPv4Punch;
            SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, portNumber, password, false, OnDisconnect);
        }

        private void AttemptFail()
        {
            connectionState = ConnectionState.Failed;

            connectingPopup?.RequestClose(PopupClosedByAction.Abortion, null);

            if(this.gridView != null)
                IndexChanged(this.gridView);

            buttonDirectIP?.ToggleInteractable(true);
        }

        private void OnDisconnect(DisconnectReason reason, string message)
        {
            Multiplayer.LogError($"Connection failed! {reason}, \"{message}\"");

            switch (reason)
            {
                case DisconnectReason.UnknownHost:
                    if (message == null || message.Length == 0)
                    {
                        message = "Unknown Host"; //TODO: add translations
                    }
                    break;
                case DisconnectReason.DisconnectPeerCalled:
                    if (message == null || message.Length == 0)
                    {
                        message = "Player Kicked"; //TODO: add translations
                    }
                    break;
                case DisconnectReason.ConnectionFailed:

                    //Check our connectionState
                    switch (connectionState)
                    {
                        case ConnectionState.AttemptingIPv6:
                            if (Multiplayer.Settings.EnableNatPunch)
                                AttemptIPv6Punch();
                            else
                                AttemptIPv4();
                            return;
                        case ConnectionState.AttemptingIPv6Punch:
                            AttemptIPv4();
                            return;
                        case ConnectionState.AttemptingIPv4:
                            if (Multiplayer.Settings.EnableNatPunch)
                                AttemptIPv4Punch();
                            else
                                AttemptFail();
                                message = "Host Unreachable"; //TODO: add translations
                            return;
                        case ConnectionState.AttemptingIPv4Punch:
                            AttemptFail();
                            message = "Host Unreachable"; //TODO: add translations
                            break;
                    }
                    break;

                case DisconnectReason.ConnectionRejected:        
                    if (message == null || message.Length == 0)
                    {
                        message = "Rejected!"; //TODO: add translations
                    }
                    break;
                case DisconnectReason.RemoteConnectionClose:
                    if (message == null || message.Length == 0)
                    {
                        message = "Server Shutting Down"; //TODO: add translations
                    }
                    break;

                case DisconnectReason.Timeout:
                    if (message == null || message.Length == 0)
                    {
                        message = "Server Timed out"; //TODO: add translations
                    }
                    break;
            }

            //Multiplayer.LogError($"OnDisconnect() Calling AF");
            AttemptFail();

            //Multiplayer.LogError($"OnDisconnect() Queuing");
            NetworkLifecycle.Instance.QueueMainMenuEvent(() =>
            {
            
                Multiplayer.LogError($"OnDisconnect() Adding PU");
                MainMenuThingsAndStuff.Instance.ShowOkPopup(message, ()=>{ });

                //Multiplayer.LogError($"OnDisconnect() Done!");
            });
        }

        IEnumerator GetRequest(string uri)
        {
            using UnityWebRequest webRequest = UnityWebRequest.Get(uri);
            // Request and wait for the desired page.
            yield return webRequest.SendWebRequest();

            string[] pages = uri.Split('/');
            int page = pages.Length - 1;

            if (webRequest.isNetworkError)
            {
                Multiplayer.LogError(pages[page] + ": Error: " + webRequest.error);
            }
            else
            {
                Multiplayer.Log(pages[page] + ":\nReceived: " + webRequest.downloadHandler.text);

                LobbyServerData[] response;

                response = Newtonsoft.Json.JsonConvert.DeserializeObject<LobbyServerData[]>(webRequest.downloadHandler.text);

                Multiplayer.Log($"Serverbrowser servers: {response.Length}");

                foreach (LobbyServerData server in response)
                {
                    Multiplayer.Log($"Server name: \"{server.Name}\", IPv4: {server.ipv4}, IPv6: {server.ipv6}, Port: {server.port}");
                }

                remoteServers.AddRange(response);

            }

            remoteRefreshComplete = true;
        }

        private async void ListActiveLobbies()
        {
            lobbies = await SteamMatchmaking.LobbyList.WithMaxResults(100).RequestAsync();
            foreach (var lobby in lobbies)
            {
                var name = lobby.GetData("Server Name");
                var difficulty = lobby.GetData("Difficulty");
                Multiplayer.Log($"Steamworks Lobby Server name: \"{name}\", Difficulty: {difficulty}");
            }

        }

        private void RefreshGridView()
        {

            bool startPing = gridViewModel.Count == 0;

            var allServers = new List<IServerBrowserGameDetails>();
            allServers.AddRange(localServers);
            allServers.AddRange(remoteServers.Where(r => !localServers.Any(l => l.id == r.id)));

            // Get all active IDs
            List<string> activeIDs = allServers.Select(s => s.id).Distinct().ToList();

            //Multiplayer.Log($"RefreshGridView() Active servers: {activeIDs.Count}\r\n{string.Join("\r\n", activeIDs)}");

            // Find servers to remove
            List<IServerBrowserGameDetails> removeList = gridViewModel.Where(gv => !activeIDs.Contains(gv.id)).ToList();
            //Multiplayer.Log($"RefreshGridView() Remove List: {removeList.Count}\r\n{string.Join("\r\n", removeList.Select(l => l.id))}");

            // Remove expired servers
            foreach (var remove in removeList)
            {
                //Multiplayer.Log($"RefreshGridView() Removing: {remove.id}");
                if (serverPings.ContainsKey(remove.id))
                    serverPings.Remove(remove.id);
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

            //trigger ping to start
            if (startPing && gridViewModel.Count() > 0)
                PingNextBatch();
        }
        private void SetButtonsActive(params GameObject[] buttons)
        {
            foreach (var button in buttons)
            {
                button.SetActive(true);
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

        #region Network Utils
        private void OnPing(string serverId, int ping, bool isIPv4)
        {
            //Multiplayer.Log($"OnPing() Ping: {ping}, {(isIPv4?"IPv4" : "IPv6")}");

            if (!serverPings.ContainsKey(serverId))
                serverPings[serverId] = (new PingRecord(), new PingRecord());

            if (isIPv4)
                serverPings[serverId].IPv4Ping.AddPing(ping);
            else
                serverPings[serverId].IPv6Ping.AddPing(ping);

            var server = gridViewModel.FirstOrDefault(s => s.id == serverId);
            if (server != null)
            {
                server.Ping = GetBestPing(serverPings[serverId].IPv4Ping.Avg(), serverPings[serverId].IPv6Ping.Avg());
                UpdateElement(server);
            }
        }
        private void SendPing(IServerBrowserGameDetails server)
        {
            //Ensure we are using the same MP mod version, don't ping other versions
            Multiplayer.LogDebug(()=>$"SendPing: {server.Name}, {server.MultiplayerVersion}, {Multiplayer.Ver}");
            if (server.MultiplayerVersion != Multiplayer.Ver)
                return;

            // For LAN servers, prioritize the local IP addresses
            string ipv4 = server.LocalIPv4 ?? server.ipv4;
            string ipv6 = server.LocalIPv6 ?? server.ipv6;

            serverBrowserClient.SendUnconnectedPingPacket(server.id, ipv4, ipv6, server.port);
        }

        private float GetPingInterval() 
        {
            int serverCount = gridViewModel.Count;
            if (serverCount < 10) return PING_INTERVAL;
            if (serverCount < 50) return PING_INTERVAL * 2;
            if (serverCount < 100) return PING_INTERVAL * 4;
            return PING_INTERVAL * 10;
        }

        private void PingNextBatch()
        {
            if (serversToPing.Count == 0)
            {
                serversToPing.AddRange(gridViewModel);
            }

            var batch = serversToPing.Take(SERVERS_PER_BATCH).ToList();
            foreach (var server in batch)
            {
                SendPing(server);
            }
            serversToPing.RemoveRange(0, batch.Count);

            if (serversToPing.Count == 0)
                pingTimer = 0;  //Get ready to start from the beginning
        }

        private int GetBestPing(int ipv4Ping, int ipv6Ping)
        {
            if (ipv4Ping > -1 && ipv6Ping > -1)
            {
                return Math.Min(ipv4Ping, ipv6Ping);
            }
            else if (ipv4Ping > -1)
            {
                return ipv4Ping;
            }
            else if (ipv6Ping > -1)
            {
                return ipv6Ping;
            }
            return -1; // No ping available
        }

        private void OnDiscovery(IPEndPoint endpoint, LobbyServerData data)
        {
            if (data == null || endpoint == null)
                return;

            Multiplayer.Log($"Discovery - Endpoint: {endpoint}, EP Family: {endpoint.AddressFamily}, LocalIPv4: {data?.LocalIPv4}, LocalIPv6: {data?.LocalIPv6}");

            // Set local IP based on endpoint address type first
            if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                data.LocalIPv4 = endpoint.Address.ToString();
                Multiplayer.Log($"Setting LocalIPv4 to {data.LocalIPv4}");
            }
            else if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                data.LocalIPv6 = endpoint.Address.ToString();
                Multiplayer.Log($"Setting LocalIPv6 to {data.LocalIPv6}");
            }

            // Then handle server list management
            if (!string.IsNullOrEmpty(data.id))
            {
                IServerBrowserGameDetails existing = localServers.FirstOrDefault(element => element.id == data.id);
                if (existing != default(IServerBrowserGameDetails))
                {
                    localServers.Remove(existing);
                }  

                data.LastSeen = (int)Time.time;
                localServers.Add(data);

                existing = gridViewModel.FirstOrDefault(element => element.id == data.id);
                if (existing != default(IServerBrowserGameDetails))
                {
                    existing.LastSeen = (int)Time.time;
                    existing.LocalIPv4 = data.LocalIPv4;
                    existing.LocalIPv6 = data.LocalIPv6;
                }
            }
        }

        private void ExpireLocalServers()
        {
            List<IServerBrowserGameDetails> timedOut = localServers.Where(s => (s.LastSeen + LAN_TIMEOUT) < Time.time ).ToList();

            foreach (IServerBrowserGameDetails expired in timedOut)
            {
                if (serverPings.ContainsKey(expired.id))
                        serverPings.Remove(expired.id);

                localServers.Remove(expired);
            }
        }
        #endregion
    }
}
