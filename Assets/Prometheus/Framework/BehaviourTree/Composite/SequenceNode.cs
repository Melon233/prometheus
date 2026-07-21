using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class SequenceNode : Node
    {
        public override NodeStatus Execute()
        {
            foreach (var child in children)
            {
                var status = child.Execute();
                if (status != NodeStatus.Success)
                    return status;
            }
            return NodeStatus.Success;
        }
    }
}