using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Bootstrap
{
    /// <summary>
    /// 世界切换的编排者，并持有**世界栈**。
    ///
    /// 切换本身是三步固定顺序：退出旧世界 → 加载场景 → 进入新世界。
    /// 第一步是关键：场景以 <c>LoadSceneMode.Single</c> 加载，Unity 会直接销毁旧场景的全部
    /// GameObject，而 Entity、POI、NPC 都包着这些对象。必须抢在销毁之前让各系统主动释放。
    ///
    /// 栈的形状：**主世界永远在栈底**，副本与活动压在它之上，结束时弹回来源世界的记录坐标。
    /// 用栈而不是平铺切换，是因为「打完副本回哪去」只有一个自洽的答案——回到你进来的地方；
    /// 平铺切换的话这件事要每个世界各自记录，必然漂移。
    ///
    /// 它放在 Bootstrap，因为切换世界需要同时认识世界配置、场景、UI 与玩法会话，
    /// 而 Bootstrap 是唯一被允许认识具体实现的装配。
    /// </summary>
    public sealed class WorldSession
    {
        /// <summary>世界目录的 YooAsset 地址；由本类自己按需加载，是私有实现细节。</summary>
        private const string CatalogAddress = "WorldCatalog";

        /// <summary>返回点栈；栈顶是当前世界的来源。主世界位于栈底时该栈为空。</summary>
        private readonly Stack<WorldReturnPoint> returnPoints = new Stack<WorldReturnPoint>();

        /// <summary>已加载的世界目录；首次使用时按地址加载。</summary>
        private WorldCatalog catalog;

        /// <summary>当前所在世界的定义；尚未进入任何世界时为空。</summary>
        private WorldDefinition current;

        /// <summary>获取当前所在世界的标识；尚未进入任何世界时为空。</summary>
        public string CurrentWorldId => current?.WorldId;

        /// <summary>获取当前压栈深度；零表示身处栈底世界。</summary>
        public int Depth => returnPoints.Count;

        /// <summary>获取当前是否可以弹栈返回。</summary>
        public bool CanPop => returnPoints.Count > 0;

        /// <summary>
        /// 进入主世界。启动流程用它建立栈底，因此会清空整个返回点栈。
        /// </summary>
        public async UniTask EnterMainWorldAsync()
        {
            WorldCatalog loaded = await EnsureCatalogAsync();
            returnPoints.Clear();
            await SwitchToAsync(loaded.RequireMainWorld(), null);
        }

        /// <summary>
        /// 切换到一个与当前世界平级的世界（主世界或次级世界）。
        /// 平级切换会清空返回点栈：从副本里直接传送去另一张大世界地图之后，"回到副本入口"已经没有意义。
        /// </summary>
        /// <param name="worldId">目标世界标识。</param>
        public async UniTask EnterAsync(string worldId)
        {
            WorldDefinition definition = (await EnsureCatalogAsync()).Require(worldId);
            if (definition.PushesReturnPoint) throw new InvalidOperationException($"World '{worldId}' is a {definition.Kind}; use {nameof(PushAsync)} to enter it.");
            returnPoints.Clear();
            await SwitchToAsync(definition, null);
        }

        /// <summary>
        /// 压栈进入一个副本或活动世界：先记录当前世界与玩家所在位置，再切换过去。
        /// </summary>
        /// <param name="worldId">目标世界标识。</param>
        public async UniTask PushAsync(string worldId)
        {
            WorldDefinition definition = (await EnsureCatalogAsync()).Require(worldId);
            if (!definition.PushesReturnPoint) throw new InvalidOperationException($"World '{worldId}' is a {definition.Kind}; use {nameof(EnterAsync)} to enter it.");
            if (current == null) throw new InvalidOperationException("A world must be entered before pushing another one on top of it.");
            // 返回点必须在退出当前世界**之前**记录：退出相位一走，玩家实体就没了，位置也就读不到了。
            returnPoints.Push(new WorldReturnPoint(current.WorldId, ReadPlayerPosition(), ReadPlayerRotation()));
            await SwitchToAsync(definition, null);
        }

        /// <summary>
        /// 弹栈返回来源世界，并把玩家放回进入前的位置。
        /// </summary>
        public async UniTask PopAsync()
        {
            if (!CanPop) throw new InvalidOperationException("The world stack has no return point to pop.");
            WorldReturnPoint point = returnPoints.Pop();
            WorldDefinition definition = (await EnsureCatalogAsync()).Require(point.WorldId);
            await SwitchToAsync(definition, point);
        }

        /// <summary>退出当前世界并关闭属于它的界面；不在任何世界时是空操作。</summary>
        public void ExitCurrentWorld()
        {
            if (!Core.Gameplay.HasWorld) return;
            if (current != null && current.ShowsHud) Core.UI.ClosePanel<HudPanel>();
            Core.Gameplay.ExitWorld();
            current = null;
        }

        /// <summary>
        /// 执行一次完整切换：退出旧世界 → 加载场景 → 进入新世界 → 按档位开界面。
        /// </summary>
        /// <param name="definition">目标世界定义。</param>
        /// <param name="returnPoint">弹栈返回时的记录点；首次进入为空，改用配置的出生点。</param>
        private async UniTask SwitchToAsync(WorldDefinition definition, WorldReturnPoint? returnPoint)
        {
            ExitCurrentWorld();
            Scene scene = await Core.Asset.LoadSceneAsync(definition.SceneAddress);
            Vector3 spawnPosition = returnPoint?.Position ?? definition.SpawnPosition;
            Quaternion spawnRotation = returnPoint?.Rotation ?? definition.SpawnRotation;
            await Core.Gameplay.EnterWorldAsync(new WorldContext(definition.WorldId, definition.SceneAddress, scene, spawnPosition, spawnRotation));
            current = definition;
            // HUD 档位由世界定义决定：主世界全量 HUD，纯演出场景关掉它。
            if (definition.ShowsHud) Core.UI.OpenPanel<HudPanel>();
        }

        /// <summary>按需加载世界目录并校验；目录错误在第一次跳转时就暴露。</summary>
        private async UniTask<WorldCatalog> EnsureCatalogAsync()
        {
            if (catalog != null) return catalog;
            WorldCatalog loaded = null;
            await Core.Asset.LoadAssetAsync<WorldCatalog>(CatalogAddress, asset => loaded = asset, error => throw new InvalidOperationException(error)).ToUniTask();
            loaded.Validate();
            catalog = loaded;
            return catalog;
        }

        /// <summary>读取当前上场玩家的位置，作为返回点坐标。</summary>
        private static Vector3 ReadPlayerPosition()
        {
            return RequireActivePlayer().transform.position;
        }

        /// <summary>读取当前上场玩家的朝向，作为返回点朝向。</summary>
        private static Quaternion ReadPlayerRotation()
        {
            return RequireActivePlayer().transform.rotation;
        }

        /// <summary>取当前上场玩家的场景对象；压栈时它必然存在，缺失属于时序错误。</summary>
        private static GameObject RequireActivePlayer()
        {
            ITeamSystem teamSystem = Core.Gameplay.GetSystem<ITeamSystem>();
            GameObject player = teamSystem.ActiveMember?.bindGo;
            return player != null ? player : throw new InvalidOperationException("Cannot record a world return point without an active team member.");
        }

        /// <summary>
        /// 一条返回点记录：从某个世界的某个位置压栈出去，弹回时就回到这里。
        /// 只记录世界与位姿，不记录任何玩法进度——进度属于会话，本来就跨世界存活。
        /// </summary>
        private readonly struct WorldReturnPoint
        {
            /// <summary>创建一条返回点记录。</summary>
            /// <param name="worldId">来源世界标识。</param>
            /// <param name="position">压栈时玩家所在坐标。</param>
            /// <param name="rotation">压栈时玩家朝向。</param>
            public WorldReturnPoint(string worldId, Vector3 position, Quaternion rotation)
            {
                WorldId = worldId;
                Position = position;
                Rotation = rotation;
            }

            /// <summary>获取来源世界标识。</summary>
            public string WorldId { get; }

            /// <summary>获取压栈时玩家所在坐标。</summary>
            public Vector3 Position { get; }

            /// <summary>获取压栈时玩家朝向。</summary>
            public Quaternion Rotation { get; }
        }
    }
}
