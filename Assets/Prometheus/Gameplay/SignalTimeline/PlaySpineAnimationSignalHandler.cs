using Xuan.Prometheus.Component;
namespace Xuan.Prometheus
{
    public sealed class PlaySpineAnimationSignalHandler : SignalHandler
    {
        public override bool CanHandle(Signal signal)
        {
            return signal is PlaySpineAnimationSignal;
        }

        public override void Handle(Signal signal, SignalContext context)
        {
            OnSignal((PlaySpineAnimationSignal)signal, context);
        }

        private static void OnSignal(PlaySpineAnimationSignal signal, SignalContext context)
        {
            context.Target.GetComponent<SpineComponent>().animationLib.atkExecutor.Execute(0, false);
        }
    }
}
