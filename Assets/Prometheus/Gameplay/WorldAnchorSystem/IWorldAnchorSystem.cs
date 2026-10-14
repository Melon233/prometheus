using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 世界锚点索引：会话建立时扫描场景中的全部 <see cref="WorldAnchor"/>，按 id 提供查询。
    /// 演出（锚点坐标系、场景演员）与任务（区域、交互点的挂载位置）都通过它定位场景内容。
    /// </summary>
    public interface IWorldAnchorSystem : ISystem
    {
        /// <summary>按 id 查找锚点；不存在时返回 false。</summary>
        /// <param name="anchorId">锚点 id。</param>
        /// <param name="anchor">找到的锚点 Transform。</param>
        bool TryGet(string anchorId, out Transform anchor);

        /// <summary>按 id 取锚点；不存在说明配置与场景不匹配，直接抛出。</summary>
        /// <param name="anchorId">锚点 id。</param>
        /// <param name="owner">报错定位描述（哪份资产在引用）。</param>
        Transform Require(string anchorId, string owner);
    }
}
