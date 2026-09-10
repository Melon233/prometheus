using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 加载并发布静态世界地图定义。
    /// 该系统只做一件事：把地图配置资产变成可查询的只读投影，并在就绪时广播一次全局事实。
    /// 地图资源缺失不是错误：世界仍可正常运行，面板显示空白地图区域。
    /// </summary>
    internal sealed class WorldMapSystem : XSystem, IWorldMapSystem
    {
        /// <summary>地图定义的 YooAsset 地址；地图拍摄工具会在 Config/Global 下生成同名资产。</summary>
        private const string MapDefinitionAddress = "WorldMapDefinition";

        /// <inheritdoc />
        public WorldMapDefinition Definition { get; private set; }

        /// <inheritdoc />
        public Texture2D Texture => Definition == null ? null : Definition.MapTexture;

        /// <inheritdoc />
        public float WorldLength => Definition == null ? 0f : Definition.WorldLength;

        /// <inheritdoc />
        public float WorldWidth => Definition == null ? 0f : Definition.WorldWidth;

        /// <inheritdoc />
        public float InitialZoom => Definition == null ? 1f : Definition.InitialZoom;

        /// <summary>
        /// 异步读取静态地图定义。
        ///
        /// 地图定义由拍摄工具离线生成，工程里可能尚未生成过——这不是时序错误而是一种正常的中间状态，
        /// 因此缺失时降级为空白地图并给出提示，而不是让整个会话装配失败。
        /// </summary>
        public override async UniTask AfterNewAsync()
        {
            WorldMapDefinition loadedDefinition = null;
            string loadError = null;
            await Core.Asset.LoadAssetAsync<WorldMapDefinition>(MapDefinitionAddress, asset => loadedDefinition = asset, error => loadError = error).ToUniTask();
            Definition = loadedDefinition;
            if (loadError != null) Debug.LogWarning($"[WorldMapSystem] 未找到地图定义资源 '{MapDefinitionAddress}'，请先使用地图拍摄工具生成；地图面板将保持空白：{loadError}");
        }

        /// <summary>广播一次就绪事实供面板绑定纹理。</summary>
        public override void AfterNew()
        {
            Core.Event.Invoke(new WorldMapReadyEvent(Definition));
        }

        /// <inheritdoc />
        public Vector2 WorldToNormalized(Vector3 worldPosition)
        {
            if (Definition == null) throw new InvalidOperationException("WorldMapSystem cannot convert coordinates before WorldMapDefinition is loaded.");
            return Definition.WorldToNormalized(worldPosition);
        }

        /// <summary>释放地图定义引用；资产句柄由 AssetKit 统一回收。</summary>
        public override void Dispose()
        {
            Definition = null;
        }
    }
}
