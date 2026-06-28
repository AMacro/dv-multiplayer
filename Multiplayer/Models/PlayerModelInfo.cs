using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Models
{
    public class PlayerModelInfo
    {
        public readonly string CharacterId;
        public readonly string DisplayName;
        public readonly GameObject Prefab;
        public readonly string SourcePath;

        public PlayerModelInfo(string characterId, string displayName, GameObject prefab, string sourcePath = null)
        {
            CharacterId = characterId;
            DisplayName = displayName;
            SourcePath = sourcePath;
            Prefab = prefab;
        }
    }
}
