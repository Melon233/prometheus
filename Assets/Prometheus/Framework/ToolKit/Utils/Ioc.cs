using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
namespace Xuan.Prometheus
{
    /// <summary>
    /// 定义由 Core 托管模块的统一生命周期。
    /// 无状态 Kit 可以直接继承默认实现，仅在确实需要初始化、逐帧更新或释放资源时覆写对应方法。
    /// </summary>
    public abstract class Kit : IDisposable
    {
        /// <summary>
        /// 执行 Kit 在同步 AfterNew 前必须完成的异步初始化任务；无异步工作的 Kit 直接返回已完成任务。
        /// </summary>
        public virtual UniTask AfterNewAsync()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 在 Entry 等待全部 Kit 的 AfterNewAsync 完成后，由 Core 按注册顺序调用。
        /// </summary>
        public virtual void AfterNew()
        {
        }

        /// <summary>
        /// 由 Core 在入口组件的 Update 中统一驱动。
        /// </summary>
        /// <param name="dt">当前帧的增量时间。</param>
        public virtual void OnUpdate(float dt)
        {
        }

        /// <summary>
        /// 按注册顺序的逆序释放 Kit 持有的运行时状态。
        /// </summary>
        public virtual void Dispose()
        {
        }
    }
    public interface IIoc
    {
        void Add<T>(T singleton);
        T Get<T>();
    }
    public class Ioc : IIoc
    {
        private Dictionary<Type, object> objDict = new();

        public T Get<T>()
        {
            if (objDict.ContainsKey(typeof(T)))
            {
                return (T)objDict[typeof(T)];
            }
            throw new Exception($"未找到类型 {typeof(T)} 的实例");  // 未找到实例时抛出异常
        }

        public void Add<T>(T singleton)
        {
            if (objDict.ContainsKey(typeof(T)))
            {
                throw new Exception($"类型 {typeof(T)} 已经存在");  // 类型已经存在时抛出异常
            }
            objDict.Add(typeof(T), singleton);
        }
    }
}
