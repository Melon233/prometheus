using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.XAsset.Editor
{
    public sealed class CollectorBuildException : Exception
    {
        public CollectorBuildException(string message) : base(message)
        {
        }
    }

    public static class CollectorBuilder
    {
        private static readonly string[] ExcludedExtensions =
        {
            ".cs",
            ".asmdef",
            ".asmref",
            ".dll"
        };

        public static IReadOnlyList<AssetManifest> Build(CollectorSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (settings.packages == null || settings.packages.Count == 0)
                throw new CollectorBuildException("Collector settings contain no packages.");

            var packageNames = new HashSet<string>(StringComparer.Ordinal);
            var globalAssetOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            var manifests = new List<AssetManifest>();

            foreach (CollectorPackageConfig package in settings.packages)
            {
                ValidatePackageName(package, packageNames);
                manifests.Add(BuildPackageInternal(package, globalAssetOwners));
            }

            return manifests;
        }

        public static AssetManifest BuildPackage(CollectorPackageConfig package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));

            var packageNames = new HashSet<string>(StringComparer.Ordinal);
            ValidatePackageName(package, packageNames);
            return BuildPackageInternal(
                package,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        public static IReadOnlyList<AssetManifest> BuildAndSave(
            CollectorSettings settings)
        {
            IReadOnlyList<AssetManifest> generated = Build(settings);
            string outputFolder = NormalizePath(settings.outputFolder);

            if (string.IsNullOrWhiteSpace(outputFolder) ||
                !outputFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                DestroyGenerated(generated);
                throw new CollectorBuildException(
                    $"Output folder must be under Assets: '{settings.outputFolder}'.");
            }

            EnsureAssetFolder(outputFolder);
            var saved = new List<AssetManifest>();

            foreach (AssetManifest manifest in generated)
            {
                string fileName = SanitizeToken(manifest.PackageName) + ".asset";
                string assetPath = $"{outputFolder}/{fileName}";
                AssetManifest existing =
                    AssetDatabase.LoadAssetAtPath<AssetManifest>(assetPath);

                if (existing == null)
                {
                    AssetDatabase.CreateAsset(manifest, assetPath);
                    saved.Add(manifest);
                }
                else
                {
                    EditorUtility.CopySerialized(manifest, existing);
                    EditorUtility.SetDirty(existing);
                    UnityEngine.Object.DestroyImmediate(manifest);
                    saved.Add(existing);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return saved;
        }

        private static AssetManifest BuildPackageInternal(
            CollectorPackageConfig package,
            IDictionary<string, string> globalAssetOwners)
        {
            if (package.groups == null || package.groups.Count == 0)
            {
                throw new CollectorBuildException(
                    $"Package '{package.packageName}' contains no groups.");
            }

            var assets = new List<XAssetInfo>();
            var assetsByGuid =
                new Dictionary<string, XAssetInfo>(StringComparer.Ordinal);
            var addresses =
                new Dictionary<string, string>(StringComparer.Ordinal);
            var bundleAssets =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var groupNames = new HashSet<string>(StringComparer.Ordinal);

            for (int groupIndex = 0; groupIndex < package.groups.Count; groupIndex++)
            {
                CollectorGroupConfig group = package.groups[groupIndex];
                ValidateGroup(package, group, groupNames);

                if (group.collectors == null || group.collectors.Count == 0)
                {
                    throw new CollectorBuildException(
                        $"Group '{group.groupName}' in package " +
                        $"'{package.packageName}' contains no collectors.");
                }

                for (int collectorIndex = 0;
                     collectorIndex < group.collectors.Count;
                     collectorIndex++)
                {
                    CollectorConfig collector = group.collectors[collectorIndex];
                    ValidateCollector(package, group, collector);

                    IReadOnlyList<string> paths = CollectAssetPaths(collector);
                    if (paths.Count == 0)
                    {
                        throw new CollectorBuildException(
                            $"Collector '{collector.collectorName}' at " +
                            $"'{collector.collectPath}' collected no assets.");
                    }

                    foreach (string assetPath in paths)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(assetPath);
                        if (string.IsNullOrEmpty(guid))
                        {
                            throw new CollectorBuildException(
                                $"Unable to resolve GUID for '{assetPath}'.");
                        }

                        string owner =
                            $"{package.packageName}/{group.groupName}/" +
                            $"{collector.collectorName}";

                        if (globalAssetOwners.TryGetValue(guid, out string existingOwner))
                        {
                            throw new CollectorBuildException(
                                $"Asset '{assetPath}' is collected more than once: " +
                                $"'{existingOwner}' and '{owner}'.");
                        }

                        string address = BuildAddress(
                            group,
                            collector,
                            assetPath);

                        if (addresses.TryGetValue(address, out string existingPath))
                        {
                            throw new CollectorBuildException(
                                $"Duplicate address '{address}' in package " +
                                $"'{package.packageName}': '{existingPath}' and " +
                                $"'{assetPath}'.");
                        }

                        string bundleId = BuildBundleId(
                            package,
                            group,
                            collector,
                            collectorIndex,
                            assetPath,
                            guid);

                        Type mainType =
                            AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                        List<string> dependencies = GetDependencyGuids(
                            assetPath,
                            guid);

                        var assetInfo = new XAssetInfo(
                            guid,
                            address,
                            assetPath,
                            mainType?.FullName ?? typeof(UnityEngine.Object).FullName,
                            bundleId,
                            dependencies);

                        globalAssetOwners.Add(guid, owner);
                        addresses.Add(address, assetPath);
                        assetsByGuid.Add(guid, assetInfo);
                        assets.Add(assetInfo);

                        if (!bundleAssets.TryGetValue(
                                bundleId,
                                out List<string> bundleGuids))
                        {
                            bundleGuids = new List<string>();
                            bundleAssets.Add(bundleId, bundleGuids);
                        }

                        bundleGuids.Add(guid);
                    }
                }
            }

            assets.Sort((left, right) =>
                string.CompareOrdinal(left.Address, right.Address));

            List<VirtualBundleInfo> bundles = BuildVirtualBundles(
                bundleAssets,
                assetsByGuid);

            AssetManifest manifest =
                ScriptableObject.CreateInstance<AssetManifest>();
            manifest.name = package.packageName + " Manifest";
            manifest.SetData(package.packageName, assets, bundles);
            return manifest;
        }

        private static IReadOnlyList<string> CollectAssetPaths(
            CollectorConfig collector)
        {
            string collectPath = NormalizePath(collector.collectPath);
            var result = new List<string>();

            if (AssetDatabase.IsValidFolder(collectPath))
            {
                string[] guids = AssetDatabase.FindAssets(
                    string.Empty,
                    new[] { collectPath });

                foreach (string guid in guids)
                {
                    string path = NormalizePath(
                        AssetDatabase.GUIDToAssetPath(guid));

                    if (ShouldCollect(path, collector))
                        result.Add(path);
                }
            }
            else if (ShouldCollect(collectPath, collector))
            {
                result.Add(collectPath);
            }
            else
            {
                throw new CollectorBuildException(
                    $"Collector path does not exist or is not supported: " +
                    $"'{collector.collectPath}'.");
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool ShouldCollect(
            string assetPath,
            CollectorConfig collector)
        {
            if (string.IsNullOrWhiteSpace(assetPath) ||
                AssetDatabase.IsValidFolder(assetPath))
            {
                return false;
            }

            string extension = Path.GetExtension(assetPath);
            if (ExcludedExtensions.Any(item =>
                    string.Equals(item, extension, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (!collector.includeEditorAssets &&
                assetPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            Type type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (type == null)
                return false;

            switch (collector.filter)
            {
                case CollectorFilter.All:
                    return true;
                case CollectorFilter.Prefab:
                    return type == typeof(GameObject) &&
                           string.Equals(
                               extension,
                               ".prefab",
                               StringComparison.OrdinalIgnoreCase);
                case CollectorFilter.ScriptableObject:
                    return typeof(ScriptableObject).IsAssignableFrom(type);
                case CollectorFilter.Texture:
                    return typeof(Texture).IsAssignableFrom(type);
                case CollectorFilter.Material:
                    return typeof(Material).IsAssignableFrom(type);
                case CollectorFilter.AudioClip:
                    return typeof(AudioClip).IsAssignableFrom(type);
                case CollectorFilter.Scene:
                    return type == typeof(SceneAsset);
                default:
                    return false;
            }
        }

        private static string BuildAddress(
            CollectorGroupConfig group,
            CollectorConfig collector,
            string assetPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(assetPath);

            switch (collector.addressRule)
            {
                case AddressRule.FullPath:
                    string extension = Path.GetExtension(assetPath);
                    return string.IsNullOrEmpty(extension)
                        ? assetPath
                        : assetPath.Substring(0, assetPath.Length - extension.Length);
                case AddressRule.FileName:
                    return fileName;
                case AddressRule.GroupAndFileName:
                    return $"{group.groupName}/{fileName}";
                default:
                    throw new CollectorBuildException(
                        $"Unsupported address rule: {collector.addressRule}.");
            }
        }

        private static string BuildBundleId(
            CollectorPackageConfig package,
            CollectorGroupConfig group,
            CollectorConfig collector,
            int collectorIndex,
            string assetPath,
            string guid)
        {
            string prefix =
                $"{SanitizeToken(package.packageName)}/" +
                $"{SanitizeToken(group.groupName)}";

            switch (collector.packRule)
            {
                case PackRule.PackTogether:
                    return $"{prefix}/" +
                           $"{SanitizeToken(collector.collectorName)}_{collectorIndex}";

                case PackRule.PackSeparately:
                    return $"{prefix}/" +
                           $"{SanitizeToken(Path.GetFileNameWithoutExtension(assetPath))}_" +
                           $"{guid.Substring(0, 8)}";

                case PackRule.PackByFirstDirectory:
                    string segment = GetFirstRelativeDirectory(
                        collector.collectPath,
                        assetPath);
                    return $"{prefix}/{SanitizeToken(segment)}";

                default:
                    throw new CollectorBuildException(
                        $"Unsupported pack rule: {collector.packRule}.");
            }
        }

        private static string GetFirstRelativeDirectory(
            string collectPath,
            string assetPath)
        {
            string root = NormalizePath(collectPath).TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root))
                return "_root";

            string relative = assetPath.StartsWith(
                root + "/",
                StringComparison.Ordinal)
                ? assetPath.Substring(root.Length + 1)
                : assetPath;

            int separator = relative.IndexOf('/');
            return separator < 0 ? "_root" : relative.Substring(0, separator);
        }

        private static List<string> GetDependencyGuids(
            string assetPath,
            string ownGuid)
        {
            var dependencies = new HashSet<string>(StringComparer.Ordinal);

            foreach (string dependencyPath in
                     AssetDatabase.GetDependencies(assetPath, true))
            {
                string dependencyGuid =
                    AssetDatabase.AssetPathToGUID(dependencyPath);

                if (!string.IsNullOrEmpty(dependencyGuid) &&
                    !string.Equals(
                        dependencyGuid,
                        ownGuid,
                        StringComparison.Ordinal))
                {
                    dependencies.Add(dependencyGuid);
                }
            }

            var result = dependencies.ToList();
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static List<VirtualBundleInfo> BuildVirtualBundles(
            IDictionary<string, List<string>> bundleAssets,
            IReadOnlyDictionary<string, XAssetInfo> assetsByGuid)
        {
            var assetBundleMap =
                new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, List<string>> pair in bundleAssets)
            {
                foreach (string guid in pair.Value)
                    assetBundleMap[guid] = pair.Key;
            }

            var bundles = new List<VirtualBundleInfo>();

            foreach (KeyValuePair<string, List<string>> pair in bundleAssets)
            {
                var dependencyBundles =
                    new HashSet<string>(StringComparer.Ordinal);

                foreach (string assetGuid in pair.Value)
                {
                    XAssetInfo assetInfo = assetsByGuid[assetGuid];

                    foreach (string dependencyGuid in assetInfo.DependencyGuids)
                    {
                        if (assetBundleMap.TryGetValue(
                                dependencyGuid,
                                out string dependencyBundleId) &&
                            !string.Equals(
                                dependencyBundleId,
                                pair.Key,
                                StringComparison.Ordinal))
                        {
                            dependencyBundles.Add(dependencyBundleId);
                        }
                    }
                }

                List<string> assetGuids = pair.Value
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToList();
                List<string> dependencyIds = dependencyBundles
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToList();

                bundles.Add(new VirtualBundleInfo(
                    pair.Key,
                    assetGuids,
                    dependencyIds));
            }

            bundles.Sort((left, right) =>
                string.CompareOrdinal(left.BundleId, right.BundleId));
            return bundles;
        }

        private static void ValidatePackageName(
            CollectorPackageConfig package,
            ISet<string> packageNames)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.packageName))
                throw new CollectorBuildException("Package name is empty.");

            if (!packageNames.Add(package.packageName))
            {
                throw new CollectorBuildException(
                    $"Duplicate package name '{package.packageName}'.");
            }
        }

        private static void ValidateGroup(
            CollectorPackageConfig package,
            CollectorGroupConfig group,
            ISet<string> groupNames)
        {
            if (group == null || string.IsNullOrWhiteSpace(group.groupName))
            {
                throw new CollectorBuildException(
                    $"Package '{package.packageName}' contains an empty group name.");
            }

            if (!groupNames.Add(group.groupName))
            {
                throw new CollectorBuildException(
                    $"Package '{package.packageName}' contains duplicate group " +
                    $"name '{group.groupName}'.");
            }
        }

        private static void ValidateCollector(
            CollectorPackageConfig package,
            CollectorGroupConfig group,
            CollectorConfig collector)
        {
            if (collector == null)
            {
                throw new CollectorBuildException(
                    $"Group '{group.groupName}' in package " +
                    $"'{package.packageName}' contains a null collector.");
            }

            if (string.IsNullOrWhiteSpace(collector.collectorName))
                throw new CollectorBuildException("Collector name is empty.");

            if (string.IsNullOrWhiteSpace(collector.collectPath))
            {
                throw new CollectorBuildException(
                    $"Collector '{collector.collectorName}' path is empty.");
            }
        }

        private static void EnsureAssetFolder(string folder)
        {
            string normalized = NormalizePath(folder).TrimEnd('/');
            string[] segments = normalized.Split('/');
            string current = segments[0];

            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);

                current = next;
            }
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unnamed";

            char[] result = value.Trim().ToLowerInvariant().Select(character =>
                char.IsLetterOrDigit(character) ||
                character == '_' ||
                character == '-'
                    ? character
                    : '_').ToArray();

            return new string(result);
        }

        private static string NormalizePath(string path)
        {
            return path?.Trim().Replace('\\', '/');
        }

        private static void DestroyGenerated(
            IEnumerable<AssetManifest> manifests)
        {
            foreach (AssetManifest manifest in manifests)
            {
                if (manifest != null)
                    UnityEngine.Object.DestroyImmediate(manifest);
            }
        }
    }
}
