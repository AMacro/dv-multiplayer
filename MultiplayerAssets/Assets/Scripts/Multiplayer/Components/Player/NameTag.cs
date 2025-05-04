using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace Multiplayer.Editor.Components.Player
{
    public class NameTag : MonoBehaviour
    {
        private static TMP_FontAsset font;

        [SerializeField]
        private Transform canvas;
        [SerializeField]
        private GameObject usernameObject;
        [SerializeField]
        private GameObject pingObject;

        [NonSerialized]
        private TMP_Text usernameText;
        [NonSerialized]
        private TMP_Text pingText;

        [NonSerialized]
        public Transform LookTarget;

        private void Awake()
        {
            SetupText();
        }

        private void SetupText()
        {
#if UNITY_EDITOR
            return;
#endif
            InitFont();
            usernameText = CreateText(usernameObject);
            usernameText.fontSize = 12.0f;
            usernameText.verticalAlignment = VerticalAlignmentOptions.Geometry;
            pingText = CreateText(pingObject);
            pingText.fontSize = 4.0f;
            pingText.color = Color.yellow;
            pingText.verticalAlignment = VerticalAlignmentOptions.Top;
        }

        private void LateUpdate()
        {
            if (LookTarget == null)
                return;
            canvas.rotation = Quaternion.LookRotation(canvas.position - LookTarget.position);
        }

        [UsedImplicitly]
        public void ShowUsername(bool show)
        {
            usernameText.enabled = show;
        }

        [UsedImplicitly]
        public void SetUsername(string text)
        {
            usernameText.text = MarkText(text);
        }

        [UsedImplicitly]
        public void ShowPing(bool show)
        {
            pingText.enabled = show;
        }

        [UsedImplicitly]
        public void SetPing(int ping)
        {
            pingText.text = ping.ToString();
        }

        private static string MarkText(string text)
        {
            return $"<mark=#00000064 padding=\"10, 10, 0, 0\">{text}</mark>";
        }

        private static void InitFont()
        {
            if (font != null)
                return;
            font = Instantiate(FindObjectOfType<TMP_Text>().font);
        }

        private static TMP_Text CreateText(GameObject gameObject)
        {
            TMP_Text text = gameObject.AddComponent<TextMeshProUGUI>();
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            text.font = font;
            text.enableCulling = true;
            text.isTextObjectScaleStatic = true;
            text.raycastTarget = false;
            return text;
        }
    }
}
