using System;
using System.IO;
using PromeArchTrial.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PromeArchTrial.Editor
{
    /// <summary>
    /// 创建只包含客户端组合根的独立演示场景，避免手工拖拽和序列化引用遗漏。
    /// </summary>
    public static class PromeArchTrialDemoSceneCreator
    {
        /// <summary>客户端演示场景的稳定资产目录。</summary>
        private const string SceneFolder = "Assets/PromeArchTrial/Scenes";

        /// <summary>客户端演示场景的稳定资产路径。</summary>
        private const string ScenePath = SceneFolder + "/PromeArchTrialClientDemo.unity";

        /// <summary>通过菜单创建或覆盖演示场景，并在保存完成后打开它。</summary>
        [MenuItem("Tools/PromeArchTrial/Create Client Demo Scene")]
        public static void CreateAndOpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateSceneAsset();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BattleClientBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<BattleClientBootstrap>();
            if (bootstrap != null) Selection.activeGameObject = bootstrap.gameObject;
        }

        /// <summary>供命令行批处理创建演示场景，不依赖当前场景内容或人工操作。</summary>
        public static void CreateSceneFromCommandLine()
        {
            CreateSceneAsset();
        }

        /// <summary>创建空场景和唯一组合根，并把净化角色 prefab 序列化到场景以支持 Player 构建。</summary>
        private static void CreateSceneAsset()
        {
            if (!AssetDatabase.IsValidFolder(SceneFolder)) Directory.CreateDirectory(SceneFolder);
            GameObject presentationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PromeArchTrialCharacterPresentationAssetCreator.PresentationPrefabPath);
            if (presentationPrefab == null)
            {
                PromeArchTrialCharacterPresentationAssetCreator.RebuildPresentationPrefab();
                presentationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PromeArchTrialCharacterPresentationAssetCreator.PresentationPrefabPath);
            }
            if (presentationPrefab == null) throw new IOException($"Failed to build Yefa presentation prefab at {PromeArchTrialCharacterPresentationAssetCreator.PresentationPrefabPath}.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject bootstrapObject = new GameObject("PromeArchTrial Client Bootstrap");
            BattleClientBootstrap bootstrap = bootstrapObject.AddComponent<BattleClientBootstrap>();
            SerializedObject serializedBootstrap = new SerializedObject(bootstrap);
            SerializedProperty prefabProperty = serializedBootstrap.FindProperty("characterPresentationPrefab");
            if (prefabProperty == null) throw new MissingFieldException(nameof(BattleClientBootstrap), "characterPresentationPrefab");
            prefabProperty.objectReferenceValue = presentationPrefab;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException($"Failed to save PromeArchTrial demo scene at {ScenePath}.");
            AssetDatabase.SaveAssets();
            Debug.Log($"[PromeArchTrial] Client demo scene created at {ScenePath}.");
        }
    }
}
