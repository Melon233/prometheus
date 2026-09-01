using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>为仍需直接检查正式 Prefab 配置的 Editor 测试提供纯 C# ELC Component 构造缓存。</summary>
    public static class EntityComponentTestExtensions
    {
        private static readonly ConditionalWeakTable<GameObject, Dictionary<Type, IComponent>> ComponentsByObject = new ConditionalWeakTable<GameObject, Dictionary<Type, IComponent>>();

        /// <summary>从根 Binder 创建并缓存指定纯 C# Component，使测试不再依赖 GameObject.GetComponent。</summary>
        public static T GetEntityComponent<T>(this GameObject gameObject) where T : class, IComponent, new()
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            Dictionary<Type, IComponent> components = ComponentsByObject.GetOrCreateValue(gameObject);
            if (components.TryGetValue(typeof(T), out IComponent existing)) return (T)existing;
            T component = new T();
            if (component is IEntityBinderComponent binderComponent)
            {
                EntityBinder binder = gameObject.GetComponent<EntityBinder>();
                if (binder == null) throw new InvalidOperationException($"GameObject '{gameObject.name}' requires EntityBinder before constructing '{typeof(T).FullName}'.");
                binderComponent.Bind(binder);
            }
            components.Add(typeof(T), component);
            return component;
        }
    }
}
