using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Prometheus.Gameplay
{
    [DefaultExecutionOrder(-99)]
    public class StartUp : MonoBehaviour
    {
        private static PlayerEntity player;
        private static List<SlimeEntity> enemies = new();
        public List<Transform> enemySpawnPoints = new();

        [SerializeField] private string packageName = "Prometheus";
        [SerializeField] private string enemyAddress = "Slime";
        [SerializeField] private string playerAddress = "Yefa";

        private ResourcePackage resourcePackage;
        private AssetHandle slimePrefabHandle;
        private AssetHandle yefaPrefabHandle;
        private bool gameplayReady;

        private IEnumerator Start()
        {
            yield return InitializeResourcePackage();

            // Application.targetFrameRate = 999;
            yefaPrefabHandle =
                resourcePackage.LoadAssetSync<GameObject>(playerAddress);
            GameObject yefa = yefaPrefabHandle.InstantiateSync();
            if (yefa == null)
                throw new InvalidOperationException(
                    $"Failed to instantiate player '{playerAddress}'.");

            yefa.SetActive(true);
            player = new PlayerEntity(yefa);
            // FillField<DataAttribute, IComponent>(player, player.AddComp);
            // FillField<LogicAttribute, ILogic>(player, player.AddLogic);
            player.AfterNew();

            slimePrefabHandle =
                resourcePackage.LoadAssetSync<GameObject>(enemyAddress);

            foreach (Transform spawnPoint in enemySpawnPoints)
            {
                if (spawnPoint == null)
                {
                    Debug.LogWarning(
                        "StartUp contains an empty enemy spawn point.",
                        this);
                    continue;
                }

                var options = new InstantiateOptions(
                    true,
                    spawnPoint,
                    false);
                GameObject slime =
                    slimePrefabHandle.InstantiateSync(options);
                if (slime == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to instantiate enemy '{enemyAddress}'.");
                }

                var enemy = new SlimeEntity(slime);
                enemy.AfterNew();
                enemies.Add(enemy);
                break; // Only instantiate one enemy for now. You can modify this to instantiate multiple enemies.
            }

            gameplayReady = true;
        }

        private void Update()
        {
            if (!gameplayReady)
                return;

            player.OnUpdate(Time.deltaTime);
            foreach (var enemy in enemies)
                enemy.OnUpdate(Time.deltaTime);
        }

        private void OnDestroy()
        {
            gameplayReady = false;

            slimePrefabHandle?.Release();
            slimePrefabHandle = null;
            yefaPrefabHandle?.Release();
            yefaPrefabHandle = null;

            resourcePackage = null;
            player = null;
            enemies.Clear();
        }

        private IEnumerator InitializeResourcePackage()
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new InvalidOperationException(
                    "StartUp YooAsset package name is empty.");
            if (string.IsNullOrWhiteSpace(playerAddress))
                throw new InvalidOperationException(
                    "StartUp player address is empty.");
            if (string.IsNullOrWhiteSpace(enemyAddress))
            {
                throw new InvalidOperationException(
                    "StartUp enemy address is empty.");
            }

            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();

            if (!YooAssets.TryGetPackage(
                    packageName,
                    out resourcePackage))
            {
                resourcePackage = YooAssets.CreatePackage(packageName);
            }

            while (resourcePackage.InitializeStatus ==
                   EOperationStatus.Processing)
            {
                yield return null;
            }

            if (resourcePackage.InitializeStatus !=
                EOperationStatus.Succeeded)
            {
                InitializePackageOptions options =
                    CreateInitializeOptions();
                InitializePackageOperation operation =
                    resourcePackage.InitializePackageAsync(options);
                yield return operation;

                if (operation.Status != EOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize YooAsset package " +
                        $"'{packageName}': {operation.Error}");
                }
            }

            if (resourcePackage.PackageValid)
                yield break;

            RequestPackageVersionOperation versionOperation =
                resourcePackage.RequestPackageVersionAsync();
            yield return versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to request YooAsset package version " +
                    $"'{packageName}': {versionOperation.Error}");
            }

            var manifestOptions = new LoadPackageManifestOptions(
                versionOperation.PackageVersion,
                60);
            LoadPackageManifestOperation manifestOperation =
                resourcePackage.LoadPackageManifestAsync(manifestOptions);
            yield return manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to load YooAsset package manifest " +
                    $"'{packageName}': {manifestOperation.Error}");
            }
        }

        private InitializePackageOptions CreateInitializeOptions()
        {
#if UNITY_EDITOR
            PackageBuildResult buildResult =
                EditorSimulateBuildInvoker.Build(
                    packageName,
                    (int)EBundleType.VirtualAssetBundle);
            var options = new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters
                        .CreateDefaultEditorFileSystemParameters(
                            buildResult.PackageRootDirectory)
            };
            return options;
#else
            var options = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters =
                    FileSystemParameters
                        .CreateDefaultBuiltinFileSystemParameters()
            };
            return options;
#endif
        }

        // public void FillField<TAbbr, TField>(Entity obj, Action<TField> callback = null)
        //     where TAbbr : Attribute
        // {
        //     obj.GetType().GetTypeInfo().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
        //         .ToList().ForEach(f =>
        //         {
        //             var abbr = f.GetCustomAttribute(typeof(TAbbr));
        //             if (abbr != null)
        //             {
        //                 if (f.FieldType.IsSubclassOf(typeof(MonoBehaviour)))
        //                     f.SetValue(obj, obj.bindGo?.GetComponent(f.FieldType));
        //                 else
        //                     f.SetValue(obj, Activator.CreateInstance(f.FieldType));
        //                 callback?.Invoke((TField)f.GetValue(obj));
        //             }
        //         });
        // }
    }
}
