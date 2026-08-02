using System;

namespace Xuan.Prometheus.Actor
{
    /// <summary>保存 Entity 与 PossessionSystem 中 Pawn 注册项之间的只读运行时身份桥接状态。</summary>
    public sealed class PawnComponent : Xuan.Prometheus.Component.Component
    {
        /// <summary>获取当前注册到 PossessionSystem 的 Pawn 编号；尚未注册或已经注销时返回零。</summary>
        public int PawnId { get; private set; }

        /// <summary>获取当前组件是否已经通过 PawnRegistrationLogic 注册到本局 PossessionSystem。</summary>
        public bool IsRegistered => PawnId > 0;

        /// <summary>记录已经成功提交给 PossessionSystem 的 Pawn 身份。</summary>
        /// <param name="pawnId">与所属 Entity.EntityId 一致的正运行时编号。</param>
        internal void MarkRegistered(int pawnId)
        {
            if (pawnId <= 0) throw new ArgumentOutOfRangeException(nameof(pawnId), pawnId, "Pawn ID must be positive.");
            if (PawnId > 0) throw new InvalidOperationException($"PawnComponent is already registered as Pawn '{PawnId}'.");
            PawnId = pawnId;
        }

        /// <summary>清除已经从 PossessionSystem 注销的 Pawn 身份；重复清理保持幂等。</summary>
        /// <param name="pawnId">本次注销的 Pawn 编号，用于防止旧 Logic 清除更新后的注册状态。</param>
        internal void MarkUnregistered(int pawnId)
        {
            if (PawnId == pawnId) PawnId = 0;
        }
    }
}
