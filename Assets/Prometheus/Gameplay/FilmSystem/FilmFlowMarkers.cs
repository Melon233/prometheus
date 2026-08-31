using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace Xuan.Prometheus.Film
{
    /// <summary>在 Timeline 上按条件跳转到两个时间点之一。</summary>
    public sealed class FilmBranchMarker : Marker
    {
        [SerializeField] private string variableKey;
        [SerializeField] private string expectedValue;
        [SerializeField] private double trueTime;
        [SerializeField] private double falseTime;

        /// <summary>获取或设置用于判断的流程变量名。</summary>
        public string VariableKey { get => variableKey; set => variableKey = value; }

        /// <summary>获取或设置变量命中时采用的字符串值。</summary>
        public string ExpectedValue { get => expectedValue; set => expectedValue = value; }

        /// <summary>获取或设置条件成立时的 Timeline 时间。</summary>
        public double TrueTime { get => trueTime; set => trueTime = value; }

        /// <summary>获取或设置条件不成立时的 Timeline 时间。</summary>
        public double FalseTime { get => falseTime; set => falseTime = value; }

        /// <summary>校验分支变量和跳转时间。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(variableKey)) throw new InvalidOperationException("Film branch marker requires a variable key.");
            if (trueTime < 0d || falseTime < 0d) throw new InvalidOperationException($"Film branch marker '{variableKey}' cannot jump to a negative time.");
        }
    }

    /// <summary>在 Timeline 上等待一个由外部系统发布的流程事件。</summary>
    public sealed class FilmWaitEventMarker : Marker
    {
        [SerializeField] private string eventId;

        /// <summary>获取或设置等待的事件标识。</summary>
        public string EventId { get => eventId; set => eventId = value; }

        /// <summary>校验事件标识。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(eventId)) throw new InvalidOperationException("Film wait-event marker requires an event ID.");
        }
    }

    /// <summary>在 Timeline 上启动一段子演出，父演出会等待其完成后继续。</summary>
    public sealed class FilmSubFilmMarker : Marker
    {
        [SerializeField] private FilmDefinition definition;

        /// <summary>获取或设置需要嵌套播放的演出定义。</summary>
        public FilmDefinition Definition { get => definition; set => definition = value; }

        /// <summary>校验子演出引用。</summary>
        public void Validate()
        {
            if (definition == null) throw new InvalidOperationException("Film sub-film marker requires a FilmDefinition.");
        }
    }

    /// <summary>在 Timeline 上并行启动多段不依赖输入和镜头租约的子演出。</summary>
    public sealed class FilmParallelMarker : Marker
    {
        [SerializeField] private List<FilmDefinition> definitions = new List<FilmDefinition>();

        /// <summary>获取并行子演出定义列表。</summary>
        public IReadOnlyList<FilmDefinition> Definitions => definitions;

        /// <summary>校验并行列表中不存在空定义。</summary>
        public void Validate()
        {
            if (definitions.Count == 0) throw new InvalidOperationException("Film parallel marker requires at least one child FilmDefinition.");
            for (int index = 0; index < definitions.Count; index++) if (definitions[index] == null) throw new InvalidOperationException($"Film parallel marker contains a null child at index {index}.");
        }
    }
}
