#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>把 Project 窗口中同目录多选的 Spine AnimationReferenceAsset 安全转换为独立 AnimationLine 资产。</summary>
    public static class AnimationReferenceAssetConversionTool
    {
        /// <summary>Project 窗口右键菜单路径。</summary>
        private const string MenuPath = "Assets/Prometheus/Convert to AnimationLine";
        /// <summary>生成资产相对源动画目录使用的固定子目录名。</summary>
        private const string OutputDirectoryName = "AnimationLines";

        /// <summary>描述一次转换中一个源动画与其唯一目标资产路径。</summary>
        private sealed class ConversionEntry
        {
            /// <summary>初始化不可变转换条目。</summary>
            public ConversionEntry(AnimationReferenceAsset source, string sourcePath, string destinationPath)
            {
                Source = source;
                SourcePath = sourcePath;
                DestinationPath = destinationPath;
            }

            /// <summary>获取需要包装的 Spine 动画引用。</summary>
            public AnimationReferenceAsset Source { get; }
            /// <summary>获取源动画的项目相对路径。</summary>
            public string SourcePath { get; }
            /// <summary>获取待创建 AnimationLine 的项目相对路径。</summary>
            public string DestinationPath { get; }
        }

        /// <summary>保存完成全部预检后才允许执行的批量转换计划。</summary>
        private sealed class ConversionPlan
        {
            /// <summary>初始化同一源目录下的批量转换计划。</summary>
            public ConversionPlan(string sourceDirectory, string outputDirectory, List<ConversionEntry> entries)
            {
                SourceDirectory = sourceDirectory;
                OutputDirectory = outputDirectory;
                Entries = entries;
            }

            /// <summary>获取全部源动画共同所在的目录。</summary>
            public string SourceDirectory { get; }
            /// <summary>获取本批次唯一的 AnimationLines 输出目录。</summary>
            public string OutputDirectory { get; }
            /// <summary>获取已经通过路径冲突检查的转换条目。</summary>
            public List<ConversionEntry> Entries { get; }
        }

        /// <summary>从当前 Project 选择创建全部 AnimationLine；任何预检或创建失败都会阻止保留不完整批次。</summary>
        [MenuItem(MenuPath, false, 2000)]
        private static void ConvertSelectedAnimationReferences()
        {
            if (!TryCollectSelectedAnimationReferences(out List<AnimationReferenceAsset> selectedReferences, out string selectionError))
            {
                EditorUtility.DisplayDialog("AnimationLine 转换", selectionError, "确定");
                return;
            }
            if (!TryBuildConversionPlan(selectedReferences, out ConversionPlan plan, out string planError))
            {
                EditorUtility.DisplayDialog("AnimationLine 转换", planError, "确定");
                return;
            }

            List<string> createdAssetPaths = new List<string>(plan.Entries.Count);
            List<AnimationLine> createdLines = new List<AnimationLine>(plan.Entries.Count);
            bool outputDirectoryCreated = false;
            try
            {
                outputDirectoryCreated = EnsureOutputDirectory(plan);
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int index = 0; index < plan.Entries.Count; index++)
                    {
                        ConversionEntry entry = plan.Entries[index];
                        createdAssetPaths.Add(entry.DestinationPath);
                        AnimationLine line = ScriptableObject.CreateInstance<AnimationLine>();
                        line.name = entry.Source.name;
                        line.SetAnimationReference(entry.Source);
                        try
                        {
                            AssetDatabase.CreateAsset(line, entry.DestinationPath);
                        }
                        catch
                        {
                            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(line))) UnityEngine.Object.DestroyImmediate(line);
                            throw;
                        }
                        createdLines.Add(line);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                RollbackCreatedAssets(createdAssetPaths, plan.OutputDirectory, outputDirectoryCreated);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("AnimationLine 转换失败", $"批量转换未完成，已经回收本批次创建的资产。\n\n{exception.Message}", "确定");
                return;
            }

            Selection.objects = createdLines.ToArray();
            EditorGUIUtility.PingObject(createdLines[0]);
            Debug.Log($"AnimationLine 转换完成：从 {plan.SourceDirectory} 转换 {createdLines.Count} 个 AnimationReferenceAsset，输出目录为 {plan.OutputDirectory}。", createdLines[0]);
        }

        /// <summary>仅在当前选择全部为同目录 AnimationReferenceAsset 时启用右键菜单；目标冲突由点击后的完整预检报告。</summary>
        [MenuItem(MenuPath, true)]
        private static bool ValidateConvertSelectedAnimationReferences()
        {
            return TryCollectSelectedAnimationReferences(out List<AnimationReferenceAsset> selectedReferences, out _) && TryResolveSourceDirectory(selectedReferences, out _, out _);
        }

        /// <summary>把当前 Project 选择转换为强类型动画引用列表，混入其他对象时整批拒绝。</summary>
        private static bool TryCollectSelectedAnimationReferences(out List<AnimationReferenceAsset> selectedReferences, out string error)
        {
            selectedReferences = new List<AnimationReferenceAsset>();
            error = null;
            UnityEngine.Object[] selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                error = "请在 Project 窗口中选择一个或多个 AnimationReferenceAsset。";
                return false;
            }
            for (int index = 0; index < selectedObjects.Length; index++)
            {
                if (!(selectedObjects[index] is AnimationReferenceAsset animationReference))
                {
                    error = "所选对象必须全部是 AnimationReferenceAsset，不能混入目录或其他资源。";
                    selectedReferences.Clear();
                    return false;
                }
                selectedReferences.Add(animationReference);
            }
            return true;
        }

        /// <summary>预先验证源资产、输出目录和全部目标路径，确保创建阶段不会覆盖或生成部分有效批次。</summary>
        private static bool TryBuildConversionPlan(IReadOnlyList<AnimationReferenceAsset> selectedReferences, out ConversionPlan plan, out string error)
        {
            plan = null;
            error = null;
            if (!TryResolveSourceDirectory(selectedReferences, out string sourceDirectory, out error)) return false;
            string outputDirectory = sourceDirectory + "/" + OutputDirectoryName;
            if (AssetDatabase.AssetPathExists(outputDirectory) && !AssetDatabase.IsValidFolder(outputDirectory))
            {
                error = $"输出路径已被非目录资源占用，无法创建 AnimationLines：\n{outputDirectory}";
                return false;
            }

            List<ConversionEntry> entries = new List<ConversionEntry>(selectedReferences.Count);
            List<string> conflictingPaths = new List<string>();
            for (int index = 0; index < selectedReferences.Count; index++)
            {
                AnimationReferenceAsset source = selectedReferences[index];
                string sourcePath = NormalizeAssetPath(AssetDatabase.GetAssetPath(source));
                try
                {
                    if (source.Animation == null)
                    {
                        error = $"AnimationReferenceAsset 无法解析 Spine 动画，未创建任何资产：\n{sourcePath}";
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    error = $"读取 AnimationReferenceAsset 时发生错误，未创建任何资产：\n{sourcePath}\n\n{exception.Message}";
                    return false;
                }
                string assetFileName = Path.GetFileNameWithoutExtension(sourcePath) + ".asset";
                string destinationPath = outputDirectory + "/" + assetFileName;
                if (AssetDatabase.AssetPathExists(destinationPath)) conflictingPaths.Add(destinationPath);
                entries.Add(new ConversionEntry(source, sourcePath, destinationPath));
            }
            if (conflictingPaths.Count > 0)
            {
                error = "以下 AnimationLine 已存在。为避免覆盖或产生带编号的重复资产，本批次未执行：\n\n" + string.Join("\n", conflictingPaths);
                return false;
            }
            entries.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.SourcePath, right.SourcePath));
            plan = new ConversionPlan(sourceDirectory, outputDirectory, entries);
            return true;
        }

        /// <summary>验证全部源对象都是同一目录下的独立主资产，并拒绝重复选择和非 Assets 路径。</summary>
        private static bool TryResolveSourceDirectory(IReadOnlyList<AnimationReferenceAsset> selectedReferences, out string sourceDirectory, out string error)
        {
            sourceDirectory = null;
            error = null;
            if (selectedReferences == null || selectedReferences.Count == 0)
            {
                error = "请至少选择一个 AnimationReferenceAsset。";
                return false;
            }
            HashSet<string> sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < selectedReferences.Count; index++)
            {
                AnimationReferenceAsset source = selectedReferences[index];
                if (source == null)
                {
                    error = "选择中包含已经丢失的 AnimationReferenceAsset 引用。";
                    return false;
                }
                string sourcePath = NormalizeAssetPath(AssetDatabase.GetAssetPath(source));
                if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Assets/", StringComparison.Ordinal) || !string.Equals(Path.GetExtension(sourcePath), ".asset", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"AnimationReferenceAsset 必须是 Assets 目录下独立保存的 .asset 主资源：\n{source.name}";
                    return false;
                }
                if (AssetDatabase.LoadMainAssetAtPath(sourcePath) != source)
                {
                    error = $"不支持把子资源或非主资源转换为 AnimationLine：\n{sourcePath}";
                    return false;
                }
                if (!sourcePaths.Add(sourcePath))
                {
                    error = $"同一个 AnimationReferenceAsset 被重复选择：\n{sourcePath}";
                    return false;
                }
                string currentDirectory = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
                if (string.IsNullOrEmpty(currentDirectory) || !AssetDatabase.IsValidFolder(currentDirectory))
                {
                    error = $"无法解析 AnimationReferenceAsset 所在目录：\n{sourcePath}";
                    return false;
                }
                if (sourceDirectory == null) sourceDirectory = currentDirectory;
                else if (!string.Equals(sourceDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    error = "多选的 AnimationReferenceAsset 必须位于同一个目录，工具只会创建一个同级 AnimationLines 输出目录。";
                    return false;
                }
            }
            return true;
        }

        /// <summary>创建本批次唯一输出目录，并返回该目录是否由当前操作新建。</summary>
        private static bool EnsureOutputDirectory(ConversionPlan plan)
        {
            if (AssetDatabase.IsValidFolder(plan.OutputDirectory)) return false;
            string folderGuid = AssetDatabase.CreateFolder(plan.SourceDirectory, OutputDirectoryName);
            if (string.IsNullOrEmpty(folderGuid) || !AssetDatabase.IsValidFolder(plan.OutputDirectory)) throw new IOException($"无法创建 AnimationLine 输出目录：{plan.OutputDirectory}");
            return true;
        }

        /// <summary>逆序删除本批次已经登记的目标资产，并仅在本操作新建且仍为空时删除输出目录。</summary>
        private static void RollbackCreatedAssets(IReadOnlyList<string> createdAssetPaths, string outputDirectory, bool outputDirectoryCreated)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int index = createdAssetPaths.Count - 1; index >= 0; index--)
                {
                    string assetPath = createdAssetPaths[index];
                    if (AssetDatabase.AssetPathExists(assetPath) && !AssetDatabase.DeleteAsset(assetPath)) Debug.LogError($"回收未完成的 AnimationLine 失败，请手动检查：{assetPath}");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            if (outputDirectoryCreated && AssetDatabase.IsValidFolder(outputDirectory))
            {
                string[] remainingAssetGuids = AssetDatabase.FindAssets(string.Empty, new[] { outputDirectory });
                if (remainingAssetGuids.Length == 0) AssetDatabase.DeleteAsset(outputDirectory);
                else Debug.LogWarning($"转换失败后输出目录仍包含其他资源，因此保留目录：{outputDirectory}");
            }
            AssetDatabase.Refresh();
        }

        /// <summary>把系统路径分隔符统一为 Unity AssetDatabase 使用的正斜杠。</summary>
        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
#endif
