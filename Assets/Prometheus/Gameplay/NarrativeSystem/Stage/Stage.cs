using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 舞台接管的入口，同时提供屏幕表现相关的剧情动作工厂。
    /// 进入顺序与退出顺序严格互为逆序，退出由 <see cref="StageScope"/> 的作用域保证。
    /// </summary>
    public static class Stage
    {
        /// <summary>
        /// 进入舞台：按固定顺序接管环境，并把每一步的还原动作压入作用域。
        /// 任何一步失败都会先把已完成的步骤还原干净再向外抛出。
        /// </summary>
        /// <param name="spec">本次接管的声明。</param>
        /// <param name="services">舞台使用的能力端口。</param>
        /// <param name="cancellationToken">外部取消令牌。</param>
        /// <returns>用 <c>await using</c> 持有的舞台作用域。</returns>
        public static async UniTask<StageScope> EnterAsync(StageSpec spec, StageServices services, CancellationToken cancellationToken = default)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (services == null) throw new ArgumentNullException(nameof(services));
            spec.Validate();

            StageScope scope = new StageScope(spec, services);
            try
            {
                await EnterFadeAsync(scope, cancellationToken);
                EnterScreenDressing(scope);
                EnterInputLock(scope);
                EnterAiFreeze(scope);
                EnterCameraControl(scope);
                await EnterActorsAsync(scope, cancellationToken);
                await EnterPreloadAsync(scope, cancellationToken);
                await EnterCinematicsAsync(scope, cancellationToken);
                await ExitFadeAsync(scope, cancellationToken);
                return scope;
            }
            catch
            {
                // 进入过程失败同样要把已经生效的接管全部还原，否则玩家会卡在黑幕或输入锁里。
                await scope.DisposeAsync();
                throw;
            }
        }

        /// <summary>把黑幕淡到全黑，并压入退出时的淡入还原。</summary>
        private static async UniTask EnterFadeAsync(StageScope scope, CancellationToken cancellationToken)
        {
            INarrativeScreen screen = scope.Services.Screen;
            float duration = Mathf.Max(0f, scope.Spec.FadeSeconds);
            float previous = screen.FadeAlpha;
            scope.PushTeardown("fade-in", async () =>
            {
                await screen.FadeAsync(previous, duration, CancellationToken.None);
            });
            await screen.FadeAsync(1f, duration, cancellationToken);
        }

        /// <summary>隐藏 HUD 并打开黑边。</summary>
        private static void EnterScreenDressing(StageScope scope)
        {
            INarrativeScreen screen = scope.Services.Screen;
            StageSpec spec = scope.Spec;
            bool previousHud = screen.HudVisible;
            float previousLetterbox = screen.LetterboxRatio;
            scope.PushTeardown("screen-dressing", () =>
            {
                screen.HudVisible = previousHud;
                screen.LetterboxRatio = previousLetterbox;
                return UniTask.CompletedTask;
            });
            if (spec.HideHud) screen.HudVisible = false;
            if (spec.LetterboxRatio > 0f) screen.LetterboxRatio = spec.LetterboxRatio;
        }

        /// <summary>接管玩法输入。</summary>
        private static void EnterInputLock(StageScope scope)
        {
            if (!scope.Spec.LockInput || scope.Services.World == null) return;
            IDisposable lease = scope.Services.World.LockGameplayInput();
            scope.PushTeardown("input-lock", () =>
            {
                lease?.Dispose();
                return UniTask.CompletedTask;
            });
        }

        /// <summary>冻结演出范围内的 AI 与怪物。</summary>
        private static void EnterAiFreeze(StageScope scope)
        {
            if (scope.Spec.FreezeAiRadius <= 0f || scope.Services.World == null) return;
            IDisposable lease = scope.Services.World.FreezeAi(scope.Spec.FreezeAiCenter, scope.Spec.FreezeAiRadius);
            scope.PushTeardown("ai-freeze", () =>
            {
                lease?.Dispose();
                return UniTask.CompletedTask;
            });
        }

        /// <summary>接管演出镜头控制权。</summary>
        private static void EnterCameraControl(StageScope scope)
        {
            if (!scope.Spec.TakeCameraControl || scope.Services.Camera == null) return;
            IDisposable lease = scope.Services.Camera.AcquireControl(scope.Spec.CameraPriority);
            scope.PushTeardown("camera-control", () =>
            {
                lease?.Dispose();
                return UniTask.CompletedTask;
            });
        }

        /// <summary>解析全部参演角色，接管其动画驱动权并完成初始摆位。</summary>
        private static async UniTask EnterActorsAsync(StageScope scope, CancellationToken cancellationToken)
        {
            IActorResolver resolver = scope.Services.Actors;
            List<ActorRef> declared = scope.Spec.Actors;
            for (int index = 0; index < declared.Count; index++)
            {
                ActorRef actor = declared[index];
                // 解析失败必须在此处暴露，不允许演到一半才发现角色不存在。
                ActorHandle handle = await resolver.ResolveAsync(actor, cancellationToken);
                if (handle == null || !handle.IsAlive) throw new InvalidOperationException($"Narrative actor '{actor}' could not be resolved in the current scene.");
                scope.RegisterActor(handle);

                ActorAnimationHandover handover = new ActorAnimationHandover(handle);
                Vector3 originalPosition = handle.Transform.position;
                Quaternion originalRotation = handle.Transform.rotation;
                scope.PushTeardown($"actor:{actor}", () =>
                {
                    handover.Dispose();
                    // 演出是纯表现：角色结束后回到进入舞台前的位置，永久性移动应由显式的状态动作表达。
                    if (handle.IsAlive && !handle.IsStunt)
                    {
                        handle.Transform.SetPositionAndRotation(originalPosition, originalRotation);
                    }
                    resolver.Release(handle);
                    return UniTask.CompletedTask;
                });

                if (scope.Spec.Placements.TryGetValue(actor, out Anchor placement)) handle.Transform.position = placement.Resolve(resolver);
            }
        }

        /// <summary>在淡黑期间预加载演出资源，避免演到一半才触发加载卡顿。</summary>
        private static async UniTask EnterPreloadAsync(StageScope scope, CancellationToken cancellationToken)
        {
            List<string> preload = scope.Spec.Preload;
            if (preload.Count == 0) return;
            INarrativeAssetPort assets = scope.Services.RequireAssets();
            for (int index = 0; index < preload.Count; index++)
            {
                string location = preload[index];
                await assets.LoadAsync<UnityEngine.Object>(location, cancellationToken);
                scope.PushTeardown($"preload:{location}", () =>
                {
                    assets.Release(location);
                    return UniTask.CompletedTask;
                });
            }
        }

        /// <summary>
        /// 创建本次舞台会用到的全部演出播放器并完成轨道绑定。
        /// 必须在角色解析之后执行，绑定才能找到对应的场景对象。
        /// </summary>
        private static async UniTask EnterCinematicsAsync(StageScope scope, CancellationToken cancellationToken)
        {
            List<string> cinematics = scope.Spec.Cinematics;
            for (int index = 0; index < cinematics.Count; index++) await scope.AcquireDirectorAsync(cinematics[index], cancellationToken);
        }

        /// <summary>淡入到演出画面，并压入退出时的淡出还原。</summary>
        private static async UniTask ExitFadeAsync(StageScope scope, CancellationToken cancellationToken)
        {
            INarrativeScreen screen = scope.Services.Screen;
            float duration = Mathf.Max(0f, scope.Spec.FadeSeconds);
            scope.PushTeardown("fade-out", async () =>
            {
                await screen.FadeAsync(1f, duration, CancellationToken.None);
            });
            await screen.FadeAsync(0f, duration, cancellationToken);
        }

        /// <summary>创建一个把黑幕过渡到指定不透明度的动作。</summary>
        public static IStoryAction Fade(float target, float duration)
        {
            return new ScreenFadeAction(target, duration);
        }

        /// <summary>创建一个淡出到全黑的动作。</summary>
        public static IStoryAction FadeOut(float duration)
        {
            return new ScreenFadeAction(1f, duration);
        }

        /// <summary>创建一个从黑幕淡入的动作。</summary>
        public static IStoryAction FadeIn(float duration)
        {
            return new ScreenFadeAction(0f, duration);
        }

        /// <summary>创建一个切换黑边比例的动作。</summary>
        public static IStoryAction Letterbox(float ratio, float duration = 0.3f)
        {
            return new ScreenLetterboxAction(ratio, duration);
        }

        /// <summary>创建一个切换 HUD 显隐的动作。</summary>
        public static IStoryAction Hud(bool visible)
        {
            return new ScreenHudAction(visible);
        }
    }
}
