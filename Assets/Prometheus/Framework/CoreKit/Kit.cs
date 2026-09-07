using System;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 定义由 Core 托管模块的统一生命周期。
    /// 无状态 Kit 可以直接继承默认实现，仅在确实需要初始化、逐帧更新或释放资源时覆写对应方法。
    /// 该基类是 Core 生命周期编排的核心抽象，因此与 Core 同处 CoreKit。
    /// </summary>
    public abstract class Kit : IDisposable
    {
        /// <summary>执行 Kit 在同步 AfterNew 前必须完成的异步初始化任务；无异步工作的 Kit 直接返回已完成任务。</summary>
        public virtual UniTask AfterNewAsync()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>在入口等待全部 Kit 的 AfterNewAsync 完成后，由 Core 按注册顺序调用。</summary>
        public virtual void AfterNew()
        {
        }

        /// <summary>由 Core 在入口组件的每帧驱动中统一调用。</summary>
        /// <param name="dt">当前帧的增量时间。</param>
        public virtual void OnUpdate(float dt)
        {
        }

        /// <summary>按注册顺序的逆序释放 Kit 持有的运行时状态。</summary>
        public virtual void Dispose()
        {
        }
    }
}
