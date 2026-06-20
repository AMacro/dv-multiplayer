using DV.UI;
using DV.UIFramework;
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
    private const int PREVIEW_RT_WIDTH = 512;
    private const int PREVIEW_RT_HEIGHT = 512;
    private const int LAYOUT_PADDING = 15;

    private GameObject selectorGO;
    private Selector characterSelector;

    private Camera previewCamera;
    private GameObject previewRoot;
    private RenderTexture previewRT;
    private RawImage displayImage;
    private ModelRotator modelRotator;

    private GameObject currentModel;
    private int currentIndex;

    protected void Awake()
    {
        LogTransformHierarchy(transform.parent.parent.parent);
        // Grab crosshair selector
        var selector = transform.FindChildByName("Crosshair").gameObject;
        selector.SetActive(false);
        selectorGO = Instantiate(selector);
        selector.SetActive(true);

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
        vlg.childAlignment = TextAnchor.LowerCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(LAYOUT_PADDING, LAYOUT_PADDING, 0, LAYOUT_PADDING);


        SetupPreviewSpawnArea();
        SetupPreviewCamera();
        BuildLayout();

        ShowModel(0);
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
        cameraGo.transform.localPosition = new Vector3(0f, 1f, 3f);
        cameraGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        previewCamera = cameraGo.AddComponent<Camera>();
        previewCamera.cullingMask = 1 << PREVIEW_LAYER;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 10f;
        previewCamera.fieldOfView = 40f;
        previewCamera.enabled = true;

        previewRT = new RenderTexture(PREVIEW_RT_WIDTH, PREVIEW_RT_HEIGHT, 16, RenderTextureFormat.ARGB32);
        previewRT.antiAliasing = 8;
        previewRT.Create();
        previewCamera.targetTexture = previewRT;
    }

    private void BuildLayout()
    {
        var root = new GameObject("CharacterSelectorRoot");
        root.transform.SetParent(transform, false);

        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(1f, 0f);
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

        // Create selector
        selectorGO.transform.SetParent(root.transform, false);

        characterSelector = selectorGO.GetOrAddComponent<Selector>();

        characterSelector.name = "Character Selector";
        characterSelector.LocalizedLabel = false;
        characterSelector.LocalizedValues = false;
        characterSelector.initialized = false;

        
        characterSelector.GetComponent<UIElementTooltip>().enabledKey = Locale.SETTINGS_CHAR_SEL_TOOLTIP_KEY;

        characterSelector.valueTMPro.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
        characterSelector.labelTMPro.gameObject.SetActive(false);

        var lE = selectorGO.GetOrAddComponent<LayoutElement>();
        lE.preferredWidth = 300f;
        lE.preferredHeight = 50f;

        characterSelector.SelectionChanged += CharacterSelector_SelectionChanged;

        // Clean up unnecessary components from the original selector
        var settingSource = selectorGO.GetComponent<SettingChangeSource>();
        if (settingSource != null)
            DestroyImmediate(settingSource);

        I2.Loc.Localize[] i2Locs = selectorGO.GetComponentsInChildren<I2.Loc.Localize>(true);
        if (i2Locs != null)
            foreach (var comp in i2Locs)
                DestroyImmediate(comp);

        DV.Localization.Localize[] dvLocs = selectorGO.GetComponentsInChildren<DV.Localization.Localize>(true);
        if (dvLocs != null)
            foreach (var comp in dvLocs)
                DestroyImmediate(comp);

        // Populate character names
        var characterMeta = Multiplayer.AssetIndex.AllCharacterMetaData();
        List<string> characterIds = characterMeta.Select(metadata => metadata.Id).ToList();
        List<string> characterNames = characterMeta.Select(metadata => metadata.DisplayName).ToList();

        characterSelector.SetValues(characterNames);
        characterSelector.SetLabel(string.Empty);

        characterSelector.SetSelectedIndex(0);
        selectorGO.SetActive(true);
    }

    private void CharacterSelector_SelectionChanged(IClickable clickable, int selectedIndex)
    {
        ShowModel(selectedIndex);
    }

    private void ShowModel(int index)
    {
        if (currentModel != null)
            Destroy(currentModel);

        currentModel = Instantiate(Multiplayer.AssetIndex.playerPrefabs[index], previewRoot.transform);
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;
        currentModel.transform.localScale = Vector3.one;

        currentModel.SetLayersRecursive(PREVIEW_LAYER);

        // Ensure all animators run in unscaled time so they animate in the pause menu
        if (Time.timeScale == 0f)
        {
            var animators = currentModel.GetComponentsInChildren<Animator>();
            foreach (var animator in animators)
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            var springManager = currentModel.GetComponentsInChildren<SpringManager>();
            foreach (var spring in springManager)
                spring.enabled = false;

            var autoBlink = currentModel.GetComponentsInChildren<AutoBlink>();
            foreach (var blink in autoBlink)
                blink.enabled = false;
        }

        if (modelRotator !=null)
            modelRotator.target = currentModel.transform;

#if DEBUG
        // Diagnostic: log where each prefab's bounds sit so you can fix the prefab root offset
        var renderers = currentModel.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            // bounds.min.y relative to previewRoot tells you the Y offset baked into the prefab
            float localMin = bounds.min.y - previewRoot.transform.position.y;
            float localMax = bounds.max.y - previewRoot.transform.position.y;
            Multiplayer.Log($"Prefab '{currentModel.name}': bounds min.y={localMin:F4}, max.y={localMax:F4}, height={localMax - localMin:F4}");
        }

        LogTransformHierarchy(currentModel.transform);
#endif
    }

#if DEBUG
    private static void LogTransformHierarchy(Transform t, string indent = "")
    {
        Multiplayer.Log($"{indent}{t.name}: pos={t.localPosition}, scale={t.localScale}");
        foreach (Transform child in t)
            LogTransformHierarchy(child, indent + "  ");
    }
#endif

    protected void OnDestroy()
    {
        if (previewRT != null)
        {
            previewRT.Release();
            Destroy(previewRT);
        }

        if (previewRoot != null)
            Destroy(previewRoot);
    }
}
