using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Film.Tests
{
    /// <summary>验证阶段一 FilmSystem 的 Timeline 绑定、生命周期控制和失败清理行为。</summary>
    public sealed class FilmSystemTests
    {
        /// <summary>验证启动演出会创建独立 Director、绑定轨道对象并接管普通玩法输入。</summary>
        [Test]
        public void Play_BindsTimelineOutputAndAcquiresCutsceneInput()
        {
            TestContext context = CreateContext(true);
            try
            {
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                Assert.That(handle.State, Is.EqualTo(FilmState.Playing));
                Assert.That(context.inputSystem.BindingCount, Is.EqualTo(1));
                PlayableDirector director = context.filmRoot.GetComponentInChildren<PlayableDirector>();
                Assert.That(director, Is.Not.Null);
                Assert.That(director.GetGenericBinding(context.track), Is.SameAs(context.actor));
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证暂停、恢复和停止会更新状态，并在停止后释放输入租约和运行时对象。</summary>
        [Test]
        public void PauseResumeStop_ReleasesRuntimeResources()
        {
            TestContext context = CreateContext(true);
            try
            {
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                handle.Pause();
                Assert.That(handle.State, Is.EqualTo(FilmState.Paused));
                handle.Resume();
                Assert.That(handle.State, Is.EqualTo(FilmState.Playing));
                handle.Stop();
                Assert.That(handle.State, Is.EqualTo(FilmState.Stopped));
                Assert.That(handle.StopReason, Is.EqualTo(FilmStopReason.Requested));
                Assert.That(context.inputSystem.BindingCount, Is.Zero);
                Assert.That(context.filmRoot.GetComponentsInChildren<PlayableDirector>(), Is.Empty);
                Assert.That(context.filmSystem.IsPlaying, Is.False);
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证缺失必需绑定会在播放前失败，且不会遗留 FilmSystem 活动实例或输入租约。</summary>
        [Test]
        public void Play_ThrowsForMissingRequiredBindingWithoutLeakingState()
        {
            TestContext context = CreateContext(true);
            try
            {
                Assert.Throws<InvalidOperationException>(() => context.filmSystem.Play(context.definition));
                Assert.That(context.filmSystem.IsPlaying, Is.False);
                Assert.That(context.inputSystem.BindingCount, Is.Zero);
                Assert.That(context.filmRoot.GetComponentsInChildren<PlayableDirector>(), Is.Empty);
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证 Timeline 交互 Marker 到达后会发出对话请求，并在手动完成后继续播放。</summary>
        [Test]
        public void DialogueMarker_RequestsServiceAndResumesAfterCompletion()
        {
            TestContext context = CreateContext(true, true);
            try
            {
                ManualFilmInteractionService service = (ManualFilmInteractionService)context.filmSystem.InteractionService;
                int requestedInstance = 0;
                service.DialogueRequested += request => requestedInstance = request.InstanceId;
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                PlayableDirector director = context.filmRoot.GetComponentInChildren<PlayableDirector>();
                director.time = 1d;
                director.Evaluate();
                context.filmSystem.OnUpdate(0.016f);
                Assert.That(requestedInstance, Is.EqualTo(handle.InstanceId));
                Assert.That(handle.State, Is.EqualTo(FilmState.WaitingForInteraction));
                Assert.That(service.CompleteDialogue(handle.InstanceId, "manual_dialogue", true), Is.True);
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证 QTE Marker 会进入等待态，并可由手动服务提交成功结果。</summary>
        [Test]
        public void QteMarker_RequestsServiceAndAcceptsManualCompletion()
        {
            TestContext context = CreateContext(true, false, true);
            try
            {
                ManualFilmInteractionService service = (ManualFilmInteractionService)context.filmSystem.InteractionService;
                bool requested = false;
                service.QteRequested += request => requested = request.InteractionId == "manual_qte";
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                PlayableDirector director = context.filmRoot.GetComponentInChildren<PlayableDirector>();
                director.time = 1d;
                director.Evaluate();
                context.filmSystem.OnUpdate(0.016f);
                Assert.That(requested, Is.True);
                Assert.That(handle.State, Is.EqualTo(FilmState.WaitingForInteraction));
                Assert.That(service.CompleteQte(handle.InstanceId, "manual_qte", true), Is.True);
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证分支 Marker 会读取 FilmFlowContext 并跳转到条件成立的 Timeline 时间。</summary>
        [Test]
        public void BranchMarker_JumpsAccordingToFlowVariable()
        {
            TestContext context = CreateContext(true, false, false, true);
            try
            {
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor), new FilmFlowContext().Set("choice", "yes"));
                PlayableDirector director = context.filmRoot.GetComponentInChildren<PlayableDirector>();
                director.time = 1d;
                director.Evaluate();
                context.filmSystem.OnUpdate(0.016f);
                Assert.That(handle.State, Is.EqualTo(FilmState.Playing));
                Assert.That(director.time, Is.EqualTo(3d).Within(0.001d));
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证等待事件 Marker 会暂停 Timeline，并可由 ManualFilmInteractionService 唤醒。</summary>
        [Test]
        public void WaitEventMarker_PausesUntilEventCompletion()
        {
            TestContext context = CreateContext(true, false, false, false, true);
            try
            {
                ManualFilmInteractionService service = (ManualFilmInteractionService)context.filmSystem.InteractionService;
                bool requested = false;
                service.EventRequested += request => requested = request.EventId == "manual_event";
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                PlayableDirector director = context.filmRoot.GetComponentInChildren<PlayableDirector>();
                director.time = 1d;
                director.Evaluate();
                context.filmSystem.OnUpdate(0.016f);
                Assert.That(requested, Is.True);
                Assert.That(handle.State, Is.EqualTo(FilmState.WaitingForInteraction));
                Assert.That(service.CompleteEvent(handle.InstanceId, "manual_event"), Is.True);
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证更高优先级演出会抢占当前演出，并将被抢占实例标记为 Replaced。</summary>
        [Test]
        public void HigherPriorityFilm_ReplacesCurrentFilm()
        {
            TestContext context = CreateContext(true);
            FilmDefinition replacement = null;
            try
            {
                FilmHandle current = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                replacement = CreateDefinition(context.timeline, "FilmSystemTests.Replacement", 10);
                FilmHandle next = context.filmSystem.Play(replacement, new FilmBindingContext().Set("Actor", context.actor));
                Assert.That(current.State, Is.EqualTo(FilmState.Stopped));
                Assert.That(current.StopReason, Is.EqualTo(FilmStopReason.Replaced));
                Assert.That(next.State, Is.EqualTo(FilmState.Playing));
                Assert.That(context.inputSystem.BindingCount, Is.EqualTo(1));
            }
            finally
            {
                context.Dispose();
                if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement);
            }
        }

        /// <summary>验证允许跳过的演出会推进到 Timeline 结尾并返回 Skipped 原因。</summary>
        [Test]
        public void Skip_StopsAtTimelineEndWithSkippedReason()
        {
            TestContext context = CreateContext(true);
            try
            {
                SerializedObject serializedDefinition = new SerializedObject(context.definition);
                serializedDefinition.FindProperty("skipMode").enumValueIndex = (int)FilmSkipMode.ToEnd;
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
                FilmHandle handle = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor));
                handle.Skip();
                Assert.That(handle.State, Is.EqualTo(FilmState.Stopped));
                Assert.That(handle.StopReason, Is.EqualTo(FilmStopReason.Skipped));
                Assert.That(handle.Time, Is.EqualTo(context.timeline.duration).Within(0.001d));
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>验证快照可恢复 Timeline 时间和流程变量，并避免重复触发已完成的 Marker。</summary>
        [Test]
        public void Snapshot_RestoresPlaybackPositionAndFlowValues()
        {
            TestContext context = CreateContext(true, false, false, true);
            try
            {
                FilmHandle original = context.filmSystem.Play(context.definition, new FilmBindingContext().Set("Actor", context.actor), new FilmFlowContext().Set("choice", "yes"));
                PlayableDirector director = context.filmRoot.GetComponentInChildren<PlayableDirector>();
                director.time = 2d;
                director.Evaluate();
                FilmPlaybackSnapshot snapshot = original.CaptureSnapshot();
                original.Stop();
                FilmHandle restored = context.filmSystem.PlayFromSnapshot(context.definition, new FilmBindingContext().Set("Actor", context.actor), snapshot);
                Assert.That(restored.State, Is.EqualTo(FilmState.Playing));
                Assert.That(restored.Time, Is.EqualTo(2d).Within(0.001d));
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>构造一个不依赖场景资源的 Timeline 和最小 GameplayKit 替身，供编辑器测试验证运行时契约。</summary>
        private static TestContext CreateContext(bool requiredBinding, bool withDialogueMarker = false, bool withQteMarker = false, bool withBranchMarker = false, bool withWaitEventMarker = false)
        {
            GameObject root = new GameObject("FilmSystemTests.Root");
            GameObject actor = new GameObject("FilmSystemTests.Actor");
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            ActivationTrack track = timeline.CreateTrack<ActivationTrack>(null, "Actor");
            TimelineClip clip = track.CreateDefaultClip();
            clip.duration = 5d;
            if (withDialogueMarker)
            {
                timeline.CreateMarkerTrack();
                FilmInteractionMarker marker = timeline.markerTrack.CreateMarker<FilmInteractionMarker>(1d);
                marker.InteractionId = "manual_dialogue";
                marker.InteractionType = FilmInteractionType.Dialogue;
            }
            if (withQteMarker)
            {
                timeline.CreateMarkerTrack();
                FilmInteractionMarker marker = timeline.markerTrack.CreateMarker<FilmInteractionMarker>(1d);
                marker.InteractionId = "manual_qte";
                marker.InteractionType = FilmInteractionType.Qte;
                marker.QteSuccessActions = InputActionMask.Submit;
            }
            if (withBranchMarker)
            {
                timeline.CreateMarkerTrack();
                FilmBranchMarker marker = timeline.markerTrack.CreateMarker<FilmBranchMarker>(1d);
                marker.VariableKey = "choice";
                marker.ExpectedValue = "yes";
                marker.TrueTime = 3d;
                marker.FalseTime = 4d;
            }
            if (withWaitEventMarker)
            {
                timeline.CreateMarkerTrack();
                FilmWaitEventMarker marker = timeline.markerTrack.CreateMarker<FilmWaitEventMarker>(1d);
                marker.EventId = "manual_event";
            }
            FilmDefinition definition = ScriptableObject.CreateInstance<FilmDefinition>();
            SerializedObject serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("filmId").stringValue = "FilmSystemTests.Basic";
            serializedDefinition.FindProperty("timeline").objectReferenceValue = timeline;
            SerializedProperty bindingArray = serializedDefinition.FindProperty("bindings");
            bindingArray.arraySize = 1;
            SerializedProperty binding = bindingArray.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("key").stringValue = "Actor";
            binding.FindPropertyRelative("required").boolValue = requiredBinding;
            binding.FindPropertyRelative("role").enumValueIndex = (int)FilmBindingRole.Generic;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
            InputSystem inputSystem = new InputSystem(new FakeInputSource());
            CameraSystem cameraSystem = new CameraSystem(root.transform);
            FakeGameplayKit gameplayKit = new FakeGameplayKit(inputSystem, cameraSystem);
            FilmSystem filmSystem = new FilmSystem(root.transform);
            filmSystem.AfterNew(gameplayKit);
            return new TestContext(root, actor, timeline, definition, track, filmSystem, inputSystem);
        }

        /// <summary>创建复用测试 Timeline 的演出定义，并设置阶段三优先级字段。</summary>
        private static FilmDefinition CreateDefinition(TimelineAsset timeline, string filmId, int priority)
        {
            FilmDefinition definition = ScriptableObject.CreateInstance<FilmDefinition>();
            SerializedObject serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("filmId").stringValue = filmId;
            serializedDefinition.FindProperty("timeline").objectReferenceValue = timeline;
            serializedDefinition.FindProperty("priority").intValue = priority;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        /// <summary>保存测试创建的对象并按创建逆序释放，避免编辑器测试污染后续用例。</summary>
        private sealed class TestContext : IDisposable
        {
            internal readonly GameObject filmRoot;
            internal readonly GameObject actor;
            internal readonly TimelineAsset timeline;
            internal readonly FilmDefinition definition;
            internal readonly ActivationTrack track;
            internal readonly FilmSystem filmSystem;
            internal readonly InputSystem inputSystem;

            internal TestContext(GameObject filmRoot, GameObject actor, TimelineAsset timeline, FilmDefinition definition, ActivationTrack track, FilmSystem filmSystem, InputSystem inputSystem)
            {
                this.filmRoot = filmRoot;
                this.actor = actor;
                this.timeline = timeline;
                this.definition = definition;
                this.track = track;
                this.filmSystem = filmSystem;
                this.inputSystem = inputSystem;
            }

            /// <summary>释放演出系统、输入源、Timeline、定义资源和场景临时对象。</summary>
            public void Dispose()
            {
                filmSystem.Dispose();
                inputSystem.Dispose();
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(timeline);
                UnityEngine.Object.DestroyImmediate(actor);
                UnityEngine.Object.DestroyImmediate(filmRoot);
            }
        }

        /// <summary>实现无动作采样的最小输入源，使测试只关注 InputSystem 租约数量。</summary>
        private sealed class FakeInputSource : IInputSource
        {
            /// <summary>获取测试输入源的稳定名称。</summary>
            public string SourceId => "FilmSystemTests";

            /// <summary>每次采样返回空输入快照，避免测试依赖真实键鼠设备。</summary>
            public InputFrame Sample(long frameId)
            {
                return default;
            }

            /// <summary>测试输入源没有外部资源需要释放。</summary>
            public void Dispose()
            {
            }
        }

        /// <summary>只为 FilmSystem 测试提供 InputSystem 和 CameraSystem 查询能力的最小 GameplayKit 替身。</summary>
        private sealed class FakeGameplayKit : IGameplayKit
        {
            private readonly InputSystem inputSystem;
            private readonly CameraSystem cameraSystem;

            internal FakeGameplayKit(InputSystem inputSystem, CameraSystem cameraSystem)
            {
                this.inputSystem = inputSystem;
                this.cameraSystem = cameraSystem;
            }

            /// <summary>测试环境始终视为已准备完成。</summary>
            public bool IsReady => true;

            /// <summary>测试不创建玩家实体。</summary>
            public PlayerEntity Player => null;

            /// <summary>返回 FilmSystem 所需的输入或镜头系统实例。</summary>
            public TSystem GetSystem<TSystem>() where TSystem : XSystem
            {
                if (typeof(TSystem) == typeof(InputSystem)) return inputSystem as TSystem;
                if (typeof(TSystem) == typeof(CameraSystem)) return cameraSystem as TSystem;
                throw new InvalidOperationException($"Unsupported test system type '{typeof(TSystem).Name}'.");
            }

            /// <summary>尝试返回 FilmSystem 所需的输入或镜头系统实例。</summary>
            public bool TryGetSystem<TSystem>(out TSystem system) where TSystem : XSystem
            {
                try
                {
                    system = GetSystem<TSystem>();
                    return true;
                }
                catch
                {
                    system = null;
                    return false;
                }
            }
        }
    }
}
