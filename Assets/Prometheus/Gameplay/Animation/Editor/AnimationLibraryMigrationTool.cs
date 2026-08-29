#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>把角色与敌人动画库中已经配置的 AnimationLine 批量标记为稳定动画语义，并验证全项目语义完整性。</summary>
    public static class AnimationLibraryMigrationTool
    {
        /// <summary>迁移全部 AnimationLibrary；重复执行保持幂等，相同 AnimationLine 的冲突语义会输出明确错误。</summary>
        [MenuItem("Prometheus/Animation/Migrate Libraries To Semantic AnimationLine")]
        public static void MigrateAll()
        {
            string[] libraryGuids = AssetDatabase.FindAssets("t:AnimationLibrary");
            int migratedLibraryCount = 0;
            for (int index = 0; index < libraryGuids.Length; index++)
            {
                string libraryPath = AssetDatabase.GUIDToAssetPath(libraryGuids[index]);
                AnimationLibrary library = AssetDatabase.LoadAssetAtPath<AnimationLibrary>(libraryPath);
                if (library == null) continue;
                MigrateLibrary(library);
                migratedLibraryCount++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            int missingSemanticCount = CountMissingSemantics();
            if (missingSemanticCount == 0) Debug.Log($"AnimationLine 语义迁移完成：处理 {migratedLibraryCount} 个 AnimationLibrary，全部 AnimationLine 均已配置语义。");
            else Debug.LogWarning($"AnimationLine 语义迁移完成：处理 {migratedLibraryCount} 个 AnimationLibrary，仍有 {missingSemanticCount} 个未被动画库引用的 AnimationLine 缺少语义。");
        }

        /// <summary>按照动画库字段的玩法职责为全部 AnimationLine 写入稳定语义。</summary>
        private static void MigrateLibrary(AnimationLibrary library)
        {
            SerializedObject serializedLibrary = new SerializedObject(library);
            AssignAttackSemantics(serializedLibrary.FindProperty("atkExecutor"));
            AssignSemantic(serializedLibrary.FindProperty("idleExecutor"), "idleLine", AnimationSemantic.Idle);
            AssignSemantic(serializedLibrary.FindProperty("groundMoveExecutor"), "walkLine", AnimationSemantic.Walk);
            AssignSemantic(serializedLibrary.FindProperty("groundMoveExecutor"), "runLine", AnimationSemantic.Run);
            AssignSemantic(serializedLibrary.FindProperty("groundMoveExecutor"), "sprintLine", AnimationSemantic.Sprint);
            AssignSemantic(serializedLibrary.FindProperty("dodgeExecutor"), "dodgeFrontLine", AnimationSemantic.DodgeFront);
            AssignSemantic(serializedLibrary.FindProperty("dodgeExecutor"), "dodgeBackLine", AnimationSemantic.DodgeBack);
            AssignSemantic(serializedLibrary.FindProperty("airMoveExecutor"), "jumpLine", AnimationSemantic.JumpStart);
            AssignSemantic(serializedLibrary.FindProperty("airMoveExecutor"), "riseLine", AnimationSemantic.Rise);
            AssignSemantic(serializedLibrary.FindProperty("airMoveExecutor"), "fallLine", AnimationSemantic.Fall);
            AssignSemantic(serializedLibrary.FindProperty("airMoveExecutor"), "landLine", AnimationSemantic.Land);
            AssignSemantic(serializedLibrary.FindProperty("ultimateExecutor"), "ultimateLine", AnimationSemantic.Ultimate);
            AssignSemantic(serializedLibrary.FindProperty("skillExecutor"), "skillStartLine", AnimationSemantic.SkillStart);
            AssignSemantic(serializedLibrary.FindProperty("skillExecutor"), "skillLine", AnimationSemantic.Skill);
            AssignSemantic(serializedLibrary.FindProperty("specialAttackExecutor"), "specialAttackLine", AnimationSemantic.SpecialAttack);
            AssignSemantic(serializedLibrary.FindProperty("attackedExecutor"), "attackedLine", AnimationSemantic.Hit);
            AssignSemantic(serializedLibrary.FindProperty("attackedExecutor"), "nextAttackedLine", AnimationSemantic.HitRecovery);
            AssignSemantic(serializedLibrary.FindProperty("dieExecutor"), "dieLine", AnimationSemantic.Death);
            library.InvalidateSemanticIndex();
            EditorUtility.SetDirty(library);
        }

        /// <summary>按照普通攻击连段下标为原地和移动 AnimationLine 分配语义，同一资产作为移动回退时保持原地语义。</summary>
        private static void AssignAttackSemantics(SerializedProperty attackConfiguration)
        {
            if (attackConfiguration == null) return;
            SerializedProperty definitions = attackConfiguration.FindPropertyRelative("attacks");
            if (definitions == null) return;
            for (int index = 0; index < definitions.arraySize; index++)
            {
                SerializedProperty definition = definitions.GetArrayElementAtIndex(index);
                AnimationLine normalLine = definition.FindPropertyRelative("animationLine")?.objectReferenceValue as AnimationLine;
                AnimationLine movingLine = definition.FindPropertyRelative("movingAnimationLine")?.objectReferenceValue as AnimationLine;
                AnimationSemantic normalSemantic = GetAttackSemantic(index, false);
                AnimationSemantic movingSemantic = GetAttackSemantic(index, true);
                AssignSemantic(normalLine, normalSemantic);
                if (movingLine != null && movingLine != normalLine) AssignSemantic(movingLine, movingSemantic);
            }
        }

        /// <summary>把一个嵌套配置字段中的 AnimationLine 标记为指定语义。</summary>
        private static void AssignSemantic(SerializedProperty configuration, string linePropertyName, AnimationSemantic semantic)
        {
            if (configuration == null) return;
            AnimationLine line = configuration.FindPropertyRelative(linePropertyName)?.objectReferenceValue as AnimationLine;
            AssignSemantic(line, semantic);
        }

        /// <summary>安全写入 AnimationLine 语义；已经存在的不同语义视为跨动画库配置冲突。</summary>
        private static void AssignSemantic(AnimationLine line, AnimationSemantic semantic)
        {
            if (line == null || semantic == AnimationSemantic.None) return;
            if (line.Semantic != AnimationSemantic.None && line.Semantic != semantic)
            {
                Debug.LogError($"AnimationLine '{AssetDatabase.GetAssetPath(line)}' 已标记为 '{line.Semantic}'，无法再次标记为 '{semantic}'。", line);
                return;
            }
            line.SetSemantic(semantic);
            EditorUtility.SetDirty(line);
        }

        /// <summary>把零起始连段下标转换为稳定普通攻击语义，当前系统明确支持四段连击。</summary>
        private static AnimationSemantic GetAttackSemantic(int index, bool moving)
        {
            switch (index)
            {
                case 0: return moving ? AnimationSemantic.Attack1Move : AnimationSemantic.Attack1;
                case 1: return moving ? AnimationSemantic.Attack2Move : AnimationSemantic.Attack2;
                case 2: return moving ? AnimationSemantic.Attack3Move : AnimationSemantic.Attack3;
                case 3: return moving ? AnimationSemantic.Attack4Move : AnimationSemantic.Attack4;
                default: return AnimationSemantic.None;
            }
        }

        /// <summary>统计全项目仍未标记语义的 AnimationLine，帮助发现未被任何角色动画库接入的孤立资源。</summary>
        private static int CountMissingSemantics()
        {
            int missingCount = 0;
            string[] lineGuids = AssetDatabase.FindAssets("t:AnimationLine");
            for (int index = 0; index < lineGuids.Length; index++)
            {
                AnimationLine line = AssetDatabase.LoadAssetAtPath<AnimationLine>(AssetDatabase.GUIDToAssetPath(lineGuids[index]));
                if (line != null && line.Semantic == AnimationSemantic.None) missingCount++;
            }
            return missingCount;
        }
    }
}
#endif
