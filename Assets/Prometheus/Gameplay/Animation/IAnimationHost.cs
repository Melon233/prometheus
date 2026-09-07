using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 动画播放会话对其宿主的最小要求。
    /// 引入该端口之后，Animation 成为一个自洽的库：它描述动画资产、播放协议与配置，
    /// 而不需要认识 SpineComponent、Entity 等任何实体模型类型，因此可以脱离 Entity 单独测试。
    /// </summary>
    public interface IAnimationHost
    {
        /// <summary>判断指定播放会话是否仍是宿主当前生效的会话；迟到的回调据此被忽略。</summary>
        /// <param name="playback">发起询问的播放会话。</param>
        bool IsPlaybackActive(AnimationPlayback playback);

        /// <summary>读取宿主当前的世界坐标，供动画事件驱动的音效定位；宿主尚未绑定表现对象时返回 false。</summary>
        /// <param name="position">成功时写入宿主世界坐标。</param>
        bool TryGetWorldPosition(out Vector3 position);

        /// <summary>通知宿主该播放会话已自然结束，由宿主原子释放优先级所有权。</summary>
        /// <param name="playback">已经结束的播放会话。</param>
        void CompletePlayback(AnimationPlayback playback);
    }
}
