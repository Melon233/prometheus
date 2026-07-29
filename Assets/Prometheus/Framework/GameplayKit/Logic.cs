namespace Xuan.Prometheus.Logic
{
    public interface ILogic
    {
        int BlockCnt { get; set; }
        bool Enable { get; set; }
        OrderTag LogicGroup { get; set; }
        Entity Entity { get; set; }
        void AfterNew();
        bool CanEnable();
        bool CanDisable();
        void OnEnable();
        void OnDisable();
        void OnUpdate(float dt);
        void OnDispose();
    }

    public abstract class Logic : ILogic
    {
        public int BlockCnt { get; set; }
        public bool Enable { get; set; }
        public OrderTag LogicGroup { get; set; } = OrderTag.Gameplay;
        public Entity Entity { get; set; }
        public abstract void AfterNew();
        public abstract bool CanEnable();
        public abstract bool CanDisable();
        public abstract void OnEnable();
        public abstract void OnDisable();
        public abstract void OnUpdate(float dt);
        public abstract void OnDispose();
    }
}