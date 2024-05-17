using System;
using System.Reflection;
using DV.UIFramework;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Multiplayer.Components.MainMenu;
public class PopupTextInputFieldControllerNoValidation : MonoBehaviour, IPopupSubmitHandler
{
    public Popup popup;
    public TMP_InputField field;
    public ButtonDV confirmButton;

    private void Awake()
    {
        //Find the components

        popup = this.GetComponentInParent<Popup>();
        field = popup.GetComponentInChildren<TMP_InputField>();

        foreach(ButtonDV btn in popup.GetComponentsInChildren<ButtonDV>())
        {
            if (btn.name == "ButtonYes")
            {
                confirmButton = btn;
            }
        }

        //patch us in as the new handler for the dialogue
        typeof(Popup).GetField("handler", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(popup, this);

    }

    private void Start()
    {
        field.onValueChanged.AddListener(new UnityAction<string>(OnInputValueChanged));
        OnInputValueChanged(field.text);
        field.Select();
        field.ActivateInputField();

    }
    private void OnInputValueChanged(string value)
    {
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
                Debug.LogError(string.Format("Unhandled action {0}", action), this);
                break;
        }
    }


    private bool IsInputValid(string value)
    {
        return true;// !string.IsNullOrWhiteSpace(value);
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
