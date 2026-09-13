using System;
using Luban;
using UnityEngine;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 配表读取入口：持有 Luban 生成的唯一表管理器，并负责把表名解析为资源地址、加载字节流。
    /// 读表统一写作 <c>Core.Config.Tables.Tb*</c>，不需要类型参数，也不需要各系统自行缓存。
    /// </summary>
    public interface IConfigKit : IKitContract
    {
        /// <summary>表数据是否已经加载完成；未加载时访问 <see cref="Tables"/> 是时序错误。</summary>
        bool IsLoaded { get; }

        /// <summary>获取全部配表；尚未加载时抛出，不返回空值掩盖时序错误。</summary>
        Cfg.Tables Tables { get; }

        /// <summary>
        /// 加载全部配表。必须在资源包就绪之后、玩法会话建立之前调用，且一个 Core 生命周期内只调用一次。
        /// 调用点由 <c>ARCH-CONFIG-001</c> 固定在 <c>GameFlow.RunHotUpdateAsync</c>。
        /// </summary>
        void Load();
    }

    /// <summary>
    /// 基于 AssetKit 的配表读取实现。
    /// 表数据是 Luban 导出的 <c>.bytes</c>，位于 <c>Assets/BundleResources/Table</c>，
    /// 资源地址规则为 AddressByFileName，因此 Luban 的表名就是资源地址，无需额外映射。
    /// </summary>
    internal sealed class ConfigKit : Kit, IConfigKit
    {
        /// <summary>保存本次加载构造的唯一表管理器。</summary>
        private Cfg.Tables tables;

        /// <summary>表数据是否已经加载完成。</summary>
        public bool IsLoaded => tables != null;

        /// <summary>获取全部配表。</summary>
        public Cfg.Tables Tables => tables ?? throw new InvalidOperationException("ConfigKit has not loaded tables yet. Load must run after the asset package is ready and before any table is read.");

        /// <summary>
        /// 构造表管理器。加载在构造函数内部同步完成：Luban 生成的构造函数会为每张表各调用一次加载委托，
        /// 因此本方法返回时全部表数据都已读入内存。
        /// </summary>
        public void Load()
        {
            LoadFrom(LoadTableBytes);
        }

        /// <summary>
        /// 用指定的字节流来源构造表管理器。
        /// 编辑器测试没有资源包可用，需要从 AssetDatabase 直接喂表；正式链路走 <see cref="Load"/>。
        /// </summary>
        /// <param name="loader">按表名返回该表字节流的委托。</param>
        internal void LoadFrom(Func<string, ByteBuf> loader)
        {
            if (tables != null) throw new InvalidOperationException("ConfigKit has already loaded tables.");
            tables = new Cfg.Tables(loader);
        }

        /// <summary>释放表管理器引用；表字节流的资源句柄由 AssetKit 统一归还。</summary>
        public override void Dispose()
        {
            tables = null;
        }

        /// <summary>
        /// 按表名同步加载一张表的字节流。表名即资源地址；加载失败由 AssetKit 抛出，
        /// 这里不做兜底——缺表是导表或打包错误，静默跳过只会把问题推迟到读表时才暴露。
        /// </summary>
        /// <param name="tableName">Luban 生成代码传入的表名，例如 <c>prometheus_config_tbreactionmatrix</c>。</param>
        private ByteBuf LoadTableBytes(string tableName)
        {
            TextAsset asset = Core.Asset.LoadAssetSync<TextAsset>(tableName);
            return new ByteBuf(asset.bytes);
        }
    }
}
