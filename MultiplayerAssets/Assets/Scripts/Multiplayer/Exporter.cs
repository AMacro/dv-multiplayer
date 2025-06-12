#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Multiplayer.Editor
{
    public static class Exporter
    {
        private const string ASSET_BUNDLE_NAME = "multiplayer";
        private const string DEST_DIR = "../build";
        private const string ASSET_BUNDLE_DEST_NAME = ASSET_BUNDLE_NAME + ".assetbundle";
        private static readonly string[] COPY_DLLS = {
            "MultiplayerEditor.dll",
            "UnityChan.dll"
        };

        [MenuItem("Multiplayer/Build Asset Bundle and Scripts")]
        private static void Build()
        {
            BuildScripts();
            BuildAssetBundle();
        }

        [MenuItem("Multiplayer/Build Asset Bundle")]
        private static void BuildAssetBundle()
        {
            Debug.Log("Building Asset Bundle");

            Directory.CreateDirectory(DEST_DIR);

            string destFile = Path.Combine(DEST_DIR, ASSET_BUNDLE_DEST_NAME);

            AssetBundleBuild build = new AssetBundleBuild {
                assetBundleName = ASSET_BUNDLE_NAME,
                assetNames = AssetDatabase.GetAssetPathsFromAssetBundle(ASSET_BUNDLE_NAME)
            };

            string buildDir = CreateTmpDir();

            Directory.CreateDirectory(buildDir);
            BuildPipeline.BuildAssetBundles(buildDir, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

            if (File.Exists(destFile))
                File.Delete(destFile);

            File.Move(Path.Combine(buildDir, ASSET_BUNDLE_NAME), destFile);

            Directory.Delete(buildDir, true);

            Debug.Log("Finished building asset bundle");
        }

        [MenuItem("Multiplayer/Build Scripts")]
        private static void BuildScripts()
        {
            Debug.Log("Validating AssetIndex");
            if (!ValidateAssetIndex())
                return;

            Debug.Log("Validation passed, building Scripts");
            Directory.CreateDirectory(DEST_DIR);
            string buildDir = CreateTmpDir();
            string exePath = Path.Combine(buildDir, Path.GetRandomFileName());
            string managedFolder = $"{Path.Combine(buildDir, Path.GetFileNameWithoutExtension(exePath))}_Data/Managed";
            BuildPipeline.BuildPlayer(Array.Empty<string>(), exePath, BuildTarget.StandaloneWindows64, BuildOptions.None);
            foreach (string dll in COPY_DLLS)
            {
                string sourceName = Path.Combine(managedFolder, dll);
                if (!File.Exists(sourceName))
                {
                    Debug.LogError($"Could not find {sourceName} in build! You can inspect the build at '{buildDir}'.");
                    return;
                }

                string destName = Path.Combine(DEST_DIR, dll);
                if (File.Exists(destName))
                    File.Delete(destName);
                File.Move(sourceName, destName);
            }

            Directory.Delete(buildDir, true);
            Debug.Log("Finished building scripts");
        }

        private static bool ValidateAssetIndex()
        {
            AssetIndex[] indices = AssetDatabase.FindAssets($"t:{nameof(AssetIndex)}").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AssetIndex>).ToArray();
            if (indices.Length != 1)
            {
                Debug.LogError($"Expected exactly one AssetIndex, found {indices.Length}");
                return false;
            }

            AssetIndex index = indices[0];
            if (AssetDatabase.GetImplicitAssetBundleName(AssetDatabase.GetAssetPath(index)) != "multiplayer")
            {
                Debug.Log("AssetIndex is not in the multiplayer asset bundle");
                return false;
            }

            foreach (FieldInfo field in typeof(AssetIndex).GetFields(BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (field.GetValue(index) == null)
                {
                    Debug.Log($"AssetIndex has null field {field.Name}");
                    return false;
                }

            return true;
        }

        private static string CreateTmpDir()
        {
            string buildDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(buildDir);
            return buildDir;
        }
    }
}
#endif
