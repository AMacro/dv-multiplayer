using DV.UIFramework;
using Multiplayer.Utils;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu.ServerBrowser
{
    public class ServerBrowserElement : AViewElement<IServerBrowserGameDetails>
    {
        private TextMeshProUGUI networkName;
        private TextMeshProUGUI playerCount;
        private TextMeshProUGUI ping;
        private GameObject goIconPassword;
        private Image iconPassword;
        private GameObject goIconLAN;
        private Image iconLAN;
        private IServerBrowserGameDetails data;

        private const int PING_WIDTH = 124; // Adjusted width for the ping text
        private const int PING_POS_X = 650; // X position for the ping text

        private const string PING_COLOR_UNKNOWN = "#808080";
        private const string PING_COLOR_EXCELLENT = "#00ff00";
        private const string PING_COLOR_GOOD = "#ffa500";
        private const string PING_COLOR_HIGH = "#ff4500";
        private const string PING_COLOR_POOR = "#ff0000";

        private const int PING_THRESHOLD_NONE = -1;
        private const int PING_THRESHOLD_EXCELLENT = 60;
        private const int PING_THRESHOLD_GOOD = 100;
        private const int PING_THRESHOLD_HIGH = 150;

        protected override void Awake()
        {
            // Find and assign TextMeshProUGUI components for displaying server details
            networkName = this.FindChildByName("name [noloc]").GetComponent<TextMeshProUGUI>();
            playerCount = this.FindChildByName("date [noloc]").GetComponent<TextMeshProUGUI>();
            ping = this.FindChildByName("time [noloc]").GetComponent<TextMeshProUGUI>();
            goIconPassword = this.FindChildByName("autosave icon");
            iconPassword = goIconPassword.GetComponent<Image>();

            // Fix alignment of the player count text relative to the network name text
            Vector3 namePos = networkName.transform.position;
            Vector2 nameSize = networkName.rectTransform.sizeDelta;
            playerCount.transform.position = new Vector3(namePos.x + nameSize.x, namePos.y, namePos.z);

            // Adjust the size and position of the ping text
            Vector2 rowSize = transform.GetComponentInParent<RectTransform>().sizeDelta;
            Vector3 pingPos = ping.transform.position;
            Vector2 pingSize = ping.rectTransform.sizeDelta;

            ping.rectTransform.sizeDelta = new Vector2(PING_WIDTH, pingSize.y);
            ping.transform.position = new Vector3(PING_POS_X, pingPos.y, pingPos.z);
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
                LANpos.x += (PING_POS_X - LANpos.x - LANSize.x) / 2;
                goIconLAN.transform.localPosition = LANpos;
                iconLAN = goIconLAN.GetComponent<Image>();
                iconLAN.sprite = Multiplayer.AssetIndex.lanIcon;
            }

        }

        public override void SetData(IServerBrowserGameDetails data, AGridView<IServerBrowserGameDetails> _)
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

            // Update the text fields with the data from the server
            networkName.text = data.Name;
            playerCount.text = $"{data.CurrentPlayers} / {data.MaxPlayers}";

            //if (data.MultiplayerVersion == Multiplayer.Ver)
                ping.text = $"<color={GetColourForPing(data.Ping)}>{(data.Ping < 0 ? "?" : data.Ping)} ms</color>";
            //else
            //    ping.text = $"<color={PING_COLOR_UNKNOWN}>N/A</color>";

            // Hide the icon if the server does not have a password
            goIconPassword.SetActive(data.HasPassword);

            bool isLan = !string.IsNullOrEmpty(data.LocalIPv4) || !string.IsNullOrEmpty(data.LocalIPv6);
            goIconLAN.SetActive(isLan);
        }

        private string GetColourForPing(int ping)
        {
            switch (ping)
            {
                case PING_THRESHOLD_NONE:
                    return PING_COLOR_UNKNOWN;
                case < PING_THRESHOLD_EXCELLENT:
                    return PING_COLOR_EXCELLENT;
                case < PING_THRESHOLD_GOOD:
                    return PING_COLOR_GOOD;
                case < PING_THRESHOLD_HIGH:
                    return PING_COLOR_HIGH;
                default:
                    return PING_COLOR_POOR;
            }
        }
    }
}
