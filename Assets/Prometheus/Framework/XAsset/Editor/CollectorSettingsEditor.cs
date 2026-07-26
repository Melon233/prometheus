using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.XAsset.Editor
{
    [CustomEditor(typeof(CollectorSettings))]
    public sealed class CollectorSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (!GUILayout.Button("Build XAsset Manifests"))
                return;

            BuildAndReport((CollectorSettings)target);
        }

        [MenuItem("Prometheus/XAsset/Create Collector Settings")]
        private static void CreateCollectorSettings()
        {
            const string path =
                "Assets/Prometheus/Framework/XAsset/XAssetCollectorSettings.asset";

            CollectorSettings existing =
                AssetDatabase.LoadAssetAtPath<CollectorSettings>(path);

            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var settings = CreateInstance<CollectorSettings>();
            var package = new CollectorPackageConfig
            {
                packageName = "DefaultPackage"
            };
            var group = new CollectorGroupConfig
            {
                groupName = "Characters"
            };
            group.collectors.Add(new CollectorConfig
            {
                collectorName = "Characters",
                collectPath = "Assets/BundleResources",
                filter = CollectorFilter.Prefab,
                addressRule = AddressRule.GroupAndFileName,
                packRule = PackRule.PackSeparately
            });
            package.groups.Add(group);
            settings.packages.Add(package);

            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem(
            "Prometheus/XAsset/Build Selected Collector Settings",
            true)]
        private static bool CanBuildSelected()
        {
            return Selection.activeObject is CollectorSettings;
        }

        [MenuItem("Prometheus/XAsset/Build Selected Collector Settings")]
        private static void BuildSelected()
        {
            BuildAndReport((CollectorSettings)Selection.activeObject);
        }

        private static void BuildAndReport(CollectorSettings settings)
        {
            try
            {
                IReadOnlyList<AssetManifest> manifests =
                    CollectorBuilder.BuildAndSave(settings);
                int assetCount = 0;
                int bundleCount = 0;

                foreach (AssetManifest manifest in manifests)
                {
                    assetCount += manifest.Assets.Count;
                    bundleCount += manifest.Bundles.Count;
                }

                Debug.Log(
                    $"XAsset build succeeded: {manifests.Count} package(s), " +
                    $"{assetCount} asset(s), {bundleCount} virtual bundle(s).");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
