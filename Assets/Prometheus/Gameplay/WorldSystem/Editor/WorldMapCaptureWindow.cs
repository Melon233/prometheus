using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// 世界地图拍摄工具：使用临时正交相机把当前场景指定矩形范围拍摄为静态 PNG，并同步生成 WorldMapDefinition 资产。
    /// 拍摄过程只发生在编辑器，运行时不会再创建俯拍相机或 RenderTexture。
    /// </summary>
    public sealed class WorldMapCaptureWindow : EditorWindow
    {
        /// <summary>地图纹理输出目录，和当前项目公共 UI 图标资源保持一致。</summary>
        private const string TextureDirectory = "Assets/BundleResources/UI/Common/Atlas";

        /// <summary>地图定义输出位置，供 WorldSystem 通过 WorldMapDefinition 地址加载。</summary>
        private const string DefinitionPath = "Assets/BundleResources/Config/Global/WorldMapDefinition.asset";

        /// <summary>地图纹理默认宽度；高度根据世界长度宽度比例自动计算。</summary>
        private const int DefaultResolution = 2048;

        /// <summary>当前拍摄范围左下角世界坐标。</summary>
        private Vector3 origin = Vector3.zero;

        /// <summary>当前拍摄范围的世界 X 轴长度。</summary>
        private float worldLength = 1000f;

        /// <summary>当前拍摄范围的世界 Z 轴宽度。</summary>
        private float worldWidth = 1000f;

        /// <summary>输出纹理的横向像素分辨率。</summary>
        private int resolution = DefaultResolution;

        /// <summary>俯拍相机的世界高度。</summary>
        private float captureHeight = 500f;

        /// <summary>地图打开时使用的初始缩放倍数。</summary>
        private float initialZoom = 1f;

        /// <summary>拍摄时允许渲染的 LayerMask。</summary>
        private int captureLayerMask;

        /// <summary>当前是否已经根据项目 Layer 配置初始化过默认 LayerMask。</summary>
        private bool initializedLayerMask;

        /// <summary>打开地图拍摄窗口。</summary>
        [MenuItem("Prometheus/World/Map Capture")]
        public static void Open()
        {
            GetWindow<WorldMapCaptureWindow>("World Map Capture");
        }

        /// <summary>首次打开窗口时默认排除 UI、角色和敌人层。</summary>
        private void OnEnable()
        {
            InitializeLayerMask();
        }

        /// <summary>绘制地图范围、拍摄参数和执行按钮。</summary>
        private void OnGUI()
        {
            EditorGUILayout.LabelField("地图拍摄范围", EditorStyles.boldLabel);
            origin = EditorGUILayout.Vector3Field("原点（左下角）", origin);
            worldLength = EditorGUILayout.FloatField("世界长度 X", worldLength);
            worldWidth = EditorGUILayout.FloatField("世界宽度 Z", worldWidth);
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("拍摄参数", EditorStyles.boldLabel);
            resolution = EditorGUILayout.IntField("纹理宽度", resolution);
            captureHeight = EditorGUILayout.FloatField("拍摄高度", captureHeight);
            initialZoom = EditorGUILayout.Slider("初始缩放", initialZoom, 1f, 4f);
            captureLayerMask = EditorGUILayout.MaskField("LayerMask", captureLayerMask, UnityEditorInternal.InternalEditorUtility.layers);
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox("地图纹理会保存到 Assets/BundleResources/UI/Common/Atlas，WorldMapDefinition 会保存到 Config/Global。拍摄内容不包含动态单位和 UI 层。", MessageType.Info);
            using (new EditorGUI.DisabledScope(worldLength <= 0f || worldWidth <= 0f || resolution <= 0 || captureHeight <= origin.y))
            {
                if (GUILayout.Button("拍摄并生成地图资源", GUILayout.Height(32f))) Capture();
            }
        }

        /// <summary>执行一次编辑器地图拍摄并更新地图定义资产。</summary>
        private void Capture()
        {
            if (worldLength <= 0f) throw new InvalidOperationException("World map length must be greater than zero.");
            if (worldWidth <= 0f) throw new InvalidOperationException("World map width must be greater than zero.");
            if (resolution <= 0) throw new InvalidOperationException("World map resolution must be greater than zero.");
            if (captureHeight <= origin.y) throw new InvalidOperationException("World map capture height must be above the map origin.");
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded) throw new InvalidOperationException("World map capture requires a loaded active scene.");
            Directory.CreateDirectory(TextureDirectory);
            string texturePath = $"{TextureDirectory}/WorldMap_{activeScene.name}.png";
            int textureHeight = Mathf.Max(1, Mathf.RoundToInt(resolution * worldWidth / worldLength));
            RenderTexture renderTexture = new RenderTexture(resolution, textureHeight, 24, RenderTextureFormat.ARGB32) { name = "World Map Capture", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false };
            GameObject cameraObject = new GameObject("[Editor World Map Capture Camera]");
            try
            {
                Camera captureCamera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.SetPositionAndRotation(new Vector3(origin.x + worldLength * 0.5f, captureHeight, origin.z + worldWidth * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                captureCamera.enabled = false;
                captureCamera.orthographic = true;
                captureCamera.orthographicSize = worldWidth * 0.5f;
                captureCamera.aspect = worldLength / worldWidth;
                captureCamera.nearClipPlane = 0.1f;
                captureCamera.farClipPlane = Mathf.Max(1000f, captureHeight - origin.y + 1000f);
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                // 空拍区域必须输出 Alpha=0，不能使用不透明深色背景，否则导出的 PNG 会出现黑底。
                captureCamera.backgroundColor = Color.clear;
                captureCamera.cullingMask = captureLayerMask;
                captureCamera.allowHDR = false;
                captureCamera.allowMSAA = false;
                captureCamera.useOcclusionCulling = false;
                captureCamera.targetTexture = renderTexture;
                renderTexture.Create();
                captureCamera.Render();
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = renderTexture;
                Texture2D texture = new Texture2D(resolution, textureHeight, TextureFormat.RGBA32, false, false);
                texture.ReadPixels(new Rect(0f, 0f, resolution, textureHeight), 0, 0, false);
                texture.Apply(false, false);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                RenderTexture.active = previousActive;
            }
            finally
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
            Texture2D mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (mapTexture == null) throw new InvalidOperationException($"Failed to import captured map texture '{texturePath}'.");
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            string definitionDirectory = Path.GetDirectoryName(DefinitionPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(definitionDirectory)) Directory.CreateDirectory(definitionDirectory);
            WorldMapDefinition definition = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            definition.MapId = activeScene.name;
            definition.Origin = origin;
            definition.WorldLength = worldLength;
            definition.WorldWidth = worldWidth;
            definition.MapTexture = mapTexture;
            definition.InitialZoom = initialZoom;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = definition;
            EditorUtility.DisplayDialog("World Map Capture", $"地图已生成：{texturePath}", "确定");
        }

        /// <summary>根据项目 Layer 名称初始化默认拍摄层，未配置的 Layer 不参与排除。</summary>
        private void InitializeLayerMask()
        {
            if (initializedLayerMask) return;
            initializedLayerMask = true;
            captureLayerMask = ~0;
            ExcludeLayer("UI");
            ExcludeLayer("Character");
            ExcludeLayer("Enemy");
        }

        /// <summary>从默认拍摄层中移除指定 Layer。</summary>
        private void ExcludeLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) captureLayerMask &= ~(1 << layer);
        }
    }
}
