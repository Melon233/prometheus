using System;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Ai
{
    /// <summary>
    /// 保存敌人 AI 根定义的纯 C# 投影；所有可变决策数据由 EnemyAiBrain 持有而不会写回 Binder 或资产。
    /// </summary>
    public sealed class EnemyAiComponent : Component.Component, IEntityBinderComponent
    {
        private EnemyAiDefinition definition;
        private UnityEngine.CharacterController characterController;

        /// <summary>获取预制体引用的只读 AI 根定义。</summary>
        public EnemyAiDefinition Definition => definition;

        /// <summary>获取用于实际位移的 CharacterController，并在旧预制体未显式赋值时自动从同一对象获取。</summary>
        public UnityEngine.CharacterController CharacterController => characterController;

        /// <summary>
        /// 从唯一根 SlimeBinder 获取 AI 定义与角色运动出口。
        /// </summary>
        public void Bind(EntityBinder binder)
        {
            SlimeBinder slimeBinder = binder as SlimeBinder ?? throw new InvalidOperationException($"EnemyAiComponent requires SlimeBinder but received '{binder?.GetType().FullName}'.");
            definition = slimeBinder.EnemyAiDefinition;
            characterController = slimeBinder.CharacterController;
        }

        /// <summary>解除 AI 配置与 Unity 运动出口引用。</summary>
        public void Unbind()
        {
            definition = null;
            characterController = null;
        }
    }
}
