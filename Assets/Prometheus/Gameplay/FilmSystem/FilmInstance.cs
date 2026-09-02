using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Film
{
    /// <summary>持有一次 Timeline 演出的全部可变状态和对称清理逻辑。</summary>
    internal sealed class FilmInstance
    {
        /// <summary>保存拥有当前实例的 FilmSystem，用于实例结束回收活动引用。</summary>
        private readonly FilmSystem owner;

        /// <summary>保存用于演出期间输入接管的单局输入系统。</summary>
        private readonly IInputSystem inputSystem;

        /// <summary>保存用于演出镜头优先级租约的单局相机系统。</summary>
        private readonly ICameraSystem cameraSystem;

        /// <summary>保存对话和 QTE 的异步交互服务。</summary>
        private readonly IFilmInteractionService interactionService;

        /// <summary>保存当前演出实例运行时对象的父节点。</summary>
        private readonly Transform runtimeRoot;

        /// <summary>保存当前实例用于条件分支读取的流程变量。</summary>
        private readonly FilmFlowContext flowContext;

        /// <summary>保存等待演出结束的异步完成源。</summary>
        private readonly UniTaskCompletionSource<FilmStopReason> completionSource = new UniTaskCompletionSource<FilmStopReason>();

        /// <summary>保存当前实例动态创建的 GameObject 和 PlayableDirector。</summary>
        private GameObject runtimeObject;

        /// <summary>保存当前实例的 Timeline 播放器。</summary>
        private PlayableDirector director;

        /// <summary>保存吞掉普通玩法输入的演出接收器。</summary>
        private FilmInputReceiver inputReceiver;

        /// <summary>保存演出期间持有的输入控制租约。</summary>
        private ControlLease inputLease;

        /// <summary>保存演出期间持有的镜头优先级租约。</summary>
        private FilmCameraLease cameraLease;

        /// <summary>保存运行时对象销毁前的最终 Timeline 时间。</summary>
        private double finalTime;

        /// <summary>标记实例是否已执行过唯一一次终态清理。</summary>
        private bool finalized;

        /// <summary>保存按时间排序的 Timeline 交互 Marker。</summary>
        private readonly List<IMarker> timelineMarkers = new List<IMarker>();

        /// <summary>保存下一个尚未触发的交互 Marker 索引。</summary>
        private int nextInteractionIndex;

        /// <summary>保存本实例启动时注入的绑定，用于嵌套演出结束后恢复父实例租约。</summary>
        private FilmBindingContext bindingContext;

        /// <summary>保存当前正在等待的交互 Marker。</summary>
        private FilmInteractionMarker waitingMarker;

        /// <summary>累计当前交互等待时间，用于处理 QTE 超时。</summary>
        private float interactionElapsed;

        /// <summary>取消正在等待的对话或 QTE，确保停止路径不会遗留异步任务。</summary>
        private CancellationTokenSource interactionCancellation;

        /// <summary>创建一个尚未绑定和播放的演出实例。</summary>
        internal FilmInstance(FilmSystem owner, int instanceId, FilmDefinition definition, IInputSystem inputSystem, ICameraSystem cameraSystem, IFilmInteractionService interactionService, FilmFlowContext flowContext, Transform runtimeRoot)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InstanceId = instanceId;
            Definition = definition != null ? definition : throw new ArgumentNullException(nameof(definition));
            this.inputSystem = inputSystem ?? throw new ArgumentNullException(nameof(inputSystem));
            this.cameraSystem = cameraSystem ?? throw new ArgumentNullException(nameof(cameraSystem));
            this.interactionService = interactionService ?? throw new ArgumentNullException(nameof(interactionService));
            this.flowContext = flowContext ?? new FilmFlowContext();
            this.runtimeRoot = runtimeRoot != null ? runtimeRoot : throw new ArgumentNullException(nameof(runtimeRoot));
            State = FilmState.Created;
        }

        /// <summary>获取 FilmSystem 分配的实例编号。</summary>
        internal int InstanceId { get; }

        /// <summary>获取当前实例使用的静态演出配置。</summary>
        internal FilmDefinition Definition { get; }

        /// <summary>获取当前演出生命周期状态。</summary>
        internal FilmState State { get; private set; }

        /// <summary>获取演出的最终停止原因。</summary>
        internal FilmStopReason StopReason { get; private set; }

        /// <summary>获取仍在运行的 Director 时间，或运行时对象释放前记录的最终时间。</summary>
        internal double Time => director != null ? director.time : finalTime;

        /// <summary>创建 Director、完成全部必需绑定并申请演出期间需要的系统租约。</summary>
        internal void Prepare(FilmBindingContext bindingContext)
        {
            if (State != FilmState.Created) throw new InvalidOperationException($"Film instance {InstanceId} can only be prepared from Created state.");
            Definition.Validate();
            bindingContext = bindingContext ?? new FilmBindingContext();
            this.bindingContext = bindingContext;
            State = FilmState.Binding;
            try
            {
                runtimeObject = new GameObject($"Film_{Definition.FilmId}_{InstanceId}");
                runtimeObject.transform.SetParent(runtimeRoot, false);
                director = runtimeObject.AddComponent<PlayableDirector>();
                director.playableAsset = Definition.Timeline;
                director.extrapolationMode = Definition.WrapMode;
                director.timeUpdateMode = Definition.UpdateMode;
                director.stopped += OnDirectorStopped;
                BindTimelineOutputs(bindingContext);
                CollectInteractionMarkers();
                AcquireRuntimeLeases(bindingContext);
                State = FilmState.Ready;
            }
            catch
            {
                State = FilmState.Failed;
                StopReason = FilmStopReason.Failed;
                FinalizeInstance();
                throw;
            }
        }

        /// <summary>从 Ready 状态启动 Timeline；阶段一不允许同一实例重复播放。</summary>
        internal void Play()
        {
            if (State != FilmState.Ready) throw new InvalidOperationException($"Film instance {InstanceId} can only play from Ready state.");
            State = FilmState.Playing;
            nextInteractionIndex = 0;
            waitingMarker = null;
            interactionElapsed = 0f;
            director.Play();
        }

        /// <summary>从快照恢复 Timeline 时间和流程变量后继续播放。</summary>
        internal void Play(FilmPlaybackSnapshot snapshot)
        {
            if (snapshot.FilmId != Definition.FilmId) throw new InvalidOperationException($"Snapshot film '{snapshot.FilmId}' does not match definition '{Definition.FilmId}'.");
            flowContext.RestoreValues(snapshot.FlowValues);
            director.time = Math.Max(0d, Math.Min(Definition.Timeline.duration, snapshot.Time));
            director.Evaluate();
            nextInteractionIndex = 0;
            while (nextInteractionIndex < timelineMarkers.Count && timelineMarkers[nextInteractionIndex].time <= director.time) nextInteractionIndex++;
            State = FilmState.Playing;
            director.Play();
        }

        /// <summary>推进 Marker 检测，并在 Timeline 到达交互点时启动异步等待。</summary>
        internal void OnUpdate(float dt)
        {
            if (finalized) return;
            if (State == FilmState.WaitingForInteraction)
            {
                interactionElapsed += Math.Max(0f, dt);
                if (waitingMarker != null && waitingMarker.InteractionType == FilmInteractionType.Qte && waitingMarker.QteTimeoutSeconds > 0f && interactionElapsed >= waitingMarker.QteTimeoutSeconds) Stop(FilmStopReason.InteractionFailed);
                return;
            }
            if (State != FilmState.Playing || nextInteractionIndex >= timelineMarkers.Count) return;
            IMarker marker = timelineMarkers[nextInteractionIndex];
            if (marker.time <= director.time) BeginMarkerAsync(marker).Forget();
        }

        /// <summary>暂停正在播放的 Timeline，并保留输入与镜头租约。</summary>
        internal void Pause()
        {
            if (State == FilmState.Paused) return;
            if (State != FilmState.Playing) throw new InvalidOperationException($"Film instance {InstanceId} can only pause while Playing.");
            director.Pause();
            State = FilmState.Paused;
        }

        /// <summary>恢复当前实例主动暂停的 Timeline。</summary>
        internal void Resume()
        {
            if (State != FilmState.Paused) throw new InvalidOperationException($"Film instance {InstanceId} can only resume from Paused state.");
            director.Resume();
            State = FilmState.Playing;
        }

        /// <summary>停止尚未结束的演出，并保证所有结束路径共享同一套清理逻辑。</summary>
        internal void Stop(FilmStopReason reason)
        {
            if (finalized) return;
            if (reason == FilmStopReason.None || reason == FilmStopReason.Completed || reason == FilmStopReason.Failed) throw new ArgumentOutOfRangeException(nameof(reason), reason, "Stop requires an explicit interruption reason.");
            StopReason = reason;
            State = FilmState.Stopping;
            interactionCancellation?.Cancel();
            if (director != null) director.Stop();
            if (!finalized) CompleteStoppedState();
        }

        /// <summary>按定义策略将 Timeline 推到结尾并以 Skipped 原因结束。</summary>
        internal void Skip()
        {
            if (Definition.SkipMode == FilmSkipMode.None) throw new InvalidOperationException($"Film '{Definition.FilmId}' does not allow skipping.");
            if (finalized) return;
            if (director != null)
            {
                director.time = Definition.Timeline.duration;
                finalTime = director.time;
            }
            Stop(FilmStopReason.Skipped);
        }

        /// <summary>创建当前实例的可持久化播放快照，并通知外部同步监听者。</summary>
        internal FilmPlaybackSnapshot CaptureSnapshot()
        {
            FilmPlaybackSnapshot snapshot = new FilmPlaybackSnapshot(Definition.FilmId, InstanceId, Time, State, flowContext.CaptureValues());
            owner.NotifySnapshotCaptured(snapshot);
            return snapshot;
        }

        /// <summary>异步等待实例进入任意终止状态。</summary>
        internal UniTask<FilmStopReason> WaitForCompletionAsync()
        {
            return completionSource.Task;
        }

        /// <summary>根据 Timeline 的输出轨道名称设置 Generic Binding，并校验必需绑定及目标类型。</summary>
        private void BindTimelineOutputs(FilmBindingContext bindingContext)
        {
            Dictionary<string, PlayableBinding> outputs = new Dictionary<string, PlayableBinding>(StringComparer.Ordinal);
            foreach (PlayableBinding output in Definition.Timeline.outputs)
            {
                if (string.IsNullOrWhiteSpace(output.streamName)) continue;
                if (!outputs.TryAdd(output.streamName, output)) throw new InvalidOperationException($"Film '{Definition.FilmId}' Timeline contains duplicate output stream name '{output.streamName}'.");
            }
            IReadOnlyList<FilmBindingDefinition> definitions = Definition.Bindings;
            for (int index = 0; index < definitions.Count; index++)
            {
                FilmBindingDefinition definition = definitions[index];
                if (!bindingContext.TryGet(definition.Key, out UnityEngine.Object target))
                {
                    if (definition.Required) throw new InvalidOperationException($"Film '{Definition.FilmId}' requires runtime binding '{definition.Key}'.");
                    continue;
                }
                if (outputs.TryGetValue(definition.Key, out PlayableBinding output)) BindTimelineOutput(output, target);
                else if (definition.Role == FilmBindingRole.Generic) throw new InvalidOperationException($"Film '{Definition.FilmId}' binding '{definition.Key}' does not match any Timeline output stream name.");
            }
        }

        /// <summary>校验目标类型后把一个 Timeline 输出轨道绑定到当前演出实例。</summary>
        private void BindTimelineOutput(PlayableBinding output, UnityEngine.Object target)
        {
            Type outputType = output.outputTargetType;
            if (outputType != null && !outputType.IsInstanceOfType(target)) throw new InvalidOperationException($"Film '{Definition.FilmId}' binding '{output.streamName}' expects {outputType.Name}, but received {target.GetType().Name}.");
            director.SetGenericBinding(output.sourceObject, target);
        }

        /// <summary>读取并按时间排序 Timeline 顶层 Marker，启动前校验每个交互配置。</summary>
        private void CollectInteractionMarkers()
        {
            timelineMarkers.Clear();
            MarkerTrack markerTrack = Definition.Timeline.markerTrack;
            if (markerTrack == null) return;
            foreach (IMarker marker in markerTrack.GetMarkers())
            {
                if (marker is FilmInteractionMarker interactionMarker) interactionMarker.Validate();
                else if (marker is FilmBranchMarker branchMarker) branchMarker.Validate();
                else if (marker is FilmWaitEventMarker waitMarker) waitMarker.Validate();
                else if (marker is FilmSubFilmMarker subFilmMarker) subFilmMarker.Validate();
                else if (marker is FilmParallelMarker parallelMarker) parallelMarker.Validate();
                else continue;
                timelineMarkers.Add(marker);
            }
            timelineMarkers.Sort((left, right) => left.time.CompareTo(right.time));
        }

        /// <summary>分发一个到达时间点的 Timeline Marker，并确保每个 Marker 只触发一次。</summary>
        private async UniTaskVoid BeginMarkerAsync(IMarker marker)
        {
            if (marker is FilmInteractionMarker interactionMarker)
            {
                await BeginInteractionAsync(interactionMarker);
                return;
            }
            if (marker is FilmBranchMarker branchMarker)
            {
                ApplyBranch(branchMarker);
                nextInteractionIndex++;
                return;
            }
            if (marker is FilmWaitEventMarker waitEventMarker)
            {
                await BeginWaitEventAsync(waitEventMarker);
                return;
            }
            if (marker is FilmSubFilmMarker subFilmMarker)
            {
                await BeginSubFilmAsync(subFilmMarker);
                return;
            }
            if (marker is FilmParallelMarker parallelMarker) await BeginParallelAsync(parallelMarker);
        }

        /// <summary>按流程变量将 Timeline 跳转到条件成立或不成立的目标时间。</summary>
        private void ApplyBranch(FilmBranchMarker marker)
        {
            flowContext.TryGet(marker.VariableKey, out string value);
            director.time = string.Equals(value, marker.ExpectedValue, StringComparison.Ordinal) ? marker.TrueTime : marker.FalseTime;
            director.Evaluate();
        }

        /// <summary>暂停 Timeline 并等待外部事件服务返回结果。</summary>
        private async UniTask BeginWaitEventAsync(FilmWaitEventMarker marker)
        {
            if (!(interactionService is IFilmFlowService flowService)) throw new InvalidOperationException($"Film '{Definition.FilmId}' requires an IFilmFlowService for event '{marker.EventId}'.");
            State = FilmState.WaitingForInteraction;
            director.Pause();
            interactionCancellation?.Dispose();
            interactionCancellation = new CancellationTokenSource();
            try
            {
                FilmInteractionResult result = await flowService.WaitForEventAsync(new FilmEventRequest(InstanceId, marker.EventId), interactionCancellation.Token);
                if (!result.Succeeded) Stop(FilmStopReason.InteractionFailed);
                else
                {
                    nextInteractionIndex++;
                    State = FilmState.Playing;
                    director.Play();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                interactionCancellation?.Dispose();
                interactionCancellation = null;
            }
        }

        /// <summary>暂停父 Timeline，启动一个子演出并在子演出结束后恢复父实例。</summary>
        private async UniTask BeginSubFilmAsync(FilmSubFilmMarker marker)
        {
            State = FilmState.WaitingForInteraction;
            director.Pause();
            FilmStopReason reason = await owner.PlayNestedAsync(this, marker.Definition, bindingContext);
            if (finalized || State == FilmState.Stopping) return;
            if (reason != FilmStopReason.Completed) Stop(FilmStopReason.InteractionFailed);
            else
            {
                nextInteractionIndex++;
                State = FilmState.Playing;
                director.Play();
            }
        }

        /// <summary>暂停父 Timeline，并等待所有并行子演出完成后恢复父实例。</summary>
        private async UniTask BeginParallelAsync(FilmParallelMarker marker)
        {
            State = FilmState.WaitingForInteraction;
            director.Pause();
            FilmStopReason[] reasons = await owner.PlayParallelAsync(this, marker.Definitions, bindingContext);
            if (finalized || State == FilmState.Stopping) return;
            for (int index = 0; index < reasons.Length; index++)
            {
                if (reasons[index] != FilmStopReason.Completed)
                {
                    Stop(FilmStopReason.InteractionFailed);
                    return;
                }
            }
            nextInteractionIndex++;
            State = FilmState.Playing;
            director.Play();
        }

        /// <summary>暂停 Timeline，调用交互服务并在成功后恢复；取消和失败不会恢复时间轴。</summary>
        private async UniTask BeginInteractionAsync(FilmInteractionMarker marker)
        {
            if (State != FilmState.Playing || waitingMarker != null) return;
            waitingMarker = marker;
            interactionElapsed = 0f;
            State = FilmState.WaitingForInteraction;
            director.Pause();
            interactionCancellation?.Dispose();
            interactionCancellation = new CancellationTokenSource();
            FilmInteractionResult result;
            try
            {
                result = marker.InteractionType == FilmInteractionType.Dialogue ? await interactionService.ShowDialogueAsync(new FilmDialogueRequest(InstanceId, marker.InteractionId), interactionCancellation.Token) : await interactionService.RunQteAsync(new FilmQteRequest(InstanceId, marker.InteractionId, marker.QteSuccessActions, marker.QteTimeoutSeconds), interactionCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                interactionCancellation?.Dispose();
                interactionCancellation = null;
            }
            if (finalized || State == FilmState.Stopping) return;
            waitingMarker = null;
            if (!result.Succeeded)
            {
                Stop(FilmStopReason.InteractionFailed);
                return;
            }
            nextInteractionIndex++;
            State = FilmState.Playing;
            director.Play();
        }

        /// <summary>根据演出配置申请输入接管和演出镜头优先级，并把租约统一交给实例清理。</summary>
        private void AcquireRuntimeLeases(FilmBindingContext bindingContext)
        {
            if (Definition.LockGameplayInput)
            {
                inputReceiver = new FilmInputReceiver(OnFilmInput);
                inputLease = inputSystem.AcquireControl(inputSystem.DefaultSourceId, inputReceiver, InputActionMask.All, InputContexts.Cutscene);
            }
            IReadOnlyList<FilmBindingDefinition> definitions = Definition.Bindings;
            for (int index = 0; index < definitions.Count; index++)
            {
                FilmBindingDefinition definition = definitions[index];
                if (definition.Role != FilmBindingRole.FilmCamera || !bindingContext.TryGet(definition.Key, out UnityEngine.Object target)) continue;
                if (cameraLease != null) throw new InvalidOperationException($"Film '{Definition.FilmId}' can declare only one FilmCamera binding in phase one.");
                if (!(target is CinemachineCamera filmCamera)) throw new InvalidOperationException($"Film '{Definition.FilmId}' FilmCamera binding '{definition.Key}' requires a CinemachineCamera.");
                cameraLease = cameraSystem.AcquireFilmCamera(filmCamera, Definition.FilmCameraPriority);
            }
        }

        /// <summary>释放父演出暂时不需要的输入和镜头租约，为子演出让出系统控制权。</summary>
        internal void SuspendExternalLeases()
        {
            cameraLease?.Dispose();
            inputLease?.Dispose();
            inputReceiver?.Invalidate();
            cameraLease = null;
            inputLease = null;
            inputReceiver = null;
        }

        /// <summary>子演出结束后重新申请父演出原有的输入和镜头租约。</summary>
        internal void ResumeExternalLeases()
        {
            if (!finalized) AcquireRuntimeLeases(bindingContext);
        }

        /// <summary>把演出接管的输入转交给交互服务，由 QTE 服务决定是否命中。</summary>
        private void OnFilmInput(InputFrame frame, InputActionMask actions)
        {
            if (State == FilmState.WaitingForInteraction && waitingMarker != null && waitingMarker.InteractionType == FilmInteractionType.Qte) interactionService.ReceiveInput(frame, actions);
        }

        /// <summary>处理 PlayableDirector 自然结束或 Stop 引发的统一回调。</summary>
        private void OnDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (finalized || !ReferenceEquals(director, stoppedDirector)) return;
            if (State == FilmState.Stopping)
            {
                CompleteStoppedState();
                return;
            }
            StopReason = FilmStopReason.Completed;
            State = FilmState.Completed;
            FinalizeInstance();
        }

        /// <summary>把主动停止或系统释放统一转换为 Stopped 终态。</summary>
        private void CompleteStoppedState()
        {
            State = FilmState.Stopped;
            FinalizeInstance();
        }

        /// <summary>记录最终时间、释放全部租约和运行时对象，并只通知 FilmSystem 一次。</summary>
        private void FinalizeInstance()
        {
            if (finalized) return;
            finalized = true;
            if (director != null)
            {
                if (StopReason != FilmStopReason.Skipped) finalTime = director.time;
                director.stopped -= OnDirectorStopped;
            }
            cameraLease?.Dispose();
            inputLease?.Dispose();
            if (inputReceiver != null) inputReceiver.Invalidate();
            interactionCancellation?.Cancel();
            interactionCancellation?.Dispose();
            interactionCancellation = null;
            cameraLease = null;
            inputLease = null;
            inputReceiver = null;
            if (runtimeObject != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(runtimeObject);
                else UnityEngine.Object.DestroyImmediate(runtimeObject);
            }
            director = null;
            runtimeObject = null;
            completionSource.TrySetResult(StopReason);
            owner.OnInstanceFinished(this);
        }

        /// <summary>接收并吞掉 Cutscene 上下文获得的输入，使普通玩法绑定在演出期间不再收到动作。</summary>
        private sealed class FilmInputReceiver : IInputReceiver
        {
            private readonly Action<InputFrame, InputActionMask> onInput;

            /// <summary>创建一个把输入交给当前交互服务的接收器。</summary>
            internal FilmInputReceiver(Action<InputFrame, InputActionMask> onInput)
            {
                this.onInput = onInput ?? throw new ArgumentNullException(nameof(onInput));
            }

            /// <summary>获取当前接收器是否仍属于一个活动演出实例。</summary>
            public bool IsAlive { get; private set; } = true;

            /// <summary>演出输入屏蔽器没有跨帧状态，因此每帧重置不执行额外操作。</summary>
            public void ResetInput()
            {
            }

            /// <summary>阶段一只接管并吞掉输入；后续 QTE 节点将在独立接收器中解释具体动作。</summary>
            public void ReceiveInput(in InputFrame frame, InputActionMask actions)
            {
                onInput(frame, actions);
            }

            /// <summary>标记接收器失效，使 InputSystem 不再向已经结束的演出分发输入。</summary>
            public void Invalidate()
            {
                IsAlive = false;
            }
        }
    }
}
