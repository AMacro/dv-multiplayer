using HarmonyLib;
using Steamworks.Data;
using System.Net;
using System.Net.Sockets;

namespace Multiplayer.Patches.Util;

[HarmonyPatch(typeof(NetAddress))]
public static class FacepunchNetAddressPatch
{
    [HarmonyPatch(nameof(NetAddress.From), new[] { typeof(IPAddress), typeof(ushort) })]
    [HarmonyPrefix]
    private static bool From(IPAddress address, ushort port, ref NetAddress __result)
    {
        if (address != null && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Multiplayer.LogDebug(() => $"FacepunchNetAddressPatch.From() IPv6");
            NetAddress cleared = NetAddress.Cleared;
            var ipv6Bytes = address.GetAddressBytes();
            NetAddress.InternalSetIPv6(ref cleared, ref ipv6Bytes[0], port);
            __result = cleared;
            return false;
        }
        return true;
    }
}
