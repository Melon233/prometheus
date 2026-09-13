#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Effects.Editor
{
    /// <summary>
    /// 生成三个伪元素状态的产物 Effect 资产，并把它们挂进正式效果库。
    ///
    /// 工具放在编辑器测试程序集，是因为写入 `EffectDefinition` 私有序列化字段的反射入口
    /// （`ConfigureForTests`）只在这里可见——正式运行时定义保持只读，不为了编写资产扩大公开 API。
    ///
    /// 三个产物一律是 `Permanent`：它们的存活时间由 `IElementSystem` 的伪元素状态决定，
    /// 由 `ReactionProductSystem` 在状态消失时撤下。在资产上再写一份时长会造成两个时钟。
    /// </summary>
    public static class ReactionProductAssetCreator
    {
        private const string RootFolder = "Assets/BundleResources/Config/Effect";
        private const string EffectDefinitionsFolder = RootFolder + "/EffectDefinitions";
        private const string LibraryPath = RootFolder + "/EffectLibrary.asset";
        private const string FrozenPath = EffectDefinitionsFolder + "/Eff_Frozen.asset";
        private const string QuickenPath = EffectDefinitionsFolder + "/Eff_Quicken.asset";
        private const string DendroCorePath = EffectDefinitionsFolder + "/Eff_Bloom_Core.asset";
        private const string CrystallizePath = EffectDefinitionsFolder + "/Eff_Crystallize.asset";

        /// <summary>创建或更新三个反应产物资产，并写进效果库。</summary>
        [MenuItem("Prometheus/Effect System/Create Or Update Reaction Product Assets")]
        public static void CreateOrUpdate()
        {
            EffectDefinition frozen = LoadOrCreate(FrozenPath);
            EffectDefinition quickened = LoadOrCreate(QuickenPath);
            EffectDefinition dendroCore = LoadOrCreate(DendroCorePath);
            EffectDefinition crystallize = LoadOrCreate(CrystallizePath);

            // 冻结禁止目标的移动、普通行为与主动技能，即 ControlState.Stun 的语义。
            // 时长不写在这里：它由 04 第 5.6 节的闭式解逐次算出，只有元素系统知道。
            frozen.ConfigureForTests("Eff_Frozen", EffectTag.Debuff | EffectTag.Control, EffectDurationType.Permanent,
                0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0,
                new EffectOperation[] { new ControlStateModifierOperation(Xuan.Prometheus.Component.ControlState.Stun) }, null, null, null);

            // 原激化只是一层标记：超激化与蔓激化的数值全部来自反应矩阵，产物不承载任何数值。
            quickened.ConfigureForTests("Eff_Quicken", EffectTag.Debuff, EffectDurationType.Permanent,
                0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0,
                Array.Empty<EffectOperation>(), null, null, null);

            // 草原核同样只是标记：引爆伤害由 ReactionProductSystem 在状态到期时按剧变公式结算，
            // 因为「被超绽放消耗」与「自然到期引爆」必须区分，而 Effect 的移除原因表达不了这个差别。
            dendroCore.ConfigureForTests("Eff_Bloom_Core", EffectTag.Debuff, EffectDurationType.Permanent,
                0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0,
                Array.Empty<EffectOperation>(), null, null, null);

            // 结晶护盾：时长 15 秒（04 第 5.6 节），吸收量与元素都随信号传入，
            // 因此四种结晶共用这一份资产。重复触发刷新时长并重算护盾，符合「同时只能存在一个」。
            crystallize.ConfigureForTests("Eff_Crystallize", EffectTag.Buff, EffectDurationType.Duration,
                CrystallizeDurationSeconds, 0f, EffectStackPolicy.RefreshDuration, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0,
                new EffectOperation[] { BuildCrystallizeShield() }, null, null, null,
                refreshOperations: new EffectOperation[] { BuildCrystallizeShield() });

            EffectLibrary library = AssetDatabase.LoadAssetAtPath<EffectLibrary>(LibraryPath);
            if (library == null) throw new InvalidOperationException($"Effect library is missing at {LibraryPath}; run 'Create Or Update Example Assets' first.");
            SetPrivateField(library, "reactionProducts", new[] { frozen, quickened, dendroCore, crystallize });

            EditorUtility.SetDirty(frozen);
            EditorUtility.SetDirty(quickened);
            EditorUtility.SetDirty(dendroCore);
            EditorUtility.SetDirty(crystallize);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Reaction product effects are ready at {EffectDefinitionsFolder}.", library);
        }

        /// <summary>结晶护盾的存在时长，来自 04 第 5.6 节。</summary>
        private const float CrystallizeDurationSeconds = 15f;

        /// <summary>
        /// 构造结晶护盾操作。吸收量取信号数值（由反应算好），元素同样取自信号，
        /// 互斥组固定为 Crystallize，从而表达「同时只能存在一个」。
        /// </summary>
        private static ShieldOperation BuildCrystallizeShield()
        {
            return new ShieldOperation(EffectValueFormula.SignalValue(), ShieldElementSource.InheritSignal, Cfg.ElementType.None, "Crystallize");
        }

        /// <summary>加载已有资产；不存在时创建并落盘。</summary>
        private static EffectDefinition LoadOrCreate(string path)
        {
            EffectDefinition asset = AssetDatabase.LoadAssetAtPath<EffectDefinition>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<EffectDefinition>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>写入效果库的私有序列化字段，与示例资产生成器使用同一手法。</summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException($"EffectLibrary has no serialized field '{fieldName}'.");
            field.SetValue(target, value);
        }
    }
}
#endif
