using System;

namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 提供客户端与服务器共同使用的角色定点数尺度和基础运算，所有模拟逻辑均不得依赖浮点 DeltaTime。
    /// </summary>
    public static class CharacterFixedPoint
    {
        /// <summary>每个世界单位对应的定点整数刻度。</summary>
        public const long PositionScale = 1_000_000L;

        /// <summary>归一化方向对应的定点整数刻度。</summary>
        public const long DirectionScale = 1_000_000L;

        /// <summary>八方向移动中斜向单位向量的单轴定点分量。</summary>
        public const long DiagonalDirectionScale = 707_107L;

        /// <summary>把十进制世界单位转换为确定性的定点整数。</summary>
        public static long FromUnits(decimal units)
        {
            return checked((long)decimal.Round(units * PositionScale, 0, MidpointRounding.AwayFromZero));
        }

        /// <summary>把定点整数转换为仅供表现和日志使用的双精度世界单位。</summary>
        public static double ToUnits(long rawValue)
        {
            return (double)rawValue / PositionScale;
        }

        /// <summary>根据八方向离散输入计算归一化定点方向。</summary>
        public static void GetNormalizedDirection(sbyte x, sbyte z, out long directionXRaw, out long directionZRaw)
        {
            if (x < -1 || x > 1) throw new ArgumentOutOfRangeException(nameof(x), "Character movement X input must be -1, 0, or 1.");
            if (z < -1 || z > 1) throw new ArgumentOutOfRangeException(nameof(z), "Character movement Z input must be -1, 0, or 1.");
            long axisScale = x != 0 && z != 0 ? DiagonalDirectionScale : DirectionScale;
            directionXRaw = x * axisScale;
            directionZRaw = z * axisScale;
        }
    }

    /// <summary>
    /// 表示使用百万分之一世界单位保存的确定性三维坐标或位移。
    /// </summary>
    public readonly struct FixedVector3 : IEquatable<FixedVector3>
    {
        /// <summary>创建一个三维定点向量。</summary>
        public FixedVector3(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>获取 X 轴定点分量。</summary>
        public long X { get; }

        /// <summary>获取 Y 轴定点分量。</summary>
        public long Y { get; }

        /// <summary>获取 Z 轴定点分量。</summary>
        public long Z { get; }

        /// <summary>获取三维原点。</summary>
        public static FixedVector3 Zero => new FixedVector3(0L, 0L, 0L);

        /// <summary>获取以世界单位表示且仅供表现层读取的 X 分量。</summary>
        public double XUnits => CharacterFixedPoint.ToUnits(X);

        /// <summary>获取以世界单位表示且仅供表现层读取的 Y 分量。</summary>
        public double YUnits => CharacterFixedPoint.ToUnits(Y);

        /// <summary>获取以世界单位表示且仅供表现层读取的 Z 分量。</summary>
        public double ZUnits => CharacterFixedPoint.ToUnits(Z);

        /// <summary>计算到另一个定点向量的世界单位距离，此结果仅用于诊断展示。</summary>
        public double DistanceUnitsTo(FixedVector3 other)
        {
            double deltaX = CharacterFixedPoint.ToUnits(X - other.X);
            double deltaY = CharacterFixedPoint.ToUnits(Y - other.Y);
            double deltaZ = CharacterFixedPoint.ToUnits(Z - other.Z);
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
        }

        /// <summary>使用十进制定点平方距离判断误差是否严格超过阈值，避免浮点比较影响回滚结论。</summary>
        public bool IsDistanceGreaterThan(FixedVector3 other, long thresholdRaw)
        {
            if (thresholdRaw < 0L) throw new ArgumentOutOfRangeException(nameof(thresholdRaw), "Distance threshold cannot be negative.");
            decimal deltaX = (decimal)X - other.X;
            decimal deltaY = (decimal)Y - other.Y;
            decimal deltaZ = (decimal)Z - other.Z;
            decimal threshold = thresholdRaw;
            return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ > threshold * threshold;
        }

        /// <summary>返回两个定点向量的逐轴和。</summary>
        public static FixedVector3 operator +(FixedVector3 left, FixedVector3 right)
        {
            return new FixedVector3(checked(left.X + right.X), checked(left.Y + right.Y), checked(left.Z + right.Z));
        }

        /// <summary>返回两个定点向量的逐轴差。</summary>
        public static FixedVector3 operator -(FixedVector3 left, FixedVector3 right)
        {
            return new FixedVector3(checked(left.X - right.X), checked(left.Y - right.Y), checked(left.Z - right.Z));
        }

        /// <summary>判断两个定点向量是否逐轴完全一致。</summary>
        public static bool operator ==(FixedVector3 left, FixedVector3 right)
        {
            return left.Equals(right);
        }

        /// <summary>判断两个定点向量是否存在任一不同分量。</summary>
        public static bool operator !=(FixedVector3 left, FixedVector3 right)
        {
            return !left.Equals(right);
        }

        /// <summary>判断当前向量是否与另一个定点向量完全一致。</summary>
        public bool Equals(FixedVector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        /// <summary>判断指定对象是否为完全一致的定点向量。</summary>
        public override bool Equals(object obj)
        {
            return obj is FixedVector3 other && Equals(other);
        }

        /// <summary>获取稳定的向量哈希码。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = hash * 397 ^ Y.GetHashCode();
                return hash * 397 ^ Z.GetHashCode();
            }
        }

        /// <summary>返回便于日志查看的世界单位坐标。</summary>
        public override string ToString()
        {
            return $"({XUnits:F4}, {YUnits:F4}, {ZUnits:F4})";
        }
    }

    /// <summary>
    /// 使用 FNV-1a 逐字段构造跨客户端和服务器稳定的六十四位内容哈希。
    /// </summary>
    internal struct CharacterStableHashBuilder
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong hash;

        /// <summary>创建一个处于标准 FNV-1a 初始状态的哈希构造器。</summary>
        public static CharacterStableHashBuilder Create()
        {
            return new CharacterStableHashBuilder { hash = OffsetBasis };
        }

        /// <summary>把一个布尔值混入当前哈希。</summary>
        public void Add(bool value)
        {
            Add(value ? 1L : 0L);
        }

        /// <summary>把一个三十二位整数混入当前哈希。</summary>
        public void Add(int value)
        {
            Add((long)value);
        }

        /// <summary>把一个六十四位整数的小端字节序混入当前哈希。</summary>
        public void Add(long value)
        {
            ulong unsignedValue = unchecked((ulong)value);
            for (int byteIndex = 0; byteIndex < sizeof(long); byteIndex++)
            {
                hash ^= (byte)(unsignedValue >> byteIndex * 8);
                hash *= Prime;
            }
        }

        /// <summary>获取已经累计完成的稳定哈希。</summary>
        public ulong ToHash()
        {
            return hash;
        }
    }
}
