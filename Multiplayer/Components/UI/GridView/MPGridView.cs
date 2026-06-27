using DV.UIFramework;
using Multiplayer.Components.UI.ServerBrowser;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.UI.Controls;

public class MPGridView<T> : AUIView
{
    public delegate void IndexChangeDelegate(MPGridView<T> sender);
    public event IndexChangeDelegate SelectedIndexChanged;
    public event IndexChangeDelegate HoveredIndexChanged;

    // Core properties
    public GameObject viewElementPrefab;
    public GameObject placeholderElementPrefab;
    public bool showPlaceholderWhenEmpty = true;
    public bool allowHoveringAndSelecting = true;

    // Internal state
    private readonly List<T> _items = [];
    private MPViewElement<T> _selectedItem;
    private MPViewElement<T> _hoveredItem;
    private bool _placeholderVisible = false;
    private bool previousInteractability;

    // Components
    private ScrollRect _scrollBar;


    // Gridview properties
    public IReadOnlyList<T> Items => _items.AsReadOnly();
    public int SelectedIndex { get; private set; }
    public int HoveredIndex { get; private set; }

    public T SelectedItem
    {
        get
        {
            return (SelectedIndex >= 0 && SelectedIndex < _items.Count) ?
                _items[SelectedIndex]
                : default;
        }
    }

    // Item access methods
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _items.Count)
                return default;
            return _items[index];
        }
    }

    public bool Contains(T item) => _items.Contains(item);
    public int IndexOf(T item) => _items.IndexOf(item);
    public int FindIndex(Predicate<T> match) => _items.FindIndex(match);
    public T Find(Predicate<T> match) => _items.Find(match);

    public void Clear()
    {
        // Clear selection
        _selectedItem = null;
        _hoveredItem = null;
        SelectedIndex = -1;
        HoveredIndex = -1;

        // Remove all child elements
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        // Clear items list
        _items.Clear();
        _placeholderVisible = false;

        // Show placeholder if needed
        UpdatePlaceholder();

        // Notify selection changed
        SelectedIndexChanged?.Invoke(this);
    }

    // Add a single item
    public void AddItem(T item)
    {
        _items.Add(item);
        CreateViewElement(item);
        UpdatePlaceholder();
    }

    // Add multiple items
    public void AddItems(IEnumerable<T> items)
    {
        if (items == null)
            return;

        foreach (var item in items)
        {
            _items.Add(item);
            CreateViewElement(item);
        }

        UpdatePlaceholder();
    }

    // Remove an item
    public void RemoveItem(T item)
    {
        int index = _items.IndexOf(item);
        if (index >= 0)
        {
            RemoveItemAt(index);
        }
    }

    // Remove an item at a specific index
    public void RemoveItemAt(int index)
    {
        if (index < 0 || index >= _items.Count || _placeholderVisible)
            return;

        // Check if we're removing the selected item
        if (_selectedItem != null && _selectedItem.transform.GetSiblingIndex() == index)
        {
            _selectedItem = null;
            SelectedIndexChanged?.Invoke(this);
        }

        // Remove the view element
        if (index < transform.childCount)
        {

            Destroy(transform.GetChild(index).gameObject);
        }

        // Remove from items list
        _items.RemoveAt(index);

        // Update placeholder
        UpdatePlaceholder();
    }

    // Sort items using a comparison function
    public void SortItems(Comparison<T> comparison)
    {
        if (_items.Count <= 1)
            return;

        // Remember selected item
        T selectedItem = SelectedItem;

        // Sort the items list
        _items.Sort(comparison);

        // Rebuild view elements
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            // Skip placeholder if it exists
            if (_placeholderVisible && i == 0)
                continue;

            Destroy(transform.GetChild(i).gameObject);
        }

        // Recreate view elements in sorted order
        foreach (var item in _items)
        {
            CreateViewElement(item);
        }

        // Restore selection if possible
        if (selectedItem != null)
        {
            int newIndex = _items.IndexOf(selectedItem);
            if (newIndex >= 0)
            {
                SetSelected(newIndex, true);
            }
        }
    }

    // Set selected item by index
    public void SetSelected(int index, bool scrollToItem = true)
    {
        Multiplayer.LogDebug(() => $"MPGridView.SetSelected({index}, {scrollToItem}) child count: {transform.childCount}");

        if (index < 0 || index >= _items.Count || _placeholderVisible)
            return;

        Multiplayer.LogDebug(() => $"MPGridView.SetSelected({index}, {scrollToItem}) items count: {index}");

        if (index >= transform.childCount)
            return;

        Transform child = transform.GetChild(index);
        MPViewElement<T> element = child.GetComponent<MPViewElement<T>>();

        Multiplayer.LogDebug(() =>
        {
            var el = element as IServerBrowserGameDetails;
            return $"MPGridView.SetSelected({index}, {scrollToItem}) {element?.name}";

        });

        if (element != null)
        {
            UpdateSelectedItem(element);
        }

        if (scrollToItem)
            ScrollToItem();
    }

    private void ScrollToItem()
    {
        if (_scrollBar != null && _items.Count > 0 && SelectedIndex >= 0)
        {
            if (_scrollBar.content != null && _scrollBar.viewport != null &&
                    _selectedItem && _selectedItem.TryGetComponent<RectTransform>(out var itemRect)
                )
            {
                // Get the content RectTransform
                RectTransform contentRect = _scrollBar.content;

                // Calculate the normalized position based on the item's position within the content
                float itemPosition = itemRect.anchoredPosition.y;
                float contentHeight = contentRect.rect.height;

                if (contentHeight == 0)
                    return;

                // Adjust for the viewport height to center the item
                float viewportHeight = _scrollBar.viewport.rect.height;
                float adjustment = viewportHeight * 0.5f / contentHeight;

                // Set the normalized position (clamped between 0 and 1)
                float normalizedPos = Mathf.Clamp01(itemPosition / contentHeight + adjustment);
                _scrollBar.verticalNormalizedPosition = 1f - normalizedPos;
            }
            else if(_items.Count != 0)
            {
                _scrollBar.verticalNormalizedPosition = 1f - (float)SelectedIndex / (float)_items.Count;
            }
        }
    }

    // Get view element at index
    public MPViewElement<T> GetElementAt(int index)
    {
        if (index < 0 || index >= _items.Count || _placeholderVisible)
            return null;

        if (index < transform.childCount)
        {
            return transform.GetChild(index).GetComponent<MPViewElement<T>>();
        }

        return null;
    }

    // Create a view element for an item
    private GameObject CreateViewElement(T item)
    {
        if (viewElementPrefab == null)
            return null;

        GameObject element = Instantiate(viewElementPrefab, transform);
        MPViewElement<T> viewElement = element.GetComponent<MPViewElement<T>>();

        viewElement.SetData(item);
        viewElement.SetInteractable(allowHoveringAndSelecting);
        viewElement.SelectionRequested += UpdateSelectedItem;
        viewElement.HoverChanged += UpdateHoverState;

        return element;
    }

    // Create placeholder element
    private GameObject CreatePlaceholderElement()
    {
        if (placeholderElementPrefab == null)
            return null;

        GameObject element = Instantiate(placeholderElementPrefab, transform);
        element.transform.SetAsFirstSibling();

        MPViewElement<T> viewElement = element.GetComponent<MPViewElement<T>>();
        viewElement.SetInteractable(false);

        return element;
    }

    // Update placeholder visibility
    private void UpdatePlaceholder()
    {
        bool shouldShowPlaceholder = _items.Count == 0 && showPlaceholderWhenEmpty;

        // If placeholder state hasn't changed, do nothing
        if (_placeholderVisible == shouldShowPlaceholder)
            return;

        _placeholderVisible = shouldShowPlaceholder;

        // Remove existing placeholder if it exists
        if (!shouldShowPlaceholder && transform.childCount > 0)
        {
            // Check for any placeholder
            MPViewElement<T>[] placeholders = transform.GetComponentsInChildren<MPViewElement<T>>().Where(e => e.IsPlaceholder).ToArray();

            for (int i = 0; i < placeholders.Length; i++)
                Destroy(placeholders[i].gameObject);
        }

        // Add placeholder if needed
        if (shouldShowPlaceholder)
        {
            CreatePlaceholderElement();
        }
    }

    // Handle selection changes
    private void UpdateSelectedItem(MPViewElement<T> element)
    {
        _selectedItem?.SetSelected(false);

        if (_placeholderVisible)
        {
            _selectedItem = null;
            SelectedIndex = -1;
            HoveredIndex = -1;
            SelectedIndexChanged?.Invoke(this);
            return;
        }

        _selectedItem = element;
        _selectedItem.SetSelected(true);

        SelectedIndex = element.transform.GetSiblingIndex();

        SelectedIndexChanged?.Invoke(this);
    }

    // Handle hover state changes
    private void UpdateHoverState(MPViewElement<T> element, bool hovered)
    {
        _hoveredItem = hovered ? element : null;

        if (_hoveredItem != null)
            HoveredIndex = element.transform.GetSiblingIndex();
        else
            HoveredIndex = -1;

        HoveredIndexChanged?.Invoke(this);
    }

    // Update interactability of all elements
    private void UpdateInteractability()
    {
        foreach (Transform child in transform)
        {
            MPViewElement<T> element = child.GetComponent<MPViewElement<T>>();
            if (element != null)
            {
                element.SetInteractable(allowHoveringAndSelecting);

                if (!allowHoveringAndSelecting)
                {
                    element.SetSelected(false);
                }
            }
        }
    }

    protected virtual void Awake()
    {
        ValidatePrefabs();

        if (_scrollBar == null)
            _scrollBar = GetComponentInParent<ScrollRect>();
    }

    protected virtual void OnValidate()
    {
        ValidatePrefabs();
    }

    private void ValidatePrefabs()
    {
        if (viewElementPrefab != null)
        {
            var viewElement = viewElementPrefab.GetComponent<MPViewElement<T>>();
            if (viewElement == null)
            {
                Multiplayer.LogError($"View element prefab must have an MPViewElement<{typeof(T).Name}> component");
                viewElementPrefab = null;
            }
            else if (viewElement.IsPlaceholder)
            {
                Multiplayer.LogError($"View element prefab must not be a placeholder");
                viewElementPrefab = null;
            }

            if (viewElementPrefab.GetComponent<IMarkable>() == null)
            {
                Multiplayer.LogError($"View element prefab must have an IMarkable component");
                viewElementPrefab = null;
            }
        }

        if (placeholderElementPrefab != null)
        {
            var placeholderElement = placeholderElementPrefab.GetComponent<MPViewElement<T>>();
            if (placeholderElement == null)
            {
                Multiplayer.LogError($"Placeholder element prefab must have an MPViewElement<{typeof(T).Name}> component");
                placeholderElementPrefab = null;
            }
            else if (placeholderElement.IsPlaceholder == false)
            {
                Multiplayer.LogError($"Placeholder element prefab must be a placeholder");
                placeholderElementPrefab = null;
            }

            if (placeholderElementPrefab.GetComponent<IMarkable>() == null)
            {
                Multiplayer.LogError($"Placeholder element prefab must have an IMarkable component");
                placeholderElementPrefab = null;
            }
        }
    }

    protected void Update()
    {
        if (previousInteractability != allowHoveringAndSelecting)
        {
            previousInteractability = allowHoveringAndSelecting;
            UpdateInteractability();
        }
    }
}
