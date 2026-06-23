using DV.UI;
using UnityEngine;

namespace Multiplayer.Utils;

public static class UIHelpers
{
    #region Prefabs

    #endregion

    private static bool intialised = false;
    public static void Initialise()
    {

    }


    #region Prefab Helpers
    public static GameObject MakePrefab<T>(Transform parent) where T : Component
    {
        Multiplayer.LogDebug(() => $"Making prefab for {typeof(T)?.Name}, parent: {parent?.name}");
        var target = parent.GetComponentInChildren<T>()?.gameObject;

        if (target == null)
            return null;

        return MakePrefab(target);
    }

    public static GameObject MakePrefab(GameObject target)
    {
        Multiplayer.LogDebug(() => $"Making prefab for {target?.name}");
        target.SetActive(false);
        var instance = GameObject.Instantiate(target);
        target.SetActive(true);

        var sco = instance.GetComponent<SettingChangeSource>();
        if (sco != null)
            GameObject.DestroyImmediate(sco);

        // Remove any I2 localization components, they will be recreated when the component is used
        var locs = instance.GetComponentsInChildren<I2.Loc.Localize>(true);
        foreach (var loc in locs)
            GameObject.DestroyImmediate(loc);

        return instance;
    }

    #endregion
}
