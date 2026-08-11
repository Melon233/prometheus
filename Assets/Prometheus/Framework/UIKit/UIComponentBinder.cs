using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 保存一次成功代码生成时的绑定名称与组件引用；所在列表的序号就是生成到 PanelBase 中的稳定索引。
    /// </summary>
    [Serializable]
    internal sealed class UIGeneratedBindingSnapshot
    {
        [SerializeField] private string name;
        [SerializeField] private UnityEngine.Component component;

        /// <summary>
        /// 获取生成时使用的绑定名称。
        /// </summary>
        internal string Name => name;

        /// <summary>
        /// 获取生成时绑定的准确组件引用。
        /// </summary>
        internal UnityEngine.Component Component => component;

        /// <summary>
        /// 判断当前绑定的名称和组件引用是否与这份生成快照完全一致。
        /// </summary>
        /// <param name="binding">需要与生成快照比较的当前绑定。</param>
        /// <returns>名称和组件引用均一致时返回 true。</returns>
        internal bool Matches(UIComponentBinding binding)
        {
            return binding != null && string.Equals(name, binding.Name, StringComparison.Ordinal) && component == binding.Component;
        }
    }

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

        // 保存完整且独立的上次生成绑定表，使当前 Bind 即使被改名、替换引用、删除或重排后仍能与旧代码准确比较。
        [SerializeField, HideInInspector] private List<UIGeneratedBindingSnapshot> generatedBindings = new List<UIGeneratedBindingSnapshot>();

        // 版本大于零表示生成绑定表已经建立；旧 Prefab 会由编辑器从现有 PanelBase 源码自动迁移到当前版本。
        [SerializeField, HideInInspector] private int generatedBindingSnapshotVersion;

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

            if (generatedBindingSnapshotVersion > 0 && !IsBindingGeneratedAtIndex(binding, index, out int generatedIndex))
                throw new InvalidOperationException(generatedIndex >= 0 ? $"Binder '{name}' binding '{binding.Name}' moved from generated index {generatedIndex} to {index}; regenerate the panel code before opening it." : $"Binder '{name}' binding '{binding.Name}' and its component reference do not exist in the generated binding table; regenerate the panel code before opening it.");

            if (!string.Equals(binding.Name, expectedName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Binder '{name}' binding {index} is named '{binding.Name}', but generated code expects '{expectedName}'; regenerate the panel code.");

            if (binding.Component == null)
                throw new MissingReferenceException($"Binder '{name}' binding '{expectedName}' does not reference a component.");

            if (!(binding.Component is TComponent typedComponent))
                throw new InvalidCastException($"Binder '{name}' binding '{expectedName}' is '{binding.Component.GetType().FullName}', not '{typeof(TComponent).FullName}'.");

            return typedComponent;
        }

        /// <summary>
        /// 检查当前绑定的名称与引用组合是否仍处于生成时索引，并在发生移动时返回它原本的生成索引。
        /// </summary>
        /// <param name="binding">运行时正准备读取的当前绑定。</param>
        /// <param name="currentIndex">生成代码传入的当前列表索引。</param>
        /// <param name="generatedIndex">找到相同名称与引用组合时返回其生成索引，否则返回 -1。</param>
        /// <returns>绑定组合仍位于生成时索引时返回 true。</returns>
        private bool IsBindingGeneratedAtIndex(UIComponentBinding binding, int currentIndex, out int generatedIndex)
        {
            generatedIndex = -1;
            for (int index = 0; index < generatedBindings.Count; index++)
            {
                UIGeneratedBindingSnapshot generatedBinding = generatedBindings[index];
                if (generatedBinding == null || !generatedBinding.Matches(binding))
                    continue;

                generatedIndex = index;
                return index == currentIndex;
            }

            return false;
        }
    }
}
