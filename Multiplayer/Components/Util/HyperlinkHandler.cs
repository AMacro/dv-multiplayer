using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System.Text.RegularExpressions;

namespace Multiplayer.Components.Util
{
    public class HyperlinkHandler : MonoBehaviour, IPointerClickHandler
    {
        public static readonly Color DEFAULT_COLOR = Color.blue;
        public static readonly Color DEFAULT_HOVER_COLOR = new Color(0x00, 0x59, 0xFF, 0xFF);

        public Color linkColor = DEFAULT_COLOR;
        public Color linkHoverColor = DEFAULT_HOVER_COLOR;

        public TextMeshProUGUI textComponent;
        private Canvas canvas;
        private Camera canvasCamera;

        private int hoveredLinkIndex = -1;
        private bool underlineLinks = true;

        void Start()
        {
            if (textComponent == null)
            {
                textComponent = GetComponent<TextMeshProUGUI>();
            }

            canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }

            ApplyLinkStyling();
        }

        void Update()
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, Input.mousePosition, canvasCamera);

            if (linkIndex != -1 && linkIndex != hoveredLinkIndex)
            {
                // Mouse is over a new link
                if (hoveredLinkIndex != -1)
                {
                    // Remove hover style from the previously hovered link
                    RemoveHoverStyle(hoveredLinkIndex);
                }
                ApplyHoverStyle(linkIndex);
                hoveredLinkIndex = linkIndex;
            }
            else if (linkIndex == -1 && hoveredLinkIndex != -1)
            {
                // Mouse is no longer over any link
                RemoveHoverStyle(hoveredLinkIndex);
                hoveredLinkIndex = -1;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, Input.mousePosition, canvasCamera);

            if (linkIndex != -1)
            {
                TMP_LinkInfo linkInfo = textComponent.textInfo.linkInfo[linkIndex];
                string url = linkInfo.GetLinkID();
                Application.OpenURL(url);
            }
        }

        private void ApplyLinkStyling()
        {
            string text = textComponent.text;
            string pattern = @"<link=""([^""]+)"">(.*?)<\/link>";
            string replacement = underlineLinks
                ? $"<link=\"$1\"><color=#{ColorUtility.ToHtmlStringRGB(linkColor)}><u>$2</u></color></link>"
                : $"<link=\"$1\"><color=#{ColorUtility.ToHtmlStringRGB(linkColor)}>$2</color></link>";

            text = Regex.Replace(text, pattern, replacement);
            textComponent.text = text;
        }

        private void ApplyHoverStyle(int linkIndex)
        {
            TMP_LinkInfo linkInfo = textComponent.textInfo.linkInfo[linkIndex];
            SetLinkColor(linkInfo, linkHoverColor);
        }

        private void RemoveHoverStyle(int linkIndex)
        {
            TMP_LinkInfo linkInfo = textComponent.textInfo.linkInfo[linkIndex];
            SetLinkColor(linkInfo, linkColor);
        }

        private void SetLinkColor(TMP_LinkInfo linkInfo, Color32 color)
        {
            var meshInfo = textComponent.textInfo.meshInfo[0];

            for (int i = 0; i < linkInfo.linkTextLength; i++)
            {
                int characterIndex = linkInfo.linkTextfirstCharacterIndex + i;

                // Check if the character is within bounds and is visible
                if (characterIndex >= textComponent.textInfo.characterCount ||
                    !textComponent.textInfo.characterInfo[characterIndex].isVisible)
                    continue;

                int materialIndex = textComponent.textInfo.characterInfo[characterIndex].materialReferenceIndex;
                int vertexIndex = textComponent.textInfo.characterInfo[characterIndex].vertexIndex;

                // Ensure we're using the correct mesh info
                meshInfo = textComponent.textInfo.meshInfo[materialIndex];

                meshInfo.colors32[vertexIndex] = color;
                meshInfo.colors32[vertexIndex + 1] = color;
                meshInfo.colors32[vertexIndex + 2] = color;
                meshInfo.colors32[vertexIndex + 3] = color;
            }

            // Mark the vertex data as dirty for all used materials
            for (int i = 0; i < textComponent.textInfo.materialCount; i++)
            {
                textComponent.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            }
        }
    }
}
