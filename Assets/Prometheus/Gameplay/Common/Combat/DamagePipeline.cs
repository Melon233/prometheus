using Xuan.Prometheus.Component;
using Xuan.Prometheus.Elements;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Combat
{
    /// <summary>
    /// 一次伤害结算的全部输入，由接线层从实体与动作上下文填好后交给管线。
    /// </summary>
    public struct DamageRequest
    {
        /// <summary>施加者属性面板；决定增伤、暴击、精通与无视防御。</summary>
        public PropertyComponent Attacker;
        /// <summary>目标属性面板；决定抗性、减防与减伤。</summary>
        public PropertyComponent Target;

        /// <summary>施加者实体编号；与天赋一起构成 ICD 分组。</summary>
        public int AttackerEntityId;
        /// <summary>目标实体编号。</summary>
        public int TargetEntityId;

        /// <summary>施加者等级；参与防御区与反应等级系数。</summary>
        public int AttackerLevel;
        /// <summary>目标等级；参与防御区。</summary>
        public int TargetLevel;

        /// <summary>本次伤害的动作类别；决定基础元素与动作加成位。</summary>
        public DamageActionType ActionType;
        /// <summary>本次伤害的最终元素，已由附魔解析完毕。</summary>
        public Cfg.ElementType Element;

        /// <summary>
        /// 已解析的基础伤害 `倍率 × 缩放属性`。
        ///
        /// `EffectValueFormula` 本来就把倍率与缩放属性写在同一个表达式里（`CasterAttack(2.4f)` 即攻击力 × 2.4），
        /// 因此管线拿到的是两者的乘积，而不是分开的两项。
        /// </summary>
        public float BaseDamage;
        /// <summary>不随面板缩放的固定伤害值。</summary>
        public float FlatDamage;

        /// <summary>本次攻击携带的附着强度档位。</summary>
        public Cfg.GaugeStrength GaugeStrength;
        /// <summary>ICD 策略。</summary>
        public Cfg.IcdPolicy IcdPolicy;
        /// <summary>同一天赋内的独立 ICD 组号。</summary>
        public int IcdGroupId;
        /// <summary>天赋标识；同一角色的普攻与战技必须不同，否则共用一条 ICD。</summary>
        public string TalentId;

        /// <summary>
        /// 暴击掷骰结果，取值 0 到 1。
        ///
        /// 由调用方掷骰而不是管线内部掷：管线因此完全确定，测试可以精确指定暴击与否，
        /// 也避免同一次攻击命中多个目标时每个目标各掷一次这类隐蔽的行为差异。
        /// </summary>
        public float CriticalRoll;
    }

    /// <summary>
    /// 一次伤害结算的结果。
    ///
    /// 主伤害与剧变伤害是**两笔独立的伤害**：剧变不吃攻击力、不吃增伤、不能暴击、不吃防御区，
    /// 因此不能合并成一个数字，必须由调用方分别落地、分别飘字、分别发信号。
    /// </summary>
    public struct DamageResolution
    {
        /// <summary>主公式结算出的伤害。</summary>
        public float Damage;
        /// <summary>本次伤害的最终元素。</summary>
        public Cfg.ElementType Element;
        /// <summary>本次是否暴击。</summary>
        public bool IsCritical;

        /// <summary>命中的反应行；未触发反应时为空。</summary>
        public Cfg.ReactionMatrixRow Reaction;
        /// <summary>剧变反应产生的独立伤害；非剧变为 0。</summary>
        public float TransformativeDamage;
        /// <summary>
        /// 产物 Effect 的数值载荷；没有数值产物时为 0。
        ///
        /// 目前唯一的使用者是结晶护盾：反应算出吸收量，由 `ProductEffectId` 指向的 Effect 承载它的时长与元素。
        /// 泛化成一个数字而不是叫「护盾量」，是因为这一列的语义由配表行决定，管线不应替产物命名。
        /// </summary>
        public float ProductValue;
        /// <summary>产物的元素；结晶护盾用它决定护盾的元素亲和。无元素产物时为 `None`。</summary>
        public Cfg.ElementType ProductElement;
        /// <summary>剧变伤害所属元素，用于目标抗性与表现；非剧变为 `None`。</summary>
        public Cfg.ElementType TransformativeElement;
        /// <summary>
        /// 命中行配置的反应产物 Effect 标识；没有产物时为空。
        ///
        /// 结晶护盾、冻结、草原核与原激化都不是「一个数字」，而是各有生命周期的实体或状态，
        /// 因此配表用 `effectId` 承载它们，由调用方施加对应 Effect，而不是在这里算出一个值。
        /// </summary>
        public string ProductEffectId;

        /// <summary>
        /// 获取本次结算实际喂给 `DamageCalculator` 的全部输入。
        ///
        /// 保留它是为了让伤害拆解能用**同一批公开函数**复算每个乘区，
        /// 而不是在诊断代码里另写一遍算式——那样的拆解会在公式调整后悄悄说谎。
        /// 结构体按值复制，不产生分配。
        /// </summary>
        public DamageContext Context;

        /// <summary>获取本次是否触发了元素反应。</summary>
        public bool Reacted => Reaction != null;
    }

    /// <summary>
    /// 把一次攻击从「倍率 × 属性」推到「目标实际承受的数字」的唯一路径。
    ///
    /// 职责边界：
    /// - `IElementSystem` 判定**附着与反应身份**，不碰任何数值；
    /// - `ReactionFormula` / `DamageCalculator` 做**纯算术**，不认识实体与配表；
    /// - 本类负责**取数与选槽**：从属性面板读出各乘区的输入，按元素选对属性位，串起前两者。
    ///
    /// 三者都不写目标生命值——落地由调用方完成，因此管线本身没有副作用，可以整条链路单测。
    /// </summary>
    public static class DamagePipeline
    {
        /// <summary>
        /// 缺少等级来源时使用的等级。
        ///
        /// 敌人目前没有等级组件，敌人等级表属于 Combat 排期第 7 步；在那之前敌我都按 1 级参与防御区，
        /// 使防御区退化为一个与双方等级无关的常数，不会悄悄给出错误的等级压制。
        /// </summary>
        public const int DefaultLevel = 1;

        /// <summary>
        /// 结算一次伤害。
        ///
        /// 顺序固定：先施加元素并取回反应身份，再按反应类别求增幅/激化/剧变，最后走主公式。
        /// 附着必须发生在求值之前——蒸发要用的正是本次命中所触发的反应。
        /// </summary>
        public static DamageResolution Resolve(in DamageRequest request, IElementSystem elementSystem)
        {
            ReactionResult reaction = elementSystem.Apply(new ElementApplyRequest
            {
                SourceEntityId = request.AttackerEntityId,
                TargetEntityId = request.TargetEntityId,
                TalentId = request.TalentId,
                IcdGroupId = request.IcdGroupId,
                IcdPolicy = request.IcdPolicy,
                Element = request.Element,
                Strength = request.GaugeStrength
            });

            DamageResolution resolution = new DamageResolution
            {
                Element = request.Element,
                Reaction = reaction.Row,
                TransformativeElement = Cfg.ElementType.None,
                ProductEffectId = reaction.Triggered ? reaction.Row.EffectId : null,
                ProductElement = Cfg.ElementType.None
            };

            float amplifyMultiplier = 1f;
            float catalyzeBonus = 0f;
            if (reaction.Triggered) ResolveReaction(in request, reaction.Row, ref resolution, ref amplifyMultiplier, ref catalyzeBonus);

            float critRate = Read(request.Attacker, PropertyType.CritRate);
            resolution.IsCritical = request.CriticalRoll < critRate;

            DamageContext context = new DamageContext
            {
                AttackerLevel = request.AttackerLevel,
                TargetLevel = request.TargetLevel,
                // 倍率与缩放属性已经在 BaseDamage 里相乘完毕，这里只需把倍率位置留作 1。
                SkillMultiplier = 1f,
                ScalingValue = request.BaseDamage,
                FlatDamage = request.FlatDamage,
                CatalyzeBonus = catalyzeBonus,
                DamageBonus = ResolveDamageBonus(in request),
                IsCritical = resolution.IsCritical,
                CritDamage = Read(request.Attacker, PropertyType.CritDmg),
                AmplifyMultiplier = amplifyMultiplier,
                DefenseReduction = Read(request.Target, PropertyType.DefenseReduction),
                DefenseIgnore = Read(request.Attacker, PropertyType.DefenseIgnore),
                TargetResistance = ResolveResistance(request.Target, request.Element),
                DamageReduction = Read(request.Target, PropertyType.DamageReduction)
            };

            resolution.Context = context;
            resolution.Damage = DamageCalculator.Evaluate(in context);

            return resolution;
        }

        /// <summary>
        /// 按反应类别把命中的配表行折算成对应乘区的输入。
        ///
        /// 一次命中只会触发一种反应，因此这四类互斥：增幅写乘区、激化写加算、剧变写独立伤害、
        /// 特殊类各有各的产物。
        /// </summary>
        private static void ResolveReaction(in DamageRequest request, Cfg.ReactionMatrixRow row, ref DamageResolution resolution, ref float amplifyMultiplier, ref float catalyzeBonus)
        {
            float mastery = Read(request.Attacker, PropertyType.ElementalMastery);
            float reactionBonus = ResolveReactionBonus(request.Attacker, row);
            float levelCoefficient = ResolveLevelCoefficient(request.AttackerLevel);


            switch (row.Category)
            {
                case Cfg.ReactionCategory.Amplifying:
                    amplifyMultiplier = ReactionFormula.Amplifying(row.Multiplier, mastery, reactionBonus, row.EmCoefficient, row.EmDenominator);
                    break;

                case Cfg.ReactionCategory.Catalyze:
                    catalyzeBonus = ReactionFormula.Catalyze(levelCoefficient, row.Multiplier, mastery, reactionBonus, row.EmCoefficient, row.EmDenominator);
                    break;

                case Cfg.ReactionCategory.Transformative:
                    resolution.TransformativeElement = row.DamageElement;
                    resolution.TransformativeDamage = ResolveTransformative(row, request.Attacker, request.Target, request.AttackerLevel);
                    break;

                case Cfg.ReactionCategory.Special:
                    // 特殊类反应不产出即时伤害。写入伪元素状态的那几行（冻结、绽放、原激化）在元素系统里完成；
                    // 剩下配了倍率的（结晶）产出一个数值载荷，交给产物 Effect 承载它的时长与元素。
                    if (row.ProducedAura == Cfg.AuraKey.None && row.Multiplier > 0f)
                    {
                        // 结晶护盾的元素是**被结晶的那个附着**，不是触发方的岩元素。
                        resolution.ProductElement = PseudoElement.ToElement(row.AuraElement);
                        resolution.ProductValue = DamageCalculator.EvaluateShield(new ShieldContext
                        {
                            Multiplier = 1f,
                            ScalingValue = levelCoefficient * row.Multiplier * ReactionFormula.Scale(mastery, reactionBonus, row.EmCoefficient, row.EmDenominator),
                            // 护盾强效在 ShieldOperation 施加时按施加者面板再乘一次，这里只出反应本身的量。
                            ShieldStrength = 0f
                        });
                    }
                    break;
            }
        }

        /// <summary>
        /// 求一次剧变反应的独立伤害。
        ///
        /// 独立成公开入口是因为剧变不一定由命中直接触发：草原核到期引爆时早已没有攻击上下文，
        /// 但它走的必须是同一条公式，否则「同一个反应两个实现」迟早会分叉。
        /// </summary>
        /// <param name="row">反应行；提供倍率、伤害元素与专属加成位。</param>
        /// <param name="attacker">施加者属性面板；提供元素精通与剧变加成。</param>
        /// <param name="target">目标属性面板；提供对应元素的抗性。</param>
        /// <param name="attackerLevel">施加者等级，用于查反应等级系数。</param>
        public static float ResolveTransformative(Cfg.ReactionMatrixRow row, PropertyComponent attacker, PropertyComponent target, int attackerLevel)
        {
            return DamageCalculator.EvaluateTransformative(new TransformativeDamageContext
            {
                LevelCoefficient = ResolveLevelCoefficient(attackerLevel),
                ReactionMultiplier = row.Multiplier,
                ElementalMastery = Read(attacker, PropertyType.ElementalMastery),
                // 剧变额外吃全剧变加成；05A 规定它与单项加成相加而非相乘。
                ReactionBonus = ResolveReactionBonus(attacker, row) + Read(attacker, PropertyType.AllTransformativeBonus),
                MasteryCoefficient = row.EmCoefficient,
                MasteryDenominator = row.EmDenominator,
                TargetResistance = ResolveResistance(target, row.DamageElement)
            });
        }

        /// <summary>
        /// 聚合伤害加成区 `全伤害加成 + 对应元素加成 + 对应动作加成`。
        /// 三者同处一个乘区相加，因此不能拆成多次乘算。
        /// </summary>
        private static float ResolveDamageBonus(in DamageRequest request)
        {
            PropertyComponent attacker = request.Attacker;
            float bonus = Read(attacker, PropertyType.AllDamageBonus) + Read(attacker, ElementPropertySlots.DamageBonus(request.Element));
            if (ElementPropertySlots.TryActionBonus(request.ActionType, out PropertyType actionSlot)) bonus += Read(attacker, actionSlot);
            // DamageBoost 是旧模型留下的通用出伤乘区，仍被现有 Effect 使用，并入同一加成区。
            return bonus + Read(attacker, PropertyType.DamageBoost);
        }

        /// <summary>求目标对指定元素的最终抗性 `抗性 - 抗性削减`；结果可为负，抗性区自己处理负段。</summary>
        private static float ResolveResistance(PropertyComponent target, Cfg.ElementType element)
        {
            return Read(target, ElementPropertySlots.Resistance(element)) - Read(target, ElementPropertySlots.ResistanceReduction(element));
        }

        /// <summary>按配表列名读取该反应的专属加成；配表未指定专属加成位时为 0。</summary>
        private static float ResolveReactionBonus(PropertyComponent attacker, Cfg.ReactionMatrixRow row)
        {
            return ElementPropertySlots.TryReactionBonus(row.ReactionBonusAttr, out PropertyType slot) ? Read(attacker, slot) : 0f;
        }

        /// <summary>按施加者等级查剧变/激化/结晶共用的等级系数。</summary>
        private static float ResolveLevelCoefficient(int attackerLevel)
        {
            return Core.Config.Tables.TbReactionLevelCoefficient.Get(attackerLevel).Coefficient;
        }

        /// <summary>读取属性位；面板缺失时按 0 处理，使无属性组件的伤害来源仍能走完管线。</summary>
        internal static float Read(PropertyComponent property, PropertyType type)
        {
            return property == null ? 0f : property.GetValue(type);
        }
    }
}
