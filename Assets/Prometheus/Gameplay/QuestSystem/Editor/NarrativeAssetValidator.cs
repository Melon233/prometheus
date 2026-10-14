#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Cutscene;

namespace Xuan.Prometheus.Quest.Editor
{
    /// <summary>
    /// 一键校验工程内全部演出与任务资产的结构；与场景的锚点对照见「校验场景锚点」。
    /// 运行时播放前同样会调用这两个校验，这里只是把错误提前到编辑期一次性暴露。
    /// </summary>
    internal static class NarrativeAssetValidator
    {
        /// <summary>遍历全部 CutsceneDefinition 与 QuestDefinition，逐个输出错误；全部通过时输出一条汇总。</summary>
        [MenuItem("Prometheus/Narrative/校验全部演出与任务")]
        private static void ValidateAll()
        {
            int checkedCount = 0;
            int failedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(CutsceneDefinition)}"))
            {
                CutsceneDefinition asset = AssetDatabase.LoadAssetAtPath<CutsceneDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                List<string> errors = new List<string>();
                asset.Validate(errors);
                checkedCount++;
                if (Report(asset, errors)) failedCount++;
            }
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(QuestDefinition)}"))
            {
                QuestDefinition asset = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                List<string> errors = new List<string>();
                asset.Validate(errors);
                checkedCount++;
                if (Report(asset, errors)) failedCount++;
            }
            Debug.Log($"[叙事校验] 共检查 {checkedCount} 个资产，{failedCount} 个存在错误。");
        }

        /// <summary>
        /// 锚点核对：收集全部演出与任务资产引用的 WorldAnchor id，与当前打开场景中的锚点对照。
        /// 被引用却不存在的报错（指向引用它的资产）；存在却没人引用的列为提示，便于清理。
        /// 只对当前打开的场景有意义——服务于其他场景的资产会被报告为缺失，请在对应场景中执行。
        /// </summary>
        [MenuItem("Prometheus/Narrative/校验场景锚点")]
        private static void ValidateAnchors()
        {
            HashSet<string> sceneAnchors = new HashSet<string>();
            foreach (WorldAnchor anchor in Object.FindObjectsByType<WorldAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(anchor.AnchorId)) Debug.LogError($"[锚点校验] WorldAnchor '{anchor.name}' 未配置 id。", anchor);
                else if (!sceneAnchors.Add(anchor.AnchorId)) Debug.LogError($"[锚点校验] 场景中存在重复的锚点 id '{anchor.AnchorId}'。", anchor);
            }
            HashSet<string> referenced = new HashSet<string>();
            int missing = 0;
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(CutsceneDefinition)}"))
            {
                CutsceneDefinition asset = AssetDatabase.LoadAssetAtPath<CutsceneDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                missing += CheckReferences(asset, asset.CollectAnchorIds(), sceneAnchors, referenced);
            }
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(QuestDefinition)}"))
            {
                QuestDefinition asset = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                missing += CheckReferences(asset, asset.CollectAnchorIds(), sceneAnchors, referenced);
            }
            List<string> unused = new List<string>();
            foreach (string id in sceneAnchors)
            {
                if (!referenced.Contains(id)) unused.Add(id);
            }
            if (unused.Count > 0) Debug.Log($"[锚点校验] 场景中未被任何演出或任务引用的锚点：{string.Join(", ", unused)}");
            Debug.Log($"[锚点校验] 场景锚点 {sceneAnchors.Count} 个，被引用 {referenced.Count} 个，缺失引用 {missing} 处。");
        }

        /// <summary>核对一个资产引用的锚点，返回缺失数量。</summary>
        private static int CheckReferences(Object asset, IEnumerable<string> ids, HashSet<string> sceneAnchors, HashSet<string> referenced)
        {
            int missing = 0;
            foreach (string id in ids)
            {
                referenced.Add(id);
                if (sceneAnchors.Contains(id)) continue;
                Debug.LogError($"[锚点校验] {asset.name} 引用的锚点 '{id}' 在当前场景中不存在。", asset);
                missing++;
            }
            return missing;
        }

        /// <summary>输出单个资产的错误；点击日志可定位到资产。</summary>
        /// <returns>存在错误时返回真。</returns>
        private static bool Report(Object asset, List<string> errors)
        {
            if (errors.Count == 0) return false;
            Debug.LogError($"[叙事校验] {asset.name}：\n{string.Join("\n", errors)}", asset);
            return true;
        }
    }
}
#endif
