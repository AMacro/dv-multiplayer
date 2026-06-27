using DV.Localization;
using DV.UI;
using DV.UIFramework;
using Multiplayer.Components.MainMenu;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Multiplayer.Utils;

public static class UIHelpers
{
    #region Prefabs
    private static GameObject selectorPrefab;
    private static GameObject togglePrefab;
    private static GameObject sliderPrefab;
    private static GameObject buttonPrefab;
    private static GameObject inputPrefab;
    private static GameObject dividerPrefab;
    private static GameObject scrollViewPrefab;
    private static GameObject hoverImage;
    #endregion

    private static bool initialised = false;
    public static void Initialise()
    {
        if (initialised)
            return;

        var settingsController = GameObject.FindObjectOfType<SettingsController>()?.transform;

        if (settingsController == null)
        {
            Multiplayer.LogError("UIHelpers: Failed to find SettingsController in scene");
            return;
        }

        Multiplayer.LogDebug(() => $"UIHelpers: Found SettingsController in scene: {settingsController?.name}");

        // Grab UI elements to make prefabs
        selectorPrefab = UIHelpers.MakePrefab<Selector>(settingsController);
        togglePrefab = UIHelpers.MakePrefab<ToggleDV>(settingsController);
        sliderPrefab = UIHelpers.MakePrefab<SliderDV>(settingsController);

        var buttonGO = settingsController.parent.FindChildByName("Open Bindings");
        buttonPrefab = UIHelpers.MakePrefab<ButtonDV>(buttonGO);

        hoverImage = buttonPrefab.FindChildByName("[image hover]"); 


        Multiplayer.LogDebug(() => $"UIHelpers: scrollView");

        var scrollGO = settingsController.parent.FindChildByName("Scroll View").gameObject;
        scrollViewPrefab = UIHelpers.MakePrefab(scrollGO);

        GameObject goMMC = GameObject.FindObjectOfType<MainMenuController>().gameObject;
        var divider = goMMC.FindChildByName("Divider");
        dividerPrefab = UIHelpers.MakePrefab(divider);

        var inputGo = MainMenuThingsAndStuff.Instance.references.popupTextInput.gameObject.FindChildByName("TextFieldTextIcon");
        inputPrefab = UIHelpers.MakePrefab(inputGo);

        initialised = true;
    }


    #region Prefab Helpers
    public static GameObject MakePrefab<T>(Transform parent, bool recreateType = false) where T : Component
    {
        Multiplayer.LogDebug(() => $"Making prefab for {typeof(T)?.Name}, parent: {parent?.name}");
        var target = parent.GetComponentInChildren<T>()?.gameObject;

        if (target == null)
            return null;

        var prefab = MakePrefab(target);

        if (recreateType)
        {
            var interactableEffect = prefab.GetComponents<InteractableEffect>();
            if (interactableEffect != null)
            {
                foreach (var effect in interactableEffect)
                    GameObject.DestroyImmediate(effect);

                prefab.AddComponent<InteractableEffect>();
            }

            var selector = prefab.GetComponent<T>();
            if (selector != null)
            {
                GameObject.DestroyImmediate(selector);
                prefab.AddComponent<T>();
            }
        }

        return prefab;
    }

    public static GameObject MakePrefab(GameObject target)
    {
        Multiplayer.LogDebug(() => $"Making prefab for {target?.name}");
        target.SetActive(false);
        var instance = GameObject.Instantiate(target);
        target.SetActive(true);

        var settingChangeSource = instance.GetComponent<SettingChangeSource>();
        if (settingChangeSource != null)
            GameObject.DestroyImmediate(settingChangeSource);

        // Remove any I2 localization components, they will be recreated when the component is used
        var locs = instance.GetComponentsInChildren<I2.Loc.Localize>(true);
        foreach (var loc in locs)
            GameObject.DestroyImmediate(loc);

        GameObject.DontDestroyOnLoad(instance);

        return instance;
    }

    #endregion

    #region ControlGenerators
    public static ToggleDV CreateToggle(RectTransform parent, string name, string label_key, bool initialValue)
    {
        var go = GameObject.Instantiate(togglePrefab, parent);
        go.name = name;

        var toggle = go.GetComponent<ToggleDV>();
        toggle.isOn = initialValue;

        var labelGo = go.FindChildByName("text");
        labelGo.GetComponent<Localize>().key = label_key;
        go.gameObject.ResetTooltip();

        go.SetActive(true);

        return toggle;
    }

    public static TMP_InputField CreateInputField(RectTransform parent, string name, string placeholderText, string initialValue, bool hoverable, int characterLimit = 0)
    {
        var go = GameObject.Instantiate(inputPrefab, parent);
        go.name = name;

        var input = go.GetComponent<TMP_InputField>();
        input.text = initialValue ?? string.Empty;

        if (characterLimit > 0)
            input.characterLimit = characterLimit;

        var placeholder = input.placeholder?.GetComponent<TMP_Text>();
        if (placeholder != null)
            placeholder.text = placeholderText;

        if (hoverable)
        {
            CreateHoverImage(input.transform);
            var hov = input.gameObject.GetOrAddComponent<SimpleHoverable>();
            hov.addEffects = true;
            var usernameTooltip = input.GetOrAddComponent<UIElementTooltip>();
            usernameTooltip.hoverable = hov;
        }

        go.SetActive(true);

        return input;
    }

    public static TMP_InputField CreateInputFieldMultiline(RectTransform parent, string name, string placeholderText, string initialValue, bool hoverable, int characterLimit = 0, float width = -1, float height = -1)
    {
        var newInput = CreateInputField(parent, name, placeholderText, initialValue, hoverable, characterLimit);

        // Set the size of the input field if width and height are specified
        if (width > -1 || height > -1)
        {
            if (width < 0)
                width = newInput.transform.GetComponent<RectTransform>().sizeDelta.x;
            if (height < 0)
                height = newInput.transform.GetComponent<RectTransform>().sizeDelta.y;

            newInput.transform.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
        }
 
        newInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        newInput.textComponent.alignment = TextAlignmentOptions.TopLeft;

        var placeholder = newInput.placeholder as TMP_Text;
        if (placeholder != null)
            placeholder.alignment = TextAlignmentOptions.TopLeft;


        //scroll to top of details field if it has a value
        newInput.caretPosition = 0;

        return newInput;
    }

    public static Selector CreateSelector(RectTransform parent, string objectName, string label, bool localisedLabel, bool localisedValues, List<string> values, int selectedIndex)
    {
        var newSelectorGO = GameObject.Instantiate(selectorPrefab, parent);
        newSelectorGO.name = objectName;

        var selector = newSelectorGO.GetComponent<Selector>();

        // Strip any existing localization so we can set values directly
        if (selector.labelTMPro?.gameObject.TryGetComponent<I2.Loc.Localize>(out var i2loc) ?? false)
            GameObject.DestroyImmediate(i2loc);
        if (selector.labelTMPro?.gameObject.TryGetComponent<Localize>(out var dvloc) ?? false)
            GameObject.DestroyImmediate(dvloc);

        selector.LocalizedLabel = localisedLabel;
        selector.SetLabel(label);

        if (localisedLabel)
            selector.labelTMPro.GetOrAddComponent<Localize>().key = label;

        selector.LocalizedValues = localisedValues;
        selector.SetValues(values);
        selector.SetSelectedIndex(selectedIndex);

        newSelectorGO.ResetTooltip();

        newSelectorGO.SetActive(true);
        selector.ToggleInteractable(true);

        return selector;
    }

    public static SliderDV CreateSlider(RectTransform parent, string objectName, string label, bool localisedLabel, string localisedValueKey, int initialValue, int increment, int minValue, int maxValue)
    {
        var newSliderGO = GameObject.Instantiate(sliderPrefab, parent);
        newSliderGO.name = objectName;

        var slider = newSliderGO.GetComponent<SliderDV>();

        var labelGo = newSliderGO.FindChildByName("[text label]");

        // Strip any existing localization so we can set values directly
        var locs = labelGo?.GetComponentsInChildren<I2.Loc.Localize>(true);
        if (locs != null)
            foreach (var loc in locs)
                GameObject.DestroyImmediate(loc);


        if (localisedLabel)
            labelGo.GetOrAddComponent<Localize>().key = label;
        else
            labelGo.GetComponent<TMP_Text>().text = label;

        newSliderGO.ResetTooltip();

        slider.localizeValueKey = localisedValueKey;

        slider.stepIncrement = 1;
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = Mathf.Clamp(initialValue, minValue, maxValue);
        newSliderGO.SetActive(true);
        slider.ToggleInteractable(true);

        return slider;
    }

    public static ButtonDV CreateButton(RectTransform parent, string name, string label_key)
    {
        var go = GameObject.Instantiate(buttonPrefab, parent);
        go.name = name;

        var button = go.GetComponent<ButtonDV>();
        var inteff = go.GetComponent<InteractableEffect>();
        GameObject.DestroyImmediate(inteff);
        GameObject.DestroyImmediate(button);
        button = go.AddComponent<ButtonDV>();

        button.GetComponentInChildren<Localize>().key = label_key;
        go.gameObject.ResetTooltip();

        go.SetActive(true);

        return button;
    }

    public static void CreateDivider(RectTransform parent)
    {
        var go = GameObject.Instantiate(dividerPrefab, parent);
        go.name = "Divider";
        go.SetActive(true);
    }

    public static GameObject CreateHoverImage(Transform parent)
    {
        var go = GameObject.Instantiate(hoverImage, parent);
        go.name = "[image hover]";
        go.SetActive(true);
        return go;
    }
    #endregion
}
