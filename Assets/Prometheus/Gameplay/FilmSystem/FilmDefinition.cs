using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Xuan.Prometheus.Film
{
    /// <summary>保存一段 Timeline 演出的静态配置；运行时状态始终由 FilmInstance 独立持有。</summary>
    [CreateAssetMenu(fileName = "FilmDefinition", menuName = "Prometheus/Film/Film Definition")]
    public sealed class FilmDefinition : ScriptableObject
    {
        [SerializeField] private string filmId;
        [SerializeField] private TimelineAsset timeline;
        [SerializeField] private DirectorWrapMode wrapMode = DirectorWrapMode.None;
        [SerializeField] private DirectorUpdateMode updateMode = DirectorUpdateMode.GameTime;
        [SerializeField] private bool lockGameplayInput = true;
        [SerializeField] private FilmSkipMode skipMode;
        [SerializeField] private int priority;
        [SerializeField] private int filmCameraPriority = 200;
        [SerializeField] private List<FilmBindingDefinition> bindings = new List<FilmBindingDefinition>();

        /// <summary>获取业务侧用于查询、日志和资源索引的稳定演出标识。</summary>
        public string FilmId => filmId;

        /// <summary>获取承载纯表现内容的 Timeline 资源。</summary>
        public TimelineAsset Timeline => timeline;

        /// <summary>获取 Timeline 到达末尾后的循环策略；阶段一推荐使用 None。</summary>
        public DirectorWrapMode WrapMode => wrapMode;

        /// <summary>获取 Timeline 使用的时间更新模式。</summary>
        public DirectorUpdateMode UpdateMode => updateMode;

        /// <summary>获取播放期间是否通过 Cutscene 输入上下文屏蔽普通玩法输入。</summary>
        public bool LockGameplayInput => lockGameplayInput;

        /// <summary>获取外部请求跳过当前演出时采用的策略。</summary>
        public FilmSkipMode SkipMode => skipMode;

        /// <summary>获取演出优先级；数值越高越允许抢占当前前台演出。</summary>
        public int Priority => priority;

        /// <summary>获取演出镜头播放期间相对普通跟随镜头使用的 Cinemachine 优先级。</summary>
        public int FilmCameraPriority => filmCameraPriority;

        /// <summary>获取启动演出前需要解析的只读绑定声明。</summary>
        public IReadOnlyList<FilmBindingDefinition> Bindings => bindings;

        /// <summary>校验不会依赖场景运行状态的配置约束，使错误在启动演出前一次性暴露。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(filmId)) throw new InvalidOperationException($"FilmDefinition '{name}' requires a non-empty FilmId.");
            if (timeline == null) throw new InvalidOperationException($"FilmDefinition '{filmId}' requires a TimelineAsset.");
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < bindings.Count; index++)
            {
                FilmBindingDefinition binding = bindings[index];
                if (binding == null) throw new InvalidOperationException($"FilmDefinition '{filmId}' contains a null binding at index {index}.");
                if (string.IsNullOrWhiteSpace(binding.Key)) throw new InvalidOperationException($"FilmDefinition '{filmId}' contains an empty binding key at index {index}.");
                if (!keys.Add(binding.Key)) throw new InvalidOperationException($"FilmDefinition '{filmId}' contains duplicate binding key '{binding.Key}'.");
            }
        }
    }
}
