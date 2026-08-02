using System;
using System.Reflection;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>提供仅存在于测试程序集的 Entity 注册辅助入口，避免正式运行时代码暴露测试生命周期分支。</summary>
    internal static class ActorEntityTestExtensions
    {
        /// <summary>缓存 Entity 的内部 GameplayKit 绑定方法。</summary>
        private static readonly MethodInfo BindGameplayKitMethod = typeof(Entity).GetMethod("BindGameplayKit", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(IGameplayKit), typeof(int) }, null);

        /// <summary>在测试中模拟 GameplayKit.AddEntity 已完成的内部绑定阶段。</summary>
        /// <param name="entity">需要绑定的测试 Entity。</param>
        /// <param name="gameplayKit">测试使用的单局 GameplayKit 替身。</param>
        /// <param name="entityId">分配给测试 Entity 的正运行时编号。</param>
        internal static void BindForActorTests(this Entity entity, IGameplayKit gameplayKit, int entityId)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (gameplayKit == null) throw new ArgumentNullException(nameof(gameplayKit));
            if (BindGameplayKitMethod == null) throw new MissingMethodException(typeof(Entity).FullName, "BindGameplayKit");
            BindGameplayKitMethod.Invoke(entity, new object[] { gameplayKit, entityId });
        }
    }
}
