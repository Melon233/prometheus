using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Spine.Unity;
using Unity.Cinemachine;
using UnityEngine;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 演出系统：按 <see cref="CutsceneDefinition"/> 播放线性演出。
    ///
    /// 播放期间它同时持有两份租约：
    /// <list type="bullet">
    /// <item>演出镜头租约（<see cref="ICameraSystem.AcquireCutsceneCamera"/>）：系统自建一台 CinemachineCamera，镜头 Action 直接驱动它的 Transform。</item>
    /// <item>输入租约（<see cref="InputContexts.Cutscene"/>）：独占全部动作，玩家角色因此原地停下；确认键推进台词，取消键跳过。</item>
    /// </list>
    /// 演出结束（播完或跳过）时按固定顺序收尾：Finish 全部运行中的 Action → 销毁临时对象 → 恢复玩家可见 → 归还两份租约 → 发布结束事件。
    /// 本系统不认识任务系统；任务通过 <see cref="CutsceneFinishedEvent"/> 得知演出结束。
    /// </summary>
    internal sealed class CutsceneSystem : ICutsceneSystem, IInputReceiver
    {
        /// <summary>演出镜头接管时使用的优先级，必须高于玩法跟随镜头（100）。</summary>
        private const int CutsceneCameraPriority = 1000;

        private readonly ICameraSystem cameraSystem;
        private readonly IInputSystem inputSystem;
        private readonly ITeamSystem teamSystem;
        private readonly IWorldAnchorSystem anchorSystem;

        /// <summary>系统运行时根节点，挂在 PersistentRoot 下。</summary>
        private GameObject systemRoot;

        /// <summary>系统自建的演出镜头；只在播放期间激活。</summary>
        private CinemachineCamera cutsceneCamera;

        /// <summary>当前播放的推进器。</summary>
        private CutscenePlayback playback;

        /// <summary>当前播放的演员（含玩家），收尾时用来销毁生成对象与恢复玩家可见。</summary>
        private Dictionary<string, CutsceneActor> actors;

        /// <summary>本次演出临时对象的父节点。</summary>
        private GameObject propRoot;

        private CutsceneCameraLease cameraLease;
        private ControlLease inputLease;

        /// <summary>本帧收到的确认键（推进台词）。</summary>
        private bool advanceRequested;

        /// <summary>本帧收到的取消键（跳过）。</summary>
        private bool skipRequested;

        private bool isDisposed;

        /// <summary>创建演出系统。</summary>
        /// <param name="cameraSystem">提供演出镜头租约与当前输出镜头位姿。</param>
        /// <param name="inputSystem">提供演出期间的输入独占。</param>
        /// <param name="teamSystem">提供玩家演员（当前上场角色）。</param>
        /// <param name="anchorSystem">按 id 定位演出坐标原点与场景演员。</param>
        public CutsceneSystem(ICameraSystem cameraSystem, IInputSystem inputSystem, ITeamSystem teamSystem, IWorldAnchorSystem anchorSystem)
        {
            this.cameraSystem = cameraSystem ?? throw new ArgumentNullException(nameof(cameraSystem));
            this.inputSystem = inputSystem ?? throw new ArgumentNullException(nameof(inputSystem));
            this.teamSystem = teamSystem ?? throw new ArgumentNullException(nameof(teamSystem));
            this.anchorSystem = anchorSystem ?? throw new ArgumentNullException(nameof(anchorSystem));
        }

        /// <inheritdoc />
        public bool IsPlaying => playback != null;

        /// <inheritdoc />
        public CutsceneDefinition Current { get; private set; }

        /// <inheritdoc />
        public CutsceneLine CurrentLine { get; private set; }

        /// <inheritdoc />
        bool IInputReceiver.IsAlive => !isDisposed;

        /// <inheritdoc />
        public UniTask AfterNewAsync() => UniTask.CompletedTask;

        /// <summary>创建演出镜头。场景锚点由先注册的锚点系统索引。</summary>
        public void AfterNew()
        {
            systemRoot = new GameObject("[CutsceneSystem]");
            systemRoot.transform.SetParent(PersistentRoot.Shared, false);
            GameObject cameraObject = new GameObject("Cutscene Camera");
            cameraObject.transform.SetParent(systemRoot.transform, false);
            cutsceneCamera = cameraObject.AddComponent<CinemachineCamera>();
            // 平时保持停用：Brain 只在两台激活的镜头之间按优先级选择，停用的镜头不会被选中。
            cameraObject.SetActive(false);
        }

        /// <summary>驱动当前演出；处理本帧收到的推进与跳过请求，演出走完时收尾。</summary>
        /// <param name="dt">帧时间（秒）。</param>
        public void OnUpdate(float dt)
        {
            if (playback == null) return;
            if (skipRequested) Skip();
            else if (advanceRequested) Advance();
            if (!playback.IsFinished) playback.Tick(dt);
            if (playback.IsFinished) End(false);
        }

        /// <inheritdoc />
        public void Play(CutsceneDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (playback != null) throw new InvalidOperationException($"演出 '{Current.name}' 尚未结束，不能开始 '{definition.name}'。");
            List<string> errors = new List<string>();
            definition.Validate(errors);
            if (errors.Count > 0) throw new InvalidOperationException($"演出 '{definition.name}' 配置错误：\n{string.Join("\n", errors)}");
            Transform anchor = anchorSystem.Require(definition.AnchorId, $"演出 '{definition.name}'");
            propRoot = new GameObject($"[Cutscene] {definition.name}");
            propRoot.transform.SetParent(systemRoot.transform, false);
            actors = CreateActors(definition, anchor);
            // 镜头从当前画面出发，第一个「从当前位置」的镜头 Action 因此能与玩法镜头平滑衔接。
            Transform output = cameraSystem.OutputCamera.transform;
            cutsceneCamera.transform.SetPositionAndRotation(output.position, output.rotation);
            cutsceneCamera.Lens = cameraSystem.FollowCamera.Lens;
            cutsceneCamera.gameObject.SetActive(true);
            cameraLease = cameraSystem.AcquireCutsceneCamera(cutsceneCamera, CutsceneCameraPriority);
            inputLease = inputSystem.AcquireControl(inputSystem.DefaultSourceId, this, InputActionMask.All, InputContexts.Cutscene);
            Current = definition;
            CutsceneContext context = new CutsceneContext(anchor, cutsceneCamera.transform, propRoot.transform, actors, SetLine);
            playback = new CutscenePlayback(definition, context);
            Core.Event.Invoke(new CutsceneStartedEvent(definition));
            playback.Start();
        }

        /// <inheritdoc />
        public void Advance()
        {
            if (playback == null) throw new InvalidOperationException("当前没有播放中的演出。");
            playback.Advance();
        }

        /// <inheritdoc />
        public void Choose(int optionIndex)
        {
            if (playback == null || !playback.HasPendingOptions) throw new InvalidOperationException("当前台词没有等待选择的选项。");
            if (optionIndex < 0 || optionIndex >= CurrentLine.Options.Count) throw new ArgumentOutOfRangeException(nameof(optionIndex), optionIndex, "选项下标越界。");
            playback.Choose();
        }

        /// <inheritdoc />
        public void Skip()
        {
            if (playback == null) throw new InvalidOperationException("当前没有播放中的演出。");
            if (!Current.Skippable) return;
            playback.Skip();
            End(true);
        }

        /// <summary>每个输入帧开始前清掉上一帧的请求。</summary>
        void IInputReceiver.ResetInput()
        {
            advanceRequested = false;
            skipRequested = false;
        }

        /// <summary>只关心确认（推进）与取消（跳过）；其余动作被独占后丢弃，玩家角色因此不会移动或攻击。</summary>
        void IInputReceiver.ReceiveInput(in InputFrame frame, InputActionMask actions)
        {
            if ((actions & InputActionMask.Submit) != 0 && frame.Submit.PressedThisFrame) advanceRequested = true;
            if ((actions & InputActionMask.Cancel) != 0 && frame.Cancel.PressedThisFrame) skipRequested = true;
        }

        /// <summary>
        /// 会话销毁时若仍在播放，只归还租约并销毁自建对象（临时演员与特效都在系统根下，随之销毁）。
        /// 不走 <see cref="Cleanup"/>：进程退出时场景对象可能已被 Unity 销毁，Action 落位与恢复玩家可见都会访问它们（ARCH-LIFE-004）；
        /// 也不发布事件，订阅者可能已经释放。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed) return;
            cameraLease?.Dispose();
            inputLease?.Dispose();
            cameraLease = null;
            inputLease = null;
            playback = null;
            actors = null;
            propRoot = null;
            Current = null;
            CurrentLine = null;
            if (systemRoot != null) UnityEngine.Object.Destroy(systemRoot);
            systemRoot = null;
            cutsceneCamera = null;
            isDisposed = true;
        }

        /// <summary>结束当前演出并发布结束事件。</summary>
        /// <param name="skipped">是否由玩家跳过。</param>
        private void End(bool skipped)
        {
            CutsceneDefinition finished = Current;
            Cleanup();
            Core.Event.Invoke(new CutsceneFinishedEvent(finished, skipped));
        }

        /// <summary>收尾：Action 落位 → 销毁临时对象 → 恢复玩家可见 → 归还租约 → 清空播放状态。</summary>
        private void Cleanup()
        {
            playback.FinishRunning();
            foreach (CutsceneActor actor in actors.Values)
            {
                if (actor.IsPlayer) actor.SetVisible(true);
            }
            UnityEngine.Object.Destroy(propRoot);
            cameraLease.Dispose();
            inputLease.Dispose();
            cutsceneCamera.gameObject.SetActive(false);
            SetLine(null);
            cameraLease = null;
            inputLease = null;
            propRoot = null;
            actors = null;
            playback = null;
            Current = null;
        }

        /// <summary>更新当前台词并广播变化。</summary>
        /// <param name="line">新台词；为空表示清除。</param>
        private void SetLine(CutsceneLine line)
        {
            if (CurrentLine == line) return;
            CurrentLine = line;
            Core.Event.Invoke(new CutsceneLineChangedEvent(line));
        }

        /// <summary>按演员表建立本次播放的演员句柄；Spawn 来源在临时对象根下生成。</summary>
        private Dictionary<string, CutsceneActor> CreateActors(CutsceneDefinition definition, Transform anchor)
        {
            Dictionary<string, CutsceneActor> result = new Dictionary<string, CutsceneActor>(StringComparer.Ordinal);
            foreach (CutsceneActorDef def in definition.Actors)
            {
                switch (def.Source)
                {
                    case CutsceneActorSource.Player:
                        Entity player = teamSystem.ActiveMember ?? throw new InvalidOperationException($"演出 '{definition.name}' 需要玩家演员，但当前没有上场角色。");
                        result.Add(def.Key, new CutsceneActor(def.Key, player.bindGo.transform, null, player, false));
                        break;
                    case CutsceneActorSource.Scene:
                        Transform sceneRoot = anchorSystem.Require(def.AnchorId, $"演出 '{definition.name}' 的演员 '{def.Key}'");
                        result.Add(def.Key, new CutsceneActor(def.Key, sceneRoot, sceneRoot.GetComponentInChildren<SkeletonAnimation>(true), null, false));
                        break;
                    case CutsceneActorSource.Spawn:
                        GameObject instance = UnityEngine.Object.Instantiate(def.SpawnPrefab, anchor.TransformPoint(def.SpawnPosition), Quaternion.identity, propRoot.transform);
                        CutsceneActor spawned = new CutsceneActor(def.Key, instance.transform, instance.GetComponentInChildren<SkeletonAnimation>(true), null, true);
                        spawned.SetFaceRight(def.SpawnFaceRight);
                        result.Add(def.Key, spawned);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(def.Source), def.Source, "未知的演员来源。");
                }
            }
            return result;
        }
    }
}
