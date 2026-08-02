using System;

namespace Xuan.Prometheus.Actor
{
    /// <summary>标识一个 GameplayObject 在战斗规则中的稳定阵营；该枚举不依赖 Unity Tag 或 Layer，可直接迁移到服务器模拟。</summary>
    public enum ActorFaction
    {
        Neutral,
        Player,
        Enemy,
        Environment
    }

    /// <summary>声明一次行为允许命中的目标阵营集合，使友伤、机关伤害和中立对象交互都由资产显式决定。</summary>
    [Flags]
    public enum ActorFactionMask
    {
        None = 0,
        Neutral = 1 << 0,
        Player = 1 << 1,
        Enemy = 1 << 2,
        Environment = 1 << 3,
        All = Neutral | Player | Enemy | Environment
    }

    /// <summary>提供单一阵营与目标掩码之间的纯逻辑转换，客户端物理查询和未来服务器命中解析共享相同语义。</summary>
    public static class ActorFactionUtility
    {
        /// <summary>把单一阵营转换为可与目标掩码比较的独立位。</summary>
        public static ActorFactionMask ToMask(this ActorFaction faction)
        {
            switch (faction)
            {
                case ActorFaction.Neutral: return ActorFactionMask.Neutral;
                case ActorFaction.Player: return ActorFactionMask.Player;
                case ActorFaction.Enemy: return ActorFactionMask.Enemy;
                case ActorFaction.Environment: return ActorFactionMask.Environment;
                default: throw new ArgumentOutOfRangeException(nameof(faction), faction, "Unsupported actor faction.");
            }
        }

        /// <summary>判断目标掩码是否允许命中给定单一阵营。</summary>
        public static bool Contains(this ActorFactionMask mask, ActorFaction faction)
        {
            return (mask & faction.ToMask()) != ActorFactionMask.None;
        }
    }
}
