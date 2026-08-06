using System;
using NUnit.Framework;

namespace Xuan.PrometheusCS.Simulation.Tests
{
    /// <summary>
    /// PlayerMovementSimulationTests 验证纯模拟层的移动方向、速度约束、输入校验和程序集隔离。
    /// </summary>
    public sealed class PlayerMovementSimulationTests
    {
        /// <summary>验证 W 对应正 Z 方向，并按配置速度和时间计算距离。</summary>
        [Test]
        public void Advance_WithForwardInput_MovesAlongPositiveZ()
        {
            PlayerMovementSimulation simulation = new PlayerMovementSimulation(5f);
            PlayerMovementSnapshot snapshot = simulation.Advance(new MovePlayerCommand(0f, 1f), 2f);
            Assert.That(snapshot.PositionX, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(snapshot.PositionZ, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(snapshot.TickNumber, Is.EqualTo(1L));
        }

        /// <summary>验证 D 对应正 X 方向。</summary>
        [Test]
        public void Advance_WithRightInput_MovesAlongPositiveX()
        {
            PlayerMovementSimulation simulation = new PlayerMovementSimulation(4f);
            PlayerMovementSnapshot snapshot = simulation.Advance(new MovePlayerCommand(1f, 0f), 0.5f);
            Assert.That(snapshot.PositionX, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(snapshot.PositionZ, Is.EqualTo(0f).Within(0.0001f));
        }

        /// <summary>验证对角输入归一化后不会比单轴移动更快。</summary>
        [Test]
        public void Advance_WithDiagonalInput_PreservesConfiguredSpeed()
        {
            PlayerMovementSimulation simulation = new PlayerMovementSimulation(6f);
            PlayerMovementSnapshot snapshot = simulation.Advance(new MovePlayerCommand(1f, 1f), 1f);
            float travelledDistance = (float)Math.Sqrt(snapshot.PositionX * snapshot.PositionX + snapshot.PositionZ * snapshot.PositionZ);
            Assert.That(travelledDistance, Is.EqualTo(6f).Within(0.0001f));
        }

        /// <summary>验证非法时间不会污染模拟状态。</summary>
        [Test]
        public void Advance_WithNegativeDeltaTime_ThrowsArgumentOutOfRangeException()
        {
            PlayerMovementSimulation simulation = new PlayerMovementSimulation(5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Advance(new MovePlayerCommand(0f, 1f), -0.1f));
            Assert.That(simulation.CurrentSnapshot.TickNumber, Is.EqualTo(0L));
        }

        /// <summary>验证纯模拟程序集没有编译期引用 UnityEngine。</summary>
        [Test]
        public void SimulationAssembly_DoesNotReferenceUnityEngine()
        {
            System.Reflection.AssemblyName[] references = typeof(PlayerMovementSimulation).Assembly.GetReferencedAssemblies();
            Assert.That(Array.Exists(references, reference => reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
        }
    }
}
