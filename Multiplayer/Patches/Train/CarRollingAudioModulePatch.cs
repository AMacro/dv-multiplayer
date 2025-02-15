using DV.ModularAudioCar;
using HarmonyLib;
using Multiplayer.Components.Networking;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CarRollingAudioModule), nameof(CarRollingAudioModule.PlayJointAtBogie))]
public static class CarRollingAudioModulePatch
{
    //private static bool Prefix()
    //{
    //    // todo: There's a bug with bogie joint sounds for clients that causes it to play hundreds of times per-frame. Once that's fixed, this patch can be removed.
    //    return NetworkLifecycle.Instance.IsHost();
    //}
}
