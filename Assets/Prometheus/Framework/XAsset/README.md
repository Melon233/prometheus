# XAsset MVP

XAsset is an editor-only asset-management MVP inspired by YooAsset's
collector, package, provider, and handle boundaries.

## Included

- Package → Group → Collector configuration.
- Folder or single-asset collection through `AssetDatabase`.
- Address rules: full path, file name, or group plus file name.
- Virtual pack rules: together, separately, or by first directory.
- Generated manifests containing asset, dependency, and virtual-bundle data.
- Synchronous and simulated asynchronous Editor loading.
- In-flight load deduplication.
- Handle-based reference counting and logical unloading.
- EditMode tests covering collection, packing, loading, races, and release.

The asynchronous Editor backend defers `AssetDatabase.LoadAssetAtPath` to a
later Editor update. It validates asynchronous API behavior but does not
simulate real disk I/O performance.

## Quick start

1. Run `Prometheus/XAsset/Create Collector Settings`.
2. Edit the generated settings asset.
3. Click `Build XAsset Manifests` in its Inspector.
4. Add `XAssetBootstrap` to a scene object and assign the generated manifest.

The Editor assembly automatically registers `EditorAssetDatabaseBackend`, so
the bootstrap creates the package when entering Play Mode. Editor tools and
tests can also create a package explicitly:

```csharp
var backend = new EditorAssetDatabaseBackend();
var package = XAssets.CreatePackage(manifest, backend, true);

var handle = package.LoadAssetSync<GameObject>("Characters/Slime");
GameObject prefab = handle.Asset;

// Keep the handle while the system still owns or pools instances that depend
// on the prefab. Release it when that ownership ends.
handle.Release();
```

For an asynchronous load:

```csharp
var handle = package.LoadAssetAsync<GameObject>("Characters/Slime");
GameObject prefab = await handle.Task;
handle.Release();
```

## Ownership contract

Each load call returns a distinct handle and increments the shared provider's
reference count. Repeated loads of the same GUID share one provider and one
asset object. A handle is idempotently releasable, but its asset must not be
used after release.

In Editor simulation, reference count zero removes the provider from XAsset's
cache. Unity still owns `AssetDatabase` memory, so this is logical rather than
physical memory unloading.

## Future AssetBundle backend

Keep the public package/provider/handle API. Replace
`EditorAssetDatabaseBackend` with an `AssetBundleBackend`, turn virtual bundle
IDs into physical bundles, and extend the manifest with file name, hash, CRC,
size, and dependency bundle IDs.
