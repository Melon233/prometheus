using System;
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

        /// <summary>从统一资源模块读取静态地图定义，并广播一次就绪事实供面板绑定纹理。</summary>
        public override void AfterNew()
        {
            Definition = null;
            try
            {
                Definition = Core.Asset.LoadAssetSync<WorldMapDefinition>(MapDefinitionAddress);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WorldMapSystem] 未找到地图定义资源 '{MapDefinitionAddress}'，请先使用地图拍摄工具生成；地图面板将保持空白：{exception.Message}");
            }

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
