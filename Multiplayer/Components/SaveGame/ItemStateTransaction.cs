using System;
using Newtonsoft.Json.Linq;

namespace Multiplayer.Components.SaveGame;

internal static class ItemStateTransaction
{
    internal static bool TryApply(JObject state, Action<JObject> save, out Exception failure)
    {
        var candidate = (JObject)state.DeepClone();
        try
        {
            save(candidate);
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }

        Replace(state, candidate);
        failure = null;
        return true;
    }

    internal static void Replace(JObject target, JObject source)
    {
        target.RemoveAll();
        foreach (JProperty property in source.Properties())
            target.Add(property.Name, property.Value.DeepClone());
    }
}
