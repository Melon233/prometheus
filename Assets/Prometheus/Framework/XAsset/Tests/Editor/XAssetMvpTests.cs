using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Xuan.Prometheus.XAsset.Editor.Tests
{
    public sealed class XAssetMvpTests
    {
        private readonly List<ScriptableObject> temporaryObjects =
            new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject temporaryObject in temporaryObjects)
            {
                if (temporaryObject != null)
                    Object.DestroyImmediate(temporaryObject);
            }

            temporaryObjects.Clear();
        }

        [Test]
        public void CollectorBuild_PackSeparately_GeneratesAddressesAndBundles()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackSeparately);

            Assert.AreEqual("TestPackage", manifest.PackageName);
            Assert.AreEqual(2, manifest.Assets.Count);
            Assert.AreEqual(2, manifest.Bundles.Count);
            Assert.That(
                manifest.Assets.Select(item => item.Address),
                Does.Contain("Characters/Slime"));
            Assert.That(
                manifest.Assets.Select(item => item.Address),
                Does.Contain("Characters/Yefa"));
            Assert.AreEqual(
                2,
                manifest.Assets
                    .Select(item => item.BundleId)
                    .Distinct()
                    .Count());
        }

        [Test]
        public void CollectorBuild_PackTogether_GeneratesOneBundle()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackTogether);

            Assert.AreEqual(2, manifest.Assets.Count);
            Assert.AreEqual(1, manifest.Bundles.Count);
            Assert.AreEqual(
                1,
                manifest.Assets
                    .Select(item => item.BundleId)
                    .Distinct()
                    .Count());
        }

        [Test]
        public void CollectorBuild_OverlappingCollectors_Throws()
        {
            CollectorSettings settings = CreateSettings(PackRule.PackSeparately);
            settings.packages[0].groups[0].collectors.Add(new CollectorConfig
            {
                collectorName = "DuplicateCharacters",
                collectPath = "Assets/BundleResources",
                filter = CollectorFilter.Prefab,
                addressRule = AddressRule.FullPath,
                packRule = PackRule.PackSeparately
            });

            CollectorBuildException exception = Assert.Throws<CollectorBuildException>(
                () => CollectorBuilder.Build(settings));

            StringAssert.Contains("collected more than once", exception.Message);
        }

        [Test]
        public void SyncLoad_SharesProviderAndUsesReferenceCounting()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackSeparately);
            var backend = new EditorAssetDatabaseBackend();
            var package = new ResourcePackage(manifest, backend);

            AssetHandle<GameObject> first =
                package.LoadAssetSync<GameObject>("Characters/Slime");
            AssetHandle<GameObject> second =
                package.LoadAssetSync<GameObject>("Characters/Slime");

            Assert.AreEqual(AssetOperationStatus.Succeeded, first.Status);
            Assert.AreEqual(AssetOperationStatus.Succeeded, second.Status);
            Assert.AreSame(first.Asset, second.Asset);
            Assert.AreEqual(1, backend.SyncLoadCount);
            Assert.AreEqual(1, package.ProviderCount);
            Assert.AreEqual(
                2,
                package.GetReferenceCount("Characters/Slime"));

            first.Release();

            Assert.AreEqual(1, package.ProviderCount);
            Assert.AreEqual(
                1,
                package.GetReferenceCount("Characters/Slime"));

            second.Release();

            Assert.AreEqual(0, package.ProviderCount);
            Assert.AreEqual(1, backend.UnloadCount);
            package.Dispose();
        }

        [UnityTest]
        public IEnumerator AsyncLoad_SharesOneInFlightProvider()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackSeparately);
            var backend = new EditorAssetDatabaseBackend();
            var package = new ResourcePackage(manifest, backend);

            AssetHandle<GameObject> first =
                package.LoadAssetAsync<GameObject>("Characters/Slime");
            AssetHandle<GameObject> second =
                package.LoadAssetAsync<GameObject>("Characters/Slime");

            Assert.AreEqual(1, backend.AsyncLoadCount);
            Assert.AreEqual(2, package.ActiveReferenceCount);
            Assert.IsFalse(first.IsDone);

            yield return WaitUntilDone(first);

            Assert.AreEqual(AssetOperationStatus.Succeeded, first.Status);
            Assert.AreEqual(AssetOperationStatus.Succeeded, second.Status);
            Assert.AreSame(first.Asset, second.Asset);

            first.Release();
            second.Release();

            Assert.AreEqual(0, package.ProviderCount);
            Assert.AreEqual(1, backend.UnloadCount);
            package.Dispose();
        }

        [UnityTest]
        public IEnumerator SyncLoad_CompletesAnExistingAsyncProvider()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackSeparately);
            var backend = new EditorAssetDatabaseBackend();
            var package = new ResourcePackage(manifest, backend);

            AssetHandle<GameObject> asyncHandle =
                package.LoadAssetAsync<GameObject>("Characters/Slime");
            AssetHandle<GameObject> syncHandle =
                package.LoadAssetSync<GameObject>("Characters/Slime");

            Assert.AreEqual(1, backend.AsyncLoadCount);
            Assert.AreEqual(1, backend.SyncLoadCount);
            Assert.IsTrue(asyncHandle.IsDone);
            Assert.IsTrue(syncHandle.IsDone);
            Assert.AreSame(asyncHandle.Asset, syncHandle.Asset);

            asyncHandle.Release();
            syncHandle.Release();
            Assert.AreEqual(0, package.ProviderCount);

            // Allow the previously scheduled editor callback to drain. Its
            // stale completion is ignored by the released provider.
            yield return null;

            package.Dispose();
        }

        [UnityTest]
        public IEnumerator ReleaseWhileLoading_EvictsAfterCompletion()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackSeparately);
            var backend = new EditorAssetDatabaseBackend();
            var package = new ResourcePackage(manifest, backend);

            AssetHandle<GameObject> handle =
                package.LoadAssetAsync<GameObject>("Characters/Slime");

            handle.Release();

            Assert.AreEqual(AssetOperationStatus.Released, handle.Status);
            Assert.AreEqual(1, package.ProviderCount);
            Assert.AreEqual(0, package.ActiveReferenceCount);

            int remainingFrames = 20;
            while (package.ProviderCount > 0 && remainingFrames-- > 0)
                yield return null;

            Assert.AreEqual(0, package.ProviderCount);
            Assert.AreEqual(1, backend.UnloadCount);
            package.Dispose();
        }

        [Test]
        public void LoadWithWrongType_FailsOnlyThatHandle()
        {
            AssetManifest manifest = BuildPrefabManifest(PackRule.PackSeparately);
            var backend = new EditorAssetDatabaseBackend();
            var package = new ResourcePackage(manifest, backend);

            AssetHandle<Texture2D> handle =
                package.LoadAssetSync<Texture2D>("Characters/Slime");

            Assert.AreEqual(AssetOperationStatus.Failed, handle.Status);
            Assert.IsInstanceOf<System.InvalidCastException>(handle.Error);
            Assert.IsTrue(handle.Task.IsFaulted);

            handle.Release();
            Assert.AreEqual(0, package.ProviderCount);
            package.Dispose();
        }

        private AssetManifest BuildPrefabManifest(PackRule packRule)
        {
            CollectorSettings settings = CreateSettings(packRule);
            AssetManifest manifest = CollectorBuilder.Build(settings).Single();
            temporaryObjects.Add(manifest);
            return manifest;
        }

        private CollectorSettings CreateSettings(PackRule packRule)
        {
            CollectorSettings settings =
                ScriptableObject.CreateInstance<CollectorSettings>();
            temporaryObjects.Add(settings);

            var collector = new CollectorConfig
            {
                collectorName = "Characters",
                collectPath = "Assets/BundleResources",
                filter = CollectorFilter.Prefab,
                addressRule = AddressRule.GroupAndFileName,
                packRule = packRule
            };

            var group = new CollectorGroupConfig
            {
                groupName = "Characters"
            };
            group.collectors.Add(collector);

            var package = new CollectorPackageConfig
            {
                packageName = "TestPackage"
            };
            package.groups.Add(group);
            settings.packages.Add(package);

            return settings;
        }

        private static IEnumerator WaitUntilDone<T>(AssetHandle<T> handle)
            where T : Object
        {
            int remainingFrames = 20;

            while (!handle.IsDone && remainingFrames-- > 0)
                yield return null;

            Assert.IsTrue(handle.IsDone, "Asset load did not finish in time.");
        }
    }
}
