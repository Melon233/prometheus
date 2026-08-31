using System;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus.Film
{
    /// <summary>向业务层暴露一次演出实例的只读状态、控制操作和异步完成结果。</summary>
    public sealed class FilmHandle
    {
        /// <summary>保存句柄所代理的运行时演出实例。</summary>
        private readonly FilmInstance instance;

        /// <summary>由 FilmSystem 为一个已经完成初始化的运行时实例创建控制句柄。</summary>
        internal FilmHandle(FilmInstance instance)
        {
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        /// <summary>获取当前 FilmSystem 内单调递增的实例编号。</summary>
        public int InstanceId => instance.InstanceId;

        /// <summary>获取该实例使用的演出配置标识。</summary>
        public string FilmId => instance.Definition.FilmId;

        /// <summary>获取当前演出生命周期状态。</summary>
        public FilmState State => instance.State;

        /// <summary>获取演出离开运行态的原因；尚未结束时为 None。</summary>
        public FilmStopReason StopReason => instance.StopReason;

        /// <summary>获取当前 Timeline 播放时间；运行时对象已经释放后保留最终时间。</summary>
        public double Time => instance.Time;

        /// <summary>捕获当前演出位置和流程变量，供存档或网络同步使用。</summary>
        public FilmPlaybackSnapshot CaptureSnapshot()
        {
            return instance.CaptureSnapshot();
        }

        /// <summary>异步等待演出自然完成、主动停止、失败或随系统释放。</summary>
        /// <returns>演出的最终停止原因。</returns>
        public UniTask<FilmStopReason> WaitForCompletionAsync()
        {
            return instance.WaitForCompletionAsync();
        }

        /// <summary>暂停当前演出，重复暂停保持幂等。</summary>
        public void Pause()
        {
            instance.Pause();
        }

        /// <summary>恢复一个由当前句柄暂停的演出。</summary>
        public void Resume()
        {
            instance.Resume();
        }

        /// <summary>主动停止当前演出并立即释放输入、镜头和运行时对象。</summary>
        public void Stop()
        {
            instance.Stop(FilmStopReason.Requested);
        }

        /// <summary>按 FilmDefinition.SkipMode 请求跳过当前演出。</summary>
        public void Skip()
        {
            instance.Skip();
        }
    }
}
