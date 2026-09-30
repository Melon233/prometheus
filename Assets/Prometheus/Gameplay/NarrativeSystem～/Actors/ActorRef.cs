using System;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>定义剧情角色引用的类别。</summary>
    public enum ActorKind
    {
        /// <summary>无具体角色，例如黑屏叙述。</summary>
        None,

        /// <summary>当前上场的队伍成员。</summary>
        ActiveMember,

        /// <summary>按槽位指定的队伍成员。</summary>
        TeamSlot,

        /// <summary>常驻同伴。</summary>
        Companion,

        /// <summary>场景中的具名 NPC。</summary>
        Npc,

        /// <summary>场景中的具名物体。</summary>
        SceneProp,

        /// <summary>仅本次演出存在的临时替身。</summary>
        Stunt
    }

    /// <summary>
    /// 描述一个剧情角色的引用方式。
    /// 本阶段只用于标识说话人与显示名解析；实际场景对象解析由第二期的 ActorResolver 承担。
    /// </summary>
    public readonly struct ActorRef : IEquatable<ActorRef>
    {
        /// <summary>获取空引用，表示没有具体说话人。</summary>
        public static readonly ActorRef None = default;

        /// <summary>创建一个角色引用。</summary>
        public ActorRef(ActorKind kind, string id)
        {
            Kind = kind;
            Id = string.IsNullOrWhiteSpace(id) ? null : id;
        }

        /// <summary>获取角色引用类别。</summary>
        public ActorKind Kind { get; }

        /// <summary>获取该类别下的稳定标识。</summary>
        public string Id { get; }

        /// <summary>获取当前引用是否为空引用。</summary>
        public bool IsNone => Kind == ActorKind.None;

        /// <summary>获取该角色显示名在文本表中的键，约定为 <c>actor.&lt;id&gt;.name</c>。</summary>
        public TextKey DisplayNameKey => Id == null ? TextKey.Empty : new TextKey($"actor.{Id}.name");

        /// <summary>引用当前上场的队伍成员。</summary>
        public static ActorRef ActiveMember()
        {
            return new ActorRef(ActorKind.ActiveMember, "active");
        }

        /// <summary>引用一个常驻同伴。</summary>
        public static ActorRef Companion(string id)
        {
            return new ActorRef(ActorKind.Companion, id);
        }

        /// <summary>引用一个场景中的具名 NPC。</summary>
        public static ActorRef Npc(string id)
        {
            return new ActorRef(ActorKind.Npc, id);
        }

        /// <summary>引用一个场景中的具名物体。</summary>
        public static ActorRef SceneProp(string id)
        {
            return new ActorRef(ActorKind.SceneProp, id);
        }

        /// <inheritdoc />
        public bool Equals(ActorRef other)
        {
            return Kind == other.Kind && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ActorRef other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ (Id != null ? StringComparer.Ordinal.GetHashCode(Id) : 0);
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Id == null ? Kind.ToString() : $"{Kind}:{Id}";
        }
    }
}
