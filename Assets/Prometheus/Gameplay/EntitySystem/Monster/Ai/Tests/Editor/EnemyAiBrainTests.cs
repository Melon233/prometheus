using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Ai.Tests
{
    /// <summary>
    /// 验证资产图解释、实例隔离、巡逻、攻击冷却和挂起生命周期；测试代理只存在于 Editor 测试程序集。
    /// </summary>
    public sealed class EnemyAiBrainTests
    {
        private readonly List<UnityEngine.Object> cleanupObjects = new List<UnityEngine.Object>();

        /// <summary>每个测试结束后销毁临时资产和 GameObject，避免 Unity 对象跨测试泄漏。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = cleanupObjects.Count - 1; index >= 0; index--)
            {
                if (cleanupObjects[index] != null) UnityEngine.Object.DestroyImmediate(cleanupObjects[index]);
            }

            cleanupObjects.Clear();
        }

        /// <summary>验证共享同一资产的两个 Brain 拥有独立目标、状态和黑板。</summary>
        [Test]
        public void SharedDefinition_BrainsKeepRuntimeStateIndependent()
        {
            EnemyAiDefinition definition = CreateAcquireAndChaseDefinition();
            RecordingEnemyAiAgent firstAgent = CreateAgent("FirstAgent", Vector3.zero);
            RecordingEnemyAiAgent secondAgent = CreateAgent("SecondAgent", new Vector3(10f, 0f, 0f));
            firstAgent.SetTarget(CreateTarget("FirstTarget", new Vector3(1f, 0f, 0f)));
            EnemyAiBrain firstBrain = new EnemyAiBrain(definition, firstAgent, 1);
            EnemyAiBrain secondBrain = new EnemyAiBrain(definition, secondAgent, 2);
            firstBrain.Start();
            secondBrain.Start();
            firstBrain.Tick(0f);
            secondBrain.Tick(0f);
            Assert.That(firstBrain.CurrentStateId, Is.EqualTo(EnemyAiStateIds.Chase));
            Assert.That(secondBrain.CurrentStateId, Is.EqualTo(EnemyAiStateIds.Idle));
            Assert.That(firstBrain.Blackboard.Target, Is.Not.Null);
            Assert.That(secondBrain.Blackboard.Target, Is.Null);
            Assert.That(firstBrain.Blackboard, Is.Not.SameAs(secondBrain.Blackboard));
            firstBrain.Dispose();
            secondBrain.Dispose();
        }

        /// <summary>验证待机计时结束后由资产转移到巡逻，并执行选点与移动动作。</summary>
        [Test]
        public void IdleElapsed_TransitionsToPatrolAndMovesTowardAssetDrivenPoint()
        {
            EnemyAiDefinition definition = CreateIdlePatrolDefinition();
            RecordingEnemyAiAgent agent = CreateAgent("PatrolAgent", Vector3.zero);
            EnemyAiBrain brain = new EnemyAiBrain(definition, agent, 7);
            brain.Start();
            brain.Tick(0.11f);
            Assert.That(brain.CurrentStateId, Is.EqualTo(EnemyAiStateIds.Patrol));
            Assert.That(brain.Blackboard.HasPatrolPoint, Is.True);
            Assert.That(agent.MoveCalls, Is.EqualTo(1));
            Assert.That(agent.PlayMoveCalls, Is.EqualTo(1));
            brain.Dispose();
        }

        /// <summary>验证攻击自然完成后进入冷却，并在冷却耗尽前不会重复开始。</summary>
        [Test]
        public void AttackCompletion_StartsCooldownAndPreventsEarlyRestart()
        {
            EnemyAiDefinition definition = CreateAttackDefinition(1f);
            RecordingEnemyAiAgent agent = CreateAgent("AttackAgent", Vector3.zero);
            agent.SetTarget(CreateTarget("AttackTarget", new Vector3(1f, 0f, 0f)));
            EnemyAiBrain brain = new EnemyAiBrain(definition, agent, 3);
            brain.Start();
            brain.Tick(0f);
            Assert.That(agent.AttackStartCalls, Is.EqualTo(1));
            agent.CompleteAttack(true);
            Assert.That(brain.Blackboard.AttackCooldownRemaining, Is.EqualTo(1f));
            brain.Tick(0.5f);
            Assert.That(agent.AttackStartCalls, Is.EqualTo(1));
            brain.Tick(0.5f);
            Assert.That(agent.AttackStartCalls, Is.EqualTo(2));
            brain.Dispose();
        }

        /// <summary>验证挂起会取消进行中的攻击，恢复后仍使用原有目标和状态继续决策。</summary>
        [Test]
        public void Suspend_CancelsAttackAndResumeKeepsStateAndTarget()
        {
            EnemyAiDefinition definition = CreateAttackDefinition(1f);
            RecordingEnemyAiAgent agent = CreateAgent("SuspendAgent", Vector3.zero);
            Transform target = CreateTarget("SuspendTarget", new Vector3(1f, 0f, 0f));
            agent.SetTarget(target);
            EnemyAiBrain brain = new EnemyAiBrain(definition, agent, 4);
            brain.Start();
            brain.Tick(0f);
            brain.Suspend();
            Assert.That(agent.CancelAttackCalls, Is.EqualTo(1));
            Assert.That(brain.Blackboard.AttackInProgress, Is.False);
            Assert.That(brain.Blackboard.Target, Is.SameAs(target));
            brain.Resume();
            brain.Tick(0f);
            Assert.That(brain.CurrentStateId, Is.EqualTo(EnemyAiStateIds.Attack));
            Assert.That(agent.AttackStartCalls, Is.EqualTo(2));
            brain.Dispose();
        }

        /// <summary>验证定义引用不存在的目标状态时在创建 Brain 前明确失败。</summary>
        [Test]
        public void DefinitionValidation_RejectsMissingTransitionTarget()
        {
            EnemyAiTransitionDefinition invalidTransition = new EnemyAiTransitionDefinition().ConfigureForTests("Missing", 1, Array.Empty<EnemyAiConditionDefinition>());
            EnemyAiStateDefinition idle = new EnemyAiStateDefinition().ConfigureForTests(EnemyAiStateIds.Idle, Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), new[] { invalidTransition });
            EnemyAiDefinition definition = CreateDefinition(EnemyAiStateIds.Idle, 0.1f, new[] { idle });
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => definition.ValidateOrThrow());
            Assert.That(exception.Message, Does.Contain("Missing"));
        }

        /// <summary>验证项目正式 Slime 资产图有效、预制体已经绑定新 AI，并且不再携带旧 AI MonoComponent。</summary>
        [Test]
        public void SlimeContent_UsesValidatedDefinitionAndExcludesRetiredAiComponents()
        {
            EnemyAiDefinition definition = AssetDatabase.LoadAssetAtPath<EnemyAiDefinition>("Assets/BundleResources/Config/Ai/SlimeEnemyAi.asset");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BundleResources/Enemy/Slime.prefab");
            Assert.That(definition, Is.Not.Null);
            Assert.That(prefab, Is.Not.Null);
            Assert.DoesNotThrow(() => definition.ValidateOrThrow());
            EnemyAiComponent aiComponent = prefab.GetEntityComponent<EnemyAiComponent>();
            Assert.That(aiComponent, Is.Not.Null);
            Assert.That(aiComponent.Definition, Is.SameAs(definition));
            MonoBehaviour[] behaviours = prefab.GetComponents<MonoBehaviour>();
            Assert.That(Array.Exists(behaviours, component => component != null && component.GetType() == typeof(PatrolComponent)), Is.False);
            Assert.That(Array.Exists(behaviours, component => component != null && component.GetType() == typeof(EnmityComponent)), Is.False);
            Assert.That(Array.Exists(behaviours, component => component != null && component.GetType() == typeof(EAttackComponent)), Is.False);
            Assert.That(Array.Exists(behaviours, component => component != null && component.GetType() == typeof(EIdleComponent)), Is.False);
        }

        /// <summary>创建包含发现目标转移的最小定义。</summary>
        private EnemyAiDefinition CreateAcquireAndChaseDefinition()
        {
            EnemyAiConditionDefinition hasTarget = new EnemyAiConditionDefinition().ConfigureForTests(EnemyAiConditionType.HasTarget);
            EnemyAiTransitionDefinition chaseTransition = new EnemyAiTransitionDefinition().ConfigureForTests(EnemyAiStateIds.Chase, 100, new[] { hasTarget });
            EnemyAiStateDefinition idle = new EnemyAiStateDefinition().ConfigureForTests(EnemyAiStateIds.Idle, Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), new[] { chaseTransition });
            EnemyAiStateDefinition chase = new EnemyAiStateDefinition().ConfigureForTests(EnemyAiStateIds.Chase, Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiTransitionDefinition>());
            return CreateDefinition(EnemyAiStateIds.Idle, 0.1f, new[] { idle, chase });
        }

        /// <summary>创建待机结束后进入巡逻的最小定义。</summary>
        private EnemyAiDefinition CreateIdlePatrolDefinition()
        {
            EnemyAiActionDefinition resetIdle = new EnemyAiActionDefinition().ConfigureForTests(EnemyAiActionType.ResetIdleTimer, 0.1f);
            EnemyAiConditionDefinition idleElapsed = new EnemyAiConditionDefinition().ConfigureForTests(EnemyAiConditionType.IdleElapsed);
            EnemyAiTransitionDefinition patrolTransition = new EnemyAiTransitionDefinition().ConfigureForTests(EnemyAiStateIds.Patrol, 10, new[] { idleElapsed });
            EnemyAiStateDefinition idle = new EnemyAiStateDefinition().ConfigureForTests(EnemyAiStateIds.Idle, new[] { resetIdle }, Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiActionDefinition>(), new[] { patrolTransition });
            EnemyAiActionDefinition choosePoint = new EnemyAiActionDefinition().ConfigureForTests(EnemyAiActionType.ChoosePatrolPoint);
            EnemyAiActionDefinition playMove = new EnemyAiActionDefinition().ConfigureForTests(EnemyAiActionType.PlayMove);
            EnemyAiActionDefinition move = new EnemyAiActionDefinition().ConfigureForTests(EnemyAiActionType.MoveToPatrolPoint);
            EnemyAiStateDefinition patrol = new EnemyAiStateDefinition().ConfigureForTests(EnemyAiStateIds.Patrol, new[] { choosePoint, playMove }, new[] { move }, Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiTransitionDefinition>());
            return CreateDefinition(EnemyAiStateIds.Idle, 0.1f, new[] { idle, patrol });
        }

        /// <summary>创建以攻击为初始状态的最小定义。</summary>
        private EnemyAiDefinition CreateAttackDefinition(float attackCooldown)
        {
            EnemyAiActionDefinition startAttack = new EnemyAiActionDefinition().ConfigureForTests(EnemyAiActionType.StartAttack);
            EnemyAiStateDefinition attack = new EnemyAiStateDefinition().ConfigureForTests(EnemyAiStateIds.Attack, Array.Empty<EnemyAiActionDefinition>(), new[] { startAttack }, Array.Empty<EnemyAiActionDefinition>(), Array.Empty<EnemyAiTransitionDefinition>());
            return CreateDefinition(EnemyAiStateIds.Attack, attackCooldown, new[] { attack });
        }

        /// <summary>创建具有合法数值范围的临时 ScriptableObject 定义并登记清理。</summary>
        private EnemyAiDefinition CreateDefinition(string initialStateId, float attackCooldown, IEnumerable<EnemyAiStateDefinition> states)
        {
            EnemyAiDefinition definition = ScriptableObject.CreateInstance<EnemyAiDefinition>();
            cleanupObjects.Add(definition);
            return definition.ConfigureForTests("Tests.EnemyAi", initialStateId, 0.01f, 0.01f, 1, 4f, 8f, 2f, 5f, 3f, 2f, 3f, 3f, 2f, attackCooldown, 0.1f, states);
        }

        /// <summary>创建测试代理及其宿主 GameObject 并登记清理。</summary>
        private RecordingEnemyAiAgent CreateAgent(string name, Vector3 position)
        {
            GameObject owner = new GameObject(name);
            cleanupObjects.Add(owner);
            owner.transform.position = position;
            return new RecordingEnemyAiAgent(owner.transform);
        }

        /// <summary>创建测试目标 Transform 并登记清理。</summary>
        private Transform CreateTarget(string name, Vector3 position)
        {
            GameObject target = new GameObject(name);
            cleanupObjects.Add(target);
            target.transform.position = position;
            return target.transform;
        }

        /// <summary>
        /// 仅用于测试的可记录 Agent，通过正式 IEnemyAiAgent 契约驱动 Brain，不向 Runtime 注入任何测试分支。
        /// </summary>
        private sealed class RecordingEnemyAiAgent : IEnemyAiAgent
        {
            private readonly Transform owner;
            private Transform availableTarget;
            private Action<bool> attackFinished;

            /// <summary>创建一个绑定到测试 Transform 的代理。</summary>
            public RecordingEnemyAiAgent(Transform owner)
            {
                this.owner = owner;
            }

            /// <inheritdoc />
            public Vector3 Position => owner.position;

            /// <inheritdoc />
            public bool CanAct { get; set; } = true;

            /// <inheritdoc />
            public bool CanMove { get; set; } = true;

            /// <summary>获取移动调用次数。</summary>
            public int MoveCalls { get; private set; }

            /// <summary>获取移动动画调用次数。</summary>
            public int PlayMoveCalls { get; private set; }

            /// <summary>获取攻击开始次数。</summary>
            public int AttackStartCalls { get; private set; }

            /// <summary>获取攻击取消次数。</summary>
            public int CancelAttackCalls { get; private set; }

            /// <summary>指定下一次感知可返回的目标。</summary>
            public void SetTarget(Transform target)
            {
                availableTarget = target;
            }

            /// <summary>完成当前测试攻击并清空回调。</summary>
            public void CompleteAttack(bool completed)
            {
                Action<bool> callback = attackFinished;
                attackFinished = null;
                callback?.Invoke(completed);
            }

            /// <inheritdoc />
            public bool TryAcquireTarget(float radius, int layerMask, string requiredTag, out Transform target)
            {
                target = availableTarget;
                return target != null && Vector3.Distance(Position, target.position) <= radius;
            }

            /// <inheritdoc />
            public bool IsTargetValid(Transform target)
            {
                return target != null && target.gameObject.activeInHierarchy;
            }

            /// <inheritdoc />
            public void Move(Vector3 worldDirection, float speed, float deltaTime)
            {
                MoveCalls++;
                owner.position += worldDirection * speed * deltaTime;
            }

            /// <inheritdoc />
            public void StopMovement()
            {
            }

            /// <inheritdoc />
            public void Face(Vector3 worldDirection)
            {
            }

            /// <inheritdoc />
            public void PlayIdle()
            {
            }

            /// <inheritdoc />
            public void PlayMove()
            {
                PlayMoveCalls++;
            }

            /// <inheritdoc />
            public bool TryStartAttack(Action<bool> onFinished)
            {
                if (attackFinished != null) return false;
                AttackStartCalls++;
                attackFinished = onFinished;
                return true;
            }

            /// <inheritdoc />
            public void CancelAttack()
            {
                if (attackFinished == null) return;
                CancelAttackCalls++;
                CompleteAttack(false);
            }
        }
    }
}
