using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Elements
{
    /// <summary>
    /// 元素附着与反应的实现。
    ///
    /// 按 Docs/Design/Combat/04 第 8 节的七步流程结算：
    /// ICD 门控 → 收集候选附着 → 按优先级取唯一反应 → 消耗附着 → 写入触发元素 → 清理归零 → 返回结果。
    ///
    /// 它不读取任何属性面板，也不结算伤害：反应的数值求值交给 `ReactionFormula` 与 `DamageCalculator`
    /// 这两个纯函数，反应产物的施加交给伤害管线。这样元素规则与数值公式各自独立可测。
    /// </summary>
    internal sealed class ElementSystem : XSystem, IElementSystem
    {
        /// <summary>按目标实体编号保存附着集合。</summary>
        private readonly Dictionary<int, ElementAuraSet> auraRegistry = new Dictionary<int, ElementAuraSet>();
        /// <summary>保存全部 ICD 窗口。</summary>
        private readonly IcdRegistry icdRegistry = new IcdRegistry();

        /// <summary>保存反应矩阵索引；在 AfterNew 建立，因为配表在会话建立前已由 ConfigKit 加载。</summary>
        private ReactionMatrixIndex reactionMatrix;
        /// <summary>保存按档位查出的附着参数，避免每次施加都查表。</summary>
        private readonly Dictionary<Cfg.GaugeStrength, Cfg.GaugeStrengthRow> gaugeByStrength = new Dictionary<Cfg.GaugeStrength, Cfg.GaugeStrengthRow>();
        /// <summary>保存默认 ICD 组参数。</summary>
        private Cfg.IcdGroupRow defaultIcd;
        /// <summary>保存伪元素状态的时长参数，避免每次触发都查表。</summary>
        private readonly Dictionary<Cfg.AuraKey, Cfg.PseudoElementStateRow> stateByAura = new Dictionary<Cfg.AuraKey, Cfg.PseudoElementStateRow>();

        /// <summary>累计会话时间，作为 ICD 窗口的绝对时钟；与 Unity 时钟解耦以便测试驱动。</summary>
        private float elapsedSeconds;

        /// <summary>复用的到期状态缓冲；每帧收集自然到期的伪元素，随后逐条广播。</summary>
        private readonly List<ElementAura> expiredStates = new List<ElementAura>();
        /// <summary>复用的实体编号缓冲；衰减要先取键快照，字典不允许在遍历中被修改。</summary>
        private readonly List<int> targetBuffer = new List<int>();

        /// <inheritdoc />
        public event Action<PseudoStateChangedEvent> PseudoStateChanged;

        /// <summary>默认 ICD 组在配表中的标识。</summary>
        private const string DefaultIcdGroupId = "Default";

        /// <summary>建立反应矩阵索引与配表缓存。</summary>
        public override void AfterNew()
        {
            Cfg.Tables tables = Core.Config.Tables;
            reactionMatrix = new ReactionMatrixIndex(tables);
            foreach (Cfg.GaugeStrengthRow row in tables.TbGaugeStrength.DataList) gaugeByStrength[row.Strength] = row;
            foreach (Cfg.PseudoElementStateRow row in tables.TbPseudoElementState.DataList) stateByAura[row.AuraKey] = row;
            defaultIcd = tables.TbIcdGroup.Get(DefaultIcdGroupId);
        }

        /// <summary>
        /// 推进全部附着的衰减与 ICD 时钟，并广播自然到期的伪元素状态。
        ///
        /// 广播放在全部目标衰减完之后：监听方可能会修改附着（例如草原核引爆继续施加草元素），
        /// 在遍历过程中广播会让字典在枚举中被修改。
        /// </summary>
        public override void OnUpdate(float dt)
        {
            if (dt <= 0f) return;
            elapsedSeconds += dt;

            expiredStates.Clear();
            targetBuffer.Clear();
            foreach (int entityId in auraRegistry.Keys) targetBuffer.Add(entityId);
            for (int index = 0; index < targetBuffer.Count; index++)
            {
                int entityId = targetBuffer[index];
                int before = expiredStates.Count;
                auraRegistry[entityId].Decay(dt, expiredStates);
                // Decay 只知道附着本身，不知道它挂在谁身上，因此在这里补上目标编号。
                for (int added = before; added < expiredStates.Count; added++) expiredTargets.Add(entityId);
            }

            for (int index = 0; index < expiredStates.Count; index++)
            {
                ElementAura state = expiredStates[index];
                RaiseStateChanged(expiredTargets[index], state.SourceEntityId, state.Key, PseudoStateChange.Expired, stateReactionByKey.TryGetValue(state.Key, out Cfg.ReactionMatrixRow row) ? row : null);
            }
            expiredTargets.Clear();
        }

        /// <summary>与 expiredStates 一一对应的目标编号。</summary>
        private readonly List<int> expiredTargets = new List<int>();

        /// <summary>记住每个伪元素状态最后一次由哪一行反应写入，供状态消失时回溯。</summary>
        private readonly Dictionary<Cfg.AuraKey, Cfg.ReactionMatrixRow> stateReactionByKey = new Dictionary<Cfg.AuraKey, Cfg.ReactionMatrixRow>();

        /// <summary>广播一次伪元素状态变化。</summary>
        private void RaiseStateChanged(int targetEntityId, int sourceEntityId, Cfg.AuraKey key, PseudoStateChange change, Cfg.ReactionMatrixRow reaction)
        {
            PseudoStateChanged?.Invoke(new PseudoStateChangedEvent(targetEntityId, sourceEntityId, key, change, reaction));
        }

        /// <summary>施加一次元素附着并返回本次触发的反应。</summary>
        public ReactionResult Apply(in ElementApplyRequest request)
        {
            // ① ICD 门控：被挡下时不改变任何状态，也不触发反应。
            if (!PassesIcd(in request)) return ReactionResult.None;

            ElementAuraSet auras = GetOrCreateAuras(request.TargetEntityId);

            // ②③④ 在目标当前全部附着中按优先级取出唯一一条反应。
            bool reacted = reactionMatrix.TryResolve(request.Element, auras, out Cfg.ReactionMatrixRow row);

            float appliedGauge = ResolveAppliedGauge(request.Strength, out float duration);

            if (!reacted)
            {
                // 无反应：写入或刷新触发元素自己的附着。风与岩不留存附着，因此不写入。
                if (appliedGauge > 0f && LeavesAura(request.Element)) auras.Apply(ToAuraKey(request.Element), appliedGauge, duration);
                return ReactionResult.None;
            }

            // ⑤ 按命中行结算附着消耗与触发元素的去留。
            // 写入伪元素状态的反应（冻结、绽放、原激化）有各自的消耗规则，走独立分支。
            float consumed = row.ProducedAura != Cfg.AuraKey.None
                ? ApplyPseudoState(auras, row, appliedGauge, duration, in request)
                : ConsumeForReaction(auras, row, appliedGauge, duration, in request);

            return new ReactionResult(row, true, consumed);
        }

        /// <summary>
        /// 结算一条普通反应的附着消耗与触发元素去留。
        ///
        /// 伪元素被命中时消耗是**全有全无**的：状态条存的是剩余秒数而不是附着量，
        /// 按比例扣秒数没有意义。碎冰、超绽放与烈绽放的消耗系数为 1，直接解除状态；
        /// 超激化与蔓激化的系数为 0，原激化状态保留。
        /// </summary>
        private float ConsumeForReaction(ElementAuraSet auras, Cfg.ReactionMatrixRow row, float appliedGauge, float duration, in ElementApplyRequest request)
        {
            float consumed;
            if (PseudoElement.IsPseudo(row.AuraElement))
            {
                consumed = 0f;
                if (row.AuraConsumeRatio > 0f && auras.Remove(row.AuraElement, out ElementAura state))
                {
                    consumed = state.Gauge;
                    RaiseStateChanged(request.TargetEntityId, state.SourceEntityId, state.Key, PseudoStateChange.Consumed, stateReactionByKey.TryGetValue(state.Key, out Cfg.ReactionMatrixRow origin) ? origin : null);
                }
            }
            else
            {
                consumed = auras.Consume(row.AuraElement, appliedGauge * row.AuraConsumeRatio);
            }

            if (!row.TriggerConsumed && appliedGauge > 0f && LeavesAura(request.Element)) auras.Apply(ToAuraKey(request.Element), appliedGauge, duration);
            return consumed;
        }

        /// <summary>
        /// 写入伪元素状态，并按状态类型结算双方附着。
        ///
        /// 三者的附着规则并不相同，因此不能共用一条路径：
        /// - **冻结**把水与冰中较小的一份合并成冻结状态，两侧都扣掉这一份；
        /// - **绽放**与**原激化**按 04 第 5.4 节「不完全消耗双方附着」，两侧附着都保留。
        ///
        /// 「哪一行写入哪个状态」与「状态时长怎么算」都来自配表，代码里没有按反应标识写死的分支。
        /// </summary>
        private float ApplyPseudoState(ElementAuraSet auras, Cfg.ReactionMatrixRow row, float appliedGauge, float duration, in ElementApplyRequest request)
        {
            if (!stateByAura.TryGetValue(row.ProducedAura, out Cfg.PseudoElementStateRow state))
                throw new InvalidOperationException($"TbPseudoElementState has no row for aura '{row.ProducedAura}'; reaction '{row.ReactionId}' cannot resolve its state duration.");

            // 触发时双方的附着量：一侧是目标身上已有的附着，另一侧是本次命中写入的量。
            float existingGauge = auras.TryGet(row.AuraElement, out ElementAura existing) ? existing.Gauge : 0f;
            float pairedGauge = existingGauge < appliedGauge ? existingGauge : appliedGauge;

            float consumed = 0f;
            if (state.DurationMode == Cfg.PseudoStateDurationMode.FreezeFormula)
            {
                // 冻结把两侧较小的那份附着合并进状态，因此两侧都要扣掉它。
                consumed = auras.Consume(row.AuraElement, pairedGauge);
                appliedGauge -= consumed;
            }

            float stateDuration = ResolveStateDuration(state, pairedGauge);
            if (stateDuration > 0f)
            {
                stateReactionByKey[row.ProducedAura] = row;
                // 只有首次写入才广播：刷新既有状态不应让产物 Effect 被撤下再重加。
                if (auras.ApplyState(row.ProducedAura, stateDuration, request.SourceEntityId))
                    RaiseStateChanged(request.TargetEntityId, request.SourceEntityId, row.ProducedAura, PseudoStateChange.Applied, row);
            }

            // 触发元素的剩余部分照常留存；冻结时这里写入的是扣除合并量之后的余量。
            if (!row.TriggerConsumed && appliedGauge > 0f && LeavesAura(request.Element)) auras.Apply(ToAuraKey(request.Element), appliedGauge, duration);
            return consumed;
        }

        /// <summary>
        /// 按配表的求法算出伪元素状态的持续时间。
        /// </summary>
        /// <param name="state">状态参数行。</param>
        /// <param name="pairedGauge">触发时双方附着量中较小的一份。</param>
        private static float ResolveStateDuration(Cfg.PseudoElementStateRow state, float pairedGauge)
        {
            switch (state.DurationMode)
            {
                case Cfg.PseudoStateDurationMode.Fixed:
                    return state.MinDurationSeconds;

                case Cfg.PseudoStateDurationMode.GaugeLerp:
                    // 附着量越高状态越久；配表给出取到最长时长所需的附着量。
                    if (state.ReferenceGauge <= 0f) return state.MinDurationSeconds;
                    float ratio = pairedGauge / state.ReferenceGauge;
                    if (ratio < 0f) ratio = 0f;
                    else if (ratio > 1f) ratio = 1f;
                    return state.MinDurationSeconds + (state.MaxDurationSeconds - state.MinDurationSeconds) * ratio;

                case Cfg.PseudoStateDurationMode.FreezeFormula:
                    // 04 第 5.6 节：冻结时间 = 2√(5g + 4) − 4，g 为冻结附着量。
                    // g 为 0 时结果为 0，因此没有附着可合并时不会写入一个零时长状态。
                    return 2f * (float)Math.Sqrt(5f * pairedGauge + 4f) - 4f;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state.DurationMode, "Unsupported pseudo element duration mode.");
            }
        }

        /// <summary>只读查询目标当前附着。</summary>
        public ElementAuraSnapshot QueryAura(int entityId)
        {
            return new ElementAuraSnapshot(auraRegistry.TryGetValue(entityId, out ElementAuraSet auras) ? auras : null);
        }

        /// <summary>清除目标的全部附着。</summary>
        public void ClearAura(int entityId)
        {
            if (auraRegistry.TryGetValue(entityId, out ElementAuraSet auras))
            {
                // 逐条广播被清掉的状态：实体死亡或回收时产物 Effect 必须跟着撤下，
                // 且按「被消耗」而不是「到期」处理——清场不应引爆草原核。
                expiredStates.Clear();
                foreach (KeyValuePair<Cfg.AuraKey, ElementAura> entry in auras.Auras)
                    if (entry.Value.IsState) expiredStates.Add(entry.Value);
                auras.Clear();
                for (int index = 0; index < expiredStates.Count; index++)
                {
                    ElementAura state = expiredStates[index];
                    RaiseStateChanged(entityId, state.SourceEntityId, state.Key, PseudoStateChange.Consumed, stateReactionByKey.TryGetValue(state.Key, out Cfg.ReactionMatrixRow row) ? row : null);
                }
                expiredStates.Clear();
            }
            icdRegistry.RemoveSource(entityId);
        }

        /// <summary>释放全部附着与 ICD 状态。</summary>
        public override void Dispose()
        {
            auraRegistry.Clear();
            icdRegistry.Clear();
            gaugeByStrength.Clear();
            stateByAura.Clear();
            stateReactionByKey.Clear();
            expiredStates.Clear();
            expiredTargets.Clear();
            PseudoStateChanged = null;
            reactionMatrix = null;
        }

        /// <summary>按请求的策略判定本次命中是否允许写入附着。</summary>
        private bool PassesIcd(in ElementApplyRequest request)
        {
            if (request.IcdPolicy == Cfg.IcdPolicy.None) return true;
            int groupId = request.IcdPolicy == Cfg.IcdPolicy.Independent ? request.IcdGroupId : 0;
            IcdKey key = new IcdKey(request.SourceEntityId, request.TalentId, groupId);
            return icdRegistry.RegisterHit(in key, elapsedSeconds, defaultIcd.WindowSeconds, defaultIcd.HitCount);
        }

        /// <summary>按档位求本次实际写入量与该档位的总持续时间。</summary>
        private float ResolveAppliedGauge(Cfg.GaugeStrength strength, out float durationSeconds)
        {
            if (strength == Cfg.GaugeStrength.None)
            {
                durationSeconds = 0f;
                return 0f;
            }
            if (!gaugeByStrength.TryGetValue(strength, out Cfg.GaugeStrengthRow row))
                throw new InvalidOperationException($"TbGaugeStrength has no row for strength '{strength}'.");
            durationSeconds = row.DurationSeconds;
            return row.NominalGauge * row.DecayTax;
        }

        /// <summary>取出或建立目标的附着集合。</summary>
        private ElementAuraSet GetOrCreateAuras(int entityId)
        {
            if (auraRegistry.TryGetValue(entityId, out ElementAuraSet auras)) return auras;
            auras = new ElementAuraSet();
            auraRegistry.Add(entityId, auras);
            return auras;
        }

        /// <summary>风与岩只作为反应触发方，自身不在目标身上留存附着。</summary>
        private static bool LeavesAura(Cfg.ElementType element)
        {
            return element != Cfg.ElementType.Anemo
                   && element != Cfg.ElementType.Geo
                   && element != Cfg.ElementType.Physical
                   && element != Cfg.ElementType.None;
        }

        /// <summary>把触发元素转换为附着键；两个枚举的前九个成员一一对应。</summary>
        private static Cfg.AuraKey ToAuraKey(Cfg.ElementType element)
        {
            switch (element)
            {
                case Cfg.ElementType.Pyro: return Cfg.AuraKey.Pyro;
                case Cfg.ElementType.Hydro: return Cfg.AuraKey.Hydro;
                case Cfg.ElementType.Electro: return Cfg.AuraKey.Electro;
                case Cfg.ElementType.Cryo: return Cfg.AuraKey.Cryo;
                case Cfg.ElementType.Dendro: return Cfg.AuraKey.Dendro;
                case Cfg.ElementType.Anemo: return Cfg.AuraKey.Anemo;
                case Cfg.ElementType.Geo: return Cfg.AuraKey.Geo;
                case Cfg.ElementType.Physical: return Cfg.AuraKey.Physical;
                default: throw new ArgumentOutOfRangeException(nameof(element), element, "Element has no corresponding aura key.");
            }
        }
    }
}
