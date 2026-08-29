using System;

namespace Xuan.Prometheus.ConfigKit
{
    /// <summary>为 ScriptableObject 配置声明配置中心中的显式分组路径；路径使用正斜杠表示层级。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ConfigCenterGroupAttribute : Attribute
    {
        /// <summary>创建配置中心分组声明。</summary>
        /// <param name="groupPath">例如“战斗/效果”的分组路径。</param>
        public ConfigCenterGroupAttribute(string groupPath) { GroupPath = groupPath; }

        /// <summary>读取配置中心使用的分组路径。</summary>
        public string GroupPath { get; }
    }

    /// <summary>为 ScriptableObject 配置声明配置中心中的中文显示名。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ConfigCenterDisplayNameAttribute : Attribute
    {
        /// <summary>创建配置中心显示名声明。</summary>
        public ConfigCenterDisplayNameAttribute(string displayName) { DisplayName = displayName; }

        /// <summary>读取配置中心使用的显示名。</summary>
        public string DisplayName { get; }
    }
}
