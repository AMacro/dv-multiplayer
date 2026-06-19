using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace Multiplayer.Editor
{
    [CreateAssetMenu(menuName = "Multiplayer/Asset Index")]
    public class AssetIndex : ScriptableObject
    {
        [Header("Prefabs")]
        public GameObject[] playerPrefabs;

        [Header("Textures")]
        public Sprite multiplayerIcon;
        public Sprite lockIcon;
        public Sprite refreshIcon;
        public Sprite connectIcon;
        public Sprite lanIcon;

        public GameObject GetModelFromId(string id)
        {
            int index = 0;

            while (index < playerPrefabs.Length)
            {
                var CharacterMetaData = playerPrefabs[index].GetComponent<CharacterMetaData>();
                if (CharacterMetaData == null)
                    Debug.LogError($"Player prefab at index {index} does not have CharacterMetaData component!");

                if (CharacterMetaData.Id == id)
                    return playerPrefabs[index];

                index++;
            }

            Debug.LogWarning($"Could not find model with id {id}");

            return null;
        }

        public IEnumerable<CharacterMetaData> AllCharacterMetaData()
        {
            int index = 0;

            while (index < playerPrefabs.Length)
            {
                var metaData = playerPrefabs[index].GetComponent<CharacterMetaData>();

                if (metaData == null)
                    continue;

                yield return metaData;

                index++;
            }
        }
        public IEnumerable<GameObject> AllCharacterModels() => playerPrefabs;

    }
}
