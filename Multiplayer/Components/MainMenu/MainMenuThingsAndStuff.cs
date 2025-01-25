using System;
using DV.UIFramework;
using DV.Utils;
using JetBrains.Annotations;
using UnityEngine;

namespace Multiplayer.Components.MainMenu
{
    public class MainMenuThingsAndStuff : SingletonBehaviour<MainMenuThingsAndStuff>
    {
        public PopupManager popupManager;
        public Popup renamePopupPrefab;
        public Popup okPopupPrefab;
        public UIMenuController uiMenuController;

        protected override void Awake()
        {
            bool shouldDestroy = false;

            // Check if PopupManager is assigned
            if (popupManager == null)
            {
                Multiplayer.LogError("Failed to find PopupManager! Destroying self.");
                shouldDestroy = true;
            }

            // Check if renamePopupPrefab is assigned
            if (renamePopupPrefab == null)
            {
                Multiplayer.LogError($"{nameof(renamePopupPrefab)} is null! Destroying self.");
                shouldDestroy = true;
            }

            // Check if okPopupPrefab is assigned
            if (okPopupPrefab == null)
            {
                Multiplayer.LogError($"{nameof(okPopupPrefab)} is null! Destroying self.");
                shouldDestroy = true;
            }

            // Check if uiMenuController is assigned
            if (uiMenuController == null)
            {
                Multiplayer.LogError($"{nameof(uiMenuController)} is null! Destroying self.");
                shouldDestroy = true;
            }

            // If all required components are assigned, call base.Awake(), otherwise destroy self
            if (!shouldDestroy)
            {
                base.Awake();
                return;
            }

            Destroy(this);
        }

        // Switch to the default menu
        public void SwitchToDefaultMenu()
        {
            uiMenuController.SwitchMenu(uiMenuController.defaultMenuIndex);
        }

        // Switch to a specific menu by index
        public void SwitchToMenu(byte index)
        {
            uiMenuController.SwitchMenu(index);
        }

        // Show the rename popup if possible
        [CanBeNull]
        public Popup ShowRenamePopup()
        {
            Multiplayer.Log("public Popup ShowRenamePopup() ...");
            return ShowPopup(renamePopupPrefab);
        }

        // Show the OK popup if possible
        [CanBeNull]
        public Popup ShowOkPopup()
        {
            return ShowPopup(okPopupPrefab);
        }

        // Generic method to show a popup if the PopupManager can show it
        [CanBeNull]
        private Popup ShowPopup(Popup popup)
        {
            if (popupManager.CanShowPopup())
                return popupManager.ShowPopup(popup);

            Multiplayer.LogError($"{nameof(PopupManager)} cannot show popup!");
            return null;
        }

        public void ShowOkPopup(string text, Action onClick)
        {
            var popup = ShowOkPopup();
            if (popup == null) return;

            popup.labelTMPro.text = text;
            popup.Closed += _ => onClick();
        }

        /// <param name="func">A function to apply to the MainMenuPopupManager while the object is disabled</param>
        public static void Create(Action<MainMenuThingsAndStuff> func)
        {
            // Create a new GameObject for MainMenuThingsAndStuff and disable it
            GameObject go = new($"[{nameof(MainMenuThingsAndStuff)}]");
            go.SetActive(false);

            // Add MainMenuThingsAndStuff component and apply the provided function
            MainMenuThingsAndStuff manager = go.AddComponent<MainMenuThingsAndStuff>();
            func.Invoke(manager);

            // Re-enable the GameObject
            go.SetActive(true);
        }
    }
}
