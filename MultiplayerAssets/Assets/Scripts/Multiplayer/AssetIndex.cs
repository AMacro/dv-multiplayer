using UnityEngine;

namespace Multiplayer.Editor
{
    [CreateAssetMenu(menuName = "Multiplayer/Asset Index")]
    public class AssetIndex : ScriptableObject
    {
        [Header("Prefabs")]
        public GameObject playerPrefab;

        [Header("Textures")]
        public Sprite multiplayerIcon;
        public Sprite lockIcon;
        public Sprite refreshIcon;
        public Sprite connectIcon;
    }
}
