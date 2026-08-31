using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Film
{
    /// <summary>保存一次演出启动时由业务侧注入的具名 Unity 对象，不参与 FilmDefinition 资源序列化。</summary>
    public sealed class FilmBindingContext
    {
        /// <summary>保存当前实例按稳定名称注入的 Unity 对象。</summary>
        private readonly Dictionary<string, UnityEngine.Object> bindings = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        /// <summary>写入或替换一个具名运行时绑定，并返回当前上下文以便连续配置。</summary>
        /// <param name="key">与 FilmDefinition 及 Timeline 输出轨道一致的绑定名。</param>
        /// <param name="target">当前演出实例实际使用的 Unity 对象。</param>
        /// <returns>当前绑定上下文。</returns>
        public FilmBindingContext Set(string key, UnityEngine.Object target)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Film binding key cannot be empty.", nameof(key));
            if (target == null) throw new ArgumentNullException(nameof(target));
            bindings[key] = target;
            return this;
        }

        /// <summary>尝试读取一个具名运行时绑定。</summary>
        /// <param name="key">需要解析的绑定名。</param>
        /// <param name="target">成功时返回对应 Unity 对象。</param>
        /// <returns>绑定存在且对象仍有效时返回 true。</returns>
        public bool TryGet(string key, out UnityEngine.Object target)
        {
            return bindings.TryGetValue(key, out target) && target != null;
        }
    }
}
