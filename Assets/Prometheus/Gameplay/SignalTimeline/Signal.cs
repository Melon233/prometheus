using System;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Gives a Signal subtype a readable path in the Signal Timeline add menu.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SignalMenuAttribute : Attribute
    {
        public SignalMenuAttribute(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }

    /// <summary>
    /// Base data for an event emitted by <see cref="SignalTimelinePlayer"/>.
    /// Add project-specific data by inheriting from this class; Signals never execute gameplay
    /// behaviour themselves.
    /// </summary>
    [Serializable]
    public abstract class Signal
    {
        [SerializeField, Min(0f)] private float _time;

        /// <summary>Time, in seconds, at which this signal is emitted.</summary>
        public float Time
        {
            get => _time;
            set => _time = Mathf.Max(0f, value);
        }

        /// <summary>Used by the editor when drawing the signal marker.</summary>
        public virtual string DisplayName => GetType().Name;
    }
}