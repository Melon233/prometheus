using System;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 定义单局 GameplayKit 独占的公共系统生命周期；同一具体系统类型在一个 GameplayKit 中只能注册一个实例。
    ///
    /// 相位与 <see cref="Kit"/> 逐项同构，并额外补上一对世界级相位。三段生命周期的划分是：
    /// 构造与 <see cref="AfterNewAsync"/>、<see cref="AfterNew"/> 属于**会话**（登录后建立，登出销毁）；
    /// <see cref="OnWorldEnterAsync"/> 与 <see cref="OnWorldExit"/> 属于**世界**（每次进出场景）。
    /// 一个会话跨越多个世界，因此跨场景存活的状态（背包、任务进度、小队编成）留在会话相位，
    /// 绑定场景 GameObject 的状态（Entity、POI、NPC）必须落在世界相位。
    /// </summary>
    public abstract class XSystem : IDisposable
    {
        /// <summary>
        /// 在全部 System 构造完成后**并行**执行，用于加载本系统自己的配置资产。
        ///
        /// 该相位禁止调用其他 System：并行执行意味着此刻别的系统可能还没加载完自己的配置，
        /// 读到的会是半成品。没有跨系统读取，并行才是安全的。
        /// 需要与其他系统协作的初始化一律放到 <see cref="AfterNew"/>。
        /// </summary>
        public virtual UniTask AfterNewAsync()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>在全部 System 完成 <see cref="AfterNewAsync"/> 后按注册顺序调用；此时可以安全调用注入的依赖。</summary>
        public virtual void AfterNew()
        {
        }

        /// <summary>
        /// 在场景加载完成后按注册顺序调用，用于建立依赖当前场景的内容。
        ///
        /// 该相位是异步的：进入世界需要加载角色预制体、生成实体，可以慢、可以显示进度。
        /// </summary>
        /// <param name="world">本次进入的世界上下文。</param>
        public virtual UniTask OnWorldEnterAsync(WorldContext world)
        {
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 在场景被卸载**之前**按注册顺序的逆序调用，用于释放绑定当前场景的内容。
        ///
        /// 该相位刻意是同步的：场景下一刻就会被 Unity 销毁，不存在"慢慢退出"的余地，
        /// 能做的只有释放引用。任何耗时操作都会让释放窗口跨帧，从而与销毁竞争。
        /// </summary>
        public virtual void OnWorldExit()
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

        /// <summary>在会话结束时按注册顺序的逆序调用，清理系统持有的会话级资源。</summary>
        public virtual void Dispose()
        {
        }
    }
}
