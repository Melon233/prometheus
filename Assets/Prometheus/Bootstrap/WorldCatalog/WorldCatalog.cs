using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 全部世界定义的目录资产。
    ///
    /// 世界之间的跳转一律按 <see cref="WorldDefinition.WorldId"/> 引用，不按场景地址——
    /// 场景地址是实现细节，换了资源不该让每个跳转点跟着改。
    ///
    /// 本类型独占一个文件：Unity 为每个文件生成一个与文件同名的 MonoScript，
    /// ScriptableObject 子类与文件名不一致时，资产主对象会在域重载后反序列化成 null。
    /// </summary>
    [CreateAssetMenu(fileName = "WorldCatalog", menuName = "Prometheus/World/World Catalog")]
    public sealed class WorldCatalog : ScriptableObject
    {
        [SerializeField] [Tooltip("本作全部世界的定义。")]
        private List<WorldDefinition> definitions = new List<WorldDefinition>();

        /// <summary>获取全部世界定义。</summary>
        public IReadOnlyList<WorldDefinition> Definitions => definitions;

        /// <summary>按标识查找世界定义；不存在时返回空。</summary>
        /// <param name="worldId">世界稳定标识。</param>
        public WorldDefinition Find(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return null;
            for (int index = 0; index < definitions.Count; index++)
            {
                if (string.Equals(definitions[index].WorldId, worldId, StringComparison.Ordinal)) return definitions[index];
            }
            return null;
        }

        /// <summary>按标识取世界定义；不存在属于配置错误，直接抛出。</summary>
        /// <param name="worldId">世界稳定标识。</param>
        public WorldDefinition Require(string worldId)
        {
            return Find(worldId) ?? throw new InvalidOperationException($"World catalog does not contain a world named '{worldId}'.");
        }

        /// <summary>
        /// 校验整个目录：标识非空且唯一、场景地址非空、有且只有一个主世界。
        /// 「只有一个主世界」是世界栈的前提——栈底必须唯一确定。
        /// </summary>
        public void Validate()
        {
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            int mainWorldCount = 0;
            for (int index = 0; index < definitions.Count; index++)
            {
                WorldDefinition definition = definitions[index];
                if (definition == null) throw new InvalidOperationException($"World catalog entry {index} is null.");
                definition.Validate();
                if (!seenIds.Add(definition.WorldId)) throw new InvalidOperationException($"World catalog contains duplicate world id '{definition.WorldId}'.");
                if (definition.Kind == WorldKind.MainWorld) mainWorldCount++;
            }
            if (mainWorldCount != 1) throw new InvalidOperationException($"World catalog must declare exactly one MainWorld; found {mainWorldCount}.");
        }

        /// <summary>取唯一的主世界定义；它是世界栈的栈底。</summary>
        public WorldDefinition RequireMainWorld()
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                if (definitions[index].Kind == WorldKind.MainWorld) return definitions[index];
            }
            throw new InvalidOperationException("World catalog does not declare a MainWorld.");
        }

        /// <summary>写入定义列表；供编辑器工具与测试使用。</summary>
        /// <param name="entries">要写入的世界定义。</param>
        public void SetDefinitions(IEnumerable<WorldDefinition> entries)
        {
            definitions = new List<WorldDefinition>(entries ?? Array.Empty<WorldDefinition>());
        }
    }
}
