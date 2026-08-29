using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// 提供 UIKit 新 UI 创建向导，统一生成资源目录和带有基础运行时组件的面板 Prefab。
    /// </summary>
    public sealed class UICreationWindow : EditorWindow
    {
        private const string AsUiMenuPath = "Assets/As UI";
        private const string UiRootDirectory = "Assets/BundleResources/UI";
        private string uiName = string.Empty;

        /// <summary>
        /// 从 UIKit 菜单打开固定最小尺寸的创建窗口。
        /// </summary>
        [MenuItem("Prometheus/UIKit/Create UI", false, 20)]
        private static void OpenWindow()
        {
            UICreationWindow window = GetWindow<UICreationWindow>(true, "Create UIKit UI", true);
            window.minSize = new Vector2(420f, 190f);
            window.Show();
        }

        /// <summary>
        /// 将 Project 窗口当前单选的空目录移动到 UIKit 资产根目录，并以目录名称创建标准 UI 子目录和基础面板 Prefab。
        /// </summary>
        [MenuItem(AsUiMenuPath, false, 2000)]
        private static void ConvertSelectedFolderToUI()
        {
            if (!TryGetSelectedEmptyFolder(out string sourceFolderPath))
                return;

            try
            {
                string prefabPath = ConvertEmptyDirectoryToUI(sourceFolderPath);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"[UIKit Creator] Converted empty folder '{sourceFolderPath}' to UI '{prefabPath}'.", prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("As UI", exception.Message, "OK");
            }
        }

        /// <summary>
        /// 仅当 Project 窗口中恰好选中一个位于 Assets 下的真实空目录时启用 As UI，普通资产、非空目录和多选状态均保持禁用。
        /// </summary>
        /// <returns>当前选择可以安全转换为 UI 时返回 true。</returns>
        [MenuItem(AsUiMenuPath, true)]
        private static bool ValidateConvertSelectedFolderToUI()
        {
            return TryGetSelectedEmptyFolder(out _);
        }

        /// <summary>
        /// 绘制 UI 名称输入框、生成路径预览、校验信息和生成按钮。
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Create UIKit UI", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            uiName = EditorGUILayout.TextField(new GUIContent("UI Name", "例如 Inventory 或 InventoryPanel。未带 Panel 后缀时，Prefab 名称会自动补全。"), uiName);
            string normalizedName = uiName.Trim();
            string panelName = GetPanelName(normalizedName);
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox($"Folder: {UiRootDirectory}/{normalizedName}\nPrefab: {UiRootDirectory}/{normalizedName}/Prefabs/{panelName}.prefab\nAtlas: {UiRootDirectory}/{normalizedName}/Atlas", MessageType.Info);
            string validationError = GetValidationError(normalizedName);

            if (!string.IsNullOrEmpty(validationError))
                EditorGUILayout.HelpBox(validationError, MessageType.Error);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(validationError)))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(30f)))
                    Generate(normalizedName);
            }

            EditorGUILayout.Space(10f);
        }

        /// <summary>
        /// 创建目录和基础 Prefab，并将新 Prefab 设为 Project 视图当前选中对象。
        /// </summary>
        /// <param name="normalizedName">已经去除首尾空白的 UI 名称。</param>
        private void Generate(string normalizedName)
        {
            try
            {
                string prefabPath = CreateUIAssets(normalizedName);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                ShowNotification(new GUIContent($"Created {GetPanelName(normalizedName)}"));
                Debug.Log($"[UIKit Creator] Created UI '{normalizedName}' at '{prefabPath}'.", prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Create UIKit UI", exception.Message, "OK");
            }
        }

        /// <summary>
        /// 创建同名 UI 根目录、Prefabs/Atlas 子目录和带 Binder、RaycastBlocker 的基础面板 Prefab。
        /// 发生异常时只回滚本次刚创建的目标目录，不会覆盖或删除已有 UI 资产。
        /// </summary>
        /// <param name="normalizedName">经过校验的 UI 名称。</param>
        /// <returns>创建成功的 Prefab 资产路径。</returns>
        internal static string CreateUIAssets(string normalizedName)
        {
            string validationError = GetValidationError(normalizedName);
            if (!string.IsNullOrEmpty(validationError))
                throw new InvalidOperationException(validationError);

            string uiDirectory = $"{UiRootDirectory}/{normalizedName}";
            bool createdUiDirectory = false;

            try
            {
                string uiFolderGuid = AssetDatabase.CreateFolder(UiRootDirectory, normalizedName);
                if (string.IsNullOrEmpty(uiFolderGuid))
                    throw new InvalidOperationException($"Failed to create UI directory '{uiDirectory}'.");

                createdUiDirectory = true;
                return CreateUIAssetsInDirectory(normalizedName, uiDirectory);
            }
            catch
            {
                if (createdUiDirectory && AssetDatabase.IsValidFolder(uiDirectory))
                    AssetDatabase.DeleteAsset(uiDirectory);

                throw;
            }
        }

        /// <summary>
        /// 将一个仍为空的 Asset 目录转换为标准 UI；目录会被移动到 UIKit 资产根目录，目录名同时作为 UI 名称和 Panel 名称来源。
        /// 移动后的初始化若失败，会删除本次创建的 UI 内容并尽量把原目录移回原位，避免丢失用户目录及其 GUID。
        /// </summary>
        /// <param name="sourceFolderPath">Project 窗口中选中的空目录资产路径。</param>
        /// <returns>创建成功的基础 Prefab 资产路径。</returns>
        internal static string ConvertEmptyDirectoryToUI(string sourceFolderPath)
        {
            string normalizedSourcePath = sourceFolderPath.Replace('\\', '/').TrimEnd('/');
            if (!IsEmptyAssetFolder(normalizedSourcePath))
                throw new InvalidOperationException($"Folder '{normalizedSourcePath}' must be an empty folder under Assets.");

            string normalizedName = Path.GetFileName(normalizedSourcePath);
            string nameValidationError = GetNameValidationError(normalizedName);
            if (!string.IsNullOrEmpty(nameValidationError))
                throw new InvalidOperationException(nameValidationError);

            if (!AssetDatabase.IsValidFolder(UiRootDirectory))
                throw new InvalidOperationException($"UIKit UI asset directory '{UiRootDirectory}' does not exist.");

            string targetDirectory = $"{UiRootDirectory}/{normalizedName}";
            bool requiresMove = !string.Equals(normalizedSourcePath, targetDirectory, StringComparison.OrdinalIgnoreCase);
            if (requiresMove && AssetDatabase.IsValidFolder(targetDirectory))
                throw new InvalidOperationException($"UI directory '{targetDirectory}' already exists and cannot be overwritten.");

            if (requiresMove)
            {
                string moveError = AssetDatabase.MoveAsset(normalizedSourcePath, targetDirectory);
                if (!string.IsNullOrEmpty(moveError))
                    throw new InvalidOperationException($"Failed to move '{normalizedSourcePath}' to '{targetDirectory}': {moveError}");
            }

            try
            {
                return CreateUIAssetsInDirectory(normalizedName, targetDirectory);
            }
            catch (Exception conversionException)
            {
                if (requiresMove && AssetDatabase.IsValidFolder(targetDirectory))
                {
                    string rollbackError = AssetDatabase.MoveAsset(targetDirectory, normalizedSourcePath);
                    if (!string.IsNullOrEmpty(rollbackError))
                        throw new InvalidOperationException($"UI conversion failed and the folder could not be moved back to '{normalizedSourcePath}': {rollbackError}", conversionException);
                }

                throw;
            }
        }

        /// <summary>
        /// 在已经存在且为空的 UI 根目录中创建 Prefabs、Atlas 和基础面板 Prefab，供创建窗口与 As UI 右键入口共同使用。
        /// 任一步骤失败时会只清理本方法刚创建的内容，而不会删除传入的根目录。
        /// </summary>
        /// <param name="normalizedName">已经通过标识符校验的 UI 名称。</param>
        /// <param name="uiDirectory">已经存在的目标 UI 根目录。</param>
        /// <returns>创建成功的基础 Prefab 资产路径。</returns>
        private static string CreateUIAssetsInDirectory(string normalizedName, string uiDirectory)
        {
            if (!IsEmptyAssetFolder(uiDirectory))
                throw new InvalidOperationException($"UI directory '{uiDirectory}' must be empty before initialization.");

            string prefabsDirectory = $"{uiDirectory}/Prefabs";
            string atlasDirectory = $"{uiDirectory}/Atlas";
            string panelName = GetPanelName(normalizedName);
            string prefabPath = $"{prefabsDirectory}/{panelName}.prefab";
            bool createdPrefabsDirectory = false;
            bool createdAtlasDirectory = false;
            GameObject panelRoot = null;

            try
            {
                string prefabsFolderGuid = AssetDatabase.CreateFolder(uiDirectory, "Prefabs");
                if (string.IsNullOrEmpty(prefabsFolderGuid))
                    throw new InvalidOperationException($"Failed to create Prefabs under '{uiDirectory}'.");

                createdPrefabsDirectory = true;
                string atlasFolderGuid = AssetDatabase.CreateFolder(uiDirectory, "Atlas");
                if (string.IsNullOrEmpty(atlasFolderGuid))
                    throw new InvalidOperationException($"Failed to create Atlas under '{uiDirectory}'.");

                createdAtlasDirectory = true;
                panelRoot = new GameObject(panelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIComponentBinder), typeof(RaycastBlocker));
                int uiLayer = LayerMask.NameToLayer("UI");
                panelRoot.layer = uiLayer >= 0 ? uiLayer : 0;
                RectTransform rectTransform = panelRoot.GetComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
                rectTransform.localScale = Vector3.one;
                RaycastBlocker blocker = panelRoot.GetComponent<RaycastBlocker>();
                blocker.raycastTarget = true;
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(panelRoot, prefabPath);
                if (savedPrefab == null)
                    throw new InvalidOperationException($"Failed to save base UI prefab '{prefabPath}'.");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return prefabPath;
            }
            catch
            {
                if (AssetDatabase.LoadMainAssetAtPath(prefabPath) != null)
                    AssetDatabase.DeleteAsset(prefabPath);

                if (createdAtlasDirectory && AssetDatabase.IsValidFolder(atlasDirectory))
                    AssetDatabase.DeleteAsset(atlasDirectory);

                if (createdPrefabsDirectory && AssetDatabase.IsValidFolder(prefabsDirectory))
                    AssetDatabase.DeleteAsset(prefabsDirectory);

                AssetDatabase.Refresh();
                throw;
            }
            finally
            {
                if (panelRoot != null)
                    DestroyImmediate(panelRoot);
            }
        }

        /// <summary>
        /// 尝试取得 Project 窗口中唯一选中的空目录，并确保该目录属于可写的 Assets 资产树。
        /// </summary>
        /// <param name="folderPath">校验成功时返回统一使用正斜杠的资产目录路径。</param>
        /// <returns>当前恰好单选了一个可转换空目录时返回 true。</returns>
        private static bool TryGetSelectedEmptyFolder(out string folderPath)
        {
            folderPath = string.Empty;
            if (Selection.objects.Length != 1 || Selection.activeObject == null)
                return false;

            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject).Replace('\\', '/').TrimEnd('/');
            if (!selectedPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false;

            if (!IsEmptyAssetFolder(selectedPath))
                return false;

            folderPath = selectedPath;
            return true;
        }

        /// <summary>
        /// 判断给定资产路径是否对应磁盘上的真实空目录；目录中的隐藏文件也会被视为内容，防止转换时意外搬运用户资产。
        /// </summary>
        /// <param name="assetFolderPath">以 Assets 开头的 Unity 资产目录路径。</param>
        /// <returns>目录存在且不包含任何文件或子目录时返回 true。</returns>
        private static bool IsEmptyAssetFolder(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath) || !assetFolderPath.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(assetFolderPath))
                return false;

            string absoluteFolderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetFolderPath));
            return Directory.Exists(absoluteFolderPath) && Directory.GetFileSystemEntries(absoluteFolderPath).Length == 0;
        }

        /// <summary>
        /// 返回当前名称的校验错误；空字符串表示名称可以安全用于目录、Prefab 和生成的 C# 类型。
        /// </summary>
        private static string GetValidationError(string normalizedName)
        {
            string nameValidationError = GetNameValidationError(normalizedName);
            if (!string.IsNullOrEmpty(nameValidationError))
                return nameValidationError;

            string uiDirectory = $"{UiRootDirectory}/{normalizedName}";
            if (AssetDatabase.IsValidFolder(uiDirectory))
                return $"UI directory '{uiDirectory}' already exists. Choose another name to avoid overwriting assets.";

            return string.Empty;
        }

        /// <summary>
        /// 返回 UI 名称本身的校验错误，不检查目标目录是否已存在，供新建和空目录转换流程分别组合自己的目录规则。
        /// </summary>
        /// <param name="normalizedName">已经去除首尾空白的 UI 名称。</param>
        /// <returns>名称有效时返回空字符串，否则返回可直接展示给用户的错误信息。</returns>
        private static string GetNameValidationError(string normalizedName)
        {
            if (string.IsNullOrWhiteSpace(normalizedName))
                return "UI name cannot be empty.";

            if (!IsValidIdentifier(normalizedName))
                return "UI name must start with a letter or underscore and contain only letters, digits, or underscores.";

            return string.Empty;
        }

        /// <summary>
        /// 判断名称是否同时满足文件夹名称和 C# 标识符约束，支持 Unicode 字母。
        /// </summary>
        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || (!char.IsLetter(value[0]) && value[0] != '_'))
                return false;

            for (int index = 1; index < value.Length; index++)
            {
                if (!char.IsLetterOrDigit(value[index]) && value[index] != '_')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 根据 UI 名称获得基础 Prefab 名称，未包含 Panel 后缀时自动补全。
        /// </summary>
        private static string GetPanelName(string normalizedName)
        {
            return normalizedName.EndsWith("Panel", StringComparison.Ordinal) ? normalizedName : normalizedName + "Panel";
        }
    }
}
