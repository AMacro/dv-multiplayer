using Multiplayer.Components.UI.Controls;
using Multiplayer.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.UI.ServerBrowser
{
    public class ServerBrowserElement : MPViewElement<IServerBrowserGameDetails>
    {
        public override bool IsPlaceholder => false;

        private TextMeshProUGUI serverName;
        private TextMeshProUGUI playerCount;
        private TextMeshProUGUI ping;
        private GameObject goIconPassword;
        private Image iconPassword;
        private GameObject goIconLAN;
        private Image iconLAN;
        private IServerBrowserGameDetails data;

        private const int PING_WIDTH = 124; // Adjusted width for the ping text
        private const int PING_PADDING_X = 10;

        private const string PING_COLOR_UNKNOWN = "#808080";
        private const string PING_COLOR_EXCELLENT = "#00ff00";
        private const string PING_COLOR_GOOD = "#ffa500";
        private const string PING_COLOR_HIGH = "#ff4500";
        private const string PING_COLOR_POOR = "#ff0000";

        private const int PING_THRESHOLD_NONE = -1;
        private const int PING_THRESHOLD_EXCELLENT = 60;
        private const int PING_THRESHOLD_GOOD = 100;
        private const int PING_THRESHOLD_HIGH = 150;

        public override void Awake()
        {
            // Find and assign TextMeshProUGUI components for displaying server details
            serverName = this.FindChildByName("name [noloc]").GetComponent<TextMeshProUGUI>();
            playerCount = this.FindChildByName("date [noloc]").GetComponent<TextMeshProUGUI>();
            ping = this.FindChildByName("time [noloc]").GetComponent<TextMeshProUGUI>();
            goIconPassword = this.FindChildByName("autosave icon");
            iconPassword = goIconPassword.GetComponent<Image>();

            RectTransform nameRT = serverName.rectTransform;

            // Align player count
            RectTransform playerCountRT = playerCount.rectTransform;
            playerCountRT.anchorMin = new Vector2(0, 0.5f);
            playerCountRT.anchorMax = new Vector2(0, 0.5f);
            playerCountRT.pivot = new Vector2(0, 0.5f);

            float nameWidth = nameRT.sizeDelta.x;
            playerCountRT.anchoredPosition = new Vector2(nameRT.position.x + nameWidth, nameRT.anchoredPosition.y);

            // Align ping
            RectTransform pingRT = ping.rectTransform;
            pingRT.anchorMin = new Vector2(0, 0.5f);
            pingRT.anchorMax = new Vector2(0, 0.5f);
            pingRT.pivot = new Vector2(0, 0.5f);

            RectTransform parentRT = transform as RectTransform;
            float pingX = parentRT.rect.width - PING_WIDTH - PING_PADDING_X;
            pingRT.anchoredPosition = new Vector2(pingX, nameRT.anchoredPosition.y);
            pingRT.sizeDelta = new Vector2(PING_WIDTH, pingRT.sizeDelta.y);
            ping.alignment = TextAlignmentOptions.Right;


            // Set password icon
            iconPassword.sprite = Multiplayer.AssetIndex.lockIcon;

            // Set LAN icon
            if(this.HasChildWithName("LAN Icon"))
            {
                goIconLAN = this.FindChildByName("LAN Icon");
            }
            else
            { 
                goIconLAN = Instantiate(goIconPassword, goIconPassword.transform.parent);
                goIconLAN.name = "LAN Icon";
                Vector3 LANpos = goIconLAN.transform.localPosition;
                Vector3 LANSize = goIconLAN.GetComponent<RectTransform>().sizeDelta;
                LANpos.x += (pingRT.position.x - LANpos.x - LANSize.x) / 2;
                goIconLAN.transform.localPosition = LANpos;
                iconLAN = goIconLAN.GetComponent<Image>();
                iconLAN.sprite = Multiplayer.AssetIndex.lanIcon;
            }

        }

        public override void SetData(IServerBrowserGameDetails data)
        {
            // Clear existing data
            if (this.data != null)
            {
                this.data = null;
            }
            // Set new data
            if (data != null)
            {
                this.data = data;
            }
            // Update the view with the new data
            UpdateView();
        }

        public void UpdateView()
        {
            //Multiplayer.LogDebug(() => $"UpdateView() serverName: {data.Name}, ping: {data.Ping}");

            // Update the text fields with the data from the server
            serverName.text = data.Name;
            playerCount.text = $"{data.CurrentPlayers} / {data.MaxPlayers}";

            ping.text = $"<color={GetColourForPing(data.Ping)}>{(data.Ping < 0 ? "?" : data.Ping)} ms</color>";

            // Hide the icon if the server does not have a password
            goIconPassword.SetActive(data.HasPassword);

            bool isLan = !string.IsNullOrEmpty(data.LocalIPv4) || !string.IsNullOrEmpty(data.LocalIPv6);
            goIconLAN.SetActive(isLan);
        }

        private string GetColourForPing(int ping)
        {
            return ping switch
            {
                PING_THRESHOLD_NONE => PING_COLOR_UNKNOWN,
                < PING_THRESHOLD_EXCELLENT => PING_COLOR_EXCELLENT,
                < PING_THRESHOLD_GOOD => PING_COLOR_GOOD,
                < PING_THRESHOLD_HIGH => PING_COLOR_HIGH,
                _ => PING_COLOR_POOR,
            };
        }
    }
}
