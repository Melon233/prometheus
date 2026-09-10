namespace Xuan.Prometheus.Asset
{
    /// <summary>
    /// 资源包启动过程中的阶段。
    ///
    /// 这几个阶段就是 YooAsset 的真实顺序，不是为了给进度条编造的分段：
    /// 初始化资源包 → 请求版本号 → 下载资源清单 → 下载内容 → 可加载。
    /// **热更不是这条链之外的一步，而是其中的 <see cref="DownloadingContent"/> 这一段**，
    /// 因此热更界面只能从这里拿进度，不能自己驱动一套下载流程。
    /// </summary>
    public enum AssetBootPhase
    {
        /// <summary>尚未开始。</summary>
        Idle = 0,

        /// <summary>正在初始化资源包与文件系统。</summary>
        InitializingPackage = 1,

        /// <summary>正在请求资源版本号；接入 HostPlayMode 后这一步会访问 CDN。</summary>
        RequestingVersion = 2,

        /// <summary>正在下载并加载资源清单。</summary>
        LoadingManifest = 3,

        /// <summary>正在下载需要更新的资源内容；当前播放模式下没有任何内容需要下载。</summary>
        DownloadingContent = 4,

        /// <summary>资源包已经可以加载资源。</summary>
        Ready = 5
    }

    /// <summary>
    /// 资源包启动进度的一次快照。
    ///
    /// 字节数只在 <see cref="AssetBootPhase.DownloadingContent"/> 阶段有意义；
    /// 其余阶段没有可量化的分母，<see cref="Ratio"/> 直接给出该阶段的整体完成度，
    /// 界面据此显示"正在准备"而不是一个编造的百分比。
    /// </summary>
    public readonly struct AssetBootProgress
    {
        /// <summary>创建一次启动进度快照。</summary>
        /// <param name="phase">当前阶段。</param>
        /// <param name="ratio">当前阶段的完成度，取值 0 到 1。</param>
        /// <param name="downloadedBytes">已下载字节数；仅下载阶段有意义。</param>
        /// <param name="totalBytes">需要下载的总字节数；仅下载阶段有意义。</param>
        public AssetBootProgress(AssetBootPhase phase, float ratio, long downloadedBytes = 0L, long totalBytes = 0L)
        {
            Phase = phase;
            Ratio = ratio;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }

        /// <summary>获取当前阶段。</summary>
        public AssetBootPhase Phase { get; }

        /// <summary>获取当前阶段的完成度，取值 0 到 1。</summary>
        public float Ratio { get; }

        /// <summary>获取已下载字节数；仅下载阶段有意义。</summary>
        public long DownloadedBytes { get; }

        /// <summary>获取需要下载的总字节数；仅下载阶段有意义，为零表示没有内容需要下载。</summary>
        public long TotalBytes { get; }

        /// <summary>当前是否确实有内容需要下载；据此决定要不要显示字节数与下载提示。</summary>
        public bool HasDownload => TotalBytes > 0L;

        /// <summary>把阶段映射成整条启动链路的总体完成度，供单一进度条显示。</summary>
        public float OverallRatio
        {
            get
            {
                if (Phase == AssetBootPhase.Ready) return 1f;
                if (Phase == AssetBootPhase.Idle) return 0f;
                // 五个阶段等分整条链路；阶段内部再按 Ratio 细分，使进度条不会长时间停在同一个位置。
                const float PhaseCount = 5f;
                return (((int)Phase - 1) + UnityEngine.Mathf.Clamp01(Ratio)) / PhaseCount;
            }
        }
    }
}
