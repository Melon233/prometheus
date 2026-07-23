using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts Spine's default export extensions into the extensions recognized by spine-unity 3.8.
/// </summary>
public static class SpineExportConverter
{
    private const string MenuPath = "Assets/Spine/转换所选三件套为 Unity 资源";
    private const float DefaultSkeletonDataScale = 0.001f;

    private sealed class SelectedSpineFiles
    {
        public string BaseName;
        public string Directory;
        public string AtlasPath;
        public string TexturePath;
        public string SkeletonPath;

        public string UnityAtlasPath => AtlasPath + ".txt";
        public string UnitySkeletonPath => SkeletonPath + ".bytes";
        public string AtlasAssetPath => Directory + "/" + BaseName + "_Atlas.asset";
        public string SkeletonDataAssetPath => Directory + "/" + BaseName + "_SkeletonData.asset";
    }

    [MenuItem(MenuPath, false, 2000)]
    private static void ConvertSelectedFiles()
    {
        if (!TryGetSelectedFiles(out SelectedSpineFiles files, out string error))
        {
            EditorUtility.DisplayDialog("Spine 资源转换", error, "确定");
            return;
        }

        if (AssetExists(files.UnityAtlasPath) || AssetExists(files.UnitySkeletonPath))
        {
            EditorUtility.DisplayDialog(
                "Spine 资源转换",
                "目标文件已存在，未进行覆盖：\n\n" +
                files.UnityAtlasPath + "\n" +
                files.UnitySkeletonPath +
                "\n\n请先确认或移走已有文件。",
                "确定");
            return;
        }

        bool atlasMoved = false;
        bool skeletonMoved = false;

        try
        {
            AssetDatabase.StartAssetEditing();

            MoveAssetOrThrow(files.AtlasPath, files.UnityAtlasPath);
            atlasMoved = true;

            MoveAssetOrThrow(files.SkeletonPath, files.UnitySkeletonPath);
            skeletonMoved = true;
        }
        catch (Exception exception)
        {
            // Keep the three source files together if the second rename unexpectedly fails.
            if (skeletonMoved)
                AssetDatabase.MoveAsset(files.UnitySkeletonPath, files.SkeletonPath);
            if (atlasMoved)
                AssetDatabase.MoveAsset(files.UnityAtlasPath, files.AtlasPath);

            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Spine 资源转换失败",
                exception.Message + "\n\n文件已尽可能恢复为转换前的名称。",
                "确定");
            return;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // StopAssetEditing imports both renamed files as one batch. The spine-unity
        // postprocessor then creates the AtlasAsset, material and SkeletonDataAsset.
        AssetDatabase.SaveAssets();

        Spine.Unity.SkeletonDataAsset skeletonDataAsset =
            AssetDatabase.LoadAssetAtPath<Spine.Unity.SkeletonDataAsset>(files.SkeletonDataAssetPath);

        if (skeletonDataAsset != null)
        {
            SetSkeletonDataScale(skeletonDataAsset);
            Selection.activeObject = skeletonDataAsset;
            EditorGUIUtility.PingObject(skeletonDataAsset);
            Debug.Log(
                "Spine 资源转换完成：" + files.SkeletonDataAssetPath +
                "，Scale=" + skeletonDataAsset.scale,
                skeletonDataAsset);
        }
        else
        {
            UnityEngine.Object atlasAsset = AssetDatabase.LoadMainAssetAtPath(files.AtlasAssetPath);
            if (atlasAsset != null)
            {
                Selection.activeObject = atlasAsset;
                EditorGUIUtility.PingObject(atlasAsset);
            }

            EditorUtility.DisplayDialog(
                "Spine 文件后缀转换完成",
                "已生成 Unity 可识别的 .atlas.txt 与 .skel.bytes 文件，但 Spine Runtime 没有生成 SkeletonDataAsset。" +
                "\n\n请查看 Console 中的 Spine 导入错误（通常是导出版本或图集图片不匹配）。",
                "确定");
        }
    }

    private static void SetSkeletonDataScale(
        Spine.Unity.SkeletonDataAsset skeletonDataAsset,
        float scale = DefaultSkeletonDataScale)
    {
        Undo.RecordObject(skeletonDataAsset, "Set Spine SkeletonData Scale");
        skeletonDataAsset.scale = scale;
        skeletonDataAsset.Clear();
        skeletonDataAsset.GetSkeletonData(true);
        EditorUtility.SetDirty(skeletonDataAsset);
        AssetDatabase.SaveAssets();
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateConvertSelectedFiles()
    {
        return TryGetSelectedFiles(out _, out _);
    }

    private static bool TryGetSelectedFiles(out SelectedSpineFiles files, out string error)
    {
        files = null;
        error = "请在 Project 窗口中同时选中同名的 .atlas、.png 和 .skel 文件。";

        var selectedPaths = new List<string>();
        foreach (UnityEngine.Object selectedObject in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selectedObject);
            if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
                selectedPaths.Add(path.Replace('\\', '/'));
        }

        if (selectedPaths.Count != 3)
            return false;

        var result = new SelectedSpineFiles();
        foreach (string path in selectedPaths)
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase))
                result.AtlasPath = path;
            else if (extension.Equals(".skel", StringComparison.OrdinalIgnoreCase))
                result.SkeletonPath = path;
            else if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                result.TexturePath = path;
            else
                return false;
        }

        if (string.IsNullOrEmpty(result.AtlasPath) ||
            string.IsNullOrEmpty(result.TexturePath) ||
            string.IsNullOrEmpty(result.SkeletonPath))
        {
            return false;
        }

        string atlasBaseName = Path.GetFileNameWithoutExtension(result.AtlasPath);
        string textureBaseName = Path.GetFileNameWithoutExtension(result.TexturePath);
        string skeletonBaseName = Path.GetFileNameWithoutExtension(result.SkeletonPath);

        if (!atlasBaseName.Equals(textureBaseName, StringComparison.OrdinalIgnoreCase) ||
            !atlasBaseName.Equals(skeletonBaseName, StringComparison.OrdinalIgnoreCase))
        {
            error = "选中的 .atlas、.png 和 .skel 必须同名且位于同一个文件夹。";
            return false;
        }

        string atlasDirectory = NormalizeDirectory(result.AtlasPath);
        string textureDirectory = NormalizeDirectory(result.TexturePath);
        string skeletonDirectory = NormalizeDirectory(result.SkeletonPath);

        if (!atlasDirectory.Equals(textureDirectory, StringComparison.OrdinalIgnoreCase) ||
            !atlasDirectory.Equals(skeletonDirectory, StringComparison.OrdinalIgnoreCase))
        {
            error = "选中的 .atlas、.png 和 .skel 必须位于同一个文件夹。";
            return false;
        }

        result.BaseName = atlasBaseName;
        result.Directory = atlasDirectory;
        files = result;
        error = null;
        return true;
    }

    private static string NormalizeDirectory(string assetPath)
    {
        return (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
    }

    private static bool AssetExists(string assetPath)
    {
        return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) ||
               File.Exists(ToAbsolutePath(assetPath));
    }

    private static string ToAbsolutePath(string assetPath)
    {
        string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectDirectory, assetPath));
    }

    private static void MoveAssetOrThrow(string sourcePath, string destinationPath)
    {
        string moveError = AssetDatabase.MoveAsset(sourcePath, destinationPath);
        if (!string.IsNullOrEmpty(moveError))
            throw new IOException(moveError);
    }
}
