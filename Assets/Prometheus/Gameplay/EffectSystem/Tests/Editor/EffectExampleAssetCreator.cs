#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Effects.Editor
{
    /// <summary>
    /// EffectExampleAssetCreator 是仅在测试程序集编译的编辑器工具，通过菜单生成默认效果及其触发集合，避免直接维护 Unity YAML。
    /// </summary>
    public static class EffectExampleAssetCreator
    {
        private const string RootFolder = "Assets/BundleResources/Config/Effect";
        private const string EffectDefinitionsFolder = RootFolder + "/EffectDefinitions";
        private const string TriggerDefinitionsFolder = RootFolder + "/TriggerDefinitions";
        private const string DirectDamagePath = EffectDefinitionsFolder + "/DirectAttackDamage.asset";
        private const string BurningPath = EffectDefinitionsFolder + "/Burning.asset";
        private const string CombatFlowPath = EffectDefinitionsFolder + "/CombatFlow.asset";
        private const string StunPath = EffectDefinitionsFolder + "/Stun.asset";
        private const string AttackTriggersPath = TriggerDefinitionsFolder + "/AttackTriggers.asset";
        private const string CombatFlowTriggersPath = TriggerDefinitionsFolder + "/CombatFlowTriggers.asset";
        private const string LibraryPath = RootFolder + "/EffectLibrary.asset";

        /// <summary>
        /// 创建或更新全部示例资产，并选中最终示例库。
        /// </summary>
        [MenuItem("Prometheus/Effect System/Create Or Update Example Assets")]
        public static void CreateOrUpdate()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(EffectDefinitionsFolder);
            EnsureFolder(TriggerDefinitionsFolder);
            EffectDefinition directDamage = LoadOrCreate<EffectDefinition>(DirectDamagePath);
            EffectDefinition burning = LoadOrCreate<EffectDefinition>(BurningPath);
            EffectDefinition combatFlow = LoadOrCreate<EffectDefinition>(CombatFlowPath);
            EffectDefinition stun = LoadOrCreate<EffectDefinition>(StunPath);
            EffectTriggerSet attackTriggers = LoadOrCreate<EffectTriggerSet>(AttackTriggersPath);
            EffectTriggerSet combatFlowTriggers = LoadOrCreate<EffectTriggerSet>(CombatFlowTriggersPath);
            EffectLibrary library = LoadOrCreate<EffectLibrary>(LibraryPath);
            EffectExampleFactory.Configure(directDamage, burning, combatFlow, stun, attackTriggers, combatFlowTriggers, library);
            EditorUtility.SetDirty(directDamage);
            EditorUtility.SetDirty(burning);
            EditorUtility.SetDirty(combatFlow);
            EditorUtility.SetDirty(stun);
            EditorUtility.SetDirty(attackTriggers);
            EditorUtility.SetDirty(combatFlowTriggers);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = library;
            Debug.Log($"Effect System example assets are ready at {RootFolder}.", library);
        }

        /// <summary>
        /// 加载已有资产；不存在时创建同类型新资产。
        /// </summary>
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null && MonoScript.FromScriptableObject(asset) != null) return asset;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>
        /// 逐级创建目标文件夹，确保首次执行菜单时路径存在。
        /// </summary>
        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
