using DV.UIFramework;
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Multiplayer.Components.UI.Controls;

[RequireComponent(typeof(IMarkable))]
public abstract class MPViewElement<T> : NullCheckingMonoBehaviour, ISelectHandler, IEventSystemHandler, IPointerEnterHandler, IPointerExitHandler
{
    public event Action<MPViewElement<T>> SelectionRequested;
    public event Action<MPViewElement<T>, bool> HoverChanged;

    public abstract bool IsPlaceholder { get; }

    public virtual void SetSelected(bool selected)
    {
        //Multiplayer.LogDebug(() =>
        //{
        //    var data = GetComponent<ServerBrowserElement>();
        //    return $"MPViewElement.SetSelected() {data?.name}";
        //});

        if (TryGetComponent<IMarkable>(out var component))
        {
            component.ToggleMarked(selected);
        }
    }

    public virtual void SetInteractable(bool interactable)
    {
        if (TryGetComponent<IMarkable>(out var component))
        {
            component.ToggleInteractable(interactable);
        }
    }

    public virtual void OnSelect(BaseEventData eventData)
    {
        
        //Multiplayer.LogDebug(()=>
        //{
        //    var data = GetComponent<ServerBrowserElement>();
        //    return $"MPViewElement.OnSelect() {data?.name}";
        //});

        SelectionRequested?.Invoke(this);
    }

    public virtual void OnPointerEnter(PointerEventData eventData)
    {
        HoverChanged?.Invoke(this, arg2: true);
    }

    public virtual void OnPointerExit(PointerEventData eventData)
    {
        HoverChanged?.Invoke(this, arg2: false);
    }

    public abstract void SetData(T data);
}
