using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Attach this component to verify that SignalTimelinePlayer and SignalDispatcher are wired.
    /// </summary>
    public sealed class DebugLogSignalHandler : SignalHandler
    {
        public override bool CanHandle(Signal signal)
        {
            return signal is DebugLogSignal;
        }

        public override void Handle(Signal signal, SignalContext context)
        {
            var debugSignal = (DebugLogSignal)signal;
            Debug.Log($"[Signal Timeline] {debugSignal.Message}", context.Owner);
        }
    }
}
