using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证 Q16 根运动区间积分在低速连续步、跨 Tick 快速步和无重叠区间中的确定性半开区间语义。</summary>
    public sealed class ActorMotionIntervalMathTests
    {
        /// <summary>验证无分配单 Tick API 在连续 0.75 倍速步中只让 Tick 0 获得 75% 加 25%，同时准确报告第二步进入 Tick 1 的 50%。</summary>
        [Test]
        public void GetTickOverlapRatio_WithTwoConsecutiveThreeQuarterSteps_PartitionsTickCoverageWithoutDuplication()
        {
            int threeQuarterTickRaw = BehaviorPhase.RateFromRatio(3, 4);
            float firstStepTickZero = ActorMotionIntervalMath.GetTickOverlapRatio(0L, threeQuarterTickRaw, 0, 2, 0);
            float secondStepTickZero = ActorMotionIntervalMath.GetTickOverlapRatio(threeQuarterTickRaw, threeQuarterTickRaw * 2L, 0, 2, 0);
            float secondStepTickOne = ActorMotionIntervalMath.GetTickOverlapRatio(threeQuarterTickRaw, threeQuarterTickRaw * 2L, 0, 2, 1);
            Assert.That(firstStepTickZero, Is.EqualTo(0.75f).Within(0.000001f));
            Assert.That(secondStepTickZero, Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(firstStepTickZero + secondStepTickZero, Is.EqualTo(1f).Within(0.000001f));
            Assert.That(secondStepTickOne, Is.EqualTo(0.5f).Within(0.000001f));
        }

        /// <summary>验证 0.75 倍速的连续两步分别消费 Tick 0 的 75% 与剩余 25%，不会把 Tick 0 的完整位移累计为 1.5 倍。</summary>
        [Test]
        public void IntegrateQ16_WithTwoConsecutiveThreeQuarterSteps_ConsumesTickZeroExactlyOnce()
        {
            int threeQuarterTickRaw = BehaviorPhase.RateFromRatio(3, 4);
            Vector3 firstStep = ActorMotionIntervalMath.IntegrateQ16(0L, threeQuarterTickRaw, 0, 2, tick => tick == 0 ? new Vector3(8f, 0f, 0f) : Vector3.zero);
            Vector3 secondStep = ActorMotionIntervalMath.IntegrateQ16(threeQuarterTickRaw, threeQuarterTickRaw * 2L, 0, 2, tick => tick == 0 ? new Vector3(8f, 0f, 0f) : Vector3.zero);
            Assert.That(firstStep.x, Is.EqualTo(6f).Within(0.000001f));
            Assert.That(secondStep.x, Is.EqualTo(2f).Within(0.000001f));
            Assert.That((firstStep + secondStep).x, Is.EqualTo(8f).Within(0.000001f));
        }

        /// <summary>验证单次相位区间跨越多个完整 Tick 与两个部分 Tick 时，会按时间顺序准确累计每个样本的覆盖比例。</summary>
        [Test]
        public void IntegrateQ16_WhenIntervalCrossesMultipleTicks_WeightsEveryCoveredTick()
        {
            long intervalStartRaw = BehaviorPhase.One / 2L;
            long intervalEndRaw = BehaviorPhase.One * 3L + BehaviorPhase.One / 4L;
            Vector3 integrated = ActorMotionIntervalMath.IntegrateQ16(intervalStartRaw, intervalEndRaw, 0, 4, tick => new Vector3(2 << tick, 0f, 0f));
            Assert.That(integrated.x, Is.EqualTo(17f).Within(0.000001f));
        }

        /// <summary>验证模拟相位与 MotionClip 没有重叠时返回零，并且不会读取任何无关 Tick 的位移样本。</summary>
        [Test]
        public void IntegrateQ16_WhenIntervalsDoNotOverlap_ReturnsZeroWithoutReadingSamples()
        {
            int sampleReadCount = 0;
            Vector3 integrated = ActorMotionIntervalMath.IntegrateQ16(0L, BehaviorPhase.One, 2, 4, tick =>
            {
                sampleReadCount++;
                return Vector3.one;
            });
            Assert.That(integrated, Is.EqualTo(Vector3.zero));
            Assert.That(sampleReadCount, Is.Zero);
        }
    }
}
