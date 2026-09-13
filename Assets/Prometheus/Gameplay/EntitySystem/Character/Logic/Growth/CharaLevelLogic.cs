using System;
using System.Collections.Generic;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Logic
{
    /// <summary>
    /// 管理角色等级与突破阶段，并用一个永久 Effect 把它们映射为三项基础属性增量与第二属性。
    ///
    /// 三项基础属性写入 **Base 通道**：它们必须与配置基础值相加后一起被攻击力%这类词条放大，
    /// 而 Offset 通道是在缩放之后才加算的。第二属性按其语义分别写入 Base、Boost 或 Offset。
    /// </summary>
    public sealed class CharaLevelLogic : Logic
    {
        /// <summary>保存当前角色独占的等级数据组件。</summary>
        private CharaLevelComponent levelComponent;
        /// <summary>保存等级与突破属性的永久 Effect 投影。</summary>
        private RuntimePermanentEffectProjection attributeProjection;
        /// <summary>标记等级或突破阶段变化后需要在安全更新边界重建 Effect。</summary>
        private bool projectionDirty;

        /// <summary>等级系统属于常驻玩法数据，不受控制状态和上下场状态暂停。</summary>
        public CharaLevelLogic()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>用已加载的配表初始化 Component，并立即建立符合启动状态的永久属性 Effect。</summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out levelComponent)) throw new InvalidOperationException("CharaLevelLogic requires CharaLevelComponent.");
            if (!Entity.TryGetComp(out EffectComponent effectComponent)) throw new InvalidOperationException("CharaLevelLogic requires EffectComponent.");
            // Entity 的 Logic 由运行时实例化，没有可注入的构造时机，因此按 ARCH-SYS-005 在使用点直接取 Kit。
            levelComponent.InitializeRuntimeData(Core.Config.Tables);
            levelComponent.AttributesChanged += OnAttributesChanged;
            attributeProjection = new RuntimePermanentEffectProjection(effectComponent.Runtime, Entity, "CharaLevel");
            RebuildAttributeProjection();
        }

        /// <summary>等级系统在 Entity 存活期间始终允许启用。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>等级系统只随 Entity 最终释放而禁用。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>全部初始化工作已在 AfterNew 完成。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>临时控制状态不会移除等级永久 Effect。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>只在等级或突破阶段实际变化后重建永久 Effect，纯经验变化不触碰战斗属性。</summary>
        public override void OnUpdate(float dt)
        {
            if (!projectionDirty) return;
            projectionDirty = false;
            RebuildAttributeProjection();
        }

        /// <summary>注销数据监听并移除永久 Effect，使其属性 Modifier 精确回滚。</summary>
        public override void OnDispose()
        {
            if (levelComponent != null) levelComponent.AttributesChanged -= OnAttributesChanged;
            attributeProjection?.Dispose();
            attributeProjection = null;
            levelComponent = null;
        }

        /// <summary>等级或突破阶段变化时标记投影脏；纯经验变化只由 ModifiableProperty 通知 UI。</summary>
        private void OnAttributesChanged()
        {
            projectionDirty = true;
        }

        /// <summary>把当前等级与突破阶段折算为三项基础属性增量与第二属性，替换旧的等级 Effect。</summary>
        private void RebuildAttributeProjection()
        {
            levelComponent.GetAttributeIncrease(out float hp, out float attack, out float defence);
            levelComponent.GetSecondaryAttribute(out string secondaryAttributeId, out float secondaryValue);

            List<EffectOperation> operations = new List<EffectOperation>(4)
            {
                new PropertyModifierOperation(PropertyType.MaxHp, PropertyModifierMode.Base, EffectValueFormula.Constant(hp)),
                new PropertyModifierOperation(PropertyType.Atk, PropertyModifierMode.Base, EffectValueFormula.Constant(attack)),
                new PropertyModifierOperation(PropertyType.Def, PropertyModifierMode.Base, EffectValueFormula.Constant(defence))
            };

            if (secondaryValue != 0f)
            {
                ResolveSecondaryAttribute(secondaryAttributeId, out PropertyType type, out PropertyModifierMode mode);
                operations.Add(new PropertyModifierOperation(type, mode, EffectValueFormula.Constant(secondaryValue)));
            }

            attributeProjection.Replace(operations.ToArray());
        }

        /// <summary>
        /// 把配表中的第二属性标识映射到属性位与通道。
        ///
        /// 覆盖 Docs/Design/Growth/01 第 3.3 节列出的全部第二属性类型。
        /// 标识来自配表，拼错不应当静默失效，因此未知标识直接抛出而不是跳过投影。
        /// </summary>
        private static void ResolveSecondaryAttribute(string attributeId, out PropertyType type, out PropertyModifierMode mode)
        {
            switch (attributeId)
            {
                case "CritRate": type = PropertyType.CritRate; mode = PropertyModifierMode.Offset; return;
                case "CritDamage": type = PropertyType.CritDmg; mode = PropertyModifierMode.Offset; return;
                case "AtkPercent": type = PropertyType.Atk; mode = PropertyModifierMode.Boost; return;
                case "HpPercent": type = PropertyType.MaxHp; mode = PropertyModifierMode.Boost; return;
                case "DefPercent": type = PropertyType.Def; mode = PropertyModifierMode.Boost; return;
                case "ElementalMastery": type = PropertyType.ElementalMastery; mode = PropertyModifierMode.Offset; return;
                case "EnergyRecharge": type = PropertyType.EnergyRecharge; mode = PropertyModifierMode.Offset; return;
                case "HealingBonus": type = PropertyType.HealingBonus; mode = PropertyModifierMode.Offset; return;
                case "PyroDamageBonus": type = PropertyType.PyroDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "HydroDamageBonus": type = PropertyType.HydroDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "ElectroDamageBonus": type = PropertyType.ElectroDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "CryoDamageBonus": type = PropertyType.CryoDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "DendroDamageBonus": type = PropertyType.DendroDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "AnemoDamageBonus": type = PropertyType.AnemoDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "GeoDamageBonus": type = PropertyType.GeoDamageBonus; mode = PropertyModifierMode.Offset; return;
                case "PhysicalDamageBonus": type = PropertyType.PhysicalDamageBonus; mode = PropertyModifierMode.Offset; return;
                default: throw new InvalidOperationException($"TbCharacterBase.ascensionSecondaryAttr '{attributeId}' is not a known attribute slot.");
            }
        }
    }
}
