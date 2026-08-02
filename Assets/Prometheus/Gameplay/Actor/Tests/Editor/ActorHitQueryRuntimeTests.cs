using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证命中窗口的行为实例隔离与资源清理语义，测试辅助类型只存在于 EditorTests 程序集。</summary>
    public sealed class ActorHitQueryRuntimeTests
    {
        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        /// <summary>在每个用例结束后回收临时 GameObject，避免 Unity 组件残留影响其他 EditMode 测试。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--) if (createdObjects[index] != null) UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            createdObjects.Clear();
        }

        /// <summary>验证窗口键包含 BehaviorHandle.InstanceId，使旧行为退出与同 Tick 新行为打开同名 Clip 时拥有独立去重集合。</summary>
        [Test]
        public void HitWindows_WithSameClipIdAcrossBehaviorInstances_AreIndependent()
        {
            GameObject actorObject = Track(new GameObject("HitQueryActor"));
            ActorAuthoringComponent authoring = actorObject.AddComponent<ActorAuthoringComponent>();
            PropertyComponent property = actorObject.AddComponent<PropertyComponent>();
            EffectComponent effect = actorObject.AddComponent<EffectComponent>();
            var entity = new TestEntity();
            entity.AddComp(property);
            entity.AddComp(effect);
            using (var runtime = new ActorHitQueryRuntime(authoring, entity, property, effect))
            using (var controller = new BehaviorController(new SilentBehaviorSink()))
            {
                var program = new BehaviorProgram("Attack", 1, Array.Empty<SimulationClip>());
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle firstHandle), Is.True);
                Assert.That(controller.Cancel(firstHandle), Is.True);
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle secondHandle), Is.True);
                var clip = new HitWindowClip("HitWindow", 0, 1, "Attack");
                Assert.DoesNotThrow(() => runtime.Open(firstHandle, clip));
                Assert.DoesNotThrow(() => runtime.Open(secondHandle, clip));
                Assert.Throws<InvalidOperationException>(() => runtime.Open(secondHandle, clip));
                Assert.That(runtime.Close(firstHandle, clip), Is.True);
                Assert.That(runtime.Close(firstHandle, clip), Is.False);
                Assert.That(runtime.Close(secondHandle, clip), Is.True);
                Assert.Throws<ArgumentException>(() => runtime.Open(default, clip));
                Assert.DoesNotThrow(() => runtime.CommitSignals());
            }
        }

        /// <summary>记录测试创建的 Unity 对象并原样返回，以便 TearDown 统一清理。</summary>
        private T Track<T>(T createdObject) where T : UnityEngine.Object
        {
            createdObjects.Add(createdObject);
            return createdObject;
        }

        /// <summary>提供不含生产行为的最小 Entity，仅用于满足 ActorHitQueryRuntime 的所有者类型约束。</summary>
        private sealed class TestEntity : Entity
        {
        }

        /// <summary>吞掉 BehaviorController 回调，使测试可以只关注其生成的稳定 BehaviorHandle。</summary>
        private sealed class SilentBehaviorSink : IBehaviorSimulationSink
        {
            /// <inheritdoc/>
            public void OnBehaviorStarted(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase)
            {
            }

            /// <inheritdoc/>
            public void OnClipEntered(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
            {
            }

            /// <inheritdoc/>
            public void OnClipSampled(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
            {
            }

            /// <inheritdoc/>
            public void OnClipExited(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase, BehaviorEndReason reason)
            {
            }

            /// <inheritdoc/>
            public void OnBehaviorEnded(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase, BehaviorEndReason reason)
            {
            }
        }
    }
}
