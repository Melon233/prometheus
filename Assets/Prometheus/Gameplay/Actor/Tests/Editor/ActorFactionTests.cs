using NUnit.Framework;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证服务器可复用的阵营位与目标掩码保持稳定且不会把同阵营隐式视为敌对。</summary>
    public sealed class ActorFactionTests
    {
        /// <summary>目标掩码应只接受资产显式声明的阵营。</summary>
        [Test]
        public void TargetFactionMask_ContainsOnlyExplicitFactions()
        {
            ActorFactionMask targets = ActorFactionMask.Enemy | ActorFactionMask.Environment;

            Assert.That(targets.Contains(ActorFaction.Enemy), Is.True);
            Assert.That(targets.Contains(ActorFaction.Environment), Is.True);
            Assert.That(targets.Contains(ActorFaction.Player), Is.False);
            Assert.That(targets.Contains(ActorFaction.Neutral), Is.False);
        }
    }
}
