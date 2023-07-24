using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(Bogie), nameof(Bogie.SetupPhysics))]
public static class Bogie_SetupPhysics_Patch
{
    private static void Postfix(Bogie __instance)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            __instance.gameObject.GetOrAddComponent<NetworkedRigidbody>();
    }
}
