using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 基于组件开关的世界接管端口。
    /// <para>
    /// 输入接管与 AI 冻结都通过临时禁用一组指定组件实现，并在释放时精确还原到接管前的启用状态。
    /// 这是一种不依赖任何具体玩法系统的通用机制，适用于测试场景与轻量关卡。
    /// 接入 <c>IInputSystem</c> 的 Cutscene 输入上下文与 AI 系统的正式实现属于设计文档 §15 的集成工作。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneWorldPort : MonoBehaviour, INarrativeWorldPort
    {
        [SerializeField] [Tooltip("演出期间需要禁用的玩法输入相关组件。")] private List<Behaviour> gameplayInputBehaviours = new List<Behaviour>();
        [SerializeField] [Tooltip("演出期间需要冻结的 AI 组件；按冻结半径筛选。")] private List<Behaviour> aiBehaviours = new List<Behaviour>();

        /// <inheritdoc />
        public IDisposable LockGameplayInput()
        {
            return new BehaviourLease(gameplayInputBehaviours, null, 0f);
        }

        /// <inheritdoc />
        public IDisposable FreezeAi(Vector3 center, float radius)
        {
            return new BehaviourLease(aiBehaviours, center, radius);
        }

        /// <summary>
        /// 一次组件禁用租约。
        /// 记录每个组件接管前的启用状态，释放时逐个还原，避免把本来就禁用的组件错误地打开。
        /// </summary>
        private sealed class BehaviourLease : IDisposable
        {
            private readonly List<Behaviour> affected = new List<Behaviour>();
            private readonly List<bool> previousEnabled = new List<bool>();
            private bool released;

            /// <summary>禁用范围内的组件并记录其原始状态。</summary>
            /// <param name="candidates">候选组件。</param>
            /// <param name="center">筛选中心；为空表示不做范围筛选。</param>
            /// <param name="radius">筛选半径。</param>
            internal BehaviourLease(List<Behaviour> candidates, Vector3? center, float radius)
            {
                if (candidates == null) return;
                for (int index = 0; index < candidates.Count; index++)
                {
                    Behaviour behaviour = candidates[index];
                    if (behaviour == null) continue;
                    if (center.HasValue && radius > 0f && (behaviour.transform.position - center.Value).sqrMagnitude > radius * radius) continue;
                    affected.Add(behaviour);
                    previousEnabled.Add(behaviour.enabled);
                    behaviour.enabled = false;
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (released) return;
                released = true;
                for (int index = 0; index < affected.Count; index++)
                {
                    if (affected[index] != null) affected[index].enabled = previousEnabled[index];
                }
                affected.Clear();
                previousEnabled.Clear();
            }
        }
    }
}
