using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.UI.Settings;

public class KeyBindInterface : SingletonBehaviour<KeyBindInterface>
{
    const float TIME_OUT = 5f;

    bool overlayVisible = false;
    GameObject overlay;
    TMP_Text overlayText;

    protected override void Awake()
    {
        base.Awake();

        Canvas[] canvases = GameObject.FindObjectsOfType<Canvas>();

        Canvas canvas = (canvases.Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).FirstOrDefault() ??
                        canvases.Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceCamera).FirstOrDefault()) ??
                        canvases.First();


        overlay = new GameObject("KeyBindOverlay");
        overlay.transform.SetParent(canvas.transform, false);

        var panelRect = overlay.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        var bgImage = overlay.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.75f);

        var canvasGroup = overlay.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        var textGO = new GameObject("OverlayText");
        textGO.transform.SetParent(overlay.transform, false);

        var textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        overlayText = textGO.AddComponent<TextMeshProUGUI>();
        overlayText.text = Locale.SETTINGS_KEY_BINDING_OVERLAY;
        overlayText.alignment = TextAlignmentOptions.Center;
        overlayText.fontSize = 36;
        overlayText.color = Color.white;
    }

    public void GetKeyBind(Action<KeyCode> callback)
    {
        if (overlayVisible)
        {
            Multiplayer.LogWarning("Key bind overlay is already visible.");
            return;
        }

        StartCoroutine(WaitForKeyPress(callback));
    }

    private IEnumerator WaitForKeyPress(Action<KeyCode> callback)
    {
        ToggleOverlay(true);

        yield return null;

        float startTime = Time.unscaledTime;

        KeyCode pressedKey = KeyCode.None;

        while (pressedKey == KeyCode.None)
        {
            if (Input.anyKeyDown)
            {
                foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
                {
                    if (Input.GetKeyDown(keyCode) && keyCode.IsKeyboardKey())
                        pressedKey = keyCode;
                }
            }

            // If the user takes too long to press a key, exit the loop and return KeyCode.None
            if (Time.unscaledTime - startTime > TIME_OUT)
                break;

            int displayTime = Mathf.CeilToInt(TIME_OUT - (Time.unscaledTime - startTime));
            overlayText.text = $"{Locale.SETTINGS_KEY_BINDING_OVERLAY}\r\n{displayTime}";

            yield return null;
        }

        ToggleOverlay(false);

        callback?.Invoke(pressedKey);
    }

    private void ToggleOverlay(bool visible)
    {
        overlayVisible = visible;
        overlay.SetActive(visible);
        overlay.transform.SetAsLastSibling();
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(KeyBindInterface)}]";
    }
}
