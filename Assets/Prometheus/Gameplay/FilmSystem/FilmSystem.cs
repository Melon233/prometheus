using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Film
{
    /// <summary>集中创建和管理单局 Timeline 演出实例，并负责输入、镜头和实例生命周期的对称清理。</summary>
    internal sealed class FilmSystem : XSystem, IFilmSystem
    {
        /// <summary>保存系统运行时根节点，保证演出对象与 GameplayKit 同生命周期。</summary>
        private readonly Transform runtimeRoot;
        private GameObject systemRoot;
        private IInputSystem inputSystem;
        private ICameraSystem cameraSystem;
        /// <summary>保存对话和 QTE 的异步交互端口，默认使用可手动完成的服务。</summary>
        private readonly IFilmInteractionService interactionService;
        /// <summary>保存按父子关系和优先级排列的活动演出实例。</summary>
        private readonly List<FilmInstance> activeInstances = new List<FilmInstance>();
        /// <summary>保存下一个演出实例使用的单调递增编号。</summary>
        private int nextInstanceId = 1;

        /// <summary>标记系统是否已释放，防止释放后继续创建演出。</summary>
        private bool isDisposed;

        /// <summary>快照生成通知，存档或网络层可订阅并自行序列化。</summary>
        public event Action<FilmPlaybackSnapshot> SnapshotCaptured;

        /// <summary>创建一个把全部演出运行时对象挂载到指定单局根节点的 FilmSystem。</summary>
        /// <param name="runtimeRoot">与 GameplayKit 生命周期一致的常驻运行时根节点。</param>
        public FilmSystem(Transform runtimeRoot)
            : this(runtimeRoot, null)
        {
        }

        /// <summary>创建一个可注入正式对话/QTE 服务的 FilmSystem。</summary>
        /// <param name="runtimeRoot">与 GameplayKit 生命周期一致的常驻根节点。</param>
        /// <param name="interactionService">交互服务；为空时使用 ManualFilmInteractionService。</param>
        public FilmSystem(Transform runtimeRoot, IFilmInteractionService interactionService)
        {
            this.runtimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
            this.interactionService = interactionService ?? new ManualFilmInteractionService();
        }

        /// <summary>获取当前是否存在尚未结束的前台演出。</summary>
        public bool IsPlaying => activeInstance != null;

        /// <summary>获取当前前台演出句柄；没有活动演出时返回 null。</summary>
        public FilmHandle ActiveFilm => activeInstance != null ? new FilmHandle(activeInstance) : null;

        /// <summary>获取当前栈顶活动演出实例。</summary>
        private FilmInstance activeInstance => activeInstances.Count == 0 ? null : activeInstances[activeInstances.Count - 1];

        /// <summary>获取当前 FilmSystem 使用的对话/QTE 交互服务。</summary>
        public IFilmInteractionService InteractionService => interactionService;

        /// <summary>取得输入与镜头系统，并创建当前单局独占的演出运行时根节点。</summary>
        public override void AfterNew()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(FilmSystem));
            inputSystem = Core.Gameplay.GetSystem<IInputSystem>();
            cameraSystem = Core.Gameplay.GetSystem<ICameraSystem>();
            systemRoot = new GameObject("[FilmSystem]");
            systemRoot.transform.SetParent(runtimeRoot, false);
        }

        /// <summary>绑定并启动一段前台演出；阶段一明确拒绝多个演出并行。</summary>
        /// <param name="definition">需要播放的静态演出配置。</param>
        /// <param name="bindings">当前实例使用的场景和系统对象绑定。</param>
        /// <returns>用于观察、暂停、恢复和停止该实例的句柄。</returns>
        public FilmHandle Play(FilmDefinition definition, FilmBindingContext bindings = null, FilmFlowContext flowContext = null)
        {
            ThrowIfUnavailable();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (activeInstance != null)
            {
                if (definition.Priority <= activeInstance.Definition.Priority) throw new InvalidOperationException($"Film '{definition.FilmId}' cannot start while higher or equal priority film '{activeInstance.Definition.FilmId}' is running.");
                StopAll(FilmStopReason.Replaced);
            }
            FilmInstance instance = CreateInstance(definition, bindings, flowContext ?? new FilmFlowContext());
            try
            {
                instance.Prepare(bindings);
                instance.Play();
                return new FilmHandle(instance);
            }
            catch
            {
                activeInstances.Remove(instance);
                throw;
            }
        }

        /// <summary>根据快照创建并恢复一段顶层演出。</summary>
        public FilmHandle PlayFromSnapshot(FilmDefinition definition, FilmBindingContext bindings, FilmPlaybackSnapshot snapshot)
        {
            ThrowIfUnavailable();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (snapshot.FilmId != definition.FilmId) throw new InvalidOperationException($"Snapshot film '{snapshot.FilmId}' does not match definition '{definition.FilmId}'.");
            if (activeInstance != null) StopAll(FilmStopReason.Replaced);
            FilmInstance instance = CreateInstance(definition, bindings, new FilmFlowContext());
            try
            {
                instance.Prepare(bindings);
                instance.Play(snapshot);
                return new FilmHandle(instance);
            }
            catch
            {
                activeInstances.Remove(instance);
                throw;
            }
        }

        /// <summary>启动父演出的单个嵌套子演出，并等待子演出结束后恢复父实例租约。</summary>
        internal async UniTask<FilmStopReason> PlayNestedAsync(FilmInstance parent, FilmDefinition definition, FilmBindingContext bindings)
        {
            parent.SuspendExternalLeases();
            FilmInstance child = CreateInstance(definition, bindings, new FilmFlowContext());
            try
            {
                child.Prepare(bindings);
                child.Play();
                FilmStopReason reason = await child.WaitForCompletionAsync();
                parent.ResumeExternalLeases();
                return reason;
            }
            catch
            {
                child.Stop(FilmStopReason.InteractionFailed);
                parent.ResumeExternalLeases();
                throw;
            }
        }

        /// <summary>并行启动多个子演出；子演出必须自行避免输入和镜头租约冲突。</summary>
        internal async UniTask<FilmStopReason[]> PlayParallelAsync(FilmInstance parent, IReadOnlyList<FilmDefinition> definitions, FilmBindingContext bindings)
        {
            parent.SuspendExternalLeases();
            List<UniTask<FilmStopReason>> tasks = new List<UniTask<FilmStopReason>>(definitions.Count);
            List<FilmInstance> children = new List<FilmInstance>(definitions.Count);
            try
            {
                for (int index = 0; index < definitions.Count; index++)
                {
                    FilmInstance child = CreateInstance(definitions[index], bindings, new FilmFlowContext());
                    children.Add(child);
                    child.Prepare(bindings);
                    child.Play();
                    tasks.Add(child.WaitForCompletionAsync());
                }
                FilmStopReason[] reasons = await UniTask.WhenAll(tasks);
                parent.ResumeExternalLeases();
                return reasons;
            }
            catch
            {
                for (int index = 0; index < children.Count; index++) children[index].Stop(FilmStopReason.InteractionFailed);
                parent.ResumeExternalLeases();
                throw;
            }
        }

        /// <summary>停止当前前台演出；没有活动演出时保持幂等。</summary>
        public void StopCurrent()
        {
            activeInstance?.Stop(FilmStopReason.Requested);
        }

        /// <summary>停止当前全部演出实例，供高优先级抢占和系统销毁使用。</summary>
        private void StopAll(FilmStopReason reason)
        {
            FilmInstance[] snapshot = activeInstances.ToArray();
            for (int index = snapshot.Length - 1; index >= 0; index--) snapshot[index].Stop(reason);
        }

        /// <summary>在 GameplayKit 每帧更新阶段推进 Timeline Marker 检测和交互超时。</summary>
        /// <param name="dt">当前玩法帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            if (isDisposed) return;
            FilmInstance[] snapshot = activeInstances.ToArray();
            for (int index = 0; index < snapshot.Length; index++) snapshot[index].OnUpdate(dt);
        }

        /// <summary>停止活动演出并销毁系统运行时根节点。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            StopAll(FilmStopReason.SystemDisposed);
            activeInstances.Clear();
            if (systemRoot != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(systemRoot);
                else UnityEngine.Object.DestroyImmediate(systemRoot);
            }
            systemRoot = null;
            inputSystem = null;
            cameraSystem = null;
        }

        /// <summary>接收实例的唯一结束通知，并在匹配当前前台实例时解除占用。</summary>
        internal void OnInstanceFinished(FilmInstance instance)
        {
            activeInstances.Remove(instance);
        }

        /// <summary>向外部同步层转发实例快照，不在 FilmSystem 内部承担序列化或传输职责。</summary>
        internal void NotifySnapshotCaptured(FilmPlaybackSnapshot snapshot)
        {
            SnapshotCaptured?.Invoke(snapshot);
        }

        /// <summary>创建并注册一个活动演出实例，统一处理实例编号和父节点挂载。</summary>
        private FilmInstance CreateInstance(FilmDefinition definition, FilmBindingContext bindings, FilmFlowContext flowContext)
        {
            FilmInstance instance = new FilmInstance(this, nextInstanceId++, definition, inputSystem, cameraSystem, interactionService, flowContext, systemRoot.transform);
            activeInstances.Add(instance);
            return instance;
        }

        /// <summary>确保 FilmSystem 已完成 AfterNew 且没有进入释放状态。</summary>
        private void ThrowIfUnavailable()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(FilmSystem));
            if (systemRoot == null || inputSystem == null || cameraSystem == null) throw new InvalidOperationException("FilmSystem must complete AfterNew before playing a film.");
        }
    }
}
