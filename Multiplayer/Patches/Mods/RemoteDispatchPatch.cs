using DV.JObjectExtstensions;
using HarmonyLib;
using MPAPI.Interfaces;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Components.Networking;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEngine;
using System.Linq;
using Multiplayer.API;

namespace Multiplayer.Patches.Mods;

public static class RemoteDispatchPatch
{
    private const byte DECIMAL_PLACES = 8;
    private const float DEGREES_PER_METER = 360f / 40e6f;

    private static MethodInfo Sessions_AddTag;

    public static void Patch(Harmony harmony, Assembly assembly)
    {
        foreach (Type type in assembly.ExportedTypes)
        {
            if (type.Namespace != "DvMod.RemoteDispatch")
                continue;
            switch (type.Name)
            {
                case "PlayerData":
                    MethodInfo getPlayerData = AccessTools.DeclaredMethod(type, "GetPlayerData");
                    MethodInfo getPlayerDataPostfix = AccessTools.Method(typeof(RemoteDispatchPatch), nameof(GetPlayerData_Postfix));
                    harmony.Patch(getPlayerData, postfix: new HarmonyMethod(getPlayerDataPostfix));

                    MethodInfo checkTransform = AccessTools.DeclaredMethod(type, "CheckTransform");
                    MethodInfo CheckTransformPostfix = AccessTools.Method(typeof(RemoteDispatchPatch), nameof(CheckTransform_Postfix));
                    harmony.Patch(checkTransform, postfix: new HarmonyMethod(CheckTransformPostfix));
                    break;
                case "Sessions":
                    Sessions_AddTag = AccessTools.DeclaredMethod(type, "AddTag", new[] { typeof(string) });
                    break;
            }
        }
    }

    private static void GetPlayerData_Postfix(ref JObject __result)
    {
        if (!NetworkLifecycle.Instance.IsClientRunning)
            return;

        IEnumerable<IPlayer> players;

        if (NetworkLifecycle.Instance.IsHost())
            players = NetworkLifecycle.Instance.Server.ServerPlayerWrappers;
        else
            players = NetworkLifecycle.Instance.Client.ClientPlayerWrappers;

        foreach (var player in players)
        {
            JObject data = new();

            Vector3 position = player.Position - WorldMover.currentMove;
            float rotation = player.RotationY;

            JArray latLon = new(
                Math.Round(DEGREES_PER_METER * position.z, DECIMAL_PLACES),
                Math.Round(DEGREES_PER_METER * position.x, DECIMAL_PLACES)
            );

            data.SetString("color", "aqua");
            data.Add("position", latLon);
            data.SetFloat("rotation", rotation);
            data.SetString("crew", player.CrewName);

            __result.SetJObject(player.Username, data);
        }
    }

    private static void CheckTransform_Postfix()
    {
        Sessions_AddTag?.Invoke(null, new object[] { "player" });
    }
}
