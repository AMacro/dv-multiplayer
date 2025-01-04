using DV.Customization.Paint;
using DV.Utils;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using JetBrains.Annotations;


namespace Multiplayer.Components.Networking.Train;

public class PaintThemeLookup : SingletonBehaviour<PaintThemeLookup>
{
    private readonly Dictionary<string, int> themeIndices = [];
    private string[] themeNames;

    protected override void Awake()
    {
        base.Awake();
        themeNames = Resources.LoadAll<Object>("").Where(x => x is PaintTheme)
            .Select(x => x.name.ToLower())
            .ToArray();

        for (int i = 0; i < themeNames.Length; i++)
        {
            themeIndices.Add(themeNames[i], i);
        }

        Multiplayer.LogDebug(() =>
        {
            return $"Registered Paint Themes:\r\n{string.Join("\r\n", themeNames.Select((name, index) => $"{index}: {name}"))}";
        });
    }

    public string GetThemeName(int index)
    {
        return (index >= 0 && index < themeNames.Length) ? themeNames[index] : null;
    }

    public int GetThemeIndex(string themeName)
    {
        return themeIndices.TryGetValue(themeName.ToLower(), out int index) ? index : -1;
    }

    /*
     * Allow other mods to register custom themes
     
    public void RegisterTheme(string themeName)
    {
        themeName = themeName.ToLower();
        if (!themeIndices.ContainsKey(themeName))
        {
            // Add to array
            Array.Resize(ref themeNames, themeNames.Length + 1);
            int newIndex = themeNames.Length - 1;
            themeNames[newIndex] = themeName;

            // Add to dictionary
            themeIndices.Add(themeName, newIndex);
        }
    }

    public void UnregisterTheme(string themeName)
    {
        themeName = themeName.ToLower();
        if (themeIndices.TryGetValue(themeName, out int index))
        {
            // Remove from dictionary
            themeIndices.Remove(themeName);

            // Remove from array and shift remaining elements
            for (int i = index; i < themeNames.Length - 1; i++)
            {
                themeNames[i] = themeNames[i + 1];
                themeIndices[themeNames[i]] = i; // Update indices
            }
            Array.Resize(ref themeNames, themeNames.Length - 1);
        }
    }
    */

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(PaintThemeLookup)}]";
    }
}
