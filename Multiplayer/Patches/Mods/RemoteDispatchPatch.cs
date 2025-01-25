using System;
using System.Reflection;
using DV.JObjectExtstensions;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Player;
using Newtonsoft.Json.Linq;
using UnityEngine;

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

        foreach (NetworkedPlayer player in NetworkLifecycle.Instance.Client.PlayerManager.Players)
        {
            JObject data = new();

            Transform playerTransform = player.transform;
            Vector3 position = playerTransform.position - WorldMover.currentMove;
            float rotation = playerTransform.eulerAngles.y;

            JArray latLon = new(
                Math.Round(DEGREES_PER_METER * position.z, DECIMAL_PLACES),
                Math.Round(DEGREES_PER_METER * position.x, DECIMAL_PLACES)
            );

            data.SetString("color", "aqua");
            data.Add("position", latLon);
            data.SetFloat("rotation", rotation);
            __result.SetJObject(player.Username, data);
        }
    }

    private static void CheckTransform_Postfix()
    {
        Sessions_AddTag?.Invoke(null, new object[] { "player" });
    }
}
