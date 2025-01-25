using System;
using System.Reflection;
using DV.UIFramework;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Multiplayer.Components.MainMenu
{
    public class PopupTextInputFieldControllerNoValidation : MonoBehaviour, IPopupSubmitHandler
    {
        public Popup popup;
        public TMP_InputField field;
        public ButtonDV confirmButton;

        private void Awake()
        {
            // Find the components
            popup = this.GetComponentInParent<Popup>();
            field = popup.GetComponentInChildren<TMP_InputField>();

            foreach (ButtonDV btn in popup.GetComponentsInChildren<ButtonDV>())
            {
                if (btn.name == "ButtonYes")
                {
                    confirmButton = btn;
                }
            }

            // Set this instance as the new handler for the dialog
            typeof(Popup).GetField("handler", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(popup, this);
        }

        private void Start()
        {
            // Add listener for input field value changes
            field.onValueChanged.AddListener(new UnityAction<string>(OnInputValueChanged));
            OnInputValueChanged(field.text);
            field.Select();
            field.ActivateInputField();
        }

        private void OnInputValueChanged(string value)
        {
            // Toggle confirm button interactability based on input validity
            confirmButton.ToggleInteractable(IsInputValid(value));
        }

        public void HandleAction(PopupClosedByAction action)
        {
            switch (action)
            {
                case PopupClosedByAction.Positive:
                    if (IsInputValid(field.text))
                    {
                        RequestPositive();
                        return;
                    }
                    break;
                case PopupClosedByAction.Negative:
                    RequestNegative();
                    return;
                case PopupClosedByAction.Abortion:
                    RequestAbortion();
                    return;
                default:
                    Multiplayer.LogError(string.Format("Unhandled action {0}", action));
                    break;
            }
        }

        private bool IsInputValid(string value)
        {
            // Always return true to disable validation
            return true;
        }

        private void RequestPositive()
        {
            this.popup.RequestClose(PopupClosedByAction.Positive, this.field.text);
        }

        private void RequestNegative()
        {
            this.popup.RequestClose(PopupClosedByAction.Negative, null);
        }

        private void RequestAbortion()
        {
            this.popup.RequestClose(PopupClosedByAction.Abortion, null);
        }
    }
}
