using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Film;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Npc
{
    /// <summary>把 NPC 交互请求转换为 Film 播放，并统一处理完成和取消。</summary>
    internal sealed class NpcInteractionCoordinator
    {
        private readonly IGameplayKit gameplayKit;
        private readonly FilmSystem filmSystem;
        private readonly Action<int> onCompleted;
        private FilmHandle activeFilm;

        /// <summary>创建绑定当前单局 GameplayKit 的交互协调器。</summary>
        internal NpcInteractionCoordinator(IGameplayKit gameplayKit, FilmSystem filmSystem, Action<int> onCompleted)
        {
            this.gameplayKit = gameplayKit;
            this.filmSystem = filmSystem;
            this.onCompleted = onCompleted;
        }

        /// <summary>异步启动 NPC 配置的 Film；没有 Film 时保留给外部 InteractionRequested 订阅者。</summary>
        internal void Start(NpcInteractionContext context)
        {
            if (!gameplayKit.GetSystem<EntitySystem>().TryGetEntity(context.EntityId, out Entity entity) || !(entity is NpcEntity npc)) return;
            StartAsync(context, npc).Forget();
        }

        /// <summary>解析玩家/NPC 绑定，等待 Film 结束后释放 NPC 会话。</summary>
        private async UniTaskVoid StartAsync(NpcInteractionContext context, NpcEntity npc)
        {
            NpcDefinition definition = npc.Definition;
            if (definition.InteractionFilm == null) return;
            if (gameplayKit.Player == null || gameplayKit.Player.bindGo == null || npc.bindGo == null) return;
            FilmBindingContext bindings = new FilmBindingContext().Set(definition.PlayerBindingKey, gameplayKit.Player.bindGo).Set(definition.NpcBindingKey, npc.bindGo);
            try
            {
                activeFilm = filmSystem.Play(definition.InteractionFilm, bindings, new FilmFlowContext().Set("NpcId", definition.NpcId).Set("InteractionId", context.InteractionId));
                await activeFilm.WaitForCompletionAsync();
            }
            finally
            {
                activeFilm = null;
                onCompleted(context.EntityId);
            }
        }

        /// <summary>取消指定 NPC 的当前 Film，确保输入和镜头租约沿 FilmSystem 统一清理。</summary>
        internal void Cancel(int entityId)
        {
            if (activeFilm != null && activeFilm.InstanceId > 0) activeFilm.Stop();
            activeFilm = null;
        }
    }
}
