using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class SelectorNode : Node
    {
        public override NodeStatus Execute()
        {
            foreach (var child in children)
            {
                var status = child.Execute();
                if (status != NodeStatus.Failure)
                    return status;
            }
            return NodeStatus.Failure;
        }
    }
}