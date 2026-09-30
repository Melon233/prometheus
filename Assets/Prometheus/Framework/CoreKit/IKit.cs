using System;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 全部可由 Core 注册的 Kit 契约的共同基接口，同时定义 Core 托管的统一生命周期。
    /// 各模块契约（IAssetKit、IEventKit 等）直接继承本接口，实现类只需实现自己的模块契约。
    /// 每个 Kit 必须实现全部生命周期方法；没有对应工作的 Kit 写空实现（AfterNewAsync 返回已完成任务）。
    /// 生命周期方法随契约一起公开，但只应由 Core 调用，业务代码不得直接驱动 Kit 的生命周期。
    /// </summary>
    public interface IKit : IDisposable
    {
        /// <summary>执行 Kit 在同步 AfterNew 前必须完成的异步初始化任务；无异步工作的 Kit 直接返回已完成任务。</summary>
        UniTask AfterNewAsync();

        /// <summary>在入口等待全部 Kit 的 AfterNewAsync 完成后，由 Core 按注册顺序调用。</summary>
        void AfterNew();

        /// <summary>由 Core 在入口组件的每帧驱动中统一调用。</summary>
        /// <param name="dt">当前帧的增量时间。</param>
        void OnUpdate(float dt);
    }
}
