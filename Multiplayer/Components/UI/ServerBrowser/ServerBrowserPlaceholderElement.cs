using DV.UI;
using DV.UIFramework;
using DV.Localization;
using Multiplayer.Utils;
using UnityEngine;
using Multiplayer.Components.UI.Controls;

namespace Multiplayer.Components.UI.ServerBrowser
{
    public class ServerBrowserPlaceholderElement : MPViewElement<IServerBrowserGameDetails>
    {
        public override bool IsPlaceholder => true;

        public override void Awake()
        {
            // Find and assign TextMeshProUGUI components for displaying server details
            GameObject networkNameGO = this.FindChildByName("name [noloc]");

            this.FindChildByName("date [noloc]").SetActive(false);
            this.FindChildByName("time [noloc]").SetActive(false);
            this.FindChildByName("autosave icon").SetActive(false);

            //Remove doubled up components
            Destroy(transform.GetComponent<HoverEffect>());
            Destroy(transform.GetComponent<MarkEffect>());
            Destroy(transform.GetComponent<ClickEffect>());
            Destroy(transform.GetComponent<PressEffect>());

            RectTransform networkNameRT = networkNameGO.transform.GetComponent<RectTransform>();
            networkNameRT.sizeDelta = new Vector2(600, networkNameRT.sizeDelta.y);

            SetInteractable(false);

            Localize loc = networkNameGO.GetOrAddComponent<Localize>();
            loc.key = Locale.SERVER_BROWSER__NO_SERVERS_KEY ;
            loc.UpdateLocalization();

            this.GetOrAddComponent<UIElementTooltip>().enabled = true;
            gameObject.ResetTooltip();

        }

        public override void SetData(IServerBrowserGameDetails data)
        {
            //do nothing
        }
    }
}
