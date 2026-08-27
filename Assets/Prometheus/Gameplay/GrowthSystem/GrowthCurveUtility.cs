using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>集中提供养成曲线的运行时深拷贝和安全归一化求值，避免多个 Entity 共享可变 AnimationCurve。</summary>
    internal static class GrowthCurveUtility
    {
        /// <summary>深拷贝曲线关键帧与前后 WrapMode；空曲线或零关键帧回退为线性映射。</summary>
        public static AnimationCurve CloneOrLinear(AnimationCurve source)
        {
            if (source == null || source.length == 0) return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            AnimationCurve clone = new AnimationCurve(source.keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
            return clone;
        }

        /// <summary>在零到一输入区间求值并约束输出，防止曲线切线过冲产生越界等级或词条数值。</summary>
        public static float Evaluate01(AnimationCurve curve, float normalizedInput)
        {
            AnimationCurve safeCurve = curve == null || curve.length == 0 ? AnimationCurve.Linear(0f, 0f, 1f, 1f) : curve;
            return Mathf.Clamp01(safeCurve.Evaluate(Mathf.Clamp01(normalizedInput)));
        }
    }
}
