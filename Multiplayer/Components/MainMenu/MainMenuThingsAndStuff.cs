using System;
using DV.UIFramework;
using DV.Utils;
using JetBrains.Annotations;
using UnityEngine;

namespace Multiplayer.Components.MainMenu;

public class MainMenuThingsAndStuff : SingletonBehaviour<MainMenuThingsAndStuff>
{
    public PopupManager popupManager;
    public Popup renamePopupPrefab;
    public Popup okPopupPrefab;
    public UIMenuController uiMenuController;

    protected override void Awake()
    {
        bool shouldDestroy = false;

        if (popupManager == null)
        {
            Multiplayer.LogError("Failed to find PopupManager! Destroying self.");
            shouldDestroy = true;
        }

        if (renamePopupPrefab == null)
        {
            Multiplayer.LogError($"{nameof(renamePopupPrefab)} is null! Destroying self.");
            shouldDestroy = true;
        }

        if (okPopupPrefab == null)
        {
            Multiplayer.LogError($"{nameof(okPopupPrefab)} is null! Destroying self.");
            shouldDestroy = true;
        }

        if (uiMenuController == null)
        {
            Multiplayer.LogError($"{nameof(uiMenuController)} is null! Destroying self.");
            shouldDestroy = true;
        }

        if (!shouldDestroy)
        {
            base.Awake();
            return;
        }

        Destroy(this);
    }

    public void SwitchToDefaultMenu()
    {
        uiMenuController.SwitchMenu(uiMenuController.defaultMenuIndex);
    }

    public void SwitchToMenu(byte index)
    {
        uiMenuController.SwitchMenu(index);
    }

    [CanBeNull]
    public Popup ShowRenamePopup()
    {
        return ShowPopup(renamePopupPrefab);
    }

    [CanBeNull]
    public Popup ShowOkPopup()
    {
        return ShowPopup(okPopupPrefab);
    }

    [CanBeNull]
    private Popup ShowPopup(Popup popup)
    {
        if (popupManager.CanShowPopup())
            return popupManager.ShowPopup(popup);
        Multiplayer.LogError($"{nameof(PopupManager)} cannot show popup!");
        return null;
    }

    /// <param name="func">A function to apply to the MainMenuPopupManager while the object is disabled</param>
    public static void Create(Action<MainMenuThingsAndStuff> func)
    {
        GameObject go = new($"[{nameof(MainMenuThingsAndStuff)}]");
        go.SetActive(false);
        MainMenuThingsAndStuff manager = go.AddComponent<MainMenuThingsAndStuff>();
        func.Invoke(manager);
        go.SetActive(true);
    }
}
