using System;

namespace Xuan.Prometheus
{
    /// <summary>定义单局 GameplayKit 独占的公共系统生命周期；同一具体系统类型在一个 GameplayKit 中只能注册一个实例。</summary>
    public abstract class XSystem : IDisposable
    {
        /// <summary>在全部 System 注册完成且创建 Entity 之前调用，供系统建立单局运行时状态。</summary>
        /// <param name="gameplayKit">持有当前系统的单局 GameplayKit。</param>
        public virtual void AfterNew(IGameplayKit gameplayKit)
        {
        }

        /// <summary>在当帧 Entity 更新开始前调用，适合输入采样和命令分发等前置公共状态。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public virtual void BeforeEntityUpdate(float dt)
        {
        }

        /// <summary>在当帧 Entity 更新结束后调用，适合推进依赖 Entity 当帧结果的公共状态。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public virtual void OnUpdate(float dt)
        {
        }

        /// <summary>在全部 Entity 释放完成后调用，清理系统持有的单局资源。</summary>
        public virtual void Dispose()
        {
        }
    }
}
