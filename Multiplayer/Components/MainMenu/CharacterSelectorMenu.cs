using DV.UI;
using DV.UIFramework;
using Multiplayer.Components.UI.Settings;
using Multiplayer.Models;
using Multiplayer.Utils;
using System.Collections.Generic;
using System.Linq;
using UnityChan;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu;

public class CharacterSelectorMenu : MonoBehaviour
{
    private const int PREVIEW_LAYER = 31;
    private const int PREVIEW_RT_WIDTH = 435;
    private const int PREVIEW_RT_HEIGHT = 435;
    private const int LAYOUT_PADDING = 15;

    const float TARGET_FOV = 40f;
    const float BASE_DISTANCE = 3f;
    const float BASE_HEIGHT = 1f;

    public int CharacterSelectorMenuIndex;
    public UIMenuController MenuController;
    public GameObject BottomButtons;
    public ButtonDV ApplyButton;
    public ButtonDV DiscardButton;

    private GameObject selectorGO;
    private Selector characterSelector;

    private Camera previewCamera;
    private GameObject previewRoot;
    private RenderTexture previewRT;
    private RawImage displayImage;
    private ModelRotator modelRotator;

    private GameObject previewModel;
    private List<string> characterIds = [];
    private int defaultModelIndex = 0;
    private int indexFromSettings = 0;

    bool returningToSettingsMenu = false;

    protected void Awake()
    {
        Multiplayer.PlayerModelRegistry.Reload();

        // Clean up child objects
        for (int i = 0; i < transform.childCount; i++)
            Destroy(transform.GetChild(i).gameObject);

        // Remove layout components
        var existingGrid = GetComponent<GridLayoutGroup>();
        if (existingGrid != null)
            DestroyImmediate(existingGrid);

        var existingCSF = GetComponent<ContentSizeFitter>();
        if (existingCSF != null)
            DestroyImmediate(existingCSF);

        // Stretch to fill whatever space the parent gives us
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(LAYOUT_PADDING, LAYOUT_PADDING, LAYOUT_PADDING, 80);

        SetupPreviewSpawnArea();
        SetupPreviewCamera();
        BuildLayout();
    }

    protected void OnEnable()
    {
        if (BottomButtons != null)
        {
            BottomButtons.SetActive(true);
            ApplyButton.Clicked += ApplyChanges;
            DiscardButton.Clicked += DiscardChanges;

            DiscardButton.ToggleInteractable(true);
        }

        Settings.OnSettingsUpdated += SettingsChanged;

        // Find current index based on settings
        indexFromSettings = characterIds.FindIndex(id => id == Multiplayer.Settings.CharacterId);

        if (indexFromSettings < 0)
            indexFromSettings = 0;

        if (previewCamera != null)
        {
            previewCamera.enabled = true;

            // Bypass normal rendering path to disable stereoscopic rendering in VR mode
            if (VRManager.IsVREnabled())
                previewCamera.renderingPath = RenderingPath.DeferredShading;
        }

        characterSelector.SetSelectedIndex(indexFromSettings);
        ShowModel(indexFromSettings);
    }

    protected void OnDisable()
    {
        if (BottomButtons != null)
        {
            if (!returningToSettingsMenu)
                BottomButtons.SetActive(false);

            returningToSettingsMenu = false;

            ApplyButton.Clicked -= ApplyChanges;
            DiscardButton.Clicked -= DiscardChanges;

            ApplyButton.ToggleInteractable(false);
            DiscardButton.ToggleInteractable(false);
        }

        if (previewCamera != null)
        {
            previewCamera.enabled = false;

            // Reset the rendering path to avoid crashing when returning to the main menu or quitting the game
            if (VRManager.IsVREnabled())
                previewCamera.renderingPath = RenderingPath.UsePlayerSettings;
        }

        Settings.OnSettingsUpdated -= SettingsChanged;
    }

    private void SetupPreviewSpawnArea()
    {
        // Set up a location for the model
        previewRoot = new GameObject("CharacterPreviewRoot");
        previewRoot.transform.position = new Vector3(0f, -1000f, 0f);

        // Add lighting to override main menu lighting
        var lightGo = new GameObject("CharacterPreviewLight");
        lightGo.transform.SetParent(previewRoot.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 3f, 2f);
        lightGo.transform.localRotation = Quaternion.Euler(45f, 180f, 0f);
        var previewLight = lightGo.AddComponent<Light>();
        previewLight.type = LightType.Directional;
        previewLight.color = Color.white;
        previewLight.intensity = 1f;
        previewLight.cullingMask = 1 << PREVIEW_LAYER;
    }

    private void SetupPreviewCamera()
    {
        // Set up the preview camera
        var cameraGo = new GameObject("CharacterPreviewCamera");
        cameraGo.transform.SetParent(previewRoot.transform, false);
        cameraGo.transform.localPosition = new Vector3(0f, BASE_HEIGHT, BASE_DISTANCE);
        cameraGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        previewCamera = cameraGo.AddComponent<Camera>();
        previewCamera.cullingMask = 1 << PREVIEW_LAYER;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 10f;
        previewCamera.stereoTargetEye = StereoTargetEyeMask.None;
        previewCamera.depth = 1f;

        Multiplayer.LogDebug(() => $"CharacterSelectorMenu.SetupPreviewCamera() mainCameraDepth: {Camera.main.depth}");

        RenderTextureDescriptor rtDescriptor = new RenderTextureDescriptor(PREVIEW_RT_WIDTH, PREVIEW_RT_HEIGHT, RenderTextureFormat.ARGB32, 24);
        rtDescriptor.vrUsage = VRTextureUsage.None;
        rtDescriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;

        previewRT = new RenderTexture(rtDescriptor);
        previewRT.antiAliasing = 8;
        previewRT.Create();

        previewCamera.targetTexture = previewRT;
        previewCamera.fieldOfView = TARGET_FOV;

        previewCamera.enabled = false;
    }

    private void BuildLayout()
    {
        var root = new GameObject("CharacterSelectorRoot");
        root.transform.SetParent(transform, false);

        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.LowerCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;

        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildCharacterSelector(root);

        // Add in preview image
        var previewContainer = new GameObject("CharacterPreviewContainer");
        previewContainer.transform.SetParent(root.transform, false);
        var previewContainerRect = previewContainer.AddComponent<RectTransform>();

        var previewLE = previewContainer.AddComponent<LayoutElement>();
        previewLE.preferredWidth = PREVIEW_RT_WIDTH;
        previewLE.preferredHeight = PREVIEW_RT_HEIGHT;
        previewLE.flexibleWidth = 1f;

        var imageGo = new GameObject("CharacterPreviewDisplay");
        imageGo.transform.SetParent(previewContainer.transform, false);

        var imageRect = imageGo.AddComponent<RectTransform>();
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = Vector2.zero;
        imageRect.sizeDelta = new Vector2(PREVIEW_RT_WIDTH, PREVIEW_RT_HEIGHT);

        displayImage = imageGo.AddComponent<RawImage>();
        displayImage.texture = previewRT;
        displayImage.color = Color.white;

        var fitter = imageGo.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = (float)PREVIEW_RT_WIDTH / PREVIEW_RT_HEIGHT;

        modelRotator = imageGo.AddComponent<ModelRotator>();

        var hoverable = previewContainer.GetOrAddComponent<SimpleHoverable>();
        hoverable.addEffects = true;
        previewContainer.AddComponent<UIElementTooltip>().enabledKey = Locale.SETTINGS_CHAR_HOVER_TOOLTIP_KEY;
    }

    private void BuildCharacterSelector(GameObject root)
    {
        // Create selector
        // Get character names
        var characterMeta = Multiplayer.PlayerModelRegistry.Models;
        characterIds = characterMeta.Select(metadata => metadata.CharacterId).ToList();
        List<string> characterNames = characterMeta.Select(metadata => metadata.DisplayName).ToList();

        // Create selector
        var rt = root.GetComponent<RectTransform>();
        characterSelector = UIHelpers.CreateSelector(rt, "Character Selector", string.Empty, false, false, characterNames, 0);
        selectorGO = characterSelector.gameObject;

        characterSelector.GetComponent<UIElementTooltip>().enabledKey = Locale.SETTINGS_CHAR_SEL_TOOLTIP_KEY;

        characterSelector.valueTMPro.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
        characterSelector.labelTMPro.gameObject.SetActive(false);

        var lE = selectorGO.GetOrAddComponent<LayoutElement>();
        lE.preferredWidth = 300f;
        lE.preferredHeight = 50f;

        characterSelector.SelectionChanged += CharacterSelector_SelectionChanged;

        // If the index from settings is 0 no model will be shown and selectedIndex will not update
        defaultModelIndex = characterIds.FindIndex(id => id == Multiplayer.PlayerModelRegistry.DefaultModel.CharacterId);
        ShowModel(defaultModelIndex);

        selectorGO.SetActive(true);
    }

    private void ShowModel(int index)
    {
        PlayerModelInfo model;

        if (previewModel != null)
            Destroy(previewModel);

        if (index < 0 || index >= characterIds.Count)
            model = Multiplayer.PlayerModelRegistry.DefaultModel;
        else
            model = Multiplayer.PlayerModelRegistry.GetModelById(characterIds[index]);

        previewModel = Instantiate(model.Prefab, previewRoot.transform);
        previewModel.transform.localPosition = Vector3.zero;
        previewModel.transform.localRotation = Quaternion.identity;
        previewModel.transform.localScale = Vector3.one;

        previewModel.SetLayersRecursive(PREVIEW_LAYER);

        // Ensure all animators run in unscaled time so they animate in the pause menu
        if (Time.timeScale == 0f)
        {
            var animators = previewModel.GetComponentsInChildren<Animator>();
            foreach (var animator in animators)
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            var springManager = previewModel.GetComponentsInChildren<SpringManager>();
            foreach (var spring in springManager)
                spring.enabled = false;

            var autoBlink = previewModel.GetComponentsInChildren<AutoBlink>();
            foreach (var blink in autoBlink)
                blink.enabled = false;
        }

        if (modelRotator != null)
            modelRotator.target = previewModel.transform;
    }

    private void CharacterSelector_SelectionChanged(IClickable clickable, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= characterIds.Count)
        {
            selectedIndex = 0;
            characterSelector.SelectedIndex = 0;
        }

        ShowModel(selectedIndex);

        if (selectedIndex != indexFromSettings)
            ApplyButton.interactable = true;
        else
            ApplyButton.interactable = false;
    }

    private void SettingsChanged(Settings settings)
    {
        int newIndex = characterIds.FindIndex(id => id == settings.CharacterId);
        if (newIndex < 0 || newIndex >= characterIds.Count)
            newIndex = 0;

        // Only update the selector if the setting has changed
        if (newIndex != indexFromSettings)
        {
            indexFromSettings = newIndex;
            characterSelector.SetSelectedIndex(newIndex);
        }
    }

    private void DiscardChanges(IClickable clickable)
    {
        // Reset all settings to their current values
        SettingsChanged(Multiplayer.Settings);

        returningToSettingsMenu = true;

        MenuController.SwitchMenu(CharacterSelectorMenuIndex);
    }

    private void ApplyChanges(IClickable clickable)
    {
        if (characterSelector.SelectedIndex < 0 || characterSelector.SelectedIndex >= characterIds.Count)
            return;

        Multiplayer.Settings.CharacterId = characterIds[characterSelector.SelectedIndex];

        indexFromSettings = characterSelector.SelectedIndex;
        Multiplayer.Settings.Save(Multiplayer.ModEntry);

        returningToSettingsMenu = true;

        MenuController.SwitchMenu(CharacterSelectorMenuIndex);
    }

    protected void OnDestroy()
    {
        if (previewCamera != null)
        {
            previewCamera.enabled = false;

            // Reset the rendering path to avoid crashing when returning to the main menu or quitting the game
            previewCamera.renderingPath = RenderingPath.UsePlayerSettings;
            previewCamera.targetTexture = null;
            DestroyImmediate(previewCamera.gameObject);
        }

        if (displayImage != null)
            displayImage.texture = null;

        if (previewRT != null)
        {
            previewRT.Release();
            DestroyImmediate(previewRT);
            previewRT = null;
        }

        if (previewRoot != null)
        {
            DestroyImmediate(previewRoot);
            previewRoot = null;
        }
    }
}
