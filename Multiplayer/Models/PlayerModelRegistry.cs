using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Models;

public class PlayerModelRegistry
{
    private readonly HashSet<PlayerModelInfo> baseModels = [];
    private readonly Dictionary<string, PlayerModelInfo> playerModels = new(StringComparer.OrdinalIgnoreCase);

    public PlayerModelInfo DefaultModel;
    public IReadOnlyList<PlayerModelInfo> Models => playerModels.Values.ToList();

    public void Reload()
    {
        baseModels.Clear();
        playerModels.Clear();

        // Set up the default model
        Multiplayer.AssetIndex.defaultModel.TryGetComponent<CharacterMetaData>(out var charMetaData);

        if (charMetaData == null)
            throw new Exception("Default model does not have a CharacterMetaData component!");

        DefaultModel = new PlayerModelInfo(charMetaData.Id, charMetaData.DisplayName, Multiplayer.AssetIndex.defaultModel);


        // Import MP base models
        foreach (var model in Multiplayer.AssetIndex.modelPrefabs)
        {
            charMetaData = model.GetComponent<CharacterMetaData>();
            if (charMetaData == null)
            {
                Multiplayer.LogWarning($"Model {model.name} does not have a CharacterMetaData component, skipping.");
                continue;
            }

            if (playerModels.ContainsKey(charMetaData.Id))
            {
                Multiplayer.LogWarning($"Model {model.name} has a duplicate characterId {charMetaData.Id}, skipping.");
                continue;
            }

            var modelInfo = new PlayerModelInfo(charMetaData.Id, charMetaData.DisplayName, model);
            baseModels.Add(modelInfo);
            playerModels.Add(modelInfo.CharacterId, modelInfo);
        }
    }

    public PlayerModelInfo GetModelById(string characterId)
    {
        if (playerModels.TryGetValue(characterId, out var model))
        {
            if (model.Prefab == null)
            {
                Reload();
                if (playerModels.TryGetValue(characterId, out model))
                    return model;
                else
                    return DefaultModel;
            }
            return model;
        }

        Multiplayer.LogWarning($"Model with characterId {characterId} not found, returning default model.");

        if (DefaultModel == null || DefaultModel.Prefab == null)
            Reload();

        return DefaultModel;
    }

}
