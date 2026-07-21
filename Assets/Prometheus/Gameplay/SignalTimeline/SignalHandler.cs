using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Non-generic MonoBehaviour base for components that receive timeline signals.
    /// Concrete handlers must live in their own same-named .cs file so Unity can attach them.
    /// </summary>
    public abstract class SignalHandler : MonoBehaviour, ISignalHandler
    {
        public abstract bool CanHandle(Signal signal);
        public abstract void Handle(Signal signal, SignalContext context);
    }
}
