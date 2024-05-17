using System;
using System.Text.RegularExpressions;
using DV.Localization;
using DV.UI;
using DV.UIFramework;
using DV.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu;

public class MultiplayerPane : MonoBehaviour
{
    // @formatter:off
    // Patterns from https://ihateregex.io/
    private static readonly Regex IPv4 = new(@"(\b25[0-5]|\b2[0-4][0-9]|\b[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}");
    private static readonly Regex IPv6 = new(@"(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])|([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9]))");
    private static readonly Regex PORT = new(@"^((6553[0-5])|(655[0-2][0-9])|(65[0-4][0-9]{2})|(6[0-4][0-9]{3})|([1-5][0-9]{4})|([0-5]{0,5})|([0-9]{1,4}))$");
    // @formatter:on

    private bool why;

    private string address;
    private ushort port;

    private GameObject directButton;
    private ButtonDV direct;

    private void Awake()
    {
        Multiplayer.Log("MultiplayerPane Awake()");

        GameObject button = GameObject.Find("ButtonTextIcon Run");

        button.SetActive(false);
        directButton = GameObject.Instantiate(button, this.transform);
        button.SetActive(true);

        directButton.name = "ButtonTextIcon DirectIP";

        direct = directButton.GetComponent<ButtonDV>();
        direct.onClick.AddListener(ShowIpPopup);



        directButton.GetComponentInChildren<Localize>().key = Locale.SERVER_BROWSER__DIRECT_KEY;

        foreach (I2.Loc.Localize loc in directButton.GetComponentsInChildren<I2.Loc.Localize>())
        {
            Component.DestroyImmediate(loc);
        }

        
        UIElementTooltip tooltip = directButton.GetComponent<UIElementTooltip>();
        tooltip.disabledKey = null;
        tooltip.enabledKey = Locale.SERVER_BROWSER__DIRECT_KEY;


        GameObject icon = directButton.FindChildByName("[icon]");
        if (icon == null)
        {
            Multiplayer.LogError("Failed to find icon on Direct IP button, destroying the Multiplayer button!");
            GameObject.Destroy(directButton);
            return;
        }

        icon.GetComponent<Image>().sprite = Multiplayer.AssetIndex.multiplayerIcon;

        directButton.SetActive(true);
        

    }

    private void OnEnable()
    {
        if (!why)
        {
            why = true;
            return;
        }

        Multiplayer.Log("MultiplayerPane OnEnable()");
        //ShowIpPopup();
        direct.enabled = true;
    }

    private void ShowIpPopup()
    {
        Popup popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
        if (popup == null)
            return;

        popup.labelTMPro.text = Locale.SERVER_BROWSER__IP;
        popup.GetComponentInChildren<TMP_InputField>().text = Multiplayer.Settings.LastRemoteIP;

        popup.Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Abortion)
            {
                MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
                return;
            }

            if (!IPv4.IsMatch(result.data) && !IPv6.IsMatch(result.data))
            {
                ShowOkPopup(Locale.SERVER_BROWSER__IP_INVALID, ShowIpPopup);
                return;
            }

            address = result.data;

            ShowPortPopup();
        };
    }

    private void ShowPortPopup()
    {
        Popup popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
        if (popup == null)
            return;

        popup.labelTMPro.text = Locale.SERVER_BROWSER__PORT;
        popup.GetComponentInChildren<TMP_InputField>().text = Multiplayer.Settings.LastRemotePort.ToString();

        popup.Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Abortion)
            {
                MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
                return;
            }

            if (!PORT.IsMatch(result.data))
            {
                ShowOkPopup(Locale.SERVER_BROWSER__PORT_INVALID, ShowPortPopup);
                return;
            }

            port = ushort.Parse(result.data);

            ShowPasswordPopup();
        };
    }

    private void ShowPasswordPopup()
    {
        Popup popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
        if (popup == null)
            return;

        popup.labelTMPro.text = Locale.SERVER_BROWSER__PASSWORD;
        popup.GetComponentInChildren<TMP_InputField>().text = Multiplayer.Settings.LastRemotePassword;

        //we need to remove the default controller and replace it with our own to override validation
        Component.DestroyImmediate(popup.GetComponentInChildren<PopupTextInputFieldController>());
        popup.GetOrAddComponent<PopupTextInputFieldControllerNoValidation>();

        popup.Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Abortion)
            {
                //MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
                return;
            }

            direct.enabled = false;


            SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, port, result.data);

            Multiplayer.Settings.LastRemoteIP = address;
            Multiplayer.Settings.LastRemotePort = port;
            Multiplayer.Settings.LastRemotePassword = result.data;
        };
    }

    private static void ShowOkPopup(string text, Action onClick)
    {
        Popup popup = MainMenuThingsAndStuff.Instance.ShowOkPopup();
        if (popup == null)
            return;

        popup.labelTMPro.text = text;
        popup.Closed += _ => { onClick(); };
    }
}
