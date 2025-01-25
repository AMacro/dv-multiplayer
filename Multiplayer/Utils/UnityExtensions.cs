using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;

namespace Multiplayer.Utils;

public static class UnityExtensions
{
    [NotNull]
    public static GameObject NewChild(this GameObject parent, string name)
    {
        return new GameObject(name) {
            transform = {
                parent = parent.transform
            }
        };
    }

    public static bool HasChildWithName(this Component parent, string name)
    {
        return parent.gameObject.HasChildWithName(name);
    }

    [NotNull]
    public static GameObject FindChildByName(this Component parent, string name)
    {
        return parent.gameObject.FindChildByName(name);
    }

    public static bool HasChildWithName(this GameObject parent, string name)
    {
        return FindChildrenByName(parent, name).Length > 0;
    }

    [NotNull]
    public static GameObject FindChildByName(this GameObject parent, string name)
    {
        Transform child = parent.transform.FindChildByName(name);
        return parent.transform.FindChildByName(name).gameObject;
    }

    [NotNull]
    public static Transform FindChildByName(this Transform parent, string name)
    {
        return FindChildrenByName(parent, name).FirstOrDefault() ?? throw new NullReferenceException($"Failed to find child {name} in {parent.name}");
    }

    [NotNull]
    public static GameObject[] FindChildrenByName(this Component parent, string name)
    {
        return FindChildrenByName(parent.gameObject, name);
    }

    [NotNull]
    public static GameObject[] FindChildrenByName(this GameObject parent, string name)
    {
        List<Transform> transforms = FindChildrenByName(parent.transform, name);
        GameObject[] gameObjects = new GameObject[transforms.Count];
        for (int i = 0; i < transforms.Count; i++)
            gameObjects[i] = transforms[i].gameObject;

        return gameObjects;
    }

    [NotNull]
    public static GameObject[] GetChildren(this GameObject parent)
    {
        Transform[] transforms = GetChildren(parent.transform);
        GameObject[] gameObjects = new GameObject[transforms.Length];
        for (int i = 0; i < transforms.Length; i++)
            gameObjects[i] = transforms[i].gameObject;
        return gameObjects;
    }

    [NotNull]
    public static List<Transform> FindChildrenByName(this Transform parent, string name)
    {
        List<Transform> list = new();
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                list.Add(t);
        return list;
    }

    [NotNull]
    public static Transform[] GetChildren(this Transform parent)
    {
        Transform[] array = new Transform[parent.childCount];
        for (int i = 0; i < parent.childCount; i++)
            array[i] = parent.GetChild(i);
        return array;
    }

    public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
            component = gameObject.AddComponent<T>();
        return component;
    }

    public static T GetOrAddComponent<T>(this Component component) where T : Component
    {
        return component.gameObject.GetOrAddComponent<T>();
    }
}
