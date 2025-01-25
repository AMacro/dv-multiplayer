using DV.UI;
using DV.UIFramework;
using DV.Localization;
using Multiplayer.Utils;
using System.ComponentModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu.ServerBrowser
{
    public class ServerBrowserDummyElement : AViewElement<IServerBrowserGameDetails>
    {
        private TextMeshProUGUI networkName;

        protected override void Awake()
        {
            // Find and assign TextMeshProUGUI components for displaying server details
            GameObject networkNameGO = this.FindChildByName("name [noloc]");
            networkName = networkNameGO.GetComponent<TextMeshProUGUI>();
            this.FindChildByName("date [noloc]").SetActive(false);
            this.FindChildByName("time [noloc]").SetActive(false);
            this.FindChildByName("autosave icon").SetActive(false);

            //Remove doubled up components
            GameObject.Destroy(this.transform.GetComponent<HoverEffect>());
            GameObject.Destroy(this.transform.GetComponent<MarkEffect>());
            GameObject.Destroy(this.transform.GetComponent<ClickEffect>());
            GameObject.Destroy(this.transform.GetComponent<PressEffect>());

            RectTransform networkNameRT = networkNameGO.transform.GetComponent<RectTransform>();
            networkNameRT.sizeDelta = new Vector2(600, networkNameRT.sizeDelta.y);

            this.SetInteractable(false);

            Localize loc = networkNameGO.GetOrAddComponent<Localize>();
            loc.key = Locale.SERVER_BROWSER__NO_SERVERS_KEY ;
            loc.UpdateLocalization();

            this.GetOrAddComponent<UIElementTooltip>().enabled = true;
            this.gameObject.ResetTooltip();

        }

        public override void SetData(IServerBrowserGameDetails data, AGridView<IServerBrowserGameDetails> _)
        {
            //do nothing
        }

        private void UpdateView(object sender = null, PropertyChangedEventArgs e = null)
        {
            //do nothing
        }
    }
}
