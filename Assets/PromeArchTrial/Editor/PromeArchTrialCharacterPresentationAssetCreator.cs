using System;
using System.Collections.Generic;
using PromeArchTrial.Presentation.Character;
using Spine;
using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PromeArchTrial.Editor
{
    /// <summary>
    /// 从旧版 Yefa prefab 构建完全独立的纯表现 prefab，并创建包含摄像机、灯光和交互驱动器的验收场景；源 prefab 始终保持只读。
    /// </summary>
    public static class PromeArchTrialCharacterPresentationAssetCreator
    {
        /// <summary>旧版 Yefa prefab 的只读来源路径。</summary>
        public const string SourcePrefabPath = "Assets/BundleResources/Character/Yefa.prefab";

        /// <summary>净化后 Yefa 表现 prefab 的稳定输出路径。</summary>
        public const string PresentationPrefabPath = "Assets/PromeArchTrial/Presentation/Character/Prefabs/YefaCharacterPresentation.prefab";

        /// <summary>独立 Yefa 表现验收场景的稳定输出路径。</summary>
        public const string AcceptanceScenePath = "Assets/PromeArchTrial/Presentation/Character/Scenes/YefaCharacterPresentationAcceptance.unity";

        /// <summary>旧 prefab 中只能服务旧相机、UI 或命中盒逻辑、不得进入纯表现资产的节点名集合。</summary>
        private static readonly HashSet<string> RemovedHierarchyNames = new HashSet<string>(StringComparer.Ordinal) { "Main Camera", "Canvas", "RotateRoot", "SkillCollider", "SpecialCollider", "UltCollider", "AttackCollider", "AtkCollider" };

        /// <summary>从菜单重新生成净化表现 prefab，并立即执行结构与动画资源校验。</summary>
        [MenuItem("Tools/PromeArchTrial/Character/Rebuild Yefa Presentation Prefab")]
        public static void RebuildPresentationPrefab()
        {
            BuildPresentationPrefabAsset();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PresentationPrefabPath);
        }

        /// <summary>从菜单重新生成净化 prefab 与验收场景，并打开最终场景。</summary>
        [MenuItem("Tools/PromeArchTrial/Character/Create Yefa Presentation Acceptance Scene")]
        public static void CreateAndOpenAcceptanceScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildPresentationPrefabAsset();
            CreateAcceptanceSceneAsset();
            EditorSceneManager.OpenScene(AcceptanceScenePath, OpenSceneMode.Single);
            CharacterPresentationAcceptanceDriver driver = UnityEngine.Object.FindFirstObjectByType<CharacterPresentationAcceptanceDriver>();
            if (driver != null) Selection.activeGameObject = driver.gameObject;
        }

        /// <summary>供 Unity batchmode 或自动化验收无交互生成净化 prefab 与验收场景。</summary>
        public static void BuildCharacterPresentationAssetsFromCommandLine()
        {
            BuildPresentationPrefabAsset();
            CreateAcceptanceSceneAsset();
        }

        /// <summary>从菜单验证现有净化 prefab，不进行任何资产修改。</summary>
        [MenuItem("Tools/PromeArchTrial/Character/Validate Yefa Presentation Prefab")]
        public static void ValidatePresentationPrefab()
        {
            ValidatePresentationPrefabAsset();
            Debug.Log($"[PromeArchTrial] Yefa presentation prefab validation passed: {PresentationPrefabPath}");
        }

        /// <summary>在预览场景中实例化并完全解包旧 prefab，净化后保存为新的独立 prefab。</summary>
        private static void BuildPresentationPrefabAsset()
        {
            EnsureAssetFolder("Assets/PromeArchTrial/Presentation/Character/Prefabs");
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null) throw new InvalidOperationException($"Cannot load source Yefa prefab at {SourcePrefabPath}.");
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(sourcePrefab, previewScene) as GameObject;
                if (instance == null) throw new InvalidOperationException("Failed to instantiate the source Yefa prefab in the preview scene.");
                if (PrefabUtility.IsPartOfPrefabInstance(instance)) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                SanitizeCharacterHierarchy(instance);
                ConfigurePurePresentationComponents(instance, previewScene);
                PrefabUtility.SaveAsPrefabAsset(instance, PresentationPrefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully) throw new InvalidOperationException($"Failed to save Yefa presentation prefab at {PresentationPrefabPath}.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PresentationPrefabPath, ImportAssetOptions.ForceUpdate);
            ValidatePresentationPrefabAsset();
            Debug.Log($"[PromeArchTrial] Rebuilt pure Yefa presentation prefab without modifying source asset: {PresentationPrefabPath}");
        }

        /// <summary>移除旧相机、Canvas、命中盒、物理组件、RootMotion 与旧 gameplay MonoBehaviour，同时保留 Spine Renderer 和视觉子树。</summary>
        private static void SanitizeCharacterHierarchy(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = transforms.Length - 1; index >= 0; index--)
            {
                Transform candidate = transforms[index];
                if (candidate == null || candidate == root.transform) continue;
                if (RemovedHierarchyNames.Contains(candidate.name)) UnityEngine.Object.DestroyImmediate(candidate.gameObject);
            }
            Component[] components = root.GetComponentsInChildren<Component>(true);
            // 保持 Unity 序列化的组件顺序可先移除依赖者再移除被依赖组件；旧 Yefa 的 SpineComponent 位于其所需 VfxComponent 之前。
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null || component is Transform || component is SkeletonAnimation || component is Renderer || component is MeshFilter) continue;
                if (component.gameObject == root && component is MonoBehaviour)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                    continue;
                }
                if (IsForbiddenPresentationComponent(component)) UnityEngine.Object.DestroyImmediate(component);
            }
            root.name = "YefaCharacterPresentation";
            root.tag = "Untagged";
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
        }

        /// <summary>判断组件是否属于净化 prefab 明确禁止的相机、UI、物理、RootMotion 或旧 gameplay 类型。</summary>
        private static bool IsForbiddenPresentationComponent(Component component)
        {
            if (component is Camera || component is AudioListener || component is AudioSource || component is Canvas) return true;
            if (component is CharacterController || component is Collider || component is Collider2D || component is Rigidbody || component is Rigidbody2D || component is Joint || component is Joint2D) return true;
            if (component is SkeletonRootMotionBase) return true;
            if (!(component is MonoBehaviour behaviour)) return false;
            string componentNamespace = behaviour.GetType().Namespace;
            return !string.IsNullOrEmpty(componentNamespace) && componentNamespace.StartsWith("Xuan.Prometheus", StringComparison.Ordinal);
        }

        /// <summary>创建世界空间血条与飘字锚点，并只向净化根节点添加新架构纯表现组件。</summary>
        private static void ConfigurePurePresentationComponents(GameObject root, Scene previewScene)
        {
            SkeletonAnimation skeletonAnimation = root.GetComponent<SkeletonAnimation>();
            if (skeletonAnimation == null) throw new InvalidOperationException("Source Yefa prefab does not contain a SkeletonAnimation on its root.");
            skeletonAnimation.loop = true;
            skeletonAnimation.AnimationName = YefaCharacterAnimationNames.Idle;
            Transform healthBarRoot = CreateEmptyChild(root.transform, previewScene, "PresentationHealthBar", new Vector3(0f, 2.15f, -0.12f));
            GameObject healthBackground = CreateCubeChild(healthBarRoot, previewScene, "Background", Vector3.zero, new Vector3(1.65f, 0.14f, 0.04f));
            GameObject healthFill = CreateCubeChild(healthBarRoot, previewScene, "Fill", new Vector3(0f, 0f, -0.025f), new Vector3(1.55f, 0.09f, 0.045f));
            Transform damageNumberAnchor = CreateEmptyChild(root.transform, previewScene, "DamageNumberAnchor", new Vector3(0f, 2.38f, -0.16f));
            YefaCharacterPresenter presenter = root.AddComponent<YefaCharacterPresenter>();
            presenter.Configure(skeletonAnimation, root.transform, healthFill.transform, damageNumberAnchor, healthBackground.GetComponent<Renderer>(), healthFill.GetComponent<Renderer>());
            CharacterDamageNumberTextPresenter damageNumberPresenter = root.AddComponent<CharacterDamageNumberTextPresenter>();
            damageNumberPresenter.Configure(presenter);
        }

        /// <summary>在指定预览场景内创建一个没有额外组件的子节点。</summary>
        private static Transform CreateEmptyChild(Transform parent, Scene targetScene, string name, Vector3 localPosition)
        {
            GameObject child = new GameObject(name);
            SceneManager.MoveGameObjectToScene(child, targetScene);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child.transform;
        }

        /// <summary>在指定预览场景内创建无 Collider 的立方体视觉子节点。</summary>
        private static GameObject CreateCubeChild(Transform parent, Scene targetScene, string name, Vector3 localPosition, Vector3 localScale)
        {
            GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(child, targetScene);
            child.name = name;
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localScale = localScale;
            Collider collider = child.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            return child;
        }

        /// <summary>创建包含主摄像机、方向光、地面和交互验收驱动器的独立场景资产。</summary>
        private static void CreateAcceptanceSceneAsset()
        {
            EnsureAssetFolder("Assets/PromeArchTrial/Presentation/Character/Scenes");
            GameObject presentationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresentationPrefabPath);
            if (presentationPrefab == null) throw new InvalidOperationException($"Cannot load generated presentation prefab at {PresentationPrefabPath}.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject character = PrefabUtility.InstantiatePrefab(presentationPrefab, scene) as GameObject;
            if (character == null) throw new InvalidOperationException("Failed to instantiate generated Yefa presentation prefab.");
            character.transform.position = Vector3.zero;
            YefaCharacterPresenter presenter = character.GetComponent<YefaCharacterPresenter>();
            CharacterPresentationAcceptanceDriver driver = character.AddComponent<CharacterPresentationAcceptanceDriver>();
            driver.Configure(presenter);
            CreateAcceptanceCamera();
            CreateAcceptanceLight();
            CreateAcceptanceGround();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, AcceptanceScenePath)) throw new InvalidOperationException($"Failed to save Yefa presentation acceptance scene at {AcceptanceScenePath}.");
            AssetDatabase.SaveAssets();
            Debug.Log($"[PromeArchTrial] Created Yefa presentation acceptance scene: {AcceptanceScenePath}");
        }

        /// <summary>创建验收场景唯一主摄像机，并以轻微俯视角观察 XZ 移动与 Y 轴跳跃。</summary>
        private static void CreateAcceptanceCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.15f, 1f);
            camera.fieldOfView = 45f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(0f, 3.1f, -10f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.12f, 1f), Vector3.up);
        }

        /// <summary>创建验收场景主方向光。</summary>
        private static void CreateAcceptanceLight()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        /// <summary>创建无碰撞体的地面视觉参考，帮助确认角色确实在 XZ 平面移动。</summary>
        private static void CreateAcceptanceGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Acceptance Ground";
            ground.transform.position = new Vector3(0f, -0.12f, 2f);
            ground.transform.localScale = new Vector3(14f, 0.1f, 14f);
            Collider collider = ground.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }

        /// <summary>验证净化 prefab 的组件边界与必须动画，任何失败都抛出明确错误阻止错误资产进入验收。</summary>
        private static void ValidatePresentationPrefabAsset()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresentationPrefabPath);
            if (prefab == null) throw new InvalidOperationException($"Generated Yefa presentation prefab is missing at {PresentationPrefabPath}.");
            List<string> errors = new List<string>();
            if (prefab.GetComponentsInChildren<SkeletonAnimation>(true).Length != 1) errors.Add("The prefab must contain exactly one SkeletonAnimation.");
            if (prefab.GetComponentsInChildren<YefaCharacterPresenter>(true).Length != 1) errors.Add("The prefab must contain exactly one YefaCharacterPresenter.");
            if (prefab.GetComponentsInChildren<CharacterController>(true).Length != 0) errors.Add("CharacterController was not removed.");
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 || prefab.GetComponentsInChildren<Collider2D>(true).Length != 0) errors.Add("One or more hitbox or physics Collider components remain.");
            if (prefab.GetComponentsInChildren<Camera>(true).Length != 0 || prefab.GetComponentsInChildren<Canvas>(true).Length != 0) errors.Add("A legacy Camera or Canvas remains.");
            if (prefab.GetComponentsInChildren<SkeletonRootMotionBase>(true).Length != 0) errors.Add("SkeletonRootMotion was not removed.");
            ValidateRemovedHierarchyNames(prefab, errors);
            ValidateOldGameplayComponents(prefab, errors);
            ValidateRequiredAnimations(prefab, errors);
            if (errors.Count > 0) throw new InvalidOperationException("Yefa presentation prefab validation failed:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>验证所有旧相机、UI 与命中盒命名节点均已从新 prefab 中移除。</summary>
        private static void ValidateRemovedHierarchyNames(GameObject prefab, ICollection<string> errors)
        {
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms) if (candidate != null && candidate != prefab.transform && RemovedHierarchyNames.Contains(candidate.name)) errors.Add($"Legacy hierarchy node remains: {candidate.name}.");
        }

        /// <summary>验证新 prefab 不再包含旧 Xuan.Prometheus gameplay MonoBehaviour 或丢失脚本。</summary>
        private static void ValidateOldGameplayComponents(GameObject prefab, ICollection<string> errors)
        {
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                {
                    errors.Add("A missing MonoBehaviour script remains.");
                    continue;
                }
                string componentNamespace = behaviour.GetType().Namespace;
                if (!string.IsNullOrEmpty(componentNamespace) && componentNamespace.StartsWith("Xuan.Prometheus", StringComparison.Ordinal)) errors.Add($"Legacy gameplay component remains: {behaviour.GetType().FullName}.");
            }
        }

        /// <summary>直接读取 Yefa SkeletonData 并验证新表现映射使用的每个动画名。</summary>
        private static void ValidateRequiredAnimations(GameObject prefab, ICollection<string> errors)
        {
            SkeletonAnimation skeletonAnimation = prefab.GetComponentInChildren<SkeletonAnimation>(true);
            if (skeletonAnimation == null || skeletonAnimation.SkeletonDataAsset == null)
            {
                errors.Add("SkeletonAnimation or SkeletonDataAsset is missing.");
                return;
            }
            SkeletonData skeletonData = skeletonAnimation.SkeletonDataAsset.GetSkeletonData(false);
            if (skeletonData == null)
            {
                errors.Add("Yefa SkeletonData could not be loaded.");
                return;
            }
            foreach (string animationName in YefaCharacterAnimationNames.RequiredAnimationNames) if (skeletonData.FindAnimation(animationName) == null) errors.Add($"Required Yefa animation is missing: {animationName}.");
        }

        /// <summary>逐级创建 Unity 资产目录，确保由 AssetDatabase 生成并维护对应 meta 文件。</summary>
        private static void EnsureAssetFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string currentPath = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string nextPath = currentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(nextPath)) AssetDatabase.CreateFolder(currentPath, segments[index]);
                currentPath = nextPath;
            }
        }
    }
}
