using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace Multiplayer.Editor
{
    [CreateAssetMenu(menuName = "Multiplayer/Asset Index")]
    public class AssetIndex : ScriptableObject
    {
        [Header("Prefabs")]
        public GameObject PlayerTag;
        public GameObject defaultModel;
        public GameObject[] modelPrefabs;

        [Header("Textures")]
        public Sprite multiplayerIcon;
        public Sprite lockIcon;
        public Sprite refreshIcon;
        public Sprite connectIcon;
        public Sprite lanIcon;
    }
}
