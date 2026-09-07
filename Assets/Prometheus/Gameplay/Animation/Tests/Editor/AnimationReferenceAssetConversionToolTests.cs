#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Editor;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>验证 AnimationReferenceAsset 批量转换菜单的同目录输出、引用绑定和冲突预检。</summary>
    public sealed class AnimationReferenceAssetConversionToolTests
    {
        /// <summary>正式史莱姆死亡动画引用，仅作为测试复制源，不会在原目录生成资产。</summary>
        private const string FirstTemplatePath = "Assets/Art/火环spine合集1/Q版小人/敌人/Enemy/slime_dark_l/Models/ReferenceAssets/death.asset";
        /// <summary>正式史莱姆待机动画引用，仅作为测试复制源，不会在原目录生成资产。</summary>
        private const string SecondTemplatePath = "Assets/Art/火环spine合集1/Q版小人/敌人/Enemy/slime_dark_l/Models/ReferenceAssets/idle.asset";
        /// <summary>测试独占根目录，所有生成物都在 TearDown 中整体删除。</summary>
        private const string TestRootDirectory = "Assets/Temp/PrometheusAnimationReferenceConversionToolTests";
        /// <summary>模拟用户同目录多选时使用的源动画目录。</summary>
        private const string SourceDirectory = TestRootDirectory + "/ReferenceAssets";
        /// <summary>工具应在源动画目录下创建的唯一输出目录。</summary>
        private const string OutputDirectory = SourceDirectory + "/AnimationLines";
        /// <summary>Project 窗口右键菜单的完整路径。</summary>
        private const string MenuPath = "Assets/Prometheus/Convert to AnimationLine";

        /// <summary>保存测试前的 Project 选择，避免测试结束后改变编辑器上下文。</summary>
        private UnityEngine.Object[] previousSelection;
        /// <summary>测试目录中的第一个有效 AnimationReferenceAsset。</summary>
        private AnimationReferenceAsset firstReference;
        /// <summary>测试目录中的第二个有效 AnimationReferenceAsset。</summary>
        private AnimationReferenceAsset secondReference;

        /// <summary>复制两个正式 Spine 动画引用到独占测试目录，确保测试不会修改业务资源。</summary>
        [SetUp]
        public void SetUp()
        {
            previousSelection = Selection.objects;
            if (AssetDatabase.IsValidFolder(TestRootDirectory)) AssetDatabase.DeleteAsset(TestRootDirectory);
            EnsureFolder(SourceDirectory);
            Assert.That(AssetDatabase.CopyAsset(FirstTemplatePath, SourceDirectory + "/death.asset"), Is.True, "无法复制第一个 AnimationReferenceAsset 测试模板。");
            Assert.That(AssetDatabase.CopyAsset(SecondTemplatePath, SourceDirectory + "/idle.asset"), Is.True, "无法复制第二个 AnimationReferenceAsset 测试模板。");
            AssetDatabase.Refresh();
            firstReference = AssetDatabase.LoadAssetAtPath<AnimationReferenceAsset>(SourceDirectory + "/death.asset");
            secondReference = AssetDatabase.LoadAssetAtPath<AnimationReferenceAsset>(SourceDirectory + "/idle.asset");
            Assert.That(firstReference, Is.Not.Null);
            Assert.That(secondReference, Is.Not.Null);
        }

        /// <summary>恢复 Project 选择并删除测试独占目录，避免生成资源进入工作区。</summary>
        [TearDown]
        public void TearDown()
        {
            Selection.objects = previousSelection;
            previousSelection = null;
            firstReference = null;
            secondReference = null;
            if (AssetDatabase.IsValidFolder(TestRootDirectory)) AssetDatabase.DeleteAsset(TestRootDirectory);
            AssetDatabase.Refresh();
        }

        /// <summary>验证同目录多选会生成一个 AnimationLines 目录，并让每个 AnimationLine 指向对应源动画。</summary>
        [Test]
        public void ConvertSelectedAnimationReferences_CreatesBoundLinesInSingleOutputDirectory()
        {
            Selection.objects = new UnityEngine.Object[] { firstReference, secondReference };
            Assert.That(EditorApplication.ExecuteMenuItem(MenuPath), Is.True, "Unity 未找到 AnimationReferenceAsset 转换菜单。");
            AnimationLine deathLine = AssetDatabase.LoadAssetAtPath<AnimationLine>(OutputDirectory + "/death.asset");
            AnimationLine idleLine = AssetDatabase.LoadAssetAtPath<AnimationLine>(OutputDirectory + "/idle.asset");
            Assert.That(AssetDatabase.IsValidFolder(OutputDirectory), Is.True);
            Assert.That(deathLine, Is.Not.Null);
            Assert.That(idleLine, Is.Not.Null);
            Assert.That(deathLine.AnimationReferenceAsset, Is.SameAs(firstReference));
            Assert.That(idleLine.AnimationReferenceAsset, Is.SameAs(secondReference));
            Assert.That(Selection.objects, Is.EquivalentTo(new UnityEngine.Object[] { deathLine, idleLine }));
        }

        /// <summary>验证任一目标路径已存在时完整计划会失败，并保持已有 AnimationLine 不变。</summary>
        [Test]
        public void TryBuildConversionPlan_RejectsEntireBatchWhenDestinationExists()
        {
            EnsureFolder(OutputDirectory);
            AnimationLine existingLine = ScriptableObject.CreateInstance<AnimationLine>();
            AssetDatabase.CreateAsset(existingLine, OutputDirectory + "/death.asset");
            Assert.That(TryBuildConversionPlan(new List<AnimationReferenceAsset> { firstReference, secondReference }, out string error), Is.False);
            Assert.That(error, Does.Contain(OutputDirectory + "/death.asset"));
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationLine>(OutputDirectory + "/death.asset"), Is.SameAs(existingLine));
            Assert.That(AssetDatabase.AssetPathExists(OutputDirectory + "/idle.asset"), Is.False);
        }

        /// <summary>验证跨目录多选不会产生多个隐式输出目录。</summary>
        [Test]
        public void TryBuildConversionPlan_RejectsReferencesFromDifferentDirectories()
        {
            string otherDirectory = TestRootDirectory + "/OtherReferenceAssets";
            EnsureFolder(otherDirectory);
            string otherReferencePath = otherDirectory + "/idle.asset";
            Assert.That(AssetDatabase.CopyAsset(SecondTemplatePath, otherReferencePath), Is.True);
            AnimationReferenceAsset otherReference = AssetDatabase.LoadAssetAtPath<AnimationReferenceAsset>(otherReferencePath);
            Assert.That(TryBuildConversionPlan(new List<AnimationReferenceAsset> { firstReference, otherReference }, out string error), Is.False);
            Assert.That(error, Does.Contain("同一个目录"));
            Assert.That(AssetDatabase.IsValidFolder(OutputDirectory), Is.False);
        }

        /// <summary>通过反射调用工具的无副作用预检入口，使测试不触发模态错误对话框。</summary>
        private static bool TryBuildConversionPlan(IReadOnlyList<AnimationReferenceAsset> references, out string error)
        {
            MethodInfo method = typeof(AnimationReferenceAssetConversionTool).GetMethod("TryBuildConversionPlan", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { references, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            error = arguments[2] as string;
            return result;
        }

        /// <summary>逐级创建指定 Assets 相对目录，不删除或重建已经存在的父目录。</summary>
        private static void EnsureFolder(string folderPath)
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
#endif
