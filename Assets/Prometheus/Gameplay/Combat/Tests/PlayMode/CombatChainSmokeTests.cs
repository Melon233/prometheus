using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xuan.Prometheus.Bootstrap;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Elements;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Shields;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Combat.Tests
{
    /// <summary>
    /// 战斗链路的 PlayMode 冒烟测试：走**真实的** Core 启动、真实的会话组合根、真实的实体预制体。
    ///
    /// 它覆盖的是 EditMode 结构上做不到的那部分。EditMode 的 343 条用例全部使用替身实体，
    /// 因此下列问题在那里一条都暴露不出来：
    ///
    /// - 会话按真实顺序启动时，各 System 的 `AfterNewAsync` / `AfterNew` 会不会互相踩到
    ///   （配表是否已加载、效果库是否已就位、订阅是否早于被订阅方初始化）；
    /// - 实体预制体上到底有没有装配战斗链路需要的组件；
    /// - `ControlState` 是否真的抑制了行为能力；
    /// - 五个系统（元素、管线、产物、护盾、硬直）在同一次命中里串起来是否成立。
    ///
    /// 用例刻意只验证**链路是否成立**，不验证数值——数值由 EditMode 的纯函数用例锁定，
    /// 在这里重复断言只会让一次配表调整同时打断两处。
    /// </summary>
    public sealed class CombatChainSmokeTests
    {
        /// <summary>本次测试独占的 Core；与正式启动流程使用同一个类，不做任何替换。</summary>
        private Core core;
        /// <summary>攻击方实体，由真实预制体装配。</summary>
        private SlimeEntity attacker;
        /// <summary>承伤方实体，由真实预制体装配。</summary>
        private SlimeEntity target;

        /// <summary>
        /// 按 `GameFlow` 的真实顺序启动 Core 与玩法会话。
        ///
        /// 顺序本身就是被测对象之一：配表必须在任何玩法 System 构造之前加载完（ARCH-CONFIG-001），
        /// 而各 System 的异步配置加载必须全部完成之后才允许任何 `AfterNew` 运行。
        /// </summary>
        private async UniTask BootAsync()
        {
            core = new Core();
            await UniTask.WhenAll(core.CreateAfterNewTasks());
            core.AfterNew();
            Core.Config.Load();
            await Core.Gameplay.CreateSessionAsync(new PrometheusSystemInstaller());
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return BootAsync().ToCoroutine();

            IEntitySystem entitySystem = Core.Gameplay.GetSystem<IEntitySystem>();
            attacker = entitySystem.SpawnEnemy(new Vector3(0f, 0f, 0f));
            target = entitySystem.SpawnEnemy(new Vector3(3f, 0f, 0f));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Core.Gameplay?.DestroySession();
            core?.Dispose();
            core = null;
            attacker = null;
            target = null;
            yield return null;
        }

        /// <summary>取出当前会话的效果运行时。</summary>
        private static EffectRuntime Runtime => Core.Gameplay.GetSystem<IEffectSystem>().Runtime;

        /// <summary>
        /// 向目标发布一次真实的命中信号，走完整的触发路由与伤害管线。
        /// </summary>
        /// <param name="element">本次命中携带的元素。</param>
        /// <param name="abilityId">
        /// 能力编号。它同时是 ICD 分组键的一部分：共享 ICD 下同一编号连续命中只有第 1、4、7 次附着，
        /// 因此需要连续两次都附着的用例必须使用不同编号（对应角色的不同天赋）。
        /// </param>
        /// <param name="requestedDamage">命中信号携带的请求伤害。</param>
        /// <param name="gaugeStrength">附着强度档位。</param>
        /// <param name="staggerLevel">打断等级。</param>
        /// <remarks>
        /// 附着档位与打断等级在正式链路里来自 `TbAttackSegment`，由动画事件在命中窗口打开时查出。
        /// 本用例不播放真实攻击动画（那条路径见类注释的「仍未覆盖」），因此在这里直接给出一段
        /// 普攻段落该有的属性——被测对象是这些属性**落地之后**的链路，段落表本身由
        /// `AttackSegmentTableTests` 覆盖。
        /// </remarks>
        private void PublishHit(Cfg.ElementType element, string abilityId = "Smoke.NormalAttack", float requestedDamage = 20f,
            Cfg.GaugeStrength gaugeStrength = Cfg.GaugeStrength.Weak, int staggerLevel = 2)
        {
            Runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, attacker, target, attacker,
                requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack, abilityId,
                position: target.bindGo.transform.position,
                damage: new DamageFacts(element, DamageActionType.NormalAttack, null, staggerLevel, false, false,
                    gaugeStrength, Cfg.IcdPolicy.Shared, 0)));
        }

        // ---------- 启动与装配 ----------

        /// <summary>
        /// 真实会话能按组合根的顺序完整启动，且战斗链路依赖的五个 System 都在场。
        ///
        /// 这是本文件里最基础也最容易被忽略的一条：EditMode 用例逐个手工构造 System，
        /// 因此永远不会发现「某个 System 的 AfterNew 依赖了还没初始化的另一个」。
        /// </summary>
        [Test]
        public void Session_BootsWithEveryCombatSystemRegistered()
        {
            Assert.That(Core.Gameplay.IsReady, Is.True, "会话必须完整启动。");
            Assert.That(Core.Config.IsLoaded, Is.True, "配表必须在玩法 System 构造之前就绪。");
            Assert.That(Core.Gameplay.GetSystem<IElementSystem>(), Is.Not.Null);
            Assert.That(Core.Gameplay.GetSystem<IShieldSystem>(), Is.Not.Null);
            Assert.That(Core.Gameplay.GetSystem<IEffectSystem>(), Is.Not.Null);
            Assert.That(Core.Gameplay.GetSystem<Reactions.IReactionProductSystem>(), Is.Not.Null);
            Assert.That(Runtime, Is.Not.Null, "效果运行时必须在 AfterNewAsync 之后可用。");
        }

        /// <summary>
        /// 效果库与反应产物资产在真实资源包里确实存在并被正确引用。
        ///
        /// 产物是按配表的 `effectId` 查找的，字符串对不上时只会静默返回空——
        /// 冻结不冻人、结晶不出盾，全程没有任何报错。
        /// </summary>
        [Test]
        public void EffectLibrary_ResolvesEveryReactionProduct()
        {
            EffectLibrary library = Core.Gameplay.GetSystem<IEffectSystem>().DefaultLibrary;
            Assert.That(library, Is.Not.Null, "效果库必须在 AfterNewAsync 里加载完成。");
            foreach (string effectId in new[] { "Eff_Frozen", "Eff_Quicken", "Eff_Bloom_Core", "Eff_Crystallize" })
                Assert.That(library.GetReactionProduct(effectId), Is.Not.Null, $"配表引用的产物 '{effectId}' 在效果库里找不到。");
        }

        /// <summary>真实预制体装配出了战斗链路需要的全部组件与 Logic。</summary>
        [Test]
        public void SpawnedEntity_CarriesEveryComponentTheCombatChainNeeds()
        {
            Assert.That(target.EntityId, Is.GreaterThan(0), "实体必须已由 EntitySystem 登记。");
            Assert.That(target.bindGo, Is.Not.Null, "预制体必须已实例化。");
            Assert.That(target.TryGetComp(out PropertyComponent _), Is.True);
            Assert.That(target.TryGetComp(out EventComponent _), Is.True);
            Assert.That(target.TryGetComp(out EffectComponent _), Is.True);
            Assert.That(target.TryGetLogic(out AttackedLogic _), Is.True, "没有 AttackedLogic 就没有受击表现。");
        }

        // ---------- 一次命中的完整链路 ----------

        /// <summary>
        /// 一次真实命中能走完「触发路由 → 元素附着 → 伤害管线 → 扣血 → 事实事件」。
        ///
        /// 只断言链路成立与方向正确，不断言具体数值。
        /// </summary>
        [UnityTest]
        public IEnumerator Hit_RunsTheWholeChainOnRealEntities()
        {
            target.TryGetComp(out PropertyComponent property);
            float hpBefore = property.Hp;
            int hpChangedCount = 0;
            target.TryGetComp(out EventComponent events);
            events.AddListener<HpChangedEvent>(_ => hpChangedCount++);

            PublishHit(Cfg.ElementType.Pyro);
            yield return null;

            Assert.That(property.Hp, Is.LessThan(hpBefore), "真实命中必须扣血。");
            Assert.That(hpChangedCount, Is.GreaterThan(0), "扣血必须发布生命变化事实。");
        }

        /// <summary>
        /// 元素附着真的写在了被击中的那个实体上，而不是别人身上。
        ///
        /// 附着按实体编号存放，编号来自真实的 `EntitySystem` 登记；EditMode 里这个编号是测试自己指定的。
        /// </summary>
        [UnityTest]
        public IEnumerator Hit_WritesElementAuraOnTheEntityThatWasHit()
        {
            PublishHit(Cfg.ElementType.Pyro);
            yield return null;

            IElementSystem elementSystem = Core.Gameplay.GetSystem<IElementSystem>();
            ElementAuraSet targetAuras = elementSystem.QueryAura(target.EntityId).Auras;
            Assert.That(targetAuras, Is.Not.Null, "被击中的实体必须有附着记录。");
            Assert.That(targetAuras.TryGet(Cfg.AuraKey.Pyro, out _), Is.True, "火属性命中必须写入火附着。");
            Assert.That(elementSystem.QueryAura(attacker.EntityId).Auras, Is.Null, "攻击方不应被写入附着。");
        }

        /// <summary>
        /// 打断成立时目标进入受击控制状态，并因此真的失去行动能力。
        ///
        /// `CanAct` 这一条是 EditMode 完全覆盖不到的：那里的替身实体没有 `AttackedLogic`，
        /// 因此「发布了受击事实」与「角色真的动不了」之间的那一段一直没有被验证过。
        /// </summary>
        [UnityTest]
        public IEnumerator Stagger_PutsTheTargetIntoAttackedControlState()
        {
            target.TryGetComp(out PropertyComponent property);
            // 正式直接伤害的打断等级是 2，把抗打断等级压到 0 使打断必定成立。
            property.SetBaseValue(PropertyType.StaggerResistance, 0f);
            target.TryGetLogic(out AttackedLogic attackedLogic);
            Assert.That(property.CanAct, Is.True, "命中之前目标应当可以行动。");

            PublishHit(Cfg.ElementType.Physical);
            yield return null;

            Assert.That(attackedLogic.IsAttacked, Is.True, "打断成立必须建立受击动画会话。");
            Assert.That(property.ActiveControlStates & ControlState.Attacked, Is.EqualTo(ControlState.Attacked));
            Assert.That(property.CanAct, Is.False, "受击状态必须真的禁止行动。");
        }

        /// <summary>打断不成立时只扣血，目标保持可行动——这是分级硬直与旧二值模型的核心区别。</summary>
        [UnityTest]
        public IEnumerator StaggerResisted_KeepsTheTargetActable()
        {
            target.TryGetComp(out PropertyComponent property);
            // 抗打断等级高于直接伤害的打断等级 2，打断因此不成立。
            property.SetBaseValue(PropertyType.StaggerResistance, 5f);
            target.TryGetLogic(out AttackedLogic attackedLogic);
            float hpBefore = property.Hp;

            PublishHit(Cfg.ElementType.Physical);
            yield return null;

            Assert.That(property.Hp, Is.LessThan(hpBefore), "打断不成立不影响扣血。");
            Assert.That(attackedLogic.IsAttacked, Is.False, "打断不成立不得进入受击状态。");
            Assert.That(property.CanAct, Is.True);
        }

        /// <summary>
        /// 冻结在真实实体上确实让目标动不了。
        ///
        /// 这条串起了整条最长的链路：元素系统写入伪元素状态 → 广播 → `ReactionProductSystem`
        /// 从效果库取出 `Eff_Frozen` → 真实 `EffectRuntime` 施加 → `ControlStateModifierOperation`
        /// 写入控制状态 → 属性组件的行为能力。中间任何一环断掉，表现都是「冻结判定成立但角色照常行动」。
        /// </summary>
        [UnityTest]
        public IEnumerator Freeze_ActuallyStopsTheTargetFromActing()
        {
            target.TryGetComp(out PropertyComponent property);
            IElementSystem elementSystem = Core.Gameplay.GetSystem<IElementSystem>();

            // 两次命中必须都附着，因此使用不同的能力编号绕开共享 ICD。
            PublishHit(Cfg.ElementType.Cryo, "Smoke.CryoSkill");
            yield return null;
            PublishHit(Cfg.ElementType.Hydro, "Smoke.HydroSkill");
            yield return null;

            ElementAuraSet auras = elementSystem.QueryAura(target.EntityId).Auras;
            Assert.That(auras.TryGet(Cfg.AuraKey.Frozen, out _), Is.True, "水冰相遇必须写入冻结状态。");
            Assert.That(property.CanAct, Is.False, "冻结的目标必须真的动不了。");
            Assert.That(property.CanMove, Is.False);
        }

        /// <summary>
        /// 护盾在真实链路里先于生命值吸收伤害。
        ///
        /// 护盾按实体编号存放，这里验证编号确实与 `EntitySystem` 登记的一致——
        /// 对不上的表现是「护盾挂着但一点伤害都没挡」。
        /// </summary>
        [UnityTest]
        public IEnumerator Shield_AbsorbsDamageBeforeHealthOnRealEntities()
        {
            target.TryGetComp(out PropertyComponent property);
            IShieldSystem shieldSystem = Core.Gameplay.GetSystem<IShieldSystem>();
            shieldSystem.AddLayer(target.EntityId, string.Empty, Cfg.ElementType.None, 100000f);
            float hpBefore = property.Hp;
            float shieldBefore = shieldSystem.GetTotalRemaining(target.EntityId);

            PublishHit(Cfg.ElementType.Physical);
            yield return null;

            Assert.That(property.Hp, Is.EqualTo(hpBefore).Within(0.0001f), "护盾足够厚时不应扣血。");
            Assert.That(shieldSystem.GetTotalRemaining(target.EntityId), Is.LessThan(shieldBefore), "护盾必须承担这笔伤害。");
        }

        /// <summary>死亡后再次命中不产生受击表现，伤害链路也不应抛异常（验收用例 H-09）。</summary>
        [UnityTest]
        public IEnumerator FatalHit_DoesNotPlayHitReactionAndStaysStable()
        {
            target.TryGetComp(out PropertyComponent property);
            property.SetBaseValue(PropertyType.StaggerResistance, 0f);
            target.TryGetLogic(out AttackedLogic attackedLogic);

            PublishHit(Cfg.ElementType.Physical, requestedDamage: property.MaxHp * 10f);
            yield return null;

            Assert.That(property.IsDead, Is.True);
            Assert.That(attackedLogic.IsAttacked, Is.False, "致死伤害不得播放受击动画。");

            PublishHit(Cfg.ElementType.Physical, requestedDamage: property.MaxHp * 10f);
            yield return null;

            Assert.That(property.Hp, Is.Zero, "对尸体的再次命中不得产生任何结算。");
        }
    }
}
