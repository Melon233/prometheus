using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 剧情 DSL 的静态入口。
    /// 用 <c>using static Xuan.Prometheus.Narrative.Story;</c> 引入后即可直接书写剧情树。
    /// </summary>
    public static class Story
    {
        /// <summary>顺序演绎全部子节点。</summary>
        public static SequenceAction Seq(params IStoryAction[] children)
        {
            return new SequenceAction(children);
        }

        /// <summary>并发演绎全部子节点并等待其全部完成。</summary>
        public static ParallelAction Par(params IStoryAction[] children)
        {
            return new ParallelAction(ParallelMode.WhenAll, children);
        }

        /// <summary>按指定完成条件并发演绎全部子节点。</summary>
        public static ParallelAction Par(ParallelMode mode, params IStoryAction[] children)
        {
            return new ParallelAction(mode, children);
        }

        /// <summary>按条件表达式选择分支。</summary>
        /// <param name="expression">条件表达式文本。</param>
        /// <param name="then">条件成立时演绎的分支。</param>
        /// <param name="otherwise">条件不成立时演绎的分支；可为空。</param>
        public static IfAction If(string expression, IStoryAction then, IStoryAction otherwise = null)
        {
            return new IfAction(expression, then, otherwise);
        }

        /// <summary>写入一个布尔旗标。</summary>
        public static SetVariableAction SetFlag(string name, bool value = true)
        {
            return new SetVariableAction(name.IndexOf('.') >= 0 ? name : string.Concat(StoryVariables.FlagRoot, ".", name), new StoryValue(value));
        }

        /// <summary>写入一个常量变量值。</summary>
        public static SetVariableAction SetVar(string path, StoryValue value)
        {
            return new SetVariableAction(path, value);
        }

        /// <summary>写入一个表达式求值结果。</summary>
        public static SetVariableAction SetVarExpr(string path, string valueExpression)
        {
            return new SetVariableAction(path, valueExpression);
        }

        /// <summary>等待固定秒数。</summary>
        public static WaitAction Wait(float seconds)
        {
            return new WaitAction(seconds);
        }

        /// <summary>等待条件成立。</summary>
        /// <param name="expression">等待成立的条件表达式。</param>
        /// <param name="timeoutSeconds">超时秒数；小于等于零表示无限等待。</param>
        public static WaitForAction WaitFor(string expression, float timeoutSeconds = 0f)
        {
            return new WaitForAction(expression, timeoutSeconds);
        }

        /// <summary>
        /// 执行一段会改变世界状态的同步逻辑。
        /// 该回调同时用于演绎与落终态，因此实现必须幂等。
        /// </summary>
        public static CallbackAction Do(Action<StoryContext> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return new CallbackAction((context, token) =>
            {
                action(context);
                return UniTask.CompletedTask;
            }, action);
        }

        /// <summary>
        /// 执行一段纯表现逻辑。
        /// 该回调不参与落终态，跳过时被整体略过，因此不得在其中改变任何世界状态。
        /// </summary>
        public static CallbackAction Present(Func<StoryContext, CancellationToken, UniTask> action)
        {
            return new CallbackAction(action, null);
        }

        /// <summary>执行一段纯表现逻辑的同步简写。</summary>
        public static CallbackAction Present(Action<StoryContext> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return new CallbackAction((context, token) =>
            {
                action(context);
                return UniTask.CompletedTask;
            }, null);
        }

        /// <summary>提前结束最近一层顺序组合子。</summary>
        public static BreakAction Break()
        {
            return new BreakAction();
        }

        /// <summary>创建一句由指定角色说出的台词节拍。</summary>
        public static Beat Say(ActorRef speaker, TextKey text)
        {
            return new Beat(speaker, text);
        }

        /// <summary>创建一句没有说话人的叙述节拍。</summary>
        public static Beat Narration(TextKey text)
        {
            return new Beat(ActorRef.None, text);
        }

        /// <summary>创建一个选项分支。</summary>
        public static ChooseAction Choose(params ChoiceOption[] options)
        {
            return new ChooseAction(options);
        }

        /// <summary>创建一个选项。</summary>
        public static ChoiceOption Option(TextKey text)
        {
            return new ChoiceOption(text);
        }

        /// <summary>播放一整段演出 Timeline；该片段必须在 StageSpec.Cinematics 中声明。</summary>
        public static CinematicAction Cinematic(string location)
        {
            return new CinematicAction(location);
        }

        /// <summary>播放一段演出 Timeline 的指定区间，用于把长演出拆开穿插对话。</summary>
        /// <param name="location">Timeline 资源地址。</param>
        /// <param name="fromCue">起始区间标记名；为空表示从当前时间继续。</param>
        /// <param name="toCue">结束区间标记名；为空表示播到结尾。</param>
        public static CinematicAction Cinematic(string location, string fromCue, string toCue)
        {
            return new CinematicAction(location, fromCue, toCue);
        }

        /// <summary>混合到一台具名演出镜头。</summary>
        public static CameraBlendAction CameraTo(string cameraId, float duration = 0.8f)
        {
            return new CameraBlendAction(cameraId, duration);
        }

        /// <summary>硬切到一台具名演出镜头。</summary>
        public static CameraBlendAction CameraCut(string cameraId)
        {
            return new CameraBlendAction(cameraId, 0f);
        }

        /// <summary>播放一次镜头抖动。</summary>
        public static CameraShakeAction CameraShake(float amplitude = 0.25f, float duration = 0.35f)
        {
            return new CameraShakeAction(amplitude, duration);
        }

        /// <summary>生成一个特效实例。</summary>
        /// <param name="location">特效预制体资源地址。</param>
        /// <param name="placement">生成位置。</param>
        /// <param name="lifetime">存活秒数；小于等于零表示随舞台退出才回收。</param>
        /// <param name="waitForCompletion">是否阻塞到存活时间结束。</param>
        public static VfxSpawnAction Vfx(string location, VfxPlacement placement, float lifetime = 0f, bool waitForCompletion = false)
        {
            return new VfxSpawnAction(location, placement, lifetime, waitForCompletion);
        }

        /// <summary>播放一次性音效。</summary>
        public static SfxAction Sfx(string eventKey)
        {
            return new SfxAction(eventKey);
        }

        /// <summary>在指定位置播放一次性音效。</summary>
        public static SfxAction Sfx(string eventKey, VfxPlacement placement)
        {
            return new SfxAction(eventKey, placement);
        }

        /// <summary>播放一段持续的背景音或环境音。</summary>
        public static AmbienceAction Ambience(string eventKey, float fadeInSeconds = 0f, float fadeOutSeconds = 1f)
        {
            return new AmbienceAction(eventKey, fadeInSeconds, fadeOutSeconds);
        }

        /// <summary>把角色或场景物体移动到指定位置。</summary>
        public static ActorMoveAction MoveTo(ActorRef actor, Anchor destination, float duration, bool faceMoveDirection = false)
        {
            return new ActorMoveAction(actor, destination, duration, faceMoveDirection);
        }

        /// <summary>让角色朝向另一个角色。</summary>
        public static ActorFaceAction Face(ActorRef actor, ActorRef lookAt)
        {
            return new ActorFaceAction(actor, lookAt);
        }

        /// <summary>让角色朝向指定欧拉角。</summary>
        public static ActorFaceAction Face(ActorRef actor, Vector3 eulerAngles)
        {
            return new ActorFaceAction(actor, eulerAngles);
        }

        /// <summary>切换角色或场景物体的显隐。</summary>
        public static ActorVisibilityAction Show(ActorRef actor, bool visible)
        {
            return new ActorVisibilityAction(actor, visible);
        }

        /// <summary>按名称播放角色的一段 Spine 动画。</summary>
        public static SpineAnimAction SpineAnim(ActorRef actor, string animationName, bool loop = false, float speed = 1f)
        {
            return new SpineAnimAction(actor, animationName, loop, speed);
        }

        /// <summary>按资源地址播放场景物体的一段原生动画片段。</summary>
        public static PropAnimAction PropAnim(ActorRef prop, string clipLocation, float speed = 1f)
        {
            return new PropAnimAction(prop, clipLocation, speed);
        }
    }
}
