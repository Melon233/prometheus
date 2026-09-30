using System;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 全部可由 GameplayKit 注册的 System 契约的共同基接口，同时定义单局会话内的统一生命周期。
    /// 各领域契约（IEntitySystem、ITeamSystem 等）直接继承本接口，实现类只需实现自己的领域契约；
    /// 同一契约在一个 GameplayKit 中只能注册一个实例。
    ///
    /// 相位与 <see cref="IKit"/> 逐项同构：构造与 AfterNewAsync、AfterNew 在会话建立时执行，Dispose 在会话销毁时执行。
    /// 游戏只有一个世界，场景在会话建立前已由入口加载完成，因此不存在独立的世界相位，依赖场景的内容直接在 AfterNew 中建立。
    /// 每个 System 必须实现全部生命周期方法；没有对应工作的 System 写空实现（AfterNewAsync 返回已完成任务）。
    /// 生命周期方法随契约一起公开，但只应由 GameplayKit 调用。
    /// </summary>
    public interface ISystem : IDisposable
    {
        /// <summary>
        /// 在全部 System 构造完成后**并行**执行，用于加载本系统自己的配置资产。
        ///
        /// 该相位禁止调用其他 System：并行执行意味着此刻别的系统可能还没加载完自己的配置，
        /// 读到的会是半成品。没有跨系统读取，并行才是安全的。
        /// 需要与其他系统协作的初始化一律放到 <see cref="AfterNew"/>。
        /// </summary>
        UniTask AfterNewAsync();

        /// <summary>在全部 System 完成 <see cref="AfterNewAsync"/> 后按注册顺序调用；此时可以安全调用注入的依赖和当前场景。</summary>
        void AfterNew();

        /// <summary>
        /// 在当帧 Entity 更新结束后按注册顺序调用。
        /// 输入采样也在这一相位完成，因此输入对 Entity 的影响固定延迟一帧；
        /// 需要同帧处理另一 System 本帧结果的 System，必须注册在其之后。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        void OnUpdate(float dt);
    }
}
