using DV.Customization;
using Multiplayer.Networking.Data.Customization;

namespace Multiplayer.Components.Networking.World;

public static class CustomizationRef
{
    public static bool TryCreate(Customization customization, out CustomizationRefData reference)
    {
        reference = default;
        if (customization == null)
            return false;

        reference.IdentificationKey = customization.GetIdentificationKey();
        return !string.IsNullOrEmpty(reference.IdentificationKey);
    }

    public static bool TryResolve(CustomizationRefData reference, out Customization customization)
    {
        return Customization.TryGetFromIdentificationKey(reference.IdentificationKey, out customization);
    }
}
