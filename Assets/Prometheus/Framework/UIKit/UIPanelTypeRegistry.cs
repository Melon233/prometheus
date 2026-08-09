using System;
using System.Collections.Generic;
using System.Reflection;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 扫描已编译程序集中的 UIPanelConfigAttribute，将面板脚本配置缓存成运行时快速查询表。
    /// 使用类型扫描而不是读取 Assets 源码目录，使编辑器和最终 Player 使用完全一致的注册逻辑。
    /// </summary>
    internal static class UIPanelTypeRegistry
    {
        private static readonly Dictionary<Type, UIPanelDescriptor> Descriptors = new Dictionary<Type, UIPanelDescriptor>();

        /// <summary>
        /// 清空旧缓存并扫描当前 AppDomain 中所有可读取程序集的具体 UIPanel 类型。
        /// </summary>
        public static void Rebuild()
        {
            Descriptors.Clear();
            List<Type> panelTypes = new List<Type>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                foreach (Type type in GetLoadableTypes(assembly))
                {
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition || !typeof(UIPanel).IsAssignableFrom(type))
                        continue;

                    if (type.GetCustomAttribute<UIPanelConfigAttribute>(false) != null)
                        panelTypes.Add(type);
                }
            }

            panelTypes.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            foreach (Type panelType in panelTypes)
            {
                UIPanelConfigAttribute configuration = panelType.GetCustomAttribute<UIPanelConfigAttribute>(false);
                if (panelType.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException($"Configured panel '{panelType.FullName}' must declare a public parameterless constructor.");

                if (Descriptors.ContainsKey(panelType))
                    throw new InvalidOperationException($"Panel type '{panelType.FullName}' was registered more than once.");

                Descriptors.Add(panelType, new UIPanelDescriptor(panelType, configuration));
            }
        }

        /// <summary>
        /// 获取指定面板类型的缓存配置，未标记特性的类型会得到包含修复建议的异常。
        /// </summary>
        public static UIPanelDescriptor Get(Type panelType)
        {
            if (panelType == null)
                throw new ArgumentNullException(nameof(panelType));

            if (!Descriptors.TryGetValue(panelType, out UIPanelDescriptor descriptor))
                throw new InvalidOperationException($"Panel '{panelType.FullName}' is not registered. Add {nameof(UIPanelConfigAttribute)} to the concrete panel script before opening it.");

            return descriptor;
        }

        /// <summary>
        /// 清除静态类型配置，避免关闭当前 GameCore 后保留无意义的运行时状态。
        /// </summary>
        public static void Clear()
        {
            Descriptors.Clear();
        }

        /// <summary>
        /// 安全读取程序集类型；部分可选程序集加载失败时仍保留其中成功加载的类型。
        /// </summary>
        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types;
            }
        }
    }
}
