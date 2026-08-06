using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xuan.PrometheusCS.Presentation;
using DemoBootstrap = Xuan.PrometheusCS.Bootstrap.Bootstrap;

namespace Xuan.PrometheusCS.Editor
{
    /// <summary>
    /// PrometheusCSDemoSceneCreator 构建一个独立 WASD 方块移动场景，并避免修改用户当前打开的场景。
    /// </summary>
    public static class PrometheusCSDemoSceneCreator
    {
        private const string DemoDirectory = "Assets/PrometheusCS/Demo";
        private const string DemoScenePath = DemoDirectory + "/PrometheusCSDemo.unity";

        /// <summary>
        /// 从 Unity 菜单生成或覆盖 Demo 场景，并在 Project 窗口中选中生成结果。
        /// </summary>
        [MenuItem("PrometheusCS/Create WASD Cube Demo Scene")]
        public static void CreateDemoScene()
        {
            CreateAndSaveDemoScene();
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath);
            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
            Debug.Log($"PrometheusCS demo scene created at '{DemoScenePath}'. Open it and enter Play Mode to use WASD.");
        }

        /// <summary>
        /// 提供给 Unity BatchMode 的无交互入口，使持续集成可以重新生成 Demo 场景。
        /// </summary>
        public static void CreateDemoSceneFromCommandLine()
        {
            CreateAndSaveDemoScene();
            Debug.Log($"PrometheusCS demo scene created at '{DemoScenePath}' from command line.");
        }

        /// <summary>
        /// 在附加临时场景中创建全部 Demo 对象、保存资产并恢复原活动场景。
        /// </summary>
        private static void CreateAndSaveDemoScene()
        {
            EnsureDemoDirectory();
            Scene originalScene = SceneManager.GetActiveScene();
            Scene demoScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(demoScene);
            try
            {
                CreateDemoObjects();
                if (!EditorSceneManager.SaveScene(demoScene, DemoScenePath)) throw new System.InvalidOperationException($"Failed to save PrometheusCS demo scene at '{DemoScenePath}'.");
            }
            finally
            {
                EditorSceneManager.CloseScene(demoScene, true);
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>确保保存 Demo 场景的 Unity 资产目录已经存在。</summary>
        private static void EnsureDemoDirectory()
        {
            if (!AssetDatabase.IsValidFolder(DemoDirectory)) AssetDatabase.CreateFolder("Assets/PrometheusCS", "Demo");
        }

        /// <summary>创建组合入口、玩家方块、地面、摄像机、灯光和 HUD。</summary>
        private static void CreateDemoObjects()
        {
            GameObject root = new GameObject("PrometheusCS Demo");
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Cube);
            player.name = "Player Cube";
            player.transform.SetParent(root.transform);
            CubePlayerView playerView = player.AddComponent<CubePlayerView>();
            playerView.Configure(0.5f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "XZ Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(root.transform);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 8f, -8f);
            cameraObject.transform.LookAt(Vector3.zero);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;

            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(root.transform);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            GameObject hudObject = new GameObject("Demo HUD");
            hudObject.transform.SetParent(root.transform);
            DemoHudView hudView = hudObject.AddComponent<DemoHudView>();
            hudView.Configure(playerView);

            DemoBootstrap bootstrap = root.AddComponent<DemoBootstrap>();
            bootstrap.Configure(playerView, 5f);
        }
    }
}
