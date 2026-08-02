using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>提供不依赖场景对象和运行时可变状态的 Q16 根运动区间积分，使客户端运行时、编辑器预览与离线验证共享同一半开区间语义。</summary>
    public static class ActorMotionIntervalMath
    {
        /// <summary>计算一个绝对行为 Tick 在模拟相位区间与 MotionClip 区间交集中的覆盖比例；方法只使用值类型运算且不创建委托或临时集合。</summary>
        /// <param name="intervalStartRaw">本次模拟步开始时的非负 Q16 绝对行为相位，包含该边界。</param>
        /// <param name="intervalEndRaw">本次模拟步结束时的非负 Q16 绝对行为相位，不包含该边界且不得早于开始相位。</param>
        /// <param name="clipStartTick">MotionClip 的非负起始行为 Tick，包含该 Tick。</param>
        /// <param name="clipEndTick">MotionClip 的结束行为 Tick，不包含该 Tick 且必须晚于起始 Tick。</param>
        /// <param name="behaviorTick">待计算覆盖比例的非负绝对行为 Tick；该 Tick 位于相位区间或 MotionClip 之外时返回零。</param>
        /// <returns>当前 Tick 被两个半开区间共同覆盖的比例，取值范围为零到一。</returns>
        /// <exception cref="ArgumentOutOfRangeException">当相位、MotionClip 区间或行为 Tick 无效时抛出。</exception>
        public static float GetTickOverlapRatio(long intervalStartRaw, long intervalEndRaw, int clipStartTick, int clipEndTick, int behaviorTick)
        {
            ValidateInterval(intervalStartRaw, intervalEndRaw, clipStartTick, clipEndTick);
            if (behaviorTick < 0) throw new ArgumentOutOfRangeException(nameof(behaviorTick), behaviorTick, "Behavior tick cannot be negative.");
            long tickStartRaw = (long)behaviorTick << BehaviorPhase.FractionBits;
            long tickEndRaw = ((long)behaviorTick + 1L) << BehaviorPhase.FractionBits;
            long overlapStartRaw = Math.Max(Math.Max(intervalStartRaw, (long)clipStartTick << BehaviorPhase.FractionBits), tickStartRaw);
            long overlapEndRaw = Math.Min(Math.Min(intervalEndRaw, (long)clipEndTick << BehaviorPhase.FractionBits), tickEndRaw);
            return overlapEndRaw <= overlapStartRaw ? 0f : (overlapEndRaw - overlapStartRaw) / (float)BehaviorPhase.One;
        }

        /// <summary>对权威行为相位区间与 MotionClip 区间的重叠部分逐 Tick 积分；每个 Tick 位移按实际覆盖的 Q16 比例缩放，因此连续低速步不会重复消费同一 Tick 的完整位移。</summary>
        /// <param name="intervalStartRaw">本次模拟步开始时的非负 Q16 绝对行为相位，包含该边界。</param>
        /// <param name="intervalEndRaw">本次模拟步结束时的非负 Q16 绝对行为相位，不包含该边界且不得早于开始相位。</param>
        /// <param name="clipStartTick">MotionClip 的非负起始行为 Tick，包含该 Tick。</param>
        /// <param name="clipEndTick">MotionClip 的结束行为 Tick，不包含该 Tick且必须晚于起始 Tick。</param>
        /// <param name="displacementAtTick">按绝对行为 Tick 返回有限局部位移的纯读取函数；仅会访问实际重叠的 Tick。</param>
        /// <returns>本次相位区间实际消费的局部位移总和；区间为空或与 MotionClip 无重叠时返回零向量。</returns>
        /// <exception cref="ArgumentNullException">当位移读取函数为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当相位、MotionClip 区间或读取到的位移无效时抛出。</exception>
        public static Vector3 IntegrateQ16(long intervalStartRaw, long intervalEndRaw, int clipStartTick, int clipEndTick, Func<int, Vector3> displacementAtTick)
        {
            if (displacementAtTick == null) throw new ArgumentNullException(nameof(displacementAtTick));
            ValidateInterval(intervalStartRaw, intervalEndRaw, clipStartTick, clipEndTick);
            long clipStartRaw = (long)clipStartTick << BehaviorPhase.FractionBits;
            long clipEndRaw = (long)clipEndTick << BehaviorPhase.FractionBits;
            long cursorRaw = Math.Max(intervalStartRaw, clipStartRaw);
            long overlapEndRaw = Math.Min(intervalEndRaw, clipEndRaw);
            Vector3 integratedDisplacement = Vector3.zero;
            while (cursorRaw < overlapEndRaw)
            {
                int behaviorTick = checked((int)(cursorRaw >> BehaviorPhase.FractionBits));
                long behaviorTickEndRaw = Math.Min(overlapEndRaw, ((long)behaviorTick + 1L) << BehaviorPhase.FractionBits);
                Vector3 tickDisplacement = displacementAtTick(behaviorTick);
                if (!IsFinite(tickDisplacement)) throw new ArgumentOutOfRangeException(nameof(displacementAtTick), tickDisplacement, $"Motion displacement at behavior tick '{behaviorTick}' must contain only finite values.");
                float coveredTickRatio = (behaviorTickEndRaw - cursorRaw) / (float)BehaviorPhase.One;
                integratedDisplacement += tickDisplacement * coveredTickRatio;
                cursorRaw = behaviorTickEndRaw;
            }
            return integratedDisplacement;
        }

        /// <summary>统一验证 Q16 模拟相位区间与 MotionClip Tick 区间，确保所有公开积分入口使用完全相同的严格契约。</summary>
        private static void ValidateInterval(long intervalStartRaw, long intervalEndRaw, int clipStartTick, int clipEndTick)
        {
            if (intervalStartRaw < 0L) throw new ArgumentOutOfRangeException(nameof(intervalStartRaw), intervalStartRaw, "Q16 motion interval cannot start before phase zero.");
            if (intervalEndRaw < intervalStartRaw) throw new ArgumentOutOfRangeException(nameof(intervalEndRaw), intervalEndRaw, "Q16 motion interval cannot end before its start phase.");
            if (clipStartTick < 0) throw new ArgumentOutOfRangeException(nameof(clipStartTick), clipStartTick, "Motion clip cannot start before behavior tick zero.");
            if (clipEndTick <= clipStartTick) throw new ArgumentOutOfRangeException(nameof(clipEndTick), clipEndTick, "Motion clip must end after its start tick.");
        }

        /// <summary>判断三维向量是否只包含可安全进入权威运动积分的有限分量。</summary>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
