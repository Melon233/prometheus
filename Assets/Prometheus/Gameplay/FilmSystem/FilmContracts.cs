using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Film
{
    /// <summary>描述一次演出实例从创建到释放的稳定生命周期状态。</summary>
    public enum FilmState
    {
        Created,
        Binding,
        Ready,
        Playing,
        WaitingForInteraction,
        Paused,
        Stopping,
        Completed,
        Stopped,
        Failed,
        Disposed
    }

    /// <summary>描述演出离开运行状态的原因，供调用方区分自然完成、主动停止和系统销毁。</summary>
    public enum FilmStopReason
    {
        None,
        Completed,
        Requested,
        SystemDisposed,
        Failed,
        InteractionFailed,
        Replaced,
        Skipped
    }

    /// <summary>定义跳过当前演出的策略；None 表示不允许外部跳过。</summary>
    public enum FilmSkipMode
    {
        None,
        ToEnd
    }

    /// <summary>描述可用于存档或网络同步的演出播放位置。</summary>
    public readonly struct FilmPlaybackSnapshot
    {
        /// <summary>创建一个包含演出标识、时间和流程变量的快照。</summary>
        public FilmPlaybackSnapshot(string filmId, int instanceId, double time, FilmState state, IReadOnlyDictionary<string, string> flowValues)
        {
            FilmId = filmId;
            InstanceId = instanceId;
            Time = time;
            State = state;
            FlowValues = flowValues ?? throw new ArgumentNullException(nameof(flowValues));
        }

        /// <summary>获取快照所属的演出标识。</summary>
        public string FilmId { get; }

        /// <summary>获取生成快照时的运行时实例编号。</summary>
        public int InstanceId { get; }

        /// <summary>获取 Timeline 播放时间。</summary>
        public double Time { get; }

        /// <summary>获取生成快照时的演出状态。</summary>
        public FilmState State { get; }

        /// <summary>获取条件分支使用的流程变量只读视图。</summary>
        public IReadOnlyDictionary<string, string> FlowValues { get; }
    }

    /// <summary>声明一个运行时绑定除了提供 Timeline 轨道目标外是否还承担 FilmSystem 的特殊职责。</summary>
    public enum FilmBindingRole
    {
        Generic,
        FilmCamera
    }

    /// <summary>声明 FilmDefinition 需要由调用方提供的一个具名运行时对象。</summary>
    [Serializable]
    public sealed class FilmBindingDefinition
    {
        [SerializeField] private string key;
        [SerializeField] private bool required = true;
        [SerializeField] private FilmBindingRole role;

        /// <summary>创建一个运行时绑定声明，主要供编辑器工具和自动化测试构造配置使用。</summary>
        /// <param name="key">与 Timeline 输出轨道 Stream Name 对齐的稳定绑定名。</param>
        /// <param name="required">缺少该绑定时是否拒绝启动演出。</param>
        /// <param name="role">该绑定在通用轨道绑定之外承担的系统职责。</param>
        public FilmBindingDefinition(string key, bool required = true, FilmBindingRole role = FilmBindingRole.Generic)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Film binding key cannot be empty.", nameof(key));
            this.key = key;
            this.required = required;
            this.role = role;
        }

        /// <summary>提供 Unity 序列化器创建 Inspector 数组元素所需的无参构造入口。</summary>
        public FilmBindingDefinition()
        {
        }

        /// <summary>获取与 Timeline 输出轨道名称对齐的稳定绑定名。</summary>
        public string Key => key;

        /// <summary>获取缺少该绑定时是否必须拒绝启动演出。</summary>
        public bool Required => required;

        /// <summary>获取该绑定在 FilmSystem 中承担的特殊职责。</summary>
        public FilmBindingRole Role => role;
    }

    /// <summary>描述 Timeline Marker 请求的一次对话交互。</summary>
    public readonly struct FilmDialogueRequest
    {
        /// <summary>创建带实例标识的对话请求，避免多个演出共享同名对话时发生串线。</summary>
        public FilmDialogueRequest(int instanceId, string interactionId)
        {
            InstanceId = instanceId;
            InteractionId = string.IsNullOrWhiteSpace(interactionId) ? throw new ArgumentException("Film interaction ID cannot be empty.", nameof(interactionId)) : interactionId;
        }

        /// <summary>获取发起请求的演出实例编号。</summary>
        public int InstanceId { get; }

        /// <summary>获取配置中的交互标识。</summary>
        public string InteractionId { get; }
    }

    /// <summary>描述 Timeline Marker 请求的一次 QTE 交互及其成功输入动作。</summary>
    public readonly struct FilmQteRequest
    {
        /// <summary>创建带成功动作和超时约束的 QTE 请求。</summary>
        public FilmQteRequest(int instanceId, string interactionId, InputActionMask successActions, float timeoutSeconds)
        {
            InstanceId = instanceId;
            InteractionId = string.IsNullOrWhiteSpace(interactionId) ? throw new ArgumentException("Film interaction ID cannot be empty.", nameof(interactionId)) : interactionId;
            SuccessActions = successActions == InputActionMask.None ? throw new ArgumentOutOfRangeException(nameof(successActions)) : successActions;
            TimeoutSeconds = timeoutSeconds;
        }

        /// <summary>获取发起请求的演出实例编号。</summary>
        public int InstanceId { get; }

        /// <summary>获取配置中的交互标识。</summary>
        public string InteractionId { get; }

        /// <summary>获取命中即视为成功的输入动作集合。</summary>
        public InputActionMask SuccessActions { get; }

        /// <summary>获取超时秒数；零表示由外部服务决定结束时间。</summary>
        public float TimeoutSeconds { get; }
    }

    /// <summary>统一表示对话或 QTE 的成功、失败及业务返回值。</summary>
    public readonly struct FilmInteractionResult
    {
        /// <summary>创建一次交互结果。</summary>
        public FilmInteractionResult(bool succeeded, string value = null)
        {
            Succeeded = succeeded;
            Value = value;
        }

        /// <summary>获取交互是否成功完成。</summary>
        public bool Succeeded { get; }

        /// <summary>获取交互返回的可选业务值。</summary>
        public string Value { get; }
    }

    /// <summary>抽象对话和 QTE 的异步执行端口，FilmSystem 不依赖具体 UI 实现。</summary>
    public interface IFilmInteractionService
    {
        /// <summary>请求显示对话并异步等待玩家完成或取消。</summary>
        UniTask<FilmInteractionResult> ShowDialogueAsync(FilmDialogueRequest request, System.Threading.CancellationToken cancellationToken);

        /// <summary>请求运行 QTE 并异步等待成功、失败、超时或取消。</summary>
        UniTask<FilmInteractionResult> RunQteAsync(FilmQteRequest request, System.Threading.CancellationToken cancellationToken);

        /// <summary>把演出期间捕获到的输入转交给当前交互服务；非 QTE 服务可以忽略。</summary>
        void ReceiveInput(in InputFrame frame, InputActionMask actions);
    }

    /// <summary>描述一次等待外部事件的流程请求。</summary>
    public readonly struct FilmEventRequest
    {
        /// <summary>创建带实例编号的事件等待请求。</summary>
        public FilmEventRequest(int instanceId, string eventId)
        {
            InstanceId = instanceId;
            EventId = string.IsNullOrWhiteSpace(eventId) ? throw new ArgumentException("Film event ID cannot be empty.", nameof(eventId)) : eventId;
        }

        /// <summary>获取等待事件的演出实例编号。</summary>
        public int InstanceId { get; }

        /// <summary>获取事件标识。</summary>
        public string EventId { get; }
    }

    /// <summary>提供 FilmSystem 条件分支和外部事件等待所需的运行时流程数据。</summary>
    public sealed class FilmFlowContext
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>设置或替换一个字符串流程变量。</summary>
        public FilmFlowContext Set(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Film variable key cannot be empty.", nameof(key));
            values[key] = value ?? string.Empty;
            return this;
        }

        /// <summary>读取流程变量；变量不存在时返回 false。</summary>
        public bool TryGet(string key, out string value)
        {
            return values.TryGetValue(key, out value);
        }

        /// <summary>导出流程变量副本，避免快照被后续运行时修改。</summary>
        internal IReadOnlyDictionary<string, string> CaptureValues()
        {
            return new Dictionary<string, string>(values, StringComparer.Ordinal);
        }

        /// <summary>从快照变量覆盖当前流程上下文。</summary>
        internal void RestoreValues(IReadOnlyDictionary<string, string> source)
        {
            values.Clear();
            if (source == null) return;
            foreach (KeyValuePair<string, string> pair in source) values[pair.Key] = pair.Value ?? string.Empty;
        }
    }

    /// <summary>抽象外部事件等待端口，正式任务系统或网络系统可注入自己的实现。</summary>
    public interface IFilmFlowService
    {
        /// <summary>异步等待指定演出实例收到外部事件。</summary>
        UniTask<FilmInteractionResult> WaitForEventAsync(FilmEventRequest request, System.Threading.CancellationToken cancellationToken);
    }
}
