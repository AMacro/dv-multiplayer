﻿using System;
using System.Text.RegularExpressions;
using DV.UIFramework;
using DV.Utils;
using Multiplayer.Components.Networking;
using UnityEngine;

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

    private void OnEnable()
    {
        if (!why)
        {
            why = true;
            return;
        }

        ShowIpPopup();
    }

    private void ShowIpPopup()
    {
        Popup popup = MainMenuThingsAndStuff.Instance.ShowRenamePopup();
        if (popup == null)
            return;

        popup.labelTMPro.text = "Enter IP Address";

        popup.Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Abortion)
            {
                MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
                return;
            }

            if (!IPv4.IsMatch(result.data) && !IPv6.IsMatch(result.data))
            {
                ShowOkPopup("Invalid IP Address!", ShowIpPopup);
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

        popup.labelTMPro.text = "Enter the port (7777 by default)";

        popup.Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Abortion)
            {
                MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
                return;
            }

            if (!PORT.IsMatch(result.data))
            {
                ShowOkPopup("Invalid Port!", ShowPortPopup);
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

        popup.labelTMPro.text = "Enter the password";

        popup.Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Abortion)
            {
                MainMenuThingsAndStuff.Instance.SwitchToDefaultMenu();
                return;
            }

            SingletonBehaviour<NetworkLifecycle>.Instance.StartClient(address, port, result.data);
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
