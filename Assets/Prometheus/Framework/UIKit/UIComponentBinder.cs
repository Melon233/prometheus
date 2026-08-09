using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 表示一个可由代码生成器转成强类型字段的组件引用。
    /// 绑定名称属于生成代码契约，修改名称后必须重新生成对应 PanelBase。
    /// </summary>
    [Serializable]
    public sealed class UIComponentBinding
    {
        [SerializeField] private string name;
        [SerializeField] private UnityEngine.Component component;

        /// <summary>
        /// 获取生成字段使用的稳定绑定名称。
        /// </summary>
        public string Name => name;

        /// <summary>
        /// 获取绑定的实际 Unity 组件。
        /// </summary>
        public UnityEngine.Component Component => component;
    }

    /// <summary>
    /// UI Prefab 根节点唯一需要的业务 MonoBehaviour，集中保存任意 Component 引用。
    /// 运行时通过索引和名称双重校验读取组件，避免 Prefab 表顺序变化后静默绑定到错误对象。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIComponentBinder : MonoBehaviour
    {
        [SerializeField] private List<UIComponentBinding> bindings = new List<UIComponentBinding>();

        /// <summary>
        /// 获取只读组件绑定表，主要供编辑器代码生成器检查和生成字段。
        /// </summary>
        public IReadOnlyList<UIComponentBinding> Bindings => bindings;

        /// <summary>
        /// 获取当前绑定数量。
        /// </summary>
        public int Count => bindings.Count;

        /// <summary>
        /// 按生成时记录的索引和名称读取强类型组件，并对所有失配情况给出明确错误。
        /// </summary>
        /// <typeparam name="TComponent">生成字段期望的具体 Component 类型。</typeparam>
        /// <param name="index">绑定在序列化列表中的稳定索引。</param>
        /// <param name="expectedName">生成代码记录的绑定名称。</param>
        /// <returns>通过名称和类型校验的组件引用。</returns>
        public TComponent Get<TComponent>(int index, string expectedName) where TComponent : UnityEngine.Component
        {
            if (index < 0 || index >= bindings.Count)
                throw new IndexOutOfRangeException($"Binder '{name}' does not contain binding index {index}; regenerate the panel code after editing its component table.");

            UIComponentBinding binding = bindings[index];
            if (binding == null)
                throw new InvalidOperationException($"Binder '{name}' contains a null binding at index {index}.");

            if (!string.Equals(binding.Name, expectedName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Binder '{name}' binding {index} is named '{binding.Name}', but generated code expects '{expectedName}'; regenerate the panel code.");

            if (binding.Component == null)
                throw new MissingReferenceException($"Binder '{name}' binding '{expectedName}' does not reference a component.");

            if (!(binding.Component is TComponent typedComponent))
                throw new InvalidCastException($"Binder '{name}' binding '{expectedName}' is '{binding.Component.GetType().FullName}', not '{typeof(TComponent).FullName}'.");

            return typedComponent;
        }
    }
}
