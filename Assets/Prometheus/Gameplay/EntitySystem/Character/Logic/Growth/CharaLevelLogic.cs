using System;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Logic
{
    /// <summary>管理角色等级经验，并用一个永久 Effect 把当前等级映射为攻击力固定提升。</summary>
    public sealed class CharaLevelLogic : Logic
    {
        /// <summary>保存当前角色独占的等级数据组件。</summary>
        private CharaLevelComponent levelComponent;
        /// <summary>保存等级攻击力永久 Effect 投影。</summary>
        private RuntimePermanentEffectProjection attackProjection;
        /// <summary>标记等级变化后需要在安全更新边界重建 Effect。</summary>
        private bool projectionDirty;

        /// <summary>等级系统属于常驻玩法数据，不受控制状态和上下场状态暂停。</summary>
        public CharaLevelLogic()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>初始化 Component 运行时副本，并立即建立符合 Debug 等级的永久攻击力 Effect。</summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out levelComponent)) throw new InvalidOperationException("CharaLevelLogic requires CharaLevelComponent.");
            if (!Entity.TryGetComp(out EffectComponent effectComponent)) throw new InvalidOperationException("CharaLevelLogic requires EffectComponent.");
            levelComponent.InitializeRuntimeData();
            levelComponent.Changed += OnLevelDataChanged;
            attackProjection = new RuntimePermanentEffectProjection(effectComponent.Runtime, Entity, "CharaLevel");
            RebuildAttackProjection();
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

        /// <summary>只在等级实际变化后重建永久 Effect，纯经验变化不触碰战斗属性。</summary>
        public override void OnUpdate(float dt)
        {
            if (!projectionDirty) return;
            projectionDirty = false;
            RebuildAttackProjection();
        }

        /// <summary>注销数据监听并移除等级永久 Effect，使其攻击力 Modifier 精确回滚。</summary>
        public override void OnDispose()
        {
            if (levelComponent != null) levelComponent.Changed -= OnLevelDataChanged;
            attackProjection?.Dispose();
            attackProjection = null;
            levelComponent = null;
        }

        /// <summary>等级变化时标记投影脏；纯经验变化只由 ModifiableProperty 通知 UI。</summary>
        private void OnLevelDataChanged(bool levelChanged)
        {
            if (levelChanged) projectionDirty = true;
        }

        /// <summary>按 Spec 公式创建唯一攻击力 Offset 操作并替换旧等级 Effect。</summary>
        private void RebuildAttackProjection()
        {
            EffectOperation[] operations = { new PropertyModifierOperation(PropertyType.Atk, PropertyModifierMode.Offset, EffectValueFormula.Constant(levelComponent.GetCurrentAttackIncrease())) };
            attackProjection.Replace(operations);
        }
    }
}
