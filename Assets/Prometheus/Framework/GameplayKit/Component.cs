using System;
using UnityEngine;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Component
{
    public interface IComponent
    {
        public Entity Entity { get; set; }
    }

    // [AttributeUsage(AttributeTargets.Field)]
    // public class InjectDataAttribute : Attribute
    // {
    // }

    public abstract class Component : IComponent
    {
        public Entity Entity { get; set; }
    }

    public abstract class MonoComponent : MonoBehaviour, IComponent
    {
        public Entity Entity { get; set; }
    }
}