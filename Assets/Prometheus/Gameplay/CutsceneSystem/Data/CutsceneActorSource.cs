namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 演员的来源。资产按整数下标序列化，只允许在末尾追加，并同步更新 SerializedEnumLayoutTests 的黄金名单。
    /// </summary>
    public enum CutsceneActorSource
    {
        /// <summary>当前上场角色。只支持站位、朝向与显隐，不支持自定义动画（玩家动画受 SpineComponent 优先级仲裁）。</summary>
        Player = 0,
        /// <summary>场景中已摆放的对象，按 <see cref="WorldAnchor"/> 的 id 绑定；演出结束后保持最终状态。</summary>
        Scene = 1,
        /// <summary>演出开始时由 prefab 生成、结束时销毁的临时对象。</summary>
        Spawn = 2,
    }
}
