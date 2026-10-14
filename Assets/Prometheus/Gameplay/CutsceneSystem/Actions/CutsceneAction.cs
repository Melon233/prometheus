using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 演出 Action 的基类。Action 本身是资产里的只读数据：同一个实例会被每次播放共享，
    /// 因此运行时状态（计时、生成的实例）一律放在 <see cref="Begin"/> 返回的 <see cref="CutsceneActionRun"/> 中。
    ///
    /// 每个 Action 都必须能回答三件事：
    /// <list type="bullet">
    /// <item><see cref="Begin"/>：正常开始。瞬时生效的 Action 返回空，持续型返回运行体。</item>
    /// <item><see cref="ApplyFinalState"/>：跳过时，未开始的 Action 直接落到最终状态，不产生过程（不出声、不出特效、不显示台词）。</item>
    /// <item><see cref="Channel"/>：同一通道上新的 Action 开始时，旧的运行体会先被 Finish，避免两个镜头 Action 同时驱动镜头。</item>
    /// </list>
    /// </summary>
    [Serializable]
    public abstract class CutsceneAction
    {
        /// <summary>互斥通道；为空表示不与任何 Action 互斥。</summary>
        public virtual string Channel => null;

        /// <summary>正常开始本 Action。</summary>
        /// <param name="context">本次播放上下文。</param>
        /// <returns>持续型 Action 的运行体；瞬时生效返回空。</returns>
        public abstract CutsceneActionRun Begin(CutsceneContext context);

        /// <summary>跳过时直接应用最终状态。只改变结果，不产生过程表现。</summary>
        /// <param name="context">本次播放上下文。</param>
        public abstract void ApplyFinalState(CutsceneContext context);

        /// <summary>校验本 Action 的配置；错误追加到 <paramref name="errors"/>。</summary>
        /// <param name="definition">所属演出，用于检查演员引用。</param>
        /// <param name="where">错误定位前缀。</param>
        /// <param name="errors">错误收集列表。</param>
        public virtual void Validate(CutsceneDefinition definition, string where, List<string> errors)
        {
        }

        /// <summary>检查演员键存在；<paramref name="allowPlayer"/> 为假时同时禁止玩家演员。供子类校验复用。</summary>
        /// <param name="definition">所属演出。</param>
        /// <param name="actorKey">被引用的演员键。</param>
        /// <param name="allowPlayer">是否允许引用玩家演员。</param>
        /// <param name="where">错误定位前缀。</param>
        /// <param name="errors">错误收集列表。</param>
        protected static void ValidateActor(CutsceneDefinition definition, string actorKey, bool allowPlayer, string where, List<string> errors)
        {
            CutsceneActorDef actor = definition.FindActor(actorKey);
            if (actor == null) errors.Add($"{where}：引用了不存在的演员 '{actorKey}'。");
            else if (!allowPlayer && actor.Source == CutsceneActorSource.Player) errors.Add($"{where}：玩家演员 '{actorKey}' 不支持此 Action。");
        }
    }

    /// <summary>持续型 Action 的一次运行。由播放器逐帧驱动，直到自然结束或被 Finish。</summary>
    public abstract class CutsceneActionRun
    {
        /// <summary>推进一帧。</summary>
        /// <param name="dt">帧时间（秒）。</param>
        /// <returns>已经自然结束时返回真，播放器随后会调用 <see cref="Finish"/>。</returns>
        public abstract bool Tick(float dt);

        /// <summary>立即落到最终状态并释放运行期资源（停止声音、销毁特效）。只会被调用一次。</summary>
        public abstract void Finish();
    }
}
